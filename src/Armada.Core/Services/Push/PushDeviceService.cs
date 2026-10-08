namespace Armada.Core.Services.Push
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// Registration and management of push devices with tenant and user scoping (see <see cref="PushDeviceAccess"/>).
    /// Errors are typed exceptions the routes map to status codes: <see cref="ArgumentException"/> (400),
    /// <see cref="UnauthorizedAccessException"/> (403), <see cref="KeyNotFoundException"/> (404; also used for devices the
    /// caller may not see, so their existence is not disclosed). Every returned device has its token masked.
    /// </summary>
    public class PushDeviceService
    {
        #region Public-Members

        /// <summary>
        /// Maximum length of an Expo push token.
        /// </summary>
        public const int MaxTokenLength = 256;

        /// <summary>
        /// Maximum length of a device name.
        /// </summary>
        public const int MaxDeviceNameLength = 128;

        /// <summary>
        /// Maximum length of an app version.
        /// </summary>
        public const int MaxAppVersionLength = 64;

        /// <summary>
        /// Maximum length of a locale.
        /// </summary>
        public const int MaxLocaleLength = 35;

        #endregion

        #region Private-Members

        private readonly string _Header = "[PushDeviceService] ";
        private readonly DatabaseDriver _Database;
        private readonly ArmadaSettings _Settings;
        private readonly LoggingModule _Logging;
        private static readonly Regex _TokenPattern = new Regex(@"^Expo(nent)?PushToken\[[A-Za-z0-9_\-]{4,200}\]$", RegexOptions.CultureInvariant);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Settings (Push.Categories defaults).</param>
        /// <param name="logging">Logging module.</param>
        public PushDeviceService(DatabaseDriver database, ArmadaSettings settings, LoggingModule logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether a string is a well-formed Expo push token.
        /// </summary>
        /// <param name="token">Candidate.</param>
        /// <returns>True when well formed.</returns>
        public static bool IsValidToken(string? token)
        {
            if (String.IsNullOrEmpty(token) || token!.Length > MaxTokenLength) return false;
            return _TokenPattern.IsMatch(token);
        }

        /// <summary>
        /// Register or refresh the caller's device. The token identifies the device: a token the caller already
        /// registered is refreshed (and reactivated). A token registered by another user or tenant is deleted there and
        /// registered afresh for the caller under a new device id, so the previous owner's records (and any app that
        /// stored the old id) no longer match it. Registering or reactivating a device beyond
        /// <see cref="PushSettings.MaxDevicesPerUser"/> deactivates the caller's least recently seen active devices.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="request">Registration.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The device (masked) and whether it was created.</returns>
        public async Task<PushDeviceRegistration> RegisterAsync(AuthContext caller, PushDeviceRegisterRequest request, CancellationToken token = default)
        {
            if (!PushDeviceAccess.CanRegister(caller)) throw new UnauthorizedAccessException("Push devices are registered by signed-in users.");
            if (request == null) throw new ArgumentException("A registration body is required.");
            if (!request.Platform.HasValue) throw new ArgumentException("Platform is required (Ios or Android).");
            string expoToken = (request.ExpoPushToken ?? String.Empty).Trim();
            if (!IsValidToken(expoToken)) throw new ArgumentException("ExpoPushToken must be an Expo push token (ExponentPushToken[...]).");
            string? deviceName = Optional(request.DeviceName, MaxDeviceNameLength, "DeviceName");
            string? appVersion = Optional(request.AppVersion, MaxAppVersionLength, "AppVersion");
            string? locale = Optional(request.Locale, MaxLocaleLength, "Locale");

            DateTime now = DateTime.UtcNow;
            PushDevice? existing = await _Database.PushDevices.ReadByTokenAsync(expoToken, token).ConfigureAwait(false);
            if (existing != null && !IsOwnedBy(existing, caller))
            {
                // The phone now belongs to another account (or tenant). Never move the row: a new id makes every record
                // of the old one (the previous owner's, and the app's own) stop matching pushes for the new owner.
                await _Database.PushDevices.DeleteAsync(existing.Id, token).ConfigureAwait(false);
                _Logging.Info(_Header + "token of device " + existing.Id + " changed owner from user " + existing.UserId + " to user " + caller.UserId + "; old device deleted");
                existing = null;
            }

            if (existing == null)
            {
                PushDevice device = new PushDevice();
                device.TenantId = caller.TenantId;
                device.UserId = caller.UserId;
                device.Platform = request.Platform.Value;
                device.ExpoPushToken = expoToken;
                device.DeviceName = deviceName;
                device.AppVersion = appVersion;
                device.Locale = locale;
                device.Categories = request.Categories ?? new List<PushCategoryEnum>(_Settings.Push.Categories);
                device.Active = true;
                device.CreatedUtc = now;
                device.LastSeenUtc = now;
                try
                {
                    device = await _Database.PushDevices.CreateAsync(device, token).ConfigureAwait(false);
                    _Logging.Info(_Header + "registered device " + device.Id + " for user " + caller.UserId);
                    await EnforceDeviceCapAsync(device, token).ConfigureAwait(false);
                    return new PushDeviceRegistration(device.ToMasked(), true);
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    // A concurrent registration of the same token won the insert; refresh that row when it is ours.
                    existing = await _Database.PushDevices.ReadByTokenAsync(expoToken, token).ConfigureAwait(false);
                    if (existing == null || !IsOwnedBy(existing, caller)) throw;
                }
            }

            if (request.Categories != null) existing.Categories = request.Categories;
            existing.Platform = request.Platform.Value;
            if (deviceName != null) existing.DeviceName = deviceName;
            if (appVersion != null) existing.AppVersion = appVersion;
            if (locale != null) existing.Locale = locale;
            bool wasActive = existing.Active;
            existing.Active = true;
            existing.LastSeenUtc = now;
            existing = await _Database.PushDevices.UpdateAsync(existing, token).ConfigureAwait(false);
            if (!wasActive) await EnforceDeviceCapAsync(existing, token).ConfigureAwait(false);
            return new PushDeviceRegistration(existing.ToMasked(), false);
        }

        /// <summary>
        /// List devices: the caller's own by default. A tenant admin may name a user of the tenant; a global admin may
        /// name any user (and optionally a tenant).
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="userId">User filter, or null for the caller.</param>
        /// <param name="tenantId">Tenant filter (global admins only), or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Devices (masked), oldest first.</returns>
        public async Task<List<PushDevice>> ListAsync(AuthContext caller, string? userId, string? tenantId, CancellationToken token = default)
        {
            if (!PushDeviceAccess.CanRegister(caller)) throw new UnauthorizedAccessException("Push devices are listed by signed-in users.");
            PushDeviceQuery query = new PushDeviceQuery();
            bool ownOnly = String.IsNullOrEmpty(userId) || String.Equals(userId, caller.UserId, StringComparison.Ordinal);
            if (ownOnly && String.IsNullOrEmpty(tenantId))
            {
                query.TenantId = caller.TenantId;
                query.UserId = caller.UserId;
            }
            else if (caller.IsAdmin)
            {
                query.TenantId = String.IsNullOrEmpty(tenantId) ? null : tenantId;
                query.UserId = String.IsNullOrEmpty(userId) ? null : userId;
            }
            else if (caller.IsTenantAdmin)
            {
                if (!String.IsNullOrEmpty(tenantId) && !String.Equals(tenantId, caller.TenantId, StringComparison.Ordinal))
                    throw new UnauthorizedAccessException("Tenant admins list devices of their own tenant only.");
                query.TenantId = caller.TenantId;
                query.UserId = String.IsNullOrEmpty(userId) ? null : userId;
            }
            else
            {
                throw new UnauthorizedAccessException("Only admins may list other users' devices.");
            }

            List<PushDevice> devices = await _Database.PushDevices.EnumerateAsync(query, token).ConfigureAwait(false);
            return devices.Where(d => PushDeviceAccess.CanManage(caller, d)).Select(d => d.ToMasked()).ToList();
        }

        /// <summary>
        /// Read a device the caller may manage (unmasked; for server-side use such as the test push).
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="id">Device identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The device.</returns>
        public async Task<PushDevice> ReadManagedAsync(AuthContext caller, string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new KeyNotFoundException("Push device not found");
            PushDevice? device = await _Database.PushDevices.ReadAsync(id, token).ConfigureAwait(false);
            if (device == null || !PushDeviceAccess.CanManage(caller, device)) throw new KeyNotFoundException("Push device not found");
            return device;
        }

        /// <summary>
        /// Update a device's name and categories.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="id">Device identifier.</param>
        /// <param name="request">Update.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated device (masked).</returns>
        public async Task<PushDevice> UpdateAsync(AuthContext caller, string id, PushDeviceUpdateRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentException("An update body is required.");
            PushDevice device = await ReadManagedAsync(caller, id, token).ConfigureAwait(false);
            if (request.DeviceName != null) device.DeviceName = Optional(request.DeviceName, MaxDeviceNameLength, "DeviceName");
            if (request.Categories != null) device.Categories = request.Categories;
            device = await _Database.PushDevices.UpdateAsync(device, token).ConfigureAwait(false);
            return device.ToMasked();
        }

        /// <summary>
        /// Delete a device.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="id">Device identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A task.</returns>
        public async Task DeleteAsync(AuthContext caller, string id, CancellationToken token = default)
        {
            PushDevice device = await ReadManagedAsync(caller, id, token).ConfigureAwait(false);
            await _Database.PushDevices.DeleteAsync(device.Id, token).ConfigureAwait(false);
            _Logging.Info(_Header + "deleted device " + device.Id);
        }

        #endregion

        #region Private-Methods

        private static bool IsOwnedBy(PushDevice device, AuthContext caller)
        {
            return String.Equals(device.TenantId, caller.TenantId, StringComparison.Ordinal)
                && String.Equals(device.UserId, caller.UserId, StringComparison.Ordinal);
        }

        /// <summary>
        /// Keep the owner of <paramref name="kept"/> at or under <see cref="PushSettings.MaxDevicesPerUser"/> active
        /// devices by deactivating their least recently seen other active devices.
        /// </summary>
        private async Task EnforceDeviceCapAsync(PushDevice kept, CancellationToken token)
        {
            if (String.IsNullOrEmpty(kept.UserId)) return;
            int max = _Settings.Push.MaxDevicesPerUser;
            PushDeviceQuery query = new PushDeviceQuery();
            query.TenantId = kept.TenantId;
            query.UserId = kept.UserId;
            query.ActiveOnly = true;
            List<PushDevice> others = (await _Database.PushDevices.EnumerateAsync(query, token).ConfigureAwait(false))
                .Where(d => d.Active
                    && !String.Equals(d.Id, kept.Id, StringComparison.Ordinal)
                    && String.Equals(d.TenantId, kept.TenantId, StringComparison.Ordinal)
                    && String.Equals(d.UserId, kept.UserId, StringComparison.Ordinal))
                .OrderBy(d => d.LastSeenUtc)
                .ThenBy(d => d.CreatedUtc)
                .ToList();
            int excess = others.Count + 1 - max;
            for (int i = 0; i < excess && i < others.Count; i++)
            {
                await _Database.PushDevices.SetActiveAsync(others[i].Id, false, token).ConfigureAwait(false);
                _Logging.Info(_Header + "deactivated device " + others[i].Id + " of user " + kept.UserId + ": more than " + max + " active devices");
            }
        }

        private static string? Optional(string? value, int max, string name)
        {
            if (value == null) return null;
            string trimmed = value.Trim();
            if (trimmed.Length == 0) return null;
            if (trimmed.Length > max) throw new ArgumentException(name + " must be at most " + max + " characters.");
            return trimmed;
        }

        #endregion
    }
}

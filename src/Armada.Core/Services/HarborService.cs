namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using SyslogLogging;

    /// <summary>
    /// Creates, updates, enumerates, and enables/disables registered Harbors (host runners), and applies
    /// the runtime state reported over the Harbor link (handshake registration and connection status).
    /// Operator edits (name, capacity, enabled) are kept separate from runtime state (capabilities,
    /// connection status, last-seen), which is owned by the connection manager.
    /// </summary>
    public class HarborService
    {
        #region Private-Members

        private readonly string _Header = "[HarborService] ";
        private readonly DatabaseDriver _Database;
        private readonly LoggingModule _Logging;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="logging">Logging module.</param>
        public HarborService(DatabaseDriver database, LoggingModule logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Enumerate Harbors visible to the caller (all for an admin, otherwise the caller's tenant), newest
        /// first.
        /// </summary>
        /// <param name="auth">Authentication context.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>List of Harbors.</returns>
        public async Task<List<Harbor>> EnumerateAsync(AuthContext auth, CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (auth.IsAdmin || String.IsNullOrEmpty(auth.TenantId))
                return await _Database.Harbors.EnumerateAsync(token).ConfigureAwait(false);
            return await _Database.Harbors.EnumerateAsync(auth.TenantId, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Read a single Harbor within the caller scope, or null when not found or not visible.
        /// </summary>
        /// <param name="auth">Authentication context.</param>
        /// <param name="id">Harbor identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The Harbor or null.</returns>
        public async Task<Harbor?> ReadAsync(AuthContext auth, string id, CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            Harbor? harbor = await _Database.Harbors.ReadAsync(id, token).ConfigureAwait(false);
            if (harbor == null) return null;
            if (!IsVisible(auth, harbor)) return null;
            return harbor;
        }

        /// <summary>
        /// Register a Harbor. The caller's tenant and user own the record. A name is required.
        /// </summary>
        /// <param name="auth">Authentication context.</param>
        /// <param name="harbor">Harbor to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created Harbor.</returns>
        public async Task<Harbor> CreateAsync(AuthContext auth, Harbor harbor, CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (harbor == null) throw new ArgumentNullException(nameof(harbor));
            if (String.IsNullOrWhiteSpace(harbor.Name)) throw new ArgumentException("Harbor name is required.");

            harbor.TenantId = auth.TenantId;
            harbor.UserId = auth.UserId;
            harbor.ConnectionStatus = HarborConnectionStatusEnum.Unknown;
            harbor.LastSeenUtc = null;
            harbor.LastConnectedUtc = null;
            harbor.CreatedUtc = DateTime.UtcNow;
            harbor.LastUpdateUtc = DateTime.UtcNow;

            _Logging.Info(_Header + "registering harbor " + harbor.Id + " (" + harbor.Name + ")");
            return await _Database.Harbors.CreateAsync(harbor, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Update a Harbor's operator-editable fields (name, capacity, enabled). Runtime state
        /// (capabilities, connection status, last-seen) is left untouched.
        /// </summary>
        /// <param name="auth">Authentication context.</param>
        /// <param name="harbor">Harbor carrying updated fields (its Id selects the record).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated Harbor.</returns>
        public async Task<Harbor> UpdateAsync(AuthContext auth, Harbor harbor, CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (harbor == null) throw new ArgumentNullException(nameof(harbor));

            Harbor? existing = await _Database.Harbors.ReadAsync(harbor.Id, token).ConfigureAwait(false);
            if (existing == null) throw new KeyNotFoundException("Harbor not found: " + harbor.Id);
            if (!IsVisible(auth, existing)) throw new UnauthorizedAccessException("Not permitted to modify harbor " + harbor.Id);
            if (String.IsNullOrWhiteSpace(harbor.Name)) throw new ArgumentException("Harbor name is required.");

            existing.Name = harbor.Name;
            existing.MaxConcurrentJobs = harbor.MaxConcurrentJobs;
            existing.Enabled = harbor.Enabled;
            existing.LastUpdateUtc = DateTime.UtcNow;

            _Logging.Info(_Header + "updating harbor " + existing.Id);
            return await _Database.Harbors.UpdateAsync(existing, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Enable or disable a Harbor for routing within the caller scope.
        /// </summary>
        /// <param name="auth">Authentication context.</param>
        /// <param name="id">Harbor identifier.</param>
        /// <param name="enabled">Whether the Harbor should receive new missions.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated Harbor.</returns>
        public async Task<Harbor> SetEnabledAsync(AuthContext auth, string id, bool enabled, CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            Harbor? existing = await _Database.Harbors.ReadAsync(id, token).ConfigureAwait(false);
            if (existing == null) throw new KeyNotFoundException("Harbor not found: " + id);
            if (!IsVisible(auth, existing)) throw new UnauthorizedAccessException("Not permitted to modify harbor " + id);

            existing.Enabled = enabled;
            existing.LastUpdateUtc = DateTime.UtcNow;
            return await _Database.Harbors.UpdateAsync(existing, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Delete a Harbor within the caller scope.
        /// </summary>
        /// <param name="auth">Authentication context.</param>
        /// <param name="id">Harbor identifier.</param>
        /// <param name="token">Cancellation token.</param>
        public async Task DeleteAsync(AuthContext auth, string id, CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            Harbor? existing = await _Database.Harbors.ReadAsync(id, token).ConfigureAwait(false);
            if (existing == null) throw new KeyNotFoundException("Harbor not found: " + id);
            if (!IsVisible(auth, existing)) throw new UnauthorizedAccessException("Not permitted to delete harbor " + id);

            _Logging.Info(_Header + "deleting harbor " + id);
            await _Database.Harbors.DeleteAsync(id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Register or refresh a Harbor from a handshake. Creates the record when it does not exist,
        /// otherwise updates its advertised capabilities, capacity, platform, protocol version, and marks it
        /// connected. Called by the connection manager with the tenant/user resolved from the Harbor's
        /// credential; not an operator-facing operation.
        /// </summary>
        /// <param name="harborId">Harbor identifier (hbr_ prefix).</param>
        /// <param name="tenantId">Owning tenant identifier.</param>
        /// <param name="userId">Owning user identifier.</param>
        /// <param name="name">Advertised name.</param>
        /// <param name="protocolVersion">Advertised protocol version.</param>
        /// <param name="osPlatform">Advertised OS platform.</param>
        /// <param name="architecture">Advertised architecture.</param>
        /// <param name="maxConcurrentJobs">Advertised capacity.</param>
        /// <param name="capabilities">Advertised capabilities.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The registered Harbor.</returns>
        public async Task<Harbor> UpsertFromHandshakeAsync(
            string harborId,
            string? tenantId,
            string? userId,
            string name,
            string protocolVersion,
            string? osPlatform,
            string? architecture,
            int maxConcurrentJobs,
            List<HarborCapability> capabilities,
            CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(harborId)) throw new ArgumentNullException(nameof(harborId));
            if (String.IsNullOrWhiteSpace(name)) throw new ArgumentException("Harbor name is required.");

            DateTime now = DateTime.UtcNow;
            Harbor? existing = await _Database.Harbors.ReadAsync(harborId, token).ConfigureAwait(false);
            if (existing == null)
            {
                Harbor created = new Harbor
                {
                    Id = harborId,
                    TenantId = tenantId,
                    UserId = userId,
                    Name = name,
                    ProtocolVersion = protocolVersion,
                    OsPlatform = osPlatform,
                    Architecture = architecture,
                    MaxConcurrentJobs = maxConcurrentJobs,
                    Capabilities = capabilities ?? new List<HarborCapability>(),
                    ConnectionStatus = HarborConnectionStatusEnum.Connected,
                    LastSeenUtc = now,
                    LastConnectedUtc = now,
                    CreatedUtc = now,
                    LastUpdateUtc = now
                };
                _Logging.Info(_Header + "harbor " + harborId + " connected (new registration)");
                return await _Database.Harbors.CreateAsync(created, token).ConfigureAwait(false);
            }

            existing.Name = name;
            existing.ProtocolVersion = protocolVersion;
            existing.OsPlatform = osPlatform;
            existing.Architecture = architecture;
            existing.MaxConcurrentJobs = maxConcurrentJobs;
            existing.Capabilities = capabilities ?? new List<HarborCapability>();
            existing.ConnectionStatus = HarborConnectionStatusEnum.Connected;
            existing.LastSeenUtc = now;
            existing.LastConnectedUtc = now;
            existing.LastUpdateUtc = now;
            _Logging.Info(_Header + "harbor " + harborId + " reconnected");
            return await _Database.Harbors.UpdateAsync(existing, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Record a connection-status change for a Harbor (heartbeat, degrade, or disconnect). Called by the
        /// connection manager; not an operator-facing operation. No-op when the Harbor is unknown.
        /// </summary>
        /// <param name="harborId">Harbor identifier.</param>
        /// <param name="status">New connection status.</param>
        /// <param name="seen">Whether to advance the last-seen timestamp.</param>
        /// <param name="token">Cancellation token.</param>
        public async Task MarkConnectionAsync(string harborId, HarborConnectionStatusEnum status, bool seen, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(harborId)) throw new ArgumentNullException(nameof(harborId));
            Harbor? existing = await _Database.Harbors.ReadAsync(harborId, token).ConfigureAwait(false);
            if (existing == null) return;

            existing.ConnectionStatus = status;
            if (seen) existing.LastSeenUtc = DateTime.UtcNow;
            existing.LastUpdateUtc = DateTime.UtcNow;
            await _Database.Harbors.UpdateAsync(existing, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static bool IsVisible(AuthContext auth, Harbor harbor)
        {
            if (auth.IsAdmin) return true;
            if (String.IsNullOrEmpty(auth.TenantId)) return true;
            return String.Equals(auth.TenantId, harbor.TenantId, StringComparison.Ordinal);
        }

        #endregion
    }
}

namespace Armada.Core.Services
{
    using System;
    using System.IO;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// Account and host identity for usage capture. Owns this host's stable identity and its protected
    /// account key material (both persisted so they survive restarts: key loss creates a new account
    /// identity and closes the old membership, it never merges accounts), derives the opaque account key
    /// the credential host attaches to an observation (the HMAC-SHA256 of the provider-issued account
    /// identity with the host-local key and the tenant namespace; access tokens are never hashed, so
    /// rotating one does not create a new account), and fingerprints Custom commands so two different
    /// executable identities never share results just because they return the same account key. It also
    /// resolves the credential host a captain's capture runs on: the Harbor that hosts the captain's
    /// dock when there is one, otherwise (a deployment with no Harbor links at all) the Admiral's own
    /// the Admiral's host. No host resolution is Failed/HostUnavailable, never a fallback to the
    /// Admiral's credentials.
    /// </summary>
    public class UsageAccountResolver
    {
        #region Public-Members

        /// <summary>
        /// Stable identity of this credential host (an Admiral host id, or the Harbor id on a Harbor).
        /// Snapshots, memberships and groups key on it.
        /// </summary>
        public string HostId
        {
            get => _HostId;
        }

        /// <summary>
        /// Harbor this host is, when it is one; null on the Admiral's own host.
        /// </summary>
        public string? HarborId
        {
            get => _HarborId;
        }

        #endregion

        #region Private-Members

        private string _Header = "[UsageAccountResolver] ";
        private readonly string _HostId;
        private readonly string? _HarborId;
        private readonly byte[] _Key;
        private readonly LoggingModule? _Logging;
        private static readonly char[] _GroupKeySeparators = new char[] { '|' };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate for this host, loading (or creating once) the stable host identity and the protected
        /// account key from the identity file under the data directory. Both persist through restarts so
        /// account identities are stable; a lost file means a new identity and a closed old membership.
        /// </summary>
        /// <param name="dataDirectory">Host data directory holding the identity file; when null or unusable the
        /// identity is process-local and documented as unstable.</param>
        /// <param name="logging">Logging module.</param>
        /// <param name="harborId">Harbor identifier when this host is a Harbor, or null on the Admiral.</param>
        public UsageAccountResolver(string? dataDirectory, LoggingModule logging, string? harborId = null)
        {
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _HarborId = String.IsNullOrWhiteSpace(harborId) ? null : harborId!.Trim();

            string? hostId = null;
            byte[]? key = null;
            if (!String.IsNullOrWhiteSpace(dataDirectory))
            {
                try
                {
                    UsageIdentityFile identity = UsageIdentityFile.Load(dataDirectory, _Logging);
                    hostId = identity.HostId;
                    key = Convert.FromHexString(identity.Key.Trim());
                }
                catch (Exception e)
                {
                    _Logging.Warn(_Header + "could not load the usage identity file; capture identities restart with this process: " + e.Message);
                }
            }

            _HostId = String.IsNullOrWhiteSpace(hostId)
                ? (_HarborId ?? "host-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant())
                : hostId!;
            _Key = key ?? RandomNumberGenerator.GetBytes(32);
            if (_Key.Length < 32) throw new ArgumentException("The usage identity key must be at least 32 bytes.");

            if (_HarborId != null && !String.Equals(_HostId, _HarborId, StringComparison.Ordinal))
                _Logging.Warn(_Header + "loaded host identity " + _HostId + " differs from this Harbor id " + _HarborId + "; the Harbor id keeps precedence on observations");
        }

        /// <summary>
        /// Instantiate with explicit identity material, for tests and hosts that manage their own key
        /// storage. The key never leaves the host.
        /// </summary>
        /// <param name="hostId">Stable host identity.</param>
        /// <param name="keyHex">Protected account key material as lowercase hex (at least 32 bytes as 64 characters).</param>
        /// <param name="harborId">Harbor identifier when this host is a Harbor, or null on the Admiral.</param>
        public UsageAccountResolver(string hostId, string keyHex, string? harborId = null)
        {
            if (String.IsNullOrWhiteSpace(hostId)) throw new ArgumentNullException(nameof(hostId));
            if (String.IsNullOrWhiteSpace(keyHex)) throw new ArgumentNullException(nameof(keyHex));
            _HostId = hostId.Trim();
            _HarborId = String.IsNullOrWhiteSpace(harborId) ? null : harborId!.Trim();
            _Key = Convert.FromHexString(keyHex.Trim());
            if (_Key.Length < 32) throw new ArgumentException("The account key material must be at least 32 bytes.", nameof(keyHex));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Resolve the credential host a captain's usage capture runs on, together with the probe
        /// configuration derived from the captain: the Harbor hosting the captain's current dock, or, in a
        /// deployment with no Harbor links at all, the Admiral's own host. A captain whose dock is on a
        /// Harbor is probed on that Harbor even while it is disconnected (HostUnavailable, never a
        /// fallback to the Admiral's credentials); a captain with no dock in a deployment that has any
        /// Harbor link has no resolved host either, whether or not a Harbor is connected right now: the
        /// number of live connections says nothing about which host holds the credentials.
        /// </summary>
        /// <param name="database">Database, to read the captain's current dock.</param>
        /// <param name="harbors">Harbor connections, or null when Harbor delegation is not available.</param>
        /// <param name="captain">Captain to resolve.</param>
        /// <param name="settings">Server settings (capture bounds).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The configured probe host context, or null when no credential host could be resolved
        /// (Failed/HostUnavailable).</returns>
        public async Task<UsageProbeHost?> TryResolveHostAsync(
            DatabaseDriver database,
            HarborConnectionManager? harbors,
            Captain captain,
            ArmadaSettings settings,
            CancellationToken token = default)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (captain == null) throw new ArgumentNullException(nameof(captain));
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            string tenantId = captain.TenantId ?? String.Empty;
            string? commandFingerprint = null;
            string? commandJson = null;
            if (captain.UsageCaptureMode == UsageCaptureModeEnum.Custom)
            {
                commandJson = captain.UsageCaptureCommand;
                if (String.IsNullOrWhiteSpace(commandJson))
                {
                    _Logging?.Warn(_Header + "captain " + captain.Id + " is in Custom mode without a command; no capture source");
                    return null;
                }

                commandFingerprint = CommandFingerprint(commandJson);
            }

            string hostId;
            string? harborId = null;
            Dock? dock = String.IsNullOrWhiteSpace(captain.CurrentDockId)
                ? null
                : await database.Docks.ReadAsync(captain.CurrentDockId!, token).ConfigureAwait(false);
            if (DockHostResolver.IsHarborDock(dock) && dock!.HarborId != null)
            {
                // The credential host is the Harbor that hosts the captain's work, even when it is
                // disconnected: capture reports HostUnavailable rather than borrowing the Admiral's host.
                harborId = dock.HarborId;
                hostId = harborId;
            }
            else if (dock != null)
            {
                // A dock created on the Admiral is worked on locally even in a split deployment (a Harbor
                // that shares the Admiral's filesystem), so this host holds the credentials.
                hostId = _HostId;
            }
            else if (await HasHarborLinksAsync(database, harbors, token).ConfigureAwait(false))
            {
                // A split deployment: any Harbor has ever linked, so a captain without a dock has no
                // resolved credential host. This does not depend on which Harbors are connected right now:
                // a live connection count is not credential ownership, and a disconnected Harbor is still
                // the host that holds this deployment's credentials.
                _Logging?.Warn(_Header + "captain " + captain.Id + " has no dock to resolve a credential host");
                return null;
            }
            else
            {
                // Standalone deployment: the Admiral's host holds the credentials.
                hostId = _HostId;
            }

            return new UsageProbeHost
            {
                TenantId = tenantId,
                HostId = hostId,
                HarborId = harborId,
                Runtime = captain.Runtime,
                Mode = captain.UsageCaptureMode,
                CommandJson = commandJson,
                AllowPty = settings.UsageCapture.AllowPtyFallback,
                TimeoutSeconds = settings.UsageCapture.ProbeTimeoutSeconds,
                ConfigurationRevision = ConfigurationRevision(hostId, captain.Runtime, commandFingerprint),
                DispatchedUtc = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Whether this deployment has Harbor links at all (any Harbor has ever registered), which is what
        /// makes it a split deployment. Deliberately independent of the live connection state: whether a
        /// Harbor is connected right now says nothing about which host holds the credentials, and a
        /// deployment that cannot be proved standalone never borrows the Admiral's credentials.
        /// </summary>
        /// <param name="database">Database, to look for registered Harbors.</param>
        /// <param name="harbors">Harbor connections, or null when Harbor delegation is not available on this Admiral.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when a Harbor has registered.</returns>
        private static async Task<bool> HasHarborLinksAsync(DatabaseDriver database, HarborConnectionManager? harbors, CancellationToken token)
        {
            if (harbors == null) return false;
            try
            {
                return await database.Harbors.ExistsAnyAsync(token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // A deployment that cannot be proved standalone is treated as split: the safe answer keeps
                // an unresolvable captain unreported rather than capturing it on the Admiral's credentials.
                return true;
            }
        }

        /// <summary>
        /// Derive the opaque account key for an observation: the provider-issued account (or organization)
        /// identity and active credential profile, HMAC-SHA256'd with this host's protected key and
        /// namespaced by tenant, host and runtime. Never hash an access token: rotating one must not
        /// create a new account. Null or empty input returns null: a missing identity never merges
        /// unrelated captains into an unknown group.
        /// </summary>
        /// <param name="tenantId">Owning tenant.</param>
        /// <param name="runtime">Runtime the account belongs to.</param>
        /// <param name="providerAccountId">Provider-issued account or organization identity.</param>
        /// <param name="profile">Active credential profile reference, or null.</param>
        /// <returns>The opaque account key, or null when no identity exists.</returns>
        public string? DeriveAccountKey(string tenantId, AgentRuntimeEnum runtime, string? providerAccountId, string? profile = null)
        {
            if (String.IsNullOrWhiteSpace(providerAccountId)) return null;

            string identity = (tenantId ?? String.Empty) + "|"
                + _HostId + "|"
                + runtime + "|"
                + (String.IsNullOrWhiteSpace(profile) ? String.Empty : profile!.Trim()) + "|"
                + providerAccountId!.Trim();
            using (HMACSHA256 hmac = new HMACSHA256(_Key))
            {
                byte[] digest = hmac.ComputeHash(Encoding.UTF8.GetBytes(identity));
                return Convert.ToHexString(digest).ToLowerInvariant();
            }
        }

        /// <summary>
        /// Stable fingerprint of a Custom command configuration, so commands with different executable
        /// or argument identities never share results merely because they return the same account key.
        /// Blank input returns null (Auto mode: no command discriminator).
        /// </summary>
        /// <param name="commandJson">Custom command configuration JSON.</param>
        /// <returns>The fingerprint, or null.</returns>
        public static string? CommandFingerprint(string? commandJson)
        {
            if (String.IsNullOrWhiteSpace(commandJson)) return null;
            byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(commandJson.Trim()));
            return Convert.ToHexString(digest).ToLowerInvariant();
        }

        /// <summary>
        /// The probe configuration discriminator for one capture group: tenant, credential host,
        /// runtime and (for Custom) the command fingerprint. Two captains with the same discriminator on
        /// the same host share one attempt and one snapshot.
        /// </summary>
        /// <param name="tenantId">Owning tenant.</param>
        /// <param name="hostId">Credential host identity.</param>
        /// <param name="runtime">Runtime.</param>
        /// <param name="commandFingerprint">Custom command fingerprint, or null for Auto.</param>
        /// <returns>The group key.</returns>
        public static string GroupKey(string tenantId, string hostId, AgentRuntimeEnum runtime, string? commandFingerprint)
        {
            return (tenantId ?? String.Empty).Trim()
                + "|" + (String.IsNullOrWhiteSpace(hostId) ? "-" : hostId.Trim())
                + "|" + runtime
                + "|" + (String.IsNullOrWhiteSpace(commandFingerprint) ? "-" : commandFingerprint.Trim());
        }

        /// <summary>
        /// The stable job name for one capture group's dedicated job, so jobs are visible per group and a
        /// restart can find the group's persisted lease.
        /// </summary>
        /// <param name="groupKey">Group key.</param>
        /// <returns>The job name.</returns>
        public static string JobName(string groupKey)
        {
            return "usage-capture:" + groupKey;
        }

        /// <summary>
        /// The configuration revision of one probe configuration: host, runtime and command fingerprint.
        /// A result whose revision no longer matches the group's is discarded, so a probe that finished
        /// after a configuration change is never applied to the new group.
        /// </summary>
        /// <param name="hostId">Credential host identity.</param>
        /// <param name="runtime">Runtime.</param>
        /// <param name="commandFingerprint">Custom command fingerprint, or null.</param>
        /// <returns>The revision.</returns>
        public static string ConfigurationRevision(string hostId, AgentRuntimeEnum runtime, string? commandFingerprint)
        {
            return hostId.Trim() + "|" + runtime + "|" + (String.IsNullOrWhiteSpace(commandFingerprint) ? "-" : commandFingerprint.Trim());
        }

        /// <summary>
        /// Split a group key back into its parts, for logging and job labels.
        /// </summary>
        /// <param name="groupKey">Group key.</param>
        /// <returns>The parts, or null when the key is not a group key.</returns>
        public static string[]? SplitGroupKey(string? groupKey)
        {
            if (String.IsNullOrWhiteSpace(groupKey)) return null;
            string[] parts = groupKey.Split(_GroupKeySeparators, StringSplitOptions.None);
            return parts.Length == 4 ? parts : null;
        }

        #endregion
    }
}
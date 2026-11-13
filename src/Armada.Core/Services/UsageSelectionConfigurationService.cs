namespace Armada.Core.Services
{
    using System;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>Shared REST/MCP policy application with file-before-publication and optimistic revision checks.</summary>
    public sealed class UsageSelectionConfigurationService : IDisposable
    {
        /// <summary>Whole-group environment override. File values remain intact when other groups are saved.</summary>
        public const string EnvironmentVariableName = "ARMADA_USAGE_SELECTION";
        private readonly ArmadaSettings _Settings;
        private readonly DatabaseDriver _Database;
        private readonly LoggingModule _Logging;
        private readonly SemaphoreSlim _Gate = new SemaphoreSlim(1, 1);
        private readonly bool _EnforcementAvailable;
        private readonly bool _EnvironmentOverride;
        private int _Disposed;

        /// <summary>Create a settings coordinator. Invalid environment policy rejects startup without logging its raw value.</summary>
        public UsageSelectionConfigurationService(ArmadaSettings settings, DatabaseDriver database, LoggingModule logging,
            bool enforcementAvailable = false, string? environmentPolicy = null)
        {
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _EnforcementAvailable = enforcementAvailable;
            _Settings.SetUsageSelectionOverride(null);
            string? raw = environmentPolicy ?? Environment.GetEnvironmentVariable(EnvironmentVariableName);
            if (!String.IsNullOrWhiteSpace(raw))
            {
                UsageSelectionSettings policy;
                try { policy = JsonSerializer.Deserialize<UsageSelectionSettings>(raw, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new JsonException(); policy.Validate(); }
                catch (Exception ex) when (ex is JsonException || ex is ArgumentException)
                { throw new ArgumentException("ARMADA_USAGE_SELECTION must contain a valid usage selection settings object.", nameof(environmentPolicy)); }
                _Settings.SetUsageSelectionOverride(policy.Copy());
                _EnvironmentOverride = true;
            }
        }

        /// <summary>Account for a completed startup rollback before accepting requests.</summary>
        public async Task RecordRecoveryAsync(CancellationToken token = default)
        {
            if (!_Settings.UsageSelectionRecoveryPerformed) return;
            await _Database.Events.CreateAsync(new ArmadaEvent("settings.usage-selection.recovered", "An interrupted usage selection settings write was rolled back before startup")
            {
                EntityType = "settings", EntityId = "usage-selection",
                Payload = JsonSerializer.Serialize(new { Revision = _Settings.UsageSelectionRevision, Outcome = "RolledBack" })
            }, token).ConfigureAwait(false);
        }

        /// <summary>Read a detached effective policy; no mutable live references or secrets are returned.</summary>
        public UsageSelectionConfiguration Read() => new UsageSelectionConfiguration
        {
            Settings = _Settings.EffectiveUsageSelection.Copy(), Revision = _Settings.UsageSelectionRevision,
            Capabilities = new UsageSelectionCapabilities { EnforcementAvailable = _EnforcementAvailable,
                EnvironmentOverride = _EnvironmentOverride, Source = _EnvironmentOverride ? "Environment" : "SettingsFile" }
        };

        /// <summary>Acquire the settings update boundary; all runtime settings writers use this same lease.</summary>
        public async Task<SettingsUpdateLease> AcquireUpdateAsync(CancellationToken token = default)
        {
            if (Volatile.Read(ref _Disposed) != 0) throw new ObjectDisposedException(nameof(UsageSelectionConfigurationService));
            await _Gate.WaitAsync(token).ConfigureAwait(false);
            return new SettingsUpdateLease(_Gate);
        }

        /// <summary>Validate a policy update before any settings group is mutated.</summary>
        public void ValidateUpdate(UsageSelectionSettings incoming, AuthContext caller, long? expectedRevision = null)
        {
            ArgumentNullException.ThrowIfNull(incoming); ArgumentNullException.ThrowIfNull(caller);
            if (!caller.IsAuthenticated || !caller.IsAdmin) throw new UnauthorizedAccessException("System administrator permission is required to edit usage selection.");
            incoming.Validate();
            if (_EnvironmentOverride) throw new UsageSelectionConfigurationException("EnvironmentOverride", "Usage selection is supplied by the environment and is read-only through the API.");
            if (incoming.Mode == UsageSelectionModeEnum.Enforce && !_EnforcementAvailable)
                throw new UsageSelectionConfigurationException("EnforcementUnavailable", "Atomic usage enforcement is not available in this implementation stage. Use Observe or Disabled.");
            if (expectedRevision.HasValue && expectedRevision.Value != _Settings.UsageSelectionRevision)
                throw new UsageSelectionConfigurationException("SettingsRevisionConflict", "Usage selection changed. Refresh the policy before saving again.");
        }

        /// <summary>Apply a policy after acquiring the shared update lease. Caller-owned lease prevents races with other settings groups.</summary>
        /// <remarks>Only call while holding this service's settings lease. API adapters must validate before mutating any other settings group.</remarks>
        public async Task<UsageSelectionConfiguration> ApplyUnderLeaseAsync(SettingsUpdateLease lease, UsageSelectionSettings incoming, AuthContext caller,
            long? expectedRevision = null, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(lease);
            if (!lease.BelongsTo(_Gate)) throw new ArgumentException("An active lease from this settings coordinator is required.", nameof(lease));
            ValidateUpdate(incoming, caller, expectedRevision);
            UsageSelectionSettings policy = incoming.Copy();
            long revision = checked(_Settings.UsageSelectionRevision + 1);
            CaptainSelectionPolicyRevision evidence = CaptainSelectionPolicyRevision.FromSettings(caller.TenantId ?? Constants.DefaultTenantId, policy);
            ArmadaSettings copy = ArmadaSettings.Parse(JsonSerializer.Serialize(_Settings));
            copy.UsageSelection = policy;
            copy.UsageSelectionRevision = revision;
            token.ThrowIfCancellationRequested();
            await AuditAsync("requested", caller, revision, evidence, token).ConfigureAwait(false);
            try { await Armada.Core.Hosting.UsageSelectionSettingsFileStore.SaveAsync(_Settings.EffectiveSettingsFilePath, copy.SerializeForStorage(), token).ConfigureAwait(false); }
            catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException)
            { throw new System.IO.IOException("Usage selection settings could not be persisted; the previous live policy remains active.", ex); }
            // Persistence committed: publish even if the caller disconnected while the file was written.
            _Settings.UsageSelection = policy;
            _Settings.UsageSelectionRevision = revision;
            try { await AuditAsync("applied", caller, revision, evidence, CancellationToken.None).ConfigureAwait(false); }
            catch (Exception ex) when (ex is System.Data.Common.DbException || ex is InvalidOperationException)
            { _Logging.Warn("[UsageSelection] settings saved; completion audit unavailable (revision " + revision + "). The durable requested event remains available."); }
            return Read();
        }

        /// <summary>Atomically coordinate a standalone REST/MCP group update.</summary>
        public async Task<UsageSelectionConfiguration> ApplyAsync(UsageSelectionSettings incoming, AuthContext caller,
            long? expectedRevision = null, CancellationToken token = default)
        {
            using SettingsUpdateLease lease = await AcquireUpdateAsync(token).ConfigureAwait(false);
            return await ApplyUnderLeaseAsync(lease, incoming, caller, expectedRevision, token).ConfigureAwait(false);
        }

        private async Task AuditAsync(string phase, AuthContext caller, long revision, CaptainSelectionPolicyRevision policy, CancellationToken token)
        {
            ArmadaEvent entry = new ArmadaEvent("settings.usage-selection." + phase, "Usage selection policy " + phase)
            {
                TenantId = caller.TenantId, UserId = caller.UserId, EntityType = "settings", EntityId = "usage-selection",
                Payload = JsonSerializer.Serialize(new { Revision = revision, PolicyFingerprint = policy.Fingerprint, policy.Mode, policy.Strategy })
            };
            await _Database.Events.CreateAsync(entry, token).ConfigureAwait(false);
        }

        /// <summary>Dispose the owned update coordinator after request processing has stopped.</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _Disposed, 1) == 0) _Gate.Dispose();
        }
    }
}

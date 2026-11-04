namespace Armada.Core.Services
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;

    /// <summary>Applies account-wide cooldowns only from structured provider-limit failures of an owned execution.</summary>
    public sealed class UsageSelectionCooldownService
    {
        private readonly DatabaseDriver _Database;
        private readonly ArmadaSettings _Settings;
        /// <summary>Create the owned-exit policy over the shared ledger.</summary>
        public UsageSelectionCooldownService(DatabaseDriver database, ArmadaSettings settings)
        { _Database = database ?? throw new ArgumentNullException(nameof(database)); _Settings = settings ?? throw new ArgumentNullException(nameof(settings)); }

        /// <summary>Record one classified provider limit. Unknown identity remains unknown; no process is stopped here.</summary>
        public async Task<bool> RecordAsync(Mission mission, RuntimeExitInfo exitInfo, string? executionKey, DateTime nowUtc, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(mission); ArgumentNullException.ThrowIfNull(exitInfo);
            if (nowUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("UTC observation time is required.", nameof(nowUtc));
            if (String.IsNullOrWhiteSpace(mission.TenantId) || String.IsNullOrWhiteSpace(mission.CaptainId)
                || exitInfo.ProviderError == null || exitInfo.FailureKind != RuntimeFailureKindEnum.UsageLimit
                || RuntimeFailureClassifier.Classify(exitInfo.ExitCode, exitInfo.ProviderError) != RuntimeFailureKindEnum.UsageLimit) return false;
            if (executionKey == null) { CaptainSelectionTelemetry.Cooldown("Unattributed"); return false; }
            DateTime untilUtc = ProviderResetParser.TryGetResetUtc(exitInfo.ProviderError, nowUtc, out DateTime? providerUntil) && providerUntil.HasValue
                ? providerUntil.Value : nowUtc.AddSeconds(_Settings.EffectiveUsageSelection.ProviderCooldownSeconds);
            bool recorded = await _Database.CaptainSelections.TryRecordProviderCooldownAsync(new UsageProviderCooldownRequest
            {
                TenantId = mission.TenantId, MissionId = mission.Id, CaptainId = mission.CaptainId,
                ExecutionKey = executionKey, ObservedUtc = nowUtc, UntilUtc = untilUtc
            }, token).ConfigureAwait(false);
            CaptainSelectionTelemetry.Cooldown(recorded ? "Recorded" : "Ignored");
            return recorded;
        }
    }
}

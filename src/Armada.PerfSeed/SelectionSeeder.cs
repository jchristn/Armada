namespace Armada.PerfSeed
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;

    /// <summary>
    /// Seeds captain-selection history for the performance baseline: one benchmark mission with a long history whose
    /// decisions compare every captain, and shorter histories on other finished missions. Rows go through the ledger's
    /// own aggregate insert, so policy reuse, candidates, windows and reasons match what dispatch records.
    /// </summary>
    public static class SelectionSeeder
    {
        #region Public-Members

        /// <summary>Decisions recorded on the benchmark mission.</summary>
        public const int BenchmarkHistory = 200;

        /// <summary>Candidates per decision on missions other than the benchmark mission.</summary>
        public const int OtherCandidates = 10;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Seed <paramref name="decisions"/> decisions. The first <see cref="BenchmarkHistory"/> belong to
        /// <paramref name="missions"/>[0] and compare every captain; the rest cycle through the other missions.
        /// </summary>
        /// <param name="db">Database driver.</param>
        /// <param name="tenantId">Tenant.</param>
        /// <param name="userId">Owner.</param>
        /// <param name="missions">Finished missions to attach history to.</param>
        /// <param name="captains">Captains compared by each decision.</param>
        /// <param name="decisions">Total decisions to seed.</param>
        /// <param name="random">Seeded random source.</param>
        /// <returns>The identifiers the benchmark reads.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        /// <exception cref="ArgumentException">Thrown when there are no missions or captains to seed.</exception>
        public static async Task<PerfSeedIds> SeedAsync(DatabaseDriver db, string tenantId, string userId, IReadOnlyList<Mission> missions,
            IReadOnlyList<Captain> captains, int decisions, Random random)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (missions == null) throw new ArgumentNullException(nameof(missions));
            if (captains == null) throw new ArgumentNullException(nameof(captains));
            if (random == null) throw new ArgumentNullException(nameof(random));
            if (missions.Count == 0 || captains.Count == 0) throw new ArgumentException("Selection history needs at least one mission and one captain.");

            PerfSeedIds ids = new PerfSeedIds { SelectionMissionId = missions[0].Id };
            UsageSelectionSettings settings = new UsageSelectionSettings { Strategy = CaptainSelectionStrategyEnum.MostRemainingAllowance };
            DateTime start = DateTime.UtcNow.AddDays(-20);
            string? previous = null;
            for (int i = 0; i < decisions; i++)
            {
                bool benchmark = i < BenchmarkHistory || missions.Count == 1;
                Mission mission = benchmark ? missions[0] : missions[1 + (i % (missions.Count - 1))];
                int candidateCount = benchmark ? captains.Count : Math.Min(OtherCandidates, captains.Count);
                bool waiting = i % 4 != 3;
                CaptainSelectionRecordSet records = Build(tenantId, userId, mission, captains, candidateCount, settings, start.AddMinutes(i * 5), waiting, random);
                if (benchmark) records.Decision.PreviousDecisionId = previous;
                await db.CaptainSelections.CreateDecisionAsync(records).ConfigureAwait(false);
                if (benchmark) { previous = records.Decision.Id; ids.SelectionDecisionId = records.Decision.Id; }
            }
            return ids;
        }

        #endregion

        #region Private-Methods

        private static CaptainSelectionRecordSet Build(string tenantId, string userId, Mission mission, IReadOnlyList<Captain> captains, int candidateCount,
            UsageSelectionSettings settings, DateTime evaluatedUtc, bool waiting, Random random)
        {
            CaptainSelectionRecordSet records = new CaptainSelectionRecordSet();
            records.Policy = CaptainSelectionPolicyRevision.FromSettings(tenantId, settings);
            CaptainSelectionDecision decision = records.Decision;
            int offset = random.Next(captains.Count);
            Captain chosen = captains[offset];
            decision.TenantId = tenantId; decision.UserId = userId; decision.MissionId = mission.Id; decision.VoyageId = mission.VoyageId;
            decision.Mode = UsageSelectionModeEnum.Observe; decision.PolicyRevisionId = records.Policy.Id;
            decision.Outcome = waiting ? CaptainSelectionOutcomes.Waiting : CaptainSelectionOutcomes.Admitted;
            decision.Trigger = waiting ? CaptainSelectionOutcomes.Waiting : "Dispatch";
            decision.ExecutionAttemptId = decision.Id;
            decision.SelectedCaptainId = waiting ? null : chosen.Id;
            decision.SelectedProvider = waiting ? null : "anthropic";
            decision.SelectedModel = waiting ? null : "opus";
            decision.HypotheticalCaptainId = chosen.Id; decision.HypotheticalOutcome = CaptainSelectionOutcomes.Selected;
            decision.AlgorithmVersion = UsageSelectionPolicy.AlgorithmVersion;
            decision.EvidenceFingerprint = Guid.NewGuid().ToString("N");
            decision.EvaluatedUtc = evaluatedUtc;
            decision.PrimaryReasonCode = waiting ? UsageSelectionReasonCodes.BelowReserve : UsageSelectionReasonCodes.SelectedForHeadroom;
            decision.CandidateCount = candidateCount;
            for (int rank = 0; rank < candidateCount; rank++)
            {
                Captain captain = captains[(offset + rank) % captains.Count];
                double used = Math.Round(random.NextDouble() * 100, 2);
                double available = Math.Max(0, Math.Round(100 - used - settings.ReservePercent, 2));
                UsageSelectionBandEnum band = available >= settings.ComfortableHeadroomPercent ? UsageSelectionBandEnum.Comfortable
                    : available >= settings.PerAttemptReservationPercent ? UsageSelectionBandEnum.Constrained : UsageSelectionBandEnum.Blocked;
                string reason = band == UsageSelectionBandEnum.Blocked ? UsageSelectionReasonCodes.BelowReserve : UsageSelectionReasonCodes.SufficientHeadroom;
                CaptainSelectionCandidateRecord candidate = new CaptainSelectionCandidateRecord
                {
                    TenantId = tenantId, UserId = userId, DecisionId = decision.Id, CaptainId = captain.Id, ConfiguredModel = "opus", Provider = "anthropic",
                    Rank = rank + 1, Capacity = 1, Eligible = band != UsageSelectionBandEnum.Blocked, Band = band, PrimaryReasonCode = reason, AvailablePercent = available
                };
                records.Candidates.Add(candidate);
                records.Windows.Add(new CaptainSelectionWindowRecord
                {
                    TenantId = tenantId, UserId = userId, DecisionId = decision.Id, CandidateId = candidate.Id, SnapshotId = "perf-snapshot",
                    WindowKey = "five_hour", WindowName = "five_hour", Generation = UsageBudgetIdentity.ContinuousGeneration, UsedPercent = used,
                    ReservePercent = settings.ReservePercent, Model = "opus", Applicable = true, CapturedUtc = evaluatedUtc.AddMinutes(-2)
                });
                records.Reasons.Add(new CaptainSelectionReason
                {
                    TenantId = tenantId, UserId = userId, DecisionId = decision.Id, CandidateId = candidate.Id, ReasonCode = reason,
                    RequiredPercent = settings.ReservePercent + settings.PerAttemptReservationPercent, AvailablePercent = available
                });
            }
            return records;
        }

        #endregion
    }
}

namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;

    /// <summary>Provider-neutral shared-account admission fixture with two independently claimable captains.</summary>
    public sealed class UsageEnforcementFixture : IDisposable
    {
        private readonly TestDatabase _Fixture;
        /// <summary>Isolated provider under test.</summary>
        public DatabaseDriver Database => _Fixture.Driver;
        /// <summary>Tenant owning the fixture.</summary>
        public TenantMetadata Tenant { get; private set; } = null!;
        /// <summary>Owner of both attempts.</summary>
        public UserMaster Owner { get; private set; } = null!;
        /// <summary>Independent captains sharing one verified account.</summary>
        public List<Captain> Captains { get; } = new List<Captain>();
        /// <summary>Independent pending attempts.</summary>
        public List<Mission> Missions { get; } = new List<Mission>();
        /// <summary>Authoritative source used by the fixture.</summary>
        public UsageSnapshot Snapshot { get; private set; } = null!;
        /// <summary>Policy for this fixture; defaults to Enforce/MostRemainingAllowance.</summary>
        public UsageSelectionSettings Policy { get; } = new UsageSelectionSettings { Mode = UsageSelectionModeEnum.Enforce, Strategy = CaptainSelectionStrategyEnum.MostRemainingAllowance };
        private UsageEnforcementFixture(TestDatabase fixture) { _Fixture = fixture; }

        /// <summary>Create two scoped candidates and one authoritative shared-account observation.</summary>
        public static async Task<UsageEnforcementFixture> CreateAsync(double used = 87, CancellationToken token = default, int captainCount = 2)
        {
            TestDatabase database = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
            UsageEnforcementFixture fixture = new UsageEnforcementFixture(database);
            try
            {
                fixture.Tenant = await fixture.Database.Tenants.CreateAsync(new TenantMetadata("usage-enforcement"), token).ConfigureAwait(false);
                fixture.Owner = await fixture.Database.Users.CreateAsync(new UserMaster(fixture.Tenant.Id, "enforcement@example.invalid", "fixture-password"), token).ConfigureAwait(false);
                for (int index = 0; index < captainCount; index++)
                {
                    Captain captain = await fixture.Database.Captains.CreateAsync(new Captain("enforced-" + index) { TenantId = fixture.Tenant.Id, UserId = fixture.Owner.Id,
                        Model = "opus", Runtime = AgentRuntimeEnum.ClaudeCode, MaxConcurrency = 2, UsageCaptureEnabled = true }, token).ConfigureAwait(false);
                    fixture.Captains.Add(captain);
                    fixture.Missions.Add(await fixture.Database.Missions.CreateAsync(new Mission("enforced-" + index) { TenantId = fixture.Tenant.Id, UserId = fixture.Owner.Id }, token).ConfigureAwait(false));
                    await fixture.Database.UsageMemberships.CreateAsync(new UsageAccountMembership { TenantId = fixture.Tenant.Id, CaptainId = captain.Id, HostId = "host", Runtime = captain.Runtime,
                        Provider = "anthropic", AccountKey = "opaque-account", ConfigurationRevision = UsageAccountResolver.ConfigurationRevision("host", captain.Runtime, null) }, token).ConfigureAwait(false);
                }
                await fixture.CaptureAsync(used, token).ConfigureAwait(false);
                return fixture;
            }
            catch { fixture.Dispose(); throw; }
        }

        /// <summary>Commit a newer immutable observation and latest-state pointer for the shared account.</summary>
        public async Task CaptureAsync(double used, CancellationToken token = default, DateTime? capturedUtc = null, bool activeProbe = true)
        {
            DateTime captured = capturedUtc ?? DateTime.UtcNow;
            Snapshot = new UsageSnapshot { TenantId = Tenant.Id, HostId = "host", AccountKey = "opaque-account", Runtime = AgentRuntimeEnum.ClaudeCode,
                Provider = "anthropic", Source = UsageCaptureSourceEnum.OAuthEndpoint, Status = UsageSnapshotStatusEnum.Ok, ProbeStartedUtc = activeProbe ? captured.AddMilliseconds(-10) : null, CapturedUtc = captured, ReceivedUtc = captured,
                WindowsJson = UsageWindowsJson.Serialize(new List<UsageWindow> { new UsageWindow { Name = "five-hour", UsedPercent = used } }) };
            await Database.UsageSnapshots.CreateAsync(Snapshot, Captains.Select(c => c.Id).ToList(), token).ConfigureAwait(false);
            string revision = UsageAccountResolver.ConfigurationRevision("host", AgentRuntimeEnum.ClaudeCode, null);
            await Database.UsageSnapshots.UpsertLatestStateAsync(new UsageLatestState { TenantId = Tenant.Id, HostId = "host", AccountKey = "opaque-account", Runtime = AgentRuntimeEnum.ClaudeCode,
                Provider = "anthropic", LastGoodSnapshotId = Snapshot.Id, LastGoodCapturedUtc = Snapshot.CapturedUtc, LastGoodReceivedUtc = Snapshot.ReceivedUtc, LastGoodStatus = Snapshot.Status,
                LastGoodWindowsJson = Snapshot.WindowsJson, CaptureContextKey = UsageAccountResolver.CaptureContextKey(UsageAccountResolver.GroupKey(Tenant.Id, "host", AgentRuntimeEnum.ClaudeCode, null), revision) }, token).ConfigureAwait(false);
        }

        /// <summary>Build a detached proposed decision from current public source/budget reads and the pure evaluator.</summary>
        public async Task<CaptainSelectionRecordSet> RecordsAsync(int index, bool captureEnabled = true, CancellationToken token = default)
        {
            Captain captain = (await Database.Captains.ReadAsync(Captains[index].Id, token).ConfigureAwait(false))!;
            Mission mission = (await Database.Missions.ReadAsync(Missions[index].Id, token).ConfigureAwait(false))!;
            UsageSelectionAccountEvidence source = (await Database.CaptainSelections.ReadAccountEvidenceAsync(Tenant.Id, new[] { captain.Id }, token).ConfigureAwait(false)).Single();
            List<UsageSelectionBudgetTotals> totals = await Database.CaptainSelections.ReadBudgetTotalsAsync(Tenant.Id, new[] { captain.Id }, token).ConfigureAwait(false);
            UsageWindow window = UsageWindowsJson.Deserialize(source.WindowsJson).Single();
            string key = UsageBudgetIdentity.WindowKey(window);
            UsageSelectionCandidate input = new UsageSelectionCandidate { CaptainId = captain.Id, Model = captain.Model, Capacity = captain.MaxConcurrency, OccupiedSlots = captain.ActiveMissionCount,
                CooldownUntilUtc = source.CooldownUntilUtc, IdentityVerified = captureEnabled, ConfigurationMatches = true, Provider = "anthropic", CapturedUtc = source.CapturedUtc, Source = source.Source, Status = source.Status,
                HasUncertainDebt = totals.Any(t => t.HasUncertainDebt), EffectiveCaptureIntervalSeconds = source.EffectiveIntervalSeconds,
                Windows = new List<UsageSelectionWindowEvidence> { new UsageSelectionWindowEvidence { Window = window, ScopeApplies = true, GenerationConfirmed = true,
                    ActiveReservationPercent = totals.Where(t => t.WindowKey == key).Sum(t => t.ActivePercent), UnsettledPercent = totals.Where(t => t.WindowKey == key).Sum(t => t.UnsettledPercent) } } };
            UsageSelectionRecovery? recovery = await Database.CaptainSelections.ReadRecoveryAsync(Tenant.Id, mission.Id, token).ConfigureAwait(false);
            UsageSelectionCandidateResult result = UsageSelectionPolicy.Evaluate(new[] { input }, Policy, DateTime.UtcNow, recovery?.ReserveException == true).Candidates.Single();
            CaptainSelectionRecordSet records = new CaptainSelectionRecordSet { Policy = CaptainSelectionPolicyRevision.FromSettings(Tenant.Id, Policy),
                AdmissionRecoveryRevision = recovery?.Revision ?? 0, AdmissionMissionVersionUtc = mission.LastUpdateUtc, AdmissionCaptainVersionUtc = captain.LastUpdateUtc, AdmissionCaptureEnabled = captureEnabled, AdmissionHostId = "host", AdmissionCaptainModel = captain.Model };
            CaptainSelectionDecision decision = records.Decision;
            decision.TenantId = Tenant.Id; decision.UserId = Owner.Id; decision.MissionId = mission.Id; decision.ExecutionAttemptId = decision.Id;
            decision.PolicyRevisionId = records.Policy.Id; decision.Mode = Policy.Mode; decision.Outcome = "Admitted"; decision.SelectedCaptainId = captain.Id;
            decision.SelectedModel = captain.Model; decision.Trigger = "Dispatch"; decision.EvaluatedUtc = DateTime.UtcNow; decision.CandidateCount = 1;
            decision.AlgorithmVersion = UsageSelectionPolicy.AlgorithmVersion; decision.PrimaryReasonCode = result.ReasonCode;
            CaptainSelectionCandidateRecord candidate = new CaptainSelectionCandidateRecord { TenantId = Tenant.Id, UserId = Owner.Id, DecisionId = decision.Id,
                CaptainId = captain.Id, ConfiguredModel = captain.Model, Rank = 1, Eligible = true, Band = result.Band, AvailablePercent = UsageSelectionNumbers.Headroom(result.AvailablePercent),
                NextReconsiderUtc = result.NextReconsiderUtc, PrimaryReasonCode = result.ReasonCode, MembershipId = source.MembershipId, HostId = source.HostId, Runtime = source.Runtime.ToString(), AccountKey = source.AccountKey, EvidenceCapturedUtc = source.CapturedUtc };
            if (recovery != null) records.Events.Add(new CaptainSelectionExecutionEvent { TenantId = Tenant.Id, UserId = Owner.Id, DecisionId = decision.Id,
                ExecutionAttemptId = decision.ExecutionAttemptId, EventKind = "RecoveryAuthorizationApplied", ReportingSource = recovery.Id, OccurredUtc = decision.EvaluatedUtc });
            records.Candidates.Add(candidate);
            records.Windows.Add(new CaptainSelectionWindowRecord { TenantId = Tenant.Id, UserId = Owner.Id, DecisionId = decision.Id, CandidateId = candidate.Id,
                WindowKey = key, SnapshotId = source.SnapshotId, CapturedUtc = source.CapturedUtc, UsedPercent = window.UsedPercent, Applicable = true,
                ActiveReservationPercent = input.Windows[0].ActiveReservationPercent, UnsettledPercent = input.Windows[0].UnsettledPercent });
            if (captureEnabled) records.Reservations.Add(new UsageBudgetReservation { TenantId = Tenant.Id, UserId = Owner.Id, DecisionId = decision.Id,
                MissionId = mission.Id, ExecutionAttemptId = decision.ExecutionAttemptId, CaptainId = captain.Id, MembershipId = source.MembershipId,
                HostId = source.HostId, Runtime = source.Runtime.ToString(), AccountKey = source.AccountKey, WindowKey = key, Generation = UsageBudgetIdentity.ContinuousGeneration,
                ReservedPercent = Policy.PerAttemptReservationPercent });
            return records;
        }

        /// <summary>Read the fixture-owned execution identity for a launch or cleanup operation.</summary>
        public async Task<string> ExecutionKeyAsync(int index, CancellationToken token = default)
            => await Database.CaptainSelections.ReadUsageExecutionKeyAsync(Tenant.Id, Missions[index].Id, token).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The fixture has not admitted this execution.");

        /// <summary>Dispose only the owned database fixture.</summary>
        public void Dispose() { _Fixture.Dispose(); }
    }
}

namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>Deterministic headroom ranking, unknown evidence, safety fences and settings boundaries.</summary>
    public sealed class UsageSelectionSuite : IArmadaTestSuite
    {
        private const string _SuiteId = "Services.UsageSelection";
        private static readonly DateTime _Now = new DateTime(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);

        /// <summary>Build portable descriptors for every standard runner.</summary>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            void Add(string id, Action body) => cases.Add(new TestCaseDescriptor(_SuiteId, id, id.Replace('_', ' '),
                (CancellationToken token) => { token.ThrowIfCancellationRequested(); body(); return Task.CompletedTask; }));
            Add("safe_defaults_and_json_roundtrip", () =>
            {
                UsageSelectionSettings settings = JsonSerializer.Deserialize<UsageSelectionSettings>("{}")!;
                settings.Validate();
                AssertEqual(UsageSelectionModeEnum.Observe, settings.Mode);
                AssertEqual(UsageUnknownBehaviorEnum.AllowDeprioritized, settings.UnknownUsageBehavior);
                AssertEqual(10.0, settings.ReservePercent);
                AssertEqual(2.0, settings.PerAttemptReservationPercent);
                AssertEqual(30.0, settings.ComfortableHeadroomPercent);
                AssertEqual(90, settings.DecisionRetentionDays);
                UsageSelectionSettings copy = JsonSerializer.Deserialize<UsageSelectionSettings>(JsonSerializer.Serialize(settings))!;
                copy.Validate(); AssertEqual(settings.Mode, copy.Mode); AssertEqual(settings.MaximumCandidates, copy.MaximumCandidates);
            });
            Add("strategies_are_explicit_and_do_not_bypass_gates", () =>
            {
                UsageSelectionSettings settings = new UsageSelectionSettings();
                AssertEqual(CaptainSelectionStrategyEnum.FirstAvailable, settings.Strategy);
                UsageSelectionCandidate a = Fresh("a", 80); a.FirstAvailableRank = 0;
                UsageSelectionCandidate b = Fresh("b", 10); b.FirstAvailableRank = 1;
                AssertEqual("a", UsageSelectionPolicy.Evaluate(new[] { b, a }, settings, _Now).SelectedCaptainId);
                settings.Strategy = CaptainSelectionStrategyEnum.MostRemainingAllowance;
                AssertEqual("b", UsageSelectionPolicy.Evaluate(new[] { a, b }, settings, _Now).SelectedCaptainId);
                settings.Strategy = CaptainSelectionStrategyEnum.MostUsedAllowance;
                AssertEqual("a", UsageSelectionPolicy.Evaluate(new[] { b, a }, settings, _Now).SelectedCaptainId);
                a.Windows[0].Window.UsedPercent = 99;
                AssertEqual("b", UsageSelectionPolicy.Evaluate(new[] { a, b }, settings, _Now).SelectedCaptainId);
                settings.Strategy = CaptainSelectionStrategyEnum.FirstAvailable;
                AssertEqual("b", UsageSelectionPolicy.Evaluate(new[] { a, b }, settings, _Now).SelectedCaptainId);
                settings.Strategy = (CaptainSelectionStrategyEnum)99;
                AssertThrows<ArgumentOutOfRangeException>(() => settings.Validate());
            });
            Add("settings_file_defaults_and_strategy_roundtrip", () =>
            {
                ArmadaSettings defaults = ArmadaSettings.Parse("{}");
                AssertEqual(CaptainSelectionStrategyEnum.FirstAvailable, defaults.UsageSelection.Strategy);
                AssertEqual(UsageSelectionModeEnum.Observe, defaults.UsageSelection.Mode);
                ArmadaSettings configured = ArmadaSettings.Parse("{\"UsageSelection\":{\"Strategy\":\"MostUsedAllowance\",\"ReservePercent\":15}}");
                AssertEqual(CaptainSelectionStrategyEnum.MostUsedAllowance, configured.UsageSelection.Strategy);
                AssertEqual(15.0, configured.UsageSelection.ReservePercent);
                AssertEqual(CaptainSelectionStrategyEnum.MostUsedAllowance,
                    ArmadaSettings.Parse(JsonSerializer.Serialize(configured)).UsageSelection.Strategy);
                AssertThrows<ArgumentException>(() => ArmadaSettings.Parse("{\"UsageSelection\":{\"ReservePercent\":90}}"));
                AssertThrows<JsonException>(() => ArmadaSettings.Parse("{\"UsageSelection\":{\"Strategy\":\"RandomUnknown\"}}"));
            });
            Add("settings_clamp_reject_nonfinite_and_crossfield_errors", () =>
            {
                UsageSelectionSettings settings = new UsageSelectionSettings { ReservePercent = -1, PerAttemptReservationPercent = 99,
                    MaximumFreshnessSeconds = 1, UnknownRetrySeconds = 9999, DecisionRetentionDays = 0, MaximumCandidates = 999999 };
                AssertEqual(0.0, settings.ReservePercent); AssertEqual(25.0, settings.PerAttemptReservationPercent);
                AssertEqual(120, settings.MaximumFreshnessSeconds); AssertEqual(3600, settings.UnknownRetrySeconds);
                AssertEqual(7, settings.DecisionRetentionDays); AssertEqual(10000, settings.MaximumCandidates);
                AssertThrows<ArgumentOutOfRangeException>(() => settings.ReservePercent = Double.NaN);
                AssertThrows<ArgumentOutOfRangeException>(() => settings.PerAttemptReservationPercent = Double.PositiveInfinity);
                settings.ReservePercent = 40; AssertThrows<ArgumentException>(() => settings.Validate());
                settings.ReservePercent = 10; settings.Mode = (UsageSelectionModeEnum)99;
                AssertThrows<ArgumentOutOfRangeException>(() => settings.Validate());
            });
            Add("example_shared_constrained_vs_healthy_accounts", () =>
            {
                UsageSelectionCandidate a1 = Fresh("a1", 89); a1.Windows[0].ActiveReservationPercent = 2;
                UsageSelectionCandidate a2 = Fresh("a2", 89); a2.Windows[0].ActiveReservationPercent = 2;
                UsageSelectionCandidate b = Fresh("b", 40); b.Provider = "openai"; b.Windows[0].ActiveReservationPercent = 2;
                UsageSelectionEvaluation result = Evaluate(a1, a2, b);
                AssertEqual("b", result.SelectedCaptainId);
                AssertEqual("BelowReserve", result.Candidates.Single(r => r.Candidate.CaptainId == "a1").ReasonCode);
                AssertEqual(9.0, result.Candidates.Single(r => r.Candidate.CaptainId == "a2").AvailablePercent!.Value);
            });
            Add("reserve_equality_allows_launch_below_equality_waits", () =>
            {
                AssertEqual("equal", Evaluate(Fresh("equal", 88)).SelectedCaptainId);
                AssertNull(Evaluate(Fresh("below", 88.001)).SelectedCaptainId);
            });
            Add("routing_bucket_precedes_headroom_band", () =>
            {
                UsageSelectionCandidate preferred = Fresh("preferred", 80);
                UsageSelectionCandidate alternate = Fresh("alternate", 10); alternate.RoutingBucket = 1;
                AssertEqual("preferred", Evaluate(alternate, preferred).SelectedCaptainId);
            });
            Add("comfort_band_precedes_load_within_routing_bucket", () =>
            {
                UsageSelectionCandidate comfortable = Fresh("comfortable", 10); comfortable.OccupiedSlots = 2; comfortable.Capacity = 4;
                AssertEqual("comfortable", Evaluate(Fresh("constrained", 80), comfortable).SelectedCaptainId);
            });
            Add("remaining_strategy_prefers_comparable_headroom_before_load", () =>
            {
                UsageSelectionCandidate a = Fresh("a", 10); a.OccupiedSlots = 1; a.Capacity = 2;
                UsageSelectionCandidate b = Fresh("b", 30); b.OccupiedSlots = 1; b.Capacity = 4;
                AssertEqual("a", Evaluate(a, b).SelectedCaptainId);
            });
            Add("equal_load_comparable_windows_prefer_headroom", () => AssertEqual("b", Evaluate(Fresh("a", 30), Fresh("b", 10)).SelectedCaptainId));
            Add("incomparable_providers_use_assignment_age_and_id", () =>
            {
                UsageSelectionCandidate a = Fresh("a", 30); a.Provider = "anthropic";
                UsageSelectionCandidate b = Fresh("b", 10); b.Provider = "openai";
                AssertEqual("a", Evaluate(b, a).SelectedCaptainId);
                b.LastAssignedUtc = _Now.AddDays(-1); AssertEqual("b", Evaluate(a, b).SelectedCaptainId);
                a.LastAssignedUtc = null; AssertEqual("b", Evaluate(a, b).SelectedCaptainId);
            });
            Add("ranking_is_permutation_invariant_with_three_providers", () =>
            {
                UsageSelectionCandidate a = Fresh("a", 50); UsageSelectionCandidate b = Fresh("b", 10);
                UsageSelectionCandidate c = Fresh("c", 30); c.Provider = "other";
                string[] expected = Evaluate(a, b, c).Candidates.Select(r => r.Candidate.CaptainId).ToArray();
                foreach (UsageSelectionCandidate[] permutation in new[] { new[] { c, a, b }, new[] { b, c, a }, new[] { a, c, b } })
                    AssertEqual(String.Join(",", expected), String.Join(",", Evaluate(permutation).Candidates.Select(r => r.Candidate.CaptainId)));
            });
            Add("quota_hold_and_cooldown_survive_unknown_evidence", () =>
            {
                UsageSelectionCandidate a = Fresh("a", 10); a.IdentityVerified = false; a.HasAuthoritativeQuotaHold = true;
                AssertEqual("AccountQuotaExhausted", Evaluate(a).Candidates[0].ReasonCode);
                a.HasAuthoritativeQuotaHold = false; a.CooldownUntilUtc = _Now.AddMinutes(1);
                AssertEqual("AccountCooldown", Evaluate(a).Candidates[0].ReasonCode);
                AssertNull(Evaluate(a).SelectedCaptainId);
            });
            Add("existing_eligibility_and_capacity_fences_are_not_bypassed", () =>
            {
                UsageSelectionCandidate a = Fresh("a", 0); a.IneligibilityReason = "WorkerEnvelopeIncompatible";
                AssertNull(Evaluate(a).SelectedCaptainId);
                a.IneligibilityReason = null; a.OccupiedSlots = 1; AssertNull(Evaluate(a).SelectedCaptainId);
                a.OccupiedSlots = 0; a.Capacity = 0; AssertNull(Evaluate(a).SelectedCaptainId);
            });
            Add("unknown_can_allow_deprioritized_or_wait", () =>
            {
                UsageSelectionCandidate a = Fresh("a", 0); a.IdentityVerified = false;
                AssertEqual("a", Evaluate(a).SelectedCaptainId);
                AssertEqual("b", Evaluate(a, Fresh("b", 80)).SelectedCaptainId);
                UsageSelectionSettings settings = new UsageSelectionSettings { UnknownUsageBehavior = UsageUnknownBehaviorEnum.Wait };
                AssertNull(UsageSelectionPolicy.Evaluate(new[] { a }, settings, _Now).SelectedCaptainId);
            });
            Add("freshness_respects_existing_horizon_and_new_cap", () =>
            {
                UsageSelectionCandidate a = Fresh("a", 0); a.CapturedUtc = _Now.AddSeconds(-121);
                AssertEqual("UsageStale", Evaluate(a).Candidates[0].ReasonCode);
                a.EffectiveCaptureIntervalSeconds = 1000; a.CapturedUtc = _Now.AddSeconds(-901);
                AssertEqual("UsageStale", Evaluate(a).Candidates[0].ReasonCode);
                a.CapturedUtc = _Now.AddSeconds(-900); AssertEqual(UsageSelectionBandEnum.Comfortable, Evaluate(a).Candidates[0].Band);
            });
            Add("failed_derived_future_and_changed_evidence_are_unknown", () =>
            {
                UsageSelectionCandidate a = Fresh("a", 0); a.Status = UsageSnapshotStatusEnum.Failed;
                AssertEqual("UsageNotAuthoritative", Evaluate(a).Candidates[0].ReasonCode);
                a.Status = UsageSnapshotStatusEnum.Ok; a.Source = UsageCaptureSourceEnum.Derived;
                AssertEqual("UsageNotAuthoritative", Evaluate(a).Candidates[0].ReasonCode);
                a.Source = UsageCaptureSourceEnum.Headers; a.CapturedUtc = _Now.AddMinutes(6);
                AssertEqual("FutureObservation", Evaluate(a).Candidates[0].ReasonCode);
                a.CapturedUtc = _Now; a.ConfigurationMatches = false;
                AssertEqual("UsageConfigurationChanged", Evaluate(a).Candidates[0].ReasonCode);
            });
            Add("tightest_window_includes_active_and_unsettled_estimates", () =>
            {
                UsageSelectionCandidate a = Fresh("a", 10);
                a.Windows.Add(new UsageSelectionWindowEvidence { Window = new UsageWindow { Name = "weekly", UsedPercent = 80 },
                    ActiveReservationPercent = 3, UnsettledPercent = 6 });
                AssertEqual("BelowReserve", Evaluate(a).Candidates[0].ReasonCode);
                AssertEqual(11.0, Evaluate(a).Candidates[0].AvailablePercent!.Value);
            });
            Add("unknown_window_does_not_hide_proven_exhaustion", () =>
            {
                UsageSelectionCandidate a = Fresh("a", 100); a.Windows.Add(new UsageSelectionWindowEvidence());
                AssertNull(Evaluate(a).SelectedCaptainId);
                AssertEqual("AccountQuotaExhausted", Evaluate(a).Candidates[0].ReasonCode);
            });
            Add("invalid_estimate_cannot_hide_proven_exhausted_window", () =>
            {
                UsageSelectionCandidate a = Fresh("a", 100); a.Windows[0].UnsettledPercent = Double.NaN;
                AssertEqual("AccountQuotaExhausted", Evaluate(a).Candidates[0].ReasonCode);
                AssertNull(Evaluate(a).SelectedCaptainId);
            });
            Add("soft_reserve_exception_cannot_bypass_hard_quota", () =>
            {
                UsageSelectionCandidate a = Fresh("a", 95);
                AssertEqual("a", UsageSelectionPolicy.Evaluate(new[] { a }, new UsageSelectionSettings { Strategy = CaptainSelectionStrategyEnum.MostRemainingAllowance }, _Now, true).SelectedCaptainId);
                a.Windows[0].Window.UsedPercent = 100;
                AssertNull(UsageSelectionPolicy.Evaluate(new[] { a }, new UsageSelectionSettings { Strategy = CaptainSelectionStrategyEnum.MostRemainingAllowance }, _Now, true).SelectedCaptainId);
                a.Windows[0].Window.UsedPercent = 99;
                AssertNull(UsageSelectionPolicy.Evaluate(new[] { a }, new UsageSelectionSettings { Strategy = CaptainSelectionStrategyEnum.MostRemainingAllowance }, _Now, true).SelectedCaptainId);
            });
            Add("model_and_scope_applicability_are_conservative", () =>
            {
                UsageSelectionCandidate a = Fresh("a", 100); a.Windows[0].Window.Model = "opus"; a.Model = "sonnet";
                AssertEqual("NoApplicableWindows", Evaluate(a).Candidates[0].ReasonCode);
                a.Model = null; AssertEqual("UsageModelUnknown", Evaluate(a).Candidates[0].ReasonCode);
                a.Windows[0].Window.Model = null; a.Windows[0].Window.Scope = "project"; a.Windows[0].ScopeApplies = false;
                AssertEqual("UsageScopeUnknown", Evaluate(a).Candidates[0].ReasonCode);
            });
            Add("passed_reset_requires_fresh_capture_not_invented_zero", () =>
            {
                UsageSelectionCandidate a = Fresh("a", 100); a.Windows[0].Window.ResetsUtc = _Now;
                AssertEqual("AwaitingFreshUsage", Evaluate(a).Candidates[0].ReasonCode);
                a.Windows[0].GenerationConfirmed = false; AssertEqual("AwaitingFreshUsage", Evaluate(a).Candidates[0].ReasonCode);
            });
            Add("multiple_blocking_windows_report_latest_known_reset", () =>
            {
                UsageSelectionCandidate a = Fresh("a", 100); a.Windows[0].Window.ResetsUtc = _Now.AddHours(1);
                a.Windows.Add(new UsageSelectionWindowEvidence { Window = new UsageWindow { Name = "weekly", UsedPercent = 100, ResetsUtc = _Now.AddHours(5) } });
                AssertEqual(_Now.AddHours(5), Evaluate(a).Candidates[0].NextReconsiderUtc!.Value);
                a.Windows[1].Window.ResetsUtc = null; AssertNull(Evaluate(a).Candidates[0].NextReconsiderUtc);
            });
            Add("count_windows_need_valid_unit_and_limit", () =>
            {
                UsageSelectionCandidate a = Fresh("a", 10); UsageWindow w = a.Windows[0].Window;
                w.UsedPercent = null; w.Used = 40; w.Limit = 100; w.Unit = "requests";
                AssertEqual(60.0, Evaluate(a).Candidates[0].AvailablePercent!.Value);
                w.Unit = null; AssertEqual("UsageEvidenceInvalid", Evaluate(a).Candidates[0].ReasonCode);
                w.Unit = "requests"; w.Limit = 0; AssertEqual("UsageEvidenceInvalid", Evaluate(a).Candidates[0].ReasonCode);
            });
            Add("malformed_numbers_and_uncertain_debt_never_authorize_known_headroom", () =>
            {
                UsageSelectionCandidate a = Fresh("a", Double.NaN); AssertEqual("UsageEvidenceInvalid", Evaluate(a).Candidates[0].ReasonCode);
                a.Windows[0].Window.UsedPercent = -1; AssertEqual("UsageEvidenceInvalid", Evaluate(a).Candidates[0].ReasonCode);
                a.Windows[0].Window.UsedPercent = 10; a.Windows[0].UnsettledPercent = Double.PositiveInfinity;
                AssertEqual("UsageEvidenceInvalid", Evaluate(a).Candidates[0].ReasonCode);
                a.Windows[0].UnsettledPercent = 0; a.HasUncertainDebt = true;
                AssertEqual("UsageUncertainDebt", Evaluate(a).Candidates[0].ReasonCode);
            });
            Add("candidate_limit_never_selects_truncated_input", () =>
            {
                UsageSelectionSettings settings = new UsageSelectionSettings { MaximumCandidates = 1 };
                UsageSelectionEvaluation result = UsageSelectionPolicy.Evaluate(new[] { Fresh("a", 0), Fresh("b", 0) }, settings, _Now);
                AssertTrue(result.CandidateLimitExceeded); AssertNull(result.SelectedCaptainId); AssertEqual(0, result.Candidates.Count);
                AssertThrows<ArgumentException>(() => Evaluate(Fresh("a", 0), Fresh("a", 0)));
            });
            Add("evaluation_does_not_mutate_source_evidence", () =>
            {
                UsageSelectionCandidate a = Fresh("a", 10); string before = JsonSerializer.Serialize(a);
                Evaluate(a); AssertEqual(before, JsonSerializer.Serialize(a));
            });
            return new TestSuiteDescriptor(_SuiteId, "Usage selection policy", cases);
        }

        private static UsageSelectionEvaluation Evaluate(params UsageSelectionCandidate[] candidates)
            => UsageSelectionPolicy.Evaluate(candidates, new UsageSelectionSettings { Strategy = CaptainSelectionStrategyEnum.MostRemainingAllowance }, _Now);

        private static UsageSelectionCandidate Fresh(string id, double used)
            => new UsageSelectionCandidate { CaptainId = id, Provider = "anthropic", Model = "opus", IdentityVerified = true,
                CapturedUtc = _Now, Status = UsageSnapshotStatusEnum.Ok, Source = UsageCaptureSourceEnum.Headers, LastAssignedUtc = _Now,
                Windows = new List<UsageSelectionWindowEvidence> { new UsageSelectionWindowEvidence {
                    Window = new UsageWindow { Name = "five_hour", WindowMinutes = 300, Unit = "percent", UsedPercent = used } } } };
    }
}

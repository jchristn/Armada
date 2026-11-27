namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Capability-aware routing (RELIABILITY_SIMPLICITY_PLAN.md RS-8.4 to RS-8.7): served context as a hard filter with
    /// the context_too_small blocker, model profile tiers and tier suggestions, per-persona outcome ranking behind its
    /// setting, and the Judge strength rule with model-identity author exclusion.
    /// </summary>
    public sealed class CapabilityRoutingSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.CapabilityRouting";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            // RS-8.4
            cases.Add(Case("prompt_fit_rule", "A served context must hold the untrimmed prompt plus the handoff within 75 percent; unknown context fits", TestTags.Positive, () =>
            {
                Mission small = new Mission("t", "short description") { Persona = "Worker" };
                AssertNull(MissionPromptFit.IneligibilityReason(small, null), "Unknown context always fits");
                AssertNull(MissionPromptFit.IneligibilityReason(small, 32768), "A short Worker prompt fits 32K");
                AssertNotNull(MissionPromptFit.IneligibilityReason(small, 2048), "2K cannot hold the fixed allowance");
                Mission big = new Mission("t", new string('x', 100000)) { Persona = "Worker" };
                string? reason = MissionPromptFit.IneligibilityReason(big, 32768);
                AssertNotNull(reason, "A 100,000-character description does not fit 32K");
                AssertContains("cannot hold the prompt", reason!);
                AssertNull(MissionPromptFit.IneligibilityReason(big, 131072), "It fits 128K");
                Mission dependent = new Mission("t", "short") { Persona = "Judge", DependsOnMissionId = "msn_x" };
                AssertTrue(MissionPromptFit.EstimateTokens(dependent, 64000) - MissionPromptFit.FixedTokens(dependent) == 64000 / HandoffRenderer.ContextDivisor,
                    "A dependent stage adds the handoff's share of the context");
                ModelEndpoint endpoint = new ModelEndpoint();
                endpoint.InferenceOptions.ContextLength = null;
                endpoint.DiscoveredContextLength = 40960;
                AssertEqual(40960, MissionPromptFit.ServedContext(endpoint, null), "A discovered context counts when none is configured");
                endpoint.InferenceOptions.ContextLength = 8192;
                AssertEqual(8192, MissionPromptFit.ServedContext(endpoint, null), "The configured context wins");
                ModelProfile profile = new ModelProfile();
                profile.Identity.ServedContextTokens = 16384;
                AssertEqual(16384, MissionPromptFit.ServedContext(null, profile), "Else the profile's served context");
                return Task.CompletedTask;
            }));

            cases.Add(Case("context_too_small_blocks_and_routes_to_larger", "A captain whose served context cannot hold the prompt is skipped with the context_too_small blocker", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Harness h = await BuildAsync(testDb.Driver).ConfigureAwait(false);
                Captain small = await AddCaptainAsync(testDb.Driver, "small", "qwen3.8:27b", null).ConfigureAwait(false);
                await LinkProfileAsync(testDb.Driver, small, "qwen3.8:27b", 8192, null).ConfigureAwait(false);
                Mission mission = await AddMissionAsync(testDb.Driver, h.Vessel, "Worker", new string('d', 60000), null).ConfigureAwait(false);

                AssertFalse(await h.Missions.TryAssignAsync(mission, h.Vessel).ConfigureAwait(false), "The 8K captain cannot take a 60,000-character mission");
                MissionAssignmentBlocker? blocker = await h.Missions.GetAssignmentBlockerAsync(mission).ConfigureAwait(false);
                AssertNotNull(blocker, "The mission has a blocker");
                AssertEqual(MissionAssignmentBlockerReasonEnum.NoEligibleCaptain, blocker!.Reason);
                AssertEqual(MissionPromptFit.BlockerCode, blocker.SelectionReasonCode, "Typed context_too_small code");
                AssertTrue(blocker.Captains.Any(c => c.CaptainId == small.Id && (c.Detail ?? "").Contains("cannot hold the prompt")), "The captain status says why");

                Captain large = await AddCaptainAsync(testDb.Driver, "large", "qwen3.8:27b-128k", null).ConfigureAwait(false);
                await LinkProfileAsync(testDb.Driver, large, "qwen3.8:27b-128k", 131072, null).ConfigureAwait(false);
                AssertTrue(await h.Missions.TryAssignAsync(mission, h.Vessel).ConfigureAwait(false), "The 128K captain takes it");
                AssertEqual(large.Id, (await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!.CaptainId);
            }));

            // RS-8.5
            cases.Add(Case("profile_tier_between_captain_and_regex", "Effective tier is the captain tier, else the profile tier, else the model name", TestTags.Positive, () =>
            {
                Captain captain = new Captain("c") { Model = "qwen3.8:27b" };
                AssertEqual(CaptainTierEnum.Standard, CaptainTierSelector.EffectiveTier(captain), "Regex classifies qwen as Standard");
                captain.ProfileTier = CaptainTierEnum.Premium;
                AssertEqual(CaptainTierEnum.Premium, CaptainTierSelector.EffectiveTier(captain), "The profile tier beats the model name");
                captain.Tier = CaptainTierEnum.Economy;
                AssertEqual(CaptainTierEnum.Economy, CaptainTierSelector.EffectiveTier(captain), "An explicit captain tier always wins");
                Captain vendorDefault = new Captain("v") { Runtime = AgentRuntimeEnum.ClaudeCode, ProfileTier = CaptainTierEnum.Economy };
                AssertFalse(CaptainTierSelector.CanHandle(vendorDefault, CaptainTierEnum.Premium), "A profile tier is taken as declared, like an explicit tier");
                return Task.CompletedTask;
            }));

            cases.Add(Case("profile_tier_routes", "A Premium mission routes to a captain whose profile an administrator set to Premium", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Harness h = await BuildAsync(testDb.Driver).ConfigureAwait(false);
                Captain plain = await AddCaptainAsync(testDb.Driver, "plain", "qwen3.8:27b", null).ConfigureAwait(false);
                Mission mission = await AddMissionAsync(testDb.Driver, h.Vessel, "Worker", "do it", CaptainTierEnum.Premium).ConfigureAwait(false);
                AssertFalse(await h.Missions.TryAssignAsync(mission, h.Vessel).ConfigureAwait(false), "qwen is Standard by name, below Premium");
                await LinkProfileAsync(testDb.Driver, plain, "qwen3.8:27b", 131072, CaptainTierEnum.Premium).ConfigureAwait(false);
                AssertTrue(await h.Missions.TryAssignAsync(mission, h.Vessel).ConfigureAwait(false), "The profile tier makes it Premium");
            }));

            cases.Add(Case("profile_tiering_round_trip", "A profile's tier and suggestion round-trip through the database", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ModelProfile profile = new ModelProfile();
                profile.Identity.Runtime = "ApiEndpoint";
                profile.Identity.Model = "qwen3.8:27b";
                profile.Tiering.Tier = CaptainTierEnum.Standard;
                profile.Tiering.SuggestedTier = CaptainTierEnum.Premium;
                profile.Tiering.SuggestedReason = "because";
                profile.Tiering.SuggestedSamples = 42;
                profile.Tiering.SuggestedUtc = new DateTime(2026, 11, 26, 10, 0, 0, DateTimeKind.Utc);
                profile = await testDb.Driver.ModelProfiles.CreateAsync(profile, ModelProfileRules.IdentityKey(null, profile.Identity)).ConfigureAwait(false);
                ModelProfile? read = await testDb.Driver.ModelProfiles.ReadAsync(profile.Id).ConfigureAwait(false);
                AssertEqual(CaptainTierEnum.Standard, read!.Tiering.Tier);
                AssertEqual(CaptainTierEnum.Premium, read.Tiering.SuggestedTier);
                AssertEqual("because", read.Tiering.SuggestedReason);
                AssertEqual(42, read.Tiering.SuggestedSamples);
                AssertNotNull(read.Tiering.SuggestedUtc);
                read.Tiering = new ModelProfileTiering();
                await testDb.Driver.ModelProfiles.UpdateAsync(read).ConfigureAwait(false);
                ModelProfile? cleared = await testDb.Driver.ModelProfiles.ReadAsync(profile.Id).ConfigureAwait(false);
                AssertNull(cleared!.Tiering.Tier, "Cleared tier reads as null");
                AssertNull(cleared.Tiering.SuggestedTier, "Cleared suggestion reads as null");
            }));

            cases.Add(Case("tier_suggester_thresholds", "Tier suggestions need the minimum samples, pool by samples, and stay quiet when they agree", TestTags.Positive, () =>
            {
                List<PersonaOutcomeScore> few = new List<PersonaOutcomeScore> { new PersonaOutcomeScore { Persona = "Worker", Samples = 19, Score = 0.95 } };
                AssertNull(ModelTierSuggester.Suggest(few, 20, 14, CaptainTierEnum.Standard), "19 samples are not enough");
                List<PersonaOutcomeScore> strong = new List<PersonaOutcomeScore>
                {
                    new PersonaOutcomeScore { Persona = "Worker", Samples = 30, Score = 0.95 },
                    new PersonaOutcomeScore { Persona = "Judge", Samples = 10, Score = 0.5 },
                    new PersonaOutcomeScore { Persona = "Architect", Samples = 5, Score = 0.0 }
                };
                // Pooled (30 x 0.95 + 10 x 0.5) / 40 = 0.84; the Architect's 5 samples are below the minimum and do not count.
                ModelProfileTiering? up = ModelTierSuggester.Suggest(strong, 10, 14, CaptainTierEnum.Economy);
                AssertEqual(CaptainTierEnum.Standard, up!.SuggestedTier, "0.84 suggests Standard");
                AssertEqual(40, up.SuggestedSamples, "Only the counted personas' samples");
                AssertNull(ModelTierSuggester.Suggest(strong, 10, 14, CaptainTierEnum.Standard), "Standard agrees, so no suggestion");
                AssertEqual(CaptainTierEnum.Standard, ModelTierSuggester.TierFor(0.84));
                AssertEqual(CaptainTierEnum.Premium, ModelTierSuggester.TierFor(0.85));
                AssertEqual(CaptainTierEnum.Economy, ModelTierSuggester.TierFor(0.64));
                List<PersonaOutcomeScore> premium = new List<PersonaOutcomeScore> { new PersonaOutcomeScore { Persona = "Worker", Samples = 25, Score = 0.9 } };
                ModelProfileTiering? suggestion = ModelTierSuggester.Suggest(premium, 20, 14, CaptainTierEnum.Standard);
                AssertEqual(CaptainTierEnum.Premium, suggestion!.SuggestedTier);
                AssertEqual(25, suggestion.SuggestedSamples);
                AssertContains("14-day half-life", suggestion.SuggestedReason!);
                AssertNull(ModelTierSuggester.Suggest(premium, 20, 14, CaptainTierEnum.Premium), "No suggestion when it agrees with the current tier");
                return Task.CompletedTask;
            }));

            cases.Add(Case("tier_suggestion_alert_lifecycle", "With SuggestTiers on, the alert pass records a suggestion and raises a TierSuggestion alert with Apply tier; off, it resolves", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                LoggingModule logging = Quiet();
                ModelProfile profile = await SeedOutcomesAsync(testDb.Driver, logging, "qwen3.8:27b", 25, true).ConfigureAwait(false);
                RoutingDataSettings routing = new RoutingDataSettings { SuggestTiers = true };
                ModelAlertService alerts = new ModelAlertService(testDb.Driver, logging, () => new ModelAlertSettings());
                alerts.RoutingData = () => routing;
                ModelAlertEvaluation evaluation = await alerts.EvaluateAsync(null).ConfigureAwait(false);
                ModelAlert? alert = evaluation.Raised.FirstOrDefault(a => a.Rule == ModelAlertRuleEnum.TierSuggestion);
                AssertNotNull(alert, "A TierSuggestion alert is raised");
                AssertEqual(profile.Id, alert!.ProfileId);
                AssertContains("suggests tier Premium", alert.Title);
                ModelProfile? stored = await testDb.Driver.ModelProfiles.ReadAsync(profile.Id).ConfigureAwait(false);
                AssertEqual(CaptainTierEnum.Premium, stored!.Tiering.SuggestedTier, "The suggestion is recorded on the profile");
                AssertNull(stored.Tiering.Tier, "Routing's tier is not changed");

                ModelAlertActionPlan plan = await new ModelAlertActionPlanner(testDb.Driver).PlanAsync(alert, null, 7).ConfigureAwait(false);
                AssertEqual(2, plan.Actions.Count, "Apply tier and Dismiss");
                ModelAlertSuggestedAction apply = plan.Actions.First(a => a.Action == ModelAlertActionEnum.ApplyTier);
                AssertTrue(apply.Available && apply.Recommended, "Apply tier is available and recommended");
                AssertEqual("Apply tier Premium", apply.Label);
                AssertTrue(plan.Actions.Any(a => a.Action == ModelAlertActionEnum.Ignore), "Dismiss is offered");

                ModelAlertEvaluation again = await alerts.EvaluateAsync(null).ConfigureAwait(false);
                AssertFalse(again.Raised.Any(a => a.Rule == ModelAlertRuleEnum.TierSuggestion), "An unchanged suggestion is not raised twice");
                routing.SuggestTiers = false;
                await alerts.EvaluateAsync(null).ConfigureAwait(false);
                ModelAlert? resolved = await testDb.Driver.ModelAlerts.ReadAsync(alert.Id).ConfigureAwait(false);
                AssertEqual(ModelAlertStatusEnum.Resolved, resolved!.Status, "Turning the setting off resolves the suggestion");
            }));

            // RS-8.6
            cases.Add(Case("outcome_stats_decay", "Outcome scores weight attempts by a half-life and charge Judge verdicts to the reviewed attempt", TestTags.Positive, () =>
            {
                DateTime now = new DateTime(2026, 11, 26, 0, 0, 0, DateTimeKind.Utc);
                AssertTrue(Math.Abs(PersonaOutcomeStats.Weight(now.AddDays(-14), now, 14) - 0.5) < 1e-9, "One half-life weighs one half");
                AssertTrue(Math.Abs(PersonaOutcomeStats.Weight(now.AddDays(1), now, 14) - 1.0) < 1e-9, "A future end time weighs 1");
                List<StageOutcome> outcomes = new List<StageOutcome>
                {
                    Outcome("msn_a", "Worker", "qwen", StageDispositionEnum.Completed, now),
                    Outcome("msn_b", "Worker", "qwen", StageDispositionEnum.Failed, now.AddDays(-14)),
                    Outcome("msn_c", "Worker", "qwen", StageDispositionEnum.Completed, now)
                };
                outcomes[0].StageCheckStatus = StageCheckStatusEnum.Passed;
                StageOutcome judge = Outcome("msn_j", "Judge", "gpt-6.1", StageDispositionEnum.Completed, now);
                judge.JudgeVerdict = "NEEDS_REVISION";
                judge.ReviewedMissionId = "msn_a";
                outcomes.Add(judge);
                StageOutcome infra = Outcome("msn_d", "Worker", "qwen", StageDispositionEnum.Failed, now);
                infra.FailureKind = MissionFailureKindEnum.Infra;
                outcomes.Add(infra);
                Dictionary<string, PersonaOutcomeScore> scores = PersonaOutcomeStats.Compute(outcomes, now, 14);
                PersonaOutcomeScore worker = scores.Values.Single(s => s.Persona == "Worker");
                AssertEqual(3, worker.Samples, "The infrastructure failure is not counted");
                AssertTrue(Math.Abs(worker.SuccessRate!.Value - 2.0 / 2.5) < 1e-9, "The 14-day-old failure weighs one half");
                AssertTrue(Math.Abs(worker.JudgePassRate!.Value - 0.0) < 1e-9, "The NEEDS_REVISION verdict is charged to the reviewed Worker");
                return Task.CompletedTask;
            }));

            cases.Add(Case("outcome_ranking_behind_setting", "RankByOutcomes orders a tier by persona results once a model has the minimum samples; off, the 1.4.0 order stays", TestTags.Positive, async () =>
            {
                foreach (bool rank in new[] { false, true })
                {
                    using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                    LoggingModule logging = Quiet();
                    ModelProfile weak = await SeedOutcomesAsync(testDb.Driver, logging, "weak-model", 20, false).ConfigureAwait(false);
                    ModelProfile strong = await SeedOutcomesAsync(testDb.Driver, logging, "strong-model", 20, true).ConfigureAwait(false);
                    Harness h = await BuildAsync(testDb.Driver, s => { s.RoutingData.RankByOutcomes = rank; }).ConfigureAwait(false);
                    // The weak captain is older, so the 1.4.0 order (load, then last update) prefers it.
                    Captain weakCaptain = await AddCaptainAsync(testDb.Driver, "weak", "weak-model", CaptainTierEnum.Standard, weak.Id, DateTime.UtcNow.AddHours(-2)).ConfigureAwait(false);
                    Captain strongCaptain = await AddCaptainAsync(testDb.Driver, "strong", "strong-model", CaptainTierEnum.Standard, strong.Id, DateTime.UtcNow.AddHours(-1)).ConfigureAwait(false);
                    Mission mission = await AddMissionAsync(testDb.Driver, h.Vessel, "Worker", "do it", CaptainTierEnum.Standard).ConfigureAwait(false);
                    AssertTrue(await h.Missions.TryAssignAsync(mission, h.Vessel).ConfigureAwait(false), "Assigned");
                    string? chosen = (await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!.CaptainId;
                    AssertEqual(rank ? strongCaptain.Id : weakCaptain.Id, chosen, rank ? "Ranking picks the better model" : "Without ranking the older captain is picked");
                    List<ArmadaEvent> events = await testDb.Driver.Events.EnumerateByMissionAsync(mission.Id, 50).ConfigureAwait(false);
                    AssertEqual(rank, events.Any(e => e.EventType == CaptainOutcomeRank.EventType), "The ranking is recorded only when it applied");
                }
            }));

            // RS-8.7
            cases.Add(Case("judge_strength_wait_blocks_weaker_judge", "With the rule on (Wait), a Judge below the reviewed work's tier waits with the judge_strength blocker", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Harness h = await BuildAsync(testDb.Driver).ConfigureAwait(false);
                Mission judge = await JudgeChainAsync(testDb.Driver, h, JudgeStrengthPolicyEnum.Wait, "claude-opus-5-5", CaptainTierEnum.Premium).ConfigureAwait(false);
                Captain weak = await AddCaptainAsync(testDb.Driver, "qwen-judge", "qwen3.8:27b", CaptainTierEnum.Standard).ConfigureAwait(false);
                AssertFalse(await h.Missions.TryAssignAsync(judge, h.Vessel).ConfigureAwait(false), "A Standard Judge cannot review Premium work");
                MissionAssignmentBlocker? blocker = await h.Missions.GetAssignmentBlockerAsync(judge).ConfigureAwait(false);
                AssertEqual(JudgeStrengthRule.BlockerCode, blocker!.SelectionReasonCode, "Typed judge_strength code");
                AssertContains("Premium", blocker.Summary);
                Captain strong = await AddCaptainAsync(testDb.Driver, "gpt-judge", "gpt-6.1", CaptainTierEnum.Premium).ConfigureAwait(false);
                AssertTrue(await h.Missions.TryAssignAsync(judge, h.Vessel).ConfigureAwait(false), "A Premium Judge takes it");
                AssertEqual(strong.Id, (await testDb.Driver.Missions.ReadAsync(judge.Id).ConfigureAwait(false))!.CaptainId);
            }));

            cases.Add(Case("judge_strength_off_keeps_legacy", "With the rule off (every voyage from before 1.5.0), any eligible captain judges", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Harness h = await BuildAsync(testDb.Driver).ConfigureAwait(false);
                Mission judge = await JudgeChainAsync(testDb.Driver, h, null, "claude-opus-5-5", CaptainTierEnum.Premium).ConfigureAwait(false);
                Captain weak = await AddCaptainAsync(testDb.Driver, "qwen-judge", "qwen3.8:27b", CaptainTierEnum.Standard).ConfigureAwait(false);
                AssertTrue(await h.Missions.TryAssignAsync(judge, h.Vessel).ConfigureAwait(false), "No rule, so the Standard Judge runs");
                AssertEqual(weak.Id, (await testDb.Driver.Missions.ReadAsync(judge.Id).ConfigureAwait(false))!.CaptainId);
            }));

            cases.Add(Case("judge_strength_proceed_and_flag", "ProceedAndFlag runs the best available Judge and records a judge strength exception event", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Harness h = await BuildAsync(testDb.Driver).ConfigureAwait(false);
                Mission judge = await JudgeChainAsync(testDb.Driver, h, JudgeStrengthPolicyEnum.ProceedAndFlag, "claude-opus-5-5", CaptainTierEnum.Premium).ConfigureAwait(false);
                Captain weak = await AddCaptainAsync(testDb.Driver, "qwen-judge", "qwen3.8:27b", CaptainTierEnum.Standard).ConfigureAwait(false);
                AssertTrue(await h.Missions.TryAssignAsync(judge, h.Vessel).ConfigureAwait(false), "The mission proceeds");
                AssertEqual(weak.Id, (await testDb.Driver.Missions.ReadAsync(judge.Id).ConfigureAwait(false))!.CaptainId);
                List<ArmadaEvent> events = await testDb.Driver.Events.EnumerateByMissionAsync(judge.Id, 50).ConfigureAwait(false);
                AssertTrue(events.Any(e => e.EventType == JudgeStrengthRule.ExceptionEventType), "The exception is flagged (G8)");
            }));

            cases.Add(Case("judge_prefers_other_family", "Among Judges that meet the rule, one of another model family than the authors is preferred", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Harness h = await BuildAsync(testDb.Driver).ConfigureAwait(false);
                Mission judge = await JudgeChainAsync(testDb.Driver, h, JudgeStrengthPolicyEnum.Wait, "claude-opus-5-5", CaptainTierEnum.Premium).ConfigureAwait(false);
                // The same-family captain is older, so it would win on the 1.4.0 order.
                await AddCaptainAsync(testDb.Driver, "sonnet-judge", "claude-sonnet-5-5", CaptainTierEnum.Premium, null, DateTime.UtcNow.AddHours(-2)).ConfigureAwait(false);
                Captain gpt = await AddCaptainAsync(testDb.Driver, "gpt-judge", "gpt-6.1", CaptainTierEnum.Premium, null, DateTime.UtcNow.AddHours(-1)).ConfigureAwait(false);
                AssertTrue(await h.Missions.TryAssignAsync(judge, h.Vessel).ConfigureAwait(false), "Assigned");
                AssertEqual(gpt.Id, (await testDb.Driver.Missions.ReadAsync(judge.Id).ConfigureAwait(false))!.CaptainId, "The other family judges");
            }));

            cases.Add(Case("author_exclusion_by_model_identity", "ExcludeReviewAuthorsFromJudge excludes every captain on an author's model, not only the author's captain", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Harness h = await BuildAsync(testDb.Driver, s => s.ExcludeReviewAuthorsFromJudge = true).ConfigureAwait(false);
                Mission judge = await JudgeChainAsync(testDb.Driver, h, null, "qwen3.8:27b", null).ConfigureAwait(false);
                await AddCaptainAsync(testDb.Driver, "qwen-twin", "ollama/qwen3.8:27b", null).ConfigureAwait(false);
                AssertFalse(await h.Missions.TryAssignAsync(judge, h.Vessel).ConfigureAwait(false), "Another captain on the author's model cannot judge");
                Captain other = await AddCaptainAsync(testDb.Driver, "glm-judge", "glm-5", null).ConfigureAwait(false);
                AssertTrue(await h.Missions.TryAssignAsync(judge, h.Vessel).ConfigureAwait(false), "A captain on another model can");
                AssertEqual(other.Id, (await testDb.Driver.Missions.ReadAsync(judge.Id).ConfigureAwait(false))!.CaptainId);
                AssertEqual("model:qwen3.8:27b", JudgeStrengthRule.ModelIdentity(new Captain("a") { Model = "ollama/QWEN3.8:27b" }, null), "Identity ignores the provider prefix and case");
                AssertEqual("default:codex", JudgeStrengthRule.ModelIdentity(new Captain("b") { Runtime = AgentRuntimeEnum.Codex }, null), "No model is the runtime default");
            }));

            cases.Add(Case("judge_strength_persists_and_defaults", "judgeStrength round-trips on pipelines and voyages; a null stored value reads as the rule off", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Pipeline pipeline = new Pipeline("rs87-pipeline") { JudgeStrength = JudgeStrengthPolicyEnum.ProceedAndFlag };
                pipeline.Stages = new List<PipelineStage> { new PipelineStage(1, "Worker"), new PipelineStage(2, "Judge") };
                pipeline = await testDb.Driver.Pipelines.CreateAsync(pipeline).ConfigureAwait(false);
                AssertEqual(JudgeStrengthPolicyEnum.ProceedAndFlag, (await testDb.Driver.Pipelines.ReadAsync(pipeline.Id).ConfigureAwait(false))!.JudgeStrength);
                Pipeline legacy = await testDb.Driver.Pipelines.CreateAsync(new Pipeline("rs87-legacy") { Stages = new List<PipelineStage> { new PipelineStage(1, "Worker") } }).ConfigureAwait(false);
                AssertNull((await testDb.Driver.Pipelines.ReadAsync(legacy.Id).ConfigureAwait(false))!.JudgeStrength, "A stored null stays null");
                AssertEqual(JudgeStrengthPolicyEnum.Off, JudgeStrengthRule.Effective(new Voyage("v")), "A voyage without a value has the rule off");
                Voyage voyage = await testDb.Driver.Voyages.CreateAsync(new Voyage("v") { JudgeStrength = JudgeStrengthPolicyEnum.Wait }).ConfigureAwait(false);
                AssertEqual(JudgeStrengthPolicyEnum.Wait, (await testDb.Driver.Voyages.ReadAsync(voyage.Id).ConfigureAwait(false))!.JudgeStrength);
                AssertNull(JudgeStrengthRule.Parse("nonsense"), "Unknown text reads as null");
                AssertNull(JudgeStrengthRule.Parse(DBNull.Value), "DBNull reads as null");
            }));

            cases.Add(Case("routing_data_settings_clamp", "RoutingData defaults are off, 20 samples and a 14-day half-life, and clamp to their ranges", TestTags.Positive, () =>
            {
                RoutingDataSettings settings = new ArmadaSettings().RoutingData;
                AssertFalse(settings.RankByOutcomes, "Ranking off by default");
                AssertFalse(settings.SuggestTiers, "Suggestions off by default");
                AssertEqual(20, settings.MinimumSamples);
                AssertEqual(14, settings.HalfLifeDays);
                settings.MinimumSamples = 1;
                settings.HalfLifeDays = 9999;
                AssertEqual(RoutingDataSettings.MinimumSamplesFloor, settings.MinimumSamples);
                AssertEqual(RoutingDataSettings.HalfLifeDaysCeiling, settings.HalfLifeDays);
                return Task.CompletedTask;
            }));

            return new TestSuiteDescriptor(SuiteId, "Capability-aware routing (RS-8.4 to RS-8.7)", cases);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        private static LoggingModule Quiet()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private static async Task<Harness> BuildAsync(DatabaseDriver db, Action<ArmadaSettings>? configure = null)
        {
            LoggingModule logging = Quiet();
            ArmadaSettings settings = new ArmadaSettings();
            settings.DocksDirectory = Path.Combine(Path.GetTempPath(), "armada_caprouting_docks_" + Guid.NewGuid().ToString("N"));
            settings.ReposDirectory = Path.Combine(Path.GetTempPath(), "armada_caprouting_repos_" + Guid.NewGuid().ToString("N"));
            configure?.Invoke(settings);
            DirCreatingGitService git = new DirCreatingGitService();
            IDockService docks = new DockService(logging, db, settings, git);
            CaptainService captains = new CaptainService(logging, db, settings, git, docks);
            MissionService missions = new MissionService(logging, db, settings, docks, captains, git: git);
            int nextPid = 7000;
            captains.OnLaunchAgent = (_, _, _) => Task.FromResult(++nextPid);
            missions.OnGetMissionOutput = _ => "output";
            Vessel vessel = new Vessel("caprouting-vessel", "https://github.com/test/repo.git");
            vessel.LocalPath = Path.Combine(Path.GetTempPath(), "armada_caprouting_bare_" + Guid.NewGuid().ToString("N"));
            vessel.WorkingDirectory = Path.Combine(Path.GetTempPath(), "armada_caprouting_work_" + Guid.NewGuid().ToString("N"));
            vessel.DefaultBranch = "main";
            vessel = await db.Vessels.CreateAsync(vessel).ConfigureAwait(false);
            return new Harness { Missions = missions, Vessel = vessel };
        }

        private static async Task<Captain> AddCaptainAsync(DatabaseDriver db, string name, string? model, CaptainTierEnum? tier, string? profileId = null, DateTime? lastUpdateUtc = null)
        {
            Captain captain = new Captain(name) { State = CaptainStateEnum.Idle, Model = model, Tier = tier };
            captain = await db.Captains.CreateAsync(captain).ConfigureAwait(false);
            if (profileId != null)
            {
                await db.ModelProfiles.LinkCaptainAsync(captain.Id, profileId).ConfigureAwait(false);
                captain = (await db.Captains.ReadAsync(captain.Id).ConfigureAwait(false))!;
            }
            if (lastUpdateUtc.HasValue)
            {
                captain.LastUpdateUtc = lastUpdateUtc.Value;
                captain = await db.Captains.UpdateAsync(captain).ConfigureAwait(false);
            }
            return captain;
        }

        private static async Task LinkProfileAsync(DatabaseDriver db, Captain captain, string model, int servedContext, CaptainTierEnum? tier)
        {
            ModelProfile profile = new ModelProfile();
            profile.Identity.Runtime = captain.Runtime.ToString();
            profile.Identity.Model = model;
            profile.Identity.ServedContextTokens = servedContext;
            profile.Tiering.Tier = tier;
            profile = await db.ModelProfiles.CreateAsync(profile, ModelProfileRules.IdentityKey(null, profile.Identity)).ConfigureAwait(false);
            await db.ModelProfiles.LinkCaptainAsync(captain.Id, profile.Id).ConfigureAwait(false);
        }

        private static async Task<Mission> AddMissionAsync(DatabaseDriver db, Vessel vessel, string persona, string description, CaptainTierEnum? tier, string? voyageId = null)
        {
            Mission mission = new Mission("route " + persona, description);
            mission.VesselId = vessel.Id;
            mission.Persona = persona;
            mission.Tier = tier;
            mission.VoyageId = voyageId;
            mission.Status = MissionStatusEnum.Pending;
            return await db.Missions.CreateAsync(mission).ConfigureAwait(false);
        }

        private static async Task<Mission> JudgeChainAsync(DatabaseDriver db, Harness h, JudgeStrengthPolicyEnum? policy, string authorModel, CaptainTierEnum? authorTier)
        {
            Voyage voyage = await db.Voyages.CreateAsync(new Voyage("rs87") { JudgeStrength = policy }).ConfigureAwait(false);
            Captain author = await AddCaptainAsync(db, "author", authorModel, authorTier).ConfigureAwait(false);
            author.State = CaptainStateEnum.Working;
            await db.Captains.UpdateAsync(author).ConfigureAwait(false);
            Mission worker = await AddMissionAsync(db, h.Vessel, "Worker", "implement", null, voyage.Id).ConfigureAwait(false);
            worker.Status = MissionStatusEnum.WorkProduced;
            worker.CaptainId = author.Id;
            await db.Missions.UpdateAsync(worker).ConfigureAwait(false);
            Mission judge = await AddMissionAsync(db, h.Vessel, "Judge", "review", null, voyage.Id).ConfigureAwait(false);
            judge.DependsOnMissionId = worker.Id;
            return await db.Missions.UpdateAsync(judge).ConfigureAwait(false);
        }

        private static StageOutcome Outcome(string missionId, string persona, string model, StageDispositionEnum disposition, DateTime endedUtc)
        {
            return new StageOutcome
            {
                MissionId = missionId,
                Persona = persona,
                Runtime = AgentRuntimeEnum.ApiEndpoint,
                Model = model,
                Disposition = disposition,
                StartedUtc = endedUtc.AddMinutes(-5),
                EndedUtc = endedUtc
            };
        }

        private static async Task<ModelProfile> SeedOutcomesAsync(DatabaseDriver db, LoggingModule logging, string model, int count, bool succeed)
        {
            DateTime now = DateTime.UtcNow;
            ModelProfile? profile = null;
            for (int i = 0; i < count; i++)
            {
                StageOutcome outcome = Outcome("msn_" + model.Replace(".", "_").Replace(":", "_") + "_" + i, "Worker", model,
                    succeed ? StageDispositionEnum.Completed : StageDispositionEnum.Failed, now.AddMinutes(-i - 1));
                outcome.StageCheckStatus = succeed ? StageCheckStatusEnum.Passed : StageCheckStatusEnum.Failed;
                if (!succeed) outcome.FailureKind = MissionFailureKindEnum.NoOp;
                await db.StageOutcomes.CreateAsync(outcome).ConfigureAwait(false);
                if (profile == null)
                    profile = (await new ModelProfileService(db, logging).ResolveAsync(ModelProfileService.ObservationFrom(outcome)!).ConfigureAwait(false)).Profile;
            }
            return profile!;
        }

        #endregion

        #region Private-Types

        private sealed class Harness
        {
            public MissionService Missions { get; set; } = null!;
            public Vessel Vessel { get; set; } = null!;
        }

        #endregion
    }
}

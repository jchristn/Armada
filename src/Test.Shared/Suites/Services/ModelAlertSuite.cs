namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Push;
    using Armada.Core.Settings;
    using Armada.Server;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Model alerts (docs/MODEL_HETEROGENEITY.md Phase 5 item 3): threshold evaluation below, at and above each threshold,
    /// minimum counts not met, unavailable metrics, overrides over defaults, the median cost rule, dedupe and auto-resolve
    /// on the configured provider, ignore-for-N-days expiry in the inbox, every suggested action (and that nothing changes
    /// until one is applied), and tenant scoping of the access service.
    /// </summary>
    public sealed class ModelAlertSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.ModelAlerts";
        private const string Tenant = "ten_ma";
        private const string OtherTenant = "ten_mb";
        private const string ModelName = "qwen3.8:27b";
        private static readonly DateTime _Now = new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("first_pass_boundaries", "First-pass success: below the threshold is crossed, at and above are clear, too few missions is MinimumNotMet, an unavailable or missing metric is Unavailable", () =>
            {
                ModelAlertThreshold defaults = new ModelAlertThresholds().EffectiveFor("Worker");
                AssertEqual(ModelAlertRuleStateEnum.Crossed, Rule(Group(Rate("firstPassSuccess", 3, 8)), defaults, ModelAlertRuleEnum.FirstPassSuccess).State, "3 of 8 (37.5%) is below 50%");
                AssertEqual(ModelAlertRuleStateEnum.Clear, Rule(Group(Rate("firstPassSuccess", 4, 8)), defaults, ModelAlertRuleEnum.FirstPassSuccess).State, "4 of 8 (50%) is at the threshold");
                AssertEqual(ModelAlertRuleStateEnum.Clear, Rule(Group(Rate("firstPassSuccess", 5, 8)), defaults, ModelAlertRuleEnum.FirstPassSuccess).State, "5 of 8 is above");
                ModelAlertRuleResult few = Rule(Group(Rate("firstPassSuccess", 0, 7)), defaults, ModelAlertRuleEnum.FirstPassSuccess);
                AssertEqual(ModelAlertRuleStateEnum.MinimumNotMet, few.State, "7 missions is below the minimum of 8");
                AssertContains("at least 8", few.Reason ?? "", "reason names the minimum");
                ModelAlertRuleResult crossed = Rule(Group(Rate("firstPassSuccess", 4, 11)), defaults, ModelAlertRuleEnum.FirstPassSuccess);
                AssertEqual((int?)4, crossed.Count, "count");
                AssertEqual((int?)11, crossed.Of, "of");
                AssertEqual((double?)WilsonScoreInterval.Lower(4, 11), crossed.Lower, "lower");
                AssertEqual(8, crossed.Minimum, "minimum");
                ModelScorecardMetric off = new ModelScorecardMetric { Key = "firstPassSuccess", Available = false, UnavailableReason = "not recorded" };
                ModelAlertRuleResult unavailable = Rule(Group(off), defaults, ModelAlertRuleEnum.FirstPassSuccess);
                AssertEqual(ModelAlertRuleStateEnum.Unavailable, unavailable.State, "unavailable metric");
                AssertEqual("not recorded", unavailable.Reason, "the scorecard's reason");
                AssertEqual(ModelAlertRuleStateEnum.Unavailable, Rule(Group(), defaults, ModelAlertRuleEnum.EscalationsInARow).State, "missing metric");
                return Task.CompletedTask;
            }));

            cases.Add(Case("escalation_and_repair_rules", "Escalations in a row cross at the threshold; protocol repairs above 30% over at least 8 attempts cross and recommend the compact template", () =>
            {
                ModelAlertThreshold defaults = new ModelAlertThresholds().EffectiveFor("Worker");
                AssertEqual(ModelAlertRuleStateEnum.Clear, Rule(Group(Rate("escalations.inARow", 2, 9)), defaults, ModelAlertRuleEnum.EscalationsInARow).State, "2 in a row");
                AssertEqual(ModelAlertRuleStateEnum.Crossed, Rule(Group(Rate("escalations.inARow", 3, 9)), defaults, ModelAlertRuleEnum.EscalationsInARow).State, "3 in a row");
                AssertEqual(ModelAlertRuleStateEnum.Crossed, Rule(Group(Rate("escalations.inARow", 4, 4)), defaults, ModelAlertRuleEnum.EscalationsInARow).State, "4 in a row");
                AssertEqual(ModelAlertRuleStateEnum.Clear, Rule(Group(Rate("repairs.protocol", 3, 10)), defaults, ModelAlertRuleEnum.ProtocolRepairRate).State, "30% is at the threshold");
                AssertEqual(ModelAlertRuleStateEnum.Crossed, Rule(Group(Rate("repairs.protocol", 4, 10)), defaults, ModelAlertRuleEnum.ProtocolRepairRate).State, "40% is above");
                AssertEqual(ModelAlertRuleStateEnum.MinimumNotMet, Rule(Group(Rate("repairs.protocol", 7, 7)), defaults, ModelAlertRuleEnum.ProtocolRepairRate).State, "7 attempts");
                ModelAlertTarget target = new ModelAlertTarget { ProfileId = "mdl_x", Model = ModelName, Persona = "Worker", RevisionGroup = "mdv_a", VesselName = "Armada", VesselId = "vsl_a", Missions = 11, Attempts = 11 };
                ModelAlertRuleResult repairs = Rule(Group(Rate("repairs.protocol", 4, 10)), defaults, ModelAlertRuleEnum.ProtocolRepairRate);
                AssertEqual(ModelName + " as Worker on Armada: protocol repairs 4 of 10 attempts (40%, 95% CI 17-69%)", ModelAlertRules.Title(target, repairs), "title with counts and interval");
                AssertContains("compact template", ModelAlertRules.Detail(target, repairs), "detail suggests the compact template");
                return Task.CompletedTask;
            }));

            cases.Add(Case("cost_median_rule", "Cost per accepted result: above the multiple of the median of other profiles crosses; at least 5 accepted results are needed on each side", () =>
            {
                ModelAlertThreshold defaults = new ModelAlertThresholds().EffectiveFor("Worker");
                List<double> others = new List<double> { 100, 120, 500 };
                AssertEqual((double?)120, ModelAlertRules.Median(others), "odd median");
                AssertEqual((double?)110, ModelAlertRules.Median(new List<double> { 120, 100 }), "even median");
                AssertNull(ModelAlertRules.Median(new List<double>()), "no median");
                ModelAlertRuleResult high = Rule(Group(Cost(241, 5, 5)), defaults, ModelAlertRuleEnum.CostPerAcceptedResult, others);
                AssertEqual(ModelAlertRuleStateEnum.Crossed, high.State, "241 > 2 x 120");
                AssertEqual((double?)120, high.Median, "median");
                AssertEqual((double?)240, high.Threshold, "threshold");
                AssertEqual((int?)3, high.MedianOf, "compared groups");
                AssertEqual(ModelAlertRuleStateEnum.Clear, Rule(Group(Cost(240, 5, 5)), defaults, ModelAlertRuleEnum.CostPerAcceptedResult, others).State, "240 is at 2 x 120");
                AssertEqual(ModelAlertRuleStateEnum.MinimumNotMet, Rule(Group(Cost(900, 4, 6)), defaults, ModelAlertRuleEnum.CostPerAcceptedResult, others).State, "4 accepted results with usage");
                AssertEqual(ModelAlertRuleStateEnum.MinimumNotMet, Rule(Group(Cost(900, 5, 5)), defaults, ModelAlertRuleEnum.CostPerAcceptedResult, new List<double>()).State, "no other profile to compare");
                ModelScorecardMetric none = new ModelScorecardMetric { Key = "cost.tokensPerAcceptedResult", Available = false, UnavailableReason = "No token usage is recorded for the accepted results.", Count = 0, Of = 6 };
                AssertEqual(ModelAlertRuleStateEnum.Unavailable, Rule(Group(none), defaults, ModelAlertRuleEnum.CostPerAcceptedResult, others).State, "no usage recorded");
                AssertNull(ModelAlertRules.QualifyingCost(Group(Cost(300, 4, 4)), 5), "too few accepted to be compared");
                AssertEqual((double?)300, ModelAlertRules.QualifyingCost(Group(Cost(300, 5, 5)), 5), "compared");
                return Task.CompletedTask;
            }));

            cases.Add(Case("thresholds_overrides_and_validation", "A persona override beats the configured default, which beats the built-in default; a minimally deserialized body gives the built-ins; invalid values are rejected", () =>
            {
                ModelAlertThresholds empty = JsonSerializer.Deserialize<ModelAlertThresholds>("{}")!;
                ModelAlertThreshold builtIn = empty.EffectiveFor("Worker");
                AssertEqual((double?)0.5, builtIn.FirstPassMinRate, "built-in rate");
                AssertEqual((int?)8, builtIn.FirstPassMinMissions, "built-in minimum");
                AssertEqual((int?)3, builtIn.EscalationsInARow, "built-in streak");
                AssertEqual((double?)0.3, builtIn.ProtocolRepairMaxRate, "built-in repair rate");
                AssertEqual((int?)8, builtIn.ProtocolRepairMinAttempts, "built-in repair minimum");
                AssertEqual((double?)2.0, builtIn.CostMedianMultiplier, "built-in multiplier");
                AssertEqual((int?)5, builtIn.CostMinAccepted, "built-in accepted minimum");
                AssertNull(empty.Validate(), "empty is valid");

                ModelAlertThresholds configured = new ModelAlertThresholds
                {
                    Defaults = new ModelAlertThreshold { FirstPassMinRate = 0.6 },
                    Overrides = new List<ModelAlertThreshold> { new ModelAlertThreshold { Persona = "Worker", FirstPassMinRate = 0.3, FirstPassMinMissions = 4 } }
                };
                AssertEqual((double?)0.3, configured.EffectiveFor("worker").FirstPassMinRate, "override beats default (case-insensitive persona)");
                AssertEqual((int?)4, configured.EffectiveFor("Worker").FirstPassMinMissions, "override minimum");
                AssertEqual((double?)0.6, configured.EffectiveFor("Judge").FirstPassMinRate, "configured default for another persona");
                AssertEqual((int?)3, configured.EffectiveFor("Worker").EscalationsInARow, "unset override value keeps the built-in");
                ModelScorecardGroup group = Group(Rate("firstPassSuccess", 5, 10));
                AssertEqual(ModelAlertRuleStateEnum.Crossed, Rule(group, configured.EffectiveFor("Judge"), ModelAlertRuleEnum.FirstPassSuccess).State, "50% below the configured 60%");
                AssertEqual(ModelAlertRuleStateEnum.Clear, Rule(group, configured.EffectiveFor("Worker"), ModelAlertRuleEnum.FirstPassSuccess).State, "50% above the override's 30%");

                AssertNotNull(new ModelAlertThresholds { Defaults = new ModelAlertThreshold { FirstPassMinRate = 1.5 } }.Validate(), "rate above 1");
                AssertNotNull(new ModelAlertThresholds { Defaults = new ModelAlertThreshold { CostMinAccepted = 0 } }.Validate(), "minimum 0");
                AssertNotNull(new ModelAlertThresholds { Overrides = new List<ModelAlertThreshold> { new ModelAlertThreshold() } }.Validate(), "override without persona");
                AssertNotNull(new ModelAlertThresholds { Overrides = new List<ModelAlertThreshold> { new ModelAlertThreshold { Persona = "Worker" }, new ModelAlertThreshold { Persona = "worker" } } }.Validate(), "duplicate persona");
                return Task.CompletedTask;
            }));

            cases.Add(DbCase("raise_dedupe_resolve_reopen", "A crossed condition raises one alert; later passes keep the same row, resolve it when the condition clears and reopen the same row when it returns; nothing else changes", async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                ModelProfile profile = await CreateProfileAsync(db, Tenant).ConfigureAwait(false);
                Persona persona = await db.Personas.ReadByNameAsync("Worker").ConfigureAwait(false) ?? await CreatePersonaAsync(db, "Worker", null).ConfigureAwait(false);
                for (int i = 0; i < 8; i++)
                    await db.StageOutcomes.CreateAsync(Outcome("msn_a" + i, i < 3, 2 + i * 0.1)).ConfigureAwait(false);

                List<ModelAlert> pushed = new List<ModelAlert>();
                ModelAlertSettings settings = new ModelAlertSettings();
                ModelAlertService service = new ModelAlertService(db, Quiet(), () => settings) { AlertRaised = a => pushed.Add(a) };

                ModelAlertEvaluation first = await service.EvaluateAsync(null, _Now).ConfigureAwait(false);
                AssertEqual(1, first.Pairs, "one (profile, persona) pair");
                AssertEqual(1, first.Raised.Count, "one alert raised");
                ModelAlert raised = first.Raised[0];
                AssertEqual(ModelAlertRuleEnum.FirstPassSuccess, raised.Rule, "rule");
                AssertEqual(Tenant, raised.TenantId, "tenant of the profile");
                AssertEqual("Worker", raised.Persona, "persona");
                AssertNull(raised.VesselId, "all vessels");
                AssertStartsWith(ModelName + " as Worker on all vessels: first pass 3 of 8 (38%, 95% CI ", raised.Title, "title with counts");
                AssertEqual(1, pushed.Count, "one notification");
                ModelAlertTarget target = first.Targets.Single();
                AssertEqual(ModelAlertRuleStateEnum.MinimumNotMet, target.Rules.Single(r => r.Rule == ModelAlertRuleEnum.CostPerAcceptedResult).State, "cost rule reports its minimum");
                AssertEqual(ModelAlertRuleStateEnum.Clear, target.Rules.Single(r => r.Rule == ModelAlertRuleEnum.ProtocolRepairRate).State, "no repairs");

                ModelAlertEvaluation second = await service.EvaluateAsync(null, _Now.AddMinutes(60)).ConfigureAwait(false);
                AssertEqual(0, second.Raised.Count, "no repeated alert");
                AssertEqual(1, second.StillOpen, "kept open");
                AssertEqual(1, pushed.Count, "no repeated notification");
                EnumerationResult<ModelAlert> all = await db.ModelAlerts.EnumerateAsync(new ModelAlertQuery()).ConfigureAwait(false);
                AssertEqual(1L, all.TotalRecords, "one row for the condition");

                for (int i = 0; i < 6; i++)
                    await db.StageOutcomes.CreateAsync(Outcome("msn_b" + i, true, 1 + i * 0.1)).ConfigureAwait(false);
                ModelAlertEvaluation third = await service.EvaluateAsync(null, _Now.AddMinutes(120)).ConfigureAwait(false);
                AssertEqual(1, third.Resolved, "9 of 14 clears the condition");
                ModelAlert resolved = (await db.ModelAlerts.ReadAsync(raised.Id).ConfigureAwait(false))!;
                AssertEqual(ModelAlertStatusEnum.Resolved, resolved.Status, "resolved");
                AssertNotNull(resolved.ResolvedUtc, "resolved time");

                for (int i = 0; i < 12; i++)
                    await db.StageOutcomes.CreateAsync(Outcome("msn_c" + i, false, 0.5 + i * 0.01)).ConfigureAwait(false);
                ModelAlertEvaluation fourth = await service.EvaluateAsync(null, _Now.AddMinutes(180)).ConfigureAwait(false);
                AssertEqual(1, fourth.Raised.Count, "reopened");
                AssertEqual(raised.Id, fourth.Raised[0].Id, "the same row is reopened");
                AssertEqual(ModelAlertStatusEnum.Open, fourth.Raised[0].Status, "open again");
                AssertNull(fourth.Raised[0].ResolvedUtc, "resolved time cleared");
                AssertEqual(2, pushed.Count, "reopen notifies again");
                AssertEqual(1L, (await db.ModelAlerts.EnumerateAsync(new ModelAlertQuery()).ConfigureAwait(false)).TotalRecords, "still one row");

                Persona after = (await db.Personas.ReadByNameAsync("Worker").ConfigureAwait(false))!;
                AssertEqual(Priorities(persona), Priorities(after), "evaluation never changes persona priorities");

                ModelAlertEvaluation otherTenant = await service.EvaluateAsync(OtherTenant, _Now.AddMinutes(240)).ConfigureAwait(false);
                AssertEqual(0, otherTenant.Pairs, "another tenant has no pairs");
                AssertEqual(0, otherTenant.Resolved, "evaluating another tenant does not resolve this tenant's alerts");
            }));

            cases.Add(DbCase("streak_metric_and_per_vessel", "The scorecard reports escalations in a row; three escalations in a row raise their own alert, and IncludeVessels adds a per-vessel alert", async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                ModelProfile profile = await CreateProfileAsync(db, Tenant).ConfigureAwait(false);
                await db.StageOutcomes.CreateAsync(Outcome("msn_s0", true, 3)).ConfigureAwait(false);
                for (int i = 0; i < 3; i++)
                    await db.StageOutcomes.CreateAsync(Outcome("msn_s" + (i + 1), false, 2 - i * 0.5, o => o.Disposition = StageDispositionEnum.Escalated)).ConfigureAwait(false);

                ModelScorecard card = await new ModelScorecardService(db, Quiet()).ComputeAsync(profile, new ModelScorecardRequest { Persona = "Worker" }, _Now).ConfigureAwait(false);
                ModelScorecardMetric streak = card.Groups.Single().Metrics.Single(m => m.Key == "escalations.inARow");
                AssertEqual((int?)3, streak.Count, "three newest attempts escalated");
                AssertEqual((int?)4, streak.Of, "of four attempts");

                ModelAlertSettings settings = new ModelAlertSettings { IncludeVessels = true };
                ModelAlertEvaluation evaluation = await new ModelAlertService(db, Quiet(), () => settings).EvaluateAsync(Tenant, _Now).ConfigureAwait(false);
                List<ModelAlert> streaks = evaluation.Raised.Where(a => a.Rule == ModelAlertRuleEnum.EscalationsInARow).ToList();
                AssertEqual(2, streaks.Count, "all vessels and the one vessel");
                AssertEqual(1, streaks.Count(a => a.VesselId == "vsl_ma"), "per-vessel alert");
                AssertEqual(1, streaks.Count(a => a.VesselId == null), "all-vessels alert");
                AssertContains("3 escalations away in a row", streaks[0].Title, "title");
                AssertEqual(0, evaluation.Raised.Count(a => a.Rule == ModelAlertRuleEnum.FirstPassSuccess), "4 missions is below the first-pass minimum");
            }));

            cases.Add(DbCase("ignore_expiry_and_inbox", "Open alerts show in the inbox for admins of their tenant only; Ignore hides one for N days (default 7) and it shows again once the time passes; days out of range are rejected", async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                ModelProfile profile = await CreateProfileAsync(db, Tenant).ConfigureAwait(false);
                ModelAlert alert = await CreateAlertAsync(db, profile, "Worker", ModelAlertRuleEnum.FirstPassSuccess).ConfigureAwait(false);
                ArmadaSettings settings = new ArmadaSettings();
                InboxService inbox = new InboxService(db, Quiet(), settings);
                AuthContext admin = AuthContext.Authenticated(Tenant, "usr_admin", false, true, "Test");
                AuthContext member = AuthContext.Authenticated(Tenant, "usr_member", false, false, "Test");
                AuthContext otherAdmin = AuthContext.Authenticated(OtherTenant, "usr_other", false, true, "Test");
                string[] kinds = new string[] { InboxItemKinds.ModelAlert };

                AssertEqual(1L, await inbox.CountAsync(admin, kinds).ConfigureAwait(false), "tenant admin sees the alert");
                AssertEqual(0L, await inbox.CountAsync(member, kinds).ConfigureAwait(false), "a member cannot apply actions, so the item is not shown");
                AssertEqual(0L, await inbox.CountAsync(otherAdmin, kinds).ConfigureAwait(false), "another tenant's admin");
                EnumerationResult<InboxItem> page = await inbox.EnumerateAsync(admin, new EnumerationQuery { PageSize = 50 }).ConfigureAwait(false);
                InboxItem item = page.Objects.Single(i => i.Kind == InboxItemKinds.ModelAlert);
                AssertEqual(alert.Id, item.EntityId, "entity");
                AssertNotNull(item.ModelAlert, "alert attached");
                AssertEqual(5, item.ModelAlert!.SuggestedActions.Count, "five suggested actions");

                ModelAlertAccessService access = Access(db, new ModelAlertSettings(), null);
                ModelAlertApplyResult ignored = await access.IgnoreAsync(admin, alert.Id, null).ConfigureAwait(false);
                AssertTrue(ignored.Alert.IgnoredUntilUtc.HasValue, "ignored until");
                double days = (ignored.Alert.IgnoredUntilUtc!.Value - ignored.Record.AppliedUtc).TotalDays;
                AssertTrue(Math.Abs(days - 7) < 0.001, "default 7 days, got " + days);
                AssertEqual(ModelAlertActionEnum.Ignore, ignored.Alert.Actions.Single().Action, "recorded");
                AssertEqual(0L, await inbox.CountAsync(admin, kinds).ConfigureAwait(false), "hidden while ignored");
                AssertEqual(1L, (await access.ListAsync(admin, new ModelAlertQuery()).ConfigureAwait(false)).TotalRecords, "the list includes ignored alerts by default");
                AssertEqual(0L, (await access.ListAsync(admin, new ModelAlertQuery { IncludeIgnored = false }).ConfigureAwait(false)).TotalRecords, "unless asked not to");

                ModelAlert stored = (await db.ModelAlerts.ReadAsync(alert.Id).ConfigureAwait(false))!;
                stored.IgnoredUntilUtc = DateTime.UtcNow.AddMinutes(-1);
                await db.ModelAlerts.UpdateAsync(stored).ConfigureAwait(false);
                AssertEqual(1L, await inbox.CountAsync(admin, kinds).ConfigureAwait(false), "shows again once the ignore expires");

                ModelAlertApplyResult three = await access.IgnoreAsync(admin, alert.Id, 3).ConfigureAwait(false);
                AssertTrue(Math.Abs((three.Alert.IgnoredUntilUtc!.Value - three.Record.AppliedUtc).TotalDays - 3) < 0.001, "explicit days");
                await AssertThrowsAsync<ArgumentException>(() => access.IgnoreAsync(admin, alert.Id, 0), "0 days").ConfigureAwait(false);
                await AssertThrowsAsync<ArgumentException>(() => access.IgnoreAsync(admin, alert.Id, 366), "366 days").ConfigureAwait(false);
            }));

            cases.Add(DbCase("suggested_actions_apply", "Suggested actions change nothing until applied; MoveDown, RemoveFromPersona and Reevaluate go through the persona and Evaluate services and are recorded; CompactTemplate is unavailable with the reason", async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                ModelProfile profile = await CreateProfileAsync(db, Tenant).ConfigureAwait(false);
                Captain small = await db.Captains.CreateAsync(new Captain { Name = "small", Runtime = AgentRuntimeEnum.Mux, Model = ModelName, TenantId = Tenant }).ConfigureAwait(false);
                await db.ModelProfiles.LinkCaptainAsync(small.Id, profile.Id).ConfigureAwait(false);
                await db.Captains.CreateAsync(new Captain { Name = "big", Runtime = AgentRuntimeEnum.ClaudeCode, Model = "claude-opus", TenantId = Tenant }).ConfigureAwait(false);
                Persona persona = await CreatePersonaAsync(db, "AlertWorker", small.Id).ConfigureAwait(false);
                ModelAlert alert = await CreateAlertAsync(db, profile, "AlertWorker", ModelAlertRuleEnum.ProtocolRepairRate).ConfigureAwait(false);
                AuthContext admin = AuthContext.Authenticated(Tenant, "usr_admin", false, true, "Test");
                AuthContext member = AuthContext.Authenticated(Tenant, "usr_member", false, false, "Test");

                LoggingModule logging = Quiet();
                ModelProbeService probes = new ModelProbeService(db, logging, new CannedModelProbeExecutor(), () => new EvaluationSettings());
                ModelProbeAccessService probeAccess = new ModelProbeAccessService(db, probes, new ModelEndpointService(db, logging), logging);
                ModelAlertAccessService access = Access(db, new ModelAlertSettings(), probeAccess);

                ModelAlert read = await access.GetAsync(member, alert.Id).ConfigureAwait(false);
                AssertTrue(Suggested(read, ModelAlertActionEnum.MoveDown).Available, "move down available");
                AssertTrue(Suggested(read, ModelAlertActionEnum.RemoveFromPersona).Available, "remove available");
                AssertTrue(Suggested(read, ModelAlertActionEnum.Reevaluate).Available, "re-evaluate available");
                AssertTrue(Suggested(read, ModelAlertActionEnum.Ignore).Available, "ignore available");
                ModelAlertSuggestedAction compact = Suggested(read, ModelAlertActionEnum.CompactTemplate);
                AssertFalse(compact.Available, "compact template unavailable");
                AssertTrue(compact.Recommended, "recommended for protocol repairs");
                AssertContains("no prompt variant per model profile and persona", compact.UnavailableReason ?? "", "reason");
                await access.ListAsync(admin, null).ConfigureAwait(false);
                Persona untouched = (await db.Personas.ReadByNameAsync("AlertWorker").ConfigureAwait(false))!;
                AssertEqual(Priorities(persona), Priorities(untouched), "reading alerts changes nothing");
                AssertEqual(small.Id, untouched.DefaultCaptainId, "default captain unchanged");
                AssertEqual(0, (await db.ModelAlerts.ReadAsync(alert.Id).ConfigureAwait(false))!.Actions.Count, "nothing recorded");
                AssertEqual(0L, (await db.ModelProbeRuns.EnumerateAsync(new ModelProbeRunQuery()).ConfigureAwait(false)).TotalRecords, "no Evaluate started");

                await AssertThrowsAsync<UnauthorizedAccessException>(() => access.ApplyAsync(member, alert.Id, new ModelAlertApplyRequest { Action = ModelAlertActionEnum.MoveDown }), "members cannot apply").ConfigureAwait(false);
                await AssertThrowsAsync<ArgumentException>(() => access.ApplyAsync(admin, alert.Id, new ModelAlertApplyRequest()), "action required").ConfigureAwait(false);
                await AssertThrowsAsync<ArgumentException>(() => access.ApplyAsync(admin, alert.Id, new ModelAlertApplyRequest { Action = ModelAlertActionEnum.CompactTemplate }), "compact unavailable").ConfigureAwait(false);
                AssertEqual(Priorities(persona), Priorities((await db.Personas.ReadByNameAsync("AlertWorker").ConfigureAwait(false))!), "rejected applies change nothing");

                ModelAlertApplyResult moved = await access.ApplyAsync(admin, alert.Id, new ModelAlertApplyRequest { Action = ModelAlertActionEnum.MoveDown }).ConfigureAwait(false);
                Persona afterMove = (await db.Personas.ReadByNameAsync("AlertWorker").ConfigureAwait(false))!;
                AssertEqual("claude-opus|*ClaudeCode|" + ModelName, Priorities(afterMove), "the profile's entry moved to the bottom");
                AssertTrue(moved.Record.Changed, "changed");
                AssertEqual("AlertWorker", moved.Record.TargetId, "target persona");
                AssertEqual("usr_admin", moved.Record.UserId, "who");
                AssertFalse(Suggested(moved.Alert, ModelAlertActionEnum.MoveDown).Available, "already at the bottom");

                ModelAlertApplyResult removed = await access.ApplyAsync(admin, alert.Id, new ModelAlertApplyRequest { Action = ModelAlertActionEnum.RemoveFromPersona }).ConfigureAwait(false);
                Persona afterRemove = (await db.Personas.ReadByNameAsync("AlertWorker").ConfigureAwait(false))!;
                AssertEqual("claude-opus|*ClaudeCode", Priorities(afterRemove), "the profile's entry removed, others kept");
                AssertNull(afterRemove.DefaultCaptainId, "default captain on the profile cleared");
                AssertContains("cleared the default captain", removed.Record.Result, "result");
                AssertFalse(Suggested(removed.Alert, ModelAlertActionEnum.RemoveFromPersona).Available, "nothing left to remove");

                ModelAlertApplyResult evaluated = await access.ApplyAsync(admin, alert.Id, new ModelAlertApplyRequest { Action = ModelAlertActionEnum.Reevaluate }).ConfigureAwait(false);
                AssertStartsWith(Constants.ModelProbeRunIdPrefix, evaluated.Record.TargetId ?? "", "Evaluate run started");
                ModelProbeRun? run = await probes.WaitAsync(evaluated.Record.TargetId!).ConfigureAwait(false);
                AssertNotNull(run, "run persisted");
                AssertEqual(small.Id, run!.CaptainId, "a captain on the profile");
                AssertEqual(ModelProbeDepthEnum.Quick, run.Depth, "Quick");

                ModelAlert final = (await db.ModelAlerts.ReadAsync(alert.Id).ConfigureAwait(false))!;
                AssertEqual("MoveDown,RemoveFromPersona,Reevaluate", String.Join(",", final.Actions.Select(a => a.Action.ToString())), "every applied action recorded in order");
            }));

            cases.Add(DbCase("tenant_scoping", "Alerts of another tenant are not found (before the request is validated); thresholds are read by anyone and changed only by a global admin", async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                ModelProfile profile = await CreateProfileAsync(db, Tenant).ConfigureAwait(false);
                ModelAlert alert = await CreateAlertAsync(db, profile, "Worker", ModelAlertRuleEnum.FirstPassSuccess).ConfigureAwait(false);
                AuthContext otherAdmin = AuthContext.Authenticated(OtherTenant, "usr_other", false, true, "Test");
                AuthContext admin = AuthContext.Authenticated(Tenant, "usr_admin", false, true, "Test");
                AuthContext global = AuthContext.Authenticated(OtherTenant, "usr_global", true, false, "Test");
                int saves = 0;
                ModelAlertSettings settings = new ModelAlertSettings();
                ModelAlertAccessService access = new ModelAlertAccessService(db, new ModelAlertService(db, Quiet(), () => settings), () => settings, () => { saves++; return Task.CompletedTask; }, null, Quiet());

                AssertEqual(0L, (await access.ListAsync(otherAdmin, new ModelAlertQuery { TenantId = Tenant }).ConfigureAwait(false)).TotalRecords, "tenant filter ignored for non-global callers");
                await AssertThrowsAsync<KeyNotFoundException>(() => access.GetAsync(otherAdmin, alert.Id), "get").ConfigureAwait(false);
                await AssertThrowsAsync<KeyNotFoundException>(() => access.ApplyAsync(otherAdmin, alert.Id, null), "apply before validation").ConfigureAwait(false);
                await AssertThrowsAsync<KeyNotFoundException>(() => access.IgnoreAsync(otherAdmin, alert.Id, 0), "ignore before validation").ConfigureAwait(false);
                AssertEqual(1L, (await access.ListAsync(global, null).ConfigureAwait(false)).TotalRecords, "a global admin sees every tenant");

                AssertEqual((double?)0.5, access.GetThresholds(admin).Defaults.FirstPassMinRate, "defaults filled in");
                ModelAlertThresholds update = new ModelAlertThresholds { Overrides = new List<ModelAlertThreshold> { new ModelAlertThreshold { Persona = " Judge ", FirstPassMinRate = 0.7 } } };
                await AssertThrowsAsync<UnauthorizedAccessException>(() => access.UpdateThresholdsAsync(admin, update), "tenant admins cannot change server settings").ConfigureAwait(false);
                await AssertThrowsAsync<ArgumentException>(() => access.UpdateThresholdsAsync(global, new ModelAlertThresholds { Defaults = new ModelAlertThreshold { EscalationsInARow = 0 } }), "invalid").ConfigureAwait(false);
                AssertEqual(0, saves, "nothing saved yet");
                ModelAlertThresholds stored = await access.UpdateThresholdsAsync(global, update).ConfigureAwait(false);
                AssertEqual(1, saves, "settings saved");
                AssertEqual("Judge", stored.Overrides.Single().Persona, "persona trimmed");
                AssertEqual((double?)0.7, settings.Thresholds.EffectiveFor("judge").FirstPassMinRate, "applied to the live settings");
                await AssertThrowsAsync<UnauthorizedAccessException>(() => access.EvaluateAsync(AuthContext.Authenticated(Tenant, "usr_member", false, false, "Test")), "members cannot evaluate").ConfigureAwait(false);
            }));

            cases.Add(Case("push_occurrence", "A model alert push goes to the tenant's admins with the alert title, under the ModelAlert category", () =>
            {
                ModelAlert alert = new ModelAlert { TenantId = Tenant, Title = ModelName + " as Worker on all vessels: first pass 3 of 8" };
                PushOccurrence occurrence = PushNotificationService.FromModelAlert(alert);
                AssertEqual(PushCategoryEnum.ModelAlert, occurrence.Category, "category");
                AssertEqual("model_alert", occurrence.Kind, "kind");
                AssertEqual(Tenant, occurrence.TenantId, "tenant");
                AssertNull(occurrence.OwnerUserId, "no owner, so the tenant's admins receive it");
                AssertContains("first pass 3 of 8", occurrence.Body, "body");
                AssertFalse(new ModelAlertSettings().PushEnabled, "off by default");
                AssertTrue(PushSettings.AllCategories().Contains(PushCategoryEnum.ModelAlert), "a known category");
                return Task.CompletedTask;
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Services: model alerts",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static ModelAlertRuleResult Rule(ModelScorecardGroup group, ModelAlertThreshold thresholds, ModelAlertRuleEnum rule, List<double>? others = null)
        {
            return ModelAlertRules.Evaluate(group, thresholds, others ?? new List<double>()).Single(r => r.Rule == rule);
        }

        private static ModelScorecardGroup Group(params ModelScorecardMetric[] metrics)
        {
            return new ModelScorecardGroup { RevisionIds = new List<string> { "mdv_a" }, Missions = 10, Attempts = 10, Metrics = metrics.ToList() };
        }

        private static ModelScorecardMetric Rate(string key, int count, int of)
        {
            return new ModelScorecardMetric
            {
                Key = key,
                Count = count,
                Of = of,
                Rate = of == 0 ? null : Math.Round((double)count / of, 4),
                Lower = WilsonScoreInterval.Lower(count, of),
                Upper = WilsonScoreInterval.Upper(count, of)
            };
        }

        private static ModelScorecardMetric Cost(double value, int counted, int of)
        {
            return new ModelScorecardMetric { Key = "cost.tokensPerAcceptedResult", Unit = "tokens", Count = counted, Of = of, Value = value, Total = (long)(value * counted) };
        }

        private static ModelAlertSuggestedAction Suggested(ModelAlert alert, ModelAlertActionEnum action)
        {
            return alert.SuggestedActions.Single(a => a.Action == action);
        }

        private static string Priorities(Persona persona)
        {
            return String.Join("|", persona.CaptainPriorities.Select(p => p.Model ?? ("*" + p.Runtime)));
        }

        private static ModelAlertAccessService Access(DatabaseDriver db, ModelAlertSettings settings, ModelProbeAccessService? probes)
        {
            return new ModelAlertAccessService(db, new ModelAlertService(db, Quiet(), () => settings), () => settings, () => Task.CompletedTask, probes, Quiet());
        }

        private static async Task<ModelProfile> CreateProfileAsync(DatabaseDriver db, string tenantId)
        {
            foreach (string id in new string[] { Tenant, OtherTenant })
            {
                if (await db.Tenants.ReadAsync(id).ConfigureAwait(false) == null)
                    await db.Tenants.CreateAsync(new TenantMetadata { Id = id, Name = id }).ConfigureAwait(false);
            }
            ModelProfile profile = new ModelProfile
            {
                TenantId = tenantId,
                Identity = new ModelProfileIdentity { Runtime = "Mux", Provider = "Ollama", Model = ModelName, ServedContextTokens = 131072 }
            };
            profile = await db.ModelProfiles.CreateAsync(profile, ModelProfileRules.IdentityKey(tenantId, profile.Identity)).ConfigureAwait(false);
            await db.ModelProfiles.CreateRevisionAsync(new ModelProfileRevision
            {
                ProfileId = profile.Id,
                TenantId = tenantId,
                RuntimeVersion = "1.4.1",
                ModelDigest = "sha256:aaa",
                FirstSeenUtc = _Now.AddDays(-100)
            }).ConfigureAwait(false);
            return profile;
        }

        private static async Task<Persona> CreatePersonaAsync(DatabaseDriver db, string name, string? defaultCaptainId)
        {
            Persona persona = new Persona(name, "persona.worker")
            {
                TenantId = Tenant,
                DefaultCaptainId = defaultCaptainId,
                CaptainPriorities = new List<PersonaCaptainPriority>
                {
                    new PersonaCaptainPriority { Model = "claude-opus" },
                    new PersonaCaptainPriority { Model = ModelName },
                    new PersonaCaptainPriority { Runtime = AgentRuntimeEnum.ClaudeCode }
                }
            };
            return await db.Personas.CreateAsync(persona).ConfigureAwait(false);
        }

        private static async Task<ModelAlert> CreateAlertAsync(DatabaseDriver db, ModelProfile profile, string persona, ModelAlertRuleEnum rule)
        {
            ModelAlert alert = new ModelAlert
            {
                TenantId = profile.TenantId,
                ProfileId = profile.Id,
                Model = ModelName,
                RevisionGroup = "mdv_a",
                Persona = persona,
                Rule = rule,
                Title = ModelName + " as " + persona + " on all vessels: test",
                Detail = "test",
                CreatedUtc = _Now,
                LastRaisedUtc = _Now,
                LastEvaluatedUtc = _Now
            };
            return await db.ModelAlerts.CreateAsync(alert, ModelAlertRules.Key(alert.TenantId, alert.ProfileId, alert.RevisionGroup, persona, null, rule)).ConfigureAwait(false);
        }

        private static StageOutcome Outcome(string missionId, bool passed, double daysAgo, Action<StageOutcome>? change = null)
        {
            DateTime ended = _Now.AddDays(-daysAgo);
            StageOutcome outcome = new StageOutcome
            {
                TenantId = Tenant,
                MissionId = missionId,
                VesselId = "vsl_ma",
                CaptainId = "cpt_small",
                Persona = PersonaCatalog.Worker,
                Attempt = 1,
                Runtime = AgentRuntimeEnum.Mux,
                ModelProvider = "Ollama",
                Model = ModelName,
                ContextTokens = 131072,
                RuntimeVersion = "1.4.1",
                ModelDigest = "sha256:aaa",
                Disposition = passed ? StageDispositionEnum.Completed : StageDispositionEnum.Failed,
                StageCheckStatus = passed ? StageCheckStatusEnum.Passed : StageCheckStatusEnum.Failed,
                StartedUtc = ended.AddMinutes(-5),
                EndedUtc = ended,
                CreatedUtc = ended,
                LastUpdateUtc = ended
            };
            change?.Invoke(outcome);
            return outcome;
        }

        private static void AssertContains(string expected, string actual, string label)
        {
            AssertTrue(actual.Contains(expected, StringComparison.Ordinal), label + ": expected '" + expected + "' in '" + actual + "'");
        }

        private static void AssertStartsWith(string expected, string actual, string label)
        {
            AssertTrue(actual.StartsWith(expected, StringComparison.Ordinal), label + ": expected '" + actual + "' to start with '" + expected + "'");
        }

        private static async Task AssertThrowsAsync<T>(Func<Task> action, string label) where T : Exception
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (T)
            {
                return;
            }
            throw new AssertionException(label + ": expected " + typeof(T).Name);
        }

        private static LoggingModule Quiet()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { TestTags.Positive });
        }

        private static TestCaseDescriptor DbCase(string caseId, string displayName, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { TestTags.Positive, TestTags.Database });
        }

        #endregion
    }
}

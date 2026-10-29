namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// BlackBoxRuleCatalog and BlackBoxAdvisor: a golden case per rule (finding, triage, recovery paths with stable ids and
    /// runnable actions, no unfilled placeholders), catalog coverage, triage ties, placeholders, the empty record,
    /// determinism, ranking, recommendations, the summary, pipeline registration and background re-analysis, and the brief
    /// audiences.
    /// </summary>
    public sealed class BlackBoxAdvisorSuite : IArmadaTestSuite
    {
        private const string SuiteId = "Services.BlackBoxAdvisor";
        private static readonly Regex _Unfilled = new Regex(@"\{[A-Za-z][A-Za-z0-9:.]*(\|[^{}]*)?\}", RegexOptions.CultureInvariant);

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            foreach (BlackBoxGoldenCase golden in BlackBoxGoldenCases())
            {
                BlackBoxGoldenCase g = golden;
                cases.Add(Case("golden_" + g.RuleId.ToLowerInvariant().Replace('-', '_'), "Rule " + g.RuleId + " fires with its finding, triage " + g.Verdict + " and runnable recovery paths", TestTags.Positive, () =>
                {
                    BlackBoxRecord record = g.Build();
                    List<string> diagnostics = new List<string>();
                    BlackBoxAnalysis a = BlackBoxAdvisor.Analyze(record, BlackBoxRuleCatalog.Default, diagnostics);
                    AssertTrue(a.Why.Any(w => w.RuleId == g.RuleId), g.RuleId + " fired; got " + String.Join(", ", a.Why.Select(w => w.RuleId)) + "; diagnostics: " + String.Join(" | ", diagnostics));
                    AssertEqual(g.Verdict, a.Triage!.Verdict, g.RuleId + " triage");
                    foreach (string also in g.AlsoFires) AssertTrue(a.Why.Any(w => w.RuleId == also), g.RuleId + ": " + also + " also fires");
                    foreach (string not in g.DoesNotFire) AssertFalse(a.Why.Any(w => w.RuleId == not), g.RuleId + ": " + not + " steps aside");
                    if (g.Statement != null) AssertEqual(g.Statement, a.Why.First(w => w.RuleId == g.RuleId).Statement, g.RuleId + " statement");
                    if (g.FirstPathId != null) AssertEqual(g.FirstPathId, a.RecoveryPaths.FirstOrDefault()?.Id, g.RuleId + " first recovery path");
                    AssertNoUnfilled(a, g.RuleId);
                    foreach (BlackBoxRecoveryPath p in a.RecoveryPaths)
                    {
                        AssertTrue(p.Id.StartsWith("bbx-", StringComparison.Ordinal) && p.Id.Contains('.'), "stable path id " + p.Id);
                        AssertTrue(p.Rank >= 1 && p.Rank <= BlackBoxLimits.MaxRecoveryPaths, "rank");
                        foreach (BlackBoxRecoveryStep st in p.Steps.Where(s => s.Action != null))
                        {
                            AssertTrue(BlackBoxActionCommands.IsSupported(st.Action!.Tool), "supported tool " + st.Action.Tool);
                            AssertStartsWith(st.Action.Tool + " {", st.McpCommand ?? "", "MCP command");
                            AssertNotNull(st.RestCommand, "REST command for " + st.Action.Tool);
                        }
                        if (p.Steps.Any(s => s.Action != null)) AssertNotNull(p.Action, "path action");
                    }
                    AssertTrue(a.Summary != null && a.Summary.Length > 20 && !a.Summary.Contains('—'), "summary");
                    AssertFalse(a.SummaryIsModelWritten, "rules summary");
                    return Task.CompletedTask;
                }));
            }

            cases.Add(Case("catalog_coverage", "The catalog is versioned, ids are unique, every phase has a rule, every plan starter rule exists and every action names a supported tool", TestTags.Positive, () =>
            {
                BlackBoxRuleCatalog catalog = BlackBoxRuleCatalog.Default;
                AssertTrue(catalog.RulesVersion >= 1, "rules version");
                AssertEqual(BlackBoxRuleCatalog.CurrentRulesVersion, new BlackBoxAdvisor().RulesVersion, "advisor reports the catalog version");
                AssertEqual(catalog.Rules.Count, catalog.Rules.Select(r => r.Id).Distinct().Count(), "unique ids");
                foreach (BlackBoxPhaseEnum phase in Enum.GetValues<BlackBoxPhaseEnum>().Where(p => p != BlackBoxPhaseEnum.Unknown))
                    AssertTrue(catalog.Rules.Any(r => r.Phase == phase), "a rule for phase " + phase);
                foreach (string id in new[] { "BBX-LAND-001", "BBX-LAND-002", "BBX-LAND-003", "BBX-LAND-004", "BBX-LAND-005", "BBX-MQ-001", "BBX-SCOPE-001", "BBX-SCOPE-002", "BBX-EXEC-001", "BBX-EXEC-002", "BBX-EXEC-003", "BBX-DEP-001", "BBX-INFRA-001", "BBX-NOOP-001", "BBX-UNKNOWN-001" })
                    AssertNotNull(catalog.Find(id), "starter rule " + id);
                AssertEqual(1, catalog.Rules.Count(r => r.IsFallback), "one fallback");
                foreach (BlackBoxRule rule in catalog.Rules)
                {
                    foreach (BlackBoxRulePath path in rule.Paths) foreach (BlackBoxRuleStep step in path.Steps.Where(s => s.Tool != null)) AssertTrue(BlackBoxActionCommands.IsSupported(step.Tool), rule.Id + " tool " + step.Tool);
                    foreach (BlackBoxRuleRecommendation rec in rule.Recommendations.Where(r => r.ApplyTool != null)) AssertTrue(BlackBoxActionCommands.IsSupported(rec.ApplyTool), rule.Id + " apply tool");
                    foreach (string name in Placeholders(rule)) AssertTrue(name.StartsWith("config:", StringComparison.Ordinal) || BlackBoxRulePlaceholders.Names.Contains(name), rule.Id + " uses known placeholder " + name);
                }
                AssertThrows<ArgumentException>(() => new BlackBoxRuleCatalog(1, new[] { new BlackBoxRule("X-1", "a", r => true), new BlackBoxRule("X-1", "b", r => true) }), "duplicate ids");
                AssertThrows<ArgumentOutOfRangeException>(() => new BlackBoxRuleCatalog(0, new List<BlackBoxRule>()), "version 0");
                return Task.CompletedTask;
            }));

            cases.Add(Case("triage_tie", "Equal top weights report Unknown with both candidates; a clear winner reports its confidence; no votes is Unknown and Low", TestTags.Negative, () =>
            {
                BlackBoxTriageVerdict tie = BlackBoxAdvisor.Tally(new[] { Vote("A", BlackBoxTriageEnum.UserConfig, 2), Vote("B", BlackBoxTriageEnum.ArmadaDefect, 2), Vote("C", BlackBoxTriageEnum.Transient, 1) });
                AssertEqual(BlackBoxTriageEnum.Unknown, tie.Verdict, "tie is Unknown");
                AssertEqual(2, tie.TiedWith.Count, "both candidates");
                AssertTrue(tie.TiedWith.Contains(BlackBoxTriageEnum.UserConfig) && tie.TiedWith.Contains(BlackBoxTriageEnum.ArmadaDefect), "candidates named");
                AssertEqual(BlackBoxConfidenceEnum.Low, tie.Confidence);
                AssertEqual(3, tie.Votes.Count, "votes kept");
                BlackBoxTriageVerdict sum = BlackBoxAdvisor.Tally(new[] { Vote("A", BlackBoxTriageEnum.UserConfig, 2), Vote("B", BlackBoxTriageEnum.UserConfig, 2), Vote("C", BlackBoxTriageEnum.ArmadaDefect, 3) });
                AssertEqual(BlackBoxTriageEnum.UserConfig, sum.Verdict, "weights add up per verdict");
                AssertEqual(BlackBoxConfidenceEnum.Medium, sum.Confidence, "close race is Medium");
                AssertEqual(BlackBoxConfidenceEnum.High, BlackBoxAdvisor.Tally(new[] { Vote("A", BlackBoxTriageEnum.ArmadaDefect, 4) }).Confidence, "strong single vote is High");
                BlackBoxTriageVerdict none = BlackBoxAdvisor.Tally(new[] { Vote("Z", BlackBoxTriageEnum.Unknown, 5), Vote("Y", BlackBoxTriageEnum.MissionBrief, 0) });
                AssertEqual(BlackBoxTriageEnum.Unknown, none.Verdict); AssertEqual(0, none.Votes.Count, "Unknown and zero-weight votes are ignored");

                BlackBoxRuleCatalog catalog = new BlackBoxRuleCatalog(1, new[]
                {
                    new BlackBoxRule("T-A", "a", r => true).Because("Config says so.", BlackBoxConfidenceEnum.High).Votes(BlackBoxTriageEnum.UserConfig, 3),
                    new BlackBoxRule("T-B", "b", r => true).Because("Armada says so.", BlackBoxConfidenceEnum.High).Votes(BlackBoxTriageEnum.ArmadaDefect, 3)
                });
                BlackBoxAnalysis a = BlackBoxAdvisor.Analyze(Rec(BlackBoxStepEnum.Push), catalog);
                AssertEqual(BlackBoxTriageEnum.Unknown, a.Triage!.Verdict, "record-level tie");
                AssertContains("tie between", a.Summary ?? "", "summary names the tie");
                return Task.CompletedTask;
            }));

            cases.Add(Case("placeholders", "A missing placeholder skips the rule and logs it (fallback text is used when given); optional components are left out; nothing unfilled is emitted", TestTags.Negative, () =>
            {
                BlackBoxRuleCatalog catalog = new BlackBoxRuleCatalog(1, new[]
                {
                    new BlackBoxRule("T-MISSING", "missing", r => true).Because("The checkout at {checkoutPath} is dirty.", BlackBoxConfidenceEnum.High).Votes(BlackBoxTriageEnum.UserConfig, 1),
                    new BlackBoxRule("T-UNKNOWN", "unknown name", r => true).Because("Value {noSuchField}.", BlackBoxConfidenceEnum.High),
                    new BlackBoxRule("T-FALLBACK", "fallback", r => true).Because("The checkout at {checkoutPath|the working directory} is dirty.", BlackBoxConfidenceEnum.Medium)
                        .Recover(new BlackBoxRulePath { Key = "optional", Title = "Restore {checkoutPath}", Optional = true, Steps = { BlackBoxRuleStep.Manual("git branch x {checkoutPath}") } })
                        .Recover(new BlackBoxRulePath { Key = "retry", Title = "Retry {missionId}", Steps = { BlackBoxRuleStep.Run("Retry {missionId}.", "retry_landing", "missionId", "{missionId}") } }),
                    new BlackBoxRule("T-REQUIRED-PATH", "required path", r => true).Because("Fine.", BlackBoxConfidenceEnum.Low)
                        .Recover(new BlackBoxRulePath { Key = "needs", Title = "Needs {checkoutPath}", Optional = false, Steps = { BlackBoxRuleStep.Manual("x") } }),
                    new BlackBoxRule("T-THROWS", "throws", r => throw new InvalidOperationException("boom")).Because("Never.", BlackBoxConfidenceEnum.Low),
                    new BlackBoxRule("T-FB", "fallback rule", r => true) { IsFallback = true }.Because("Nothing matched.", BlackBoxConfidenceEnum.Low)
                });
                BlackBoxRecord record = Rec(BlackBoxStepEnum.DirtyCheckout);
                List<string> diagnostics = new List<string>();
                BlackBoxAnalysis a = BlackBoxAdvisor.Analyze(record, catalog, diagnostics);
                AssertFalse(a.Why.Any(w => w.RuleId == "T-MISSING"), "rule with a missing placeholder skipped");
                AssertFalse(a.Why.Any(w => w.RuleId == "T-UNKNOWN"), "unknown placeholder skipped");
                AssertFalse(a.Why.Any(w => w.RuleId == "T-REQUIRED-PATH"), "a required component with a missing placeholder skips the rule");
                AssertFalse(a.Why.Any(w => w.RuleId == "T-THROWS"), "a throwing predicate is no match");
                AssertTrue(diagnostics.Any(d => d.Contains("T-MISSING") && d.Contains("checkoutPath")), "skip logged with the placeholder: " + String.Join(" | ", diagnostics));
                AssertTrue(diagnostics.Any(d => d.Contains("T-THROWS")), "throw logged");
                BlackBoxFinding fb = a.Why.Single(w => w.RuleId == "T-FALLBACK");
                AssertEqual("The checkout at the working directory is dirty.", fb.Statement, "fallback text");
                AssertEqual(1, a.RecoveryPaths.Count, "optional path left out, required one kept");
                AssertEqual("t-fallback.retry", a.RecoveryPaths[0].Id);
                AssertFalse(a.Why.Any(w => w.RuleId == "T-FB"), "fallback rule does not fire when another rule did");
                AssertNoUnfilled(a, "placeholders");
                AssertEqual(BlackBoxTriageEnum.Unknown, a.Triage!.Verdict, "skipped rule's vote is not counted");
                AssertNull(BlackBoxRuleTemplate.Render("{checkoutPath}", record), "render returns null for a missing value");
                AssertEqual("x", BlackBoxRuleTemplate.Render("{checkoutPath|x}", record), "fallback");
                AssertEqual("msn_t", BlackBoxRuleTemplate.Render("{missionId}", record));
                AssertEqual("MergeAndPush", BlackBoxRuleTemplate.Render("{config:LandingMode}", WithConfig(record, "LandingMode", "MergeAndPush")), "config placeholder");
                BlackBoxAdvisor logged = new BlackBoxAdvisor(catalog, BlackBoxTestKit.Logging());
                AssertNotNull(logged.Analyze(record), "the instance advisor logs and returns");
                return Task.CompletedTask;
            }));

            cases.Add(Case("empty_record", "An empty record (sweeper, nothing known) fires only the Unknown fallback, Unknown triage and a summary that says what is missing", TestTags.Negative, () =>
            {
                BlackBoxRecord empty = new BlackBoxRecord { EntryId = "bbx_empty" };
                BlackBoxAnalysis a = BlackBoxAdvisor.Analyze(empty, BlackBoxRuleCatalog.Default);
                AssertEqual("BBX-UNKNOWN-001", a.Why.Single().RuleId, "only the fallback");
                AssertEqual(BlackBoxTriageEnum.Unknown, a.Triage!.Verdict);
                AssertEqual(0, a.Triage.Votes.Count, "no votes");
                AssertEqual(0, a.RecoveryPaths.Count, "no recovery paths");
                AssertEqual(0, a.ConfigRecommendations.Count, "no recommendations");
                AssertContains("missing", a.Summary ?? "", "summary says what is missing");
                AssertNull(new BlackBoxAdvisor().Analyze(null!), "null record");
                return Task.CompletedTask;
            }));

            cases.Add(Case("deterministic_ranked", "The same record always gives the same analysis; paths are ranked strongest rule first, deduplicated and capped; recommendations skip a value already in effect", TestTags.Positive, () =>
            {
                BlackBoxRecord record = DirtyCheckout();
                string first = JsonSerializer.Serialize(BlackBoxAdvisor.Analyze(record, BlackBoxRuleCatalog.Default), BlackBoxJson.Options);
                string second = JsonSerializer.Serialize(BlackBoxAdvisor.Analyze(record, BlackBoxRuleCatalog.Default), BlackBoxJson.Options);
                AssertEqual(first, second, "deterministic");
                BlackBoxAnalysis a = BlackBoxAdvisor.Analyze(record, BlackBoxRuleCatalog.Default);
                AssertEqual("1,2", String.Join(",", a.RecoveryPaths.Select(p => p.Rank)), "ranks 1..n");
                AssertEqual("bbx-land-001.stash-retry", a.RecoveryPaths[0].Id);
                BlackBoxRecoveryPath pr = a.RecoveryPaths.Single(p => p.Id == "bbx-land-001.pull-request");
                AssertEqual("update_vessel", pr.Steps[0].Action!.Tool); AssertEqual("PullRequest", pr.Steps[0].Action!.Arguments["landingMode"]);
                AssertEqual("retry_landing", pr.Action!.Tool, "path action is the recovery itself");
                AssertContains("PUT /api/v1/vessels/vsl_t", pr.Steps[0].RestCommand ?? "");
                BlackBoxConfigRecommendation rec = a.ConfigRecommendations.Single(r => r.Id == "bbx-land-001.landing-mode");
                AssertEqual("MergeAndPush", rec.CurrentValue, "current value from the configuration");
                AssertEqual("update_vessel", rec.ApplyAction!.Tool); AssertEqual("vsl_t", rec.ApplyAction.Arguments["vesselId"]);
                BlackBoxAnalysis already = BlackBoxAdvisor.Analyze(WithConfig(DirtyCheckout(), "LandingMode", "PullRequest"), BlackBoxRuleCatalog.Default);
                AssertFalse(already.ConfigRecommendations.Any(r => r.Id == "bbx-land-001.landing-mode"), "not recommended when already in effect");

                BlackBoxRuleCatalog many = new BlackBoxRuleCatalog(1, Enumerable.Range(1, 6).Select(i => new BlackBoxRule("T-" + i, "r", r => true).Because("Rule " + i + ".", BlackBoxConfidenceEnum.Low).Votes(BlackBoxTriageEnum.Transient, i)
                    .Recover(new BlackBoxRulePath { Key = "a", Title = "Restart", Steps = { BlackBoxRuleStep.Run("Restart {missionId}.", "restart_mission", "missionId", "{missionId}") } })
                    .Recover(new BlackBoxRulePath { Key = "b" + i, Title = "Manual " + i, Steps = { BlackBoxRuleStep.Manual("Step " + i) } })
                    .Recover(new BlackBoxRulePath { Key = "c" + i, Title = "Other " + i, Steps = { BlackBoxRuleStep.Manual("Other " + i) } })));
                BlackBoxAnalysis ranked = BlackBoxAdvisor.Analyze(Rec(BlackBoxStepEnum.Push), many);
                AssertEqual(BlackBoxLimits.MaxRecoveryPaths, ranked.RecoveryPaths.Count, "capped at 10");
                AssertEqual("t-6.a", ranked.RecoveryPaths[0].Id, "strongest rule first");
                AssertEqual(1, ranked.RecoveryPaths.Count(p => p.Steps.Any(s => s.Action?.Tool == "restart_mission")), "identical actions deduplicated");
                AssertEqual("T-6", ranked.Why[0].RuleId, "findings ordered by weight");
                return Task.CompletedTask;
            }));

            cases.Add(Case("pipeline_and_reanalysis", "The runtime registers the advisor: analysis lands on the entry and briefs; an entry with older rules is re-analyzed in the background, once", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver);
                AssertTrue(bb.Pipeline.Advisor is BlackBoxAdvisor, "advisor registered");
                AssertTrue(bb.Pipeline.Enrichers.Any(e => e.Name == "configuration"), "configuration enricher registered");
                Mission mission = await BlackBoxTestKit.FailedMissionAsync(t.Driver, null, null, MissionFailureKindEnum.Infra, "Landing refused: dirty checkout", MissionStatusEnum.LandingFailed).ConfigureAwait(false);
                string id = (await bb.Capture.RecordMissionFailureAsync(mission, MissionStatusEnum.WorkProduced, BlackBoxStepEnum.DirtyCheckout, c =>
                {
                    c.GitFacts = new BlackBoxGitFacts { CheckoutPath = "/work/checkout" };
                    c.GitFacts.DirtyPaths.AddRange(new[] { "src/a.cs", "src/b.cs" });
                }).ConfigureAwait(false))!;
                BlackBoxEntry analyzed = (await bb.Pipeline.ProcessAsync(id).ConfigureAwait(false))!;
                AssertEqual(BlackBoxStateEnum.Analyzed, analyzed.State, "analyzed: " + analyzed.LastError);
                AssertEqual(BlackBoxTriageEnum.UserConfig, analyzed.Triage, "entry triage: " + analyzed.Summary);
                AssertEqual(BlackBoxRuleCatalog.CurrentRulesVersion, analyzed.RulesVersion, "rules version");
                AssertContains("uncommitted tracked changes in 2 files", analyzed.Summary ?? "", "entry summary");
                string brief = await bb.Service.GetBriefAsync(BlackBoxTestKit.Admin(), id).ConfigureAwait(false);
                AssertContains("BBX-LAND-001", brief, "brief carries the rule");
                AssertContains("bbx-land-001.stash-retry", brief, "brief carries the path id");
                AssertFalse(brief.Contains("bbx-land-001.pull-request"), "a path whose vessel is unknown is left out");

                analyzed.RulesVersion = 0;
                await t.Driver.BlackBoxEntries.UpdateAsync(analyzed).ConfigureAwait(false);
                AssertEqual(1, await bb.Reanalyzer.RunOnceAsync().ConfigureAwait(false), "older entry re-analyzed");
                BlackBoxEntry again = (await t.Driver.BlackBoxEntries.ReadAsync(id).ConfigureAwait(false))!;
                AssertEqual(BlackBoxRuleCatalog.CurrentRulesVersion, again.RulesVersion, "rules version updated");
                AssertEqual(BlackBoxStateEnum.Analyzed, again.State);
                AssertEqual(0, await bb.Reanalyzer.RunOnceAsync().ConfigureAwait(false), "nothing left to re-analyze");
                bb.Pipeline.Advisor = new NoOpBlackBoxAdvisor();
                AssertEqual(0, await bb.Reanalyzer.RunOnceAsync().ConfigureAwait(false), "rules version 0 never re-analyzes");
            }));

            cases.Add(Case("brief_audiences", "User leads with what to do and drops rule ids and source pointers; maintainer leads with the code site, triage, rules, fingerprint and occurrences", TestTags.Positive, () =>
            {
                BlackBoxRecord record = DirtyCheckout();
                record.Site!.SourceLine = 308;
                record.Mechanism = new BlackBoxMechanism { ExceptionType = "Armada.Core.Services.GitDirtyCheckoutException", ExceptionMessage = "dirty" };
                record.Mechanism.StackFrames.Add("Armada.Server.MissionLandingHandler.HandleMissionCompleteAsync() in MissionLandingHandler.cs:line 308");
                record.Related = new BlackBoxRelated { Fingerprint = "fp_golden", OccurrencesLast30Days = 4 };
                record.Analysis = BlackBoxAdvisor.Analyze(record, BlackBoxRuleCatalog.Default);
                BlackBoxEntry entry = new BlackBoxEntry { Id = "bbx_t", MissionId = "msn_t", Phase = BlackBoxPhaseEnum.Landing, Step = BlackBoxStepEnum.DirtyCheckout, Fingerprint = "fp_golden", RulesVersion = 1 };
                BlackBoxBriefRenderer renderer = new BlackBoxBriefRenderer();
                string user = renderer.Render(entry, record, BlackBoxAudienceEnum.User);
                AssertStartsWith("# What went wrong", user);
                AssertTrue(user.IndexOf("## What you can do", StringComparison.Ordinal) > 0 && user.IndexOf("## What you can do", StringComparison.Ordinal) < user.IndexOf("## Why", StringComparison.Ordinal), "user leads with recovery");
                AssertFalse(user.Contains("BBX-"), "no rule ids for the user");
                AssertFalse(user.Contains("HandleMissionCompleteAsync"), "no source pointers or stack frames for the user");
                AssertFalse(user.Contains("PUT /api/v1"), "no REST commands for the user");
                AssertContains("retry_landing", user, "MCP command still shown");
                string maintainer = renderer.Render(entry, record, BlackBoxAudienceEnum.Maintainer);
                int codeSite = maintainer.IndexOf("## Code site and triage", StringComparison.Ordinal);
                AssertTrue(codeSite > 0 && codeSite < maintainer.IndexOf("## Summary", StringComparison.Ordinal), "maintainer leads with the code site");
                AssertTrue(maintainer.IndexOf("## What Armada could change", StringComparison.Ordinal) < maintainer.IndexOf("## Summary", StringComparison.Ordinal), "improvements before the summary");
                AssertContains("src/Armada.Server/MissionLandingHandler.cs:308", maintainer, "code site");
                AssertContains("Rules: BBX-LAND-001", maintainer, "rule ids");
                AssertContains("Fingerprint: fp_golden", maintainer, "fingerprint");
                AssertContains("Occurrences in the last 30 days: 4", maintainer, "occurrences");
                AssertContains("Triage: UserConfig", maintainer, "triage");
                string agent = renderer.Render(entry, record, BlackBoxAudienceEnum.Agent);
                AssertStartsWith("# FIX THIS", agent);
                AssertTrue(agent.IndexOf("## Summary", StringComparison.Ordinal) < agent.IndexOf("## Recovery", StringComparison.Ordinal), "agent keeps record order");
                AssertContains("## Your answer", agent);
                foreach (string brief in new[] { user, maintainer, agent }) AssertFalse(brief.Contains('—'), "no em-dashes");
                return Task.CompletedTask;
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Black box advisor and rules", cases: cases);
        }

        private static List<BlackBoxGoldenCase> BlackBoxGoldenCases()
        {
            List<BlackBoxGoldenCase> list = new List<BlackBoxGoldenCase>();
            list.Add(new BlackBoxGoldenCase("BBX-LAND-001", BlackBoxTriageEnum.UserConfig, DirtyCheckout) { Statement = "The checkout at /work/checkout has uncommitted tracked changes in 3 files, so the landing refused to overwrite them.", FirstPathId = "bbx-land-001.stash-retry", DoesNotFire = { "BBX-LAND-007" } });
            list.Add(new BlackBoxGoldenCase("BBX-LAND-002", BlackBoxTriageEnum.ArmadaDefect, () => { BlackBoxRecord r = Rec(BlackBoxStepEnum.TargetIntegration, MissionFailureKindEnum.LandingConflict, "An error occurred trying to start process '/usr/bin/git' with working directory '/work/docks/msn_t'. No such file or directory"); r.Workspace!.WorktreePath = "/work/docks/msn_t"; return r; }) { Statement = "The dock directory /work/docks/msn_t was removed before landing, so git could not start in it.", FirstPathId = "bbx-land-002.retry-landing" });
            list.Add(new BlackBoxGoldenCase("BBX-LAND-003", BlackBoxTriageEnum.UserConfig, () => Conflict("src/Armada.Core/Services/MissionService.cs", "src/Armada.Core/Services/DockService.cs")) { Statement = "msn_other (Shared helpers) landed c0ffee1 on main at 2026-10-28 10:00Z, changing 2 of the same files.", FirstPathId = "bbx-land-003.restart-on-target" });
            list.Add(new BlackBoxGoldenCase("BBX-LAND-004", BlackBoxTriageEnum.ArmadaDefect, () => Conflict("CHANGELOG.md", "docs/api-surface-1.0.json", "src/Armada.Dashboard/src/i18n/armada.json")) { DoesNotFire = { "BBX-LAND-003" } });
            list.Add(new BlackBoxGoldenCase("BBX-LAND-005", BlackBoxTriageEnum.MissionBrief, () => { BlackBoxRecord r = Rec(BlackBoxStepEnum.Verification); r.Mechanism!.FailingCommand = "dotnet test"; r.Mechanism.CommandExitCode = 1; return r; }) { Statement = "Post-merge verification `dotnet test` failed with exit 1." });
            list.Add(new BlackBoxGoldenCase("BBX-LAND-006", BlackBoxTriageEnum.External, () => Rec(BlackBoxStepEnum.Push, MissionFailureKindEnum.Infra, "push rejected")));
            list.Add(new BlackBoxGoldenCase("BBX-LAND-007", BlackBoxTriageEnum.Unknown, () => Rec(BlackBoxStepEnum.FastForward, MissionFailureKindEnum.Infra, "fast-forward refused")) { FirstPathId = "bbx-land-007.retry-landing" });
            list.Add(new BlackBoxGoldenCase("BBX-MQ-001", BlackBoxTriageEnum.ArmadaDefect, () => Rec(BlackBoxStepEnum.QueueProcessing, MissionFailureKindEnum.LandingConflict, "merge queue entry failed")) { DoesNotFire = { "BBX-MQ-003" } });
            list.Add(new BlackBoxGoldenCase("BBX-MQ-002", BlackBoxTriageEnum.MissionBrief, () => Rec(BlackBoxStepEnum.QueueTests, MissionFailureKindEnum.TestFail, "tests failed")) { DoesNotFire = { "BBX-CHECK-001" } });
            list.Add(new BlackBoxGoldenCase("BBX-MQ-003", BlackBoxTriageEnum.Unknown, () => Rec(BlackBoxStepEnum.QueuePush, MissionFailureKindEnum.Infra, "push failed")));
            list.Add(new BlackBoxGoldenCase("BBX-SCOPE-001", BlackBoxTriageEnum.ArmadaDefect, () => Scope(true, "e4f1a20")) { DoesNotFire = { "BBX-SCOPE-003", "BBX-SCOPE-002" }, FirstPathId = "bbx-scope-001.review-restart" });
            list.Add(new BlackBoxGoldenCase("BBX-SCOPE-002", BlackBoxTriageEnum.MissionBrief, () => Scope(false, null)) { AlsoFires = { "BBX-SCOPE-003" } });
            list.Add(new BlackBoxGoldenCase("BBX-SCOPE-003", BlackBoxTriageEnum.MissionBrief, () => Scope(false, "e4f1a20")) { DoesNotFire = { "BBX-SCOPE-002" } });
            list.Add(new BlackBoxGoldenCase("BBX-NOOP-001", BlackBoxTriageEnum.MissionBrief, () => Rec(BlackBoxStepEnum.NoOp, MissionFailureKindEnum.NoOp, "no changes")) { Statement = "The captain finished without producing changes." });
            list.Add(new BlackBoxGoldenCase("BBX-DEP-001", BlackBoxTriageEnum.MissionBrief, () => Rec(BlackBoxStepEnum.CaptainBlocked, MissionFailureKindEnum.Blocked, "Captain reported BLOCKED: mission 3 depends on mission 2, which has not landed; missions 1-4 are running concurrently.")) { DoesNotFire = { "BBX-BLOCK-001" }, Statement = "Missions that depend on each other were dispatched in parallel: the captain reported BLOCKED because mission 2 had not landed." });
            list.Add(new BlackBoxGoldenCase("BBX-BLOCK-001", BlackBoxTriageEnum.MissionBrief, () => Rec(BlackBoxStepEnum.CaptainBlocked, MissionFailureKindEnum.Blocked, "Captain reported BLOCKED: the brief contradicts the vessel's style guide.")) { DoesNotFire = { "BBX-DEP-001" } });
            list.Add(new BlackBoxGoldenCase("BBX-OUT-001", BlackBoxTriageEnum.UserConfig, () => Rec(BlackBoxStepEnum.InvalidOutput, MissionFailureKindEnum.InvalidOutput, "missing markers")));
            list.Add(new BlackBoxGoldenCase("BBX-BOUND-001", BlackBoxTriageEnum.MissionBrief, () => Rec(BlackBoxStepEnum.BoundaryViolation, MissionFailureKindEnum.Boundary, "changed .github/workflows")));
            list.Add(new BlackBoxGoldenCase("BBX-CHECK-001", BlackBoxTriageEnum.MissionBrief, () => { BlackBoxRecord r = Rec(BlackBoxStepEnum.DefinitionOfDone, MissionFailureKindEnum.Compile, "build failed"); r.Mechanism!.FailingCommand = "dotnet build"; r.Mechanism.CommandExitCode = 1; return r; }));
            list.Add(new BlackBoxGoldenCase("BBX-UNFIN-001", BlackBoxTriageEnum.MissionBrief, () => Rec(BlackBoxStepEnum.Unfinished, MissionFailureKindEnum.Unknown, "stopped early")));
            list.Add(new BlackBoxGoldenCase("BBX-EXEC-001", BlackBoxTriageEnum.Transient, () => { BlackBoxRecord r = Rec(BlackBoxStepEnum.UsageLimit, MissionFailureKindEnum.Infra, "usage limit reached"); r.Mechanism!.RuntimeFailureKind = RuntimeFailureKindEnum.UsageLimit; return r; }) { Statement = "The ClaudeCode account hit its usage limit." });
            list.Add(new BlackBoxGoldenCase("BBX-EXEC-004", BlackBoxTriageEnum.UserConfig, () => { BlackBoxRecord r = Rec(BlackBoxStepEnum.AuthFailure, MissionFailureKindEnum.Infra, "401"); r.Mechanism!.RuntimeFailureKind = RuntimeFailureKindEnum.AuthFailure; return r; }));
            list.Add(new BlackBoxGoldenCase("BBX-EXEC-005", BlackBoxTriageEnum.Transient, () => { BlackBoxRecord r = Rec(BlackBoxStepEnum.ModelUnavailable, MissionFailureKindEnum.Infra, "model not found"); r.Mechanism!.RuntimeFailureKind = RuntimeFailureKindEnum.ModelUnavailable; return r; }));
            list.Add(new BlackBoxGoldenCase("BBX-EXEC-002", BlackBoxTriageEnum.Transient, () => WithConfig(Rec(BlackBoxStepEnum.Stall, MissionFailureKindEnum.StallRecoveryExhausted, "stalled"), "StallThresholdMinutes", "10")) { Statement = "No activity for 10 minutes; recovery was exhausted.", DoesNotFire = { "BBX-EXEC-008" } });
            list.Add(new BlackBoxGoldenCase("BBX-EXEC-003", BlackBoxTriageEnum.MissionBrief, () => WithConfig(Rec(BlackBoxStepEnum.MaxRuntime, MissionFailureKindEnum.MaxRuntimeExceeded, "too long"), "MaxMissionRuntimeMinutes", "240")) { Statement = "The mission ran longer than 240 minutes." });
            list.Add(new BlackBoxGoldenCase("BBX-EXEC-006", BlackBoxTriageEnum.Transient, () => { BlackBoxRecord r = Rec(BlackBoxStepEnum.ProcessCrash, MissionFailureKindEnum.Crash, "exited"); r.Mechanism!.ExitCode = 137; return r; }) { Statement = "The captain's process exited with code 137 before finishing." });
            list.Add(new BlackBoxGoldenCase("BBX-EXEC-007", BlackBoxTriageEnum.External, () => Rec(BlackBoxStepEnum.OperatorStop, MissionFailureKindEnum.OperatorAction, "stopped by operator")));
            list.Add(new BlackBoxGoldenCase("BBX-EXEC-008", BlackBoxTriageEnum.Transient, () => Rec(BlackBoxStepEnum.RecoveryExhausted, null, "recovery exhausted")) { DoesNotFire = { "BBX-EXEC-006" } });
            list.Add(new BlackBoxGoldenCase("BBX-INFRA-001", BlackBoxTriageEnum.External, () => { BlackBoxRecord r = Rec(BlackBoxStepEnum.Push, MissionFailureKindEnum.Infra, "harbor gone"); r.Workspace!.HarborLinkState = "Disconnected"; r.Subject!.HarborName = "laptop"; return r; }) { Statement = "Harbor laptop was disconnected.", DoesNotFire = { "BBX-LAND-006" }, FirstPathId = "bbx-infra-001.reconnect-retry" });
            list.Add(new BlackBoxGoldenCase("BBX-DISP-001", BlackBoxTriageEnum.UserConfig, () => Rec(BlackBoxStepEnum.NoEligibleCaptain, null, "no idle captain")) { DoesNotFire = { "BBX-DISP-002" } });
            list.Add(new BlackBoxGoldenCase("BBX-DISP-002", BlackBoxTriageEnum.Transient, () => Rec(BlackBoxStepEnum.MemoryPressure, null, "memory pressure")));
            list.Add(new BlackBoxGoldenCase("BBX-PROV-001", BlackBoxTriageEnum.Transient, () => Rec(BlackBoxStepEnum.WorktreeCreate, MissionFailureKindEnum.Infra, "worktree add failed")) { FirstPathId = "bbx-prov-001.repair-restart" });
            list.Add(new BlackBoxGoldenCase("BBX-REV-001", BlackBoxTriageEnum.External, () => Rec(BlackBoxStepEnum.ReviewDenied, MissionFailureKindEnum.ReviewDenied, "not what I asked for")));
            list.Add(new BlackBoxGoldenCase("BBX-REV-002", BlackBoxTriageEnum.MissionBrief, () => Rec(BlackBoxStepEnum.JudgeRejected, MissionFailureKindEnum.JudgeRejected, "tests missing")));
            list.Add(new BlackBoxGoldenCase("BBX-REV-003", BlackBoxTriageEnum.ArmadaDefect, () => Rec(BlackBoxStepEnum.ReviewApprovedDockMissing, null, "dock gone")));
            list.Add(new BlackBoxGoldenCase("BBX-REV-004", BlackBoxTriageEnum.External, () => Rec(BlackBoxStepEnum.ReviewTimeout, null, "review timed out")));
            list.Add(new BlackBoxGoldenCase("BBX-DEP-002", BlackBoxTriageEnum.External, () => Rec(BlackBoxStepEnum.DependencyCancelled, MissionFailureKindEnum.DependencyFailed, "dependency failed")) { DoesNotFire = { "BBX-HAND-001" } });
            list.Add(new BlackBoxGoldenCase("BBX-HAND-001", BlackBoxTriageEnum.ArmadaDefect, () => Rec(BlackBoxStepEnum.PlanParse, null, "plan block unreadable")));
            list.Add(new BlackBoxGoldenCase("BBX-RECOV-001", BlackBoxTriageEnum.ArmadaDefect, () => Rec(BlackBoxStepEnum.RescueFailed, null, "rescue failed")));
            list.Add(new BlackBoxGoldenCase("BBX-VOY-001", BlackBoxTriageEnum.Unknown, () =>
            {
                BlackBoxRecord r = Rec(BlackBoxStepEnum.Halted, null, "halted");
                r.Subject = new BlackBoxSubject { SubjectType = BlackBoxSubjectTypeEnum.Voyage, VoyageId = "vyg_t", Title = "Voyage T" };
                r.Voyage = new BlackBoxVoyageRollup { CascadeRootMissionId = "msn_root", HaltReason = "stage failed" };
                return r;
            }) { Statement = "The voyage stopped at step Halted (stage failed); start with the black box of msn_root." });
            list.Add(new BlackBoxGoldenCase("BBX-EXT-001", BlackBoxTriageEnum.External, () => Rec(BlackBoxStepEnum.VesselDeleted, MissionFailureKindEnum.OperatorAction, "vessel deleted")) { AlsoFires = { "BBX-EXEC-007" } });
            list.Add(new BlackBoxGoldenCase("BBX-UNKNOWN-001", BlackBoxTriageEnum.Unknown, () => Rec(BlackBoxStepEnum.Unknown, null, "something odd")) { Statement = "No black box rule matched this failure (Unknown / Unknown); decide the cause from the reason, mechanism and evidence." });
            return list;
        }

        private static BlackBoxRecord Rec(BlackBoxStepEnum step, MissionFailureKindEnum? kind = null, string? reason = null)
        {
            return new BlackBoxRecord
            {
                EntryId = "bbx_t",
                Subject = new BlackBoxSubject { SubjectType = BlackBoxSubjectTypeEnum.Mission, MissionId = "msn_t", Title = "Golden mission", VesselId = "vsl_t", VesselName = "Vessel T", VesselDefaultBranch = "main", CaptainId = "cpt_t", CaptainName = "claude-1", CaptainRuntime = "ClaudeCode", DockId = "dck_t", BranchName = "armada/msn_t" },
                Site = new BlackBoxSite { Phase = BlackBoxTaxonomy.PhaseOf(step), Step = step, SourceFile = "src/Armada.Server/MissionLandingHandler.cs", Method = "HandleMissionCompleteAsync", Component = "MissionLandingHandler", ArmadaVersion = "1.2.0" },
                Mechanism = new BlackBoxMechanism(),
                Classification = new BlackBoxClassification { Kind = kind, RawReason = reason },
                Workspace = new BlackBoxWorkspace { DockId = "dck_t", BranchName = "armada/msn_t", StartCommit = "abc1234", TargetBranch = "main", WorktreePath = "/work/docks/msn_t", MissionCommit = "def5678" }
            };
        }

        private static BlackBoxRecord DirtyCheckout()
        {
            BlackBoxRecord r = WithConfig(Rec(BlackBoxStepEnum.DirtyCheckout, MissionFailureKindEnum.Infra, "Landing refused: dirty checkout"), "LandingMode", "MergeAndPush");
            r.Workspace!.CheckoutPath = "/work/checkout";
            r.Workspace.CheckoutDirty = true;
            r.Workspace.DirtyPaths = new List<string> { "src/a.cs", "src/b.cs", "src/c.cs" };
            r.Workspace.DirtyPathTotal = 3;
            return r;
        }

        private static BlackBoxRecord Conflict(params string[] files)
        {
            BlackBoxRecord r = Rec(BlackBoxStepEnum.TargetIntegration, MissionFailureKindEnum.LandingConflict, "merge conflict");
            r.Conflict = new BlackBoxConflict { IsConflict = true, ConflictedFiles = files.ToList(), ConflictedFileTotal = files.Length };
            BlackBoxConflictingMission other = new BlackBoxConflictingMission { MissionId = "msn_other", Title = "Shared helpers", CommitHash = "c0ffee1", LandedUtc = new DateTime(2026, 10, 28, 10, 0, 0, DateTimeKind.Utc) };
            other.OverlappingFiles.AddRange(files.Take(2));
            other.OverlappingFileTotal = Math.Min(2, files.Length);
            r.Conflict.ConflictingMissions.Add(other);
            return r;
        }

        private static BlackBoxRecord Scope(bool falseViolation, string? startCommit)
        {
            BlackBoxRecord r = Rec(BlackBoxStepEnum.ScopeViolation, MissionFailureKindEnum.ScopeViolation, falseViolation ? "Mission modified files outside its scoped file list: , src/g/Server.cs" : "Mission modified files outside its scoped file list: src/other.cs");
            r.Site!.Phase = BlackBoxPhaseEnum.Completion;
            r.Workspace!.StartCommit = startCommit;
            r.Scope = new BlackBoxScope { ScopedFiles = new List<string> { "src/g/Server.cs" }, OutOfScopeFiles = falseViolation ? new List<string> { "", "./src/g/Server.cs" } : new List<string> { "src/other.cs" } };
            return r;
        }

        private static BlackBoxRecord WithConfig(BlackBoxRecord r, string setting, string value)
        {
            r.Configuration ??= new BlackBoxConfiguration();
            r.Configuration.Values.RemoveAll(v => v.Setting == setting);
            r.Configuration.Values.Add(new BlackBoxConfigValue { Setting = setting, Value = value, Source = BlackBoxConfigSourceEnum.Global });
            return r;
        }

        private static BlackBoxTriageVote Vote(string rule, BlackBoxTriageEnum verdict, int weight)
        {
            return new BlackBoxTriageVote { RuleId = rule, Verdict = verdict, Weight = weight };
        }

        private static IEnumerable<string> Placeholders(BlackBoxRule rule)
        {
            List<string> texts = new List<string> { rule.Why };
            foreach (BlackBoxRuleRecommendation r in rule.Recommendations) { texts.Add(r.SuggestedValue ?? ""); texts.Add(r.Rationale ?? ""); texts.Add(r.HowToApply ?? ""); texts.AddRange(r.ApplyArguments.Values); }
            foreach (BlackBoxRuleImprovement i in rule.Improvements) { texts.Add(i.Hypothesis); texts.Add(i.Evidence ?? ""); }
            foreach (BlackBoxRulePath p in rule.Paths)
            {
                texts.Add(p.Title); texts.AddRange(p.Preconditions); texts.Add(p.ExpectedOutcome ?? "");
                foreach (BlackBoxRuleStep s in p.Steps) { texts.Add(s.Description); texts.Add(s.Cli ?? ""); texts.AddRange(s.Arguments.Values); }
            }
            return texts.SelectMany(BlackBoxRuleTemplate.Placeholders).Distinct();
        }

        private static void AssertNoUnfilled(BlackBoxAnalysis a, string label)
        {
            List<string> texts = new List<string> { a.Summary ?? "" };
            texts.AddRange(a.Why.Select(w => w.Statement));
            texts.AddRange(a.ConfigRecommendations.SelectMany(r => new[] { r.Rationale ?? "", r.HowToApply ?? "", r.SuggestedValue ?? "" }));
            texts.AddRange(a.ArmadaImprovements.SelectMany(i => new[] { i.Hypothesis, i.Evidence ?? "" }));
            texts.AddRange(a.RecoveryPaths.SelectMany(p => new[] { p.Title, p.ExpectedOutcome ?? "" }.Concat(p.Preconditions).Concat(p.Steps.SelectMany(s => new[] { s.Description, s.CliCommand ?? "", s.McpCommand ?? "" }))));
            foreach (string text in texts)
            {
                // JSON in MCP command strings has braces, but never a {name} placeholder.
                AssertFalse(_Unfilled.IsMatch(text.Replace("{\"", "").Replace("{}", "")), label + ": unfilled placeholder in '" + text + "'");
            }
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(suiteId: SuiteId, caseId: caseId, displayName: displayName, executeAsync: (CancellationToken ct) => body(), tags: new List<string> { tag });
        }
    }
}

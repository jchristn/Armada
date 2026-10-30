namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.BlackBox;
    using Armada.Core.BlackBox.Enrichment;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// The black box resilience bias: the handoff and landing scope, the BBX-PRIOR-ARMADA triage prior (wins without
    /// strong counter-evidence, loses to strong evidence, never applies outside the scope), the resilience section on
    /// every rule in scope, severity escalation by recurrence and Armada versions, the summary and brief leading with the
    /// Armada angle, the handoff trail, the resilience gaps list and the maintainer bundle's default filter.
    /// </summary>
    public sealed class BlackBoxResilienceSuite : IArmadaTestSuite
    {
        private const string SuiteId = "Services.BlackBoxResilience";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("scope", "The handoff and landing scope covers its phases, Armada handoff mechanics in Completion and dock facts of any phase, and nothing else", TestTags.Positive, () =>
            {
                foreach (BlackBoxStepEnum step in new[] { BlackBoxStepEnum.DirtyCheckout, BlackBoxStepEnum.QueueTests, BlackBoxStepEnum.PlanParse, BlackBoxStepEnum.OrphanedExit, BlackBoxStepEnum.WorktreeCreate, BlackBoxStepEnum.InvalidOutput, BlackBoxStepEnum.ReviewApprovedDockMissing })
                    AssertTrue(BlackBoxResilienceScope.InScope(Rec(step)), step + " is in scope");
                foreach (BlackBoxStepEnum step in new[] { BlackBoxStepEnum.Stall, BlackBoxStepEnum.UsageLimit, BlackBoxStepEnum.NoOp, BlackBoxStepEnum.ReviewDenied, BlackBoxStepEnum.NoEligibleCaptain, BlackBoxStepEnum.Halted })
                    AssertFalse(BlackBoxResilienceScope.InScope(Rec(step)), step + " is outside scope");
                BlackBoxRecord harness = Rec(BlackBoxStepEnum.StageCheck);
                harness.Mechanism!.CommandExitCode = 124;
                AssertTrue(BlackBoxResilienceScope.InScope(harness, out string? reason) && reason!.Contains("harness"), "a stage check harness error (timeout exit 124) is in scope");
                BlackBoxRecord realTest = Rec(BlackBoxStepEnum.StageCheck, MissionFailureKindEnum.TestFail);
                realTest.Mechanism!.CommandExitCode = 1;
                AssertFalse(BlackBoxResilienceScope.InScope(realTest), "a real test failure in a stage check is outside scope");
                AssertTrue(BlackBoxResilienceScope.ConcreteCheckFailure(realTest), "a non-zero non-harness exit is a concrete failure");
                BlackBoxRecord couldNotRun = Rec(BlackBoxStepEnum.DefinitionOfDone);
                couldNotRun.Evidence = new BlackBoxEvidenceIndex();
                couldNotRun.Evidence.CheckResults.Add(new StageCheckResult { Status = StageCheckStatusEnum.CouldNotRun });
                AssertTrue(BlackBoxResilienceScope.HarnessError(couldNotRun), "a CouldNotRun check result is a harness error");
                BlackBoxRecord dockGone = Rec(BlackBoxStepEnum.Stall);
                dockGone.Workspace!.WorktreeExists = false;
                AssertTrue(BlackBoxResilienceScope.InScope(dockGone, out string? dockReason) && dockReason!.Contains("worktree"), "a missing worktree puts any phase in scope");
                BlackBoxRecord voyage = Rec(BlackBoxStepEnum.DirtyCheckout);
                voyage.Subject!.SubjectType = BlackBoxSubjectTypeEnum.Voyage;
                AssertFalse(BlackBoxResilienceScope.InScope(voyage), "voyage entries are outside scope");
                AssertTrue(BlackBoxResilienceScope.IndexInScope(BlackBoxPhaseEnum.Completion, BlackBoxStepEnum.InvalidOutput, null), "index scope: InvalidOutput step");
                AssertTrue(BlackBoxResilienceScope.IndexInScope(BlackBoxPhaseEnum.Unknown, BlackBoxStepEnum.Push, null), "index scope: a landing step without a phase");
                AssertFalse(BlackBoxResilienceScope.IndexInScope(BlackBoxPhaseEnum.Execution, BlackBoxStepEnum.Stall, MissionFailureKindEnum.StallRecoveryExhausted), "index scope: execution");
                AssertFalse(BlackBoxResilienceScope.InScope(null!), "null record");
                return Task.CompletedTask;
            }));

            cases.Add(Case("prior_wins_without_strong_evidence", "In scope the BBX-PRIOR-ARMADA vote makes ArmadaDefect win over a weak counter vote, a tie and no vote; a verdict that rests on the prior alone is suspected with Low confidence", TestTags.Positive, () =>
            {
                BlackBoxAnalysis weak = Analyze(Rec(BlackBoxStepEnum.Push, MissionFailureKindEnum.Infra, "push rejected"));
                AssertEqual(BlackBoxTriageEnum.ArmadaDefect, weak.Triage!.Verdict, "External 2 ties the prior and the tie goes to ArmadaDefect");
                AssertTrue(weak.Triage.PriorApplied, "prior applied");
                AssertTrue(weak.Triage.Suspected, "no rule voted ArmadaDefect, so it is suspected");
                AssertEqual(BlackBoxConfidenceEnum.Low, weak.Triage.Confidence, "suspected is Low");
                AssertEqual(0, weak.Triage.TiedWith.Count, "a resolved tie lists no candidates");
                BlackBoxTriageVote prior = weak.Triage.Votes.Single(v => v.RuleId == BlackBoxResilienceScope.PriorRuleId);
                AssertEqual(BlackBoxTriageEnum.ArmadaDefect, prior.Verdict); AssertEqual(BlackBoxLimits.ArmadaPriorWeight, prior.Weight, "the prior is its own visible vote");
                AssertContains("suspected ArmadaDefect", weak.Summary ?? "", "summary says suspected");

                BlackBoxAnalysis none = Analyze(Rec(BlackBoxStepEnum.FastForward, MissionFailureKindEnum.Infra, "fast-forward refused"));
                AssertEqual(BlackBoxTriageEnum.ArmadaDefect, none.Triage!.Verdict, "no vote in scope becomes ArmadaDefect");
                AssertTrue(none.Triage.Suspected && none.Triage.Confidence == BlackBoxConfidenceEnum.Low, "Unknown in scope is a Low suspected ArmadaDefect");

                BlackBoxRecord unknownStep = Rec(BlackBoxStepEnum.Unknown, null, "something odd in landing");
                unknownStep.Site!.Phase = BlackBoxPhaseEnum.Landing;
                BlackBoxAnalysis fallback = Analyze(unknownStep);
                AssertEqual(BlackBoxTriageEnum.ArmadaDefect, fallback.Triage!.Verdict, "an unmatched landing failure is a suspected ArmadaDefect");
                AssertTrue(fallback.Resilience!.InScope && fallback.Resilience.ProposedChanges.Count > 0 && fallback.Resilience.SelfHealingProposal != null, "the phase defaults fill the resilience section when no rule carries one");

                BlackBoxAnalysis armadaRule = Analyze(DockDeleted());
                AssertEqual(BlackBoxTriageEnum.ArmadaDefect, armadaRule.Triage!.Verdict);
                AssertFalse(armadaRule.Triage.Suspected, "a rule voted ArmadaDefect, so it is not merely suspected");
                AssertEqual(BlackBoxConfidenceEnum.High, armadaRule.Triage.Confidence, "rule 3 plus prior 2 is High");
                return Task.CompletedTask;
            }));

            cases.Add(Case("prior_loses_to_strong_evidence", "Strong specific evidence beats the prior: a dirty checkout stays UserConfig with its user-side fix, yet keeps the resilience section and proposes the Armada self-healing change", TestTags.Negative, () =>
            {
                BlackBoxAnalysis a = Analyze(DirtyCheckout());
                AssertEqual(BlackBoxTriageEnum.UserConfig, a.Triage!.Verdict, "UserConfig 3 beats the prior 2");
                AssertTrue(a.Triage.PriorApplied, "the prior still voted");
                AssertFalse(a.Triage.Suspected, "not suspected");
                AssertTrue(a.ConfigRecommendations.Any(r => r.Id == "bbx-land-001.landing-mode"), "the user-side fix is still recommended");
                BlackBoxResilience res = a.Resilience!;
                AssertTrue(res.InScope, "in scope");
                AssertEqual(BlackBoxSelfHealableEnum.Yes, res.SelfHealable, "Armada could have healed it");
                AssertContains("integration worktree", res.SelfHealingProposal ?? "", "self-healing proposal");
                AssertContains("integration worktree", res.ProposedChanges[0].Title, "the top Armada change lands via an integration worktree");
                AssertTrue(res.ProposedChanges.Any(c => c.Title.Contains("defer", StringComparison.OrdinalIgnoreCase)), "auto-defer is proposed too");
                AssertContains("Acceptance test", res.ProposedChanges[0].MissionBrief ?? "", "mission brief carries the acceptance test");
                AssertContains("commit abc9999", res.ProposedChanges[0].MissionBrief ?? "", "mission brief names the Armada build commit");
                AssertEqual("src/Armada.Server/MissionLandingHandler.cs:HandleMissionCompleteAsync", res.CodePointers[0], "the failure site leads the code pointers");
                AssertNotNull(res.RegressionTest);

                BlackBoxRecord harbor = Rec(BlackBoxStepEnum.Push, MissionFailureKindEnum.Infra, "harbor gone");
                harbor.Workspace!.HarborLinkState = "Disconnected";
                AssertEqual(BlackBoxTriageEnum.External, Analyze(harbor).Triage!.Verdict, "a disconnected Harbor (External 3) beats the prior");

                BlackBoxRecord auth = Rec(BlackBoxStepEnum.Push, MissionFailureKindEnum.Infra, "push failed");
                auth.Mechanism!.GitStderrTail = "fatal: Authentication failed for 'https://github.com/o/r.git/'";
                BlackBoxAnalysis authA = Analyze(auth);
                AssertEqual(BlackBoxTriageEnum.UserConfig, authA.Triage!.Verdict, "a credential refusal is UserConfig");
                AssertEqual(BlackBoxSelfHealableEnum.No, authA.Resilience!.SelfHealable, "but Armada should have preflighted it");
                AssertContains("Preflight", authA.Resilience.ProposedChanges[0].Title, "preflight proposal");

                BlackBoxRecord verification = Rec(BlackBoxStepEnum.Verification, MissionFailureKindEnum.TestFail, "verification failed");
                verification.Mechanism!.FailingCommand = "dotnet test"; verification.Mechanism.CommandExitCode = 1;
                AssertEqual(BlackBoxTriageEnum.MissionBrief, Analyze(verification).Triage!.Verdict, "a concrete post-merge test failure (MissionBrief 3) beats the prior");
                return Task.CompletedTask;
            }));

            cases.Add(Case("no_prior_outside_scope", "Outside the scope there is no prior vote, the triage is unchanged, and the resilience section is short (no Armada change suggested, with the reason)", TestTags.Negative, () =>
            {
                BlackBoxRecord usage = Rec(BlackBoxStepEnum.UsageLimit, MissionFailureKindEnum.Infra, "usage limit reached");
                usage.Mechanism!.RuntimeFailureKind = RuntimeFailureKindEnum.UsageLimit;
                BlackBoxAnalysis a = Analyze(usage);
                AssertEqual(BlackBoxTriageEnum.Transient, a.Triage!.Verdict);
                AssertFalse(a.Triage.PriorApplied, "no prior");
                AssertFalse(a.Triage.Votes.Any(v => v.RuleId == BlackBoxResilienceScope.PriorRuleId), "no prior vote");
                AssertFalse(a.Resilience!.InScope, "outside scope");
                AssertEqual(0, a.Resilience.ProposedChanges.Count, "no Armada change");
                AssertContains("No Armada change suggested", a.Resilience.NoChangeReason ?? "", "the reason");
                AssertStartsWith("Mission", a.Summary ?? "", "the summary keeps its usual lead");

                BlackBoxAnalysis stall = Analyze(Rec(BlackBoxStepEnum.Stall, MissionFailureKindEnum.StallRecoveryExhausted, "stalled"));
                AssertFalse(stall.Resilience!.InScope);
                AssertTrue(stall.Resilience.ProposedChanges.Count > 0, "an out-of-scope improvement still becomes a proposed Armada change");
                AssertNull(stall.Resilience.NoChangeReason, "no 'no change' reason when one is proposed");

                BlackBoxAnalysis voyage = Analyze(new BlackBoxRecord { EntryId = "bbx_v", Subject = new BlackBoxSubject { SubjectType = BlackBoxSubjectTypeEnum.Voyage, VoyageId = "vyg_v" }, Site = new BlackBoxSite { Phase = BlackBoxPhaseEnum.Voyage, Step = BlackBoxStepEnum.Halted } });
                AssertEqual(BlackBoxTriageEnum.Unknown, voyage.Triage!.Verdict, "voyage entries keep Unknown");
                AssertFalse(voyage.Triage.PriorApplied);
                return Task.CompletedTask;
            }));

            cases.Add(Case("every_scope_rule_heals", "Every catalog rule about Armada's handoff and landing carries complete resilience content (self-healing, detection gap, invariant, code pointers, proposed changes with acceptance tests, regression test)", TestTags.Positive, () =>
            {
                BlackBoxRuleCatalog catalog = BlackBoxRuleCatalog.Default;
                AssertTrue(catalog.RulesVersion >= 2, "rules version bumped for the resilience bias");
                List<BlackBoxRule> scoped = catalog.Rules.Where(r => r.InResilienceScope).ToList();
                AssertTrue(scoped.Count >= 30, "handoff and landing rules: " + scoped.Count);
                foreach (BlackBoxRule rule in scoped)
                {
                    BlackBoxRuleResilience? res = rule.Resilience;
                    AssertNotNull(res, rule.Id + " carries resilience content");
                    AssertFalse(String.IsNullOrWhiteSpace(res!.SelfHealing), rule.Id + " self-healing");
                    AssertFalse(String.IsNullOrWhiteSpace(res.DetectionGap), rule.Id + " detection gap");
                    AssertFalse(String.IsNullOrWhiteSpace(res.Invariant), rule.Id + " invariant");
                    AssertTrue(res.GapKind != BlackBoxDetectionGapEnum.Unknown, rule.Id + " gap kind");
                    AssertTrue(res.CodePointers.Count > 0 && res.CodePointers.All(p => p.StartsWith("src/", StringComparison.Ordinal) && p.Contains(':')), rule.Id + " code pointers are file:method");
                    AssertTrue(res.Changes.Count > 0, rule.Id + " proposes an Armada change");
                    foreach (BlackBoxRuleChange c in res.Changes)
                    {
                        AssertFalse(String.IsNullOrWhiteSpace(c.Title) || String.IsNullOrWhiteSpace(c.Change) || String.IsNullOrWhiteSpace(c.AcceptanceTest), rule.Id + " change is mission-ready");
                        AssertFalse((c.Title + c.Change + c.AcceptanceTest).Contains('—'), rule.Id + " no em-dashes");
                    }
                    AssertFalse(String.IsNullOrWhiteSpace(res.RegressionSuite) || String.IsNullOrWhiteSpace(res.RegressionTest), rule.Id + " regression test");
                }
                foreach (BlackBoxStepEnum step in Enum.GetValues<BlackBoxStepEnum>().Where(s => BlackBoxResilienceScope.IsScopePhase(BlackBoxTaxonomy.PhaseOf(s))))
                {
                    BlackBoxAnalysis a = Analyze(Rec(step, MissionFailureKindEnum.Infra, "failed at " + step));
                    BlackBoxResilience res = a.Resilience!;
                    AssertTrue(res.InScope, step + " in scope");
                    AssertTrue(res.SelfHealingProposal != null && res.DetectionGap != null && res.CodePointers.Count > 0 && res.ProposedChanges.Count > 0 && res.RegressionTest != null, step + ": the resilience section is never empty in scope");
                }
                return Task.CompletedTask;
            }));

            cases.Add(Case("severity_escalation", "Severity escalates with recurrence: 1 occurrence Medium, 3 High, 5 or 2 Armada versions Critical; a higher base severity is kept", TestTags.Positive, () =>
            {
                AssertEqual(BlackBoxSeverityEnum.Medium, BlackBoxResilienceSeverity.FromRecurrence(1, 1));
                AssertEqual(BlackBoxSeverityEnum.Medium, BlackBoxResilienceSeverity.FromRecurrence(0, 0), "unknown recurrence counts as one");
                AssertEqual(BlackBoxSeverityEnum.High, BlackBoxResilienceSeverity.FromRecurrence(3, 1));
                AssertEqual(BlackBoxSeverityEnum.Critical, BlackBoxResilienceSeverity.FromRecurrence(5, 1));
                AssertEqual(BlackBoxSeverityEnum.Critical, BlackBoxResilienceSeverity.FromRecurrence(1, 2), "seen across two Armada versions");
                AssertEqual(BlackBoxSeverityEnum.High, BlackBoxResilienceSeverity.Escalate(BlackBoxSeverityEnum.High, 1, 1), "base kept when higher");
                AssertEqual(BlackBoxSeverityEnum.Medium, BlackBoxResilienceSeverity.Escalate(BlackBoxSeverityEnum.Unknown, 1, 1), "Unknown counts as Low");

                BlackBoxRecord once = Rec(BlackBoxStepEnum.FastForward, MissionFailureKindEnum.Infra, "fast-forward refused");
                AssertEqual(BlackBoxSeverityEnum.Medium, Analyze(once).Resilience!.Severity, "a Medium change seen once stays Medium");
                BlackBoxRecord thrice = Rec(BlackBoxStepEnum.FastForward, MissionFailureKindEnum.Infra, "fast-forward refused");
                thrice.Related = new BlackBoxRelated { Fingerprint = "fp_t", OccurrencesLast30Days = 3 };
                BlackBoxResilience high = Analyze(thrice).Resilience!;
                AssertEqual(BlackBoxSeverityEnum.High, high.Severity, "three in 30 days is High");
                AssertTrue(high.ProposedChanges.All(c => BlackBoxResilienceSeverity.Order(c.Severity) >= BlackBoxResilienceSeverity.Order(BlackBoxSeverityEnum.High)), "every change escalated");
                AssertEqual(3, high.Occurrences30Days);
                AssertContains("seen 3 times in 30 days", high.ProposedChanges[0].MissionBrief ?? "", "the brief cites the recurrence");
                BlackBoxRecord versions = Rec(BlackBoxStepEnum.FastForward, MissionFailureKindEnum.Infra, "fast-forward refused");
                versions.Related = new BlackBoxRelated { Fingerprint = "fp_t", OccurrencesLast30Days = 1, ArmadaVersions = new List<string> { "1.1.0", "1.2.0" } };
                BlackBoxResilience critical = Analyze(versions).Resilience!;
                AssertEqual(BlackBoxSeverityEnum.Critical, critical.Severity, "present across two versions is Critical");
                AssertEqual(2, critical.ArmadaVersionCount);
                return Task.CompletedTask;
            }));

            cases.Add(Case("summary_and_brief_lead_with_armada", "In scope the summary leads with the Armada angle and the brief puts '## Armada resilience' right after the Summary (before the user-facing advice), with the bias preamble; outside scope the section is present but short", TestTags.Positive, () =>
            {
                BlackBoxRecord record = DockDeleted();
                record.Analysis = Analyze(record);
                AssertStartsWith("Armada's landing could not finish Mission \"Golden mission\" (msn_t) at step TargetIntegration", record.Analysis.Summary ?? "", "summary lead");
                AssertContains("; Armada could have recreated the worktree", record.Analysis.Summary ?? "", "what Armada could have done");
                string brief = new BlackBoxBriefRenderer().Render(Entry(record), record);
                int summary = brief.IndexOf("## Summary", StringComparison.Ordinal);
                int resilience = brief.IndexOf("## Armada resilience", StringComparison.Ordinal);
                int identity = brief.IndexOf("## Identity", StringComparison.Ordinal);
                AssertTrue(summary > 0 && resilience > summary && resilience < identity, "resilience right after the Summary");
                AssertEqual(-1, brief.IndexOf("\n## ", summary + 5, resilience - summary - 5, StringComparison.Ordinal), "nothing between Summary and Armada resilience");
                AssertContains("assumes Armada's handoff and landing could have been more resilient", brief, "bias preamble");
                AssertContains("first look for the Armada fix", brief, "asks for the Armada fix first");
                AssertContains("Self-healable: **Yes**", brief, "self-healable verdict");
                AssertContains("Detection gap (Race)", brief, "detection gap");
                AssertContains("Code pointers (Armada 1.2.0 commit abc9999)", brief, "code pointers with the build commit");
                AssertContains("Proposed Armada changes", brief, "mission briefs");
                AssertContains("Regression test: Services.LandingPipeline", brief, "regression test");
                AssertContains("Start with the Armada angle", brief, "the answer asks for the Armada angle first");
                AssertFalse(brief.Contains('—'), "no em-dashes");

                BlackBoxRecord dirty = DirtyCheckout();
                dirty.Analysis = Analyze(dirty);
                string dirtyBrief = new BlackBoxBriefRenderer().Render(Entry(dirty), dirty);
                AssertTrue(dirtyBrief.IndexOf("## Armada resilience", StringComparison.Ordinal) < dirtyBrief.IndexOf("## What would have prevented it", StringComparison.Ordinal), "the Armada section comes before the user-facing configuration advice");

                BlackBoxRecord usage = Rec(BlackBoxStepEnum.UsageLimit, MissionFailureKindEnum.Infra, "usage limit reached");
                usage.Mechanism!.RuntimeFailureKind = RuntimeFailureKindEnum.UsageLimit;
                usage.Analysis = Analyze(usage);
                string outside = new BlackBoxBriefRenderer().Render(Entry(usage), usage);
                int section = outside.IndexOf("## Armada resilience", StringComparison.Ordinal);
                AssertTrue(section > outside.IndexOf("## Summary", StringComparison.Ordinal), "outside scope the section is still there");
                string body = outside.Substring(section, outside.IndexOf("## Identity", StringComparison.Ordinal) - section);
                AssertContains("No Armada change suggested", body, "short reason");
                AssertTrue(body.Length < 400, "short: " + body.Length);
                AssertFalse(outside.Contains("assumes Armada's handoff and landing"), "no bias preamble outside scope");
                return Task.CompletedTask;
            }));

            cases.Add(Case("handoff_trail", "The handoff trail orders mission, dock and merge entry steps, reports the automatic recoveries with their outcome and the target movement, and feeds AttemptedRecoveries", TestTags.Positive, () =>
            {
                DateTime t0 = new DateTime(2026, 10, 28, 10, 0, 0, DateTimeKind.Utc);
                List<ArmadaEvent> mission = new List<ArmadaEvent>
                {
                    Ev("evt_1", "mission.work_produced", t0, "work produced"),
                    Ev("evt_2", "mission.landing_failed", t0.AddMinutes(1), "landing failed"),
                    Ev("evt_3", "mission.landing_auto_retry", t0.AddMinutes(2), "retry 1"),
                    Ev("evt_4", "mission.landing_auto_retry", t0.AddMinutes(4), "retry 2"),
                    Ev("evt_5", "mission.landing_retries_exhausted", t0.AddMinutes(6), "gave up"),
                    Ev("evt_6", "mission.landing_target_advanced", t0.AddMinutes(3), "target advanced"),
                    Ev("evt_7", "mission.activity", t0.AddMinutes(1), "not a handoff step"),
                    Ev("evt_8", "blackbox.captured", t0.AddMinutes(7), "black box")
                };
                List<ArmadaEvent> dock = new List<ArmadaEvent> { Ev("evt_9", "dock.repaired", t0.AddMinutes(5), "worktree restored"), Ev("evt_1", "mission.work_produced", t0, "duplicate id") };
                BlackBoxRecord record = Rec(BlackBoxStepEnum.RetryLanding, MissionFailureKindEnum.Infra, "landing retries exhausted");
                record.Subject!.RedispatchAttempts = 2;
                BlackBoxHandoffTrail trail = BlackBoxHandoffEnricher.Build(record, mission, dock, null);
                AssertEqual("mission.work_produced,mission.landing_failed,mission.landing_auto_retry,mission.landing_target_advanced,mission.landing_auto_retry,dock.repaired,mission.landing_retries_exhausted", String.Join(",", trail.Steps.Select(s => s.Kind)), "ordered, filtered, deduplicated");
                AssertEqual(3, trail.LandingAttempts, "first landing plus two retries");
                AssertTrue(trail.TargetMoved == true, "target advanced");
                BlackBoxAutoRecovery retry = trail.AutoRecoveries.Single(r => r.Kind == BlackBoxAutoRecoveryKindEnum.LandingRetry);
                AssertEqual(2, retry.Attempts); AssertEqual(BlackBoxRecoveryOutcomeEnum.Expired, retry.Outcome, "exhausted retries");
                AssertEqual(BlackBoxRecoveryOutcomeEnum.Succeeded, trail.AutoRecoveries.Single(r => r.Kind == BlackBoxAutoRecoveryKindEnum.DockRepair).Outcome);
                AssertEqual(2, trail.AutoRecoveries.Single(r => r.Kind == BlackBoxAutoRecoveryKindEnum.Redispatch).Attempts, "subject counter");
                AssertEqual(1, trail.DockTransitions.Count, "dock transition");
                record.Handoff = trail;
                BlackBoxAnalysis a = Analyze(record);
                AssertTrue(a.Why.Any(w => w.RuleId == "BBX-LAND-013"), "retries exhausted rule");
                AssertTrue(a.Resilience!.AttemptedRecoveries.Any(x => x.StartsWith("LandingRetry x2 (Expired)", StringComparison.Ordinal)), "attempted recoveries: " + String.Join(" | ", a.Resilience.AttemptedRecoveries));
                BlackBoxHandoffTrail empty = BlackBoxHandoffEnricher.Build(Rec(BlackBoxStepEnum.Push), null, null, null);
                AssertEqual(0, empty.Steps.Count); AssertNotNull(empty.Note, "says why it is empty");
                AssertThrows<ArgumentNullException>(() => BlackBoxHandoffEnricher.Build(null!, null, null, null));
                return Task.CompletedTask;
            }));

            cases.Add(Case("pipeline_trail_gaps_and_bundle", "A landing failure gets its handoff trail and evidence object, shows up as a resilience gap grouped by fingerprint, and the maintainer bundle defaults to the gaps with their proposed changes", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver);
                Mission landing = await BlackBoxTestKit.FailedMissionAsync(t.Driver, null, null, MissionFailureKindEnum.Infra, "An error occurred trying to start process 'git' with working directory '/work/docks/gone'. No such file or directory", MissionStatusEnum.LandingFailed).ConfigureAwait(false);
                string landingId = (await bb.Capture.RecordMissionFailureAsync(landing, MissionStatusEnum.WorkProduced, BlackBoxStepEnum.TargetIntegration).ConfigureAwait(false))!;
                BlackBoxEntry analyzed = (await bb.Pipeline.ProcessAsync(landingId).ConfigureAwait(false))!;
                AssertEqual(BlackBoxTriageEnum.ArmadaDefect, analyzed.Triage, "landing triage");
                BlackBoxRecord record = await bb.Service.GetRecordAsync(BlackBoxTestKit.Admin(), landingId).ConfigureAwait(false);
                AssertNotNull(record.Handoff, "handoff trail stored in the record");
                AssertTrue(record.Analysis!.Resilience!.InScope, "resilience in the stored record");
                EnumerationResult<BlackBoxObjectSummary> objects = await bb.Service.EnumerateObjectsAsync(BlackBoxTestKit.Admin(), landingId, null).ConfigureAwait(false);
                AssertTrue(objects.Objects.Any(o => o.Name == BlackBoxObjectNames.HandoffTimeline), "evidence/handoff-timeline.json stored");
                BlackBoxSectionResult section = await bb.Service.GetSectionAsync(BlackBoxTestKit.Admin(), landingId, BlackBoxSectionEnum.Resilience).ConfigureAwait(false);
                AssertNotNull(section.Record.Analysis?.Resilience, "Resilience section read");
                BlackBoxSectionResult handoff = await bb.Service.GetSectionAsync(BlackBoxTestKit.Admin(), landingId, BlackBoxSectionEnum.Handoff).ConfigureAwait(false);
                AssertNotNull(handoff.Record.Handoff, "Handoff section read");
                BlackBoxDigest digest = await bb.Service.GetDigestAsync(BlackBoxTestKit.Admin(), landingId).ConfigureAwait(false);
                AssertTrue(digest.ResilienceInScope, "digest scope");
                AssertEqual(BlackBoxSelfHealableEnum.Yes, digest.SelfHealable, "digest self-healable");
                AssertNotNull(digest.DetectionGap, "digest detection gap");
                AssertNotNull(digest.TopProposedChange, "digest top change");
                AssertNull(digest.TopProposedChange!.MissionBrief, "the compact change leaves the brief out");

                Mission usage = await BlackBoxTestKit.FailedMissionAsync(t.Driver, null, null, MissionFailureKindEnum.Infra, "usage limit reached", MissionStatusEnum.Failed).ConfigureAwait(false);
                string usageId = (await bb.Capture.RecordMissionFailureAsync(usage, MissionStatusEnum.InProgress, BlackBoxStepEnum.UsageLimit).ConfigureAwait(false))!;
                await bb.Pipeline.ProcessAsync(usageId).ConfigureAwait(false);
                BlackBoxRecord usageRecord = await bb.Service.GetRecordAsync(BlackBoxTestKit.Admin(), usageId).ConfigureAwait(false);
                AssertNull(usageRecord.Handoff, "no handoff trail outside scope");

                EnumerationResult<BlackBoxResilienceGap> gaps = await bb.Service.EnumerateResilienceGapsAsync(BlackBoxTestKit.Admin(), new BlackBoxResilienceGapRequest { PageSize = 10 }).ConfigureAwait(false);
                AssertEqual(1, gaps.Objects.Count, "only the in-scope ArmadaDefect failure is a gap");
                BlackBoxResilienceGap gap = gaps.Objects[0];
                AssertEqual(landingId, gap.LatestEntryId); AssertEqual(1L, gap.Occurrences);
                AssertEqual(BlackBoxSelfHealableEnum.Yes, gap.SelfHealable); AssertNotNull(gap.TopProposedChange);
                AssertEqual(landing.Id, gap.LatestMissionId);
                EnumerationResult<BlackBoxResilienceGap> big = await bb.Service.EnumerateResilienceGapsAsync(BlackBoxTestKit.Admin(), new BlackBoxResilienceGapRequest { PageSize = 500 }).ConfigureAwait(false);
                AssertTrue(big.PageSize <= BlackBoxLimits.MaxResilienceGapPageSize, "page size capped at 50");
                EnumerationResult<BlackBoxResilienceGap> other = await bb.Service.EnumerateResilienceGapsAsync(BlackBoxTestKit.User("ten_other", "usr_other"), null).ConfigureAwait(false);
                AssertEqual(0, other.Objects.Count, "gaps are scoped to the caller");
                await AssertThrowsAsync<ArgumentException>(() => bb.Service.EnumerateResilienceGapsAsync(BlackBoxTestKit.Admin(), new BlackBoxResilienceGapRequest { CreatedAfter = DateTime.UtcNow, CreatedBefore = DateTime.UtcNow.AddDays(-1) }), "bad window").ConfigureAwait(false);

                EnumerationResult<BlackBoxEntrySummary> inScope = await bb.Service.EnumerateAsync(BlackBoxTestKit.Admin(), new BlackBoxEnumerateRequest { ResilienceScope = true }).ConfigureAwait(false);
                AssertEqual(landingId, inScope.Objects.Single().Id, "ResilienceScope filter (true)");
                EnumerationResult<BlackBoxEntrySummary> outScope = await bb.Service.EnumerateAsync(BlackBoxTestKit.Admin(), new BlackBoxEnumerateRequest { ResilienceScope = false }).ConfigureAwait(false);
                AssertEqual(usageId, outScope.Objects.Single().Id, "ResilienceScope filter (false)");

                BlackBoxBundleResult bundle = await bb.Service.GetBundleAsync(BlackBoxTestKit.Admin(), new BlackBoxBundleRequest()).ConfigureAwait(false);
                AssertEqual(1, bundle.EntryCount, "the default bundle holds the resilience gaps only");
                AssertContains("Produce Armada missions", bundle.Content ?? "", "preamble asks for Armada missions");
                AssertContains("Armada resilience gaps", bundle.Content ?? "", "gaps preamble");
                AssertContains("Proposed Armada changes (write each as an Armada mission)", bundle.Content ?? "", "group carries the proposed changes");
                AssertContains("Regression test:", bundle.Content ?? "", "group carries the regression test");
                BlackBoxBundleResult all = await bb.Service.GetBundleAsync(BlackBoxTestKit.Admin(), new BlackBoxBundleRequest { AllFailures = true }).ConfigureAwait(false);
                AssertEqual(2, all.EntryCount, "AllFailures bundles every failure");
                BlackBoxBundleResult json = await bb.Service.GetBundleAsync(BlackBoxTestKit.Admin(), new BlackBoxBundleRequest { Format = BlackBoxBundleFormatEnum.Json }).ConfigureAwait(false);
                BlackBoxBundleDocument doc = System.Text.Json.JsonSerializer.Deserialize<BlackBoxBundleDocument>(json.Content!, BlackBoxJson.Options)!;
                AssertTrue(doc.Groups[0].ProposedChanges.Count > 0 && doc.Groups[0].RegressionTest != null, "JSON groups carry proposed changes and the regression test");
                AssertEqual(BlackBoxSelfHealableEnum.Yes, doc.Groups[0].SelfHealable);
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Black box resilience bias", cases: cases);
        }

        private static BlackBoxAnalysis Analyze(BlackBoxRecord record)
        {
            return BlackBoxAdvisor.Analyze(record, BlackBoxRuleCatalog.Default);
        }

        private static BlackBoxRecord Rec(BlackBoxStepEnum step, MissionFailureKindEnum? kind = null, string? reason = null)
        {
            return new BlackBoxRecord
            {
                EntryId = "bbx_t",
                Subject = new BlackBoxSubject { SubjectType = BlackBoxSubjectTypeEnum.Mission, MissionId = "msn_t", Title = "Golden mission", VesselId = "vsl_t", VesselName = "Vessel T", VesselDefaultBranch = "main", CaptainId = "cpt_t", CaptainRuntime = "ClaudeCode", DockId = "dck_t", BranchName = "armada/msn_t" },
                Site = new BlackBoxSite { Phase = BlackBoxTaxonomy.PhaseOf(step), Step = step, SourceFile = "src/Armada.Server/MissionLandingHandler.cs", Method = "HandleMissionCompleteAsync", Component = "MissionLandingHandler", ArmadaVersion = "1.2.0", ArmadaCommit = "abc9999" },
                Mechanism = new BlackBoxMechanism(),
                Classification = new BlackBoxClassification { Kind = kind, RawReason = reason },
                Workspace = new BlackBoxWorkspace { DockId = "dck_t", BranchName = "armada/msn_t", StartCommit = "abc1234", TargetBranch = "main", WorktreePath = "/work/docks/msn_t", MissionCommit = "def5678" }
            };
        }

        private static BlackBoxRecord DirtyCheckout()
        {
            BlackBoxRecord r = Rec(BlackBoxStepEnum.DirtyCheckout, MissionFailureKindEnum.Infra, "Landing refused: dirty checkout");
            r.Configuration = new BlackBoxConfiguration();
            r.Configuration.Values.Add(new BlackBoxConfigValue { Setting = "LandingMode", Value = "MergeAndPush", Source = BlackBoxConfigSourceEnum.Global });
            r.Workspace!.CheckoutPath = "/work/checkout";
            r.Workspace.CheckoutDirty = true;
            r.Workspace.DirtyPaths = new List<string> { "src/a.cs", "src/b.cs" };
            return r;
        }

        private static BlackBoxRecord DockDeleted()
        {
            BlackBoxRecord r = Rec(BlackBoxStepEnum.TargetIntegration, MissionFailureKindEnum.LandingConflict, "An error occurred trying to start process '/usr/bin/git' with working directory '/work/docks/msn_t'. No such file or directory");
            r.Workspace!.WorktreeExists = false;
            return r;
        }

        private static BlackBoxEntry Entry(BlackBoxRecord record)
        {
            return new BlackBoxEntry { Id = "bbx_t", MissionId = "msn_t", Phase = record.Site!.Phase, Step = record.Site.Step, RulesVersion = BlackBoxRuleCatalog.CurrentRulesVersion, ArmadaVersion = "1.2.0", ArmadaCommit = "abc9999" };
        }

        private static ArmadaEvent Ev(string id, string type, DateTime when, string message)
        {
            return new ArmadaEvent { Id = id, EventType = type, CreatedUtc = when, Message = message, MissionId = "msn_t" };
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(suiteId: SuiteId, caseId: caseId, displayName: displayName, executeAsync: (CancellationToken ct) => body(), tags: new List<string> { tag });
        }
    }
}

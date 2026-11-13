namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
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
    /// Safe contract repair (MODEL_HETEROGENEITY.md, design principle 4): a Worker or Test Engineer with a missing or
    /// malformed completion line is judged by its repository, a Judge verdict in a nearby non-canonical form and a single
    /// valid plan outside a canonical fence are repaired, and everything ambiguous is refused. Every repair is recorded.
    /// </summary>
    public sealed class ContractRepairSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.ContractRepair";

        private const string ValidPlanJson = "{\"missions\":[{\"title\":\"A\",\"description\":\"do a\"},{\"title\":\"B\",\"description\":\"do b\",\"dependsOn\":1}]}";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            // Completion line classification and repository evidence.

            cases.Add(Case("completion_line_classified", "The canonical completion line needs no repair; other forms are malformed; no line is missing", TestTags.Positive, () =>
            {
                AssertEqual(ContractRepairKindEnum.None, ContractRepair.ClassifyCompletionLine("Done.\n[ARMADA:RESULT] COMPLETE\nSummary."));
                AssertEqual(ContractRepairKindEnum.CompletionLineMissing, ContractRepair.ClassifyCompletionLine(null));
                AssertEqual(ContractRepairKindEnum.CompletionLineMissing, ContractRepair.ClassifyCompletionLine(""));
                AssertEqual(ContractRepairKindEnum.CompletionLineMissing, ContractRepair.ClassifyCompletionLine("I changed the parser and ran the tests."));
                foreach (string malformed in new[] { "[ARMADA:RESULT]COMPLETE", "ARMADA:RESULT COMPLETE", "[armada:result] complete", "**[ARMADA RESULT] DONE**", "[ARMADA: RESULT] COMPLETE" })
                    AssertEqual(ContractRepairKindEnum.CompletionLineMalformed, ContractRepair.ClassifyCompletionLine("Work done.\n" + malformed), malformed);
            }));

            cases.Add(Case("repository_evidence", "Only a non-empty diff or a moved dock head is evidence of work; unknown commits are not", TestTags.Negative, () =>
            {
                AssertTrue(ContractRepair.HasNewWork("diff --git a/x b/x", null, null), "a diff is work");
                AssertTrue(ContractRepair.HasNewWork(null, "aaa", "bbb"), "a moved head is work");
                AssertFalse(ContractRepair.HasNewWork(null, "aaa", "AAA "), "the same commit is not work");
                AssertFalse(ContractRepair.HasNewWork("  \n", "aaa", "aaa"), "a blank diff is not work");
                AssertFalse(ContractRepair.HasNewWork(null, null, "bbb"), "an unknown start is not evidence");
                AssertFalse(ContractRepair.HasNewWork(null, "aaa", null), "an unknown head is not evidence");
            }));

            // Judge verdict repair and refusal.

            cases.Add(Case("verdict_nearby_forms_recovered", "A single PASS, FAIL or NEEDS_REVISION written in a nearby non-canonical form is recovered", TestTags.Positive, () =>
            {
                Dictionary<string, JudgeVerdictEnum> forms = new Dictionary<string, JudgeVerdictEnum>
                {
                    ["**[ARMADA:VERDICT] PASS**"] = JudgeVerdictEnum.Pass,
                    ["`[ARMADA:VERDICT] PASS`"] = JudgeVerdictEnum.Pass,
                    ["[ARMADA:VERDICT] PASS."] = JudgeVerdictEnum.Pass,
                    ["[ARMADA:VERDICT]: FAIL"] = JudgeVerdictEnum.Fail,
                    ["ARMADA:VERDICT FAIL"] = JudgeVerdictEnum.Fail,
                    ["## ARMADA VERDICT: NEEDS REVISION"] = JudgeVerdictEnum.NeedsRevision,
                    ["- [ARMADA_VERDICT] = needs-revision"] = JudgeVerdictEnum.NeedsRevision,
                };
                foreach (KeyValuePair<string, JudgeVerdictEnum> form in forms)
                {
                    string output = "## Review\nThe change is correct.\n" + form.Key + "\n";
                    AssertEqual(JudgeVerdictEnum.None, JudgeVerdictParser.Parse(output), "not canonical: " + form.Key);
                    AssertTrue(ContractRepair.TryRecoverVerdict(output, out JudgeVerdictEnum verdict, out string? line), "recovered: " + form.Key);
                    AssertEqual(form.Value, verdict, form.Key);
                    AssertEqual(form.Key, line);

                    string repaired = ContractRepair.AppendRepair(output, ContractRepairKindEnum.VerdictNonCanonical, ContractRepair.CanonicalVerdictLine(verdict));
                    AssertTrue(repaired.StartsWith(output, StringComparison.Ordinal), "the original output is kept above the repair");
                    AssertContains(ContractRepair.AppendedMarkerPrefix + "VerdictNonCanonical", repaired);
                    AssertEqual(form.Value, JudgeVerdictParser.Parse(repaired), "the canonical parser reads the repaired output: " + form.Key);
                }
            }));

            cases.Add(Case("verdict_refusals", "Ambiguous, conflicting, duplicated, fenced, spec-conflict and already-canonical verdicts are never repaired", TestTags.Negative, () =>
            {
                string[] refused = new[]
                {
                    "",
                    "The work looks fine overall.",
                    "[ARMADA:VERDICT] PASS or FAIL",
                    "**[ARMADA:VERDICT] PASS with reservations**",
                    "[ARMADA:VERDICT] MAYBE",
                    "Previous verdict: FAIL",
                    "**[ARMADA:VERDICT] PASS**\n**[ARMADA:VERDICT] FAIL**",
                    "**[ARMADA:VERDICT] PASS**\n[ARMADA:VERDICT]: PASS",
                    "[ARMADA:VERDICT] PASS\n**[ARMADA:VERDICT] FAIL**",
                    "[ARMADA:VERDICT] PASS\n[ARMADA:VERDICT] FAIL",
                    "Example:\n```\n**[ARMADA:VERDICT] PASS**\n```",
                    "**[ARMADA:VERDICT] SPEC_CONFLICT**",
                    "[ARMADA:VERDICT] PASS"
                };
                foreach (string output in refused)
                {
                    AssertFalse(ContractRepair.TryRecoverVerdict(output, out JudgeVerdictEnum verdict, out string? line), "refused: " + output);
                    AssertEqual(JudgeVerdictEnum.None, verdict, output);
                    AssertNull(line, output);
                }
            }));

            // Architect plan repair and refusal.

            cases.Add(Case("plan_single_noncanonical_block_recovered", "A single valid plan in a json fence, a bare fence or an unclosed armada-plan fence is recovered", TestTags.Positive, () =>
            {
                string[] outputs = new[]
                {
                    "Here is the plan.\n```json\n" + ValidPlanJson + "\n```\n",
                    "Here is the plan.\n```\n" + ValidPlanJson + "\n```",
                    "Here is the plan.\n```armada_plan\n" + ValidPlanJson + "\n```",
                    "Here is the plan.\n```armada-plan\n" + ValidPlanJson + "\n"
                };
                foreach (string output in outputs)
                {
                    AssertFalse(ArchitectPlanParser.TryParse(output, out _, out string? error), "not canonical: " + output);
                    AssertNull(error, output);
                    AssertTrue(ContractRepair.TryRecoverArchitectPlan(output, out string? json), "recovered: " + output);
                    AssertEqual(ValidPlanJson, json);

                    string repaired = ContractRepair.AppendRepair(output, ContractRepairKindEnum.PlanBlockNonCanonical, "```armada-plan\n" + json + "\n```");
                    AssertTrue(repaired.StartsWith(output, StringComparison.Ordinal), "the original output is kept");
                    AssertTrue(ArchitectPlanParser.TryParse(repaired, out ArchitectPlan? plan), "the canonical parser reads the repaired output");
                    AssertEqual(2, plan!.Missions.Count);
                }
            }));

            cases.Add(Case("plan_refusals", "Two plan blocks, an existing canonical block, invalid plan JSON and prose are never repaired", TestTags.Negative, () =>
            {
                string[] refused = new[]
                {
                    "",
                    "I would split this into two missions: A then B.",
                    "```json\n" + ValidPlanJson + "\n```\n```json\n" + ValidPlanJson + "\n```",
                    "```json\n" + ValidPlanJson + "\n```\n```armada-plan\n" + ValidPlanJson + "\n```",
                    "```armada-plan\n" + ValidPlanJson + "\n```",
                    "```json\n{\"missions\":[{\"title\":\"A\",\"dependsOn\":\"one\"}]}\n```",
                    "```json\n{\"missions\":[]}\n```",
                    "```json\n{\"title\":\"not a plan\"}\n```"
                };
                foreach (string output in refused)
                {
                    AssertFalse(ContractRepair.TryRecoverArchitectPlan(output, out string? json), "refused: " + output);
                    AssertNull(json, output);
                }
            }));

            // Worker and Test Engineer repairs through the real completion path.

            cases.Add(CaseAsync("worker_missing_line_with_commit_is_repaired_and_continues", "A Worker that committed but wrote no completion line continues the pipeline and records CompletionLineMissing", TestTags.Positive, async () =>
            {
                await WithRepoAsync(async repo =>
                {
                    using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                    Harness h = new Harness(testDb);
                    h.Missions.OnGetMissionOutput = _ => "I updated the parser and its tests; all checks pass.";
                    Scenario s = await Scenario.CreateAsync(testDb, h, repo, "Worker").ConfigureAwait(false);
                    await File.WriteAllTextAsync(Path.Combine(repo, "work.txt"), "work\n").ConfigureAwait(false);
                    await GitAsync(repo, "add", "-A").ConfigureAwait(false);
                    await GitAsync(repo, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "-q", "-m", "captain commit").ConfigureAwait(false);

                    DateTime attemptStarted = DateTime.UtcNow.AddSeconds(-1);
                    await h.Missions.HandleCompletionAsync(s.Captain, s.Mission.Id).ConfigureAwait(false);

                    Mission after = (await testDb.Driver.Missions.ReadAsync(s.Mission.Id).ConfigureAwait(false))!;
                    AssertEqual(MissionStatusEnum.WorkProduced, after.Status, "the work counts as produced: " + after.FailureReason);
                    AssertEqual(0, after.NoOpRedispatchAttempts, "not re-dispatched");
                    AssertEqual(0, after.OutputRepairAttempts, "the model was not asked again");
                    AssertEqual("I updated the parser and its tests; all checks pass.", after.AgentOutput, "the original output is unchanged");

                    Mission next = (await testDb.Driver.Missions.ReadAsync(s.Next!.Id).ConfigureAwait(false))!;
                    AssertEqual(s.Mission.BranchName, next.BranchName, "the next stage was handed the branch");
                    AssertNotEqual("Review the work", next.Description, "the next stage received the handoff");

                    List<ContractRepairRecord> repairs = await RepairsAsync(testDb, s.Mission.Id).ConfigureAwait(false);
                    AssertEqual(1, repairs.Count, "one repair recorded");
                    AssertEqual(ContractRepairKindEnum.CompletionLineMissing, repairs[0].RepairKind);
                    AssertEqual(s.Captain.Id, repairs[0].CaptainId);
                    AssertEqual("Worker", repairs[0].Persona);
                    AssertContains(after.CommitHash!, repairs[0].Detail!);

                    StageOutcome? outcome = await RecordOutcomeAsync(testDb, s.Captain, s.Mission.Id, attemptStarted).ConfigureAwait(false);
                    AssertNotNull(outcome, "stage outcome recorded");
                    AssertEqual(ContractRepairKindEnum.CompletionLineMissing, outcome!.ContractRepairKind, "the repair is on the stage outcome");
                    StageOutcome? stored = await testDb.Driver.StageOutcomes.ReadAsync(outcome.Id).ConfigureAwait(false);
                    AssertEqual(ContractRepairKindEnum.CompletionLineMissing, stored!.ContractRepairKind, "and stored with it");
                }).ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("worker_uncommitted_work_is_committed_and_recorded", "A Worker that left non-scratch work uncommitted and wrote a malformed line records both repairs", TestTags.Positive, async () =>
            {
                await WithRepoAsync(async repo =>
                {
                    using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                    Harness h = new Harness(testDb);
                    h.Missions.OnGetMissionOutput = _ => "Implemented.\n[ARMADA:RESULT]COMPLETE";
                    Scenario s = await Scenario.CreateAsync(testDb, h, repo, "Worker", true).ConfigureAwait(false);
                    await File.WriteAllTextAsync(Path.Combine(repo, "work.txt"), "work\n").ConfigureAwait(false);
                    await File.WriteAllTextAsync(Path.Combine(repo, "work.txt.orig"), "scratch\n").ConfigureAwait(false);

                    await h.Missions.HandleCompletionAsync(s.Captain, s.Mission.Id).ConfigureAwait(false);

                    Mission after = (await testDb.Driver.Missions.ReadAsync(s.Mission.Id).ConfigureAwait(false))!;
                    AssertEqual(MissionStatusEnum.Review, after.Status, "held at its review gate: " + after.FailureReason);
                    string tracked = await GitAsync(repo, "ls-files").ConfigureAwait(false);
                    AssertContains("work.txt", tracked);
                    AssertFalse(tracked.Contains("work.txt.orig", StringComparison.Ordinal), "scratch files stay off the branch");

                    List<ContractRepairKindEnum> kinds = (await RepairsAsync(testDb, s.Mission.Id).ConfigureAwait(false)).Select(r => r.RepairKind).ToList();
                    AssertEqual(2, kinds.Count, String.Join(",", kinds));
                    AssertTrue(kinds.Contains(ContractRepairKindEnum.CommitMissingRecoveredFromRepo), "the missing commit is recorded");
                    AssertTrue(kinds.Contains(ContractRepairKindEnum.CompletionLineMalformed), "the malformed line is recorded");
                }).ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("worker_with_nothing_and_no_line_is_not_repaired", "A Worker with an empty repository diff, only scratch leftovers and a malformed completion line is a failure, not a repair", TestTags.Negative, async () =>
            {
                await WithRepoAsync(async repo =>
                {
                    using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                    Harness h = new Harness(testDb);
                    h.Missions.OnGetMissionOutput = _ => "I'll start by reading the key files.\n[ARMADA:RESULT]COMPLETE";
                    Scenario s = await Scenario.CreateAsync(testDb, h, repo, "Worker").ConfigureAwait(false);
                    await File.WriteAllTextAsync(Path.Combine(repo, "README.md.backup"), "scratch\n").ConfigureAwait(false);

                    await h.Missions.HandleCompletionAsync(s.Captain, s.Mission.Id).ConfigureAwait(false);

                    Mission after = (await testDb.Driver.Missions.ReadAsync(s.Mission.Id).ConfigureAwait(false))!;
                    AssertEqual(1, after.NoOpRedispatchAttempts, "treated as unfinished: " + after.Status);
                    AssertTrue(after.Status != MissionStatusEnum.WorkProduced && after.Status != MissionStatusEnum.Complete, "not handed off");
                    AssertEqual(0, after.OutputRepairAttempts, "no format repair was asked for");
                    AssertEqual(0, (await RepairsAsync(testDb, s.Mission.Id).ConfigureAwait(false)).Count, "no repair recorded");
                }).ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("worker_canonical_line_records_no_repair", "A Worker that committed and wrote the canonical completion line records no repair", TestTags.Negative, async () =>
            {
                await WithRepoAsync(async repo =>
                {
                    using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                    Harness h = new Harness(testDb);
                    h.Missions.OnGetMissionOutput = _ => "Implemented.\n[ARMADA:RESULT] COMPLETE\nSummary.";
                    Scenario s = await Scenario.CreateAsync(testDb, h, repo, "Worker").ConfigureAwait(false);
                    await File.WriteAllTextAsync(Path.Combine(repo, "work.txt"), "work\n").ConfigureAwait(false);
                    await GitAsync(repo, "add", "-A").ConfigureAwait(false);
                    await GitAsync(repo, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "-q", "-m", "captain commit").ConfigureAwait(false);

                    DateTime attemptStarted = DateTime.UtcNow.AddSeconds(-1);
                    await h.Missions.HandleCompletionAsync(s.Captain, s.Mission.Id).ConfigureAwait(false);
                    StageOutcome? outcome = await RecordOutcomeAsync(testDb, s.Captain, s.Mission.Id, attemptStarted).ConfigureAwait(false);
                    AssertNotNull(outcome, "stage outcome recorded");
                    AssertNull(outcome!.ContractRepairKind, "no repair on the stage outcome");

                    Mission after = (await testDb.Driver.Missions.ReadAsync(s.Mission.Id).ConfigureAwait(false))!;
                    AssertEqual(MissionStatusEnum.WorkProduced, after.Status);
                    AssertEqual(0, (await RepairsAsync(testDb, s.Mission.Id).ConfigureAwait(false)).Count, "no repair recorded");
                }).ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("test_engineer_uses_repository_inspection", "A Test Engineer with no completion line is repaired from its commits and is unfinished without them", TestTags.Positive, async () =>
            {
                foreach (bool committed in new[] { true, false })
                {
                    await WithRepoAsync(async repo =>
                    {
                        using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                        Harness h = new Harness(testDb);
                        h.Missions.OnGetMissionOutput = _ => "## Coverage Added\nParser tests.\n## Negative Paths\nEmpty input.\n## Residual Risks\nNone.";
                        Scenario s = await Scenario.CreateAsync(testDb, h, repo, "TestEngineer").ConfigureAwait(false);
                        if (committed)
                        {
                            await File.WriteAllTextAsync(Path.Combine(repo, "test.txt"), "test\n").ConfigureAwait(false);
                            await GitAsync(repo, "add", "-A").ConfigureAwait(false);
                            await GitAsync(repo, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "-q", "-m", "tests").ConfigureAwait(false);
                        }

                        await h.Missions.HandleCompletionAsync(s.Captain, s.Mission.Id).ConfigureAwait(false);

                        Mission after = (await testDb.Driver.Missions.ReadAsync(s.Mission.Id).ConfigureAwait(false))!;
                        List<ContractRepairRecord> repairs = await RepairsAsync(testDb, s.Mission.Id).ConfigureAwait(false);
                        if (committed)
                        {
                            AssertEqual(MissionStatusEnum.WorkProduced, after.Status, after.FailureReason ?? "");
                            AssertEqual(0, after.NoOpRedispatchAttempts);
                            AssertEqual(1, repairs.Count);
                            AssertEqual(ContractRepairKindEnum.CompletionLineMissing, repairs[0].RepairKind);
                            AssertEqual("TestEngineer", repairs[0].Persona);
                        }
                        else
                        {
                            AssertEqual(1, after.NoOpRedispatchAttempts, "an empty Test Engineer with no line is unfinished: " + after.Status);
                            AssertEqual(0, repairs.Count);
                        }
                    }).ConfigureAwait(false);
                }
            }));

            cases.Add(CaseAsync("blocked_test_engineer_is_not_unfinished", "A Test Engineer that ends with the blocked marker and no commit is not treated as unfinished work", TestTags.Negative, async () =>
            {
                await WithRepoAsync(async repo =>
                {
                    using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                    Harness h = new Harness(testDb);
                    h.Missions.OnGetMissionOutput = _ => "## Affected Case\nsrc/Parser.cs:12 throws on empty input.\n" + VerifierContract.BlockedMarker;
                    Scenario s = await Scenario.CreateAsync(testDb, h, repo, "TestEngineer").ConfigureAwait(false);

                    await h.Missions.HandleCompletionAsync(s.Captain, s.Mission.Id).ConfigureAwait(false);

                    Mission after = (await testDb.Driver.Missions.ReadAsync(s.Mission.Id).ConfigureAwait(false))!;
                    AssertEqual(0, after.NoOpRedispatchAttempts, "a blocked verifier is not re-dispatched as a no-op");
                    AssertTrue(after.FailureKind != MissionFailureKindEnum.Blocked, "a verifier's BLOCKED keeps its rework meaning: " + after.Status + " " + after.FailureReason);
                    AssertEqual(0, (await testDb.Driver.Events.EnumerateByMissionAsync(s.Mission.Id, 100).ConfigureAwait(false)).Count(e => e.EventType == CaptainBlockedContract.EventType));
                    AssertEqual(0, (await RepairsAsync(testDb, s.Mission.Id).ConfigureAwait(false)).Count);
                }).ConfigureAwait(false);
            }));

            // A captain that reports BLOCKED is a reported blocker, not an unfinished completion (FIX_LANDING_FAILURES C2).

            cases.Add(Case("blocked_contract_parses_marker_message_and_reason", "The blocked outcome is read from the marker, its result alias, or a BLOCKED message, outside fences, with a bounded reason", TestTags.Positive, () =>
            {
                AssertTrue(CaptainBlockedContract.IsReported("Checked.\n" + VerifierContract.BlockedMarker + "\nMission 1 has not landed."));
                AssertTrue(CaptainBlockedContract.IsReported("Checked.\n" + CaptainBlockedContract.ResultAlias + "\nMission 1 has not landed."));
                AssertTrue(CaptainBlockedContract.IsReported("[ARMADA:MESSAGE] BLOCKED: mission 5 depends on missions 1-4"));
                AssertFalse(CaptainBlockedContract.IsReported(null));
                AssertFalse(CaptainBlockedContract.IsReported("[ARMADA:MESSAGE] not BLOCKED: just a note"));
                AssertFalse(CaptainBlockedContract.IsReported("Quoting the prompt:\n```\n" + VerifierContract.BlockedMarker + "\n```"), "a fenced marker is not a report");

                AssertEqual("mission 5 depends on missions 1-4", CaptainBlockedContract.ExtractReason("[ARMADA:MESSAGE] BLOCKED:   mission 5 depends on missions 1-4  "));
                AssertEqual("Mission 1 has not landed on main.", CaptainBlockedContract.ExtractReason("Checked.\n" + VerifierContract.BlockedMarker + "\n\nMission 1 has not landed\non main."));
                AssertEqual("The brief contradicts itself.", CaptainBlockedContract.ExtractReason("Intro.\n\nThe brief contradicts itself.\n" + VerifierContract.BlockedMarker));
                AssertNull(CaptainBlockedContract.ExtractReason(VerifierContract.BlockedMarker));
                string longReason = CaptainBlockedContract.ExtractReason("[ARMADA:MESSAGE] BLOCKED: " + new string('x', 5000))!;
                AssertEqual(CaptainBlockedContract.MaxReasonLength, longReason.Length, "the reason is bounded");
                AssertTrue(CaptainBlockedContract.HasCompletionOrVerdict("[ARMADA:MESSAGE] BLOCKED: waiting\n[ARMADA:RESULT] COMPLETE"));
            }));

            foreach (string blockedOutput in new[]
            {
                "Inspected the branches; the prerequisites have not landed.\n" + VerifierContract.BlockedMarker + "\nMission 5 depends on missions 1-4, which are not on main yet.",
                "Inspected the branches.\n[ARMADA:MESSAGE] BLOCKED: mission 5 depends on missions 1-4, which are not on main yet."
            })
            {
                bool markerForm = blockedOutput.Contains(VerifierContract.BlockedMarker, StringComparison.Ordinal);
                cases.Add(CaseAsync(markerForm ? "worker_blocked_marker_fails_without_redispatch" : "worker_blocked_message_fails_without_redispatch",
                    "A Worker that reports BLOCKED " + (markerForm ? "with the marker line" : "with a BLOCKED message only") + " and leaves no work fails as Blocked with its reason and is not re-dispatched", TestTags.Positive, async () =>
                {
                    await WithRepoAsync(async repo =>
                    {
                        using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                        Harness h = new Harness(testDb);
                        h.Missions.OnGetMissionOutput = _ => blockedOutput;
                        Scenario s = await Scenario.CreateAsync(testDb, h, repo, "Worker").ConfigureAwait(false);

                        await h.Missions.HandleCompletionAsync(s.Captain, s.Mission.Id).ConfigureAwait(false);

                        Mission after = (await testDb.Driver.Missions.ReadAsync(s.Mission.Id).ConfigureAwait(false))!;
                        AssertEqual(0, after.NoOpRedispatchAttempts, "a reported blocker is not re-dispatched");
                        AssertEqual(MissionStatusEnum.Failed, after.Status);
                        AssertEqual(MissionFailureKindEnum.Blocked, after.FailureKind);
                        AssertStartsWith(CaptainBlockedContract.FailureReasonPrefix, after.FailureReason ?? "");
                        AssertContains("depends on missions 1-4", after.FailureReason ?? "");
                        AssertFalse((after.FailureReason ?? "").Contains("stopping mid-task", StringComparison.Ordinal), "not described as a model stopping mid-task");

                        List<ArmadaEvent> events = await testDb.Driver.Events.EnumerateByMissionAsync(s.Mission.Id, 100).ConfigureAwait(false);
                        ArmadaEvent blocked = events.Single(e => e.EventType == CaptainBlockedContract.EventType);
                        MissionBlockedByCaptainPayload payload = JsonSerializer.Deserialize<MissionBlockedByCaptainPayload>(blocked.Payload!)!;
                        AssertEqual(s.Mission.Id, payload.MissionId);
                        AssertEqual(s.Captain.Id, payload.CaptainId);
                        AssertContains("depends on missions 1-4", payload.Reason ?? "");
                    }).ConfigureAwait(false);
                }));
            }

            cases.Add(CaseAsync("worker_blocked_with_work_is_not_blocked", "A Worker that reports BLOCKED but committed work is handled as produced work, not as a blocker", TestTags.Negative, async () =>
            {
                await WithRepoAsync(async repo =>
                {
                    using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                    Harness h = new Harness(testDb);
                    h.Missions.OnGetMissionOutput = _ => "Did part of it.\n[ARMADA:MESSAGE] BLOCKED: the second half needs mission 1";
                    Scenario s = await Scenario.CreateAsync(testDb, h, repo, "Worker").ConfigureAwait(false);
                    await File.WriteAllTextAsync(Path.Combine(repo, "work.txt"), "work\n").ConfigureAwait(false);
                    await GitAsync(repo, "add", "-A").ConfigureAwait(false);
                    await GitAsync(repo, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "-q", "-m", "partial").ConfigureAwait(false);

                    await h.Missions.HandleCompletionAsync(s.Captain, s.Mission.Id).ConfigureAwait(false);

                    Mission after = (await testDb.Driver.Missions.ReadAsync(s.Mission.Id).ConfigureAwait(false))!;
                    AssertTrue(after.FailureKind != MissionFailureKindEnum.Blocked, "work on the branch is not a reported blocker: " + after.Status);
                    AssertEqual(0, (await testDb.Driver.Events.EnumerateByMissionAsync(s.Mission.Id, 100).ConfigureAwait(false)).Count(e => e.EventType == CaptainBlockedContract.EventType));
                }).ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("unfinished_wording_only_without_result_or_blocking_line", "Exhausted unfinished runs say the model stopped mid-task only when the output had no result line and no blocking message", TestTags.Negative, async () =>
            {
                foreach (bool hasResultLine in new[] { false, true })
                {
                    await WithRepoAsync(async repo =>
                    {
                        using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                        Harness h = new Harness(testDb);
                        h.Settings.MaxNoOpRedispatchAttempts = 0;
                        h.Missions.OnGetMissionOutput = _ => hasResultLine ? "Nothing needed.\n[ARMADA:RESULT]COMPLETE" : "I'll start by reading the key files.";
                        Scenario s = await Scenario.CreateAsync(testDb, h, repo, "Worker").ConfigureAwait(false);

                        await h.Missions.HandleCompletionAsync(s.Captain, s.Mission.Id).ConfigureAwait(false);

                        Mission after = (await testDb.Driver.Missions.ReadAsync(s.Mission.Id).ConfigureAwait(false))!;
                        AssertEqual(MissionStatusEnum.Failed, after.Status);
                        AssertEqual(MissionFailureKindEnum.NoOp, after.FailureKind);
                        AssertEqual(!hasResultLine, (after.FailureReason ?? "").Contains("stopping mid-task", StringComparison.Ordinal), after.FailureReason ?? "");
                    }).ConfigureAwait(false);
                }
            }));

            cases.Add(CaseAsync("empty_worker_output_keeps_unfinished_redispatch", "A Worker with no output and no work is still re-dispatched as unfinished", TestTags.Negative, async () =>
            {
                await WithRepoAsync(async repo =>
                {
                    using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                    Harness h = new Harness(testDb);
                    h.Missions.OnGetMissionOutput = _ => String.Empty;
                    Scenario s = await Scenario.CreateAsync(testDb, h, repo, "Worker").ConfigureAwait(false);

                    await h.Missions.HandleCompletionAsync(s.Captain, s.Mission.Id).ConfigureAwait(false);

                    Mission after = (await testDb.Driver.Missions.ReadAsync(s.Mission.Id).ConfigureAwait(false))!;
                    AssertEqual(1, after.NoOpRedispatchAttempts, "unfinished: " + after.Status);
                    AssertTrue(after.FailureKind != MissionFailureKindEnum.Blocked, "not a blocker");
                }).ConfigureAwait(false);
            }));

            // Judge repair through the real completion path.

            cases.Add(CaseAsync("judge_nearby_verdict_is_repaired_and_recorded", "A Judge verdict in a nearby form is appended canonically and recorded; two verdicts are not", TestTags.Positive, async () =>
            {
                foreach (bool ambiguous in new[] { false, true })
                {
                    await WithRepoAsync(async repo =>
                    {
                        using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                        Harness h = new Harness(testDb);
                        string output = "## Review\nsrc/Parser.cs:12 drops the last token.\n**[ARMADA:VERDICT] NEEDS_REVISION**"
                            + (ambiguous ? "\n**[ARMADA:VERDICT] PASS**" : "");
                        h.Missions.OnGetMissionOutput = _ => output;
                        Scenario s = await Scenario.CreateAsync(testDb, h, repo, "Judge").ConfigureAwait(false);

                        DateTime attemptStarted = DateTime.UtcNow.AddSeconds(-1);
                        await h.Missions.HandleCompletionAsync(s.Captain, s.Mission.Id).ConfigureAwait(false);
                        StageOutcome? outcome = await RecordOutcomeAsync(testDb, s.Captain, s.Mission.Id, attemptStarted).ConfigureAwait(false);
                        AssertNotNull(outcome, "stage outcome recorded");

                        List<ContractRepairRecord> repairs = await RepairsAsync(testDb, s.Mission.Id).ConfigureAwait(false);
                        if (ambiguous)
                        {
                            AssertEqual(0, repairs.Count, "two verdicts are never repaired");
                            AssertNull(outcome!.ContractRepairKind, "no repair on the stage outcome");
                        }
                        else
                        {
                            AssertEqual(1, repairs.Count);
                            AssertEqual(ContractRepairKindEnum.VerdictNonCanonical, repairs[0].RepairKind);
                            AssertContains("**[ARMADA:VERDICT] NEEDS_REVISION**", repairs[0].Detail!);
                            AssertEqual(ContractRepairKindEnum.VerdictNonCanonical, outcome!.ContractRepairKind, "the repair is on the stage outcome");
                        }
                    }).ConfigureAwait(false);
                }
            }));

            // Architect repair through the real completion and handoff path.

            cases.Add(CaseAsync("architect_single_json_plan_is_refenced_and_two_blocks_are_not", "A single valid plan in a json fence fans out to the Worker; two plan blocks go to the existing format repair instead", TestTags.Positive, async () =>
            {
                foreach (bool twoBlocks in new[] { false, true })
                {
                    await WithRepoAsync(async repo =>
                    {
                        using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                        Harness h = new Harness(testDb);
                        string block = "```json\n" + ValidPlanJson + "\n```\n";
                        string output = "Here is the plan.\n" + block + (twoBlocks ? block : "");
                        h.Missions.OnGetMissionOutput = _ => output;
                        Scenario s = await Scenario.CreateAsync(testDb, h, repo, "Architect", false, "Worker").ConfigureAwait(false);

                        await h.Missions.HandleCompletionAsync(s.Captain, s.Mission.Id).ConfigureAwait(false);

                        Mission after = (await testDb.Driver.Missions.ReadAsync(s.Mission.Id).ConfigureAwait(false))!;
                        Mission worker = (await testDb.Driver.Missions.ReadAsync(s.Next!.Id).ConfigureAwait(false))!;
                        List<ContractRepairRecord> repairs = await RepairsAsync(testDb, s.Mission.Id).ConfigureAwait(false);
                        if (twoBlocks)
                        {
                            AssertEqual(0, repairs.Count, "two plan blocks are never repaired");
                            AssertEqual("[Worker] Next", worker.Title, "no plan was dispatched");
                            AssertEqual(1, after.OutputRepairAttempts, "the existing format repair runs instead");
                        }
                        else
                        {
                            AssertEqual(1, repairs.Count);
                            AssertEqual(ContractRepairKindEnum.PlanBlockNonCanonical, repairs[0].RepairKind);
                            AssertEqual(0, after.OutputRepairAttempts, "the model was not asked again");
                            AssertEqual("A [Worker]", worker.Title, "the plan's first mission filled the Worker slot");
                            AssertTrue(after.AgentOutput!.StartsWith(output, StringComparison.Ordinal), "the original output is kept");
                        }
                    }).ConfigureAwait(false);
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Safe contract repair",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task<List<ContractRepairRecord>> RepairsAsync(TestDatabase testDb, string missionId)
        {
            List<ArmadaEvent> events = await testDb.Driver.Events.EnumerateByMissionAsync(missionId, 100).ConfigureAwait(false);
            List<ContractRepairRecord> records = new List<ContractRepairRecord>();
            foreach (ArmadaEvent evt in events.Where(e => e.EventType == ContractRepair.EventType))
                records.Add(JsonSerializer.Deserialize<ContractRepairRecord>(evt.Payload!)!);
            return records;
        }

        private static async Task<StageOutcome?> RecordOutcomeAsync(TestDatabase testDb, Captain captain, string missionId, DateTime startedUtc)
        {
            StageOutcomeService service = new StageOutcomeService(testDb.Driver, CreateLogging());
            StageAttemptCapture capture = new StageAttemptCapture
            {
                Claimed = true,
                MissionId = missionId,
                Captain = captain,
                ExitCode = 0,
                StartedUtc = startedUtc,
                EndedUtc = DateTime.UtcNow
            };
            return await service.RecordAttemptAsync(capture).ConfigureAwait(false);
        }

        private static async Task WithRepoAsync(Func<string, Task> body)
        {
            string repo = Path.Combine(Path.GetTempPath(), "armada_repair_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(repo);
            try
            {
                await GitAsync(repo, "init", "-q", "-b", Scenario.Branch).ConfigureAwait(false);
                await GitAsync(repo, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "-q", "--allow-empty", "-m", "initial").ConfigureAwait(false);
                await body(repo).ConfigureAwait(false);
            }
            finally
            {
                try { Directory.Delete(repo, true); } catch { }
            }
        }

        private static async Task<string> GitAsync(string directory, params string[] args)
        {
            ProcessStartInfo info = new ProcessStartInfo("git") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (string arg in args) info.ArgumentList.Add(arg);
            using Process process = Process.Start(info)!;
            string output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
            await process.WaitForExitAsync().ConfigureAwait(false);
            if (process.ExitCode != 0) throw new InvalidOperationException("git " + String.Join(" ", args) + " exited " + process.ExitCode);
            return output;
        }

        private static LoggingModule CreateLogging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => { body(); return Task.CompletedTask; },
                tags: new List<string> { tag });
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion

        #region Private-Classes

        private sealed class Harness
        {
            public ArmadaSettings Settings { get; }

            public MissionService Missions { get; }

            public Harness(TestDatabase testDb)
            {
                LoggingModule logging = CreateLogging();
                Settings = new ArmadaSettings();
                string root = Path.Combine(Path.GetTempPath(), "armada_repair_settings_" + Guid.NewGuid().ToString("N"));
                Settings.DocksDirectory = Path.Combine(root, "docks");
                Settings.ReposDirectory = Path.Combine(root, "repos");
                Settings.LogDirectory = Path.Combine(root, "logs");
                IGitService dockGit = new DirCreatingGitService();
                IDockService docks = new DockService(logging, testDb.Driver, Settings, dockGit);
                ICaptainService captains = new CaptainService(logging, testDb.Driver, Settings, dockGit, docks);
                Missions = new MissionService(logging, testDb.Driver, Settings, docks, captains, git: new GitService(logging));
            }
        }

        private sealed class Scenario
        {
            public const string Branch = "armada/repair/stage";

            public Captain Captain { get; set; } = null!;

            public Mission Mission { get; set; } = null!;

            public Mission? Next { get; set; } = null;

            public static async Task<Scenario> CreateAsync(TestDatabase testDb, Harness h, string repo, string persona, bool requiresReview = false, string nextPersona = "Judge")
            {
                Vessel vessel = new Vessel("repair-vessel", "https://github.com/test/repo.git");
                vessel.LocalPath = Path.Combine(Path.GetTempPath(), "armada_repair_bare_" + Guid.NewGuid().ToString("N"));
                vessel.WorkingDirectory = Path.Combine(Path.GetTempPath(), "armada_repair_work_" + Guid.NewGuid().ToString("N"));
                vessel.DefaultBranch = "main";
                vessel = await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                Captain captain = new Captain("Mux", AgentRuntimeEnum.Mux);
                captain.State = CaptainStateEnum.Working;
                captain = await CaptainAssignmentFixture.CreateAsync(testDb.Driver, captain).ConfigureAwait(false);
                Voyage voyage = await testDb.Driver.Voyages.CreateAsync(new Voyage("repair") { Status = VoyageStatusEnum.InProgress }).ConfigureAwait(false);

                Mission mission = new Mission("[" + persona + "] Task", "Do the task");
                mission.VesselId = vessel.Id;
                mission.VoyageId = voyage.Id;
                mission.Persona = persona;
                mission.Status = MissionStatusEnum.InProgress;
                mission.CaptainId = captain.Id;
                mission.RequiresReview = requiresReview;
                mission.BranchName = Branch;
                mission.StartedUtc = DateTime.UtcNow.AddMinutes(-5);
                mission.TotalRuntimeMs = 300000;
                mission = await testDb.Driver.Missions.CreateAsync(mission).ConfigureAwait(false);

                Dock dock = new Dock(vessel.Id);
                dock.CaptainId = captain.Id;
                dock.WorktreePath = repo;
                dock.BranchName = Branch;
                dock.Active = true;
                dock = await testDb.Driver.Docks.CreateAsync(dock).ConfigureAwait(false);

                mission.DockId = dock.Id;
                await testDb.Driver.Missions.UpdateAsync(mission).ConfigureAwait(false);
                captain.Assignment = mission;
                captain.Assignment ??= new Mission("fixture assignment"); captain.Assignment.DockId = dock.Id;
                await CaptainAssignmentFixture.UpdateAsync(testDb.Driver, captain).ConfigureAwait(false);

                // The dock started from the repository's current head, as a real dock does.
                string start = (await GitAsync(repo, "rev-parse", "HEAD").ConfigureAwait(false)).Trim();
                string metadata = Path.Combine(h.Settings.LogDirectory, "docks");
                Directory.CreateDirectory(metadata);
                File.WriteAllText(Path.Combine(metadata, dock.Id + ".start"), start + "\n");

                // A held stage keeps its dock (for inspection) only when nothing depends on it.
                Mission? next = null;
                if (!requiresReview)
                {
                    next = new Mission("[" + nextPersona + "] Next", "Review the work");
                    next.VesselId = vessel.Id;
                    next.VoyageId = voyage.Id;
                    next.Persona = nextPersona;
                    next.DependsOnMissionId = mission.Id;
                    next = await testDb.Driver.Missions.CreateAsync(next).ConfigureAwait(false);
                }

                return new Scenario { Captain = captain, Mission = mission, Next = next };
            }
        }

        #endregion
    }
}

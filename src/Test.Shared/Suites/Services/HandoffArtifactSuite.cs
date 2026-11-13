namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using Armada.Server;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Handoff artifacts (docs/MODEL_HETEROGENEITY.md 3.1): the builder (Judge findings, verifier blocks, completion lines,
    /// checks, commits, criteria, missing and oversized output), the budgeted renderer (budget respected, findings first,
    /// did-not-fit list, pointer by reader capabilities, default budget, zero and tiny budgets, a reader with neither MCP
    /// nor files), the reader resolver, the scratch matcher, the stage outcome link, captain-session scoping, and the
    /// flow that carries a Judge's NEEDS_REVISION findings intact into the rework Worker's brief.
    /// </summary>
    public sealed class HandoffArtifactSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.HandoffArtifacts";

        private const string FindingOne = "File: src/Parser.cs:42\nScenario: an empty input string returns success instead of a parse error.\nViolated requirement: AC-1 rejects empty input.\nRequired correction: return ParseError.Empty before tokenizing.";

        private const string FindingTwo = "File: src/Lexer.cs:7\nScenario: a trailing backslash is dropped silently and the token stream ends early.\nRequired fix: keep the backslash and report an unterminated escape.";

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("builder_parses_judge_findings", "A Judge NEEDS_REVISION yields numbered findings with file, line, severity and required fix, plus verdict, protocol lines, commits, criteria and actions", TestTags.Positive, () =>
            {
                Mission judge = JudgeMission(JudgeOutput(0));
                judge.Description = "Fix the parser.\n\n## Acceptance criteria\n- AC-1: reject empty input\n- AC-2: keep escapes\n\n## Notes\n- not a criterion" + "\n\n---\n## Prior Stage Output\n- stale handoff";
                StageCheckResult check = new StageCheckResult { Status = StageCheckStatusEnum.Failed, FailedCommand = "dotnet test", ExitCode = 1 };
                HandoffArtifact artifact = HandoffArtifactBuilder.Build(judge, check, new Captain("judge-cpt"), "sto_x", 2);

                AssertEqual("NEEDS_REVISION", artifact.Verdict, "verdict");
                AssertEqual(HandoffArtifact.CurrentSchemaVersion, artifact.SchemaVersion, "schema");
                AssertEqual("sto_x", artifact.StageOutcomeId, "outcome link");
                AssertEqual(2, artifact.Attempt, "attempt");
                AssertEqual("judge-cpt", artifact.CaptainName, "captain");
                AssertEqual(3, artifact.Findings.Count, "two affected cases and one nonblocking note: " + String.Join(" || ", artifact.Findings.Select(f => f.Summary)));
                HandoffFinding first = artifact.Findings[0];
                AssertEqual(1, first.Number);
                AssertEqual("src/Parser.cs", first.File);
                AssertEqual((int?)42, first.Line);
                AssertEqual(HandoffFindingSeverityEnum.Blocking, first.Severity);
                AssertEqual("return ParseError.Empty before tokenizing.", first.RequiredFix);
                AssertEqual(FindingOne, first.Summary, "finding text intact");
                AssertEqual("src/Lexer.cs", artifact.Findings[1].File);
                AssertEqual((int?)7, artifact.Findings[1].Line);
                AssertEqual("keep the backslash and report an unterminated escape.", artifact.Findings[1].RequiredFix);
                AssertEqual(HandoffFindingSeverityEnum.NonBlocking, artifact.Findings[2].Severity, "labelled nonblocking");
                AssertEqual("[ARMADA:VERDICT] NEEDS_REVISION", artifact.CompletionLines.Single());
                AssertEqual("abc1234|9f8e7d6", String.Join("|", artifact.CommitReferences), "produced commit first, then cited ones");
                AssertEqual("AC-1: reject empty input|AC-2: keep escapes", String.Join("|", artifact.AcceptanceCriteria), "criteria from the task, not the handoff");
                AssertEqual(3, artifact.RequiredActions.Count, "blocking, other, failing check: " + String.Join(" | ", artifact.RequiredActions));
                AssertContains("2 blocking finding(s)", artifact.RequiredActions[0]);
                AssertContains("dotnet test (exit 1)", artifact.RequiredActions[2]);
                AssertEqual(StageCheckStatusEnum.Failed, artifact.CheckResults.Single().Status);
                AssertEqual(judge.AgentOutput, artifact.Output, "full output stored");
                AssertContains("get_mission_diff", artifact.DiffPointer, "diff pointer");
                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("builder_missing_and_oversized_output", "Missing output gives an empty artifact; oversized output and diff are not stored but measured and pointed to", TestTags.Negative, () =>
            {
                Mission empty = new Mission("[Worker] Task", "Do it") { Persona = "Worker" };
                HandoffArtifact none = HandoffArtifactBuilder.Build(empty);
                AssertNull(none.Output, "no output");
                AssertEqual(0, none.OutputLength);
                AssertTrue(none.OutputComplete, "nothing missing");
                AssertNull(none.Diff, "no diff");
                AssertEqual(0, none.Findings.Count, "no findings");
                AssertEqual(0, none.RequiredActions.Count, "no actions");
                AssertNull(none.Verdict, "no verdict");
                AssertNotNull(none.DiffPointer, "pointer always set");
                AssertContains("No findings, checks, output or diff", HandoffRenderer.RenderWithBudget(none, null, 4000).Evidence, "empty rendering says so");

                Mission huge = new Mission("[Worker] Task", "Do it") { Persona = "Worker", AgentOutput = new string('o', HandoffArtifactBuilder.MaxStoredOutputChars + 1) + "\n[ARMADA:RESULT] COMPLETE", DiffSnapshot = new string('d', HandoffArtifactBuilder.MaxStoredDiffChars + 1) };
                HandoffArtifact big = HandoffArtifactBuilder.Build(huge);
                AssertNull(big.Output, "oversized output not stored");
                AssertFalse(big.OutputComplete, "output marked incomplete");
                AssertEqual(huge.AgentOutput!.Length, big.OutputLength, "output measured");
                AssertNull(big.Diff, "oversized diff not stored");
                AssertFalse(big.DiffComplete, "diff marked incomplete");
                AssertEqual("COMPLETE", big.Verdict, "result line still parsed");
                string doc = HandoffRenderer.RenderFullDocument(big);
                AssertContains("too large to store", doc, "full document explains");
                AssertTrue(doc.Length < 5000, "full document does not inline what was not stored");
                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("builder_verifier_block_and_worker_result", "A verifier's BLOCKED affected case is a blocking finding; a Worker's COMPLETE has none", TestTags.Positive, () =>
            {
                Mission tester = new Mission("[Test Engineer] Test", "Test it") { Persona = "Test Engineer", AgentOutput = "Ran tests.\n## Affected Case\nFile: tests/ParserTests.cs:10\nScenario: the empty-input test fails with a NullReferenceException.\n" + VerifierContract.BlockedMarker };
                HandoffArtifact blocked = HandoffArtifactBuilder.Build(tester);
                AssertEqual("BLOCKED", blocked.Verdict);
                AssertEqual(HandoffFindingSeverityEnum.Blocking, blocked.Findings.Single().Severity);
                AssertEqual("tests/ParserTests.cs", blocked.Findings.Single().File);

                Mission worker = new Mission("[Worker] Do", "Do") { Persona = "Worker", AgentOutput = "Implemented in commit 1a2b3c4d.\n[ARMADA:RESULT] COMPLETE" };
                HandoffArtifact done = HandoffArtifactBuilder.Build(worker);
                AssertEqual("COMPLETE", done.Verdict);
                AssertEqual(0, done.Findings.Count);
                AssertEqual("1a2b3c4d", done.CommitReferences.Single());
                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("builder_bounds_agent_lists_with_marker", "Findings, acceptance criteria and protocol lines over 256 keep 255 entries plus a marker naming the omitted count; actions and verdict use the full lists", TestTags.Negative, () =>
            {
                System.Text.StringBuilder output = new System.Text.StringBuilder("Reviewed.\n## Findings\n");
                for (int i = 1; i <= 300; i++) output.Append(i).Append(". File: src/F").Append(i).Append(".cs:").Append(i).Append(" Severity: Blocking. Problem ").Append(i).Append(".\n");
                output.Append("\n");
                for (int i = 0; i < 299; i++) output.Append("[ARMADA:STAGE-OUTCOME] note ").Append(i).Append('\n');
                output.Append("[ARMADA:VERDICT] NEEDS_REVISION\n");
                System.Text.StringBuilder description = new System.Text.StringBuilder("Fix it.\n## Acceptance criteria\n");
                for (int i = 0; i < 270; i++) description.Append("- criterion ").Append(i).Append('\n');
                Mission judge = JudgeMission(output.ToString());
                judge.Description = description.ToString();

                HandoffArtifact artifact = HandoffArtifactBuilder.Build(judge);
                AssertEqual("NEEDS_REVISION", artifact.Verdict, "verdict decided from the full protocol lines");
                AssertEqual(256, artifact.Findings.Count, "findings bounded");
                AssertContains("45 more findings", artifact.Findings[255].Summary);
                AssertContains("of 300", artifact.Findings[255].Summary);
                AssertEqual(256, artifact.CompletionLines.Count, "protocol lines bounded");
                AssertStartsWith(HandoffArtifactBuilder.TruncatedLinePrefix, artifact.CompletionLines[255]);
                AssertContains("45 more of 300", artifact.CompletionLines[255]);
                AssertEqual(256, artifact.AcceptanceCriteria.Count, "criteria bounded");
                AssertContains("15 more of 270", artifact.AcceptanceCriteria[255]);
                AssertContains("300 blocking finding", String.Join(" ", artifact.RequiredActions));
                CollectionBounds.Validate(artifact);
                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("renderer_budget_respected", "Across budgets the rendering never exceeds the budget once the budget covers the pointer and did-not-fit floor", TestTags.Positive, () =>
            {
                HandoffArtifact artifact = BigArtifact();
                int floor = HandoffRenderer.RenderWithBudget(artifact, null, 0).EstimatedTokens;
                foreach (int budget in new[] { floor, floor + 50, 600, 1000, 2500, 4000, 20000 })
                {
                    HandoffRendering rendering = HandoffRenderer.RenderWithBudget(artifact, null, budget);
                    AssertEqual(budget, rendering.BudgetTokens, "budget echoed");
                    AssertEqual(HandoffRenderer.EstimateTokens(rendering.Evidence) + HandoffRenderer.EstimateTokens(rendering.Diff), rendering.EstimatedTokens, "estimate is the delivered text");
                    AssertTrue(rendering.EstimatedTokens <= Math.Max(budget, floor), "budget " + budget + " exceeded: " + rendering.EstimatedTokens);
                    if (budget <= 4000) AssertTrue(rendering.DidNotFit.Count > 0, "a big artifact does not fit whole in " + budget);
                }

                HandoffRendering roomy = HandoffRenderer.RenderWithBudget(artifact, null, 2000000);
                AssertEqual(0, roomy.DidNotFit.Count, "everything fits a huge budget");
                AssertContains("Nothing was left out.", roomy.Evidence);
                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("renderer_findings_first", "Findings and actions are delivered before diff sections, diff sections of files named in findings before others, and the output last", TestTags.Positive, () =>
            {
                HandoffArtifact artifact = BigArtifact();
                int floor = HandoffRenderer.RenderWithBudget(artifact, null, 0).EstimatedTokens;
                int findingsCost = artifact.Findings.Sum(f => HandoffRenderer.EstimateTokens(f.Summary) + 40) + 120;
                HandoffRendering tight = HandoffRenderer.RenderWithBudget(artifact, null, floor + findingsCost);
                AssertContains(FindingOne.Split('\n')[1], tight.Evidence, "first finding delivered");
                AssertContains(FindingTwo.Split('\n')[1], tight.Evidence, "second finding delivered");
                AssertContains("#### Required actions", tight.Evidence, "actions delivered");
                AssertFalse(tight.Diff.Contains("diff --git", StringComparison.Ordinal), "no diff section fits after the findings");
                AssertTrue(tight.DidNotFit.Any(d => d.StartsWith("diff of src/Parser.cs", StringComparison.Ordinal)), "named diff listed as not fitting");
                AssertTrue(tight.DidNotFit.Any(d => d.StartsWith("stage output", StringComparison.Ordinal)), "output listed as not fitting");
                AssertTrue(tight.Evidence.IndexOf("Unresolved findings", StringComparison.Ordinal) < tight.Evidence.IndexOf("#### Complete artifact", StringComparison.Ordinal), "findings before the pointer");
                AssertTrue(tight.Evidence.IndexOf("#### Complete artifact", StringComparison.Ordinal) < tight.Evidence.IndexOf("#### Did not fit", StringComparison.Ordinal), "pointer before the did-not-fit list");

                int namedCost = HandoffRenderer.EstimateTokens(NamedDiff()) + 10;
                HandoffRendering more = HandoffRenderer.RenderWithBudget(artifact, null, floor + findingsCost + namedCost + 60);
                AssertContains("b/src/Parser.cs", more.Diff, "the diff of a file named in a finding comes first");
                AssertFalse(more.Diff.Contains("b/docs/Unrelated.md", StringComparison.Ordinal), "an unnamed diff does not displace it");
                AssertTrue(more.DidNotFit.Any(d => d == "diff of docs/Unrelated.md"), "unnamed diff listed");
                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("renderer_partial_output_listed", "An output that does not fit is delivered as its tail with the cut start listed under Did not fit", TestTags.Positive, () =>
            {
                Mission worker = new Mission("[Worker] Do", "Do") { Persona = "Worker", AgentOutput = "HEAD-MARKER " + new string('w', 20000) + " TAIL-MARKER\n[ARMADA:RESULT] COMPLETE" };
                HandoffRendering rendering = HandoffRenderer.RenderWithBudget(HandoffArtifactBuilder.Build(worker), null, 1500);
                AssertContains("TAIL-MARKER", rendering.Evidence, "the end of the output (completion report) is delivered");
                AssertFalse(rendering.Evidence.Contains("HEAD-MARKER", StringComparison.Ordinal), "the start is cut");
                AssertTrue(rendering.DidNotFit.Any(d => d.StartsWith("stage output", StringComparison.Ordinal) && d.Contains("first ", StringComparison.Ordinal)), "the cut is listed: " + String.Join(" | ", rendering.DidNotFit));
                AssertTrue(rendering.EstimatedTokens <= 1500, "budget respected: " + rendering.EstimatedTokens);
                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("renderer_pointer_by_reader_capabilities", "The pointer uses MCP when the reader has it, a worktree file when it can only read files, and says nothing can reach a reader with neither", TestTags.Positive, () =>
            {
                HandoffArtifact artifact = BigArtifact();
                HandoffRendering unknown = HandoffRenderer.RenderWithBudget(artifact, null, 4000);
                AssertEqual(HandoffPointerKindEnum.Mcp, unknown.Pointer, "unknown reader assumed to have MCP");
                HandoffRendering mcp = HandoffRenderer.RenderWithBudget(artifact, new HandoffReader { HasMcpTools = true, CanReadFiles = true }, 4000);
                AssertEqual(HandoffPointerKindEnum.Mcp, mcp.Pointer);
                AssertContains("get_handoff_artifact with artifactId " + artifact.Id, mcp.Evidence, "MCP pointer names the tool and id");
                AssertNull(mcp.FilePath, "no file for an MCP reader");

                HandoffRendering file = HandoffRenderer.RenderWithBudget(artifact, new HandoffReader { HasMcpTools = false, CanReadFiles = true }, 4000);
                AssertEqual(HandoffPointerKindEnum.File, file.Pointer);
                AssertEqual(".armada/handoff/judge-" + artifact.MissionId + ".md", file.FilePath);
                AssertContains("Read the file " + file.FilePath, file.Evidence, "file pointer names the file");
                AssertFalse(file.Evidence.Contains("get_handoff_artifact", StringComparison.Ordinal), "no MCP tool offered to a file-only reader");

                HandoffRendering neither = HandoffRenderer.RenderWithBudget(artifact, new HandoffReader { HasMcpTools = false, CanReadFiles = false }, 4000);
                AssertEqual(HandoffPointerKindEnum.None, neither.Pointer);
                AssertContains("neither MCP tools nor file access", neither.Evidence);
                AssertFalse(neither.Evidence.Contains("get_handoff_artifact", StringComparison.Ordinal), "no unusable tool offered");
                AssertContains(FindingOne.Split('\n')[1], neither.Evidence, "findings still delivered within the budget");
                AssertTrue(neither.EstimatedTokens <= 4000, "nothing inlined past the budget for a tool-less reader");
                AssertTrue(neither.DidNotFit.Count > 0, "what did not fit is listed for a tool-less reader");
                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("renderer_budget_resolution", "The budget is one eighth of a known served context, else the configured default; no budget means the default", TestTags.Positive, () =>
            {
                AssertEqual(4000, HandoffRenderer.ResolveBudget(null, 4000), "no reader");
                AssertEqual(4000, HandoffRenderer.ResolveBudget(new HandoffReader(), 4000), "unknown context");
                AssertEqual(4096, HandoffRenderer.ResolveBudget(new HandoffReader { ContextTokens = 32768 }, 4000), "served context");
                AssertEqual(4000, HandoffRenderer.ResolveBudget(new HandoffReader { ContextTokens = 0 }, 4000), "zero context is unknown");
                AssertEqual(0, HandoffRenderer.ResolveBudget(null, -5), "negative default clamps to 0");
                AssertEqual(1234, HandoffRenderer.Render(BigArtifact(), new HandoffReader(), 1234).BudgetTokens, "default applied by Render");
                AssertEqual(Armada.Core.Constants.DefaultHandoffBudgetTokens, new ArmadaSettings().HandoffBudgetTokens, "setting default");
                ArmadaSettings settings = new ArmadaSettings { HandoffBudgetTokens = -1 };
                AssertEqual(0, settings.HandoffBudgetTokens, "setting clamps low");
                settings.HandoffBudgetTokens = 5000000;
                AssertEqual(1000000, settings.HandoffBudgetTokens, "setting clamps high");
                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("renderer_zero_and_tiny_budget", "A zero or tiny budget delivers only the pointer and lists every item as not fitting, without throwing", TestTags.Negative, () =>
            {
                HandoffArtifact artifact = BigArtifact();
                foreach (int budget in new[] { -10, 0, 1, 10 })
                {
                    HandoffRendering rendering = HandoffRenderer.RenderWithBudget(artifact, null, budget);
                    AssertFalse(rendering.Evidence.Contains(FindingOne.Split('\n')[1], StringComparison.Ordinal), "no finding inlined at " + budget);
                    AssertFalse(rendering.Diff.Contains("diff --git", StringComparison.Ordinal), "no diff inlined at " + budget);
                    AssertContains("#### Complete artifact", rendering.Evidence, "pointer always delivered");
                    AssertContains("finding 1 (src/Parser.cs)", rendering.Evidence, "did-not-fit list names the first finding");
                    AssertTrue(rendering.DidNotFit.Count >= artifact.Findings.Count + 3, "every item listed at " + budget + ": " + rendering.DidNotFit.Count);
                }

                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("reader_resolver_hard_facts", "Reader capabilities come from the runtime and the endpoint's inference options", TestTags.Positive, () =>
            {
                HandoffReader unknown = HandoffReaderResolver.Resolve(null, null);
                AssertTrue(unknown.HasMcpTools && unknown.CanReadFiles, "unknown reader: CLI default");
                AssertNull(unknown.ContextTokens, "unknown context");
                HandoffReader claude = HandoffReaderResolver.Resolve(new Captain("c", AgentRuntimeEnum.ClaudeCode), null);
                AssertTrue(claude.HasMcpTools && claude.CanReadFiles, "Claude Code");
                HandoffReader custom = HandoffReaderResolver.Resolve(new Captain("c", AgentRuntimeEnum.Custom), null);
                AssertFalse(custom.HasMcpTools, "Custom: no MCP assumed");
                AssertTrue(custom.CanReadFiles, "Custom reads files");

                ModelEndpoint endpoint = new ModelEndpoint();
                endpoint.InferenceOptions = new InferenceOptions { ContextLength = 32768, ToolsEnabled = false };
                HandoffReader noTools = HandoffReaderResolver.Resolve(new Captain("c", AgentRuntimeEnum.ApiEndpoint), endpoint);
                AssertFalse(noTools.HasMcpTools || noTools.CanReadFiles, "no tools: neither MCP nor files");
                AssertEqual((int?)32768, noTools.ContextTokens, "served context");
                endpoint.InferenceOptions = new InferenceOptions { ToolsEnabled = true, McpToolsEnabled = false };
                HandoffReader filesOnly = HandoffReaderResolver.Resolve(new Captain("c", AgentRuntimeEnum.Mux), endpoint);
                AssertFalse(filesOnly.HasMcpTools, "MCP disabled");
                AssertTrue(filesOnly.CanReadFiles, "files with tools");
                endpoint.InferenceOptions = new InferenceOptions { AllowedTools = new List<string> { "read_file" } };
                AssertFalse(HandoffReaderResolver.Resolve(new Captain("c", AgentRuntimeEnum.ApiEndpoint), endpoint).HasMcpTools, "allow-list without the tool");
                endpoint.InferenceOptions = new InferenceOptions { AllowedTools = new List<string> { "read_file", "armada__get_handoff_artifact" } };
                AssertTrue(HandoffReaderResolver.Resolve(new Captain("c", AgentRuntimeEnum.ApiEndpoint), endpoint).HasMcpTools, "allow-list with the tool");
                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("handoff_files_are_scratch", "Files under .armada/handoff/ are scratch files that are never committed; other paths are not", TestTags.Positive, () =>
            {
                AssertTrue(ScratchFileMatcher.IsScratchFile(".armada/handoff/judge-msn_x.md"), "handoff file");
                AssertTrue(ScratchFileMatcher.IsScratchFile(".armada\\handoff\\worker-msn_x.md"), "backslashes");
                AssertFalse(ScratchFileMatcher.IsScratchFile("src/armada/handoff/Renderer.cs"), "a source path containing the words");
                AssertFalse(ScratchFileMatcher.IsScratchFile(".armada/playbooks/01_X.md"), "other .armada paths unchanged");
                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("outcome_links_artifact_without_duplicates", "Recording a stage outcome stores the attempt's artifact with the check result, completing an artifact a launch stored first instead of duplicating it", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Captain captain = await CaptainAssignmentFixture.CreateAsync(testDb.Driver, new Captain("judge-cpt", AgentRuntimeEnum.ClaudeCode)).ConfigureAwait(false);
                Mission judge = JudgeMission(JudgeOutput(0));
                judge.CaptainId = captain.Id;
                judge.Status = MissionStatusEnum.WorkProduced;
                judge = await testDb.Driver.Missions.CreateAsync(judge).ConfigureAwait(false);

                HandoffArtifactService artifacts = new HandoffArtifactService(testDb.Driver, CreateLogging());
                HandoffArtifact launched = await artifacts.GetOrCreateForStageAsync(judge).ConfigureAwait(false);
                AssertNull(launched.StageOutcomeId, "a launch stores an artifact without an outcome");
                HandoffArtifact again = await artifacts.GetOrCreateForStageAsync(judge).ConfigureAwait(false);
                AssertEqual(launched.Id, again.Id, "a second launch reuses it");

                StageOutcomeService outcomes = new StageOutcomeService(testDb.Driver, CreateLogging());
                StageCheckResult check = StageCheckResult.Skip(StageCheckReasonEnum.ReviewPersona);
                StageOutcome? outcome = await outcomes.RecordAttemptAsync(new StageAttemptCapture { Claimed = true, MissionId = judge.Id, Captain = captain, StageCheck = check }).ConfigureAwait(false);
                AssertNotNull(outcome, "outcome recorded");

                EnumerationResult<HandoffArtifact> all = await testDb.Driver.HandoffArtifacts.EnumerateAsync(new HandoffArtifactQuery { MissionId = judge.Id }).ConfigureAwait(false);
                HandoffArtifact linked = all.Objects.Single();
                AssertEqual(launched.Id, linked.Id, "same artifact completed");
                AssertEqual(outcome!.Id, linked.StageOutcomeId, "linked to the outcome");
                AssertEqual(StageCheckReasonEnum.ReviewPersona, linked.CheckResults.Single().Reason, "skip reason kept");
                AssertEqual(captain.Id, linked.CaptainId);

                judge.AgentOutput = judge.AgentOutput + "\nSecond attempt.";
                await testDb.Driver.Missions.UpdateAsync(judge).ConfigureAwait(false);
                await outcomes.RecordAttemptAsync(new StageAttemptCapture { Claimed = true, MissionId = judge.Id, Captain = captain }).ConfigureAwait(false);
                all = await testDb.Driver.HandoffArtifacts.EnumerateAsync(new HandoffArtifactQuery { MissionId = judge.Id }).ConfigureAwait(false);
                AssertEqual(2, (int)all.TotalRecords, "a new attempt gets its own artifact");
                AssertEqual(2, all.Objects[0].Attempt, "attempt numbered from the outcome");
            }));

            cases.Add(CaseAsync("captain_session_scope", "A captain session reads artifacts of its own voyage and dependency, not another voyage's; an Ask session is refused", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Voyage one = await testDb.Driver.Voyages.CreateAsync(new Voyage("one") { TenantId = Armada.Core.Constants.DefaultTenantId }).ConfigureAwait(false);
                Voyage two = await testDb.Driver.Voyages.CreateAsync(new Voyage("two") { TenantId = Armada.Core.Constants.DefaultTenantId }).ConfigureAwait(false);
                Mission judge = await testDb.Driver.Missions.CreateAsync(new Mission("[Judge] R", "R") { TenantId = Armada.Core.Constants.DefaultTenantId, VoyageId = one.Id, Persona = "Judge" }).ConfigureAwait(false);
                Mission rework = await testDb.Driver.Missions.CreateAsync(new Mission("[Worker] W", "W") { TenantId = Armada.Core.Constants.DefaultTenantId, VoyageId = one.Id, DependsOnMissionId = judge.Id }).ConfigureAwait(false);
                Mission elsewhere = await testDb.Driver.Missions.CreateAsync(new Mission("[Judge] X", "X") { TenantId = Armada.Core.Constants.DefaultTenantId, VoyageId = two.Id }).ConfigureAwait(false);
                HandoffArtifact mine = await testDb.Driver.HandoffArtifacts.CreateAsync(HandoffArtifactBuilder.Build(judge)).ConfigureAwait(false);
                HandoffArtifact theirs = await testDb.Driver.HandoffArtifacts.CreateAsync(HandoffArtifactBuilder.Build(elsewhere)).ConfigureAwait(false);

                HandoffArtifactAccessService access = new HandoffArtifactAccessService(testDb.Driver);
                AuthContext captainSession = AuthContext.Authenticated(Armada.Core.Constants.DefaultTenantId, "usr_owner", false, false, "Bearer");
                captainSession.MissionId = rework.Id;
                AssertEqual(mine.Id, (await access.GetAsync(captainSession, mine.Id).ConfigureAwait(false)).Id, "its dependency's artifact");
                AssertEqual(1, (int)(await access.ListByMissionAsync(captainSession, judge.Id).ConfigureAwait(false)).TotalRecords, "lists its dependency");
                await AssertThrowsAsync<KeyNotFoundException>(() => access.GetAsync(captainSession, theirs.Id), "another voyage").ConfigureAwait(false);
                await AssertThrowsAsync<KeyNotFoundException>(() => access.ListByMissionAsync(captainSession, elsewhere.Id), "another voyage's mission").ConfigureAwait(false);

                AuthContext person = AuthContext.Authenticated(Armada.Core.Constants.DefaultTenantId, "usr_owner", false, false, "Bearer");
                AssertEqual(theirs.Id, (await access.GetAsync(person, theirs.Id).ConfigureAwait(false)).Id, "a person sees the whole tenant");
                AuthContext otherTenant = AuthContext.Authenticated("ten_other", "usr_other", false, false, "Bearer");
                await AssertThrowsAsync<KeyNotFoundException>(() => access.GetAsync(otherTenant, mine.Id), "another tenant").ConfigureAwait(false);
                AuthContext ask = AuthContext.Authenticated(Armada.Core.Constants.DefaultTenantId, "usr_owner", false, false, "Bearer");
                ask.AskThreadId = "ath_x";
                await AssertThrowsAsync<UnauthorizedAccessException>(() => access.GetAsync(ask, mine.Id), "Ask session").ConfigureAwait(false);
                await AssertThrowsAsync<UnauthorizedAccessException>(() => access.GetAsync(new AuthContext(), mine.Id), "unauthenticated").ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("judge_findings_reach_rework_worker_intact", "A Judge's NEEDS_REVISION findings reach the rework Worker's brief intact through the artifact, where the old 3000-character excerpt would have cut them", TestTags.Reliability, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                string output = JudgeOutput(6000);
                string legacyExcerpt = output.Substring(0, 1500) + output.Substring(output.Length - 1500);
                AssertFalse(legacyExcerpt.Contains("src/Parser.cs:42", StringComparison.Ordinal), "precondition: the old head-and-tail excerpt loses the findings");

                Flow flow = await Flow.StartAsync(testDb, output, AgentRuntimeEnum.ClaudeCode).ConfigureAwait(false);
                string brief = await flow.GenerateReworkBriefAsync().ConfigureAwait(false);
                foreach (string line in (FindingOne + "\n" + FindingTwo).Split('\n'))
                    AssertContains(line, brief, "finding line delivered intact");
                AssertContains("1. [Blocking] src/Parser.cs:42 -- Required fix: return ParseError.Empty before tokenizing.", brief, "numbered finding with file, line and fix");
                AssertContains("2. [Blocking] src/Lexer.cs:7", brief, "second numbered finding");
                AssertContains("get_handoff_artifact with artifactId", brief, "MCP pointer for a Claude Code reader");
                AssertContains("#### Did not fit", brief, "explicit did-not-fit section");
                AssertFalse(brief.Contains(PipelineHandoffContext.ArtifactPlaceholder, StringComparison.Ordinal), "placeholder filled");
                // Phase 3 item 2: the findings are listed once in the rework section, then the demands, then the contract last.
                string parserFinding = "1. [Blocking] src/Parser.cs:42 -- Required fix:";
                AssertEqual(brief.IndexOf(parserFinding, StringComparison.Ordinal), brief.LastIndexOf(parserFinding, StringComparison.Ordinal), "finding delivered once");
                AssertTrue(brief.IndexOf("### Findings to fix", StringComparison.Ordinal) < brief.IndexOf(parserFinding, StringComparison.Ordinal), "finding in the rework section");
                AssertContains("### Rework demands\n1. Resolve every reported finding.\n2. ", brief, "numbered demands, one per line");
                AssertFalse(brief.Contains(ReworkPromptBuilder.FindingsPlaceholder, StringComparison.Ordinal), "findings placeholder filled");
                AssertTrue(brief.IndexOf("#### Did not fit", StringComparison.Ordinal) < brief.LastIndexOf(ReworkPromptBuilder.OutputContract, StringComparison.Ordinal), "output contract repeated after the handoff");

                HandoffArtifact stored = (await testDb.Driver.HandoffArtifacts.ReadLatestByMissionAsync(flow.Judge.Id).ConfigureAwait(false))!;
                AssertNotNull(stored, "the artifact the pointer names is stored");
                AssertContains(stored.Id, brief, "brief names the stored artifact");
                AssertEqual(output.TrimEnd(), stored.Output!.TrimEnd(), "the complete review is in the artifact");
            }));

            cases.Add(CaseAsync("file_only_reader_gets_worktree_file", "A rework Worker that can only read files gets the complete artifact as a git-excluded file in its worktree and a pointer to it", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Flow flow = await Flow.StartAsync(testDb, JudgeOutput(6000), AgentRuntimeEnum.Custom).ConfigureAwait(false);
                await GitAsync(flow.WorktreePath, "init", "-q").ConfigureAwait(false);
                string brief = await flow.GenerateReworkBriefAsync().ConfigureAwait(false);
                string relative = ".armada/handoff/judge-" + flow.Judge.Id + ".md";
                AssertContains("Read the file " + relative, brief, "file pointer");
                AssertFalse(brief.Contains("get_handoff_artifact with artifactId", StringComparison.Ordinal), "no MCP pointer for a file-only reader");
                string file = Path.Combine(flow.WorktreePath, ".armada", "handoff", "judge-" + flow.Judge.Id + ".md");
                AssertTrue(File.Exists(file), "handoff file written");
                string content = File.ReadAllText(file);
                AssertContains(FindingOne.Split('\n')[3], content, "complete findings in the file");
                AssertContains("[ARMADA:VERDICT] NEEDS_REVISION", content, "complete output in the file");
                string status = await GitAsync(flow.WorktreePath, "status", "--porcelain", "--untracked-files=all").ConfigureAwait(false);
                AssertFalse(status.Contains("handoff", StringComparison.Ordinal), "handoff file is git-excluded: " + status);
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Handoff artifacts: builder, budgeted renderer and delivery",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static string JudgeOutput(int padding)
        {
            return "## Correctness\n" + Filler("The change handles the common path correctly. ", padding)
                + "\n\n## Affected Case\n" + FindingOne + "\n" + FindingTwo + "\n\n"
                + "## Nonblocking Findings\n- Optional: the helper name ParseCore could be clearer.\n\n"
                + "## Blast Radius\nThe parser is used by the importer; see commit 9f8e7d6 for the prior behavior. " + Filler("Callers are otherwise unaffected. ", padding)
                + "\n\n## Source Fidelity\nScope matches the mission.\n\n## Verdict\nNeeds revision.\n[ARMADA:VERDICT] NEEDS_REVISION\n";
        }

        private static string Filler(string sentence, int length)
        {
            System.Text.StringBuilder text = new System.Text.StringBuilder();
            while (text.Length < length) text.Append(sentence);
            return text.ToString();
        }

        private static Mission JudgeMission(string output)
        {
            Mission judge = new Mission("[Judge] Review parser", "Review the parser change.");
            judge.Persona = "Judge";
            judge.AgentOutput = output;
            judge.BranchName = "armada/parser";
            judge.CommitHash = "abc1234";
            return judge;
        }

        private static string NamedDiff()
        {
            return "diff --git a/src/Parser.cs b/src/Parser.cs\n--- a/src/Parser.cs\n+++ b/src/Parser.cs\n@@ -40,3 +40,4 @@\n" + Filler("+    // parser change line\n", 1600);
        }

        private static HandoffArtifact BigArtifact()
        {
            Mission judge = JudgeMission(JudgeOutput(12000));
            judge.Id = "msn_big";
            judge.DiffSnapshot = "diff --git a/docs/Unrelated.md b/docs/Unrelated.md\n--- a/docs/Unrelated.md\n+++ b/docs/Unrelated.md\n@@ -1 +1,2 @@\n" + Filler("+unrelated documentation line\n", 1600)
                + NamedDiff()
                + "diff --git a/src/Big.cs b/src/Big.cs\n" + Filler("+big generated line\n", 40000);
            StageCheckResult check = new StageCheckResult { Status = StageCheckStatusEnum.Failed, FailedCommand = "dotnet test", ExitCode = 1, Commands = new List<string> { "dotnet build", "dotnet test" } };
            return HandoffArtifactBuilder.Build(judge, check);
        }

        private static async Task<string> GitAsync(string directory, params string[] args)
        {
            ProcessStartInfo info = new ProcessStartInfo("git") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (string arg in args) info.ArgumentList.Add(arg);
            using Process process = Process.Start(info)!;
            string output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
            await process.WaitForExitAsync().ConfigureAwait(false);
            return output;
        }

        private static async Task AssertThrowsAsync<T>(Func<Task> action, string message) where T : Exception
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (T)
            {
                return;
            }

            throw new Exception("Expected " + typeof(T).Name + ": " + message);
        }

        private static LoggingModule CreateLogging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
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

        /// <summary>
        /// A Worker and a running Judge in a voyage; the Judge completes with NEEDS_REVISION through the real completion
        /// handler, which schedules the rework Worker and prepares its handoff.
        /// </summary>
        private sealed class Flow
        {
            public MissionService Missions { get; private set; } = null!;

            public Mission Judge { get; private set; } = null!;

            public Mission Rework { get; private set; } = null!;

            public Vessel Vessel { get; private set; } = null!;

            public Captain Reader { get; private set; } = null!;

            public string WorktreePath { get; private set; } = "";

            public static async Task<Flow> StartAsync(TestDatabase testDb, string judgeOutput, AgentRuntimeEnum readerRuntime)
            {
                LoggingModule logging = CreateLogging();
                ArmadaSettings settings = new ArmadaSettings();
                settings.DocksDirectory = Path.Combine(Path.GetTempPath(), "armada_handoff_docks_" + Guid.NewGuid().ToString("N"));
                settings.ReposDirectory = Path.Combine(Path.GetTempPath(), "armada_handoff_repos_" + Guid.NewGuid().ToString("N"));
                settings.LogDirectory = Path.Combine(Path.GetTempPath(), "armada_handoff_logs_" + Guid.NewGuid().ToString("N"));
                IGitService git = new DirCreatingGitService();
                IDockService docks = new DockService(logging, testDb.Driver, settings, git);
                ICaptainService captains = new CaptainService(logging, testDb.Driver, settings, git, docks);
                Flow flow = new Flow();
                flow.Missions = new MissionService(logging, testDb.Driver, settings, docks, captains, git: git);
                flow.Missions.OnGetMissionOutput = _ => judgeOutput;

                Vessel vessel = new Vessel("handoff-vessel", "https://github.com/test/repo.git");
                vessel.LocalPath = Path.Combine(Path.GetTempPath(), "armada_handoff_bare_" + Guid.NewGuid().ToString("N"));
                vessel.WorkingDirectory = Path.Combine(Path.GetTempPath(), "armada_handoff_work_" + Guid.NewGuid().ToString("N"));
                vessel.DefaultBranch = "main";
                flow.Vessel = await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);
                Voyage voyage = await testDb.Driver.Voyages.CreateAsync(new Voyage("handoff") { Status = VoyageStatusEnum.InProgress }).ConfigureAwait(false);

                Mission worker = await testDb.Driver.Missions.CreateAsync(new Mission("Parser fix [Worker]", "Fix the parser.\n\n## Acceptance criteria\n- AC-1: reject empty input")
                { VesselId = flow.Vessel.Id, VoyageId = voyage.Id, Persona = "Worker", Status = MissionStatusEnum.WorkProduced, BranchName = "armada/handoff/parser" }).ConfigureAwait(false);

                Captain judgeCaptain = new Captain("judge-cpt", AgentRuntimeEnum.Codex);
                judgeCaptain.State = CaptainStateEnum.Working;
                judgeCaptain = await CaptainAssignmentFixture.CreateAsync(testDb.Driver, judgeCaptain).ConfigureAwait(false);
                Mission judge = new Mission("[Judge] Parser fix", "Review the parser fix.");
                judge.VesselId = flow.Vessel.Id;
                judge.VoyageId = voyage.Id;
                judge.Persona = "Judge";
                judge.Status = MissionStatusEnum.InProgress;
                judge.CaptainId = judgeCaptain.Id;
                judge.DependsOnMissionId = worker.Id;
                judge.BranchName = worker.BranchName;
                judge = await testDb.Driver.Missions.CreateAsync(judge).ConfigureAwait(false);
                Dock dock = new Dock(flow.Vessel.Id);
                dock.CaptainId = judgeCaptain.Id;
                dock.WorktreePath = Path.Combine(settings.DocksDirectory, flow.Vessel.Name, judge.Id);
                Directory.CreateDirectory(dock.WorktreePath);
                dock.BranchName = judge.BranchName;
                dock.Active = true;
                dock = await testDb.Driver.Docks.CreateAsync(dock).ConfigureAwait(false);
                judge.DockId = dock.Id;
                await testDb.Driver.Missions.UpdateAsync(judge).ConfigureAwait(false);
                judgeCaptain.Assignment = judge;
                judgeCaptain.Assignment ??= new Mission("fixture assignment"); judgeCaptain.Assignment.DockId = dock.Id;
                await CaptainAssignmentFixture.UpdateAsync(testDb.Driver, judgeCaptain).ConfigureAwait(false);

                await flow.Missions.HandleCompletionAsync(judgeCaptain, judge.Id).ConfigureAwait(false);
                flow.Judge = (await testDb.Driver.Missions.ReadAsync(judge.Id).ConfigureAwait(false))!;
                List<Mission> all = await testDb.Driver.Missions.EnumerateByVoyageAsync(voyage.Id).ConfigureAwait(false);
                Mission? rework = all.FirstOrDefault(m => m.DependsOnMissionId == judge.Id && m.Persona == "Worker");
                AssertNotNull(rework, "the Judge's NEEDS_REVISION scheduled a rework Worker");
                flow.Rework = rework!;
                AssertContains(PipelineHandoffContext.ArtifactPlaceholder, flow.Rework.Description, "the persisted handoff carries the placeholder, not an excerpt");
                AssertFalse(flow.Rework.Description!.Contains("src/Parser.cs:42", StringComparison.Ordinal), "no excerpt persisted on the description");

                flow.Reader = await CaptainAssignmentFixture.CreateAsync(testDb.Driver, new Captain("rework-cpt", readerRuntime)).ConfigureAwait(false);
                flow.WorktreePath = Path.Combine(Path.GetTempPath(), "armada_handoff_rework_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(flow.WorktreePath);
                return flow;
            }

            public async Task<string> GenerateReworkBriefAsync()
            {
                await Missions.GenerateClaudeMdAsync(WorktreePath, Rework, Vessel, Reader).ConfigureAwait(false);
                string name = MissionPromptBuilder.GetInstructionsFileName(Reader.Runtime.ToString());
                return File.ReadAllText(Path.Combine(WorktreePath, name));
            }
        }

        #endregion
    }
}

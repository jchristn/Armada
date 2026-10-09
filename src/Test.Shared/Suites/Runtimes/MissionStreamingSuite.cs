namespace Test.Shared.Suites.Runtimes
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Harbor;
    using Armada.Core.Models;
    using Armada.Core.Protocol;
    using Armada.Core.Services;
    using Armada.Runtimes;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Mission streaming: mission launches run Claude Code with stream-json and Codex with exec --json, the runtime decodes
    /// the stream into the readable output text mode prints (so the final message is unchanged) plus typed activity, the
    /// activity parser, the launch display fields on the wire, the Running now row, and the activity throttle.
    /// </summary>
    public sealed class MissionStreamingSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Runtimes.MissionStreaming";

        private const string ClaudeInit = "{\"type\":\"system\",\"subtype\":\"init\",\"model\":\"claude-sonnet-4\"}";
        private const string ClaudeBash = "{\"type\":\"assistant\",\"message\":{\"model\":\"claude-sonnet-4\",\"content\":[{\"type\":\"text\",\"text\":\"I will run the tests.\"},{\"type\":\"tool_use\",\"id\":\"tu_1\",\"name\":\"Bash\",\"input\":{\"command\":\"dotnet test src/App.sln\",\"description\":\"Running tests\"}}]}}";
        private const string ClaudeToolResult = "{\"type\":\"user\",\"message\":{\"content\":[{\"type\":\"tool_result\",\"tool_use_id\":\"tu_1\",\"content\":\"Passed\"}]}}";
        private const string ClaudeThinking = "{\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":\"thinking\",\"thinking\":\"Checking the edge case\"}]}}";
        private const string ClaudeResult = "{\"type\":\"result\",\"subtype\":\"success\",\"is_error\":false,\"duration_ms\":5,\"result\":\"All tests pass.\\n\\n[ARMADA:RESULT] COMPLETE\"}";

        private const string CodexThread = "{\"type\":\"thread.started\",\"thread_id\":\"th_1\"}";
        private const string CodexTurn = "{\"type\":\"turn.started\"}";
        private const string CodexReasoning = "{\"type\":\"item.completed\",\"item\":{\"id\":\"i0\",\"type\":\"reasoning\",\"text\":\"Looking at the tests\"}}";
        private const string CodexCommandStarted = "{\"type\":\"item.started\",\"item\":{\"id\":\"i1\",\"type\":\"command_execution\",\"command\":\"dotnet test\",\"status\":\"in_progress\"}}";
        private const string CodexCommandCompleted = "{\"type\":\"item.completed\",\"item\":{\"id\":\"i1\",\"type\":\"command_execution\",\"command\":\"dotnet test\",\"exit_code\":0,\"status\":\"completed\"}}";
        private const string CodexFirstMessage = "{\"type\":\"item.completed\",\"item\":{\"id\":\"i2\",\"type\":\"agent_message\",\"text\":\"Working on it\"}}";
        private const string CodexFinalMessage = "{\"type\":\"item.completed\",\"item\":{\"id\":\"i3\",\"type\":\"agent_message\",\"text\":\"Done and tested\"}}";
        private const string CodexTurnCompleted = "{\"type\":\"turn.completed\",\"usage\":{\"input_tokens\":10,\"cached_input_tokens\":0,\"output_tokens\":3}}";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            // ---- Command lines ----

            cases.Add(Case("claude_mission_runs_stream_json", "A Claude Code mission with structured progress runs --print --verbose --output-format stream-json, without partial messages", TestTags.Positive, () =>
            {
                InspectableClaude runtime = new InspectableClaude(Logging()) { StructuredProgress = true };
                List<string> args = runtime.Args("do the work");
                AssertTrue(args.Contains("--print"), "print mode");
                AssertTrue(args.Contains("--verbose"), "stream-json in print mode needs --verbose");
                int format = args.IndexOf("--output-format");
                AssertTrue(format >= 0 && args[format + 1] == "stream-json", "stream-json output");
                AssertFalse(args.Contains("--include-partial-messages"), "no per-token deltas for a mission");
            }));

            cases.Add(Case("claude_mission_without_structured_progress_stays_text", "Without structured progress a Claude Code mission keeps text output", TestTags.Negative, () =>
            {
                List<string> args = new InspectableClaude(Logging()).Args("do the work");
                AssertFalse(args.Contains("--output-format"), "text mode");
            }));

            cases.Add(Case("claude_chat_stream_unchanged_by_structured_progress", "A chat launch that streams JSON keeps its partial messages, and structured progress adds no second format", TestTags.Negative, () =>
            {
                List<string> args = new InspectableClaude(Logging()) { StreamJsonOutput = true, StructuredProgress = true }.Args("hello");
                AssertEqual(1, args.Count(a => a == "--output-format"), "one output format");
                AssertTrue(args.Contains("--include-partial-messages"), "chat keeps its deltas");
            }));

            cases.Add(Case("codex_mission_runs_exec_json", "A Codex mission with structured progress runs exec --json and still writes --output-last-message", TestTags.Positive, () =>
            {
                List<string> args = new InspectableCodex(Logging()) { StructuredProgress = true }.Args("do the work", null, "/tmp/final.txt");
                AssertEqual("exec", args[0]);
                AssertEqual(1, args.Count(a => a == "--json"), "--json once");
                int last = args.IndexOf("--output-last-message");
                AssertTrue(last >= 0 && args[last + 1] == "/tmp/final.txt", "the final message file is kept");

                List<string> plain = new InspectableCodex(Logging()).Args("do the work");
                AssertFalse(plain.Contains("--json"), "no --json without structured progress");
                List<string> chat = new InspectableCodex(Logging()) { JsonOutput = true, StructuredProgress = true }.Args("hi");
                AssertEqual(1, chat.Count(a => a == "--json"), "chat and progress share one --json");
            }));

            cases.Add(Case("runtimes_without_structured_output_stay_plain", "Runtimes without structured mission output have no decoder and keep plain text", TestTags.Negative, () =>
            {
                AssertTrue(MissionStreamDecoder.Supports(AgentRuntimeEnum.ClaudeCode));
                AssertTrue(MissionStreamDecoder.Supports(AgentRuntimeEnum.Codex));
                foreach (AgentRuntimeEnum runtime in new AgentRuntimeEnum[] { AgentRuntimeEnum.Gemini, AgentRuntimeEnum.Cursor, AgentRuntimeEnum.Mux, AgentRuntimeEnum.OpenCode, AgentRuntimeEnum.ApiEndpoint })
                {
                    AssertFalse(MissionStreamDecoder.Supports(runtime), runtime + " has no structured mission output");
                    AssertNull(MissionStreamDecoder.For(runtime), runtime + " has no decoder");
                }
            }));

            // ---- Final message and activity from a running CLI ----

            cases.Add(CaseAsync("claude_stream_json_final_message_is_text_mode_output", "A Claude Code mission streaming stream-json raises the result text as its stdout lines (as text mode prints it), raises each activity, and logs readable lines only", TestTags.Positive, async () =>
            {
                string root = TestTemp.NewDirectory("mission_stream_claude");
                string record = Path.Combine(root, "record");
                string shim = StreamingShimCli.Write(Path.Combine(root, "bin"), "claude", record,
                    new List<List<string>> { new List<string> { ClaudeInit, ClaudeBash, ClaudeToolResult, ClaudeThinking, ClaudeResult } }, null, null);
                ClaudeCodeRuntime runtime = new ClaudeCodeRuntime(Logging()) { ExecutablePath = shim, StructuredProgress = true };
                string log = Path.Combine(root, "mission.log");

                RunCapture run = await RunAsync(runtime, root, log, null).ConfigureAwait(false);

                AssertEqual(2, run.Stdout.Count, "only the result text reaches stdout: " + String.Join(" | ", run.Stdout));
                AssertEqual("All tests pass.", run.Stdout[0]);
                AssertEqual("[ARMADA:RESULT] COMPLETE", run.Stdout[1], "protocol lines in the final reply read as before");
                AssertEqual(run.Stdout.Count, run.Output.Count, "the combined output carries the same lines");

                List<string> summaries = run.Activities.Select(a => a.Summary).ToList();
                AssertEqual(3, summaries.Count, String.Join(" | ", summaries));
                AssertEqual("I will run the tests.", summaries[0]);
                AssertEqual("Running tests: dotnet test src/App.sln", summaries[1]);
                AssertEqual("Thinking: Checking the edge case", summaries[2]);
                AssertEqual(RuntimeActivityKindEnum.ToolCall, run.Activities[1].Kind);
                AssertEqual("Bash", run.Activities[1].ToolName);

                string text = File.ReadAllText(log);
                AssertContains("> Running tests: dotnet test src/App.sln", text, "the log shows the activity");
                AssertContains("All tests pass.", text, "the log shows the final reply");
                AssertFalse(text.Contains("\"type\":", StringComparison.Ordinal), "no raw stream-json in the log");
                AssertContains("stream-json", ShimArgs(record), "the CLI was asked for stream-json");
            }));

            cases.Add(CaseAsync("codex_json_final_message_is_text_mode_output", "A Codex mission streaming exec --json raises only the turn's last agent message as stdout, still writes the final message file, and raises each activity", TestTags.Positive, async () =>
            {
                string root = TestTemp.NewDirectory("mission_stream_codex");
                string record = Path.Combine(root, "record");
                string shim = StreamingShimCli.Write(Path.Combine(root, "bin"), "codex", record,
                    new List<List<string>> { new List<string> { CodexThread, CodexTurn, CodexReasoning, CodexCommandStarted, CodexCommandCompleted, CodexFirstMessage, CodexFinalMessage, CodexTurnCompleted } }, null, "Done and tested");
                CodexRuntime runtime = new CodexRuntime(Logging()) { ExecutablePath = shim, StructuredProgress = true };
                string finalFile = Path.Combine(root, "final.txt");

                RunCapture run = await RunAsync(runtime, root, Path.Combine(root, "mission.log"), finalFile).ConfigureAwait(false);

                AssertEqual(1, run.Stdout.Count, "only the final message reaches stdout: " + String.Join(" | ", run.Stdout));
                AssertEqual("Done and tested", run.Stdout[0]);
                AssertEqual("Working on it|Done and tested|Done and tested", String.Join("|", run.Output), "each agent message reaches the combined output as the stderr transcript did, then the final message as stdout");
                AssertEqual("Done and tested", File.ReadAllText(finalFile).Trim(), "the final message file is written as before");
                List<string> summaries = run.Activities.Select(a => a.Summary).ToList();
                AssertEqual(4, summaries.Count, String.Join(" | ", summaries));
                AssertEqual("Thinking: Looking at the tests", summaries[0]);
                AssertEqual("Running dotnet test", summaries[1]);
                AssertEqual("Working on it", summaries[2]);
                AssertEqual("Done and tested", summaries[3]);
                AssertTrue(ShimArgs(record).Split('\n').Contains("--json"), "the CLI was asked for --json");
            }));

            cases.Add(CaseAsync("plain_lines_in_structured_mode_pass_through", "A CLI that prints plain text although structured progress was asked for (an older CLI) reads exactly as text mode", TestTags.Negative, async () =>
            {
                string root = TestTemp.NewDirectory("mission_stream_plain");
                string record = Path.Combine(root, "record");
                string shim = StreamingShimCli.Write(Path.Combine(root, "bin"), "claude", record,
                    new List<List<string>> { new List<string> { "Plain reply line", "[ARMADA:PROGRESS] 50" } }, null, null);
                ClaudeCodeRuntime runtime = new ClaudeCodeRuntime(Logging()) { ExecutablePath = shim, StructuredProgress = true };

                RunCapture run = await RunAsync(runtime, root, null, null).ConfigureAwait(false);

                AssertEqual(2, run.Stdout.Count);
                AssertEqual("Plain reply line", run.Stdout[0]);
                AssertEqual("[ARMADA:PROGRESS] 50", run.Stdout[1]);
                AssertEqual(0, run.Activities.Count, "plain text reports no activity");
            }));

            cases.Add(CaseAsync("harbor_runner_streams_mission_and_observes_ask_turn", "On a Harbor, a mission launch with structured progress reports readable output and activity; an Ask turn that streams JSON keeps its raw events for the Admiral and still reports activity for Running now", TestTags.Positive, async () =>
            {
                string root = TestTemp.NewDirectory("mission_stream_harbor_runner");
                string record = Path.Combine(root, "record");
                string shim = StreamingShimCli.Write(Path.Combine(root, "bin"), "claude", record,
                    new List<List<string>> { new List<string> { ClaudeInit, ClaudeBash, ClaudeResult } }, null, null);
                LoggingModule logging = Logging();
                AgentRuntimeFactory factory = new AgentRuntimeFactory(logging);
                factory.Override(AgentRuntimeEnum.ClaudeCode, () => new ClaudeCodeRuntime(logging) { ExecutablePath = shim });
                LocalHarborJobRunner runner = new LocalHarborJobRunner(logging, factory, Path.Combine(root, "scratch"))
                {
                    JobLogs = new Armada.Core.Hosting.HarborLogPaths(Path.Combine(root, "logs"))
                };

                foreach (bool interactive in new bool[] { false, true })
                {
                    HarborLaunchRequest request = new HarborLaunchRequest
                    {
                        JobId = interactive ? "job-ask" : "job-mission",
                        Runtime = "ClaudeCode",
                        WorkingDirectory = root,
                        JobKind = interactive ? "AskTurn" : "Mission",
                        MissionId = interactive ? null : "msn_runner",
                        Prompt = "go",
                        StreamJsonOutput = interactive,
                        StructuredProgress = !interactive
                    };
                    List<string> stdout = new List<string>();
                    List<RuntimeActivity> activities = new List<RuntimeActivity>();
                    string? logPath = null;
                    Action<string, RuntimeActivity> onActivity = (jobId, activity) =>
                    {
                        if (jobId != request.JobId) return;
                        lock (activities)
                        {
                            activities.Add(activity);
                            logPath = runner.LogPathOf(jobId);
                        }
                    };
                    runner.ActivityReported += onActivity;
                    TaskCompletionSource<int> exited = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                    await runner.StartAsync(request, null, pid => { },
                        (stream, data) => { if (stream == HarborOutputStreamEnum.Stdout) lock (stdout) stdout.Add(data); },
                        code => exited.TrySetResult(code), CancellationToken.None).ConfigureAwait(false);
                    Task done = await Task.WhenAny(exited.Task, Task.Delay(TimeSpan.FromSeconds(60))).ConfigureAwait(false);
                    AssertTrue(done == exited.Task, "the job ended");
                    runner.ActivityReported -= onActivity;

                    AssertEqual(2, activities.Count, "text and the Bash call (" + (interactive ? "Ask turn" : "mission") + ")");
                    AssertEqual("Running tests: dotnet test src/App.sln", activities[1].Summary);
                    AssertNotNull(logPath, "the job's log is known while it runs");
                    AssertNull(runner.LogPathOf(request.JobId), "forgotten after the exit");
                    if (interactive)
                    {
                        AssertEqual(3, stdout.Count, "the raw events reach the Admiral unchanged");
                        AssertEqual(ClaudeBash, stdout[1]);
                    }
                    else
                    {
                        AssertEqual(2, stdout.Count, "only the readable final reply reaches the Admiral");
                        AssertEqual("All tests pass.", stdout[0]);
                        AssertContains("> Running tests: dotnet test src/App.sln", File.ReadAllText(logPath!), "the Harbor's job log shows the activity");
                    }
                }
            }));

            // ---- Decoders ----

            cases.Add(Case("claude_decoder_maps_events", "The Claude decoder: result stands for its text lines, assistant blocks for activities, system and user events for nothing, plain text is not structured", TestTags.Positive, () =>
            {
                ClaudeMissionStreamDecoder decoder = new ClaudeMissionStreamDecoder();
                DateTime now = new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);

                MissionStreamDecodeResult init = decoder.Decode(ClaudeInit, now);
                AssertTrue(init.Structured);
                AssertEqual(0, init.OutputLines.Count + init.Activities.Count, "system init stands for nothing");

                MissionStreamDecodeResult bash = decoder.Decode(ClaudeBash, now);
                AssertEqual(2, bash.Activities.Count, "text and tool_use");
                AssertEqual(now, bash.Activities[1].TimestampUtc);
                AssertEqual(0, bash.OutputLines.Count, "assistant text is activity, not the final reply");

                AssertEqual(0, decoder.Decode(ClaudeToolResult, now).Activities.Count, "tool results stand for nothing");

                MissionStreamDecodeResult result = decoder.Decode(ClaudeResult, now);
                AssertEqual(2, result.OutputLines.Count, "blank lines dropped as the runtime drops them");

                AssertFalse(decoder.Decode("Some warning", now).Structured);
                AssertFalse(decoder.Decode("{\"type\":\"unknown_event\"}", now).Structured, "unknown JSON is not a protocol event");
                AssertEqual(0, decoder.Flush(now).OutputLines.Count);
            }));

            cases.Add(Case("codex_decoder_prints_last_message_at_turn_end", "The Codex decoder prints the turn's last agent message when the turn completes, or when the process ends first, and an error event's message", TestTags.Positive, () =>
            {
                DateTime now = DateTime.UtcNow;
                CodexMissionStreamDecoder decoder = new CodexMissionStreamDecoder();
                MissionStreamDecodeResult first = decoder.Decode(CodexFirstMessage, now);
                AssertEqual(0, first.OutputLines.Count, "held until the turn ends");
                AssertEqual("Working on it", first.TranscriptLines.Single(), "but part of the transcript at once");
                AssertEqual(0, decoder.Decode(CodexFinalMessage, now).OutputLines.Count);
                MissionStreamDecodeResult end = decoder.Decode(CodexTurnCompleted, now);
                AssertEqual(1, end.OutputLines.Count);
                AssertEqual("Done and tested", end.OutputLines[0]);
                AssertEqual(0, decoder.Flush(now).OutputLines.Count, "nothing left after the turn");

                CodexMissionStreamDecoder cut = new CodexMissionStreamDecoder();
                cut.Decode(CodexFinalMessage, now);
                MissionStreamDecodeResult flushed = cut.Flush(now);
                AssertEqual(1, flushed.OutputLines.Count, "a turn that never completed still yields its message");

                MissionStreamDecodeResult error = new CodexMissionStreamDecoder().Decode("{\"type\":\"error\",\"message\":\"stream disconnected\"}", now);
                AssertEqual("stream disconnected", error.OutputLines.Single());
            }));

            // ---- Activity parser ----

            cases.Add(Case("activity_parser_summarizes_claude_tools", "Claude Code tool calls read as short activity lines from their typed input", TestTags.Positive, () =>
            {
                DateTime now = DateTime.UtcNow;
                AssertEqual("Running tests: dotnet test", ClaudeTool("Bash", "{\"command\":\"dotnet test\",\"description\":\"Running tests\"}", now).Summary);
                AssertEqual("Running ls -la", ClaudeTool("Bash", "{\"command\":\"ls -la\"}", now).Summary);
                AssertEqual("Reading src/App.cs", ClaudeTool("Read", "{\"file_path\":\"src/App.cs\"}", now).Summary);
                AssertEqual("Editing src/App.cs", ClaudeTool("Edit", "{\"file_path\":\"src/App.cs\",\"old_string\":\"a\",\"new_string\":\"b\"}", now).Summary);
                AssertEqual("Writing notes.md", ClaudeTool("Write", "{\"file_path\":\"notes.md\",\"content\":\"x\"}", now).Summary);
                AssertEqual("Searching for TODO in src", ClaudeTool("Grep", "{\"pattern\":\"TODO\",\"path\":\"src\"}", now).Summary);
                AssertEqual("Finding files **/*.cs", ClaudeTool("Glob", "{\"pattern\":\"**/*.cs\"}", now).Summary);
                AssertEqual("Calling tool armada_status", ClaudeTool("mcp__armada__armada_status", "{}", now).Summary);
                RuntimeActivity unknown = ClaudeTool("Frobnicate", "not json", now);
                AssertEqual("Calling tool Frobnicate", unknown.Summary, "malformed input still names the tool");
                AssertNull(unknown.Detail);

                RuntimeActivity multiLine = ClaudeTool("Bash", "{\"command\":\"cat <<EOF\\nline two\\nEOF\"}", now);
                AssertEqual("cat <<EOF ...", multiLine.Detail, "only the first line of a command, marked as cut");
            }));

            cases.Add(Case("activity_parser_text_thinking_and_truncation", "Text and reasoning read as their first line, cut to the limit with an ASCII ellipsis", TestTags.Positive, () =>
            {
                DateTime now = DateTime.UtcNow;
                RuntimeActivity? text = RuntimeActivityParser.FromClaudeBlock(new ClaudeStreamContentBlock { Type = "text", Text = "\n  First line  \nsecond" }, now);
                AssertEqual("First line ...", text!.Summary);
                AssertEqual(RuntimeActivityKindEnum.Text, text.Kind);
                AssertNull(RuntimeActivityParser.FromClaudeBlock(new ClaudeStreamContentBlock { Type = "text", Text = "   " }, now), "blank text is no activity");
                RuntimeActivity? thinking = RuntimeActivityParser.FromClaudeBlock(new ClaudeStreamContentBlock { Type = "thinking", Thinking = "" }, now);
                AssertEqual("Thinking", thinking!.Summary, "redacted reasoning still shows the captain is thinking");

                string longLine = new string('x', 500);
                string? cut = RuntimeActivityParser.FirstLine(longLine, RuntimeActivityParser.MaxDetailChars);
                AssertEqual(RuntimeActivityParser.MaxDetailChars, cut!.Length);
                AssertTrue(cut.EndsWith("...", StringComparison.Ordinal));
                foreach (char c in cut) AssertTrue(c < 128, "ASCII only");
            }));

            cases.Add(Case("activity_parser_reads_codex_items", "Codex items read as activity when commands and MCP calls start and when reasoning, file changes, and messages complete", TestTags.Positive, () =>
            {
                DateTime now = DateTime.UtcNow;
                AssertEqual("Running dotnet test", Codex(CodexCommandStarted, now)!.Summary);
                AssertNull(Codex(CodexCommandCompleted, now), "a finished command adds nothing new");
                RuntimeActivity mcp = Codex("{\"type\":\"item.started\",\"item\":{\"id\":\"i9\",\"type\":\"mcp_tool_call\",\"server\":\"armada\",\"tool\":\"armada_status\",\"status\":\"in_progress\"}}", now)!;
                AssertEqual("Calling tool armada_status", mcp.Summary);
                AssertEqual("armada", mcp.Detail);
                AssertEqual("Editing files", Codex("{\"type\":\"item.completed\",\"item\":{\"id\":\"i4\",\"type\":\"file_change\",\"status\":\"completed\"}}", now)!.Summary);
                AssertEqual(RuntimeActivityKindEnum.Thinking, Codex(CodexReasoning, now)!.Kind);
                AssertEqual(RuntimeActivityKindEnum.Text, Codex(CodexFinalMessage, now)!.Kind);
                AssertNull(Codex(CodexTurnCompleted, now));
            }));

            // ---- Launch display fields ----

            cases.Add(Case("launch_display_round_trips", "The launch's display fields and structured-progress flag round-trip through the Harbor protocol, and are absent when unset", TestTags.Positive, () =>
            {
                HarborLaunchRequest request = new HarborLaunchRequest
                {
                    JobId = "job-d",
                    Runtime = "ClaudeCode",
                    JobKind = "Mission",
                    MissionId = "msn_d",
                    StructuredProgress = true,
                    Display = new HarborLaunchDisplay
                    {
                        MissionTitle = "Add rate limiting middleware",
                        VesselName = "PrettyId",
                        VoyageId = "vyg_1",
                        VoyageTitle = "API hardening",
                        VoyagePosition = 2,
                        VoyageMissionCount = 3,
                        CaptainName = "ada",
                        CaptainModel = "claude-sonnet-4",
                        Stage = "Implement",
                        BranchName = "armada/msn_d",
                        AskThreadId = "ath_x",
                        AskQuestion = "What MCP tools do you have?"
                    }
                };

                string json = HarborProtocol.Serialize(request);
                AssertContains("\"display\":{", json, "camelCase nested display");
                AssertContains("\"structuredProgress\":true", json);
                AssertContains("\"voyagePosition\":2", json);
                HarborLaunchRequest back = (HarborLaunchRequest)HarborProtocol.Deserialize(json);
                AssertTrue(back.StructuredProgress);
                HarborLaunchDisplay d = back.Display!;
                AssertEqual("Add rate limiting middleware", d.MissionTitle);
                AssertEqual("PrettyId", d.VesselName);
                AssertEqual("vyg_1", d.VoyageId);
                AssertEqual("API hardening", d.VoyageTitle);
                AssertEqual(2, d.VoyagePosition);
                AssertEqual(3, d.VoyageMissionCount);
                AssertEqual("ada", d.CaptainName);
                AssertEqual("claude-sonnet-4", d.CaptainModel);
                AssertEqual("Implement", d.Stage);
                AssertEqual("armada/msn_d", d.BranchName);
                AssertEqual("ath_x", d.AskThreadId);
                AssertEqual("What MCP tools do you have?", d.AskQuestion);

                string bare = HarborProtocol.Serialize(new HarborLaunchRequest { JobId = "job-e", Runtime = "Codex" });
                AssertFalse(bare.Contains("\"display\"", StringComparison.Ordinal), "no display from an Admiral that sends none");
                AssertNull(((HarborLaunchRequest)HarborProtocol.Deserialize(bare)).Display);

                HarborJobInfo info = HarborJobInfo.FromLaunch(back, DateTime.UtcNow);
                AssertEqual("ada", info.Display!.CaptainName, "the job keeps the display fields");
                AssertNull(HarborJobInfo.FromLaunch(new HarborLaunchRequest { JobId = "j", Display = new HarborLaunchDisplay() }, DateTime.UtcNow).Display, "an empty display is none");

                HarborActivity activity = new HarborActivity { JobId = "job-d", Activity = new RuntimeActivity { Kind = RuntimeActivityKindEnum.ToolCall, ToolName = "Bash", Summary = "Running ls" } };
                string activityJson = HarborProtocol.Serialize(activity);
                AssertContains("\"type\":\"activity\"", activityJson);
                AssertContains("\"kind\":\"ToolCall\"", activityJson, "enum as a string");
                HarborActivity activityBack = (HarborActivity)HarborProtocol.Deserialize(activityJson);
                AssertEqual("Running ls", activityBack.Activity.Summary);
                AssertEqual(RuntimeActivityKindEnum.ToolCall, activityBack.Activity.Kind);
            }));

            cases.Add(Case("launch_display_builder_positions_and_questions", "Display fields name the mission's place in its voyage by creation order, and an Ask turn's question by its first line, cut to the limit", TestTags.Positive, () =>
            {
                Mission mission = new Mission("Second", "d") { VoyageId = "vyg_p", Persona = "Implement", BranchName = "armada/two" };
                Captain captain = new Captain("ada", AgentRuntimeEnum.ClaudeCode) { Model = "claude-sonnet-4" };
                Voyage voyage = new Voyage("API hardening") { Id = "vyg_p" };
                DateTime t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                List<MissionSummary> missions = new List<MissionSummary>
                {
                    new MissionSummary { Id = "msn_c", CreatedUtc = t.AddMinutes(2) },
                    new MissionSummary { Id = mission.Id, CreatedUtc = t.AddMinutes(1) },
                    new MissionSummary { Id = "msn_a", CreatedUtc = t }
                };

                HarborLaunchDisplay display = HarborLaunchDisplayBuilder.ForMission(mission, captain, new Vessel("PrettyId", "https://example.com/p.git"), voyage, missions, "armada/dock");
                AssertEqual(2, display.VoyagePosition);
                AssertEqual(3, display.VoyageMissionCount);
                AssertEqual("API hardening", display.VoyageTitle);
                AssertEqual("armada/dock", display.BranchName, "the dock's branch wins");
                AssertEqual("Implement", display.Stage);
                AssertEqual("claude-sonnet-4", display.CaptainModel);

                HarborLaunchDisplay noVoyage = HarborLaunchDisplayBuilder.ForMission(new Mission("Solo", "d"), captain, null, null, null, null);
                AssertNull(noVoyage.VoyagePosition);
                AssertNull(noVoyage.VesselName);

                HarborLaunchDisplay ask = HarborLaunchDisplayBuilder.ForAskTurn("ath_q", "  What MCP tools do you have for Armada?\nAnd more", captain);
                AssertEqual("What MCP tools do you have for Armada? ...", ask.AskQuestion);
                string longQuestion = HarborLaunchDisplayBuilder.ForAskTurn("ath_q", new string('q', 1000), null).AskQuestion!;
                AssertEqual(HarborLaunchDisplay.MaxQuestionChars, longQuestion.Length);
            }));

            // ---- Running now row ----

            cases.Add(Case("running_now_mission_row", "A mission's Running now row reads as the approved mockup: title, vessel and voyage, captain and stage, branch, activity, and dashboard link", TestTags.Positive, () =>
            {
                DateTime started = new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);
                HarborJobInfo job = new HarborJobInfo
                {
                    JobId = "job-1",
                    Kind = HarborJobKindEnum.Mission,
                    MissionId = "msn_mhq2k7c9xw4e8r1t",
                    CaptainId = "cpt_1",
                    Runtime = "ClaudeCode",
                    StartedUtc = started,
                    Display = new HarborLaunchDisplay
                    {
                        MissionTitle = "Add rate limiting middleware",
                        VesselName = "PrettyId",
                        VoyageId = "vyg_1",
                        VoyageTitle = "API hardening",
                        VoyagePosition = 2,
                        VoyageMissionCount = 3,
                        CaptainName = "ada",
                        CaptainModel = "claude-sonnet-4",
                        Stage = "Implement",
                        BranchName = "armada/msn_mhq2k7c9xw4e8r1t"
                    },
                    LogPath = "/tmp/jobs/msn_mhq2k7c9xw4e8r1t.log"
                };
                job.SetActivity(new RuntimeActivity { Kind = RuntimeActivityKindEnum.ToolCall, Summary = "Running tests: dotnet test src/PrettyId.sln" });

                HarborRunningJobView row = HarborRunningJobView.From(job, started.AddMinutes(12).AddSeconds(34), "http://127.0.0.1:7890/dashboard/");
                AssertEqual("Add rate limiting middleware", row.Title);
                AssertEqual("ClaudeCode", row.Runtime);
                AssertEqual("12m 34s", row.Elapsed);
                AssertEqual("PrettyId  >  Voyage \"API hardening\" (2 of 3)", row.Context);
                AssertEqual("Captain ada (claude-sonnet-4)  -  Implement stage", row.Captain);
                AssertEqual("armada/msn_mhq2k7c9xw4e8r1t", row.Branch);
                AssertEqual("msn_mhq2k7c9xw4e8r1t", row.MissionId);
                AssertEqual("> Running tests: dotnet test src/PrettyId.sln", row.Activity);
                AssertEqual("http://127.0.0.1:7890/dashboard/missions/msn_mhq2k7c9xw4e8r1t", row.DashboardLink);
                AssertEqual("/tmp/jobs/msn_mhq2k7c9xw4e8r1t.log", row.LogPath);
                AssertNull(row.ConversationId);
            }));

            cases.Add(Case("running_now_ask_row", "An Ask turn's Running now row shows the question, the captain, and the conversation, and links to the conversation", TestTags.Positive, () =>
            {
                DateTime started = DateTime.UtcNow;
                HarborJobInfo job = new HarborJobInfo
                {
                    JobId = "job-2",
                    Kind = HarborJobKindEnum.AskTurn,
                    CaptainId = "cpt_2",
                    Runtime = "Codex",
                    StartedUtc = started,
                    Model = "gpt-5",
                    Display = new HarborLaunchDisplay { AskThreadId = "ath_k3", AskQuestion = "What MCP tools do you have for Armada?", CaptainName = "grace" }
                };

                HarborRunningJobView row = HarborRunningJobView.From(job, started.AddSeconds(41), "https://armada.example.com/dashboard");
                AssertEqual("Ask turn: \"What MCP tools do you have for Armada?\"", row.Title);
                AssertEqual("Captain grace (gpt-5)", row.Captain, "the launch model stands in for the captain's");
                AssertEqual("conversation", row.ConversationLabel);
                AssertEqual("ath_k3", row.ConversationId);
                AssertEqual("41s", row.Elapsed);
                AssertNull(row.Context);
                AssertEqual("https://armada.example.com/dashboard/ask/ath_k3", row.DashboardLink);
                AssertNull(row.Activity, "no activity yet");
            }));

            cases.Add(Case("running_now_row_from_older_admiral", "A job from an Admiral that sends no display fields falls back to its kind and IDs, and an unusable dashboard address gives no link", TestTags.Negative, () =>
            {
                HarborJobInfo job = new HarborJobInfo { JobId = "job-3", Kind = HarborJobKindEnum.Mission, MissionId = "msn_old", CaptainId = "cpt_old", Runtime = "" };
                HarborRunningJobView row = HarborRunningJobView.From(job, DateTime.UtcNow, "not a url");
                AssertEqual("Mission", row.Title);
                AssertEqual("Unknown runtime", row.Runtime);
                AssertNull(row.Captain, "no captain name");
                AssertEqual("cpt_old", row.CaptainId, "the captain's ID stands in");
                AssertNull(row.DashboardLink);
                AssertNull(HarborRunningJobView.DashboardPage("ftp://host/dashboard", "/missions/x"), "only http(s)");
                AssertNull(HarborRunningJobView.From(new HarborJobInfo { JobId = "job-4", Kind = HarborJobKindEnum.Planning }, DateTime.UtcNow, "http://h/dashboard").DashboardLink, "no page for a planning job");
            }));

            cases.Add(Case("running_now_keeps_last_five_lines", "A job keeps its last five output lines, activity lines among them, and copies are independent", TestTags.Positive, () =>
            {
                HarborJobInfo job = new HarborJobInfo { JobId = "job-5", Kind = HarborJobKindEnum.Mission };
                for (int i = 1; i <= 6; i++) job.AddRecentLine("line " + i);
                job.AddRecentLine("   ");
                job.SetActivity(new RuntimeActivity { Summary = "Reading README.md" });
                AssertEqual(HarborJobInfo.MaxRecentLines, job.RecentLines.Count);
                AssertEqual("line 3", job.RecentLines[0]);
                AssertEqual("> Reading README.md", job.RecentLines[4]);

                HarborJobInfo copy = job.Clone();
                job.AddRecentLine("later");
                job.Activity!.Summary = "changed";
                AssertEqual("> Reading README.md", copy.RecentLines[4], "the copy's lines do not change");
                AssertEqual("Reading README.md", copy.Activity!.Summary, "the copy's activity does not change");
                AssertEqual(5, HarborRunningJobView.From(copy, DateTime.UtcNow, null).RecentLines.Count);
            }));

            // ---- Throttle ----

            cases.Add(Case("throttle_sends_first_and_newest", "The activity throttle sends the first value at once and only the newest of a burst when the interval ends", TestTags.Positive, () =>
            {
                ManualTimerTimeProvider time = new ManualTimerTimeProvider();
                List<string> sent = new List<string>();
                using (LatestValueThrottle<string> throttle = new LatestValueThrottle<string>(TimeSpan.FromSeconds(1), time, value => sent.Add(value)))
                {
                    throttle.Submit("a");
                    throttle.Submit("b");
                    throttle.Submit("c");
                    AssertEqual(1, sent.Count, "the first goes out at once");
                    time.Advance(TimeSpan.FromMilliseconds(999));
                    AssertEqual(1, sent.Count, "nothing before the interval ends");
                    time.Advance(TimeSpan.FromMilliseconds(1));
                    AssertEqual(2, sent.Count);
                    AssertEqual("c", sent[1], "the newest wins");

                    time.Advance(TimeSpan.FromSeconds(5));
                    throttle.Submit("d");
                    AssertEqual("d", sent[2], "after a quiet interval a value goes out at once");
                    throttle.Submit("e");
                    throttle.Flush();
                    AssertEqual("e", sent[3], "Flush sends a held value");
                    throttle.Submit("f");
                }

                time.Advance(TimeSpan.FromSeconds(5));
                AssertEqual(4, sent.Count, "a disposed throttle drops its held value");
                AssertEqual(0, time.PendingTimers);
            }));

            cases.Add(Case("mission_activity_tracker", "The mission activity tracker keeps copies of the latest activity per mission until cleared", TestTags.Positive, () =>
            {
                MissionActivityTracker tracker = new MissionActivityTracker();
                RuntimeActivity activity = new RuntimeActivity { Summary = "Running ls" };
                tracker.Update("msn_t", activity);
                activity.Summary = "mutated";
                AssertEqual("Running ls", tracker.Get("msn_t")!.Summary, "stored as a copy");
                tracker.Update("msn_t", new RuntimeActivity { Summary = "Reading a.cs" });
                AssertEqual("Reading a.cs", tracker.Get("msn_t")!.Summary, "newest wins");
                AssertNull(tracker.Get("msn_other"));
                tracker.Clear("msn_t");
                AssertNull(tracker.Get("msn_t"));
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Mission Streaming",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static LoggingModule Logging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private static RuntimeActivity ClaudeTool(string name, string input, DateTime now)
        {
            ClaudeStreamContentBlock block = new ClaudeStreamContentBlock { Type = ClaudeStreamContentBlock.TypeToolUse, Name = name, Input = input };
            return RuntimeActivityParser.FromClaudeBlock(block, now)!;
        }

        private static RuntimeActivity? Codex(string line, DateTime now)
        {
            AssertTrue(CodexStreamEvent.TryParse(line, out CodexStreamEvent? evt), "parses: " + line);
            return RuntimeActivityParser.FromCodex(evt!, now);
        }

        private static string ShimArgs(string record)
        {
            return File.ReadAllText(Path.Combine(record, "args.txt")).Replace("\r\n", "\n");
        }

        private static async Task<RunCapture> RunAsync(BaseAgentRuntime runtime, string workingDirectory, string? logFile, string? finalMessageFile)
        {
            RunCapture capture = new RunCapture();
            TaskCompletionSource<int?> exited = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
            runtime.OnStdoutReceived += (pid, line) => { lock (capture) capture.Stdout.Add(line); };
            runtime.OnOutputReceived += (pid, line) => { lock (capture) capture.Output.Add(line); };
            runtime.OnActivity += (pid, activity) => { lock (capture) capture.Activities.Add(activity); };
            runtime.OnProcessExited += (pid, code) => exited.TrySetResult(code);

            await runtime.StartAsync(workingDirectory, "do the work", null, logFile, finalMessageFile).ConfigureAwait(false);
            Task done = await Task.WhenAny(exited.Task, Task.Delay(TimeSpan.FromSeconds(60))).ConfigureAwait(false);
            AssertTrue(done == exited.Task, "the shim exited");
            AssertEqual(0, await exited.Task.ConfigureAwait(false), "clean exit");
            return capture;
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) =>
                {
                    body();
                    return Task.CompletedTask;
                },
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

        #region Private-Types

        private sealed class RunCapture
        {
            public List<string> Stdout { get; } = new List<string>();

            public List<string> Output { get; } = new List<string>();

            public List<RuntimeActivity> Activities { get; } = new List<RuntimeActivity>();
        }

        private sealed class InspectableClaude : ClaudeCodeRuntime
        {
            public InspectableClaude(LoggingModule logging) : base(logging)
            {
            }

            public List<string> Args(string prompt) => BuildArguments(Path.GetTempPath(), prompt, null, null, null);
        }

        private sealed class InspectableCodex : CodexRuntime
        {
            public InspectableCodex(LoggingModule logging) : base(logging)
            {
            }

            public List<string> Args(string prompt, string? model = null, string? finalMessageFilePath = null) =>
                BuildArguments(Path.GetTempPath(), prompt, model, finalMessageFilePath, null);
        }

        #endregion
    }
}

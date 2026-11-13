namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Armada's pi permission gate extension (<c>Pi/armada-permission-gate.ts</c> in the build output), run under node
    /// against a fake Armada MCP endpoint: allow runs the tool, deny blocks it with the reason, an unreachable server, a
    /// timeout, a missing token and an error result block it (fail closed), two concurrent tool calls are decided one at a
    /// time in order, and file and Armada MCP tools need no approval. Skipped when node is not on PATH.
    /// </summary>
    public sealed class PiPermissionGateSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.PiPermissionGate";
        private const string Token = "armada-scoped-token-for-test";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            string? skip = PiPermissionGateHarness.SkipReason();
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("extension_ships_in_build_output", "The extension is copied to Pi/armada-permission-gate.ts in the build output", TestTags.Positive, null, () =>
            {
                AssertTrue(File.Exists(PiPermissionGateHarness.ExtensionPath), "missing " + PiPermissionGateHarness.ExtensionPath);
                string text = File.ReadAllText(PiPermissionGateHarness.ExtensionPath);
                AssertContains("cli_permission_prompt", text);
                AssertContains("pi.on(\"tool_call\"", text);
                foreach (char c in text) AssertTrue(c < 128, "the extension is ASCII");
                return Task.CompletedTask;
            }));

            cases.Add(Case("allow_runs_the_command", "An allow decision lets the bash call run; Armada sees it as Bash with the command, the scoped token and the MCP session", TestTags.Positive, skip, async () =>
            {
                using FakePermissionPromptMcpServer server = new FakePermissionPromptMcpServer();
                PiGateEvent tool = PiGateEvent.Bash("git status", "toolu_allow");
                // Real pi can include a numeric timeout. A string-only fake used to abort the MCP reply.
                tool.Input["timeout"] = 10;
                List<PiGateOutcome> outcomes = await PiPermissionGateHarness.RunAsync(new List<PiGateEvent> { tool }, server.Url, Token, 30).ConfigureAwait(false);

                AssertEqual(1, outcomes.Count);
                AssertTrue(outcomes[0].Allowed, "allowed calls return nothing to pi, so the tool runs");
                AssertNull(outcomes[0].Result, "no block result");
                FakePermissionPromptCall call = server.Calls[0];
                AssertEqual("Bash", call.ToolName, "pi's bash is reported as Bash so Bash permission rules apply");
                AssertEqual("git status", call.Command);
                AssertEqual("toolu_allow", call.ToolUseId);
                AssertEqual(Token, call.Token, "the scoped token is sent as X-Token");
                AssertEqual(FakePermissionPromptMcpServer.SessionId, call.SessionId, "the tool call uses the initialized MCP session");
            }));

            cases.Add(Case("deny_blocks_with_reason", "A deny decision blocks the call and hands Armada's reason to pi as the tool result", TestTags.Negative, skip, async () =>
            {
                using FakePermissionPromptMcpServer server = new FakePermissionPromptMcpServer();
                server.Decide = call => Task.FromResult(FakePermissionPromptMcpServer.Deny("Denied by alice: not on main"));
                List<PiGateOutcome> outcomes = await PiPermissionGateHarness.RunAsync(new List<PiGateEvent> { PiGateEvent.Bash("git push origin main") }, server.Url, Token, 30).ConfigureAwait(false);

                AssertFalse(outcomes[0].Allowed, "denied");
                AssertTrue(outcomes[0].Result!.Block, "block:true");
                AssertEqual("Denied by alice: not on main", outcomes[0].Result!.Reason, "the reason reaches the model verbatim");
                AssertEqual(1, server.Calls.Count, "asked exactly once");
            }));

            cases.Add(Case("event_stream_reply_is_read", "A text/event-stream reply is parsed like a JSON reply (deny)", TestTags.Positive, skip, async () =>
            {
                using FakePermissionPromptMcpServer server = new FakePermissionPromptMcpServer { UseEventStream = true };
                server.Decide = call => Task.FromResult(FakePermissionPromptMcpServer.Deny("streamed no"));
                List<PiGateOutcome> outcomes = await PiPermissionGateHarness.RunAsync(new List<PiGateEvent> { PiGateEvent.Bash("ls") }, server.Url, Token, 30).ConfigureAwait(false);
                AssertEqual("streamed no", outcomes[0].Result?.Reason);
            }));

            cases.Add(Case("unreachable_server_blocks", "When Armada cannot be reached the command does not run (fail closed)", TestTags.Negative, skip, async () =>
            {
                List<PiGateOutcome> outcomes = await PiPermissionGateHarness.RunAsync(new List<PiGateEvent> { PiGateEvent.Bash("rm -rf build") }, FakePermissionPromptMcpServer.UnreachableUrl(), Token, 30).ConfigureAwait(false);
                AssertFalse(outcomes[0].Allowed, "blocked");
                AssertContains("could not be reached", outcomes[0].Result!.Reason ?? "");
            }));

            cases.Add(Case("timeout_blocks", "When no decision arrives within the gate timeout the command does not run (fail closed)", TestTags.Negative, skip, async () =>
            {
                using FakePermissionPromptMcpServer server = new FakePermissionPromptMcpServer();
                server.Decide = async call =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(6)).ConfigureAwait(false);
                    return FakePermissionPromptMcpServer.Allow();
                };
                List<PiGateOutcome> outcomes = await PiPermissionGateHarness.RunAsync(new List<PiGateEvent> { PiGateEvent.Bash("make deploy") }, server.Url, Token, 1).ConfigureAwait(false);

                AssertEqual(1, server.Calls.Count, "the prompt reached Armada (capture saw the request)");
                AssertFalse(outcomes[0].Allowed, "a late allow does not count");
                AssertContains("no decision arrived within 1 seconds", outcomes[0].Result!.Reason ?? "");
                AssertTrue(outcomes[0].FinishedMs < 5000, "the gate gave up at its own timeout, not the server's reply: " + outcomes[0].FinishedMs + " ms");
            }));

            cases.Add(Case("two_concurrent_calls_decided_in_order", "Two tool calls from one response are both decided, one at a time and in order", TestTags.Reliability, skip, async () =>
            {
                using FakePermissionPromptMcpServer server = new FakePermissionPromptMcpServer();
                server.Decide = async call =>
                {
                    if (call.Command == "first")
                    {
                        await Task.Delay(700).ConfigureAwait(false);
                        return FakePermissionPromptMcpServer.Allow();
                    }

                    return FakePermissionPromptMcpServer.Deny("second denied");
                };
                List<PiGateEvent> events = new List<PiGateEvent> { PiGateEvent.Bash("first", "call_a"), PiGateEvent.Bash("second", "call_b") };
                List<PiGateOutcome> outcomes = await PiPermissionGateHarness.RunAsync(events, server.Url, Token, 30).ConfigureAwait(false);

                List<FakePermissionPromptCall> calls = server.Calls;
                AssertEqual(2, calls.Count, "both calls were asked");
                AssertEqual("first", calls[0].Command, "in arrival order");
                AssertEqual("second", calls[1].Command);
                AssertTrue(calls[1].ArrivedUtc >= calls[0].AnsweredUtc, "the second prompt waits for the first decision");
                AssertTrue(outcomes[0].Allowed, "first allowed");
                AssertFalse(outcomes[1].Allowed, "second denied");
                AssertEqual("second denied", outcomes[1].Result!.Reason);
                AssertTrue(outcomes[1].FinishedMs >= outcomes[0].FinishedMs, "decided in order");
            }));

            cases.Add(Case("file_and_armada_tools_skip_the_prompt", "read, edit, write, grep, find, ls, tool_search, codemode and Armada MCP tools run without a prompt; powershell and unknown tools are gated", TestTags.Positive, skip, async () =>
            {
                using FakePermissionPromptMcpServer server = new FakePermissionPromptMcpServer();
                server.Decide = call => Task.FromResult(FakePermissionPromptMcpServer.Deny("gated " + call.ToolName));
                List<PiGateEvent> events = new List<PiGateEvent>();
                foreach (string tool in new[] { "read", "edit", "write", "grep", "find", "ls", "tool_search", "codemode", "mcp__armada__status" })
                    events.Add(new PiGateEvent { ToolName = tool, ToolCallId = "c_" + tool });
                events.Add(new PiGateEvent { ToolName = "powershell", ToolCallId = "c_ps", Input = new Dictionary<string, object> { ["command"] = "Remove-Item x" } });
                events.Add(new PiGateEvent { ToolName = "repo_extension_shell", ToolCallId = "c_ext" });
                List<PiGateOutcome> outcomes = await PiPermissionGateHarness.RunAsync(events, server.Url, Token, 30).ConfigureAwait(false);

                for (int i = 0; i < 9; i++) AssertTrue(outcomes[i].Allowed, events[i].ToolName + " needs no approval");
                AssertEqual("gated powershell", outcomes[9].Result?.Reason, "powershell is gated under its own name (Bash rules do not apply)");
                AssertEqual("gated repo_extension_shell", outcomes[10].Result?.Reason, "unknown tools are gated");
                AssertEqual(2, server.Calls.Count, "only the gated tools reached Armada");
            }));

            cases.Add(Case("missing_token_blocks_without_calling", "Without ARMADA_MCP_TOKEN or ARMADA_MCP_URL the gate blocks every gated call and calls nothing", TestTags.Negative, skip, async () =>
            {
                using FakePermissionPromptMcpServer server = new FakePermissionPromptMcpServer();
                List<PiGateOutcome> noToken = await PiPermissionGateHarness.RunAsync(new List<PiGateEvent> { PiGateEvent.Bash("ls") }, server.Url, null, 30).ConfigureAwait(false);
                List<PiGateOutcome> noUrl = await PiPermissionGateHarness.RunAsync(new List<PiGateEvent> { PiGateEvent.Bash("ls") }, null, Token, 30).ConfigureAwait(false);
                AssertFalse(noToken[0].Allowed, "no token blocks");
                AssertFalse(noUrl[0].Allowed, "no URL blocks");
                AssertContains("no Armada MCP URL or token", noToken[0].Result!.Reason ?? "");
                AssertEqual(0, server.Calls.Count, "nothing was sent without a token");
            }));

            cases.Add(Case("error_result_blocks", "An MCP error result (for example Forbidden) or a reply without a decision blocks the call", TestTags.Negative, skip, async () =>
            {
                using FakePermissionPromptMcpServer forbidden = new FakePermissionPromptMcpServer { ResultIsError = true };
                forbidden.Decide = call => Task.FromResult("{\"errorCode\":\"Forbidden\",\"message\":\"cli_permission_prompt can only be called by a captain session.\"}");
                List<PiGateOutcome> error = await PiPermissionGateHarness.RunAsync(new List<PiGateEvent> { PiGateEvent.Bash("ls") }, forbidden.Url, Token, 30).ConfigureAwait(false);
                AssertFalse(error[0].Allowed, "an error result blocks");
                AssertContains("did not return a decision", error[0].Result!.Reason ?? "");

                using FakePermissionPromptMcpServer garbage = new FakePermissionPromptMcpServer();
                garbage.Decide = call => Task.FromResult("not json");
                List<PiGateOutcome> unreadable = await PiPermissionGateHarness.RunAsync(new List<PiGateEvent> { PiGateEvent.Bash("ls") }, garbage.Url, Token, 30).ConfigureAwait(false);
                AssertFalse(unreadable[0].Allowed, "an unreadable reply blocks");
            }));

            return new TestSuiteDescriptor(SuiteId, "pi permission gate extension", cases);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, string? skipReason, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag },
                skipReason: skipReason);
        }

        #endregion
    }
}

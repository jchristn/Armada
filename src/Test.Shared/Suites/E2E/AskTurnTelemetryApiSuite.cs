namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// End-to-end coverage of Ask turn telemetry through the real server and REST API: a scripted captain
    /// (<see cref="StubCaptainRuntime"/> standing in for Claude Code) writes stream-json output with a thinking block, a
    /// tool call, text deltas, and a result usage block, and the persisted reply read back over REST carries the turn's
    /// metrics (time to first token and first text, streaming, total, tool calls and tool time, input, output, and cached
    /// tokens, cost, tokens per second). A turn whose output reports no usage carries timing and an estimate only.
    /// </summary>
    public sealed class AskTurnTelemetryApiSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "E2E.AskTurnTelemetry";
        private const int TurnTimeoutMs = 60000;
        private readonly StubCaptainBehavior _Behavior = new StubCaptainBehavior();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("reply_carries_turn_metrics", "A captain reply read over REST carries the turn's telemetry, including the runtime's usage and cost", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                StubCaptainRuntime.Install(fx.Server, _Behavior);
                _Behavior.OnTurn = turn => Task.FromResult("All quiet.");
                _Behavior.TurnOutput = (turn, reply) => new List<string>
                {
                    "{\"type\":\"system\",\"subtype\":\"init\",\"model\":\"claude-stub\"}",
                    "{\"type\":\"stream_event\",\"event\":{\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"thinking\"}}}",
                    "{\"type\":\"assistant\",\"message\":{\"model\":\"claude-stub\",\"content\":[{\"type\":\"tool_use\",\"id\":\"toolu_1\",\"name\":\"mcp__armada__status\",\"input\":{}}]}}",
                    "{\"type\":\"user\",\"message\":{\"content\":[{\"type\":\"tool_result\",\"tool_use_id\":\"toolu_1\",\"content\":\"ok\"}]}}",
                    "{\"type\":\"stream_event\",\"event\":{\"type\":\"content_block_delta\",\"index\":1,\"delta\":{\"type\":\"text_delta\",\"text\":\"All \"}}}",
                    "{\"type\":\"stream_event\",\"event\":{\"type\":\"content_block_delta\",\"index\":1,\"delta\":{\"type\":\"text_delta\",\"text\":\"quiet.\"}}}",
                    "{\"type\":\"result\",\"subtype\":\"success\",\"is_error\":false,\"duration_ms\":12,\"total_cost_usd\":0.0123,\"usage\":{\"input_tokens\":100,\"cache_read_input_tokens\":900,\"cache_creation_input_tokens\":50,\"output_tokens\":120},\"result\":\"" + reply + "\"}"
                };

                AskMessage reply = await RunTurnAsync(fx, "tel", "How is the fleet?").ConfigureAwait(false);
                AssertEqual("All quiet.", reply.ContentText);
                CaptainChatMetrics? m = reply.Metrics;
                AssertNotNull(m, "the reply carries metrics over REST");
                AssertEqual(1050, m!.PromptTokens, "input tokens (cache included)");
                AssertEqual(900, m.CachedTokens, "cached tokens");
                AssertEqual(120, m.CompletionTokens, "output tokens");
                AssertEqual(1170, m.TotalTokens, "total tokens");
                AssertEqual(false, m.TokensEstimated, "reported by the runtime");
                AssertTrue(m.CostUsd.HasValue && Math.Abs(m.CostUsd.Value - 0.0123) < 1e-9, "cost");
                AssertNotNull(m.TimeToFirstTokenMs, "time to first token");
                AssertNotNull(m.TimeToFirstTextMs, "time to first text");
                AssertTrue(m.TimeToFirstTokenMs <= m.TimeToFirstTextMs, "the thinking block came before the text");
                AssertNotNull(m.StreamingMs, "streaming time");
                AssertEqual((double)reply.DurationMs!.Value, m.TotalMs, "total time is the turn duration");
                AssertEqual(1, m.ToolCallCount, "one tool call");
                AssertEqual(1, reply.ToolCalls.Count, "the tool call is on the reply");
                AssertEqual((double)(reply.ToolCalls[0].ElapsedMs ?? 0), m.ToolTimeMs, "tool time is the call's time");
            }));

            cases.Add(CaseAsync("reply_without_usage_has_timing_and_estimate", "A reply whose runtime reports no usage carries timing and an estimated token count only", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                StubCaptainRuntime.Install(fx.Server, _Behavior);
                _Behavior.OnTurn = turn => Task.FromResult("Seven voyages landed today.");
                _Behavior.TurnOutput = (turn, reply) => new List<string>
                {
                    "{\"type\":\"stream_event\",\"event\":{\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"" + reply + "\"}}}"
                };

                AskMessage reply = await RunTurnAsync(fx, "tel2", "What landed?").ConfigureAwait(false);
                CaptainChatMetrics? m = reply.Metrics;
                AssertNotNull(m, "metrics present");
                AssertNull(m!.PromptTokens, "no input reported");
                AssertNull(m.CachedTokens, "no cache reported");
                AssertNull(m.CostUsd, "no cost reported");
                AssertEqual(true, m.TokensEstimated, "estimated");
                AssertEqual((int)Math.Round("Seven voyages landed today.".Length / 3.5), m.CompletionTokens, "estimate from the reply");
                AssertEqual(m.TimeToFirstTokenMs, m.TimeToFirstTextMs, "the text was the first output");
                AssertEqual(0, m.ToolCallCount, "no tool calls");
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "E2E Ask Turn Telemetry",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Create a user, a captain, and a thread over REST, send one message, wait for the turn's ask.turn completion
        /// on the user's socket, and return the reply read back through the messages endpoint.
        /// </summary>
        private static async Task<AskMessage> RunTurnAsync(E2EServerFixture fx, string label, string question)
        {
            E2ETenantUser user = await E2ETenantUser.CreateAsync(fx.AuthClient, label).ConfigureAwait(false);
            using HttpClient client = user.CreateClient(fx.BaseUrl);
            using WebSocketTestClient socket = await WebSocketTestClient.ConnectAsync(fx.RestPort, user.BearerToken).ConfigureAwait(false);
            await socket.SendAsync(new Dictionary<string, object> { ["Route"] = "subscribe" }).ConfigureAwait(false);
            AssertNotNull(await socket.WaitForAsync(f => E2eWebSocketFrame.IsType(f, "status.snapshot")).ConfigureAwait(false), "subscribed");

            HttpResponseMessage captainResp = await client.PostAsync("/api/v1/captains", JsonHelper.ToJsonContent(new { Name = "telemetry-" + label, Runtime = "ClaudeCode" })).ConfigureAwait(false);
            captainResp.EnsureSuccessStatusCode();
            Captain captain = await JsonHelper.DeserializeAsync<Captain>(captainResp).ConfigureAwait(false);
            AskThread thread = await JsonHelper.DeserializeAsync<AskThread>(await client.PostAsync("/api/v1/ask/threads", JsonHelper.ToJsonContent(new { CaptainId = captain.Id })).ConfigureAwait(false)).ConfigureAwait(false);

            HttpResponseMessage sent = await client.PostAsync("/api/v1/ask/threads/" + thread.Id + "/messages", JsonHelper.ToJsonContent(new { Content = question })).ConfigureAwait(false);
            AssertStatusCode(HttpStatusCode.Accepted, sent);

            string? done = await socket.WaitForAsync(f =>
            {
                AskTurnEventFrame? frame = AskTurnEventFrame.Parse(f);
                return frame?.Data != null && frame.Data.ThreadId == thread.Id && frame.Data.State != "started";
            }, TurnTimeoutMs).ConfigureAwait(false);
            AskTurnEventFrame? end = AskTurnEventFrame.Parse(done);
            AssertNotNull(end, "the turn ended");
            AssertEqual("completed", end!.Data!.State, "the turn completed: " + end.Data.Error);

            AskMessagePage page = await JsonHelper.DeserializeAsync<AskMessagePage>(await client.PostAsync("/api/v1/ask/threads/" + thread.Id + "/messages/enumerate", JsonHelper.ToJsonContent(new { })).ConfigureAwait(false)).ConfigureAwait(false);
            AskMessage? reply = page.Messages.FirstOrDefault(x => x.Id == end.Data.MessageId);
            AssertNotNull(reply, "the reply is listed");
            return reply!;
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: async (CancellationToken ct) => { await body().ConfigureAwait(false); },
                tags: new List<string> { tag });
        }

        #endregion
    }
}

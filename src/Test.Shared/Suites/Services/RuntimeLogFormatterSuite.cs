namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="RuntimeLogFormatter"/>. Positive cases confirm tool-name resolution,
    /// secret redaction, and noise dropping; negative cases confirm plain lines pass through unchanged, a
    /// malformed JSON line does not throw, and ordinary text is not over-redacted.
    /// </summary>
    public sealed class RuntimeLogFormatterSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the runtime-log-formatter suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("tool_call_resolves_name", "A tool_call_proposed event resolves the tool name", TestTags.Positive, () =>
            {
                FormattedLogLine line = RuntimeLogFormatter.Format("{\"eventType\":\"tool_call_proposed\",\"toolCall\":{\"name\":\"enumerate\"}}", AgentRuntimeEnum.Mux);
                AssertTrue(line.IsToolCall, "expected tool call");
                AssertEqual("enumerate", line.ToolName);
                AssertTrue(line.Text.Contains("enumerate"), "expected tool name in text");
            }));

            cases.Add(Case("claude_tool_use_block_resolves_name", "A Claude assistant tool_use content block resolves the tool name", TestTags.Positive, () =>
            {
                FormattedLogLine line = RuntimeLogFormatter.Format(
                    "{\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":\"tool_use\",\"name\":\"Bash\"}]}}", AgentRuntimeEnum.ClaudeCode);
                AssertTrue(line.IsToolCall, "expected a tool call from the Claude tool_use block");
                AssertEqual("Bash", line.ToolName);
            }));

            cases.Add(Case("claude_bare_tool_use_resolves_name", "A bare Claude tool_use event resolves the tool name", TestTags.Positive, () =>
            {
                FormattedLogLine line = RuntimeLogFormatter.Format("{\"type\":\"tool_use\",\"name\":\"Read\"}", AgentRuntimeEnum.ClaudeCode);
                AssertTrue(line.IsToolCall, "expected a tool call");
                AssertEqual("Read", line.ToolName);
            }));

            cases.Add(Case("tool_completed_shows_status", "A tool_call_completed event shows ok/failed", TestTags.Positive, () =>
            {
                FormattedLogLine ok = RuntimeLogFormatter.Format("{\"eventType\":\"tool_call_completed\",\"toolName\":\"dispatch\",\"result\":{\"success\":true}}", AgentRuntimeEnum.Mux);
                AssertTrue(ok.Text.Contains("ok"), "expected ok");
                FormattedLogLine bad = RuntimeLogFormatter.Format("{\"eventType\":\"tool_call_completed\",\"toolName\":\"dispatch\",\"result\":{\"success\":false}}", AgentRuntimeEnum.Mux);
                AssertTrue(bad.Text.Contains("failed"), "expected failed");
            }));

            cases.Add(Case("redacts_secrets", "Secret-shaped values are redacted", TestTags.Positive, () =>
            {
                FormattedLogLine bearer = RuntimeLogFormatter.Format("Authorization: Bearer abcdef1234567890TOKEN", AgentRuntimeEnum.ClaudeCode);
                AssertTrue(bearer.Redacted, "expected redaction");
                AssertFalse(bearer.Text.Contains("abcdef1234567890TOKEN"), "token should be gone");

                FormattedLogLine apiKey = RuntimeLogFormatter.Format("api_key=sk-verysecretkey1234567890", AgentRuntimeEnum.Codex);
                AssertTrue(apiKey.Redacted, "expected api key redaction");
                AssertTrue(apiKey.Text.Contains("[REDACTED]"), "expected redaction marker");
            }));

            cases.Add(Case("truncates_oversized", "An oversized line is truncated with a marker", TestTags.Positive, () =>
            {
                FormattedLogLine big = RuntimeLogFormatter.Format(new string('x', 5000), AgentRuntimeEnum.ClaudeCode);
                AssertTrue(big.Truncated, "expected truncation");
                AssertTrue(big.Text.Contains("truncated"), "expected truncation marker");
            }));

            cases.Add(Case("plain_line_passes_through", "A plain line passes through unchanged", TestTags.Negative, () =>
            {
                FormattedLogLine line = RuntimeLogFormatter.Format("Running tests in src/Api...", AgentRuntimeEnum.ClaudeCode);
                AssertFalse(line.Dropped, "should not drop");
                AssertFalse(line.Redacted, "should not redact ordinary text");
                AssertFalse(line.IsToolCall, "should not be a tool call");
                AssertEqual("Running tests in src/Api...", line.Text);
            }));

            cases.Add(Case("malformed_json_does_not_throw", "A malformed JSON line is shown raw, not thrown", TestTags.Negative, () =>
            {
                FormattedLogLine line = RuntimeLogFormatter.Format("{ this is not valid json", AgentRuntimeEnum.Mux);
                AssertFalse(line.Dropped, "should not drop");
                AssertFalse(line.IsToolCall, "not a tool call");
                AssertTrue(line.Text.Contains("not valid json"), "raw text retained");
            }));

            cases.Add(Case("blank_and_noise_dropped", "Blank and known-noise lines are dropped", TestTags.Negative, () =>
            {
                AssertTrue(RuntimeLogFormatter.Format("   ", AgentRuntimeEnum.ClaudeCode).Dropped, "blank should drop");
                AssertTrue(RuntimeLogFormatter.Format(null, AgentRuntimeEnum.ClaudeCode).Dropped, "null should drop");
                AssertTrue(RuntimeLogFormatter.Format("Determining projects to restore...", AgentRuntimeEnum.ClaudeCode).Dropped, "noise should drop");
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.RuntimeLogFormatter",
                displayName: "Runtime Log Formatter",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.RuntimeLogFormatter",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) =>
                {
                    body();
                    return Task.CompletedTask;
                },
                tags: new List<string> { tag });
        }

        #endregion
    }
}

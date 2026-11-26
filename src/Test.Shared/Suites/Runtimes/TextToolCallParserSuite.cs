namespace Test.Shared.Suites.Runtimes
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Armada.Runtimes;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Tool calls written as text are recovered in the Hermes, function-tag and fenced JSON formats, only for permitted
    /// tools, and prose that merely shows JSON is left alone (RS-7.2).
    /// </summary>
    public sealed class TextToolCallParserSuite : IArmadaTestSuite
    {
        private const string SuiteId = "Runtimes.TextToolCallParser";
        private static readonly HashSet<string> _Permitted = new HashSet<string>(StringComparer.Ordinal) { "read_file", "write_file", "grep" };

        /// <summary>Builds the parser cases.</summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            cases.Add(Case("hermes_block", "A tool_call block with a JSON body is recovered and removed from the text", TestTags.Positive, () =>
            {
                TextToolCallParseResult r = TextToolCallParser.Parse("Let me look.\n<tool_call>\n{\"name\": \"read_file\", \"arguments\": {\"path\": \"a.txt\"}}\n</tool_call>", _Permitted);
                AssertEqual(1, r.Calls.Count);
                AssertEqual("read_file", r.Calls[0].Name);
                AssertEqual("{\"path\":\"a.txt\"}", r.Calls[0].ArgumentsJson);
                AssertEqual(TextToolCallParser.HermesFormat, r.Format);
                AssertEqual("Let me look.", r.RemainingText);
                AssertTrue(r.Calls[0].Id.StartsWith("text_call_", StringComparison.Ordinal), "recovered calls get their own ids");
            }));
            cases.Add(Case("hermes_string_arguments", "Arguments given as a JSON string are unwrapped", TestTags.Positive, () =>
            {
                TextToolCallParseResult r = TextToolCallParser.Parse("<tool_call>{\"name\":\"grep\",\"arguments\":\"{\\\"pattern\\\":\\\"TODO\\\"}\"}</tool_call>", _Permitted);
                AssertEqual("{\"pattern\":\"TODO\"}", r.Calls.Single().ArgumentsJson);
            }));
            cases.Add(Case("hermes_several_blocks", "Several tool_call blocks become several calls in order", TestTags.Positive, () =>
            {
                TextToolCallParseResult r = TextToolCallParser.Parse("<tool_call>{\"name\":\"read_file\",\"arguments\":{\"path\":\"a\"}}</tool_call>\n<tool_call>{\"name\":\"grep\",\"arguments\":{\"pattern\":\"b\"}}</tool_call>", _Permitted);
                AssertEqual("read_file,grep", String.Join(",", r.Calls.Select(c => c.Name)));
                AssertEqual(String.Empty, r.RemainingText);
            }));
            cases.Add(Case("function_tag_json", "A function tag with a JSON body is recovered", TestTags.Positive, () =>
            {
                TextToolCallParseResult r = TextToolCallParser.Parse("<function=read_file>{\"path\":\"a.txt\"}</function>", _Permitted);
                AssertEqual("read_file", r.Calls.Single().Name);
                AssertEqual("{\"path\":\"a.txt\"}", r.Calls.Single().ArgumentsJson);
                AssertEqual(TextToolCallParser.FunctionTagFormat, r.Format);
            }));
            cases.Add(Case("function_tag_parameters", "Qwen-style parameter tags inside a tool_call block keep file content and type integers", TestTags.Positive, () =>
            {
                string text = "<tool_call>\n<function=write_file>\n<parameter=path>\nsrc/a.cs\n</parameter>\n<parameter=content>\nline one\n    line two\n</parameter>\n<parameter=offset>\n12\n</parameter>\n</function>\n</tool_call>";
                TextToolCallParseResult r = TextToolCallParser.Parse(text, _Permitted);
                AssertEqual("write_file", r.Calls.Single().Name);
                AssertEqual("{\"path\":\"src/a.cs\",\"content\":\"line one\\n    line two\",\"offset\":12}", r.Calls.Single().ArgumentsJson);
                AssertEqual(TextToolCallParser.HermesFormat, r.Format);
            }));
            cases.Add(Case("fenced_json", "A fenced JSON call object is recovered", TestTags.Positive, () =>
            {
                TextToolCallParseResult r = TextToolCallParser.Parse("I will read it.\n```json\n{\"name\": \"read_file\", \"arguments\": {\"path\": \"a.txt\"}}\n```", _Permitted);
                AssertEqual("read_file", r.Calls.Single().Name);
                AssertEqual(TextToolCallParser.FencedJsonFormat, r.Format);
                AssertEqual("I will read it.", r.RemainingText);
            }));
            cases.Add(Case("fenced_json_array_and_wrapper", "A fenced array of calls and the OpenAI function wrapper are recovered", TestTags.Positive, () =>
            {
                TextToolCallParseResult r = TextToolCallParser.Parse("```\n[{\"type\":\"function\",\"function\":{\"name\":\"grep\",\"arguments\":{\"pattern\":\"x\"}}},{\"name\":\"read_file\",\"parameters\":{\"path\":\"y\"}}]\n```", _Permitted);
                AssertEqual("grep,read_file", String.Join(",", r.Calls.Select(c => c.Name)));
                AssertEqual("{\"path\":\"y\"}", r.Calls[1].ArgumentsJson);
            }));
            cases.Add(Case("fenced_json_without_arguments_is_prose", "A fenced JSON object naming a tool without arguments is left as text", TestTags.Negative, () =>
            {
                TextToolCallParseResult r = TextToolCallParser.Parse("The config is:\n```json\n{\"name\": \"read_file\"}\n```", _Permitted);
                AssertEqual(0, r.Calls.Count);
                AssertTrue(r.RemainingText.Contains("```json", StringComparison.Ordinal), "the text is unchanged");
            }));
            cases.Add(Case("fenced_json_ordinary_data", "Fenced JSON that is not a call (no permitted name) is left alone", TestTags.Negative, () =>
            {
                TextToolCallParseResult r = TextToolCallParser.Parse("```json\n{\"name\": \"armada\", \"version\": \"1.5.0\"}\n```", _Permitted);
                AssertEqual(0, r.Calls.Count);
                AssertEqual(null, r.Format);
            }));
            cases.Add(Case("unpermitted_tool_ignored", "Calls to tools outside the permitted set are not recovered", TestTags.Negative, () =>
            {
                TextToolCallParseResult r = TextToolCallParser.Parse("<tool_call>{\"name\":\"rm_rf\",\"arguments\":{}}</tool_call><function=delete_repo>{}</function>", _Permitted);
                AssertEqual(0, r.Calls.Count);
            }));
            cases.Add(Case("malformed_json_ignored", "A tool_call block with broken JSON is not recovered", TestTags.Negative, () =>
            {
                TextToolCallParseResult r = TextToolCallParser.Parse("<tool_call>{\"name\":\"read_file\",\"arguments\":{\"path\": </tool_call>", _Permitted);
                AssertEqual(0, r.Calls.Count);
            }));
            cases.Add(Case("plain_text_and_null", "Plain text and null text recover nothing", TestTags.Negative, () =>
            {
                AssertEqual(0, TextToolCallParser.Parse("All done.\n[ARMADA:RESULT] COMPLETE", _Permitted).Calls.Count);
                AssertEqual(0, TextToolCallParser.Parse(null, _Permitted).Calls.Count);
                AssertThrows<ArgumentNullException>(() => TextToolCallParser.Parse("x", null!));
            }));
            cases.Add(Case("call_cap", "At most MaxCalls calls are recovered from one response", TestTags.Negative, () =>
            {
                string text = String.Concat(Enumerable.Range(0, TextToolCallParser.MaxCalls + 4).Select(i => "<tool_call>{\"name\":\"grep\",\"arguments\":{\"pattern\":\"p" + i + "\"}}</tool_call>"));
                AssertEqual(TextToolCallParser.MaxCalls, TextToolCallParser.Parse(text, _Permitted).Calls.Count);
            }));
            return new TestSuiteDescriptor(SuiteId, "Text tool-call parser", cases);
        }

        private static TestCaseDescriptor Case(string id, string name, string tag, Action body)
        {
            return new TestCaseDescriptor(SuiteId, id, name, ct =>
            {
                body();
                return Task.CompletedTask;
            }, tags: new List<string> { tag });
        }
    }
}

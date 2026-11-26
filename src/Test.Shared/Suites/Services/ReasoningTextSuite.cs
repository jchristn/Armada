namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Services;
    using Armada.Runtimes;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="ReasoningText"/> and its use when a CLI runtime writes the final message (RS-7.12):
    /// complete think blocks, a bare closing tag and an unclosed leading block are removed; quoted tags and ordinary text
    /// are kept.
    /// </summary>
    public sealed class ReasoningTextSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the reasoning text suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("complete_block_removed", "A complete think block is removed and the reply trimmed", TestTags.Positive, () =>
            {
                AssertEqual("Done.\n[ARMADA:RESULT] COMPLETE", ReasoningText.Strip("<think>\nplan the edit\n</think>\n\nDone.\n[ARMADA:RESULT] COMPLETE"));
                AssertEqual("Done.", ReasoningText.Strip("<think type=\"x\">a</think>Done."));
            }));

            cases.Add(Case("bare_close_removes_prefix", "Text before a bare closing tag (Qwen3 template) is reasoning", TestTags.Positive, () =>
            {
                AssertEqual("[ARMADA:VERDICT] PASS", ReasoningText.Strip("I should check the tests first.\nThey pass.</think>\n\n[ARMADA:VERDICT] PASS"));
                AssertEqual("Answer", ReasoningText.Strip("reasoning\n</think>\nAnswer"));
            }));

            cases.Add(Case("unclosed_leading_block_is_all_reasoning", "An unclosed think block at the start leaves no reply", TestTags.Positive, () =>
            {
                AssertEqual(String.Empty, ReasoningText.Strip("  <think>\nstill thinking when the output was cut"));
            }));

            cases.Add(Case("quoted_tags_and_plain_text_kept", "Plain text, quoted tags and a longer tag name are unchanged", TestTags.Negative, () =>
            {
                string plain = "Changed the parser.\n[ARMADA:RESULT] COMPLETE\n";
                AssertEqual(plain, ReasoningText.Strip(plain));
                string quoted = "The parser now handles `</think>` at the end of a line.\nDone.";
                AssertEqual(quoted, ReasoningText.Strip(quoted));
                string inline = "A bare </think> tag mid-line is kept, so is the rest.";
                AssertEqual(inline, ReasoningText.Strip(inline));
                string longer = "<thinking>not a think tag</thinking>\nDone.";
                AssertEqual(longer, ReasoningText.Strip(longer));
                AssertEqual(String.Empty, ReasoningText.Strip(null));
                AssertFalse(ReasoningText.HasReasoning(plain), "plain text carries no reasoning");
                AssertTrue(ReasoningText.HasReasoning("<think>x</think>y"), "a think block is reasoning");
            }));

            cases.Add(Case("final_message_writer_strips_reasoning", "The shared final-message writer drops reasoning and writes nothing for reasoning only", TestTags.Positive, () =>
            {
                string root = Path.Combine(Path.GetTempPath(), "armada-reasoning-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(root);
                try
                {
                    string path = Path.Combine(root, "final.txt");
                    AssertTrue(MuxRuntime.WriteFinalMessageIfMissing(path, "<think>x</think>\nDone."), "the reply is written");
                    AssertEqual("Done.", File.ReadAllText(path));

                    string onlyReasoning = Path.Combine(root, "only.txt");
                    AssertFalse(MuxRuntime.WriteFinalMessageIfMissing(onlyReasoning, "<think>cut off"), "reasoning alone is not a reply");
                    AssertFalse(File.Exists(onlyReasoning), "no file for reasoning alone");

                    string cliWritten = Path.Combine(root, "cli.txt");
                    File.WriteAllText(cliWritten, "thinking...\n</think>\n[ARMADA:RESULT] COMPLETE");
                    AssertTrue(MuxRuntime.StripReasoningFromFinalMessage(cliWritten), "a CLI-written file is cleaned");
                    AssertEqual("[ARMADA:RESULT] COMPLETE", File.ReadAllText(cliWritten));
                    AssertFalse(MuxRuntime.StripReasoningFromFinalMessage(cliWritten), "a clean file is left alone");
                }
                finally
                {
                    try { Directory.Delete(root, true); } catch { }
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.ReasoningText",
                displayName: "Reasoning Text",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.ReasoningText",
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

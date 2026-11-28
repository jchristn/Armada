namespace Test.Shared.Suites.Runtimes
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Armada.Runtimes.Tools;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// The read_file and run_process hard maximums scale to the context window, explicit reads page at the read
    /// maximum, and the default page stays 200 lines and 24,000 characters (RS-7.9).
    /// </summary>
    public sealed class ToolLimitsSuite : IArmadaTestSuite
    {
        /// <summary>Builds the scaling and paging cases.</summary>
        /// <returns>The discovered runtime suite.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            cases.Add(Case("unknown_window_keeps_fixed_caps", "An unknown window keeps the 1 MB read and 100 KB process caps", dir =>
            {
                ToolLimits limits = ToolLimits.ForContextWindow(null, null);
                AssertEqual(1_048_576, limits.MaxReadCharacters);
                AssertEqual(102_400, limits.MaxProcessOutputBytes);
                AssertEqual(120_000, limits.DefaultProcessTimeoutMs);
                return Task.CompletedTask;
            }));
            cases.Add(Case("known_window_scales", "A known window scales both maximums to 12 percent at 3.2 characters per token, within floors and a ceiling", dir =>
            {
                ToolLimits large = ToolLimits.ForContextWindow(131_072, null);
                AssertEqual(50_331, large.MaxReadCharacters);
                AssertEqual(50_331, large.MaxProcessOutputBytes);
                ToolLimits small = ToolLimits.ForContextWindow(8_192, null);
                AssertEqual(24_000, small.MaxReadCharacters);
                AssertEqual(8_192, small.MaxProcessOutputBytes);
                ToolLimits huge = ToolLimits.ForContextWindow(2_097_152, 50);
                AssertEqual(1_048_576, huge.MaxReadCharacters);
                ToolLimits custom = ToolLimits.ForContextWindow(131_072, 25);
                AssertEqual(104_857, custom.MaxProcessOutputBytes);
                AssertThrows<ArgumentOutOfRangeException>(() => ToolLimits.ForContextWindow(131_072, 0));
                AssertThrows<ArgumentOutOfRangeException>(() => ToolLimits.ForContextWindow(131_072, 51));
                return Task.CompletedTask;
            }));
            cases.Add(Case("inference_option_validated", "toolOutputWindowPercent is validated and counts as a non-default setting", dir =>
            {
                new InferenceOptions { ToolOutputWindowPercent = 20 }.Validate();
                AssertTrue(new InferenceOptions { ToolOutputWindowPercent = 20 }.HasNonDefaultSettings);
                AssertThrows<ArgumentException>(() => new InferenceOptions { ToolOutputWindowPercent = 0 }.Validate());
                AssertThrows<ArgumentException>(() => new InferenceOptions { ToolOutputWindowPercent = 51 }.Validate());
                return Task.CompletedTask;
            }));
            cases.Add(Case("explicit_read_pages_at_maximum", "An explicit full read pages at the read maximum with an offset that continues without gaps", async dir =>
            {
                string[] lines = Enumerable.Range(1, 3000).Select(n => "line-" + n.ToString("D4") + new string('.', 40)).ToArray();
                File.WriteAllLines(Path.Combine(dir, "big.txt"), lines);
                ReadFileTool tool = new ReadFileTool(ToolLimits.ForContextWindow(32_768, null));
                List<int> seen = new List<int>();
                int offset = 1;
                for (int page = 0; page < 20 && offset > 0; page++)
                {
                    ToolResult result = await tool.ExecuteAsync("r", JsonSerializer.SerializeToElement(new { file_path = "big.txt", offset, limit = 0 }), dir, CancellationToken.None);
                    AssertTrue(result.Success, result.Content);
                    AssertTrue(result.Content.Length <= 24_000, "Page exceeded the read maximum: " + result.Content.Length);
                    seen.AddRange(Regex.Matches(result.Content, @"(?m)^\s*(\d+)\t").Select(m => Int32.Parse(m.Groups[1].Value)));
                    Match next = Regex.Match(result.Content, @"Continue with offset=(\d+)");
                    offset = next.Success ? Int32.Parse(next.Groups[1].Value) : 0;
                    if (next.Success) AssertContains("read limit for this context window", result.Content);
                }

                AssertEqual(3000, seen.Count);
                AssertTrue(seen.SequenceEqual(Enumerable.Range(1, 3000)), "Pages must cover every line once, in order.");
            }));
            cases.Add(Case("default_page_unchanged", "The default page stays 200 lines on a large window", async dir =>
            {
                File.WriteAllLines(Path.Combine(dir, "src.cs"), Enumerable.Range(1, 500).Select(n => "x" + n));
                ToolResult result = await new ReadFileTool(ToolLimits.ForContextWindow(1_000_000, null)).ExecuteAsync("r", JsonSerializer.SerializeToElement(new { file_path = "src.cs" }), dir, CancellationToken.None);
                AssertEqual(200, Regex.Matches(result.Content, @"(?m)^\s*\d+\t").Count);
                AssertContains("offset=201", result.Content);
            }));
            cases.Add(Case("long_line_cut_at_maximum", "A single line longer than the read maximum is cut with a note instead of refused", async dir =>
            {
                File.WriteAllText(Path.Combine(dir, "min.js"), new string('a', 60_000) + "\nnext\n");
                ToolResult result = await new ReadFileTool(ToolLimits.ForContextWindow(32_768, null)).ExecuteAsync("r", JsonSerializer.SerializeToElement(new { file_path = "min.js", limit = 1 }), dir, CancellationToken.None);
                AssertTrue(result.Success, result.Content);
                AssertTrue(result.Content.Length <= 24_000, "Cut line must fit the read maximum.");
                AssertContains("line 1 is longer than the 24000-character read limit", result.Content);
            }));
            cases.Add(Case("process_output_scales", "run_process keeps output within the scaled per-stream maximum", async dir =>
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
                RunProcessTool tool = new RunProcessTool(ToolLimits.ForContextWindow(8_192, null));
                ToolResult result = await tool.ExecuteAsync("p", JsonSerializer.SerializeToElement(new { command = "head -c 50000 /dev/zero | tr '\\0' 'x'" }), dir, CancellationToken.None);
                AssertContains("[truncated - output exceeded 8192 bytes", result.Content);
            }));
            return new TestSuiteDescriptor("Runtimes.ToolLimits", "Tool output limits scale to the context window", cases);
        }

        private static TestCaseDescriptor Case(string id, string name, Func<string, Task> action)
        {
            return new TestCaseDescriptor("Runtimes.ToolLimits", id, name, async ct =>
            {
                string directory = Path.Combine(Path.GetTempPath(), "armada-tool-limits-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(directory);
                try { await action(directory).ConfigureAwait(false); }
                finally { Directory.Delete(directory, true); }
            }, tags: new List<string> { TestTags.Positive });
        }
    }
}

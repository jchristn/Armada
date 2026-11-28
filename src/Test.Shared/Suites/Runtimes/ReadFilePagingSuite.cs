namespace Test.Shared.Suites.Runtimes
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Runtimes.Tools;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Prevents unrestricted default file reads from consuming an agent's context while preserving explicit
    /// ranges, small-file output, and complete repository instruction reads.
    /// </summary>
    public sealed class ReadFilePagingSuite : IArmadaTestSuite
    {
        /// <summary>Builds the paging and compatibility regression cases.</summary>
        /// <returns>The discovered runtime suite.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            cases.Add(Case("default_pages_cover_file_once", "Default reads page large source files with exact continuation and no lost or repeated lines", async dir =>
            {
                string[] lines = Enumerable.Range(1, 450).Select(n => "source-" + n).ToArray();
                File.WriteAllLines(Path.Combine(dir, "source.cs"), lines);
                List<int> seen = new List<int>();
                foreach (int start in new[] { 1, 201, 401 })
                {
                    ToolResult result = await Read(dir, "source.cs", start);
                    AssertTrue(result.Success, result.Content);
                    seen.AddRange(Numbers(result.Content));
                    AssertTrue(Numbers(result.Content).Count <= 200);
                    if (start == 401) AssertFalse(result.Content.Contains("output paged"));
                    else AssertContains("offset=" + (start + 200), result.Content);
                }
                AssertEqual(String.Join(",", Enumerable.Range(1, 450)), String.Join(",", seen));
            }));
            cases.Add(Case("character_bound_preserves_lines", "Character-limited pages preserve complete Unicode lines and resume at the first omitted line", async dir =>
            {
                string[] lines = Enumerable.Range(1, 9).Select(n => n + new string('\u00e9', 8_000) + "\U0001f600").ToArray();
                File.WriteAllLines(Path.Combine(dir, "wide.txt"), lines);
                int offset = 1;
                List<int> seen = new List<int>();
                for (int page = 0; page < 9 && offset <= lines.Length; page++)
                {
                    ToolResult result = await Read(dir, "wide.txt", offset);
                    AssertTrue(result.Success, result.Content);
                    AssertTrue(result.Content.Length <= 24_000, "The full tool output, including footer, is bounded.");
                    List<int> numbers = Numbers(result.Content);
                    AssertTrue(numbers.Count > 0);
                    foreach (int n in numbers) AssertContains(lines[n - 1], result.Content);
                    seen.AddRange(numbers);
                    offset = numbers.Last() + 1;
                    if (offset <= lines.Length) AssertContains("offset=" + offset, result.Content);
                }
                AssertEqual("1,2,3,4,5,6,7,8,9", String.Join(",", seen));
            }));
            cases.Add(Case("oversized_line_explicit_opt_in", "A huge single line is bounded by default and can be deliberately read with an explicit limit", async dir =>
            {
                string huge = new string('x', 50_000);
                File.WriteAllText(Path.Combine(dir, "minified.js"), huge + "\nlast\n");
                ToolResult page = await Read(dir, "minified.js");
                AssertTrue(page.Success);
                AssertTrue(page.Content.Length < 1_000);
                AssertContains("offset=1", page.Content);
                AssertContains("explicit limit=1", page.Content);
                AssertFalse(page.Content.Contains(huge));
                ToolResult explicitRead = await Read(dir, "minified.js", 1, 1);
                AssertTrue(explicitRead.Success);
                AssertEqual("     1\t" + huge + "\n", explicitRead.Content);
            }));
            cases.Add(Case("explicit_ranges_and_full_reads", "Explicit ranges and nonpositive full-read limits retain compatibility, including integer boundaries", async dir =>
            {
                File.WriteAllLines(Path.Combine(dir, "source.cs"), Enumerable.Range(1, 450).Select(n => "line-" + n));
                ToolResult range = await Read(dir, "source.cs", 100, 300);
                AssertEqual(300, Numbers(range.Content).Count);
                AssertEqual(100, Numbers(range.Content).First());
                AssertEqual(399, Numbers(range.Content).Last());
                ToolResult wideLimit = await Read(dir, "source.cs", 300, int.MaxValue);
                AssertEqual(151, Numbers(wideLimit.Content).Count);
                foreach (int limit in new[] { 0, -1 })
                {
                    ToolResult all = await Read(dir, "source.cs", 1, limit);
                    AssertEqual(450, Numbers(all.Content).Count);
                    AssertFalse(all.Content.Contains("output paged"));
                }
                ToolResult pastEnd = await Read(dir, "source.cs", int.MaxValue);
                AssertEqual(String.Empty, pastEnd.Content);
                ToolResult lowOffset = await Read(dir, "source.cs", int.MinValue, 1);
                AssertEqual("     1\tline-1\n", lowOffset.Content);
            }));
            cases.Add(Case("instruction_files_remain_complete", "CLAUDE.md and AGENTS.md remain complete by default; explicit instruction ranges remain available", async dir =>
            {
                foreach (string name in new[] { "CLAUDE.md", "AGENTS.md", "claude.md", "agents.md" })
                {
                    File.WriteAllLines(Path.Combine(dir, name), Enumerable.Range(1, 500).Select(n => "instruction-" + n));
                    ToolResult result = await Read(dir, name);
                    AssertEqual(500, Numbers(result.Content).Count);
                    AssertContains("instruction-500", result.Content);
                    AssertFalse(result.Content.Contains("output paged"));
                    ToolResult range = await Read(dir, name, 250, 1);
                    AssertEqual("   250\tinstruction-250\n", range.Content);
                }
            }));
            cases.Add(Case("small_empty_missing_oversize", "Small/empty content and missing/oversize file error contracts stay unchanged; the size refusal is a setting", async dir =>
            {
                File.WriteAllText(Path.Combine(dir, "small.txt"), "one\r\ntwo\n");
                AssertEqual("     1\tone\n     2\ttwo\n", (await Read(dir, "small.txt")).Content);
                File.WriteAllText(Path.Combine(dir, "empty.txt"), String.Empty);
                AssertEqual(String.Empty, (await Read(dir, "empty.txt")).Content);
                ToolResult missing = await Read(dir, "missing.txt");
                AssertFalse(missing.Success);
                AssertContains("file_not_found", missing.Content);
                File.WriteAllText(Path.Combine(dir, "oversize.txt"), new string('x', 4097));
                ToolResult oversize = await new ReadFileTool(new ToolLimits { MaxReadFileBytes = 4096 }).ExecuteAsync("read", JsonSerializer.SerializeToElement(new { file_path = "oversize.txt" }), dir, CancellationToken.None);
                AssertFalse(oversize.Success);
                AssertContains("file_too_large", oversize.Content);
            }));
            return new TestSuiteDescriptor("Runtimes.ReadFilePaging", "Bounded default file reads", cases);
        }

        private static Task<ToolResult> Read(string directory, string name, int? offset = null, int? limit = null)
        {
            return new ReadFileTool().ExecuteAsync("read", JsonSerializer.SerializeToElement(new { file_path = name, offset, limit }), directory, CancellationToken.None);
        }

        private static List<int> Numbers(string text)
        {
            return Regex.Matches(text, @"(?m)^\s*(\d+)\t").Select(m => Int32.Parse(m.Groups[1].Value)).ToList();
        }

        private static TestCaseDescriptor Case(string id, string name, Func<string, Task> action)
        {
            return new TestCaseDescriptor("Runtimes.ReadFilePaging", id, name, async ct =>
            {
                string directory = Path.Combine(Path.GetTempPath(), "armada-read-page-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(directory);
                try { await action(directory).ConfigureAwait(false); }
                finally { Directory.Delete(directory, true); }
            }, tags: new List<string> { TestTags.Positive });
        }
    }
}

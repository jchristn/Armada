namespace Test.Shared.Suites.Runtimes
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Runtimes.Tools;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Byte-level checks that edit_file, multi_edit, and write_file keep a file's byte order mark, encoding, and
    /// line endings (RS-7.14).
    /// </summary>
    public sealed class ToolFileFormatSuite : IArmadaTestSuite
    {
        private static readonly byte[] _Utf8Bom = new byte[] { 0xEF, 0xBB, 0xBF };

        /// <summary>Builds the byte-comparison cases.</summary>
        /// <returns>The discovered runtime suite.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            foreach (string ending in new[] { "\r\n", "\n" })
            {
                string label = ending == "\r\n" ? "crlf" : "lf";
                foreach (bool bom in new[] { true, false })
                {
                    string variant = label + (bom ? "_bom" : "_nobom");
                    cases.Add(Case("edit_file_keeps_" + variant, "edit_file keeps " + variant, async dir =>
                    {
                        string path = Seed(dir, "a.cs", "alpha\nbeta\ngamma\n", ending, bom);
                        ToolResult result = await Run(new EditFileTool(), dir, new { file_path = path, old_string = "beta", new_string = "BETA\ndelta" });
                        AssertTrue(result.Success, result.Content);
                        AssertBytes(Expected("alpha\nBETA\ndelta\ngamma\n", ending, bom), path);
                    }));
                    cases.Add(Case("multi_edit_keeps_" + variant, "multi_edit keeps " + variant, async dir =>
                    {
                        string path = Seed(dir, "b.cs", "one\ntwo\nthree\n", ending, bom);
                        ToolResult result = await Run(new MultiEditTool(), dir, new
                        {
                            file_path = path,
                            edits = new[] { new { old_string = "one", new_string = "ONE" }, new { old_string = "three", new_string = "THREE\nfour" } }
                        });
                        AssertTrue(result.Success, result.Content);
                        AssertBytes(Expected("ONE\ntwo\nTHREE\nfour\n", ending, bom), path);
                    }));
                    cases.Add(Case("write_file_keeps_" + variant, "write_file over an existing file keeps " + variant, async dir =>
                    {
                        string path = Seed(dir, "c.cs", "old\ncontent\n", ending, bom);
                        ToolResult result = await Run(new WriteFileTool(), dir, new { file_path = path, content = "new\r\ncontent\nhere\n" });
                        AssertTrue(result.Success, result.Content);
                        AssertBytes(Expected("new\ncontent\nhere\n", ending, bom), path);
                    }));
                }
            }

            cases.Add(Case("non_ascii_round_trips", "Non-ASCII text in a BOM file round-trips byte for byte outside the edited span", async dir =>
            {
                string path = Seed(dir, "d.txt", "café 日本\nkeep ü\n", "\r\n", true);
                ToolResult result = await Run(new EditFileTool(), dir, new { file_path = path, old_string = "keep", new_string = "kept" });
                AssertTrue(result.Success, result.Content);
                AssertBytes(Expected("café 日本\nkept ü\n", "\r\n", true), path);
            }));
            cases.Add(Case("utf16_bom_round_trips", "A UTF-16 file with a byte order mark stays UTF-16", async dir =>
            {
                string path = Path.Combine(dir, "e.txt");
                Encoding utf16 = new UnicodeEncoding(false, true);
                File.WriteAllBytes(path, utf16.GetPreamble().Concat(utf16.GetBytes("x = 1\r\ny = 2\r\n")).ToArray());
                ToolResult result = await Run(new EditFileTool(), dir, new { file_path = path, old_string = "y = 2", new_string = "y = 3" });
                AssertTrue(result.Success, result.Content);
                AssertBytes(utf16.GetPreamble().Concat(utf16.GetBytes("x = 1\r\ny = 3\r\n")).ToArray(), path);
            }));
            cases.Add(Case("new_file_has_no_bom", "write_file creates a new file as UTF-8 without a byte order mark", async dir =>
            {
                string path = Path.Combine(dir, "sub", "new.txt");
                ToolResult result = await Run(new WriteFileTool(), dir, new { file_path = path, content = "hello" });
                AssertTrue(result.Success, result.Content);
                AssertBytes(Encoding.UTF8.GetBytes("hello"), path);
            }));
            return new TestSuiteDescriptor("Runtimes.ToolFileFormat", "Edit and write tools keep encoding and line endings", cases);
        }

        private static string Seed(string dir, string name, string lfText, string ending, bool bom)
        {
            string path = Path.Combine(dir, name);
            File.WriteAllBytes(path, Expected(lfText, ending, bom));
            return path;
        }

        private static byte[] Expected(string lfText, string ending, bool bom)
        {
            byte[] body = new UTF8Encoding(false).GetBytes(lfText.Replace("\n", ending));
            return bom ? _Utf8Bom.Concat(body).ToArray() : body;
        }

        private static void AssertBytes(byte[] expected, string path)
        {
            byte[] actual = File.ReadAllBytes(path);
            AssertTrue(expected.SequenceEqual(actual), "Bytes differ. Expected " + Convert.ToHexString(expected) + " but found " + Convert.ToHexString(actual));
        }

        private static Task<ToolResult> Run(IToolExecutor tool, string dir, object arguments)
        {
            return tool.ExecuteAsync("t", JsonSerializer.SerializeToElement(arguments), dir, CancellationToken.None);
        }

        private static TestCaseDescriptor Case(string id, string name, Func<string, Task> action)
        {
            return new TestCaseDescriptor("Runtimes.ToolFileFormat", id, name, async ct =>
            {
                string directory = Path.Combine(Path.GetTempPath(), "armada-tool-format-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(directory);
                try { await action(directory).ConfigureAwait(false); }
                finally { Directory.Delete(directory, true); }
            }, tags: new List<string> { TestTags.Positive });
        }
    }
}

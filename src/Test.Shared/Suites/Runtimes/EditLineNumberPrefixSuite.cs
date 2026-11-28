namespace Test.Shared.Suites.Runtimes
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Runtimes.Tools;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Edit text that carries read_file line-number prefixes is rejected with an error the model can act on, and
    /// tab-separated data with numeric first columns is not (RS-7.15).
    /// </summary>
    public sealed class EditLineNumberPrefixSuite : IArmadaTestSuite
    {
        private const string _Source = "using System;\n\npublic class A\n{\n    int x = 1;\n}\n";

        /// <summary>Builds the prefix-rejection cases.</summary>
        /// <returns>The discovered runtime suite.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            cases.Add(Case("old_string_padded_prefixes_rejected", "old_string copied from read_file output is rejected and the file is unchanged", async dir =>
            {
                string path = Seed(dir, "a.cs", _Source);
                ToolResult result = await Run(new EditFileTool(), dir, new { file_path = path, old_string = "     3\tpublic class A\n     4\t{", new_string = "public class B\n{" });
                AssertRejected(result, "old_string");
                AssertEqual(_Source, File.ReadAllText(path));
            }));
            cases.Add(Case("old_string_unpadded_prefixes_rejected", "Prefixes with the padding dropped are also recognized in old_string", async dir =>
            {
                string path = Seed(dir, "a.cs", _Source);
                ToolResult result = await Run(new EditFileTool(), dir, new { file_path = path, old_string = "5\t    int x = 1;", new_string = "    int x = 2;" });
                AssertRejected(result, "old_string");
            }));
            cases.Add(Case("new_string_prefixes_rejected", "new_string with consecutive read_file prefixes is rejected", async dir =>
            {
                string path = Seed(dir, "a.cs", _Source);
                ToolResult result = await Run(new EditFileTool(), dir, new { file_path = path, old_string = "    int x = 1;", new_string = "     5\t    int x = 2;\n     6\t    int y = 3;" });
                AssertRejected(result, "new_string");
                AssertEqual(_Source, File.ReadAllText(path));
            }));
            cases.Add(Case("plain_edit_still_applies", "A normal edit applies and a missing old_string keeps its own error", async dir =>
            {
                string path = Seed(dir, "a.cs", _Source);
                ToolResult ok = await Run(new EditFileTool(), dir, new { file_path = path, old_string = "int x = 1;", new_string = "int x = 2;" });
                AssertTrue(ok.Success, ok.Content);
                ToolResult missing = await Run(new EditFileTool(), dir, new { file_path = path, old_string = "int z = 9;", new_string = "int z = 0;" });
                AssertContains("old_string_not_found", missing.Content);
            }));
            cases.Add(Case("tsv_numeric_columns_allowed", "Tab-separated rows with numeric first columns are edited and written normally", async dir =>
            {
                string path = Seed(dir, "people.tsv", "1\tAlice\n2\tBob\n3\tCarol\n");
                ToolResult edit = await Run(new EditFileTool(), dir, new { file_path = path, old_string = "2\tBob\n3\tCarol", new_string = "2\tBobby\n3\tCarol\n4\tDan" });
                AssertTrue(edit.Success, edit.Content);
                AssertEqual("1\tAlice\n2\tBobby\n3\tCarol\n4\tDan\n", File.ReadAllText(path));
                ToolResult write = await Run(new WriteFileTool(), dir, new { file_path = Path.Combine(dir, "ids.tsv"), content = "1\tx\n2\ty\n3\tz\n" });
                AssertTrue(write.Success, write.Content);
            }));
            cases.Add(Case("padded_rows_in_file_allowed", "Replacement text shaped like prefixes is allowed when the file already has such lines", async dir =>
            {
                string path = Seed(dir, "report.txt", "     1\tfirst\n     2\tsecond\n");
                ToolResult result = await Run(new EditFileTool(), dir, new { file_path = path, old_string = "     2\tsecond", new_string = "     2\tsecond\n     3\tthird" });
                AssertTrue(result.Success, result.Content);
            }));
            cases.Add(Case("multi_edit_reports_index", "multi_edit names the edit whose text carries prefixes", async dir =>
            {
                string path = Seed(dir, "a.cs", _Source);
                ToolResult result = await Run(new MultiEditTool(), dir, new
                {
                    file_path = path,
                    edits = new[] { new { old_string = "using System;", new_string = "using System.IO;" }, new { old_string = "     5\t    int x = 1;", new_string = "    int x = 2;" } }
                });
                AssertRejected(result, "old_string");
                AssertContains("\"edit_index\":1", result.Content);
                AssertEqual(_Source, File.ReadAllText(path));
            }));
            cases.Add(Case("write_file_prefixes_rejected", "write_file content copied from read_file output is rejected", async dir =>
            {
                string path = Path.Combine(dir, "b.cs");
                ToolResult result = await Run(new WriteFileTool(), dir, new { file_path = path, content = "     1\tusing System;\n     2\t\n     3\tpublic class B { }\n" });
                AssertRejected(result, "content");
                AssertFalse(File.Exists(path));
            }));
            return new TestSuiteDescriptor("Runtimes.EditLineNumberPrefix", "Edits with read_file line numbers are rejected", cases);
        }

        private static void AssertRejected(ToolResult result, string argumentName)
        {
            AssertFalse(result.Success, result.Content);
            AssertContains(LineNumberPrefix.ErrorCode, result.Content);
            AssertContains(argumentName + " starts each line with a read_file line number", result.Content);
        }

        private static string Seed(string dir, string name, string text)
        {
            string path = Path.Combine(dir, name);
            File.WriteAllText(path, text);
            return path;
        }

        private static Task<ToolResult> Run(IToolExecutor tool, string dir, object arguments)
        {
            return tool.ExecuteAsync("t", JsonSerializer.SerializeToElement(arguments), dir, CancellationToken.None);
        }

        private static TestCaseDescriptor Case(string id, string name, Func<string, Task> action)
        {
            return new TestCaseDescriptor("Runtimes.EditLineNumberPrefix", id, name, async ct =>
            {
                string directory = Path.Combine(Path.GetTempPath(), "armada-edit-prefix-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(directory);
                try { await action(directory).ConfigureAwait(false); }
                finally { Directory.Delete(directory, true); }
            }, tags: new List<string> { TestTags.Positive });
        }
    }
}

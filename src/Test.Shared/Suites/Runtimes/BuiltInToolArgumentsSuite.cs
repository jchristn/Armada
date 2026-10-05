namespace Test.Shared.Suites.Runtimes
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Runtimes.Tools;
    using Armada.Runtimes.Tools.Arguments;
    using Armada.Runtimes.Tools.Tasks;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for the typed argument parsing of the built-in coding tools: wrong-typed and missing
    /// parameters are rejected with the invalid_parameter code, valid calls keep working, and run_process
    /// passes an argument vector to the executable verbatim without a shell.
    /// </summary>
    public sealed class BuiltInToolArgumentsSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string _SuiteId = "Runtimes.BuiltInToolArguments";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the built-in tool arguments suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("wrong_typed_arguments_rejected", "Wrong-typed arguments return invalid_parameter instead of being ignored or defaulted", TestTags.Negative, async () =>
            {
                string dir = NewTempDir();
                try
                {
                    File.WriteAllText(Path.Combine(dir, "existing.txt"), "alpha\nbeta\n");
                    BuiltInToolRegistry registry = new BuiltInToolRegistry(new TaskPlan());

                    List<ToolCallCase> calls = new List<ToolCallCase>
                    {
                        new ToolCallCase("read_file", "{\"file_path\":123}"),
                        new ToolCallCase("read_file", "{\"file_path\":\"existing.txt\",\"offset\":\"2\"}"),
                        new ToolCallCase("read_file", "{\"file_path\":\"existing.txt\",\"limit\":1.5}"),
                        new ToolCallCase("run_process", "{\"command\":\"echo\",\"timeout_ms\":\"abc\"}"),
                        new ToolCallCase("run_process", "{\"command\":\"echo\",\"args\":\"notanarray\"}"),
                        new ToolCallCase("run_process", "{\"command\":\"echo\",\"args\":[1,2]}"),
                        new ToolCallCase("run_process", "{\"command\":\"echo\",\"args\":[\"ok\",null]}"),
                        new ToolCallCase("multi_edit", "{\"file_path\":\"existing.txt\",\"edits\":{\"old_string\":\"alpha\",\"new_string\":\"gamma\"}}"),
                        new ToolCallCase("multi_edit", "{\"file_path\":\"existing.txt\",\"edits\":[{\"old_string\":1,\"new_string\":\"gamma\"}]}"),
                        new ToolCallCase("multi_edit", "{\"file_path\":\"existing.txt\",\"edits\":[null]}"),
                        new ToolCallCase("write_file", "{\"file_path\":\"typed.txt\",\"content\":42}"),
                        new ToolCallCase("write_file", "[\"typed.txt\",\"content\"]"),
                        new ToolCallCase("edit_file", "{\"file_path\":\"existing.txt\",\"old_string\":\"alpha\",\"new_string\":false}"),
                        new ToolCallCase("glob", "{\"pattern\":[\"*.txt\"]}"),
                        new ToolCallCase("grep", "{\"pattern\":\"alpha\",\"include\":5}"),
                        new ToolCallCase("list_directory", "{\"path\":{}}"),
                        new ToolCallCase("manage_directory", "{\"action\":\"create\",\"path\":true}"),
                        new ToolCallCase("plan_tasks", "{\"tasks\":[{\"id\":\"t1\",\"title\":\"one\",\"dependsOn\":\"t0\"}]}"),
                        new ToolCallCase("plan_tasks", "{\"tasks\":\"t1\"}"),
                        new ToolCallCase("update_task", "{\"id\":\"t1\",\"status\":3}")
                    };

                    foreach (ToolCallCase call in calls)
                    {
                        ToolResult result = await registry.ExecuteAsync("c1", call.ToolName, ParseArgs(call.ArgumentsJson), dir, CancellationToken.None).ConfigureAwait(false);
                        AssertInvalidParameter(result, call.ToolName + " " + call.ArgumentsJson);
                    }

                    AssertFalse(File.Exists(Path.Combine(dir, "typed.txt")), "write_file with a wrong-typed content must not create the file");
                    AssertEqual("alpha\nbeta\n", File.ReadAllText(Path.Combine(dir, "existing.txt")), "existing.txt must be untouched by rejected edits");
                }
                finally
                {
                    Cleanup(dir);
                }
            }));

            cases.Add(CaseAsync("missing_required_arguments_rejected", "Missing or null required parameters return invalid_parameter", TestTags.Negative, async () =>
            {
                string dir = NewTempDir();
                try
                {
                    BuiltInToolRegistry registry = new BuiltInToolRegistry(new TaskPlan());

                    List<ToolCallCase> calls = new List<ToolCallCase>
                    {
                        new ToolCallCase("read_file", "{}"),
                        new ToolCallCase("read_file", "{\"file_path\":null}"),
                        new ToolCallCase("write_file", "{\"file_path\":\"a.txt\"}"),
                        new ToolCallCase("edit_file", "{\"file_path\":\"a.txt\",\"old_string\":\"x\"}"),
                        new ToolCallCase("multi_edit", "{\"file_path\":\"a.txt\"}"),
                        new ToolCallCase("multi_edit", "{\"file_path\":\"a.txt\",\"edits\":[{\"old_string\":\"x\"}]}"),
                        new ToolCallCase("delete_file", "{}"),
                        new ToolCallCase("list_directory", "{}"),
                        new ToolCallCase("file_metadata", "{}"),
                        new ToolCallCase("glob", "{\"path\":\".\"}"),
                        new ToolCallCase("grep", "{}"),
                        new ToolCallCase("manage_directory", "{\"path\":\"sub\"}"),
                        new ToolCallCase("run_process", "{\"args\":[\"x\"]}"),
                        new ToolCallCase("plan_tasks", "{}"),
                        new ToolCallCase("update_task", "{\"id\":\"t1\"}"),
                        new ToolCallCase("update_task", "{\"id\":\"  \",\"status\":\"completed\"}")
                    };

                    foreach (ToolCallCase call in calls)
                    {
                        ToolResult result = await registry.ExecuteAsync("c2", call.ToolName, ParseArgs(call.ArgumentsJson), dir, CancellationToken.None).ConfigureAwait(false);
                        AssertInvalidParameter(result, call.ToolName + " " + call.ArgumentsJson);
                    }

                    AssertFalse(File.Exists(Path.Combine(dir, "a.txt")), "write_file without content must not create the file");
                    AssertFalse(Directory.Exists(Path.Combine(dir, "sub")), "manage_directory without an action must not create the directory");
                }
                finally
                {
                    Cleanup(dir);
                }
            }));

            cases.Add(CaseAsync("valid_calls_still_work", "Valid write, read, edit, multi_edit, glob, list, plan_tasks, and update_task calls keep their behavior", TestTags.Positive, async () =>
            {
                string dir = NewTempDir();
                try
                {
                    TaskPlan plan = new TaskPlan();
                    BuiltInToolRegistry registry = new BuiltInToolRegistry(plan);

                    ToolResult write = await Run(registry, "write_file", "{\"file_path\":\"src/notes.txt\",\"content\":\"one\\ntwo\\nthree\\n\"}", dir).ConfigureAwait(false);
                    AssertTrue(write.Success, "write_file: " + write.Content);
                    AssertEqual("one\ntwo\nthree\n", File.ReadAllText(Path.Combine(dir, "src", "notes.txt")), "written content");

                    ToolResult readAll = await Run(registry, "read_file", "{\"file_path\":\"src/notes.txt\"}", dir).ConfigureAwait(false);
                    AssertTrue(readAll.Success, "read_file: " + readAll.Content);
                    AssertEqual("     1\tone\n     2\ttwo\n     3\tthree\n", readAll.Content, "read_file full content");

                    ToolResult readRange = await Run(registry, "read_file", "{\"file_path\":\"src/notes.txt\",\"offset\":2,\"limit\":1}", dir).ConfigureAwait(false);
                    AssertTrue(readRange.Success, "read_file range: " + readRange.Content);
                    AssertEqual("     2\ttwo\n", readRange.Content, "read_file offset/limit content");

                    ToolResult edit = await Run(registry, "edit_file", "{\"file_path\":\"src/notes.txt\",\"old_string\":\"two\",\"new_string\":\"TWO\"}", dir).ConfigureAwait(false);
                    AssertTrue(edit.Success, "edit_file: " + edit.Content);
                    AssertEqual("one\nTWO\nthree\n", File.ReadAllText(Path.Combine(dir, "src", "notes.txt")), "edited content");

                    ToolResult multi = await Run(registry, "multi_edit", "{\"file_path\":\"src/notes.txt\",\"edits\":[{\"old_string\":\"one\",\"new_string\":\"1\"},{\"old_string\":\"three\",\"new_string\":\"3\"}]}", dir).ConfigureAwait(false);
                    AssertTrue(multi.Success, "multi_edit: " + multi.Content);
                    MultiEditOutput? multiOutput = JsonSerializer.Deserialize<MultiEditOutput>(multi.Content);
                    AssertNotNull(multiOutput, "multi_edit output");
                    AssertEqual(2, multiOutput!.EditsApplied, "multi_edit edits_applied");
                    AssertEqual("1\nTWO\n3\n", File.ReadAllText(Path.Combine(dir, "src", "notes.txt")), "multi-edited content");

                    File.WriteAllText(Path.Combine(dir, "src", "other.cs"), "class X {}");
                    ToolResult glob = await Run(registry, "glob", "{\"pattern\":\"**/*.txt\"}", dir).ConfigureAwait(false);
                    AssertTrue(glob.Success, "glob: " + glob.Content);
                    AssertEqual("Found 1 matching file(s):" + Environment.NewLine + "src/notes.txt" + Environment.NewLine, glob.Content, "glob content");

                    ToolResult list = await Run(registry, "list_directory", "{\"path\":\"src\"}", dir).ConfigureAwait(false);
                    AssertTrue(list.Success, "list_directory: " + list.Content);
                    AssertEqual("[FILE] notes.txt" + Environment.NewLine + "[FILE] other.cs" + Environment.NewLine, list.Content, "list_directory content");

                    ToolResult planned = await Run(registry, "plan_tasks", "{\"tasks\":[{\"id\":\"t1\",\"title\":\"First\"},{\"id\":\"t2\",\"title\":\"Second\",\"dependsOn\":[\"t1\"]}]}", dir).ConfigureAwait(false);
                    AssertTrue(planned.Success, "plan_tasks: " + planned.Content);
                    IReadOnlyList<AgentTask> snapshot = plan.Snapshot();
                    AssertEqual(2, snapshot.Count, "planned task count");
                    AssertEqual("t1", snapshot[1].DependsOn[0], "t2 dependency");

                    ToolResult updated = await Run(registry, "update_task", "{\"id\":\"t1\",\"status\":\"in_progress\",\"note\":\"started\"}", dir).ConfigureAwait(false);
                    AssertTrue(updated.Success, "update_task: " + updated.Content);
                    AssertEqual(AgentTaskStatusEnum.InProgress, plan.Snapshot()[0].Status, "t1 status");

                    ToolResult badStatus = await Run(registry, "update_task", "{\"id\":\"t1\",\"status\":\"bogus\"}", dir).ConfigureAwait(false);
                    AssertEqual("invalid_status", ReadError(badStatus).Error, "unknown status keeps its own code");
                }
                finally
                {
                    Cleanup(dir);
                }
            }));

            cases.Add(CaseAsync("run_process_args_are_not_shell_interpreted", "run_process passes args verbatim to the executable without a shell", TestTags.Negative, async () =>
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

                string dir = NewTempDir();
                try
                {
                    BuiltInToolRegistry registry = new BuiltInToolRegistry(null);
                    RunProcessToolArguments request = new RunProcessToolArguments
                    {
                        Command = "printf",
                        Args = new List<string?> { "%s\\n", "a b", "$HOME", "x;touch INJECTED", "q\"uote", "`touch BACKTICK`" }
                    };

                    ToolResult result = await registry.ExecuteAsync("p1", "run_process", JsonSerializer.SerializeToElement(request), dir, CancellationToken.None).ConfigureAwait(false);
                    AssertTrue(result.Success, "run_process: " + result.Content);

                    RunProcessOutput? output = JsonSerializer.Deserialize<RunProcessOutput>(result.Content);
                    AssertNotNull(output, "run_process output");
                    AssertEqual(0, output!.ExitCode, "exit code");
                    AssertFalse(output.TimedOut, "timed out");
                    AssertEqual("a b\n$HOME\nx;touch INJECTED\nq\"uote\n`touch BACKTICK`\n", output.Stdout, "literal argv output");
                    AssertFalse(File.Exists(Path.Combine(dir, "INJECTED")), "the ; metacharacter must not run a second command");
                    AssertFalse(File.Exists(Path.Combine(dir, "BACKTICK")), "backticks must not run a command");
                }
                finally
                {
                    Cleanup(dir);
                }
            }));

            cases.Add(CaseAsync("run_process_without_args_uses_shell", "run_process without args still runs the command line through the shell, quotes intact", TestTags.Positive, async () =>
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

                string dir = NewTempDir();
                try
                {
                    BuiltInToolRegistry registry = new BuiltInToolRegistry(null);

                    ToolResult shell = await Run(registry, "run_process", "{\"command\":\"printf '%s|' \\\"double quoted\\\" 'single' && echo done\",\"args\":[]}", dir).ConfigureAwait(false);
                    AssertTrue(shell.Success, "run_process shell: " + shell.Content);
                    RunProcessOutput? output = JsonSerializer.Deserialize<RunProcessOutput>(shell.Content);
                    AssertNotNull(output, "run_process output");
                    AssertEqual("double quoted|single|done\n", output!.Stdout, "shell output");

                    ToolResult failing = await Run(registry, "run_process", "{\"command\":\"exit 3\"}", dir).ConfigureAwait(false);
                    AssertFalse(failing.Success, "non-zero exit is a failure");
                    RunProcessOutput? failingOutput = JsonSerializer.Deserialize<RunProcessOutput>(failing.Content);
                    AssertNotNull(failingOutput, "failing output");
                    AssertEqual(3, failingOutput!.ExitCode, "exit code");
                }
                finally
                {
                    Cleanup(dir);
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: _SuiteId,
                displayName: "Built-in Tool Arguments",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task<ToolResult> Run(BuiltInToolRegistry registry, string toolName, string argumentsJson, string dir)
        {
            return await registry.ExecuteAsync("t-" + toolName, toolName, ParseArgs(argumentsJson), dir, CancellationToken.None).ConfigureAwait(false);
        }

        private static void AssertInvalidParameter(ToolResult result, string label)
        {
            AssertFalse(result.Success, label + ": expected failure");
            ToolErrorContent error = ReadError(result);
            AssertEqual(ToolArgumentParser.InvalidParameterCode, error.Error, label + ": error code");
            AssertFalse(String.IsNullOrWhiteSpace(error.Message), label + ": error message");
        }

        private static ToolErrorContent ReadError(ToolResult result)
        {
            ToolErrorContent? error = JsonSerializer.Deserialize<ToolErrorContent>(result.Content);
            AssertNotNull(error, "error content");
            return error!;
        }

        private static JsonElement ParseArgs(string json)
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }

        private static string NewTempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "armada-tool-args-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void Cleanup(string dir)
        {
            try { Directory.Delete(dir, true); } catch { }
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: _SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion

        #region Private-Classes

        private sealed class ToolCallCase
        {
            public ToolCallCase(string toolName, string argumentsJson)
            {
                ToolName = toolName;
                ArgumentsJson = argumentsJson;
            }

            public string ToolName { get; }

            public string ArgumentsJson { get; }
        }

        private sealed class RunProcessOutput
        {
            [JsonPropertyName("stdout")]
            public string Stdout { get; set; } = String.Empty;

            [JsonPropertyName("stderr")]
            public string Stderr { get; set; } = String.Empty;

            [JsonPropertyName("exit_code")]
            public int ExitCode { get; set; } = 0;

            [JsonPropertyName("timed_out")]
            public bool TimedOut { get; set; } = false;
        }

        private sealed class MultiEditOutput
        {
            [JsonPropertyName("success")]
            public bool Success { get; set; } = false;

            [JsonPropertyName("edits_applied")]
            public int EditsApplied { get; set; } = 0;
        }

        #endregion
    }
}

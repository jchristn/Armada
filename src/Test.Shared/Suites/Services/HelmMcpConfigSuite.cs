namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Helm.Commands;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// File-based Helm MCP install and remove: a JSONC file (comments or trailing commas) is backed up next to itself
    /// before the rewrite and the result says so, a file whose root is not an object is refused untouched, and plain
    /// JSON is rewritten as before with no backup.
    /// </summary>
    public sealed class HelmMcpConfigSuite : IArmadaTestSuite
    {
        private const string SuiteId = "Services.HelmMcpConfig";
        private const string BackupMarker = ".armada-backup-";
        private const string NotPreserved = "comments or trailing commas, which were not preserved";

        /// <summary>
        /// Build the Helm MCP configuration descriptors.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("jsonc_comments_backed_up_on_install_and_remove", "Installing into and removing from a config with comments backs the original up and reports where", TestTags.Positive, async () =>
            {
                string root = TestTemp.NewDirectory("helm-mcp-comments");
                try
                {
                    string path = Path.Combine(root, ".claude.json");
                    string original = "{\n  // the user's note\n  \"theme\": \"dark\",\n  /* other servers */\n  \"mcpServers\": { \"docs\": { \"url\": \"http://docs.example\" } }\n}\n";
                    File.WriteAllText(path, original);
                    McpConfigHelper.ConfigTarget target = new McpConfigHelper.ConfigTarget("Claude Code", path, ArmadaEntry());

                    McpConfigHelper.ApplyResult installed = await McpConfigHelper.InstallTargetAsync(target).ConfigureAwait(false);
                    AssertTrue(installed.Changed, "the entry was added");
                    string backup = SingleBackup(root, ".claude.json");
                    AssertEqual(original, File.ReadAllText(backup), "the backup holds the original text");
                    AssertContains(NotPreserved, installed.Message, "the result says comments were not preserved");
                    AssertContains(backup, installed.Message, "the result names the backup");
                    JsonObject written = ParseStrict(path);
                    AssertEqual("dark", written["theme"]!.GetValue<string>(), "unrelated settings stay");
                    AssertEqual("http://docs.example", written["mcpServers"]!["docs"]!["url"]!.GetValue<string>(), "unrelated servers stay");
                    AssertEqual("http://localhost:7891/mcp", written["mcpServers"]!["armada"]!["url"]!.GetValue<string>(), "the Armada entry is written");

                    string withComment = "// re-added comment\n" + File.ReadAllText(path);
                    File.WriteAllText(path, withComment);
                    File.Delete(backup);
                    McpConfigHelper.ApplyResult removed = await McpConfigHelper.RemoveTargetAsync(target).ConfigureAwait(false);
                    AssertTrue(removed.Changed, "the entry was removed");
                    string removeBackup = SingleBackup(root, ".claude.json");
                    AssertEqual(withComment, File.ReadAllText(removeBackup), "the remove backup holds the text before removal");
                    AssertContains(removeBackup, removed.Message, "the remove result names the backup");
                    JsonObject afterRemove = ParseStrict(path);
                    AssertFalse(afterRemove["mcpServers"]!.AsObject().ContainsKey("armada"), "the Armada entry is gone");
                    AssertEqual("http://docs.example", afterRemove["mcpServers"]!["docs"]!["url"]!.GetValue<string>(), "unrelated servers stay after removal");
                }
                finally
                {
                    TestTemp.TryDelete(root);
                }
            }));

            cases.Add(CaseAsync("jsonc_trailing_commas_backed_up_for_opencode_and_mux", "A config with only trailing commas is backed up for OpenCode and Mux installs", TestTags.Positive, async () =>
            {
                string root = TestTemp.NewDirectory("helm-mcp-commas");
                try
                {
                    string openCodePath = Path.Combine(root, "opencode.jsonc");
                    string openCodeText = "{ \"model\": \"x\", \"mcp\": { \"docs\": { \"type\": \"remote\" }, }, }";
                    File.WriteAllText(openCodePath, openCodeText);
                    McpConfigHelper.ConfigTarget openCode = new McpConfigHelper.ConfigTarget("OpenCode", openCodePath, ArmadaEntry(), IsOpenCodeConfig: true);
                    McpConfigHelper.ApplyResult openCodeResult = await McpConfigHelper.InstallTargetAsync(openCode).ConfigureAwait(false);
                    string openCodeBackup = SingleBackup(root, "opencode.jsonc");
                    AssertEqual(openCodeText, File.ReadAllText(openCodeBackup), "the OpenCode backup holds the original");
                    AssertContains(openCodeBackup, openCodeResult.Message, "the OpenCode result names the backup");
                    JsonObject openCodeWritten = ParseStrict(openCodePath);
                    AssertEqual("x", openCodeWritten["model"]!.GetValue<string>(), "OpenCode settings stay");
                    AssertTrue(openCodeWritten["mcp"]!.AsObject().ContainsKey("armada"), "the OpenCode entry is written");

                    string muxPath = Path.Combine(root, "mcp-servers.json");
                    string muxText = "{ \"servers\": [ { \"name\": \"docs\", \"url\": \"http://docs.example\" }, ], }";
                    File.WriteAllText(muxPath, muxText);
                    JsonObject muxEntry = ArmadaEntry();
                    muxEntry["name"] = "armada";
                    McpConfigHelper.ConfigTarget mux = new McpConfigHelper.ConfigTarget("Mux", muxPath, muxEntry, IsMuxServers: true);
                    McpConfigHelper.ApplyResult muxResult = await McpConfigHelper.InstallTargetAsync(mux).ConfigureAwait(false);
                    string muxBackup = SingleBackup(root, "mcp-servers.json");
                    AssertEqual(muxText, File.ReadAllText(muxBackup), "the Mux backup holds the original");
                    AssertContains(muxBackup, muxResult.Message, "the Mux result names the backup");
                    AssertEqual(2, ParseStrict(muxPath)["servers"]!.AsArray().Count, "the Mux servers keep docs and add armada");
                }
                finally
                {
                    TestTemp.TryDelete(root);
                }
            }));

            cases.Add(CaseAsync("jsonc_matching_entry_is_not_rewritten", "A JSONC config whose Armada entry already matches is left byte-for-byte unchanged", TestTags.Positive, async () =>
            {
                string root = TestTemp.NewDirectory("helm-mcp-match");
                try
                {
                    string path = Path.Combine(root, ".claude.json");
                    string text = "{\n  // keep me\n  \"mcpServers\": { \"armada\": " + ArmadaEntry().ToJsonString() + " }\n}";
                    File.WriteAllText(path, text);
                    McpConfigHelper.ApplyResult result = await McpConfigHelper.InstallTargetAsync(new McpConfigHelper.ConfigTarget("Claude Code", path, ArmadaEntry())).ConfigureAwait(false);
                    AssertFalse(result.Changed, "nothing changed");
                    AssertEqual(text, File.ReadAllText(path), "the file keeps its comments");
                    AssertEqual(0, Backups(root, ".claude.json").Count, "no backup is needed");
                    AssertFalse(result.Message.Contains(NotPreserved, StringComparison.Ordinal), "no backup note");
                }
                finally
                {
                    TestTemp.TryDelete(root);
                }
            }));

            cases.Add(CaseAsync("non_object_root_is_refused", "A config whose root is not a JSON object is refused and left unchanged", TestTags.Negative, async () =>
            {
                string root = TestTemp.NewDirectory("helm-mcp-root");
                try
                {
                    string path = Path.Combine(root, ".claude.json");
                    foreach (string text in new[] { "[ { \"url\": \"x\" } ]", "\"just a string\"", "null", "// note\n[1, 2,]" })
                    {
                        File.WriteAllText(path, text);
                        McpConfigHelper.ConfigTarget target = new McpConfigHelper.ConfigTarget("Claude Code", path, ArmadaEntry());
                        InvalidDataException? installError = await CaptureAsync(() => McpConfigHelper.InstallTargetAsync(target)).ConfigureAwait(false);
                        AssertNotNull(installError, "install refuses root " + text);
                        AssertContains("not a JSON object", installError!.Message, "the refusal explains why");
                        AssertEqual(text, File.ReadAllText(path), "install leaves root " + text + " unchanged");

                        InvalidDataException? removeError = await CaptureAsync(() => McpConfigHelper.RemoveTargetAsync(target)).ConfigureAwait(false);
                        AssertNotNull(removeError, "remove refuses root " + text);
                        AssertEqual(text, File.ReadAllText(path), "remove leaves root " + text + " unchanged");

                        McpConfigHelper.ConfigTarget mux = new McpConfigHelper.ConfigTarget("Mux", path, ArmadaEntry(), IsMuxServers: true);
                        AssertNotNull(await CaptureAsync(() => McpConfigHelper.InstallTargetAsync(mux)).ConfigureAwait(false), "Mux install refuses root " + text);
                        McpConfigHelper.ConfigTarget openCode = new McpConfigHelper.ConfigTarget("OpenCode", path, ArmadaEntry(), IsOpenCodeConfig: true);
                        AssertNotNull(await CaptureAsync(() => McpConfigHelper.InstallTargetAsync(openCode)).ConfigureAwait(false), "OpenCode install refuses root " + text);
                        AssertEqual(text, File.ReadAllText(path), "root " + text + " is never replaced");
                        AssertEqual(0, Backups(root, ".claude.json").Count, "a refused file gets no backup");
                    }
                }
                finally
                {
                    TestTemp.TryDelete(root);
                }
            }));

            cases.Add(CaseAsync("plain_json_rewritten_without_backup", "Plain JSON, a missing file and an empty file are written as before, with no backup or note", TestTags.Positive, async () =>
            {
                string root = TestTemp.NewDirectory("helm-mcp-plain");
                try
                {
                    string path = Path.Combine(root, ".claude.json");
                    File.WriteAllText(path, "{\"theme\":\"dark\",\"mcpServers\":{\"docs\":{\"url\":\"http://docs.example\"}}}");
                    McpConfigHelper.ConfigTarget target = new McpConfigHelper.ConfigTarget("Claude Code", path, ArmadaEntry());
                    McpConfigHelper.ApplyResult installed = await McpConfigHelper.InstallTargetAsync(target).ConfigureAwait(false);
                    AssertEqual("Configured Armada MCP entry.", installed.Message, "the plain message is unchanged");
                    JsonObject written = ParseStrict(path);
                    AssertEqual("dark", written["theme"]!.GetValue<string>(), "settings stay");
                    AssertTrue(written["mcpServers"]!.AsObject().ContainsKey("armada"), "the entry is written");

                    McpConfigHelper.ApplyResult again = await McpConfigHelper.InstallTargetAsync(target).ConfigureAwait(false);
                    AssertFalse(again.Changed, "a second install changes nothing");
                    AssertEqual("Armada MCP entry already matched the expected configuration.", again.Message);

                    McpConfigHelper.ApplyResult removed = await McpConfigHelper.RemoveTargetAsync(target).ConfigureAwait(false);
                    AssertEqual("Removed Armada MCP entry.", removed.Message, "the plain remove message is unchanged");
                    AssertEqual(0, Backups(root, ".claude.json").Count, "plain JSON is never backed up");

                    string missing = Path.Combine(root, "nested", "mcp.json");
                    McpConfigHelper.ApplyResult created = await McpConfigHelper.InstallTargetAsync(new McpConfigHelper.ConfigTarget("Cursor", missing, ArmadaEntry(), IsProjectScoped: true)).ConfigureAwait(false);
                    AssertEqual("Configured Armada MCP entry.", created.Message, "a missing file is created");
                    AssertTrue(ParseStrict(missing)["mcpServers"]!.AsObject().ContainsKey("armada"), "the created file holds the entry");

                    string empty = Path.Combine(root, "empty.json");
                    File.WriteAllText(empty, "  \n");
                    await McpConfigHelper.InstallTargetAsync(new McpConfigHelper.ConfigTarget("Claude Code", empty, ArmadaEntry())).ConfigureAwait(false);
                    AssertTrue(ParseStrict(empty)["mcpServers"]!.AsObject().ContainsKey("armada"), "an empty file is filled");
                    AssertEqual(0, Backups(root, "empty.json").Count, "an empty file is not backed up");
                }
                finally
                {
                    TestTemp.TryDelete(root);
                }
            }));

            cases.Add(Case("backup_path_avoids_existing_files", "The backup name carries a UTC timestamp and a numeric suffix when the name is taken", TestTags.Positive, () =>
            {
                string root = TestTemp.NewDirectory("helm-mcp-backup-name");
                try
                {
                    string path = Path.Combine(root, ".claude.json");
                    DateTime stamp = new DateTime(2026, 11, 19, 8, 5, 3, DateTimeKind.Utc);
                    string first = McpConfigHelper.GetBackupPath(path, stamp);
                    AssertEqual(path + ".armada-backup-20261119080503", first, "the name format");
                    File.WriteAllText(first, "taken");
                    AssertEqual(first + "-1", McpConfigHelper.GetBackupPath(path, stamp), "a taken name gets a suffix");
                }
                finally
                {
                    TestTemp.TryDelete(root);
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Helm MCP config files",
                cases: cases);
        }

        private static JsonObject ArmadaEntry()
        {
            return new JsonObject
            {
                ["type"] = "http",
                ["url"] = "http://localhost:7891/mcp",
            };
        }

        private static JsonObject ParseStrict(string path)
        {
            JsonNode? node = JsonNode.Parse(File.ReadAllText(path));
            AssertTrue(node is JsonObject, path + " is a strict JSON object");
            return node!.AsObject();
        }

        private static List<string> Backups(string directory, string fileName)
        {
            return Directory.GetFiles(directory, fileName + BackupMarker + "*").ToList();
        }

        private static string SingleBackup(string directory, string fileName)
        {
            List<string> backups = Backups(directory, fileName);
            AssertEqual(1, backups.Count, "exactly one backup of " + fileName);
            return backups[0];
        }

        private static async Task<InvalidDataException?> CaptureAsync(Func<Task<McpConfigHelper.ApplyResult>> action)
        {
            try
            {
                await action().ConfigureAwait(false);
                return null;
            }
            catch (InvalidDataException ex)
            {
                return ex;
            }
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) =>
                {
                    body();
                    return Task.CompletedTask;
                },
                tags: new List<string> { tag });
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }
    }
}

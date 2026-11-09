namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Helm.Commands;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// File-based Helm MCP install and remove keep every byte that is not the managed Armada entry.
    /// </summary>
    public sealed class HelmMcpConfigSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.HelmMcpConfig";

        private const string Comment = "// keep-this-comment";

        private const string BlockComment = "/* keep-block */";

        private const string BraceText = "\"braceText\": \"braces { } and \\\"armada\\\" stay\"";

        private const string OtherUrl = "\"url\": \"http://other.example\",";

        private const string NestedUrl = "http://nested.example";

        private const string OldObjectValue = "{ \"url\": \"http://old.example\" }";

        private const string OldMuxObject = "{ \"name\": \"armada\", \"url\": \"http://old.example\" }";

        private static readonly byte[] Utf8Bom = new byte[] { 0xEF, 0xBB, 0xBF };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the Helm MCP configuration descriptors.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync(
                "mcp_servers_jsonc_preserves_unrelated_text",
                "Claude and Cursor JSONC install and remove change only the Armada mcpServers entry",
                TestTags.Positive,
                async () =>
                {
                    string root = TestTemp.NewDirectory("helm-jsonc-servers");
                    try
                    {
                        JsonObject config = KeyedConfig();
                        string original = KeyedDocument("mcpServers");
                        await ExerciseKeyedAsync(
                            new McpConfigHelper.ConfigTarget("Claude Code", Path.Combine(root, ".claude.json"), config),
                            original,
                            OldObjectValue).ConfigureAwait(false);
                        await ExerciseKeyedAsync(
                            new McpConfigHelper.ConfigTarget(
                                "Cursor",
                                Path.Combine(root, "project", ".cursor", "mcp.json"),
                                config,
                                IsProjectScoped: true),
                            original,
                            OldObjectValue).ConfigureAwait(false);
                    }
                    finally
                    {
                        TestTemp.TryDelete(root);
                    }
                }));

            cases.Add(CaseAsync(
                "mux_servers_jsonc_preserves_unrelated_text",
                "Mux JSONC install and remove change only the Armada servers object",
                TestTags.Positive,
                async () =>
                {
                    string root = TestTemp.NewDirectory("helm-jsonc-mux");
                    try
                    {
                        await ExerciseMuxAsync(Path.Combine(root, "mcp-servers.json")).ConfigureAwait(false);
                    }
                    finally
                    {
                        TestTemp.TryDelete(root);
                    }
                }));

            cases.Add(CaseAsync(
                "opencode_mcp_jsonc_preserves_unrelated_text",
                "OpenCode JSONC install and remove change only the Armada mcp entry",
                TestTags.Positive,
                async () =>
                {
                    string root = TestTemp.NewDirectory("helm-jsonc-opencode");
                    try
                    {
                        string path = Path.Combine(root, "opencode.jsonc");
                        await ExerciseKeyedAsync(
                            new McpConfigHelper.ConfigTarget(
                                "OpenCode",
                                path,
                                new JsonObject
                                {
                                    ["type"] = "remote",
                                    ["url"] = "http://127.0.0.1:9/mcp",
                                    ["enabled"] = true,
                                },
                                IsOpenCodeConfig: true),
                            KeyedDocument("mcp"),
                            OldObjectValue).ConfigureAwait(false);
                    }
                    finally
                    {
                        TestTemp.TryDelete(root);
                    }
                }));

            cases.Add(CaseAsync(
                "malformed_jsonc_is_left_unchanged",
                "Malformed JSONC is refused and the file bytes stay as they were",
                TestTags.Negative,
                async () =>
                {
                    string root = TestTemp.NewDirectory("helm-jsonc-malformed");
                    try
                    {
                        McpConfigHelper.ConfigTarget target = KeyedTarget(Path.Combine(root, "mcp.json"));
                        await ExpectUnchangedAsync(target, "{\n").ConfigureAwait(false);
                        await ExpectUnchangedAsync(target, "{\n  \"mcpServers\": {\n    \"armada\": INVALID\n  }\n}\n").ConfigureAwait(false);
                    }
                    finally
                    {
                        TestTemp.TryDelete(root);
                    }
                }));

            cases.Add(CaseAsync(
                "wrong_shape_jsonc_is_left_unchanged",
                "A root or container of the wrong shape is refused and the file bytes stay as they were",
                TestTags.Negative,
                async () =>
                {
                    string root = TestTemp.NewDirectory("helm-jsonc-shape");
                    try
                    {
                        await ExpectUnchangedAsync(
                            KeyedTarget(Path.Combine(root, "array-container.json")),
                            "{\n  \"mcpServers\": [\n    { \"url\": \"http://other.example\" }\n  ]\n}\n").ConfigureAwait(false);
                        await ExpectUnchangedAsync(
                            MuxTarget(Path.Combine(root, "object-servers.json")),
                            "{\n  \"servers\": {\n    \"name\": \"other\"\n  }\n}\n").ConfigureAwait(false);
                        await ExpectUnchangedAsync(
                            OpenCodeTarget(Path.Combine(root, "string-mcp.json")),
                            "{\n  \"mcp\": \"not-an-object\"\n}\n").ConfigureAwait(false);
                        await ExpectUnchangedAsync(
                            KeyedTarget(Path.Combine(root, "root-array.json")),
                            "[\n  1,\n]\n").ConfigureAwait(false);
                    }
                    finally
                    {
                        TestTemp.TryDelete(root);
                    }
                }));

            cases.Add(CaseAsync(
                "duplicate_armada_entries_are_left_unchanged",
                "Duplicate Armada entries are refused and the file bytes stay as they were",
                TestTags.Negative,
                async () =>
                {
                    string root = TestTemp.NewDirectory("helm-jsonc-duplicate");
                    try
                    {
                        await ExpectUnchangedAsync(
                            MuxTarget(Path.Combine(root, "mux.json")),
                            "{\n  \"servers\": [\n    { \"name\": \"armada\", \"url\": \"http://one.example\" },\n    { \"name\": \"armada\", \"url\": \"http://two.example\" }\n  ]\n}\n").ConfigureAwait(false);
                        await ExpectUnchangedAsync(
                            KeyedTarget(Path.Combine(root, "servers.json")),
                            "{\n  \"mcpServers\": {\n    \"armada\": { \"url\": \"http://one.example\" },\n    \"armada\": { \"url\": \"http://two.example\" }\n  }\n}\n").ConfigureAwait(false);
                        await ExpectUnchangedAsync(
                            OpenCodeTarget(Path.Combine(root, "mcp.json")),
                            "{\n  \"mcp\": {\n    \"armada\": { \"url\": \"http://one.example\" },\n    \"other\": 1,\n    \"armada\": { \"url\": \"http://two.example\" }\n  }\n}\n").ConfigureAwait(false);
                        await ExpectUnchangedAsync(
                            KeyedTarget(Path.Combine(root, "two-containers.json")),
                            "{\n  \"mcpServers\": {},\n  \"mcpServers\": {}\n}\n").ConfigureAwait(false);
                    }
                    finally
                    {
                        TestTemp.TryDelete(root);
                    }
                }));

            cases.Add(CaseAsync(
                "missing_or_blank_file_gains_armada_entry",
                "A missing or blank MCP file still receives one Armada entry",
                TestTags.Positive,
                async () =>
                {
                    string root = TestTemp.NewDirectory("helm-jsonc-create");
                    try
                    {
                        await AssertCreatesEntryAsync(KeyedTarget(Path.Combine(root, ".claude.json")), false).ConfigureAwait(false);
                        await AssertCreatesEntryAsync(MuxTarget(Path.Combine(root, "mux", "mcp-servers.json")), true).ConfigureAwait(false);
                        await AssertCreatesEntryAsync(OpenCodeTarget(Path.Combine(root, "opencode", "opencode.json")), false).ConfigureAwait(false);
                        string blank = Path.Combine(root, "blank.json");
                        Directory.CreateDirectory(root);
                        await File.WriteAllTextAsync(blank, " \n\t").ConfigureAwait(false);
                        await AssertCreatesEntryAsync(KeyedTarget(blank), false).ConfigureAwait(false);
                    }
                    finally
                    {
                        TestTemp.TryDelete(root);
                    }
                }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Helm MCP Config",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task ExerciseKeyedAsync(McpConfigHelper.ConfigTarget target, string original, string oldValue)
        {
            string serialized = target.ArmadaConfig!.ToJsonString(McpConfigHelper.JsonOptions);
            await AssertReplacedSpanAsync(target, original, oldValue, serialized).ConfigureAwait(false);
            await AssertMarkersRemainAfterRemoveAsync(target, "mcpServers").ConfigureAwait(false);

            string insert = original.Replace(
                "    \"armada\": " + OldObjectValue + "\n",
                "",
                StringComparison.Ordinal);
            AssertEqual(-1, insert.IndexOf(oldValue, StringComparison.Ordinal), "add fixture has no managed value");
            string insertPath = target.FilePath + ".insert";
            McpConfigHelper.ConfigTarget insertTarget = target with { FilePath = insertPath };
            await WriteBomAsync(insertPath, insert).ConfigureAwait(false);
            McpConfigHelper.ApplyResult inserted = await McpConfigHelper.InstallTargetAsync(insertTarget).ConfigureAwait(false);
            AssertTrue(inserted.Changed, "install adds a missing Armada entry");
            AssertEqual(target.IsProjectScoped, inserted.IsProjectScoped, "project scope is unchanged");
            string insertedText = await ReadTextAsync(insertPath).ConfigureAwait(false);
            AssertPreservedMarkers(insertedText);
            JsonObject insertedRoot = ParseObject(insertedText);
            string container = target.IsOpenCodeConfig ? "mcp" : "mcpServers";
            AssertEqual("http://127.0.0.1:9/mcp", insertedRoot[container]!["armada"]!["url"]!.GetValue<string>(), "added entry");
            AssertEqual("http://other.example", insertedRoot[container]!["other"]!["url"]!.GetValue<string>(), "other entry");
            AssertEqual(NestedUrl, insertedRoot["nested"]![container]!["armada"]!["url"]!.GetValue<string>(), "nested entry");
        }

        private static async Task ExerciseMuxAsync(string path)
        {
            McpConfigHelper.ConfigTarget target = MuxTarget(path);
            string original = MuxDocument();
            string serialized = target.ArmadaConfig!.ToJsonString(McpConfigHelper.JsonOptions);
            await AssertReplacedSpanAsync(target, original, OldMuxObject, serialized).ConfigureAwait(false);

            McpConfigHelper.ApplyResult removed = await McpConfigHelper.RemoveTargetAsync(target).ConfigureAwait(false);
            AssertTrue(removed.Changed, "remove deletes the managed entry");
            string removedText = await ReadTextAsync(path).ConfigureAwait(false);
            AssertPreservedMarkers(removedText);
            AssertFalse(removedText.Contains("http://127.0.0.1:9", StringComparison.Ordinal), "managed Mux URL is gone");
            AssertFalse(removedText.Contains("http://old.example", StringComparison.Ordinal), "old Mux URL is gone");
            JsonObject root = ParseObject(removedText);
            bool foundOther = false;
            bool foundManaged = false;
            foreach (JsonNode? item in root["servers"]!.AsArray())
            {
                string? name = item!["name"]?.GetValue<string>();
                if (String.Equals(name, "other", StringComparison.Ordinal)) foundOther = true;
                if (String.Equals(name, "armada", StringComparison.Ordinal)) foundManaged = true;
            }

            AssertTrue(foundOther, "other Mux server remains");
            AssertFalse(foundManaged, "direct Armada Mux server is gone");
            AssertEqual(NestedUrl, root["nested"]!["servers"]![0]!["url"]!.GetValue<string>(), "nested Mux server remains");
            byte[] afterRemove = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
            McpConfigHelper.ApplyResult removedAgain = await McpConfigHelper.RemoveTargetAsync(target).ConfigureAwait(false);
            AssertFalse(removedAgain.Changed, "second Mux remove finds nothing");
            byte[] afterAgain = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
            AssertBytes(afterRemove, afterAgain, "second Mux remove does not write");

            string insert = original.Replace("    " + OldMuxObject + "\n", "", StringComparison.Ordinal);
            AssertEqual(-1, insert.IndexOf(OldMuxObject, StringComparison.Ordinal), "add fixture has no managed Mux object");
            string insertPath = path + ".insert";
            await WriteBomAsync(insertPath, insert).ConfigureAwait(false);
            McpConfigHelper.ApplyResult inserted = await McpConfigHelper.InstallTargetAsync(MuxTarget(insertPath)).ConfigureAwait(false);
            AssertTrue(inserted.Changed, "install adds a missing Mux entry");
            string insertedText = await ReadTextAsync(insertPath).ConfigureAwait(false);
            AssertPreservedMarkers(insertedText);
            JsonObject insertedRoot = ParseObject(insertedText);
            bool added = false;
            foreach (JsonNode? item in insertedRoot["servers"]!.AsArray())
            {
                if (String.Equals(item!["name"]?.GetValue<string>(), "armada", StringComparison.Ordinal)
                    && String.Equals(item["url"]?.GetValue<string>(), "http://127.0.0.1:9", StringComparison.Ordinal))
                {
                    added = true;
                }
            }

            AssertTrue(added, "added Mux entry");
            AssertEqual(NestedUrl, insertedRoot["nested"]!["servers"]![0]!["url"]!.GetValue<string>(), "nested Mux entry");
        }

        private static async Task AssertReplacedSpanAsync(
            McpConfigHelper.ConfigTarget target,
            string original,
            string oldValue,
            string serialized)
        {
            int at = original.IndexOf(oldValue, StringComparison.Ordinal);
            int later = original.IndexOf(oldValue, at + oldValue.Length, StringComparison.Ordinal);
            AssertTrue(at >= 0, "fixture contains the managed value");
            AssertEqual(-1, later, "fixture contains one managed value");
            string expected = original.Substring(0, at) + serialized + original.Substring(at + oldValue.Length);
            await WriteBomAsync(target.FilePath, original).ConfigureAwait(false);

            McpConfigHelper.ApplyResult installed = await McpConfigHelper.InstallTargetAsync(target).ConfigureAwait(false);
            AssertTrue(installed.Changed, "install changes the managed entry");
            AssertEqual(target.IsProjectScoped, installed.IsProjectScoped, "project scope is unchanged");
            byte[] afterInstall = await File.ReadAllBytesAsync(target.FilePath).ConfigureAwait(false);
            AssertBytes(WithBom(expected), afterInstall, "install keeps unrelated bytes");

            McpConfigHelper.ApplyResult again = await McpConfigHelper.InstallTargetAsync(target).ConfigureAwait(false);
            AssertFalse(again.Changed, "second install matches the managed entry");
            byte[] afterAgain = await File.ReadAllBytesAsync(target.FilePath).ConfigureAwait(false);
            AssertBytes(afterInstall, afterAgain, "second install does not write");
        }

        private static async Task AssertMarkersRemainAfterRemoveAsync(McpConfigHelper.ConfigTarget target, string container)
        {
            McpConfigHelper.ApplyResult removed = await McpConfigHelper.RemoveTargetAsync(target).ConfigureAwait(false);
            AssertTrue(removed.Changed, "remove deletes the managed entry");
            string text = await ReadTextAsync(target.FilePath).ConfigureAwait(false);
            AssertPreservedMarkers(text);
            AssertFalse(text.Contains("http://127.0.0.1:9/mcp", StringComparison.Ordinal), "managed URL is gone");
            AssertFalse(text.Contains("http://old.example", StringComparison.Ordinal), "old URL is gone");
            JsonObject root = ParseObject(text);
            string containerName = target.IsOpenCodeConfig ? "mcp" : container;
            AssertFalse(root[containerName]!.AsObject().ContainsKey("armada"), "direct Armada entry is gone");
            AssertEqual("http://other.example", root[containerName]!["other"]!["url"]!.GetValue<string>(), "other entry remains");
            AssertEqual(NestedUrl, root["nested"]![containerName]!["armada"]!["url"]!.GetValue<string>(), "nested entry remains");

            byte[] afterRemove = await File.ReadAllBytesAsync(target.FilePath).ConfigureAwait(false);
            McpConfigHelper.ApplyResult removedAgain = await McpConfigHelper.RemoveTargetAsync(target).ConfigureAwait(false);
            AssertFalse(removedAgain.Changed, "second remove finds nothing");
            byte[] afterAgain = await File.ReadAllBytesAsync(target.FilePath).ConfigureAwait(false);
            AssertBytes(afterRemove, afterAgain, "second remove does not write");
        }

        private static async Task AssertCreatesEntryAsync(McpConfigHelper.ConfigTarget target, bool mux)
        {
            McpConfigHelper.ApplyResult created = await McpConfigHelper.InstallTargetAsync(target).ConfigureAwait(false);
            AssertTrue(created.Changed, "missing or blank file gains an entry");
            AssertTrue(File.Exists(target.FilePath), "file is created");
            string text = await File.ReadAllTextAsync(target.FilePath).ConfigureAwait(false);
            JsonObject root = ParseObject(text);
            if (mux)
            {
                bool found = false;
                foreach (JsonNode? item in root["servers"]!.AsArray())
                {
                    if (String.Equals(item!["name"]?.GetValue<string>(), "armada", StringComparison.Ordinal))
                        found = true;
                }

                AssertTrue(found, "created Mux entry");
            }
            else if (target.IsOpenCodeConfig)
            {
                AssertEqual("http://127.0.0.1:9/mcp", root["mcp"]!["armada"]!["url"]!.GetValue<string>(), "created OpenCode entry");
            }
            else
            {
                AssertEqual("http://127.0.0.1:9/mcp", root["mcpServers"]!["armada"]!["url"]!.GetValue<string>(), "created mcpServers entry");
            }

            byte[] once = await File.ReadAllBytesAsync(target.FilePath).ConfigureAwait(false);
            McpConfigHelper.ApplyResult again = await McpConfigHelper.InstallTargetAsync(target).ConfigureAwait(false);
            AssertFalse(again.Changed, "created entry matches on the next install");
            byte[] twice = await File.ReadAllBytesAsync(target.FilePath).ConfigureAwait(false);
            AssertBytes(once, twice, "matching install does not rewrite a created file");
        }

        private static async Task ExpectUnchangedAsync(McpConfigHelper.ConfigTarget target, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target.FilePath)!);
            byte[] original = Encoding.UTF8.GetBytes(text);
            await File.WriteAllBytesAsync(target.FilePath, original).ConfigureAwait(false);
            await ExpectRefusalAsync(() => McpConfigHelper.InstallTargetAsync(target), target.FilePath, original, "install").ConfigureAwait(false);
            await ExpectRefusalAsync(() => McpConfigHelper.RemoveTargetAsync(target), target.FilePath, original, "remove").ConfigureAwait(false);
        }

        private static async Task ExpectRefusalAsync(Func<Task> action, string path, byte[] original, string label)
        {
            Exception? caught = null;
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                caught = ex;
            }

            AssertNotNull(caught, label + " refuses the document");
            byte[] after = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
            AssertBytes(original, after, label + " leaves the file unchanged");
        }

        private static void AssertPreservedMarkers(string text)
        {
            AssertContains(Comment, text, "line comment");
            AssertContains(BlockComment, text, "block comment");
            AssertContains(BraceText, text, "braces inside a string");
            AssertContains(OtherUrl, text, "other server and its comma");
            AssertContains(NestedUrl, text, "nested server");
            AssertTrue(text.IndexOf('\uFEFF') < 0, "BOM is not inserted into the text");
        }

        private static JsonObject ParseObject(string text)
        {
            JsonNode? node = JsonNode.Parse(text, null, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
            AssertNotNull(node, "edited document parses");
            return node!.AsObject();
        }

        private static string KeyedDocument(string container)
        {
            return "{\n"
                + "  " + Comment + "\n"
                + "  " + BlockComment + "\n"
                + "  " + BraceText + ",\n"
                + "  \"nested\": { \"" + container + "\": { \"armada\": { \"url\": \"" + NestedUrl + "\" } } },\n"
                + "  \"" + container + "\": {\n"
                + "    \"other\": { " + OtherUrl.Trim() + " },\n"
                + "    \"armada\": " + OldObjectValue + "\n"
                + "  },\n"
                + "}\n";
        }

        private static string MuxDocument()
        {
            return "{\n"
                + "  " + Comment + "\n"
                + "  " + BlockComment + "\n"
                + "  " + BraceText + ",\n"
                + "  \"nested\": { \"servers\": [ { \"name\": \"armada\", \"url\": \"" + NestedUrl + "\" } ] },\n"
                + "  \"servers\": [\n"
                + "    { \"name\": \"other\", " + OtherUrl.Trim() + " },\n"
                + "    " + OldMuxObject + "\n"
                + "  ],\n"
                + "}\n";
        }

        private static JsonObject KeyedConfig()
        {
            return new JsonObject
            {
                ["type"] = "http",
                ["url"] = "http://127.0.0.1:9/mcp",
            };
        }

        private static McpConfigHelper.ConfigTarget KeyedTarget(string path)
        {
            return new McpConfigHelper.ConfigTarget("Claude Code", path, KeyedConfig());
        }

        private static McpConfigHelper.ConfigTarget OpenCodeTarget(string path)
        {
            return new McpConfigHelper.ConfigTarget(
                "OpenCode",
                path,
                new JsonObject
                {
                    ["type"] = "remote",
                    ["url"] = "http://127.0.0.1:9/mcp",
                    ["enabled"] = true,
                },
                IsOpenCodeConfig: true);
        }

        private static McpConfigHelper.ConfigTarget MuxTarget(string path)
        {
            return new McpConfigHelper.ConfigTarget(
                "Mux",
                path,
                new JsonObject
                {
                    ["name"] = "armada",
                    ["transport"] = "http",
                    ["url"] = "http://127.0.0.1:9",
                    ["mcpPath"] = "/mcp",
                },
                IsMuxServers: true);
        }

        private static async Task WriteBomAsync(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, WithBom(text)).ConfigureAwait(false);
        }

        private static async Task<string> ReadTextAsync(string path)
        {
            byte[] bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
            AssertTrue(HasBom(bytes), "UTF-8 BOM remains");
            return Encoding.UTF8.GetString(bytes, Utf8Bom.Length, bytes.Length - Utf8Bom.Length);
        }

        private static byte[] WithBom(string text)
        {
            byte[] body = Encoding.UTF8.GetBytes(text);
            byte[] result = new byte[Utf8Bom.Length + body.Length];
            Buffer.BlockCopy(Utf8Bom, 0, result, 0, Utf8Bom.Length);
            Buffer.BlockCopy(body, 0, result, Utf8Bom.Length, body.Length);
            return result;
        }

        private static bool HasBom(byte[] bytes)
        {
            return bytes.Length >= Utf8Bom.Length
                && bytes[0] == Utf8Bom[0]
                && bytes[1] == Utf8Bom[1]
                && bytes[2] == Utf8Bom[2];
        }

        private static void AssertBytes(byte[] expected, byte[] actual, string label)
        {
            AssertEqual(expected.Length, actual.Length, label + " length");
            for (int i = 0; i < expected.Length; i++)
            {
                if (expected[i] != actual[i])
                    AssertEqual(expected[i], actual[i], label + " at byte " + i);
            }
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

        #endregion
    }
}

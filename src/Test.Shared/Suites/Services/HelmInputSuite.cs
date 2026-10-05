namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Services;
    using Armada.Helm.Commands;
    using Armada.Helm.Infrastructure;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Helm input handling that used to rest on text: runtime names map to <see cref="AgentRuntimeEnum"/> or are
    /// rejected (an unknown or unlisted runtime used to become Claude Code), <c>armada go</c> missions come only from
    /// explicit --task values (prompts used to be split on "1." and ";"), HTTP failures carry a status code (callers
    /// used to search the message for "HTTP 409"), and managed instruction blocks with broken markers are refused.
    /// </summary>
    public sealed class HelmInputSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.HelmInput";
        private const string Begin = "<!-- armada:mcp:begin -->";
        private const string End = "<!-- armada:mcp:end -->";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Helm input suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("runtime_names_map_to_every_runtime", "Runtime names and aliases map to every AgentRuntimeEnum value", TestTags.Positive, () =>
            {
                Dictionary<string, AgentRuntimeEnum> expected = new Dictionary<string, AgentRuntimeEnum>(StringComparer.Ordinal)
                {
                    ["claude"] = AgentRuntimeEnum.ClaudeCode,
                    ["ClaudeCode"] = AgentRuntimeEnum.ClaudeCode,
                    ["claude-code"] = AgentRuntimeEnum.ClaudeCode,
                    ["codex"] = AgentRuntimeEnum.Codex,
                    ["GEMINI"] = AgentRuntimeEnum.Gemini,
                    ["cursor"] = AgentRuntimeEnum.Cursor,
                    ["mux"] = AgentRuntimeEnum.Mux,
                    ["opencode"] = AgentRuntimeEnum.OpenCode,
                    ["open-code"] = AgentRuntimeEnum.OpenCode,
                    ["api"] = AgentRuntimeEnum.ApiEndpoint,
                    ["ApiEndpoint"] = AgentRuntimeEnum.ApiEndpoint,
                    ["custom"] = AgentRuntimeEnum.Custom,
                    [" codex "] = AgentRuntimeEnum.Codex
                };

                foreach (KeyValuePair<string, AgentRuntimeEnum> pair in expected)
                {
                    AssertTrue(AgentRuntimeParser.TryParse(pair.Key, out AgentRuntimeEnum runtime), "'" + pair.Key + "' should parse");
                    AssertEqual(pair.Value, runtime, "runtime for '" + pair.Key + "'");
                }

                foreach (AgentRuntimeEnum value in Enum.GetValues<AgentRuntimeEnum>())
                {
                    AssertTrue(AgentRuntimeParser.TryParse(value.ToString(), out AgentRuntimeEnum roundTrip), value + " should parse by name");
                    AssertEqual(value, roundTrip);
                }
            }));

            cases.Add(Case("unknown_runtime_names_are_rejected", "Unknown runtime names are rejected instead of becoming Claude Code", TestTags.Negative, () =>
            {
                foreach (string value in new string[] { "", "  ", "claud", "gpt", "0", "3", "ClaudeCode,Codex" })
                {
                    AssertFalse(AgentRuntimeParser.TryParse(value, out AgentRuntimeEnum _), "'" + value + "' must be rejected");
                }

                AssertContains("opencode", AgentRuntimeParser.DescribeInvalid("gpt"), "error lists accepted values");
            }));

            cases.Add(Case("go_prompt_is_never_split", "armada go keeps a prompt as one mission and uses explicit --task values", TestTags.Negative, () =>
            {
                string[] prompts = new string[]
                {
                    "Add rate limiting; Add request logging",
                    "1. Extract UserRepository 2. Add ILogger",
                    "Bump to v1. Then update v2. docs"
                };
                foreach (string prompt in prompts)
                {
                    List<string> single = GoTaskList.Build(prompt, null);
                    AssertEqual(1, single.Count, "prompt '" + prompt + "' must stay one mission");
                    AssertEqual(prompt, single[0]);
                }

                List<string> explicitTasks = GoTaskList.Build("API hardening", new string[] { "Add rate limiting", " ", "Add logging; with request ids" });
                AssertEqual(2, explicitTasks.Count, "blank --task values are dropped");
                AssertEqual("Add rate limiting", explicitTasks[0]);
                AssertEqual("Add logging; with request ids", explicitTasks[1], "a task is never split either");

                AssertEqual(0, GoTaskList.Build("  ", Array.Empty<string>()).Count, "nothing to dispatch");
            }));

            cases.Add(CaseAsync("http_error_carries_status_code", "Helm HTTP errors carry the status code", TestTags.Negative, async () =>
            {
                using HttpResponseMessage conflict = new HttpResponseMessage(HttpStatusCode.Conflict)
                {
                    Content = new StringContent("{\"error\":\"An evaluation is already running\"}")
                };
                HttpRequestException ex = await HelmHttpError.FromResponseAsync(conflict, "POST /api/v1/vessel-health/evaluate").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.Conflict, ex.StatusCode);
                AssertContains("already running", ex.Message, "body is kept for display");
            }));

            cases.Add(Case("managed_block_upsert_and_remove", "Managed instruction block is inserted, replaced and removed", TestTags.Positive, () =>
            {
                string block = Begin + "\nnew\n" + End;
                string appended = ManagedBlockEditor.Upsert("# Mine\n", block, Begin, End);
                AssertContains("# Mine", appended);
                AssertContains(block, appended);

                string replaced = ManagedBlockEditor.Upsert("# Mine\n\n" + Begin + "\nold\n" + End + "\n\n# After\n", block, Begin, End);
                AssertContains("new", replaced);
                AssertFalse(replaced.Contains("old", StringComparison.Ordinal), "old block replaced");
                AssertContains("# After", replaced);

                string removed = ManagedBlockEditor.Remove(replaced, Begin, End);
                AssertFalse(removed.Contains(Begin, StringComparison.Ordinal), "block removed");
                AssertContains("# Mine", removed);
                AssertContains("# After", removed);
                AssertEqual("plain\n", ManagedBlockEditor.Remove("plain\n", Begin, End), "no markers, unchanged");
            }));

            cases.Add(Case("managed_block_malformed_markers_are_refused", "Begin without end, end before begin, and duplicate blocks are refused", TestTags.Negative, () =>
            {
                string[] malformed = new string[]
                {
                    "# Mine\n" + Begin + "\nold\n# user text after a lost end marker\n",
                    "# Mine\n" + End + "\n" + Begin + "\n",
                    Begin + "\na\n" + End + "\n" + Begin + "\nb\n" + End + "\n",
                    "# Mine\n" + End + "\n"
                };
                foreach (string text in malformed)
                {
                    bool upsertThrew = false;
                    try { ManagedBlockEditor.Upsert(text, Begin + "\nnew\n" + End, Begin, End); }
                    catch (InvalidDataException) { upsertThrew = true; }
                    AssertTrue(upsertThrew, "upsert must refuse malformed markers");

                    bool removeThrew = false;
                    try { ManagedBlockEditor.Remove(text, Begin, End); }
                    catch (InvalidDataException) { removeThrew = true; }
                    AssertTrue(removeThrew, "remove must refuse malformed markers");
                }
            }));

            cases.Add(CaseAsync("mcp_descriptions_list_complete_enum_values", "MCP tool and argument descriptions that list an enum's values list all of them", TestTags.Positive, async () =>
            {
                List<string> texts = new List<string>();
                string dataDir = TestTemp.NewDirectory("mcp-descriptions");
                Armada.Core.Settings.ArmadaSettings settings = new Armada.Core.Settings.ArmadaSettings();
                settings.DataDirectory = dataDir;
                settings.LogDirectory = Path.Combine(dataDir, "logs");
                settings.DocksDirectory = Path.Combine(dataDir, "docks");
                settings.ReposDirectory = Path.Combine(dataDir, "repos");
                SyslogLogging.LoggingModule logging = new SyslogLogging.LoggingModule();
                logging.Settings.EnableConsole = false;
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                using (McpStdioToolSet toolSet = new McpStdioToolSet(settings, logging, testDb.Driver))
                {
                    toolSet.Register((name, description, schema, handler) =>
                    {
                        texts.Add(name + ": " + description);
                        McpSchemaNode node = JsonHelper.Deserialize<McpSchemaNode>(System.Text.Json.JsonSerializer.Serialize(schema));
                        node.CollectDescriptions(name, texts);
                    });
                }

                AssertTrue(texts.Count > 500, "expected tool and argument descriptions, got " + texts.Count);

                // Arguments that take an enum value: each description must name every value of its enum (the
                // descriptions are generated from the enums so they cannot go stale).
                Dictionary<string, Type> enumArguments = new Dictionary<string, Type>(StringComparer.Ordinal)
                {
                    ["create_captain.runtime"] = typeof(AgentRuntimeEnum),
                    ["update_captain.runtime"] = typeof(AgentRuntimeEnum),
                    ["create_captain.reasoningEffort"] = typeof(ReasoningEffortEnum),
                    ["update_captain.reasoningEffort"] = typeof(ReasoningEffortEnum),
                    ["create_captain.tier"] = typeof(CaptainTierEnum),
                    ["update_captain.tier"] = typeof(CaptainTierEnum),
                    ["transition_mission_status.status"] = typeof(MissionStatusEnum),
                    ["create_model_endpoint.kind"] = typeof(ModelEndpointKindEnum),
                    ["create_model_endpoint.provider"] = typeof(ModelProviderEnum),
                    ["update_model_endpoint.kind"] = typeof(ModelEndpointKindEnum),
                    ["update_model_endpoint.provider"] = typeof(ModelProviderEnum),
                    ["create_pipeline.stages[].reviewDenyAction"] = typeof(ReviewDenyActionEnum),
                    ["update_pipeline.stages[].reviewDenyAction"] = typeof(ReviewDenyActionEnum),
                    ["create_objective.status"] = typeof(ObjectiveStatusEnum),
                    ["create_objective.kind"] = typeof(ObjectiveKindEnum),
                    ["create_objective.priority"] = typeof(ObjectivePriorityEnum),
                    ["create_objective.backlogState"] = typeof(ObjectiveBacklogStateEnum),
                    ["create_objective.effort"] = typeof(ObjectiveEffortEnum),
                    ["update_backlog_item.status"] = typeof(ObjectiveStatusEnum),
                    ["list_backlog.kind"] = typeof(ObjectiveKindEnum),
                    ["papercut_summary.minSeverity"] = typeof(PapercutSeverityEnum),
                    ["create_release.status"] = typeof(ReleaseStatusEnum),
                    ["run_check.type"] = typeof(CheckRunTypeEnum),
                    ["create_mission.mode"] = typeof(MissionModeEnum)
                };

                List<string> problems = new List<string>();
                foreach (KeyValuePair<string, Type> argument in enumArguments)
                {
                    string? text = texts.FirstOrDefault(t => t.StartsWith(argument.Key + ": ", StringComparison.Ordinal));
                    if (text == null)
                    {
                        problems.Add(argument.Key + ": no such argument description");
                        continue;
                    }

                    HashSet<string> words = new HashSet<string>(System.Text.RegularExpressions.Regex.Split(text, "[^A-Za-z0-9_]+"), StringComparer.Ordinal);
                    List<string> missing = Enum.GetNames(argument.Value).Where(n => !words.Contains(n)).ToList();
                    if (missing.Count > 0) problems.Add(argument.Key + " is missing " + argument.Value.Name + " values: " + String.Join(", ", missing));
                }

                // Known stale statements from the pre-1.0 audit.
                AssertFalse(texts.Any(t => t.StartsWith("enumerate.status: ", StringComparison.Ordinal) && t.Contains("Active/Complete/Cancelled", StringComparison.Ordinal)), "voyage statuses are not Active/Complete/Cancelled");
                AssertTrue(texts.Any(t => t.StartsWith("add_vessel.enableModelContext: ", StringComparison.Ordinal) && t.Contains("(default true)", StringComparison.Ordinal)), "add_vessel enables model context by default");

                AssertTrue(problems.Count == 0, "descriptions with incomplete enum lists:\n" + String.Join("\n", problems));
            }));

            cases.Add(Case("claude_agent_definition_uses_subagent_tools_field", "The generated Claude Code agent restricts tools with the subagent 'tools' field", TestTags.Positive, () =>
            {
                // McpConfigHelper is internal to Helm; read the generated definition through reflection.
                Type? helper = typeof(McpStdioToolSet).Assembly.GetType("Armada.Helm.Commands.McpConfigHelper");
                AssertNotNull(helper, "McpConfigHelper type");
                System.Reflection.MethodInfo? generate = helper!.GetMethod("GenerateAgentDefinition", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                AssertNotNull(generate, "GenerateAgentDefinition method");
                string definition = (string)generate!.Invoke(null, null)!;

                Dictionary<string, string> frontmatter = ParseFrontmatter(definition);
                HashSet<string> supported = new HashSet<string>(StringComparer.Ordinal) { "name", "description", "tools", "disallowedTools", "model", "permissionMode", "maxTurns", "skills", "mcpServers", "hooks", "memory", "background", "effort", "isolation", "color" };
                foreach (string key in frontmatter.Keys)
                    AssertTrue(supported.Contains(key), "unsupported Claude Code subagent frontmatter field: " + key);
                AssertEqual("armada", frontmatter["name"]);
                AssertTrue(frontmatter.ContainsKey("tools"), "the agent must restrict its tools with 'tools'");
                AssertEqual("mcp__armada", frontmatter["tools"], "server-level MCP entry grants every Armada tool");
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Helm Input",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static Dictionary<string, string> ParseFrontmatter(string markdown)
        {
            Dictionary<string, string> fields = new Dictionary<string, string>(StringComparer.Ordinal);
            string[] lines = markdown.Replace("\r\n", "\n").Split('\n');
            AssertTrue(lines.Length > 0 && lines[0].Trim() == "---", "definition starts with a frontmatter fence");
            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.Trim() == "---") return fields;
                int colon = line.IndexOf(':');
                if (colon <= 0) continue;
                fields[line.Substring(0, colon).Trim()] = line.Substring(colon + 1).Trim();
            }

            throw new AssertionException("frontmatter is not closed");
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

        #endregion
    }
}

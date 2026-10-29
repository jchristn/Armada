namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Reflection;
    using System.Text;
    using System.Text.Json.Serialization;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Server;
    using Test.Shared.Infrastructure;
    using Test.Shared.Infrastructure.ApiSurface;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// The guard for docs/ENUMERATION.md that looks at real response shapes rather than declared type names: every
    /// collection property of a model a route or tool can return declares a bound with <see cref="CollectionBoundAttribute"/>;
    /// every array in a live REST or MCP response is either a page's Objects or a declared bounded property within its
    /// limit; <see cref="CollectionBounds"/> enforces declared limits; and an oversized request is a typed error on REST
    /// and MCP rather than a silent truncation.
    /// </summary>
    public sealed class ResponseShapeSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "E2E.ResponseShape";

        // Routes whose body is not an Armada model (a generated document or a file), with why.
        private static readonly Dictionary<string, string> _NotModelResponses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["GET /openapi.json"] = "OpenAPI document: one entry per registered route, defined in code",
            ["GET /swagger"] = "API document viewer (HTML)",
            ["GET /api/v1/backup"] = "Backup archive download (ZIP stream)",
        };

        // Responses fixed in code, with the most items they may hold and why. Arrays inside them are code-defined too.
        private static readonly Dictionary<string, int> _CodeFixedResponses = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["GET /api/v1/ask/quick-actions"] = 10,
            ["MCP list_ask_quick_actions"] = 10,
            ["GET /api/v1/doctor"] = 11,
        };

        // Models in Armada.Core.Models that no REST route, MCP tool or WebSocket command returns, with why. They are not
        // roots of the declared-bounds check, but a response model that reaches one makes it checked again.
        private static readonly Dictionary<string, string> _InternalModels = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CaptainChatTurnResult"] = "In-process result of an Ask turn; its tool calls are stored and read through the paged Ask tool-call enumerations",
            ["DockLeaseState"] = "Landing's view of which missions hold a dock, used inside the landing pipeline",
            ["LandingDeferral"] = "Landing pipeline decision to defer a landing, recorded in-process",
            ["DetectedEcosystem"] = "Workflow-profile provisioning input built from a repository scan",
            ["EcosystemCommand"] = "Workflow-profile provisioning input built from a repository scan",
            ["ResolvedEcosystemCommand"] = "Workflow-profile provisioning input built from a repository scan",
            ["RepositoryLayout"] = "Repository file list used by provisioning on the server",
            ["GitAnchorsSnapshot"] = "Git evidence collected for prompt rendering",
            ["GitCommitMergeResult"] = "Result of the git integration barrier, consumed by the landing pipeline",
            ["MergeQueueMergeResult"] = "Result of merging a merge queue entry's branch, consumed by the merge queue",
            ["MergeQueueFailure"] = "Merge queue failure facts, stored as mission.landing_failed event Payload JSON (an event string field)",
            ["RemoteTunnelHandshakePayload"] = "Remote tunnel protocol frame (proxy link), not a REST or MCP response",
            ["RemoteTunnelHandshakeResponse"] = "Remote tunnel protocol frame (proxy link), not a REST or MCP response",
            ["RemoteTunnelHttpRelayRequest"] = "Remote tunnel protocol frame (proxy link), not a REST or MCP response",
            ["RemoteTunnelHttpRelayResponse"] = "Remote tunnel protocol frame (proxy link), not a REST or MCP response",
            ["ShellCommandSplit"] = "Parser output for CLI permission matching",
            ["UnifiedDiffFile"] = "Diff parser output used by landing and scope checks",
            ["UrlPathCanonicalizationResult"] = "Route matcher output used by authorization",
        };

        // Read-only MCP tools exercised by the live scan: names with these prefixes, plus the named tools.
        private static readonly string[] _ReadToolPrefixes = new[] { "get_", "list_", "search_" };
        private static readonly HashSet<string> _ReadTools = new HashSet<string>(StringComparer.Ordinal)
        {
            "enumerate", "status", "voyage_status", "mission_status", "vessel_health", "token_usage_summary",
            "papercut_summary", "fleet_action_run_status",
        };

        private static readonly string[] _EnumerateEntityTypes = new[]
        {
            "objectives", "model_endpoints", "harbors", "fleets", "vessels", "captains", "missions", "voyages", "docks",
            "signals", "events", "merge_queue", "personas", "prompt_templates", "pipelines", "playbooks",
            "workflow_profiles", "project_profiles", "skills", "check_runs", "releases", "deployments", "incidents",
            "runbooks", "runbook_executions", "vessel_import_batch", "fleet_action", "fleet_action_run", "vessel_health",
        };

        private ResponseShapeSeed? _Seed = null;
        private readonly SemaphoreSlim _SeedLock = new SemaphoreSlim(1, 1);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("collection_properties_declare_bounds", "Every collection property of a model reachable from a REST response or in Armada.Core.Models declares [CollectionBound]", async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                List<ApiRestRoute> routes = ApiSurfaceBuilder.Build(fx.Server).Rest;
                List<Type> roots = new List<Type>();
                roots.AddRange(ModelTypes().Where(t => !_InternalModels.ContainsKey(t.Name)));
                foreach (ApiRestRoute route in routes)
                {
                    foreach (string declared in route.Responses.Values)
                    {
                        if (!String.IsNullOrEmpty(declared)) roots.AddRange(ResolveTypes(declared));
                    }
                }

                StringBuilder problems = new StringBuilder();
                int checkedProperties = 0;
                foreach (Type type in Reachable(roots).OrderBy(t => t.FullName, StringComparer.Ordinal))
                {
                    foreach (PropertyInfo property in ResponseProperties(type))
                    {
                        if (!CollectionBounds.IsCollectionType(property.PropertyType)) continue;
                        checkedProperties++;
                        if (CollectionBounds.BoundOf(property) == null) problems.Append(type.FullName).Append('.').Append(property.Name).Append('\n');
                    }
                }

                AssertTrue(checkedProperties > 100, "the guard inspected the model collections (" + checkedProperties + ")");
                AssertTrue(problems.Length == 0, "collection properties without [CollectionBound] (bound them, or move them out of the response into a paged enumeration):\n" + problems);
            }));

            cases.Add(Case("collection_bounds_validate_declared_limits", "CollectionBounds rejects a declared list one item over its limit, naming the field, and accepts it at the limit", async () =>
            {
                await Task.CompletedTask.ConfigureAwait(false);
                int exercised = 0;
                StringBuilder problems = new StringBuilder();
                foreach (Type type in ModelTypes())
                {
                    if (type.GetConstructor(Type.EmptyTypes) == null) continue;
                    foreach (PropertyInfo property in ResponseProperties(type).Where(p => p.CanWrite && p.PropertyType == typeof(List<string>)))
                    {
                        CollectionBoundAttribute? bound = CollectionBounds.BoundOf(property);
                        if (bound == null || bound.Limit > CollectionLimits.MaxRequestItems) continue;
                        object model;
                        try
                        {
                            model = Activator.CreateInstance(type)!;
                            property.SetValue(model, Enumerable.Range(0, bound.Limit).Select(i => "v" + i).ToList());
                        }
                        catch (Exception)
                        {
                            continue;
                        }

                        exercised++;
                        try
                        {
                            CollectionBounds.Validate(model);
                        }
                        catch (CollectionLimitExceededException ex)
                        {
                            problems.Append(type.Name).Append('.').Append(property.Name).Append(" at its limit was rejected: ").Append(ex.Message).Append('\n');
                            continue;
                        }

                        property.SetValue(model, Enumerable.Range(0, bound.Limit + 1).Select(i => "v" + i).ToList());
                        try
                        {
                            CollectionBounds.Validate(model);
                            problems.Append(type.Name).Append('.').Append(property.Name).Append(" one over its limit was accepted\n");
                        }
                        catch (CollectionLimitExceededException ex)
                        {
                            if (!ex.Field.EndsWith(property.Name, StringComparison.Ordinal)) problems.Append(type.Name).Append('.').Append(property.Name).Append(" error names ").Append(ex.Field).Append('\n');
                            if (ex.Limit != bound.Limit || ex.Actual != bound.Limit + 1) problems.Append(type.Name).Append('.').Append(property.Name).Append(" error reports limit ").Append(ex.Limit).Append(" actual ").Append(ex.Actual).Append('\n');
                        }
                    }
                }

                AssertTrue(exercised > 10, "the validator was exercised on declared string lists (" + exercised + ")");
                AssertTrue(problems.Length == 0, "validator problems:\n" + problems);
            }));

            cases.Add(Case("live_rest_responses_carry_only_bounded_arrays", "Every array in every live GET response over a seeded world is a page's Objects or a declared bounded property within its limit", async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                ResponseShapeSeed seed = await SeedAsync(fx).ConfigureAwait(false);
                Dictionary<string, int> bounded = BoundedPropertyNames();
                List<ApiRestRoute> routes = ApiSurfaceBuilder.Build(fx.Server).Rest
                    .Where(r => String.Equals(r.Method, "GET", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                SortedSet<string> problems = new SortedSet<string>(StringComparer.Ordinal);
                List<string> exercised = new List<string>();
                using HttpClient client = new HttpClient();
                client.BaseAddress = fx.AuthClient.BaseAddress;
                client.DefaultRequestHeaders.Add("X-Api-Key", fx.ApiKey);
                client.Timeout = TimeSpan.FromSeconds(20);
                foreach (ApiRestRoute route in routes)
                {
                    string key = "GET " + route.Route;
                    if (_NotModelResponses.ContainsKey(key)) continue;
                    string? path = seed.Resolve(route.Route);
                    if (path == null) continue;
                    string body;
                    try
                    {
                        using HttpResponseMessage response = await client.GetAsync(path).ConfigureAwait(false);
                        if (response.StatusCode != HttpStatusCode.OK) continue;
                        string? mediaType = response.Content.Headers.ContentType?.MediaType;
                        if (mediaType == null || !mediaType.Contains("json", StringComparison.OrdinalIgnoreCase)) continue;
                        body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    }
                    catch (TaskCanceledException)
                    {
                        continue;
                    }

                    exercised.Add(key);
                    CheckArrays(key, body, bounded, problems);
                }

                AssertTrue(exercised.Count >= 60, "the scan read enough live GET responses (" + exercised.Count + " of " + routes.Count + ")");
                AssertTrue(problems.Count == 0, "unbounded arrays in live REST responses:\n" + String.Join("\n", problems));
            }));

            cases.Add(Case("live_mcp_reads_carry_only_bounded_arrays", "Every array in every live read-only MCP tool result over a seeded world is a page's Objects or a declared bounded property within its limit", async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                ResponseShapeSeed seed = await SeedAsync(fx).ConfigureAwait(false);
                Dictionary<string, int> bounded = BoundedPropertyNames();
                List<ApiMcpTool> tools = ApiSurfaceBuilder.Build(fx.Server).Mcp
                    .Where(t => _ReadTools.Contains(t.Name) || _ReadToolPrefixes.Any(p => t.Name.StartsWith(p, StringComparison.Ordinal)))
                    .ToList();

                using HttpClient mcp = new HttpClient();
                mcp.BaseAddress = fx.McpClient.BaseAddress;
                mcp.DefaultRequestHeaders.Add("X-Api-Key", fx.ApiKey);
                mcp.Timeout = TimeSpan.FromSeconds(30);
                SortedSet<string> problems = new SortedSet<string>(StringComparer.Ordinal);
                int exercised = 0;
                foreach (ApiMcpTool tool in tools)
                {
                    List<Dictionary<string, object>> calls = new List<Dictionary<string, object>>();
                    if (tool.Name == "enumerate")
                    {
                        foreach (string entityType in _EnumerateEntityTypes) calls.Add(new Dictionary<string, object> { ["entityType"] = entityType, ["pageSize"] = 1000, ["includeDescription"] = true, ["includeContext"] = true });
                    }
                    else
                    {
                        calls.Add(seed.ArgumentsFor(tool));
                    }

                    foreach (Dictionary<string, object> arguments in calls)
                    {
                        string text;
                        try
                        {
                            string rpc = await McpTestCalls.CallToolAsync(mcp, tool.Name, arguments).ConfigureAwait(false);
                            E2eMcpToolEnvelope envelope = JsonHelper.Deserialize<E2eMcpToolEnvelope>(rpc);
                            text = envelope.Result?.Content.FirstOrDefault()?.Text ?? "";
                        }
                        catch (TaskCanceledException)
                        {
                            continue;
                        }

                        string trimmed = text.TrimStart();
                        if (!(trimmed.StartsWith("{", StringComparison.Ordinal) || trimmed.StartsWith("[", StringComparison.Ordinal))) continue;
                        if (trimmed.Contains("\"ErrorCode\"", StringComparison.Ordinal) && !trimmed.Contains("\"Objects\"", StringComparison.Ordinal)) continue;
                        exercised++;
                        string label = "MCP " + tool.Name + (arguments.TryGetValue("entityType", out object? entityType) ? " " + entityType : "");
                        CheckArrays(label, text, bounded, problems);
                    }
                }

                AssertTrue(exercised >= 40, "the scan read enough live MCP results (" + exercised + ")");
                AssertTrue(problems.Count == 0, "unbounded arrays in live MCP results:\n" + String.Join("\n", problems));
            }));

            cases.Add(Case("oversized_requests_are_typed_errors", "A bulk request one id over the limit is a REST 400 and an MCP InvalidArgument, both with Code CollectionLimitExceeded, and deletes nothing", async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                ResponseShapeSeed seed = await SeedAsync(fx).ConfigureAwait(false);
                List<string> ids = Enumerable.Range(0, CollectionLimits.MaxRequestItems).Select(i => "msn_none" + i).ToList();
                ids.Add(seed.MissionId);

                using (HttpResponseMessage response = await fx.AuthClient.PostAsync("/api/v1/missions/delete/multiple", JsonHelper.ToJsonContent(new { Ids = ids })).ConfigureAwait(false))
                {
                    string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.BadRequest, response.StatusCode, body);
                    E2eLimitErrorBody error = JsonHelper.Deserialize<E2eLimitErrorBody>(body);
                    AssertEqual(CollectionLimitExceededException.ErrorCode, error.Data?.Code, body);
                    AssertEqual(CollectionLimits.MaxRequestItems, error.Data!.Limit, "limit");
                    AssertEqual((long)ids.Count, error.Data.Actual, "actual");
                }

                using (HttpClient mcp = new HttpClient())
                {
                    mcp.BaseAddress = fx.McpClient.BaseAddress;
                    mcp.DefaultRequestHeaders.Add("X-Api-Key", fx.ApiKey);
                    string rpc = await McpTestCalls.CallToolAsync(mcp, "delete_missions", new { ids = ids }).ConfigureAwait(false);
                    E2eMcpToolEnvelope envelope = JsonHelper.Deserialize<E2eMcpToolEnvelope>(rpc);
                    string text = envelope.Result?.Content.FirstOrDefault()?.Text ?? "";
                    McpToolError error = JsonHelper.Deserialize<McpToolError>(text);
                    AssertEqual(McpToolErrorCodeEnum.InvalidArgument, error.ErrorCode, text);
                    AssertEqual(CollectionLimitExceededException.ErrorCode, error.Code, text);
                }

                using HttpResponseMessage still = await fx.AuthClient.GetAsync("/api/v1/missions/" + seed.MissionId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, still.StatusCode, "a rejected request deletes nothing");
            }));

            return new TestSuiteDescriptor(SuiteId, "Live response shapes are bounded", cases);
        }

        #endregion

        #region Private-Methods

        private static void CheckArrays(string label, string json, Dictionary<string, int> bounded, SortedSet<string> problems)
        {
            List<JsonArrayShape> arrays;
            try
            {
                arrays = JsonArrays.Find(json);
            }
            catch (System.Text.Json.JsonException)
            {
                return;
            }

            if (_CodeFixedResponses.TryGetValue(label, out int fixedLimit))
            {
                JsonArrayShape? root = arrays.FirstOrDefault(a => a.Path.Length == 0);
                if (root != null && root.Count > fixedLimit) problems.Add(label + ": code-fixed catalog holds " + root.Count + ", over " + fixedLimit);
                return;
            }

            foreach (JsonArrayShape array in arrays)
            {
                if (array.IsPageObjects)
                {
                    if (array.Count > CollectionLimits.MaxPageSize) problems.Add(label + ": " + array.Path + " page holds " + array.Count);
                    continue;
                }

                if (array.Name == "[]" && array.Path.Length == 0)
                {
                    problems.Add(label + ": the response is a bare list");
                    continue;
                }

                string name = array.Name == "[]" ? LastName(array.Path) : array.Name;
                if (!bounded.TryGetValue(name, out int limit))
                {
                    problems.Add(label + ": " + array.Path + " is not a declared bounded property");
                    continue;
                }

                if (array.Count > limit) problems.Add(label + ": " + array.Path + " holds " + array.Count + ", over its limit " + limit);
            }
        }

        private static string LastName(string path)
        {
            string trimmed = path.Replace("[]", "");
            int dot = trimmed.LastIndexOf('.');
            return dot < 0 ? trimmed : trimmed.Substring(dot + 1);
        }

        private static List<Type> ModelTypes()
        {
            return typeof(EnumerationQuery).Assembly.GetTypes()
                .Where(t => t.IsClass && t.IsPublic && !t.IsAbstract && t.Namespace != null && t.Namespace.StartsWith("Armada.Core.Models", StringComparison.Ordinal))
                .Where(t => !typeof(Exception).IsAssignableFrom(t))
                .ToList();
        }

        private static List<Type> ResolveTypes(string declared)
        {
            List<Type> result = new List<Type>();
            foreach (string name in declared.Split(new[] { '<', '>', ',', ' ', '[', ']' }, StringSplitOptions.RemoveEmptyEntries))
            {
                result.AddRange(ArmadaTypes().Where(t => t.Name == name));
            }

            return result;
        }

        private static List<Type> ArmadaTypes()
        {
            return new[] { typeof(EnumerationQuery).Assembly, typeof(ArmadaServer).Assembly }
                .SelectMany(a => a.GetTypes())
                .Where(t => t.IsClass && t.IsPublic && !t.IsGenericTypeDefinition)
                .ToList();
        }

        private static IEnumerable<Type> Reachable(IEnumerable<Type> roots)
        {
            HashSet<Type> seen = new HashSet<Type>();
            Queue<Type> queue = new Queue<Type>(roots);
            while (queue.Count > 0)
            {
                Type type = queue.Dequeue();
                if (type.IsGenericType && !type.IsGenericTypeDefinition)
                {
                    foreach (Type argument in type.GetGenericArguments()) queue.Enqueue(argument);
                    if (!IsArmada(type)) continue;
                }

                if (type.IsArray)
                {
                    queue.Enqueue(type.GetElementType()!);
                    continue;
                }

                if (!IsArmada(type) || type.IsEnum || typeof(Exception).IsAssignableFrom(type)) continue;
                if (!seen.Add(type)) continue;
                foreach (PropertyInfo property in ResponseProperties(type)) queue.Enqueue(property.PropertyType);
            }

            return seen;
        }

        private static bool IsArmada(Type type)
        {
            return type.Namespace != null && type.Namespace.StartsWith("Armada.", StringComparison.Ordinal) && (type.IsClass || (type.IsValueType && !type.IsPrimitive));
        }

        private static IEnumerable<PropertyInfo> ResponseProperties(Type type)
        {
            return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
                .Where(p => !CollectionBounds.IsNeverSerialized(p));
        }

        private static Dictionary<string, int> BoundedPropertyNames()
        {
            // Name to the largest declared limit for that name across Armada types.
            Dictionary<string, int> result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (Type type in ArmadaTypes())
            {
                foreach (PropertyInfo property in ResponseProperties(type))
                {
                    CollectionBoundAttribute? bound = CollectionBounds.BoundOf(property);
                    if (bound == null) continue;
                    if (!result.TryGetValue(property.Name, out int existing) || existing < bound.Limit) result[property.Name] = bound.Limit;
                }
            }

            return result;
        }

        private async Task<ResponseShapeSeed> SeedAsync(E2EServerFixture fx)
        {
            await _SeedLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_Seed != null) return _Seed;
                string suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
                ResponseShapeSeed seed = new ResponseShapeSeed();

                Fleet fleet = await CreateAsync<Fleet>(fx, "/api/v1/fleets", new { Name = "shape-fleet-" + suffix }).ConfigureAwait(false);
                seed.FleetId = fleet.Id;
                Vessel vessel = await CreateAsync<Vessel>(fx, "/api/v1/vessels", new { Name = "shape-vessel-" + suffix, RepoUrl = TestRepoHelper.GetLocalBareRepoUrl(), FleetId = fleet.Id }).ConfigureAwait(false);
                seed.VesselId = vessel.Id;
                Captain captain = await CreateAsync<Captain>(fx, "/api/v1/captains", new { Name = "shape-captain-" + suffix }).ConfigureAwait(false);
                seed.CaptainId = captain.Id;
                Voyage voyage = await CreateAsync<Voyage>(fx, "/api/v1/voyages", new { Title = "shape-voyage-" + suffix }).ConfigureAwait(false);
                seed.VoyageId = voyage.Id;

                using (HttpResponseMessage response = await fx.AuthClient.PostAsync("/api/v1/missions", JsonHelper.ToJsonContent(new { Title = "shape-mission-" + suffix, VesselId = vessel.Id })).ConfigureAwait(false))
                {
                    string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.Created, response.StatusCode, "seed mission: " + body);
                    MissionCreateResponse wrapper = JsonHelper.Deserialize<MissionCreateResponse>(body);
                    seed.MissionId = wrapper.Mission != null ? wrapper.Mission.Id : JsonHelper.Deserialize<Mission>(body).Id;
                }

                for (int i = 0; i < 3; i++)
                {
                    using HttpResponseMessage signal = await fx.AuthClient.PostAsync("/api/v1/signals", JsonHelper.ToJsonContent(new { Type = "Nudge", Payload = "shape " + i, ToCaptainId = captain.Id })).ConfigureAwait(false);
                }

                _Seed = seed;
                return seed;
            }
            finally
            {
                _SeedLock.Release();
            }
        }

        private static async Task<T> CreateAsync<T>(E2EServerFixture fx, string route, object body)
        {
            using HttpResponseMessage response = await fx.AuthClient.PostAsync(route, JsonHelper.ToJsonContent(body)).ConfigureAwait(false);
            string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            AssertTrue(response.IsSuccessStatusCode, "seed " + route + ": " + (int)response.StatusCode + " " + text);
            return JsonHelper.Deserialize<T>(text);
        }

        private TestCaseDescriptor Case(string caseId, string displayName, Func<Task> body)
        {
            return new TestCaseDescriptor(SuiteId, caseId, displayName, (CancellationToken ct) => body());
        }

        #endregion
    }
}

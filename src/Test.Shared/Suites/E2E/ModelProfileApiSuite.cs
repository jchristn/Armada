namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using Armada.Runtimes.Mcp;
    using Armada.Server.Routes;
    using McpToolCallResult = Armada.Runtimes.Mcp.McpToolCallResult;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// End-to-end model profiles through the real server: REST list (paged, filtered, tenant-scoped), enumerate, get with
    /// revisions, update of the mutable parts (admins only, omitted sections kept, identity unchanged), symmetric
    /// compatibility marks, the captain's modelProfileId, 404/400/403 paths, and the MCP tools get_model_profile and
    /// list_model_profiles with the same rules.
    /// </summary>
    public sealed class ModelProfileApiSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "E2E.ModelProfiles";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("query_defaults", "A querystring with no filters parses to the defaults; bad page values are rejected", () =>
            {
                ModelProfileQuery empty = ModelProfileRoutes.ParseQuery(key => null);
                AssertNull(empty.TenantId, "tenant");
                AssertNull(empty.Runtime, "runtime");
                AssertNull(empty.Provider, "provider");
                AssertNull(empty.ModelContains, "model");
                AssertEqual(1, empty.PageNumber, "page number");
                AssertEqual(100, empty.PageSize, "page size");
                Dictionary<string, string?> full = new Dictionary<string, string?> { ["runtime"] = " Mux ", ["provider"] = " ", ["model"] = "qwen", ["pageNumber"] = "2", ["pageSize"] = "5000" };
                ModelProfileQuery parsed = ModelProfileRoutes.ParseQuery(key => full.TryGetValue(key, out string? v) ? v : null);
                AssertEqual("Mux", parsed.Runtime, "trimmed");
                AssertNull(parsed.Provider, "blank is null");
                AssertEqual("qwen", parsed.ModelContains, "model");
                AssertEqual(2, parsed.PageNumber, "page number");
                AssertEqual(1000, parsed.PageSize, "clamped");
                AssertThrows<ArgumentException>(() => ModelProfileRoutes.ParseQuery(key => key == "pageSize" ? "x" : null), "bad page size");
                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("rest_contract", "REST: list, enumerate, get with revisions, update, compatibility marks, captain reference, tenant isolation and errors", async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                E2ETenantUser adminA = await E2ETenantUser.CreateAsync(fx.AuthClient, "mdl-admin-a", true).ConfigureAwait(false);
                E2ETenantUser userA = await E2ETenantUser.CreateAsync(fx.AuthClient, "mdl-user-a", false, adminA.TenantId).ConfigureAwait(false);
                E2ETenantUser adminB = await E2ETenantUser.CreateAsync(fx.AuthClient, "mdl-admin-b", true).ConfigureAwait(false);
                Seeded seeded = await SeedAsync(fx, adminA.TenantId).ConfigureAwait(false);
                ModelProfileResolution other = await ResolveAsync(fx, Observation(adminA.TenantId, null, "ClaudeCode", null, "1.0.0")).ConfigureAwait(false);

                using HttpClient a = adminA.CreateClient(fx.BaseUrl);
                using HttpClient u = userA.CreateClient(fx.BaseUrl);
                using HttpClient b = adminB.CreateClient(fx.BaseUrl);
                HttpClient global = fx.AuthClient;

                EnumerationResult<ModelProfile> listed = await GetAsync<EnumerationResult<ModelProfile>>(u, "/api/v1/model-profiles").ConfigureAwait(false);
                AssertEqual(2L, listed.TotalRecords, "two profiles in tenant A");
                AssertEqual(other.Profile.Id, listed.Objects[0].Id, "newest first");
                AssertTrue(listed.Objects.All(p => p.Revisions.Count == 0), "list pages carry no revisions");
                EnumerationResult<ModelProfile> paged = await GetAsync<EnumerationResult<ModelProfile>>(u, "/api/v1/model-profiles?pageSize=1&pageNumber=2").ConfigureAwait(false);
                AssertEqual(seeded.Profile.Id, paged.Objects.Single().Id, "page two");
                AssertEqual(2, paged.TotalPages, "two pages");
                EnumerationResult<ModelProfile> mux = await GetAsync<EnumerationResult<ModelProfile>>(u, "/api/v1/model-profiles?runtime=mux&provider=ollama&model=QWEN").ConfigureAwait(false);
                AssertEqual(seeded.Profile.Id, mux.Objects.Single().Id, "filters");
                EnumerationResult<ModelProfile> leakedQuery = await GetAsync<EnumerationResult<ModelProfile>>(u, "/api/v1/model-profiles?tenantId=" + adminB.TenantId).ConfigureAwait(false);
                AssertEqual(2L, leakedQuery.TotalRecords, "a non-global caller cannot pick another tenant");

                HttpResponseMessage enumerated = await u.PostAsync("/api/v1/model-profiles/enumerate", JsonHelper.ToJsonContent(new { runtime = "ClaudeCode", pageSize = 10 })).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.OK, enumerated, "enumerate");
                EnumerationResult<ModelProfile> byBody = await JsonHelper.DeserializeAsync<EnumerationResult<ModelProfile>>(enumerated).ConfigureAwait(false);
                AssertEqual(other.Profile.Id, byBody.Objects.Single().Id, "enumerate filter");
                AssertStatusCode(HttpStatusCode.OK, await u.PostAsync("/api/v1/model-profiles/enumerate", null).ConfigureAwait(false), "empty body is the defaults");
                AssertStatusCode(HttpStatusCode.BadRequest, await u.PostAsync("/api/v1/model-profiles/enumerate", new StringContent("{not json")).ConfigureAwait(false), "malformed body");

                ModelProfile got = await GetAsync<ModelProfile>(u, "/api/v1/model-profiles/" + seeded.Profile.Id).ConfigureAwait(false);
                AssertEqual("Mux", got.Identity.Runtime, "identity");
                AssertEqual((int?)131072, got.Identity.ServedContextTokens, "served context");
                AssertEqual(2, got.Revisions.Count, "two revisions");
                AssertEqual(seeded.First.Id, got.Revisions[0].Id, "oldest first");
                AssertEqual("2.0.0", got.Revisions[1].RuntimeVersion, "major version revision");

                Captain captain = await GetAsync<Captain>(a, "/api/v1/captains/" + seeded.CaptainId).ConfigureAwait(false);
                AssertEqual(seeded.Profile.Id, captain.ModelProfileId, "the captain shows its profile");

                AssertStatusCode(HttpStatusCode.NotFound, await b.GetAsync("/api/v1/model-profiles/" + seeded.Profile.Id).ConfigureAwait(false), "another tenant");
                AssertEqual(0L, (await GetAsync<EnumerationResult<ModelProfile>>(b, "/api/v1/model-profiles").ConfigureAwait(false)).TotalRecords, "another tenant lists nothing");
                AssertEqual(seeded.Profile.Id, (await GetAsync<ModelProfile>(global, "/api/v1/model-profiles/" + seeded.Profile.Id).ConfigureAwait(false)).Id, "global admin reads any tenant");
                AssertStatusCode(HttpStatusCode.NotFound, await u.GetAsync("/api/v1/model-profiles/mdl_missing").ConfigureAwait(false), "missing");
                AssertStatusCode(HttpStatusCode.BadRequest, await u.GetAsync("/api/v1/model-profiles?pageSize=x").ConfigureAwait(false), "bad page size");

                string path = "/api/v1/model-profiles/" + seeded.Profile.Id;
                object update = new
                {
                    capabilities = new { toolsAvailable = true, mcpAvailable = false },
                    rendering = new { template = "Compact", repeatContractAtEnd = true, maxFilesPerMission = 3 },
                    notes = "Slow but careful."
                };
                AssertStatusCode(HttpStatusCode.Forbidden, await u.PutAsync(path, JsonHelper.ToJsonContent(update)).ConfigureAwait(false), "a regular user cannot update");
                AssertStatusCode(HttpStatusCode.NotFound, await b.PutAsync(path, JsonHelper.ToJsonContent(update)).ConfigureAwait(false), "another tenant's admin");
                HttpResponseMessage updated = await a.PutAsync(path, JsonHelper.ToJsonContent(update)).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.OK, updated, "admin update");
                ModelProfile afterUpdate = await JsonHelper.DeserializeAsync<ModelProfile>(updated).ConfigureAwait(false);
                AssertEqual((bool?)true, afterUpdate.Capabilities.ToolsAvailable, "tools");
                AssertEqual("Compact", afterUpdate.Rendering.Template, "template");
                AssertEqual((int?)3, afterUpdate.Rendering.MaxFilesPerMission, "max files");
                AssertEqual("Slow but careful.", afterUpdate.Notes, "notes");
                AssertEqual("qwen3.8:27b", afterUpdate.Identity.Model, "identity unchanged");
                AssertEqual(2, afterUpdate.Revisions.Count, "revisions returned");

                HttpResponseMessage notesOnly = await a.PutAsync(path, JsonHelper.ToJsonContent(new { notes = "" })).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.OK, notesOnly, "notes only");
                ModelProfile kept = await JsonHelper.DeserializeAsync<ModelProfile>(notesOnly).ConfigureAwait(false);
                AssertNull(kept.Notes, "empty notes clear");
                AssertEqual("Compact", kept.Rendering.Template, "omitted rendering kept");
                AssertEqual((bool?)true, kept.Capabilities.ToolsAvailable, "omitted capabilities kept");
                AssertStatusCode(HttpStatusCode.BadRequest, await a.PutAsync(path, JsonHelper.ToJsonContent(new { rendering = new { maxFilesPerMission = 0 } })).ConfigureAwait(false), "max files below 1");
                AssertStatusCode(HttpStatusCode.NotFound, await a.PutAsync("/api/v1/model-profiles/mdl_missing", JsonHelper.ToJsonContent(update)).ConfigureAwait(false), "missing profile");

                string compat = path + "/revisions/" + seeded.First.Id + "/compatible-with";
                AssertStatusCode(HttpStatusCode.Forbidden, await u.PutAsync(compat, JsonHelper.ToJsonContent(new { compatibleWith = new[] { seeded.Second.Id } })).ConfigureAwait(false), "a regular user cannot mark");
                HttpResponseMessage marked = await a.PutAsync(compat, JsonHelper.ToJsonContent(new { compatibleWith = new[] { seeded.Second.Id } })).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.OK, marked, "mark compatible");
                ModelProfileRevision markedRevision = await JsonHelper.DeserializeAsync<ModelProfileRevision>(marked).ConfigureAwait(false);
                AssertTrue(markedRevision.CompatibleWith.SequenceEqual(new[] { seeded.Second.Id }), "marked");
                ModelProfile withMarks = await GetAsync<ModelProfile>(u, path).ConfigureAwait(false);
                AssertTrue(withMarks.Revisions[1].CompatibleWith.SequenceEqual(new[] { seeded.First.Id }), "symmetric");
                AssertEqual(seeded.First.RuntimeVersion, withMarks.Revisions[0].RuntimeVersion, "the revision itself is unchanged");
                AssertStatusCode(HttpStatusCode.BadRequest, await a.PutAsync(compat, JsonHelper.ToJsonContent(new { compatibleWith = new[] { "mdv_missing" } })).ConfigureAwait(false), "unknown revision");
                AssertStatusCode(HttpStatusCode.BadRequest, await a.PutAsync(compat, JsonHelper.ToJsonContent(new { })).ConfigureAwait(false), "missing list");
                AssertStatusCode(HttpStatusCode.NotFound, await a.PutAsync(path + "/revisions/" + other.Revision.Id + "/compatible-with", JsonHelper.ToJsonContent(new { compatibleWith = new string[0] })).ConfigureAwait(false), "revision of another profile");
                HttpResponseMessage cleared = await a.PutAsync(compat, JsonHelper.ToJsonContent(new { compatibleWith = new string[0] })).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.OK, cleared, "clear");
                AssertEqual(0, (await JsonHelper.DeserializeAsync<ModelProfileRevision>(cleared).ConfigureAwait(false)).CompatibleWith.Count, "cleared");
            }));

            cases.Add(CaseAsync("mcp_tools", "MCP: get_model_profile and list_model_profiles apply the REST rules with typed errors", async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                E2ETenantUser adminA = await E2ETenantUser.CreateAsync(fx.AuthClient, "mdm-admin-a", true).ConfigureAwait(false);
                E2ETenantUser userA = await E2ETenantUser.CreateAsync(fx.AuthClient, "mdm-user-a", false, adminA.TenantId).ConfigureAwait(false);
                E2ETenantUser adminB = await E2ETenantUser.CreateAsync(fx.AuthClient, "mdm-admin-b", true).ConfigureAwait(false);
                Seeded seeded = await SeedAsync(fx, adminA.TenantId).ConfigureAwait(false);

                using (McpToolClient user = Mcp(fx, userA))
                using (McpToolClient b = Mcp(fx, adminB))
                using (McpToolClient global = new McpToolClient("http://127.0.0.1:" + fx.McpPort + "/mcp", null, new Dictionary<string, string> { ["X-Api-Key"] = fx.ApiKey }))
                {
                    await user.InitializeAsync().ConfigureAwait(false);
                    await b.InitializeAsync().ConfigureAwait(false);
                    await global.InitializeAsync().ConfigureAwait(false);

                    ModelProfile got = await OkAsync<ModelProfile>(user, "get_model_profile", new { id = seeded.Profile.Id }).ConfigureAwait(false);
                    AssertEqual(seeded.Profile.Id, got.Id, "get");
                    AssertEqual(2, got.Revisions.Count, "with revisions");
                    EnumerationResult<ModelProfile> page = await OkAsync<EnumerationResult<ModelProfile>>(user, "list_model_profiles", new { runtime = "MUX", pageSize = 1 }).ConfigureAwait(false);
                    AssertEqual(1L, page.TotalRecords, "list");
                    AssertEqual(1, page.PageSize, "page size honored");
                    EnumerationResult<ModelProfile> defaults = await OkAsync<EnumerationResult<ModelProfile>>(user, "list_model_profiles", new { }).ConfigureAwait(false);
                    AssertEqual(25, defaults.PageSize, "MCP default page size");
                    EnumerationResult<ModelProfile> none = await OkAsync<EnumerationResult<ModelProfile>>(user, "list_model_profiles", new { model = "no-such-model" }).ConfigureAwait(false);
                    AssertEqual(0L, none.TotalRecords, "empty result");

                    await ErrorAsync(b, "get_model_profile", new { id = seeded.Profile.Id }, McpToolErrorCodeEnum.NotFound).ConfigureAwait(false);
                    await ErrorAsync(user, "get_model_profile", new { id = "mdl_missing" }, McpToolErrorCodeEnum.NotFound).ConfigureAwait(false);
                    // The integer schema is enforced before the handler runs, so a bad page number answers plain text.
                    McpToolCallResult badPage = await user.CallToolResultAsync("list_model_profiles", JsonHelper.Serialize(new { pageNumber = "x" })).ConfigureAwait(false);
                    AssertContains("pageNumber must be a JSON integer", badPage.Text);
                    EnumerationResult<ModelProfile> leaked = await OkAsync<EnumerationResult<ModelProfile>>(b, "list_model_profiles", new { runtime = "Mux" }).ConfigureAwait(false);
                    AssertEqual(0L, leaked.TotalRecords, "another tenant lists nothing");
                    AssertEqual(seeded.Profile.Id, (await OkAsync<ModelProfile>(global, "get_model_profile", new { id = seeded.Profile.Id }).ConfigureAwait(false)).Id, "global admin");
                }
            }));

            cases.Add(CaseAsync("scorecard", "REST and MCP: the scorecard of one (profile, persona) with revision groups, exclusions, validation and tenant isolation", async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                E2ETenantUser adminA = await E2ETenantUser.CreateAsync(fx.AuthClient, "mds-admin-a", true).ConfigureAwait(false);
                E2ETenantUser userA = await E2ETenantUser.CreateAsync(fx.AuthClient, "mds-user-a", false, adminA.TenantId).ConfigureAwait(false);
                E2ETenantUser adminB = await E2ETenantUser.CreateAsync(fx.AuthClient, "mds-admin-b", true).ConfigureAwait(false);
                Seeded seeded = await SeedAsync(fx, adminA.TenantId).ConfigureAwait(false);
                using (DatabaseDriver db = await OpenDatabaseAsync(fx).ConfigureAwait(false))
                {
                    foreach (string version in new string[] { "1.4.1", "2.0.0" })
                    {
                        await db.StageOutcomes.CreateAsync(new StageOutcome
                        {
                            TenantId = adminA.TenantId, MissionId = "msn_sc_" + version, CaptainId = seeded.CaptainId, Persona = "Worker",
                            Runtime = AgentRuntimeEnum.Mux, ModelProvider = "Ollama", Model = "qwen3.8:27b", ContextTokens = 131072,
                            RuntimeVersion = version, ModelDigest = "sha256:aaa", Disposition = StageDispositionEnum.Completed,
                            StageCheckStatus = StageCheckStatusEnum.Passed, EndedUtc = DateTime.UtcNow.AddHours(-1)
                        }).ConfigureAwait(false);
                    }
                    await db.StageOutcomes.CreateAsync(new StageOutcome
                    {
                        TenantId = adminA.TenantId, MissionId = "msn_sc_eval", CaptainId = seeded.CaptainId, Persona = "Worker",
                        Runtime = AgentRuntimeEnum.Mux, ModelProvider = "Ollama", Model = "qwen3.8:27b", ContextTokens = 131072,
                        RuntimeVersion = "1.4.1", ModelDigest = "sha256:aaa", Disposition = StageDispositionEnum.Completed,
                        IsEvaluation = true, EndedUtc = DateTime.UtcNow.AddHours(-1)
                    }).ConfigureAwait(false);
                }

                using HttpClient u = userA.CreateClient(fx.BaseUrl);
                using HttpClient b = adminB.CreateClient(fx.BaseUrl);
                string path = "/api/v1/model-profiles/" + seeded.Profile.Id + "/scorecard";
                ModelScorecard card = await GetAsync<ModelScorecard>(u, path + "?persona=Worker").ConfigureAwait(false);
                AssertEqual(seeded.Profile.Id, card.ProfileId, "profile");
                AssertEqual(30, card.WindowDays, "default days");
                AssertEqual(50, card.WindowMissions, "default missions");
                AssertEqual(2, card.Groups.Count, "two revisions shown separately");
                AssertEqual(seeded.First.Id, card.Groups[0].RevisionIds.Single(), "first revision group");
                AssertEqual(1, card.Groups[0].Missions, "1.x mission");
                AssertEqual(1, card.Groups[1].Missions, "2.x mission");
                ModelScorecardMetric firstPass = card.Groups[0].Metrics.Single(m => m.Key == "firstPassSuccess");
                AssertEqual((int?)1, firstPass.Count, "count");
                AssertEqual((int?)1, firstPass.Of, "of");
                AssertEqual((double?)1.0, firstPass.Upper, "Wilson upper at n");
                AssertEqual(1, card.Exclusions.Single(e => e.Rule == "Evaluation").Removed, "evaluation removed");
                AssertFalse(card.Groups[0].Metrics.Single(m => m.Key == "judge.findingCategories").Available, "unavailable metric");

                ModelScorecard narrow = await GetAsync<ModelScorecard>(u, path + "?persona=worker&windowDays=7&windowMissions=1&vesselId=vsl_none").ConfigureAwait(false);
                AssertEqual(7, narrow.WindowDays, "explicit days");
                AssertEqual(1, narrow.WindowMissions, "explicit missions");
                AssertEqual(0, narrow.Missions, "no missions on that vessel");
                AssertStatusCode(HttpStatusCode.BadRequest, await u.GetAsync(path).ConfigureAwait(false), "persona required");
                AssertStatusCode(HttpStatusCode.BadRequest, await u.GetAsync(path + "?persona=Worker&windowDays=0").ConfigureAwait(false), "windowDays range");
                AssertStatusCode(HttpStatusCode.BadRequest, await u.GetAsync(path + "?persona=Worker&windowMissions=x").ConfigureAwait(false), "windowMissions integer");
                AssertStatusCode(HttpStatusCode.NotFound, await b.GetAsync(path + "?persona=Worker").ConfigureAwait(false), "another tenant");
                AssertStatusCode(HttpStatusCode.NotFound, await b.GetAsync(path + "?windowDays=x").ConfigureAwait(false), "another tenant before validation");
                AssertStatusCode(HttpStatusCode.NotFound, await u.GetAsync("/api/v1/model-profiles/mdl_missing/scorecard?persona=Worker").ConfigureAwait(false), "missing profile");

                using (McpToolClient user = Mcp(fx, userA))
                using (McpToolClient other = Mcp(fx, adminB))
                {
                    await user.InitializeAsync().ConfigureAwait(false);
                    await other.InitializeAsync().ConfigureAwait(false);
                    ModelScorecard viaMcp = await OkAsync<ModelScorecard>(user, "get_model_scorecard", new { id = seeded.Profile.Id, persona = "Worker", windowDays = 10 }).ConfigureAwait(false);
                    AssertEqual(10, viaMcp.WindowDays, "MCP window");
                    AssertEqual(2, viaMcp.Groups.Count, "MCP groups");
                    await ErrorAsync(other, "get_model_scorecard", new { id = seeded.Profile.Id, persona = "Worker", windowDays = 0 }, McpToolErrorCodeEnum.NotFound).ConfigureAwait(false);
                    await ErrorAsync(user, "get_model_scorecard", new { id = seeded.Profile.Id, persona = "Worker", windowMissions = 0 }, McpToolErrorCodeEnum.InvalidArgument).ConfigureAwait(false);
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "E2E: model profiles (REST and MCP)",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task<Seeded> SeedAsync(E2EServerFixture fx, string tenantId)
        {
            Seeded seeded = new Seeded();
            using (DatabaseDriver db = await OpenDatabaseAsync(fx).ConfigureAwait(false))
            {
                Captain captain = await db.Captains.CreateAsync(new Captain { Name = "mdl-" + Guid.NewGuid().ToString("N").Substring(0, 8), Runtime = AgentRuntimeEnum.Mux, TenantId = tenantId }).ConfigureAwait(false);
                seeded.CaptainId = captain.Id;
                ModelProfileService service = new ModelProfileService(db, Quiet());
                ModelProfileResolution first = await service.ResolveAsync(Observation(tenantId, captain.Id, "Mux", "Ollama", "1.4.1")).ConfigureAwait(false);
                ModelProfileResolution second = await service.ResolveAsync(Observation(tenantId, captain.Id, "Mux", "Ollama", "2.0.0")).ConfigureAwait(false);
                seeded.Profile = first.Profile;
                seeded.First = first.Revision;
                seeded.Second = second.Revision;
            }

            return seeded;
        }

        private static async Task<ModelProfileResolution> ResolveAsync(E2EServerFixture fx, ModelProfileObservation observation)
        {
            using (DatabaseDriver db = await OpenDatabaseAsync(fx).ConfigureAwait(false))
            {
                return await new ModelProfileService(db, Quiet()).ResolveAsync(observation).ConfigureAwait(false);
            }
        }

        private static ModelProfileObservation Observation(string tenantId, string? captainId, string runtime, string? provider, string version)
        {
            return new ModelProfileObservation
            {
                TenantId = tenantId,
                CaptainId = captainId,
                Runtime = runtime,
                Provider = provider,
                Model = provider != null ? "qwen3.8:27b" : null,
                ServedContextTokens = provider != null ? 131072 : null,
                RuntimeVersion = version,
                ModelDigest = provider != null ? "sha256:aaa" : null
            };
        }

        private static async Task<T> GetAsync<T>(HttpClient c, string path)
        {
            HttpResponseMessage response = await c.GetAsync(path).ConfigureAwait(false);
            AssertStatusCode(HttpStatusCode.OK, response, path);
            return await JsonHelper.DeserializeAsync<T>(response).ConfigureAwait(false);
        }

        private static McpToolClient Mcp(E2EServerFixture fx, E2ETenantUser user)
        {
            Dictionary<string, string> headers = new Dictionary<string, string> { ["Authorization"] = "Bearer " + user.BearerToken };
            return new McpToolClient("http://127.0.0.1:" + fx.McpPort + "/mcp", null, headers);
        }

        private static async Task<T> OkAsync<T>(McpToolClient client, string tool, object args)
        {
            McpToolCallResult result = await client.CallToolResultAsync(tool, JsonHelper.Serialize(args)).ConfigureAwait(false);
            AssertFalse(result.IsError, tool + ": " + result.Text);
            AssertFalse(result.Text.Contains("\"errorCode\"", StringComparison.OrdinalIgnoreCase), tool + " returned an error: " + result.Text);
            return JsonHelper.Deserialize<T>(result.Text);
        }

        private static async Task ErrorAsync(McpToolClient client, string tool, object args, McpToolErrorCodeEnum expected)
        {
            McpToolCallResult result = await client.CallToolResultAsync(tool, JsonHelper.Serialize(args)).ConfigureAwait(false);
            AssertTrue(result.Text.TrimStart().StartsWith("{", StringComparison.Ordinal), tool + " returned no McpToolError: " + result.Text);
            McpToolError error = JsonHelper.Deserialize<McpToolError>(result.Text);
            AssertEqual(expected, error.ErrorCode, tool + ": " + result.Text);
        }

        private static LoggingModule Quiet()
        {
            LoggingModule quiet = new LoggingModule();
            quiet.Settings.EnableConsole = false;
            return quiet;
        }

        private static async Task<DatabaseDriver> OpenDatabaseAsync(E2EServerFixture fx)
        {
            DatabaseSettings settings = new DatabaseSettings { Type = DatabaseTypeEnum.Sqlite, Filename = Path.Combine(fx.TempDir, "armada.db") };
            DatabaseDriver db = DatabaseDriverFactory.Create(settings, Quiet());
            await db.InitializeAsync().ConfigureAwait(false);
            return db;
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { TestTags.EndToEnd });
        }

        #endregion

        #region Private-Classes

        private sealed class Seeded
        {
            public string CaptainId { get; set; } = "";
            public ModelProfile Profile { get; set; } = new ModelProfile();
            public ModelProfileRevision First { get; set; } = new ModelProfileRevision();
            public ModelProfileRevision Second { get; set; } = new ModelProfileRevision();
        }

        #endregion
    }
}

namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Bounded collections for vessels, fleets, docks, and Workspace (docs/ENUMERATION.md, audit Group B): the fleet read
    /// carries a vessel count instead of its vessels, bulk deletes reject more than MaxRequestItems ids with a typed
    /// error on REST and MCP and delete nothing, vessel configuration lists are limited to MaxConfigItems on every
    /// write path, and Workspace active missions are paged from the database with exact totals and tenant scope.
    /// </summary>
    public sealed class VesselFleetBoundsSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "E2E.VesselFleetBounds";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("fleet_read_counts_vessels_and_pages_them", "GET /fleets/{id} and MCP get_fleet carry VesselCount and no vessel list; the vessels page through vessels enumerate with fleetId without gaps", async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                Fleet fleet = await CreateFleetAsync(fx, "bounds-fleet").ConfigureAwait(false);
                List<string> created = new List<string>();
                for (int i = 0; i < 5; i++) created.Add((await CreateVesselAsync(fx, "bounds-v" + i, fleet.Id, null).ConfigureAwait(false)).Id);

                using (HttpResponseMessage response = await fx.AuthClient.GetAsync("/api/v1/fleets/" + fleet.Id).ConfigureAwait(false))
                {
                    string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.OK, response.StatusCode, json);
                    FleetDetailResponse detail = JsonHelper.Deserialize<FleetDetailResponse>(json);
                    AssertEqual(fleet.Id, detail.Fleet?.Id, "fleet");
                    AssertEqual(5L, detail.VesselCount, "vessel count");
                    AssertFalse(json.Contains("\"Vessels\"", StringComparison.OrdinalIgnoreCase), "no embedded vessel list: " + json);
                }

                List<string> seen = new List<string>();
                for (int page = 1; page <= 3; page++)
                {
                    using HttpResponseMessage pageResponse = await fx.AuthClient.PostAsync("/api/v1/vessels/enumerate", JsonHelper.ToJsonContent(new { FleetId = fleet.Id, PageNumber = page, PageSize = 2 })).ConfigureAwait(false);
                    EnumerationResult<Vessel> result = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(pageResponse).ConfigureAwait(false);
                    AssertEqual(5L, result.TotalRecords, "stable total");
                    AssertEqual(page < 3 ? 2 : 1, result.Objects.Count, "rows on page " + page);
                    seen.AddRange(result.Objects.Select(v => v.Id));
                }

                AssertEqual(5, seen.Distinct().Count(), "every vessel once: " + String.Join(",", seen));
                AssertTrue(created.All(seen.Contains), "the fleet's vessels");

                using HttpClient mcp = McpClient(fx);
                string rpc = await McpTestCalls.CallToolAsync(mcp, "get_fleet", new { fleetId = fleet.Id }).ConfigureAwait(false);
                string text = JsonHelper.Deserialize<E2eMcpToolEnvelope>(rpc).Result?.Content.FirstOrDefault()?.Text ?? "";
                FleetDetailResponse mcpDetail = JsonHelper.Deserialize<FleetDetailResponse>(text);
                AssertEqual(5L, mcpDetail.VesselCount, "MCP vessel count");
                AssertFalse(text.Contains("\"vessels\"", StringComparison.OrdinalIgnoreCase), "MCP get_fleet embeds no vessel list: " + text);
            }));

            cases.Add(Case("fleet_vessel_count_is_tenant_scoped", "A tenant admin's fleet count covers only that tenant's vessels, and another tenant cannot read the fleet", async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                E2ETenantUser tenantA = await E2ETenantUser.CreateAsync(fx.AuthClient, "bndfleeta", true).ConfigureAwait(false);
                E2ETenantUser tenantB = await E2ETenantUser.CreateAsync(fx.AuthClient, "bndfleetb", true).ConfigureAwait(false);
                using HttpClient a = tenantA.CreateClient(fx.BaseUrl);
                using HttpClient b = tenantB.CreateClient(fx.BaseUrl);
                using HttpResponseMessage createdFleet = await a.PostAsync("/api/v1/fleets", JsonHelper.ToJsonContent(new { Name = "bnd-a-" + Guid.NewGuid().ToString("N").Substring(0, 6) })).ConfigureAwait(false);
                Fleet fleet = await JsonHelper.DeserializeAsync<Fleet>(createdFleet).ConfigureAwait(false);
                for (int i = 0; i < 3; i++)
                {
                    string name = "bnd-a-v" + i + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
                    using HttpResponseMessage v = await a.PostAsync("/api/v1/vessels", JsonHelper.ToJsonContent(new { Name = name, RepoUrl = "https://github.com/test/" + name, FleetId = fleet.Id })).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.Created, v.StatusCode, await v.Content.ReadAsStringAsync().ConfigureAwait(false));
                }

                using (HttpResponseMessage read = await a.GetAsync("/api/v1/fleets/" + fleet.Id).ConfigureAwait(false))
                {
                    FleetDetailResponse detail = await JsonHelper.DeserializeAsync<FleetDetailResponse>(read).ConfigureAwait(false);
                    AssertEqual(3L, detail.VesselCount, "tenant A counts its three vessels");
                }

                using HttpResponseMessage foreign = await b.GetAsync("/api/v1/fleets/" + fleet.Id).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NotFound, foreign.StatusCode, "tenant B cannot read tenant A's fleet");
            }));

            cases.Add(Case("bulk_deletes_reject_over_limit", "Vessel, fleet, and dock bulk deletes with one id over MaxRequestItems are a REST 400 and an MCP InvalidArgument with Code CollectionLimitExceeded, and delete nothing", async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                Fleet fleet = await CreateFleetAsync(fx, "bounds-del-fleet").ConfigureAwait(false);
                Vessel vessel = await CreateVesselAsync(fx, "bounds-del-vessel", null, null).ConfigureAwait(false);
                Dock dock;
                using (DatabaseDriver db = await OpenDatabaseAsync(fx).ConfigureAwait(false))
                {
                    dock = await db.Docks.CreateAsync(new Dock(vessel.Id) { TenantId = vessel.TenantId, UserId = vessel.UserId, BranchName = "armada/bounds/dock" }).ConfigureAwait(false);
                }

                Dictionary<string, string> targets = new Dictionary<string, string>
                {
                    ["vessels"] = vessel.Id,
                    ["fleets"] = fleet.Id,
                    ["docks"] = dock.Id
                };
                using HttpClient mcp = McpClient(fx);
                foreach (KeyValuePair<string, string> target in targets)
                {
                    List<string> ids = Enumerable.Range(0, CollectionLimits.MaxRequestItems).Select(i => target.Key + "_none" + i).ToList();
                    ids.Add(target.Value);

                    using (HttpResponseMessage response = await fx.AuthClient.PostAsync("/api/v1/" + target.Key + "/delete/multiple", JsonHelper.ToJsonContent(new { Ids = ids })).ConfigureAwait(false))
                    {
                        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        AssertEqual(HttpStatusCode.BadRequest, response.StatusCode, target.Key + ": " + body);
                        E2eLimitErrorBody error = JsonHelper.Deserialize<E2eLimitErrorBody>(body);
                        AssertEqual(CollectionLimitExceededException.ErrorCode, error.Data?.Code, body);
                        AssertEqual(CollectionLimits.MaxRequestItems, error.Data!.Limit, target.Key + " limit");
                        AssertEqual((long)ids.Count, error.Data.Actual, target.Key + " actual");
                    }

                    string rpc = await McpTestCalls.CallToolAsync(mcp, "delete_" + target.Key, new { ids = ids }).ConfigureAwait(false);
                    string text = JsonHelper.Deserialize<E2eMcpToolEnvelope>(rpc).Result?.Content.FirstOrDefault()?.Text ?? "";
                    McpToolError mcpError = JsonHelper.Deserialize<McpToolError>(text);
                    AssertEqual(McpToolErrorCodeEnum.InvalidArgument, mcpError.ErrorCode, "delete_" + target.Key + ": " + text);
                    AssertEqual(CollectionLimitExceededException.ErrorCode, mcpError.Code, text);
                }

                using (HttpResponseMessage stillVessel = await fx.AuthClient.GetAsync("/api/v1/vessels/" + vessel.Id).ConfigureAwait(false)) AssertEqual(HttpStatusCode.OK, stillVessel.StatusCode, "vessel kept");
                using (HttpResponseMessage stillFleet = await fx.AuthClient.GetAsync("/api/v1/fleets/" + fleet.Id).ConfigureAwait(false)) AssertEqual(HttpStatusCode.OK, stillFleet.StatusCode, "fleet kept");
                using (HttpResponseMessage stillDock = await fx.AuthClient.GetAsync("/api/v1/docks/" + dock.Id).ConfigureAwait(false)) AssertEqual(HttpStatusCode.OK, stillDock.StatusCode, "dock kept");

                using (HttpResponseMessage atLimit = await fx.AuthClient.PostAsync("/api/v1/fleets/delete/multiple", JsonHelper.ToJsonContent(new { Ids = Enumerable.Range(0, CollectionLimits.MaxRequestItems).Select(i => "flt_none" + i).ToList() })).ConfigureAwait(false))
                {
                    DeleteMultipleResult result = await JsonHelper.DeserializeAsync<DeleteMultipleResult>(atLimit).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.OK, atLimit.StatusCode, "exactly the limit is accepted");
                    AssertEqual(CollectionLimits.MaxRequestItems, result.Skipped.Count, "Skipped is bounded by the request");
                }
            }));

            cases.Add(Case("vessel_config_lists_limited_on_every_write_path", "A vessel configuration list over MaxConfigItems is rejected (CollectionLimitExceeded) on REST create and update and MCP update_vessel (the database layer validates every write path); exactly the limit is accepted", async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                List<string> over = Enumerable.Range(0, CollectionLimits.MaxConfigItems + 1).Select(i => "release/" + i).ToList();
                List<string> atLimit = Enumerable.Range(0, CollectionLimits.MaxConfigItems).Select(i => "release/" + i).ToList();
                string name = "bounds-cfg-" + Guid.NewGuid().ToString("N").Substring(0, 6);

                using (HttpResponseMessage create = await fx.AuthClient.PostAsync("/api/v1/vessels", JsonHelper.ToJsonContent(new { Name = name, RepoUrl = "https://github.com/test/" + name, ProtectedBranchPatterns = over })).ConfigureAwait(false))
                {
                    await AssertLimitAsync(create, "ProtectedBranchPatterns", "REST create").ConfigureAwait(false);
                }

                Vessel vessel = await CreateVesselAsync(fx, name, null, atLimit).ConfigureAwait(false);
                AssertEqual(CollectionLimits.MaxConfigItems, vessel.ProtectedBranchPatterns.Count, "exactly the limit is stored");

                foreach (string field in new[] { "ProtectedPathPatterns", "PrivateIdentifierDenylist", "AutoLandPathAllowGlobs", "AutoLandPathDenyGlobs" })
                {
                    Dictionary<string, object> body = new Dictionary<string, object> { ["Name"] = vessel.Name, ["RepoUrl"] = vessel.RepoUrl ?? "", [field] = over };
                    using HttpResponseMessage update = await fx.AuthClient.PutAsync("/api/v1/vessels/" + vessel.Id, JsonHelper.ToJsonContent(body)).ConfigureAwait(false);
                    await AssertLimitAsync(update, field, "REST update " + field).ConfigureAwait(false);
                }

                using HttpClient mcp = McpClient(fx);
                string mcpName = name + "-mcp";
                Fleet mcpFleet = await CreateFleetAsync(fx, "bounds-cfg-fleet").ConfigureAwait(false);
                string addRpc = await McpTestCalls.CallToolAsync(mcp, "add_vessel", new { name = mcpName, repoUrl = "https://github.com/test/" + mcpName, fleetId = mcpFleet.Id, protectedBranchPatterns = over }).ConfigureAwait(false);
                string addText = JsonHelper.Deserialize<E2eMcpToolEnvelope>(addRpc).Result?.Content.FirstOrDefault()?.Text ?? "";
                AssertTrue(addText.TrimStart().StartsWith("{", StringComparison.Ordinal), "add_vessel answered JSON: " + addText);
                if (addText.Contains("ErrorCode", StringComparison.OrdinalIgnoreCase))
                {
                    McpToolError addError = JsonHelper.Deserialize<McpToolError>(addText);
                    AssertEqual(McpToolErrorCodeEnum.InvalidArgument, addError.ErrorCode, addText);
                    AssertEqual(CollectionLimitExceededException.ErrorCode, addError.Code, addText);
                }
                else
                {
                    // add_vessel does not take the list: the created vessel has the default (empty) list.
                    Vessel added = JsonHelper.Deserialize<Vessel>(addText);
                    AssertTrue(added.ProtectedBranchPatterns.Count <= CollectionLimits.MaxConfigItems, "stored list within the limit");
                }

                string updateRpc = await McpTestCalls.CallToolAsync(mcp, "update_vessel", new { vesselId = vessel.Id, autoLandPathAllowGlobs = over }).ConfigureAwait(false);
                string updateText = JsonHelper.Deserialize<E2eMcpToolEnvelope>(updateRpc).Result?.Content.FirstOrDefault()?.Text ?? "";
                AssertTrue(updateText.TrimStart().StartsWith("{", StringComparison.Ordinal), "update_vessel answered JSON: " + updateText);
                McpToolError updateError = JsonHelper.Deserialize<McpToolError>(updateText);
                AssertEqual(McpToolErrorCodeEnum.InvalidArgument, updateError.ErrorCode, "MCP update_vessel: " + updateText);
                AssertEqual(CollectionLimitExceededException.ErrorCode, updateError.Code, updateText);

                using HttpResponseMessage reread = await fx.AuthClient.GetAsync("/api/v1/vessels/" + vessel.Id).ConfigureAwait(false);
                Vessel stored = await JsonHelper.DeserializeAsync<Vessel>(reread).ConfigureAwait(false);
                AssertEqual(0, stored.ProtectedPathPatterns.Count, "rejected updates stored nothing");
                AssertEqual(0, stored.AutoLandPathAllowGlobs.Count, "the rejected MCP update stored nothing");
                AssertEqual(CollectionLimits.MaxConfigItems, stored.ProtectedBranchPatterns.Count, "the accepted list is unchanged");
            }));

            cases.Add(Case("workspace_active_missions_page_from_the_database", "Workspace status carries ActiveMissionCount only; active missions page newest first across statuses without gaps, with scoped files, exact totals, and tenant scope", async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                Vessel vessel = await CreateVesselAsync(fx, "bounds-ws", null, null).ConfigureAwait(false);
                MissionStatusEnum[] statuses = new[] { MissionStatusEnum.Pending, MissionStatusEnum.InProgress, MissionStatusEnum.Review, MissionStatusEnum.Testing, MissionStatusEnum.Assigned, MissionStatusEnum.Pending, MissionStatusEnum.Complete };
                List<string> active = new List<string>();
                using (DatabaseDriver db = await OpenDatabaseAsync(fx).ConfigureAwait(false))
                {
                    for (int i = 0; i < statuses.Length; i++)
                    {
                        Mission mission = new Mission("ws mission " + i, "Touch only src/File" + i + ".cs")
                        {
                            VesselId = vessel.Id,
                            TenantId = vessel.TenantId,
                            UserId = vessel.UserId,
                            Status = statuses[i],
                            CreatedUtc = DateTime.UtcNow.AddMinutes(-60 + i)
                        };
                        mission = await db.Missions.CreateAsync(mission).ConfigureAwait(false);
                        if (statuses[i] != MissionStatusEnum.Complete) active.Add(mission.Id);
                    }
                }

                active.Reverse();
                string basePath = "/api/v1/workspace/vessels/" + vessel.Id + "/active-missions";
                List<string> seen = new List<string>();
                for (int page = 1; page <= 3; page++)
                {
                    using HttpResponseMessage response = await fx.AuthClient.GetAsync(basePath + "?pageNumber=" + page + "&pageSize=2").ConfigureAwait(false);
                    EnumerationResult<WorkspaceActiveMission> result = await JsonHelper.DeserializeAsync<EnumerationResult<WorkspaceActiveMission>>(response).ConfigureAwait(false);
                    AssertEqual(6L, result.TotalRecords, "six active missions (the Complete one is not counted)");
                    AssertEqual(3, result.TotalPages, "pages of two");
                    AssertEqual(2, result.Objects.Count, "rows on page " + page);
                    AssertTrue(result.Objects.All(m => m.ScopedFiles.Count == 1 && m.ScopedFileCount == 1), "scoped files parsed");
                    seen.AddRange(result.Objects.Select(m => m.MissionId));
                }

                AssertEqual(String.Join(",", active), String.Join(",", seen), "newest first across statuses without gaps or duplicates");

                using (HttpResponseMessage past = await fx.AuthClient.PostAsync(basePath + "/enumerate", JsonHelper.ToJsonContent(new { pageNumber = 9, pageSize = 2 })).ConfigureAwait(false))
                {
                    EnumerationResult<WorkspaceActiveMission> empty = await JsonHelper.DeserializeAsync<EnumerationResult<WorkspaceActiveMission>>(past).ConfigureAwait(false);
                    AssertEqual(0, empty.Objects.Count, "past the end is empty");
                    AssertEqual(6L, empty.TotalRecords, "with the total");
                }

                using (HttpResponseMessage status = await fx.AuthClient.GetAsync("/api/v1/workspace/vessels/" + vessel.Id + "/status").ConfigureAwait(false))
                {
                    string json = await status.Content.ReadAsStringAsync().ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.OK, status.StatusCode, json);
                    WorkspaceStatusResult statusResult = JsonHelper.Deserialize<WorkspaceStatusResult>(json);
                    AssertEqual(6, statusResult.ActiveMissionCount, "status counts the active missions");
                    AssertFalse(json.Contains("\"ActiveMissions\"", StringComparison.OrdinalIgnoreCase), "status embeds no mission list: " + json);
                }

                using (StringContent bad = new StringContent("{nope", System.Text.Encoding.UTF8, "application/json"))
                using (HttpResponseMessage malformed = await fx.AuthClient.PostAsync(basePath + "/enumerate", bad).ConfigureAwait(false))
                {
                    AssertEqual(HttpStatusCode.BadRequest, malformed.StatusCode, "malformed body");
                }

                E2ETenantUser other = await E2ETenantUser.CreateAsync(fx.AuthClient, "bndws", true).ConfigureAwait(false);
                using HttpClient otherClient = other.CreateClient(fx.BaseUrl);
                using HttpResponseMessage foreign = await otherClient.GetAsync(basePath).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NotFound, foreign.StatusCode, "another tenant cannot page this vessel's missions");
            }));

            return new TestSuiteDescriptor(SuiteId, "Vessel, fleet, dock, and Workspace collections are bounded", cases);
        }

        #endregion

        #region Private-Methods

        private static async Task AssertLimitAsync(HttpResponseMessage response, string field, string label)
        {
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            AssertEqual(HttpStatusCode.BadRequest, response.StatusCode, label + ": " + body);
            E2eLimitErrorBody error;
            try { error = JsonHelper.Deserialize<E2eLimitErrorBody>(body); }
            catch (System.Text.Json.JsonException ex) { throw new InvalidOperationException(label + ": the error body is not JSON: " + body, ex); }
            AssertEqual(CollectionLimitExceededException.ErrorCode, error.Data?.Code, label + ": " + body);
            AssertEqual(CollectionLimits.MaxConfigItems, error.Data!.Limit, label + " limit");
            AssertContains(field, error.Data.Field ?? "", label + " field");
        }

        private static async Task<Fleet> CreateFleetAsync(E2EServerFixture fx, string prefix)
        {
            string name = prefix + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            using HttpResponseMessage response = await fx.AuthClient.PostAsync("/api/v1/fleets", JsonHelper.ToJsonContent(new { Name = name })).ConfigureAwait(false);
            AssertEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            return await JsonHelper.DeserializeAsync<Fleet>(response).ConfigureAwait(false);
        }

        private static async Task<Vessel> CreateVesselAsync(E2EServerFixture fx, string prefix, string? fleetId, List<string>? protectedBranchPatterns)
        {
            string name = prefix + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            Dictionary<string, object?> body = new Dictionary<string, object?> { ["Name"] = name, ["RepoUrl"] = "https://github.com/test/" + name, ["FleetId"] = fleetId };
            if (protectedBranchPatterns != null) body["ProtectedBranchPatterns"] = protectedBranchPatterns;
            using HttpResponseMessage response = await fx.AuthClient.PostAsync("/api/v1/vessels", JsonHelper.ToJsonContent(body)).ConfigureAwait(false);
            AssertEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            return await JsonHelper.DeserializeAsync<Vessel>(response).ConfigureAwait(false);
        }

        private static HttpClient McpClient(E2EServerFixture fx)
        {
            HttpClient mcp = new HttpClient();
            mcp.BaseAddress = fx.McpClient.BaseAddress;
            mcp.DefaultRequestHeaders.Add("X-Api-Key", fx.ApiKey);
            return mcp;
        }

        private static async Task<DatabaseDriver> OpenDatabaseAsync(E2EServerFixture fx)
        {
            LoggingModule quiet = new LoggingModule();
            quiet.Settings.EnableConsole = false;
            DatabaseSettings settings = new DatabaseSettings { Type = DatabaseTypeEnum.Sqlite, Filename = Path.Combine(fx.TempDir, "armada.db") };
            DatabaseDriver db = DatabaseDriverFactory.Create(settings, quiet);
            await db.InitializeAsync().ConfigureAwait(false);
            return db;
        }

        private TestCaseDescriptor Case(string caseId, string displayName, Func<Task> body)
        {
            return new TestCaseDescriptor(SuiteId, caseId, displayName, async ct => await body().ConfigureAwait(false));
        }

        #endregion
    }
}

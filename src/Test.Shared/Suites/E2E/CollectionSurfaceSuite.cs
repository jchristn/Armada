namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.ApiSurface;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Test.Shared.Infrastructure.ApiSurface;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// The durable guard against get-all APIs (DEPRECATION.md, Bounded, not retired): no live REST route or MCP tool
    /// returns an unpaged collection unless it is allowlisted here with the reason it is not a stored collection; every
    /// paged GET list has a POST .../enumerate form; every retired whole-list route is unregistered and answers 410 Gone
    /// naming its replacement; and the paged routes bound, normalize, and traverse pages consistently with tenant scope
    /// and REST/MCP parity.
    /// </summary>
    public sealed class CollectionSurfaceSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "E2E.CollectionSurface";

        // Routes whose 200 response is a bare list, with why the list is not a stored collection.
        private static readonly Dictionary<string, string> _UnpagedListRoutes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["GET /api/v1/ask/quick-actions"] = "Fixed catalog defined in code",
        };

        // GET routes shaped like a collection (no trailing path parameter) that are not paged, with why they are a
        // single object, a summary, or a bounded read rather than a collection of stored rows.
        private static readonly Dictionary<string, string> _SingleObjectReads = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["GET /openapi.json"] = "API document",
            ["GET /swagger"] = "API document viewer",
            ["GET /api/v1/ask/quick-actions"] = "Fixed catalog defined in code",
            ["GET /api/v1/whoami"] = "The caller",
            ["GET /api/v1/storage"] = "Storage snapshot with host totals; host reports page through GET /api/v1/storage/hosts",
            ["GET /api/v1/backup"] = "Backup archive download",
            ["GET /api/v1/captains/{id}/tools"] = "One captain's tool sources (at most 256) and counts; its tools are one EnumerationResult page (POST .../tools/enumerate twin)",
            ["GET /api/v1/captains/{id}/log"] = "Log read bounded by lines (1-5000) with offset paging",
            ["GET /api/v1/harbors/{id}/metrics"] = "One Harbor's metrics",
            ["GET /api/v1/missions/history"] = "Aggregated summary buckets",
            ["GET /api/v1/missions/{id}/github/pull-request"] = "One pull request",
            ["GET /api/v1/missions/{id}/landing-preview"] = "One preview",
            ["GET /api/v1/missions/{id}/blackbox"] = "The mission's current black box entry (its record is bounded by construction)",
            ["GET /api/v1/missions/{id}/blackbox/brief"] = "One Markdown brief (at most 40 KB)",
            ["GET /api/v1/voyages/{id}/blackbox"] = "The voyage's current black box entry",
            ["GET /api/v1/voyages/{id}/blackbox/brief"] = "One Markdown brief (at most 40 KB)",
            ["GET /api/v1/blackbox/{id}/brief"] = "One Markdown brief (at most 40 KB)",
            ["GET /api/v1/blackbox/{id}/download"] = "One entry's zip (at most 128 objects of bounded size)",
            ["GET /api/v1/blackbox/retention"] = "Retention status snapshot (counts and the last report)",
            ["GET /api/v1/blackbox/storage"] = "Storage status snapshot (at most one usage row per target)",
            ["GET /api/v1/blackbox/fingerprints/{fingerprint}/trend"] = "One failure mode's counts per Armada version (at most 100 versions)",
            ["GET /api/v1/missions/{id}/evaluate-autoland"] = "One decision",
            ["GET /api/v1/missions/{id}/diff"] = "One mission's diff",
            ["GET /api/v1/missions/{id}/log"] = "Log read bounded by lines (1-5000) with offset paging",
            ["GET /api/v1/missions/{id}/instructions"] = "One instructions file",
            ["GET /api/v1/request-history/summary"] = "Aggregated summary buckets",
            ["GET /api/v1/status"] = "Admiral status snapshot",
            ["GET /api/v1/status/health"] = "Health probe",
            ["GET /api/v1/doctor"] = "Diagnostics report",
            ["GET /api/v1/server/rebuild/status"] = "One rebuild status",
            ["GET /api/v1/settings"] = "Settings object",
            ["GET /api/v1/token-usage/summary"] = "Aggregated summary buckets",
            ["GET /api/v1/vessel-health/summary"] = "Aggregated summary",
            ["GET /api/v1/vessels/import/browse"] = "One page of one directory listing (VesselBrowseResult carries PageNumber, PageSize, TotalPages, TotalRecords)",
            ["GET /api/v1/vessels/import/categorization/default-prompt"] = "One prompt",
            ["GET /api/v1/vessels/{id}/cleanup"] = "One vessel's cleanup inventory summary (counts); resources page through GET .../cleanup/resources",
            ["GET /api/v1/vessels/{id}/git-status"] = "One working tree status",
            ["GET /api/v1/voyages/{id}/blackbox/cascade"] = "One voyage's cascade graph, fixed at 200 nodes and 1000 edges with MissionCount and Truncated; the missions page through POST /api/v1/missions/enumerate",
            ["GET /api/v1/vessels/{id}/health"] = "One vessel's health with DependencyCount; dependencies page through GET .../health/dependencies",
            ["GET /api/v1/vessels/{id}/history/activity"] = "Aggregated commit activity buckets",
            ["GET /api/v1/vessels/{id}/history/commits"] = "Paged commit history (VesselCommitPage)",
            ["GET /api/v1/vessels/{id}/readiness"] = "One readiness report",
            ["GET /api/v1/vessels/{id}/landing-preview"] = "One preview",
            ["GET /api/v1/workspace/vessels/{vesselId}/tree"] = "One page of one directory of one working tree (WorkspaceTreeResult carries PageNumber, PageSize, TotalPages, TotalRecords)",
            ["GET /api/v1/workspace/vessels/{vesselId}/diff"] = "One diff",
            ["GET /api/v1/workspace/vessels/{vesselId}/file"] = "One file",
            ["GET /api/v1/workspace/vessels/{vesselId}/search"] = "Search bounded by maxResults (1-1000, default 200)",
            ["GET /api/v1/workspace/vessels/{vesselId}/changes"] = "One page of one working tree's changes (WorkspaceChangesResult carries PageNumber, PageSize, TotalPages, TotalRecords)",
            ["GET /api/v1/workspace/vessels/{vesselId}/status"] = "One working tree status with ActiveMissionCount; missions page through GET .../active-missions",
        };

        // MCP tools named like a list that do not take paging arguments, with why.
        private static readonly Dictionary<string, string> _UnpagedListTools = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["list_ask_quick_actions"] = "Fixed catalog defined in code",
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("no_rest_route_returns_an_unpaged_list", "No live REST route answers 200 with a bare list unless it is allowlisted with a reason", async () =>
            {
                List<ApiRestRoute> routes = await LiveRestAsync().ConfigureAwait(false);
                StringBuilder problems = new StringBuilder();
                foreach (ApiRestRoute route in routes)
                {
                    string key = route.Method.ToUpperInvariant() + " " + route.Route;
                    string response = route.Responses.TryGetValue("200", out string? ok) ? ok ?? "" : "";
                    bool list = response.StartsWith("List<", StringComparison.Ordinal) || response.StartsWith("IEnumerable<", StringComparison.Ordinal) || response.EndsWith("[]", StringComparison.Ordinal);
                    if (list && !_UnpagedListRoutes.ContainsKey(key)) problems.Append(key).Append(" returns ").Append(response).Append('\n');
                }

                AssertTrue(problems.Length == 0, "unpaged list routes (page them, or allowlist with a reason in CollectionSurfaceSuite):\n" + problems);
            }));

            cases.Add(Case("collection_shaped_get_routes_are_paged", "Every GET route without a trailing path parameter returns an EnumerationResult page or is an allowlisted single-object read", async () =>
            {
                List<ApiRestRoute> routes = await LiveRestAsync().ConfigureAwait(false);
                StringBuilder problems = new StringBuilder();
                foreach (ApiRestRoute route in routes.Where(r => String.Equals(r.Method, "GET", StringComparison.OrdinalIgnoreCase)))
                {
                    string last = route.Route.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";
                    if (last.StartsWith("{", StringComparison.Ordinal)) continue;
                    string key = "GET " + route.Route;
                    string response = route.Responses.TryGetValue("200", out string? ok) ? ok ?? "" : "";
                    if (IsPageResponse(response)) continue;
                    if (route.Route == "/api/v1/jobs") continue;
                    if (!_SingleObjectReads.ContainsKey(key)) problems.Append(key).Append(" -> ").Append(response.Length == 0 ? "(undeclared)" : response).Append('\n');
                }

                AssertTrue(problems.Length == 0, "collection-shaped GET routes that are not paged (return EnumerationResult<T>, or allowlist with a reason):\n" + problems);
            }));

            cases.Add(Case("paged_get_lists_have_post_enumerate", "Every paged GET list has a POST .../enumerate form", async () =>
            {
                List<ApiRestRoute> routes = await LiveRestAsync().ConfigureAwait(false);
                HashSet<string> keys = new HashSet<string>(routes.Select(r => r.Method.ToUpperInvariant() + " " + r.Route), StringComparer.OrdinalIgnoreCase);
                StringBuilder problems = new StringBuilder();
                foreach (ApiRestRoute route in routes.Where(r => String.Equals(r.Method, "GET", StringComparison.OrdinalIgnoreCase)))
                {
                    string response = route.Responses.TryGetValue("200", out string? ok) ? ok ?? "" : "";
                    bool paged = IsPageResponse(response) || route.Route == "/api/v1/jobs";
                    if (!paged) continue;
                    if (!keys.Contains("POST " + route.Route + "/enumerate")) problems.Append("GET ").Append(route.Route).Append(" has no POST ").Append(route.Route).Append("/enumerate\n");
                }

                AssertTrue(problems.Length == 0, "paged GET lists without a POST enumerate form:\n" + problems);
            }));

            cases.Add(Case("retired_routes_answer_gone", "Every retired whole-list route is unregistered, answers 410 Gone naming its replacement, and the replacement is registered", async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                List<ApiRestRoute> routes = ApiSurfaceBuilder.Build(fx.Server).Rest;
                HashSet<string> keys = new HashSet<string>(routes.Select(r => r.Method.ToUpperInvariant() + " " + r.Route), StringComparer.OrdinalIgnoreCase);
                AssertTrue(RetiredSurface.Routes().Count >= 13, "the retired list is populated");
                foreach (string entry in RetiredSurface.Routes())
                {
                    AssertFalse(keys.Contains(entry), entry + " is no longer registered");
                    string[] parts = entry.Split(' ');
                    string replacement = RetiredSurface.RouteReplacement(parts[0], parts[1])!;
                    string[] replacementParts = replacement.Split(' ');
                    AssertTrue(keys.Contains(replacementParts[0] + " " + replacementParts[1]), entry + ": replacement " + replacementParts[0] + " " + replacementParts[1] + " is registered");

                    string path = String.Join("/", parts[1].Split('/').Select(s => s.StartsWith("{", StringComparison.Ordinal) ? "x_retired1" : s));
                    foreach (HttpClient client in new[] { fx.AuthClient, fx.UnauthClient })
                    {
                        using HttpResponseMessage response = await client.GetAsync(path + "?pageSize=5").ConfigureAwait(false);
                        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        AssertEqual(HttpStatusCode.Gone, response.StatusCode, entry + ": " + body);
                        AssertContains(replacementParts[1], body, entry + " names its replacement");
                    }
                }
            }));

            cases.Add(Case("mcp_list_tools_page_and_retired_tools_are_gone", "Every list_ or search_ MCP tool takes paging arguments unless allowlisted, and no retired tool is registered", async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                List<ApiMcpTool> tools = ApiSurfaceBuilder.Build(fx.Server).Mcp;
                StringBuilder problems = new StringBuilder();
                foreach (ApiMcpTool tool in tools.Where(t => t.Name.StartsWith("list_", StringComparison.Ordinal) || t.Name.StartsWith("search_", StringComparison.Ordinal) || t.Name == "enumerate"))
                {
                    if (_UnpagedListTools.ContainsKey(tool.Name)) continue;
                    if (!tool.Arguments.Any(a => a.Name == "pageSize")) problems.Append(tool.Name).Append(" has no pageSize argument\n");
                }

                foreach (string retired in RetiredSurface.Tools())
                {
                    if (tools.Any(t => t.Name == retired)) problems.Append(retired).Append(" is retired but still registered\n");
                }

                AssertTrue(problems.Length == 0, "MCP collection tools:\n" + problems);
            }));

            cases.Add(Case("users_enumerate_bounds_and_traverses_pages", "POST /users/enumerate pages a tenant's users without gaps or duplicates, normalizes invalid paging, rejects a malformed body, matches GET, and stays in the caller's tenant", async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                E2ETenantUser tenantA = await E2ETenantUser.CreateAsync(fx.AuthClient, "pagea", true).ConfigureAwait(false);
                for (int i = 0; i < 4; i++) await E2ETenantUser.CreateAsync(fx.AuthClient, "pagea" + i, false, tenantA.TenantId).ConfigureAwait(false);
                E2ETenantUser tenantB = await E2ETenantUser.CreateAsync(fx.AuthClient, "pageb", true).ConfigureAwait(false);
                await E2ETenantUser.CreateAsync(fx.AuthClient, "pageb0", false, tenantB.TenantId).ConfigureAwait(false);

                using HttpClient a = tenantA.CreateClient(fx.BaseUrl);
                EnumerationResult<UserMaster> first = await PostPageAsync<UserMaster>(a, "/api/v1/users/enumerate", new { PageNumber = 1, PageSize = 2 }).ConfigureAwait(false);
                long total = first.TotalRecords;
                AssertTrue(total >= 5, "tenant A has the five created users (plus any seeded admin): " + total);
                int pages = (int)Math.Ceiling(total / 2.0);
                AssertEqual(pages, first.TotalPages, "pages of two");
                List<string> seen = new List<string>();
                for (int page = 1; page <= pages; page++)
                {
                    EnumerationResult<UserMaster> result = await PostPageAsync<UserMaster>(a, "/api/v1/users/enumerate", new { PageNumber = page, PageSize = 2 }).ConfigureAwait(false);
                    AssertEqual(total, result.TotalRecords, "stable total on page " + page);
                    AssertEqual(page < pages ? 2 : (int)(total - 2 * (pages - 1)), result.Objects.Count, "rows on page " + page);
                    AssertTrue(result.Objects.All(u => u.TenantId == tenantA.TenantId), "only tenant A users");
                    seen.AddRange(result.Objects.Select(u => u.Id));
                }

                AssertEqual((int)total, seen.Distinct().Count(), "every user once across the pages: " + String.Join(",", seen));
                AssertEqual((int)total, seen.Count, "no duplicates across the pages");
                AssertTrue(seen.Contains(tenantA.UserId), "the caller is on one of the pages");

                EnumerationResult<UserMaster> past = await PostPageAsync<UserMaster>(a, "/api/v1/users/enumerate", new { PageNumber = 9, PageSize = 2 }).ConfigureAwait(false);
                AssertEqual(0, past.Objects.Count, "a page past the end is empty");
                AssertEqual(total, past.TotalRecords, "and keeps the total");

                EnumerationResult<UserMaster> defaults = await PostPageAsync<UserMaster>(a, "/api/v1/users/enumerate", null).ConfigureAwait(false);
                AssertEqual(1, defaults.PageNumber, "empty body is page 1");
                AssertEqual(10, defaults.PageSize, "empty body uses the bounded default page size");

                EnumerationResult<UserMaster> clamped = await PostPageAsync<UserMaster>(a, "/api/v1/users/enumerate", new { PageNumber = -4, PageSize = 0 }).ConfigureAwait(false);
                AssertEqual(1, clamped.PageNumber, "negative page number becomes 1");
                AssertEqual(1, clamped.PageSize, "page size 0 becomes 1, never 'all'");
                AssertEqual(1, clamped.Objects.Count, "one row for a page of one");
                EnumerationResult<UserMaster> capped = await PostPageAsync<UserMaster>(a, "/api/v1/users/enumerate", new { PageSize = 1000000 }).ConfigureAwait(false);
                AssertEqual(1000, capped.PageSize, "page size is capped at 1000");
                EnumerationResult<UserMaster> sentinel = await PostPageAsync<UserMaster>(a, "/api/v1/users/enumerate?pageSize=-1", new { PageSize = 5 }).ConfigureAwait(false);
                AssertEqual(1, sentinel.PageSize, "a -1 querystring sentinel is clamped, not unlimited");

                using (HttpResponseMessage malformed = await a.PostAsync("/api/v1/users/enumerate", new StringContent("{\"PageSize\":", Encoding.UTF8, "application/json")).ConfigureAwait(false))
                    AssertTrue((int)malformed.StatusCode >= 400 && (int)malformed.StatusCode < 500, "malformed body is a client error, got " + (int)malformed.StatusCode);

                using (HttpResponseMessage getPage = await a.GetAsync("/api/v1/users?pageNumber=2&pageSize=2").ConfigureAwait(false))
                {
                    EnumerationResult<UserMaster> viaGet = await JsonHelper.DeserializeAsync<EnumerationResult<UserMaster>>(getPage).ConfigureAwait(false);
                    EnumerationResult<UserMaster> viaPost = await PostPageAsync<UserMaster>(a, "/api/v1/users/enumerate", new { PageNumber = 2, PageSize = 2 }).ConfigureAwait(false);
                    AssertEqual(String.Join(",", viaPost.Objects.Select(u => u.Id)), String.Join(",", viaGet.Objects.Select(u => u.Id)), "GET and POST return the same page");
                }

                using HttpClient b = tenantB.CreateClient(fx.BaseUrl);
                EnumerationResult<UserMaster> other = await PostPageAsync<UserMaster>(b, "/api/v1/users/enumerate", new { PageSize = 100, TenantId = tenantA.TenantId }).ConfigureAwait(false);
                AssertTrue(other.TotalRecords >= 2 && other.TotalRecords == other.Objects.Count, "tenant B sees its own users even when the body names tenant A: " + other.TotalRecords);
                AssertTrue(other.Objects.All(u => u.TenantId == tenantB.TenantId), "no tenant A user leaks to tenant B");
                AssertFalse(other.Objects.Any(u => seen.Contains(u.Id)), "no tenant A id on tenant B's page");
            }));

            cases.Add(Case("credentials_and_jobs_enumerate_are_caller_scoped", "A regular user's credential and job pages hold only their own rows, and GET /jobs without parameters is one bounded page", async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                E2ETenantUser admin = await E2ETenantUser.CreateAsync(fx.AuthClient, "credadm", true).ConfigureAwait(false);
                E2ETenantUser member = await E2ETenantUser.CreateAsync(fx.AuthClient, "credusr", false, admin.TenantId).ConfigureAwait(false);
                using HttpClient m = member.CreateClient(fx.BaseUrl);
                EnumerationResult<Credential> mine = await PostPageAsync<Credential>(m, "/api/v1/credentials/enumerate", new { PageSize = 50 }).ConfigureAwait(false);
                AssertTrue(mine.TotalRecords >= 1, "the member has a credential");
                AssertTrue(mine.Objects.All(c => c.UserId == member.UserId), "only the member's credentials");
                AssertTrue(mine.Objects.All(c => !String.Equals(c.BearerToken, member.BearerToken, StringComparison.Ordinal)), "bearer tokens stay redacted");
                using HttpClient t = admin.CreateClient(fx.BaseUrl);
                EnumerationResult<Credential> tenant = await PostPageAsync<Credential>(t, "/api/v1/credentials/enumerate", new { PageSize = 50 }).ConfigureAwait(false);
                AssertTrue(tenant.TotalRecords >= 2, "the tenant admin sees both credentials");
                AssertTrue(tenant.Objects.All(c => c.TenantId == admin.TenantId), "only the tenant's credentials");

                EnumerationResult<Job> jobs = await PostPageAsync<Job>(m, "/api/v1/jobs/enumerate", new { PageSize = 7 }).ConfigureAwait(false);
                AssertEqual(7, jobs.PageSize, "jobs enumerate echoes the page size");
                AssertTrue(jobs.Objects.All(j => j.UserId == member.UserId), "only the member's jobs");
                using HttpResponseMessage plain = await fx.AuthClient.GetAsync("/api/v1/jobs").ConfigureAwait(false);
                EnumerationResult<Job> firstPage = await JsonHelper.DeserializeAsync<EnumerationResult<Job>>(plain).ConfigureAwait(false);
                AssertEqual(1, firstPage.PageNumber, "GET /jobs without parameters is page 1");
                AssertEqual(100, firstPage.PageSize, "of the bounded default size");
            }));

            cases.Add(Case("signals_rest_and_mcp_pages_match", "POST /signals/enumerate and MCP enumerate signals return the same pages and totals for the same filter", async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                HttpResponseMessage captainResponse = await fx.AuthClient.PostAsync("/api/v1/captains", JsonHelper.ToJsonContent(new { Name = "collection-parity-" + Guid.NewGuid().ToString("N").Substring(0, 8) })).ConfigureAwait(false);
                Captain captain = await JsonHelper.DeserializeAsync<Captain>(captainResponse).ConfigureAwait(false);
                for (int i = 0; i < 3; i++)
                {
                    using HttpResponseMessage created = await fx.AuthClient.PostAsync("/api/v1/signals", JsonHelper.ToJsonContent(new { Type = "Nudge", Payload = "parity " + i, ToCaptainId = captain.Id })).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.Created, created.StatusCode, "signal " + i);
                }

                using HttpClient mcp = new HttpClient();
                mcp.BaseAddress = fx.McpClient.BaseAddress;
                mcp.DefaultRequestHeaders.Add("X-Api-Key", fx.ApiKey);
                List<string> restIds = new List<string>();
                List<string> mcpIds = new List<string>();
                for (int page = 1; page <= 2; page++)
                {
                    EnumerationResult<Signal> rest = await PostPageAsync<Signal>(fx.AuthClient, "/api/v1/signals/enumerate", new { ToCaptainId = captain.Id, PageNumber = page, PageSize = 2 }).ConfigureAwait(false);
                    string text = ToolText(await McpTestCalls.CallToolAsync(mcp, "enumerate", new { entityType = "signals", toCaptainId = captain.Id, pageNumber = page, pageSize = 2 }).ConfigureAwait(false));
                    EnumerationResult<Signal> viaMcp = JsonHelper.Deserialize<EnumerationResult<Signal>>(text);
                    AssertEqual(3L, rest.TotalRecords, "REST total");
                    AssertEqual(rest.TotalRecords, viaMcp.TotalRecords, "MCP total matches REST");
                    AssertEqual(rest.TotalPages, viaMcp.TotalPages, "MCP pages match REST");
                    restIds.AddRange(rest.Objects.Select(s => s.Id));
                    mcpIds.AddRange(viaMcp.Objects.Select(s => s.Id));
                }

                AssertEqual(3, restIds.Distinct().Count(), "REST pages cover every signal once");
                AssertEqual(String.Join(",", restIds), String.Join(",", mcpIds), "MCP pages hold the same signals in the same order");
            }));

            cases.Add(Case("cli_permission_requests_enumerate_rest_and_mcp", "CLI permission requests page on REST and MCP with the same bounds and reject an invalid status", async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                EnumerationResult<CliPermissionRequest> rest = await PostPageAsync<CliPermissionRequest>(fx.AuthClient, "/api/v1/cli-permissions/requests/enumerate", new { PageSize = 3, Status = "Pending" }).ConfigureAwait(false);
                AssertEqual(3, rest.PageSize, "REST echoes the page size");
                using (HttpResponseMessage bad = await fx.AuthClient.PostAsync("/api/v1/cli-permissions/requests/enumerate", JsonHelper.ToJsonContent(new { Status = "NotAStatus" })).ConfigureAwait(false))
                    AssertEqual(HttpStatusCode.BadRequest, bad.StatusCode, "an invalid status is a 400");

                using HttpClient mcp = new HttpClient();
                mcp.BaseAddress = fx.McpClient.BaseAddress;
                mcp.DefaultRequestHeaders.Add("X-Api-Key", fx.ApiKey);
                EnumerationResult<CliPermissionRequest> viaMcp = JsonHelper.Deserialize<EnumerationResult<CliPermissionRequest>>(ToolText(await McpTestCalls.CallToolAsync(mcp, "enumerate", new { entityType = "cli_permission_requests", pageSize = 3, status = "Pending" }).ConfigureAwait(false)));
                AssertEqual(3, viaMcp.PageSize, "MCP echoes the page size");
                AssertEqual(rest.TotalRecords, viaMcp.TotalRecords, "MCP total matches REST");
                string invalid = ToolText(await McpTestCalls.CallToolAsync(mcp, "enumerate", new { entityType = "cli_permission_requests", status = "NotAStatus" }).ConfigureAwait(false));
                AssertContains("status must be one of", invalid, "MCP rejects an invalid status");
            }));

            return new TestSuiteDescriptor(SuiteId, "No get-all collection APIs", cases);
        }

        #endregion

        #region Private-Methods

        private async Task<List<ApiRestRoute>> LiveRestAsync()
        {
            E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
            return ApiSurfaceBuilder.Build(fx.Server).Rest;
        }

        // A declared 200 type is a page when it is EnumerationResult<T> or an Armada model derived from it (for example
        // MuxEndpointPage, a page that also says which Harbor's mux answered).
        private static bool IsPageResponse(string response)
        {
            if (response.StartsWith("EnumerationResult<", StringComparison.Ordinal)) return true;
            if (String.IsNullOrEmpty(response) || response.Contains('<')) return false;
            Type? type = typeof(EnumerationQuery).Assembly.GetType("Armada.Core.Models." + response, false);
            for (Type? t = type?.BaseType; t != null; t = t.BaseType)
            {
                if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(EnumerationResult<>)) return true;
            }

            return false;
        }

        private static async Task<EnumerationResult<T>> PostPageAsync<T>(HttpClient client, string route, object? body)
        {
            HttpContent content = body == null ? new StringContent("", Encoding.UTF8, "application/json") : JsonHelper.ToJsonContent(body);
            using HttpResponseMessage response = await client.PostAsync(route, content).ConfigureAwait(false);
            string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            AssertEqual(HttpStatusCode.OK, response.StatusCode, route + ": " + text);
            return JsonHelper.Deserialize<EnumerationResult<T>>(text);
        }

        private static string ToolText(string rpcBody)
        {
            McpRpcEnvelope envelope = JsonHelper.Deserialize<McpRpcEnvelope>(rpcBody);
            if (envelope.Error != null) throw new AssertionException("MCP error: " + envelope.Error.Message);
            string? text = envelope.Result?.Content.FirstOrDefault()?.Text;
            AssertTrue(!String.IsNullOrEmpty(text), "MCP returned text content: " + rpcBody);
            return text!;
        }

        private TestCaseDescriptor Case(string caseId, string displayName, Func<Task> body)
        {
            return new TestCaseDescriptor(SuiteId, caseId, displayName, (CancellationToken ct) => body());
        }

        #endregion
    }
}

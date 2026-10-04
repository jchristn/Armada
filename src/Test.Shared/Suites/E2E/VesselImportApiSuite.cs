namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// End-to-end descriptors for the vessel import REST routes and MCP tools: authorization (401, 403 for a
    /// non-admin), validation (400 for empty input), allowed-root enforcement (403), browse with base64url and plain
    /// paths, inline import (200), background import (202), batch history reads, cross-tenant isolation (404), and an
    /// MCP discover_vessels / import_vessels / enumerate round trip. The fixture allows the system temp directory and
    /// sets Import.InlineBatchLimit to 3.
    /// </summary>
    public sealed class VesselImportApiSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "E2E.VesselImportApi";
        private string _Root = "";
        private string _BatchId = "";
        private HttpClient? _NonAdminClient;
        private HttpClient? _OtherTenantAdminClient;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the vessel import API suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("setup", "Setup repositories and tenant clients", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                _Root = TestTemp.NewDirectory("e2e_import");
                MakeFakeRepo(Path.Combine(_Root, "small", "repo-a"));
                MakeFakeRepo(Path.Combine(_Root, "small", "repo-b"));
                for (int i = 0; i < 5; i++) MakeFakeRepo(Path.Combine(_Root, "large", "bulk-" + i));

                _NonAdminClient = await CreateTenantClientAsync(fx, "importuser", false, null).ConfigureAwait(false);
                _OtherTenantAdminClient = await CreateTenantClientAsync(fx, "importother", true, null).ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("unauthenticated_401", "Discover without credentials returns 401", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                HttpResponseMessage response = await fx.UnauthClient.PostAsync("/api/v1/vessels/import/discover", JsonHelper.ToJsonContent(new { roots = new[] { _Root } })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.Unauthorized, response.StatusCode);
            }));

            cases.Add(CaseAsync("non_admin_403", "A non-admin user gets 403 on browse, discover, and import, but can read history", TestTags.Negative, async () =>
            {
                await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                HttpResponseMessage discover = await _NonAdminClient!.PostAsync("/api/v1/vessels/import/discover", JsonHelper.ToJsonContent(new { roots = new[] { _Root } })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.Forbidden, discover.StatusCode, "discover");
                HttpResponseMessage browse = await _NonAdminClient.GetAsync("/api/v1/vessels/import/browse").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.Forbidden, browse.StatusCode, "browse");
                HttpResponseMessage import = await _NonAdminClient.PostAsync("/api/v1/vessels/import", JsonHelper.ToJsonContent(new { batchId = "vib_x", paths = new[] { _Root } })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.Forbidden, import.StatusCode, "import");
                HttpResponseMessage history = await _NonAdminClient.PostAsync("/api/v1/vessels/import/batches/enumerate", JsonHelper.ToJsonContent(new { })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, history.StatusCode, "history");
            }));

            cases.Add(CaseAsync("empty_input_400", "Discover and import with empty input return 400", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                HttpResponseMessage discover = await fx.AuthClient.PostAsync("/api/v1/vessels/import/discover", JsonHelper.ToJsonContent(new { directories = new string[0], roots = new string[0] })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.BadRequest, discover.StatusCode, "discover");
                HttpResponseMessage import = await fx.AuthClient.PostAsync("/api/v1/vessels/import", JsonHelper.ToJsonContent(new { batchId = "", paths = new string[0] })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.BadRequest, import.StatusCode, "import");
            }));

            cases.Add(CaseAsync("outside_allowed_root_403", "A path outside the allowed roots returns 403 with code PathNotAllowed", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                string outside = Path.GetPathRoot(Path.GetTempPath()) ?? "/";
                HttpResponseMessage response = await fx.AuthClient.PostAsync("/api/v1/vessels/import/discover", JsonHelper.ToJsonContent(new { directories = new[] { outside } })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.Forbidden, response.StatusCode);
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                AssertContains("PathNotAllowed", body);
            }));

            cases.Add(CaseAsync("browse_base64url_and_plain", "Browse accepts base64url and plain paths and flags repositories", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                string small = Path.Combine(_Root, "small");
                string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(small)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

                HttpResponseMessage response = await fx.AuthClient.GetAsync("/api/v1/vessels/import/browse?path=" + encoded).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode, "base64url");
                VesselBrowseResult listing = await JsonHelper.DeserializeAsync<VesselBrowseResult>(response).ConfigureAwait(false);
                AssertEqual(2, listing.Entries.Count);
                AssertTrue(listing.Entries.All(e => e.IsGitRepository), "both are repositories");

                HttpResponseMessage plain = await fx.AuthClient.GetAsync("/api/v1/vessels/import/browse?path=" + Uri.EscapeDataString(small)).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, plain.StatusCode, "plain");
                VesselBrowseResult plainListing = await JsonHelper.DeserializeAsync<VesselBrowseResult>(plain).ConfigureAwait(false);
                AssertEqual(2, plainListing.Entries.Count);

                HttpResponseMessage roots = await fx.AuthClient.GetAsync("/api/v1/vessels/import/browse").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, roots.StatusCode, "roots");

                HttpResponseMessage missing = await fx.AuthClient.GetAsync("/api/v1/vessels/import/browse?path=" + Uri.EscapeDataString(Path.Combine(_Root, "nope"))).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NotFound, missing.StatusCode, "missing");
            }));

            cases.Add(CaseAsync("discover_and_inline_import_200", "Discover returns candidates and a small import runs inline with 200", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                HttpResponseMessage response = await fx.AuthClient.PostAsync("/api/v1/vessels/import/discover", JsonHelper.ToJsonContent(new { roots = new[] { Path.Combine(_Root, "small") } })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode, "discover");
                string raw = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                AssertContains("\"BatchId\"", raw, "PascalCase wire format (the dashboard client camel-cases keys)");
                AssertContains("\"CandidateStatus\":\"New\"", raw, "string enums");
                VesselImportDiscoverResponse discovered = JsonHelper.Deserialize<VesselImportDiscoverResponse>(raw);
                AssertEqual(2, discovered.Candidates.Count);
                _BatchId = discovered.BatchId;

                HttpResponseMessage import = await fx.AuthClient.PostAsync("/api/v1/vessels/import", JsonHelper.ToJsonContent(new { batchId = _BatchId, paths = discovered.Candidates.Select(c => c.Path).ToArray() })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, import.StatusCode, "import");
                VesselImportResponse result = await JsonHelper.DeserializeAsync<VesselImportResponse>(import).ConfigureAwait(false);
                AssertEqual(2, result.Batch!.CreatedCount);
                AssertTrue(result.Items.All(i => i.Outcome == VesselImportOutcomeEnum.Created), "all created");

                HttpResponseMessage unknownBatch = await fx.AuthClient.PostAsync("/api/v1/vessels/import", JsonHelper.ToJsonContent(new { batchId = "vib_missing", paths = new[] { "/x" } })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NotFound, unknownBatch.StatusCode, "unknown batch");
            }));

            cases.Add(CaseAsync("large_import_202", "An import above the inline limit returns 202 with a job and completes", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                HttpResponseMessage response = await fx.AuthClient.PostAsync("/api/v1/vessels/import/discover", JsonHelper.ToJsonContent(new { directories = new[] { Path.Combine(_Root, "large") } })).ConfigureAwait(false);
                VesselImportDiscoverResponse discovered = await JsonHelper.DeserializeAsync<VesselImportDiscoverResponse>(response).ConfigureAwait(false);
                AssertEqual(5, discovered.Candidates.Count);

                HttpResponseMessage import = await fx.AuthClient.PostAsync("/api/v1/vessels/import", JsonHelper.ToJsonContent(new { batchId = discovered.BatchId, paths = discovered.Candidates.Select(c => c.Path).ToArray() })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.Accepted, import.StatusCode);
                VesselImportResponse accepted = await JsonHelper.DeserializeAsync<VesselImportResponse>(import).ConfigureAwait(false);
                AssertTrue(accepted.RunsInBackground, "background");
                AssertStartsWith("job_", accepted.JobId ?? "");

                VesselImportBatchDetail? detail = null;
                DateTime deadline = DateTime.UtcNow.AddSeconds(30);
                while (DateTime.UtcNow < deadline)
                {
                    HttpResponseMessage get = await fx.AuthClient.GetAsync("/api/v1/vessels/import/batches/" + discovered.BatchId).ConfigureAwait(false);
                    detail = await JsonHelper.DeserializeAsync<VesselImportBatchDetail>(get).ConfigureAwait(false);
                    if (detail.Batch.Status != VesselImportBatchStatusEnum.Importing) break;
                    await Task.Delay(100).ConfigureAwait(false);
                }

                AssertEqual(VesselImportBatchStatusEnum.Completed, detail!.Batch.Status);
                AssertEqual(5, detail.Batch.CreatedCount);
                AssertEqual(5, detail.Items.Count);
            }));

            cases.Add(CaseAsync("history_and_cross_tenant_404", "Batch history reads work in-tenant and return 404 from another tenant", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                HttpResponseMessage own = await fx.AuthClient.GetAsync("/api/v1/vessels/import/batches/" + _BatchId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, own.StatusCode, "own tenant");
                VesselImportBatchDetail detail = await JsonHelper.DeserializeAsync<VesselImportBatchDetail>(own).ConfigureAwait(false);
                AssertEqual(VesselImportBatchStatusEnum.Completed, detail.Batch.Status);

                HttpResponseMessage page = await fx.AuthClient.PostAsync("/api/v1/vessels/import/batches/enumerate", JsonHelper.ToJsonContent(new { pageSize = 10 })).ConfigureAwait(false);
                EnumerationResult<VesselImportBatch> batches = await JsonHelper.DeserializeAsync<EnumerationResult<VesselImportBatch>>(page).ConfigureAwait(false);
                AssertTrue(batches.Objects.Any(b => b.Id == _BatchId), "enumerate contains batch");

                HttpResponseMessage cross = await _OtherTenantAdminClient!.GetAsync("/api/v1/vessels/import/batches/" + _BatchId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NotFound, cross.StatusCode, "cross tenant read");
                HttpResponseMessage crossImport = await _OtherTenantAdminClient.PostAsync("/api/v1/vessels/import", JsonHelper.ToJsonContent(new { batchId = _BatchId, paths = new[] { "/x" } })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NotFound, crossImport.StatusCode, "cross tenant import");
                HttpResponseMessage crossPage = await _OtherTenantAdminClient.PostAsync("/api/v1/vessels/import/batches/enumerate", JsonHelper.ToJsonContent(new { })).ConfigureAwait(false);
                EnumerationResult<VesselImportBatch> crossBatches = await JsonHelper.DeserializeAsync<EnumerationResult<VesselImportBatch>>(crossPage).ConfigureAwait(false);
                AssertEqual(0L, crossBatches.TotalRecords, "cross tenant enumerate");
            }));

            cases.Add(CaseAsync("mcp_round_trip", "MCP discover_vessels, import_vessels, and enumerate vessel_import_batch round-trip", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                string mcpRoot = Path.Combine(_Root, "mcp");
                MakeFakeRepo(Path.Combine(mcpRoot, "mcp-repo"));
                string sessionId = await InitMcpSessionAsync(fx.McpClient).ConfigureAwait(false);

                string discoverText = await CallToolTextAsync(fx.McpClient, sessionId, "discover_vessels", new { roots = new[] { mcpRoot } }).ConfigureAwait(false);
                VesselImportDiscoverResponse discovered = JsonHelper.Deserialize<VesselImportDiscoverResponse>(discoverText);
                AssertEqual(1, discovered.Candidates.Count);

                string importText = await CallToolTextAsync(fx.McpClient, sessionId, "import_vessels", new { batchId = discovered.BatchId, allNew = true }).ConfigureAwait(false);
                VesselImportResponse imported = JsonHelper.Deserialize<VesselImportResponse>(importText);
                AssertEqual(1, imported.Batch!.CreatedCount);
                AssertNotNull(imported.Items.Single().VesselId, "vessel id");

                string enumerateText = await CallToolTextAsync(fx.McpClient, sessionId, "enumerate", new { entityType = "vessel_import_batch", pageSize = 25 }).ConfigureAwait(false);
                EnumerationResult<VesselImportBatch> page = JsonHelper.Deserialize<EnumerationResult<VesselImportBatch>>(enumerateText);
                AssertTrue(page.Objects.Any(b => b.Id == discovered.BatchId), "enumerate contains MCP batch");

                string errorText = await CallToolTextAsync(fx.McpClient, sessionId, "discover_vessels", new { roots = new string[0] }).ConfigureAwait(false);
                AssertContains("InvalidRequest", errorText);
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Vessel Import API",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static string MakeFakeRepo(string path)
        {
            Directory.CreateDirectory(Path.Combine(path, ".git"));
            return path;
        }

        private static async Task<HttpClient> CreateTenantClientAsync(E2EServerFixture fx, string label, bool isTenantAdmin, string? tenantId)
        {
            string resolvedTenant = tenantId ?? "";
            if (String.IsNullOrEmpty(resolvedTenant))
            {
                HttpResponseMessage tenantResp = await fx.AuthClient.PostAsync("/api/v1/tenants",
                    JsonHelper.ToJsonContent(new { Name = "imp-" + label + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) })).ConfigureAwait(false);
                TenantMetadata tenant = await JsonHelper.DeserializeAsync<TenantMetadata>(tenantResp).ConfigureAwait(false);
                resolvedTenant = tenant.Id;
            }

            HttpResponseMessage userResp = await fx.AuthClient.PostAsync("/api/v1/users",
                JsonHelper.ToJsonContent(new
                {
                    TenantId = resolvedTenant,
                    Email = label + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + "@import.armada",
                    PasswordSha256 = UserMaster.ComputePasswordHash("testpass"),
                    IsTenantAdmin = isTenantAdmin
                })).ConfigureAwait(false);
            UserMaster user = await JsonHelper.DeserializeAsync<UserMaster>(userResp).ConfigureAwait(false);

            HttpResponseMessage credResp = await fx.AuthClient.PostAsync("/api/v1/credentials",
                JsonHelper.ToJsonContent(new { TenantId = resolvedTenant, UserId = user.Id, Name = label + "-cred" })).ConfigureAwait(false);
            Credential cred = await JsonHelper.DeserializeAsync<Credential>(credResp).ConfigureAwait(false);

            HttpClient client = new HttpClient();
            client.BaseAddress = new Uri(fx.BaseUrl);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cred.BearerToken);
            return client;
        }

        private static async Task<string> InitMcpSessionAsync(HttpClient mcpClient)
        {
            object request = new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new
                {
                    protocolVersion = "2024-11-05",
                    capabilities = new { },
                    clientInfo = new { name = "import-test-client", version = "1.0" }
                }
            };

            HttpRequestMessage httpRequest = new HttpRequestMessage(HttpMethod.Post, "/rpc");
            httpRequest.Content = JsonHelper.ToJsonContent(request);
            HttpResponseMessage response = await mcpClient.SendAsync(httpRequest).ConfigureAwait(false);
            Assert(response.IsSuccessStatusCode, "MCP initialize failed with " + response.StatusCode);

            string sessionId = String.Empty;
            if (response.Headers.TryGetValues("Mcp-Session-Id", out IEnumerable<string>? values)) sessionId = values.FirstOrDefault() ?? "";
            Assert(!String.IsNullOrEmpty(sessionId), "MCP initialize did not return an Mcp-Session-Id header");

            HttpRequestMessage notification = new HttpRequestMessage(HttpMethod.Post, "/rpc");
            notification.Content = JsonHelper.ToJsonContent(new { jsonrpc = "2.0", method = "notifications/initialized", @params = new { } });
            notification.Headers.Add("Mcp-Session-Id", sessionId);
            HttpResponseMessage notificationResponse = await mcpClient.SendAsync(notification).ConfigureAwait(false);
            notificationResponse.Dispose();
            return sessionId;
        }

        private static async Task<string> CallToolTextAsync(HttpClient mcpClient, string sessionId, string toolName, object arguments)
        {
            object request = new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "tools/call",
                @params = new { name = toolName, arguments = arguments }
            };

            HttpRequestMessage httpRequest = new HttpRequestMessage(HttpMethod.Post, "/rpc");
            httpRequest.Content = JsonHelper.ToJsonContent(request);
            httpRequest.Headers.Add("Mcp-Session-Id", sessionId);
            HttpResponseMessage response = await mcpClient.SendAsync(httpRequest).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            Assert(response.IsSuccessStatusCode, "MCP " + toolName + " failed with " + response.StatusCode + ": " + body);

            McpRpcEnvelope envelope = JsonHelper.Deserialize<McpRpcEnvelope>(body);
            if (envelope.Error != null) throw new AssertionException("MCP error from " + toolName + ": " + envelope.Error.Message);
            string? text = envelope.Result?.Content.FirstOrDefault()?.Text;
            Assert(!String.IsNullOrEmpty(text), "MCP " + toolName + " returned no text content: " + body);
            return text!;
        }

        private TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag, TestTags.EndToEnd });
        }

        #endregion
    }
}

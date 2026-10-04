namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// End-to-end descriptors for background discovery and fleet categorization routes: background discover (202 then
    /// Discovered), the default prompt endpoint, captain validation on import (400), applying fleet recommendations
    /// with fleet-name reuse (200), authorization (403 for a non-admin), tenant isolation (404), categorize on an
    /// unfinished batch (409).
    /// </summary>
    public sealed class VesselImportCategorizationApiSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "E2E.VesselImportCategorizationApi";
        private string _Root = "";
        private HttpClient? _NonAdminClient;
        private HttpClient? _OtherTenantAdminClient;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("setup", "Setup repositories and tenant clients", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                _Root = TestTemp.NewDirectory("e2e_categorize");
                MakeFakeRepo(Path.Combine(_Root, "bg", "bg-a"));
                MakeFakeRepo(Path.Combine(_Root, "bg", "bg-b"));
                MakeFakeRepo(Path.Combine(_Root, "apply", "svc-a"));
                MakeFakeRepo(Path.Combine(_Root, "apply", "svc-b"));
                MakeFakeRepo(Path.Combine(_Root, "apply", "svc-c"));
                _NonAdminClient = await CreateTenantClientAsync(fx, "catuser", false).ConfigureAwait(false);
                _OtherTenantAdminClient = await CreateTenantClientAsync(fx, "catother", true).ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("background_discover_202", "Discover with runInBackground returns 202 and the batch reaches Discovered", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                HttpResponseMessage response = await fx.AuthClient.PostAsync("/api/v1/vessels/import/discover",
                    JsonHelper.ToJsonContent(new { roots = new[] { Path.Combine(_Root, "bg") }, runInBackground = true })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.Accepted, response.StatusCode);
                VesselImportDiscoverResponse accepted = await JsonHelper.DeserializeAsync<VesselImportDiscoverResponse>(response).ConfigureAwait(false);
                AssertTrue(accepted.RunsInBackground, "runsInBackground");
                AssertStartsWith("job_", accepted.JobId ?? "");

                VesselImportBatchDetail detail = await WaitForAsync(fx, accepted.BatchId, d => d.Batch.Status != VesselImportBatchStatusEnum.Discovering).ConfigureAwait(false);
                AssertEqual(VesselImportBatchStatusEnum.Discovered, detail.Batch.Status);
                AssertEqual(2, detail.Items.Count);

                HttpResponseMessage job = await fx.AuthClient.GetAsync("/api/v1/jobs/" + accepted.JobId).ConfigureAwait(false);
                Job parsed = await JsonHelper.DeserializeAsync<Job>(job).ConfigureAwait(false);
                AssertEqual(JobKindEnum.VesselDiscovery, parsed.Kind);
            }));

            cases.Add(CaseAsync("default_prompt", "The default prompt endpoint returns the template for admins and 403 for non-admins", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                HttpResponseMessage response = await fx.AuthClient.GetAsync("/api/v1/vessels/import/categorization/default-prompt").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);
                FleetCategorizationDefaultPrompt prompt = await JsonHelper.DeserializeAsync<FleetCategorizationDefaultPrompt>(response).ConfigureAwait(false);
                AssertEqual("import.fleet_categorization", prompt.TemplateName);
                AssertContains("REPOSITORIES.md", prompt.Prompt);
                AssertEqual(20, prompt.TimeoutMinutes);

                HttpResponseMessage denied = await _NonAdminClient!.GetAsync("/api/v1/vessels/import/categorization/default-prompt").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.Forbidden, denied.StatusCode);
            }));

            cases.Add(CaseAsync("import_unknown_captain_400", "Import with categorization and an unknown captain returns 400 and creates nothing", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                VesselImportDiscoverResponse discovered = await DiscoverAsync(fx, Path.Combine(_Root, "bg")).ConfigureAwait(false);
                HttpResponseMessage import = await fx.AuthClient.PostAsync("/api/v1/vessels/import", JsonHelper.ToJsonContent(new
                {
                    batchId = discovered.BatchId,
                    paths = discovered.Candidates.Select(c => c.Path).ToArray(),
                    categorization = new { enabled = true, captainId = "cpt_does_not_exist" }
                })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.BadRequest, import.StatusCode);
                string body = await import.Content.ReadAsStringAsync().ConfigureAwait(false);
                AssertContains("Captain not found", body);
            }));

            cases.Add(CaseAsync("apply_reuses_fleet_and_isolates_tenants", "Apply reuses a fleet by name, assigns vessels, and is denied to non-admins and other tenants", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                VesselImportDiscoverResponse discovered = await DiscoverAsync(fx, Path.Combine(_Root, "apply")).ConfigureAwait(false);
                HttpResponseMessage import = await fx.AuthClient.PostAsync("/api/v1/vessels/import",
                    JsonHelper.ToJsonContent(new { batchId = discovered.BatchId, paths = discovered.Candidates.Select(c => c.Path).ToArray() })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, import.StatusCode, "import");
                VesselImportResponse imported = await JsonHelper.DeserializeAsync<VesselImportResponse>(import).ConfigureAwait(false);
                Dictionary<string, string> ids = imported.Items.ToDictionary(i => Path.GetFileName(i.Path), i => i.VesselId!);

                string sharedName = "Shared Fleet " + Guid.NewGuid().ToString("N").Substring(0, 6);
                HttpResponseMessage fleetResp = await fx.AuthClient.PostAsync("/api/v1/fleets", JsonHelper.ToJsonContent(new { Name = sharedName })).ConfigureAwait(false);
                Fleet shared = await JsonHelper.DeserializeAsync<Fleet>(fleetResp).ConfigureAwait(false);

                object body = new
                {
                    fleets = new object[]
                    {
                        new { name = sharedName.ToUpperInvariant(), vesselIds = new[] { ids["svc-a"], ids["svc-b"] } },
                        new { name = "Created By Apply " + shared.Id, description = "new", vesselIds = new[] { ids["svc-c"] } }
                    }
                };
                string url = "/api/v1/vessels/import/batches/" + discovered.BatchId + "/fleet-recommendations/apply";

                HttpResponseMessage nonAdmin = await _NonAdminClient!.PostAsync(url, JsonHelper.ToJsonContent(body)).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.Forbidden, nonAdmin.StatusCode, "non-admin");
                HttpResponseMessage cross = await _OtherTenantAdminClient!.PostAsync(url, JsonHelper.ToJsonContent(body)).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NotFound, cross.StatusCode, "other tenant");

                HttpResponseMessage bad = await fx.AuthClient.PostAsync(url, JsonHelper.ToJsonContent(new { fleets = new object[] { new { name = "X", vesselIds = new[] { "vsl_not_in_batch" } } } })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.BadRequest, bad.StatusCode, "foreign vessel");

                HttpResponseMessage response = await fx.AuthClient.PostAsync(url, JsonHelper.ToJsonContent(body)).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode, "apply");
                FleetRecommendationApplyResult result = await JsonHelper.DeserializeAsync<FleetRecommendationApplyResult>(response).ConfigureAwait(false);
                AssertEqual(shared.Id, result.Fleets[0].Id, "reused by name");
                AssertEqual(1, result.CreatedFleetIds.Count);
                AssertEqual(3, result.Assignments.Count);

                HttpResponseMessage vessel = await fx.AuthClient.GetAsync("/api/v1/vessels/" + ids["svc-a"]).ConfigureAwait(false);
                Vessel svcA = await JsonHelper.DeserializeAsync<Vessel>(vessel).ConfigureAwait(false);
                AssertEqual(shared.Id, svcA.FleetId, "vessel assigned");

                HttpResponseMessage get = await fx.AuthClient.GetAsync("/api/v1/vessels/import/batches/" + discovered.BatchId).ConfigureAwait(false);
                VesselImportBatchDetail detail = await JsonHelper.DeserializeAsync<VesselImportBatchDetail>(get).ConfigureAwait(false);
                AssertEqual(VesselImportCategorizationStatusEnum.Applied, detail.Batch.CategorizationStatus);
                AssertEqual(2, detail.FleetRecommendations.Count);
                AssertEqual(shared.Id, detail.FleetRecommendations[0].AppliedFleetId);
            }));

            cases.Add(CaseAsync("categorize_unfinished_409", "Categorize on a batch that has not been imported returns 409; a missing batch returns 404", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                VesselImportDiscoverResponse discovered = await DiscoverAsync(fx, Path.Combine(_Root, "bg")).ConfigureAwait(false);
                HttpResponseMessage conflict = await fx.AuthClient.PostAsync("/api/v1/vessels/import/batches/" + discovered.BatchId + "/categorize",
                    JsonHelper.ToJsonContent(new { captainId = "cpt_x" })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.Conflict, conflict.StatusCode, "unfinished");
                HttpResponseMessage missing = await fx.AuthClient.PostAsync("/api/v1/vessels/import/batches/vib_missing/categorize", JsonHelper.ToJsonContent(new { })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NotFound, missing.StatusCode, "missing");
                HttpResponseMessage nonAdmin = await _NonAdminClient!.PostAsync("/api/v1/vessels/import/batches/" + discovered.BatchId + "/categorize", JsonHelper.ToJsonContent(new { })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.Forbidden, nonAdmin.StatusCode, "non-admin");
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Vessel Import Categorization API",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static string MakeFakeRepo(string path)
        {
            Directory.CreateDirectory(Path.Combine(path, ".git"));
            File.WriteAllText(Path.Combine(path, "README.md"), "# " + Path.GetFileName(path) + "\n");
            return path;
        }

        private static async Task<VesselImportDiscoverResponse> DiscoverAsync(E2EServerFixture fx, string root)
        {
            HttpResponseMessage response = await fx.AuthClient.PostAsync("/api/v1/vessels/import/discover", JsonHelper.ToJsonContent(new { roots = new[] { root } })).ConfigureAwait(false);
            AssertEqual(HttpStatusCode.OK, response.StatusCode, "discover");
            return await JsonHelper.DeserializeAsync<VesselImportDiscoverResponse>(response).ConfigureAwait(false);
        }

        private static async Task<VesselImportBatchDetail> WaitForAsync(E2EServerFixture fx, string batchId, Func<VesselImportBatchDetail, bool> done)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(30);
            VesselImportBatchDetail? detail = null;
            while (DateTime.UtcNow < deadline)
            {
                HttpResponseMessage get = await fx.AuthClient.GetAsync("/api/v1/vessels/import/batches/" + batchId).ConfigureAwait(false);
                detail = await JsonHelper.DeserializeAsync<VesselImportBatchDetail>(get).ConfigureAwait(false);
                if (done(detail)) return detail;
                await Task.Delay(100).ConfigureAwait(false);
            }

            throw new AssertionException("Timed out waiting for batch " + batchId + " (status " + detail?.Batch.Status + ")");
        }

        private static async Task<HttpClient> CreateTenantClientAsync(E2EServerFixture fx, string label, bool isTenantAdmin)
        {
            HttpResponseMessage tenantResp = await fx.AuthClient.PostAsync("/api/v1/tenants",
                JsonHelper.ToJsonContent(new { Name = "cat-" + label + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) })).ConfigureAwait(false);
            TenantMetadata tenant = await JsonHelper.DeserializeAsync<TenantMetadata>(tenantResp).ConfigureAwait(false);

            HttpResponseMessage userResp = await fx.AuthClient.PostAsync("/api/v1/users",
                JsonHelper.ToJsonContent(new
                {
                    TenantId = tenant.Id,
                    Email = label + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + "@categorize.armada",
                    PasswordSha256 = UserMaster.ComputePasswordHash("testpass"),
                    IsTenantAdmin = isTenantAdmin
                })).ConfigureAwait(false);
            UserMaster user = await JsonHelper.DeserializeAsync<UserMaster>(userResp).ConfigureAwait(false);

            HttpResponseMessage credResp = await fx.AuthClient.PostAsync("/api/v1/credentials",
                JsonHelper.ToJsonContent(new { TenantId = tenant.Id, UserId = user.Id, Name = label + "-cred" })).ConfigureAwait(false);
            Credential cred = await JsonHelper.DeserializeAsync<Credential>(credResp).ConfigureAwait(false);

            HttpClient client = new HttpClient();
            client.BaseAddress = new Uri(fx.BaseUrl);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cred.BearerToken);
            return client;
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

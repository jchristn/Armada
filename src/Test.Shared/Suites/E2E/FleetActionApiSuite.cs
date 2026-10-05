namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
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
    /// End-to-end descriptors for the fleet action REST routes: built-ins are listed, a non-admin cannot create or
    /// run a Command action (403) but can enumerate, an unknown template variable is a 400, a cross-tenant vessel in
    /// vesselIds rejects the whole run (404), a run is accepted with 202 and reaches a terminal status, another
    /// tenant cannot read the run (404), the target endpoint returns output fields, and cancelling a finished run is
    /// a 409. Cases run in order against this suite's own server and share state through instance fields.
    /// </summary>
    public sealed class FleetActionApiSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "E2E.FleetActionApi";

        private HttpClient? _AdminA;
        private HttpClient? _RegularA;
        private HttpClient? _AdminB;
        private string _VesselAId = String.Empty;
        private string _VesselBId = String.Empty;
        private string _RunId = String.Empty;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("setup_tenants_and_vessels", "Create two tenants, users, and one vessel each", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                TenantMetadata tenantA = await CreateTenantAsync(fx.AuthClient, "fa-a").ConfigureAwait(false);
                TenantMetadata tenantB = await CreateTenantAsync(fx.AuthClient, "fa-b").ConfigureAwait(false);
                _AdminA = await CreateUserClientAsync(fx, tenantA.Id, "admin-a", true).ConfigureAwait(false);
                _RegularA = await CreateUserClientAsync(fx, tenantA.Id, "user-a", false).ConfigureAwait(false);
                _AdminB = await CreateUserClientAsync(fx, tenantB.Id, "admin-b", true).ConfigureAwait(false);
                _VesselAId = await CreateVesselAsync(_AdminA, "fa-vessel-a").ConfigureAwait(false);
                _VesselBId = await CreateVesselAsync(_AdminB, "fa-vessel-b").ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("enumerate_lists_builtins", "Enumerate lists the five seeded built-ins, also for a regular user", TestTags.Positive, async () =>
            {
                await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                HttpResponseMessage response = await _RegularA!.PostAsync("/api/v1/fleet-actions/enumerate", JsonHelper.ToJsonContent(new { pageSize = 50 })).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.OK, response);
                EnumerationResult<FleetAction> result = await JsonHelper.DeserializeAsync<EnumerationResult<FleetAction>>(response).ConfigureAwait(false);
                AssertEqual(5, result.Objects.Count(a => a.IsBuiltIn), "built-ins");
                AssertTrue(result.Objects.Any(a => a.BuiltInKey == "ff-default-branch"), "ff-default-branch present");
            }));

            cases.Add(CaseAsync("non_admin_command_create_forbidden", "A non-admin creating a Command action gets 403", TestTags.Negative, async () =>
            {
                await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                HttpResponseMessage response = await _RegularA!.PostAsync("/api/v1/fleet-actions",
                    JsonHelper.ToJsonContent(new { name = "pull", kind = "Command", commandText = "git pull" })).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.Forbidden, response);
            }));

            cases.Add(CaseAsync("non_admin_command_run_forbidden", "A non-admin running a Command action gets 403", TestTags.Negative, async () =>
            {
                await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                HttpResponseMessage response = await _RegularA!.PostAsync("/api/v1/fleet-actions/run",
                    JsonHelper.ToJsonContent(new
                    {
                        vesselIds = new[] { _VesselAId },
                        definition = new { name = "x", kind = "Command", commandText = "echo x" }
                    })).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.Forbidden, response);
            }));

            cases.Add(CaseAsync("unknown_variable_400", "An unknown template variable is rejected with 400 naming it", TestTags.Negative, async () =>
            {
                await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                HttpResponseMessage response = await _AdminA!.PostAsync("/api/v1/fleet-actions",
                    JsonHelper.ToJsonContent(new { name = "bad", kind = "Command", commandText = "echo {{vessel.secret}}" })).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.BadRequest, response);
                ApiErrorProbe error = ApiErrorProbe.From(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                AssertEqual(WatsonWebserver.Core.ApiResultEnum.BadRequest, error.Error, "error");
                // TODO(R5, production): the unknown variable name is only in the message (FleetActionTemplateException.VariableName is not on the wire).
                AssertContains("{{vessel.secret}}", error.Message ?? "", "names the unknown variable");
            }));

            cases.Add(CaseAsync("cross_tenant_vessel_rejected", "A cross-tenant vessel rejects the whole run with 404", TestTags.Negative, async () =>
            {
                await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                HttpResponseMessage response = await _AdminA!.PostAsync("/api/v1/fleet-actions/run",
                    JsonHelper.ToJsonContent(new
                    {
                        vesselIds = new[] { _VesselAId, _VesselBId },
                        definition = new { name = "x", kind = "Command", commandText = "echo x", requiresCleanWorkingTree = false }
                    })).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.NotFound, response);

                HttpResponseMessage runs = await _AdminA!.PostAsync("/api/v1/fleet-action-runs/enumerate", JsonHelper.ToJsonContent(new { })).ConfigureAwait(false);
                EnumerationResult<FleetActionRun> page = await JsonHelper.DeserializeAsync<EnumerationResult<FleetActionRun>>(runs).ConfigureAwait(false);
                AssertEqual(0L, page.TotalRecords, "no run created");
            }));

            cases.Add(CaseAsync("run_accepted_and_finishes", "An admin run is accepted with 202 and reaches a terminal status", TestTags.Positive, async () =>
            {
                await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                HttpResponseMessage response = await _AdminA!.PostAsync("/api/v1/fleet-actions/run",
                    JsonHelper.ToJsonContent(new
                    {
                        vesselIds = new[] { _VesselAId },
                        definition = new { name = "echo", kind = "Command", commandText = "echo e2e-{{vessel.name}}", requiresCleanWorkingTree = false }
                    })).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.Accepted, response);
                FleetActionRunStartResult start = await JsonHelper.DeserializeAsync<FleetActionRunStartResult>(response).ConfigureAwait(false);
                AssertStartsWith("far_", start.RunId);
                AssertEqual(1, start.TargetCount);
                _RunId = start.RunId;

                FleetActionRunDetail? detail = null;
                MonotonicDeadline deadline = MonotonicDeadline.After(TimeSpan.FromSeconds(30));
                while (!deadline.Passed)
                {
                    HttpResponseMessage get = await _AdminA!.GetAsync("/api/v1/fleet-action-runs/" + _RunId).ConfigureAwait(false);
                    AssertStatusCode(HttpStatusCode.OK, get);
                    detail = await JsonHelper.DeserializeAsync<FleetActionRunDetail>(get).ConfigureAwait(false);
                    if (detail.Run.CompletedUtc.HasValue) break;
                    await Task.Delay(100).ConfigureAwait(false);
                }

                AssertNotNull(detail, "detail");
                AssertNotNull(detail!.Run.CompletedUtc, "run completed");
                AssertEqual(1, detail.Targets.Count);
                FleetActionRunTargetSummary summary = detail.Targets[0];
                AssertTrue(summary.Status == FleetActionTargetStatusEnum.Succeeded
                    || (summary.Status == FleetActionTargetStatusEnum.Skipped && summary.SkipReason == "NoWorkingDirectory"),
                    "target succeeded or was skipped for lack of a working directory, got " + summary.Status);

                HttpResponseMessage targetResponse = await _AdminA!.GetAsync("/api/v1/fleet-action-runs/" + _RunId + "/targets/" + summary.Id).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.OK, targetResponse);
                FleetActionRunTarget target = await JsonHelper.DeserializeAsync<FleetActionRunTarget>(targetResponse).ConfigureAwait(false);
                AssertEqual(summary.Id, target.Id);

                HttpResponseMessage targetsPage = await _AdminA!.PostAsync("/api/v1/fleet-action-runs/" + _RunId + "/targets/enumerate",
                    JsonHelper.ToJsonContent(new { pageSize = 10 })).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.OK, targetsPage);
            }));

            cases.Add(CaseAsync("other_tenant_cannot_read_run", "Another tenant reading the run gets 404", TestTags.Negative, async () =>
            {
                await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                HttpResponseMessage response = await _AdminB!.GetAsync("/api/v1/fleet-action-runs/" + _RunId).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.NotFound, response);
            }));

            cases.Add(CaseAsync("cancel_finished_run_conflict", "Cancelling a finished run returns 409", TestTags.Negative, async () =>
            {
                await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                HttpResponseMessage response = await _AdminA!.PostAsync("/api/v1/fleet-action-runs/" + _RunId + "/cancel", JsonHelper.ToJsonContent(new { })).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.Conflict, response);
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Fleet Action API",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task<TenantMetadata> CreateTenantAsync(HttpClient adminClient, string label)
        {
            HttpResponseMessage response = await adminClient.PostAsync("/api/v1/tenants",
                JsonHelper.ToJsonContent(new { Name = label + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) })).ConfigureAwait(false);
            return await JsonHelper.DeserializeAsync<TenantMetadata>(response).ConfigureAwait(false);
        }

        private static async Task<HttpClient> CreateUserClientAsync(E2EServerFixture fx, string tenantId, string label, bool tenantAdmin)
        {
            HttpResponseMessage userResp = await fx.AuthClient.PostAsync("/api/v1/users",
                JsonHelper.ToJsonContent(new
                {
                    TenantId = tenantId,
                    Email = label + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + "@fa.armada",
                    PasswordSha256 = UserMaster.ComputePasswordHash("testpass"),
                    IsTenantAdmin = tenantAdmin
                })).ConfigureAwait(false);
            UserMaster user = await JsonHelper.DeserializeAsync<UserMaster>(userResp).ConfigureAwait(false);

            HttpResponseMessage credResp = await fx.AuthClient.PostAsync("/api/v1/credentials",
                JsonHelper.ToJsonContent(new { TenantId = tenantId, UserId = user.Id, Name = label + "-cred" })).ConfigureAwait(false);
            Credential cred = await JsonHelper.DeserializeAsync<Credential>(credResp).ConfigureAwait(false);

            HttpClient client = new HttpClient();
            client.BaseAddress = new Uri(fx.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cred.BearerToken);
            return client;
        }

        private static async Task<string> CreateVesselAsync(HttpClient client, string label)
        {
            HttpResponseMessage fleetResp = await client.PostAsync("/api/v1/fleets",
                JsonHelper.ToJsonContent(new { Name = label + "-fleet-" + Guid.NewGuid().ToString("N").Substring(0, 6) })).ConfigureAwait(false);
            AssertStatusCode(HttpStatusCode.Created, fleetResp, "fleet create");
            Fleet fleet = await JsonHelper.DeserializeAsync<Fleet>(fleetResp).ConfigureAwait(false);

            HttpResponseMessage vesselResp = await client.PostAsync("/api/v1/vessels",
                JsonHelper.ToJsonContent(new
                {
                    Name = label + "-" + Guid.NewGuid().ToString("N").Substring(0, 6),
                    FleetId = fleet.Id,
                    RepoUrl = TestRepoHelper.GetLocalBareRepoUrl()
                })).ConfigureAwait(false);
            AssertStatusCode(HttpStatusCode.Created, vesselResp, "vessel create");
            Vessel vessel = await JsonHelper.DeserializeAsync<Vessel>(vesselResp).ConfigureAwait(false);
            return vessel.Id;
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

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
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// End-to-end descriptors for request-history capture, scoping, summaries, and delete flows,
    /// ported 1:1 from the retired automated RequestHistoryTests suite. Cases run sequentially
    /// against the shared e2e server fixture and carry tenant/user/credential/client identifiers and
    /// captured trace markers across cases as suite instance state, mirroring the legacy shared
    /// fields.
    /// </summary>
    public sealed class RequestHistorySuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "E2E.RequestHistory";

        private string? _TenantAId;
        private string? _TenantAAdminUserId;
        private string? _TenantAAdminCredentialId;
        private HttpClient? _TenantAAdminClient;

        private string? _TenantAUserId;
        private string? _TenantAUserCredentialId;
        private HttpClient? _TenantAUserClient;

        private string? _TenantBId;
        private string? _TenantBAdminUserId;
        private string? _TenantBAdminCredentialId;
        private HttpClient? _TenantBAdminClient;

        private string? _TenantAdminTrace;
        private string? _TenantUserTrace;
        private string? _OtherTenantTrace;
        private string? _AuthFailureTrace;
        private string? _OtherTenantEntryId;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Request History Routes suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("setup_create_tenant_a_admin", "Setup_CreateTenantAAdmin", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient adminClient = fx.AuthClient;
                string baseUrl = fx.BaseUrl;

                _TenantAId = await CreateTenantAsync(adminClient, "tenant-a").ConfigureAwait(false);
                UserCredentialResult tenantAAdmin =
                    await CreateUserWithCredentialAsync(adminClient, _TenantAId, "tenant-a-admin", true).ConfigureAwait(false);

                _TenantAAdminUserId = tenantAAdmin.UserId;
                _TenantAAdminCredentialId = tenantAAdmin.CredentialId;
                _TenantAAdminClient = CreateBearerClient(baseUrl, tenantAAdmin.BearerToken);
            }));

            cases.Add(CaseAsync("setup_create_tenant_a_user", "Setup_CreateTenantAUser", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient adminClient = fx.AuthClient;
                string baseUrl = fx.BaseUrl;

                AssertNotNull(_TenantAId, "Tenant A ID");
                UserCredentialResult tenantAUser =
                    await CreateUserWithCredentialAsync(adminClient, _TenantAId!, "tenant-a-user", false).ConfigureAwait(false);

                _TenantAUserId = tenantAUser.UserId;
                _TenantAUserCredentialId = tenantAUser.CredentialId;
                _TenantAUserClient = CreateBearerClient(baseUrl, tenantAUser.BearerToken);
            }));

            cases.Add(CaseAsync("setup_create_tenant_b_admin", "Setup_CreateTenantBAdmin", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient adminClient = fx.AuthClient;
                string baseUrl = fx.BaseUrl;

                _TenantBId = await CreateTenantAsync(adminClient, "tenant-b").ConfigureAwait(false);
                UserCredentialResult tenantBAdmin =
                    await CreateUserWithCredentialAsync(adminClient, _TenantBId, "tenant-b-admin", true).ConfigureAwait(false);

                _TenantBAdminUserId = tenantBAdmin.UserId;
                _TenantBAdminCredentialId = tenantBAdmin.CredentialId;
                _TenantBAdminClient = CreateBearerClient(baseUrl, tenantBAdmin.BearerToken);
            }));

            cases.Add(CaseAsync("request_history_list_without_auth_returns_401", "RequestHistory_ListWithoutAuth_Returns401", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient unauthClient = fx.UnauthClient;

                HttpResponseMessage response = await unauthClient.GetAsync("/api/v1/request-history").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.Unauthorized, response.StatusCode);
            }));

            cases.Add(CaseAsync("request_history_captures_status_request_and_redacts_authorization_header", "RequestHistory_CapturesStatusRequest_AndRedactsAuthorizationHeader", TestTags.Positive, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                _TenantAdminTrace = "tenant-admin-" + Guid.NewGuid().ToString("N").Substring(0, 10);
                await InvokeStatusAsync(_TenantAAdminClient!, _TenantAdminTrace).ConfigureAwait(false);

                RequestHistoryEntry? entry = await FindEntryByTraceAsync(
                    _TenantAAdminClient!,
                    "/api/v1/status",
                    _TenantAdminTrace,
                    "GET").ConfigureAwait(false);

                AssertNotNull(entry, "Captured tenant-admin request");
                AssertEqual(_TenantAId, entry!.TenantId);
                AssertEqual(_TenantAAdminUserId, entry.UserId);
                AssertEqual(_TenantAAdminCredentialId, entry.CredentialId);
                AssertEqual("GET", entry.Method);
                AssertEqual("/api/v1/status", entry.Route);
                AssertEqual(200, entry.StatusCode);

                RequestHistoryRecord record = await ReadEntryAsync(_TenantAAdminClient!, entry.Id).ConfigureAwait(false);
                AssertNotNull(record.Detail, "Request detail");
                AssertNotNull(record.Detail!.RequestHeadersJson, "Request headers json");
                AssertNotNull(record.Detail.QueryParamsJson, "Query params json");

                Dictionary<string, string?> headers =
                    JsonHelper.Deserialize<Dictionary<string, string?>>(record.Detail.RequestHeadersJson!);
                Dictionary<string, string?> query =
                    JsonHelper.Deserialize<Dictionary<string, string?>>(record.Detail.QueryParamsJson!);

                AssertEqual("[REDACTED]", headers["Authorization"], "Authorization header redaction");
                AssertEqual(_TenantAdminTrace, query["trace"], "Trace query param");
            }));

            cases.Add(CaseAsync("request_history_captures_authenticate_failure_and_redacts_body_secrets", "RequestHistory_CapturesAuthenticateFailure_AndRedactsBodySecrets", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient adminClient = fx.AuthClient;
                HttpClient unauthClient = fx.UnauthClient;

                string password = "super-secret-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                _AuthFailureTrace = "auth-failure-" + Guid.NewGuid().ToString("N").Substring(0, 10);

                await InvokeAuthenticateFailureAsync(unauthClient, _AuthFailureTrace, password).ConfigureAwait(false);

                RequestHistoryEntry? entry = await FindEntryByTraceAsync(
                    adminClient,
                    "/api/v1/authenticate",
                    _AuthFailureTrace,
                    "POST",
                    401).ConfigureAwait(false);

                AssertNotNull(entry, "Captured authenticate failure");
                RequestHistoryRecord record = await ReadEntryAsync(adminClient, entry!.Id).ConfigureAwait(false);
                AssertNotNull(record.Detail, "Authenticate detail");
                AssertNotNull(record.Detail!.RequestBodyText, "Authenticate request body");
                AssertContains("[REDACTED]", record.Detail.RequestBodyText!);
                AssertFalse(record.Detail.RequestBodyText!.Contains(password, StringComparison.Ordinal), "Secret should be redacted");
            }));

            cases.Add(CaseAsync("request_history_summary_returns_bucketed_counts", "RequestHistory_Summary_ReturnsBucketedCounts", TestTags.Positive, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                string fromUtc = DateTime.UtcNow.AddMinutes(-10).ToString("o");
                string toUtc = DateTime.UtcNow.AddMinutes(10).ToString("o");

                HttpResponseMessage response = await _TenantAAdminClient!.GetAsync(
                    "/api/v1/request-history/summary?route=/api/v1/status&fromUtc="
                    + Uri.EscapeDataString(fromUtc)
                    + "&toUtc=" + Uri.EscapeDataString(toUtc)
                    + "&bucketMinutes=5").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                RequestHistorySummaryResult summary =
                    await JsonHelper.DeserializeAsync<RequestHistorySummaryResult>(response).ConfigureAwait(false);

                AssertTrue(summary.TotalCount >= 1, "Summary total count");
                AssertEqual(5, summary.BucketMinutes);
                AssertTrue(summary.Buckets.Count >= 1, "Summary buckets");
                AssertTrue(summary.Buckets.Any(b => b.TotalCount > 0), "At least one populated bucket");
            }));

            cases.Add(CaseAsync("request_history_captures_release_and_history_routes", "RequestHistory_CapturesReleaseAndHistoryRoutes", TestTags.Positive, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                string releaseTrace = "release-route-" + Guid.NewGuid().ToString("N").Substring(0, 10);
                string historyTrace = "history-route-" + Guid.NewGuid().ToString("N").Substring(0, 10);

                HttpResponseMessage releaseResponse = await _TenantAAdminClient!.GetAsync(
                    "/api/v1/releases?trace=" + Uri.EscapeDataString(releaseTrace)).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, releaseResponse.StatusCode);

                HttpResponseMessage historyResponse = await _TenantAAdminClient.GetAsync(
                    "/api/v1/history?trace=" + Uri.EscapeDataString(historyTrace)).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, historyResponse.StatusCode);

                RequestHistoryEntry? releaseEntry = await FindEntryByTraceAsync(
                    _TenantAAdminClient,
                    "/api/v1/releases",
                    releaseTrace,
                    "GET").ConfigureAwait(false);
                RequestHistoryEntry? historyEntry = await FindEntryByTraceAsync(
                    _TenantAAdminClient,
                    "/api/v1/history",
                    historyTrace,
                    "GET").ConfigureAwait(false);

                AssertNotNull(releaseEntry, "Captured releases route entry");
                AssertNotNull(historyEntry, "Captured history route entry");
            }));

            cases.Add(CaseAsync("request_history_captures_backlog_and_refinement_routes", "RequestHistory_CapturesBacklogAndRefinementRoutes", TestTags.Positive, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                string backlogTrace = "backlog-route-" + Guid.NewGuid().ToString("N").Substring(0, 10);
                string refinementTrace = "refinement-route-" + Guid.NewGuid().ToString("N").Substring(0, 10);
                string objectiveId = String.Empty;
                string captainId = String.Empty;
                string sessionId = String.Empty;

                try
                {
                    HttpResponseMessage backlogResponse = await _TenantAAdminClient!.PostAsync(
                        "/api/v1/backlog?trace=" + Uri.EscapeDataString(backlogTrace),
                        JsonHelper.ToJsonContent(new
                        {
                            Title = "RequestHistory Backlog",
                            Description = "Track backlog route capture."
                        })).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.Created, backlogResponse.StatusCode);
                    Objective objective = await JsonHelper.DeserializeAsync<Objective>(backlogResponse).ConfigureAwait(false);
                    objectiveId = objective.Id;

                    HttpResponseMessage captainResponse = await _TenantAAdminClient.PostAsync(
                        "/api/v1/captains",
                        JsonHelper.ToJsonContent(new
                        {
                            Name = "RequestHistoryRefinement-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                            Runtime = "ClaudeCode"
                        })).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.Created, captainResponse.StatusCode);
                    Captain captain = await JsonHelper.DeserializeAsync<Captain>(captainResponse).ConfigureAwait(false);
                    captainId = captain.Id;

                    HttpResponseMessage refinementResponse = await _TenantAAdminClient.PostAsync(
                        "/api/v1/backlog/" + objectiveId + "/refinement-sessions?trace=" + Uri.EscapeDataString(refinementTrace),
                        JsonHelper.ToJsonContent(new
                        {
                            CaptainId = captainId,
                            Title = "Request history refinement"
                        })).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.Created, refinementResponse.StatusCode);
                    ObjectiveRefinementSessionDetail detail = await JsonHelper.DeserializeAsync<ObjectiveRefinementSessionDetail>(refinementResponse).ConfigureAwait(false);
                    sessionId = detail.Session.Id;

                    RequestHistoryEntry? backlogEntry = await FindEntryByTraceAsync(
                        _TenantAAdminClient,
                        "/api/v1/backlog",
                        backlogTrace,
                        "POST").ConfigureAwait(false);
                    RequestHistoryEntry? refinementEntry = await FindEntryByTraceAsync(
                        _TenantAAdminClient,
                        "/api/v1/backlog/" + objectiveId + "/refinement-sessions",
                        refinementTrace,
                        "POST").ConfigureAwait(false);

                    AssertNotNull(backlogEntry, "Captured backlog-create request");
                    AssertNotNull(refinementEntry, "Captured backlog-refinement request");

                    RequestHistoryRecord record = await ReadEntryAsync(_TenantAAdminClient, refinementEntry!.Id).ConfigureAwait(false);
                    AssertNotNull(record.Detail, "Backlog-refinement detail");
                    AssertNotNull(record.Detail!.RequestHeadersJson, "Backlog-refinement request headers");
                    AssertNotNull(record.Detail.RequestBodyText, "Backlog-refinement request body");

                    Dictionary<string, string?> headers =
                        JsonHelper.Deserialize<Dictionary<string, string?>>(record.Detail.RequestHeadersJson!);
                    AssertEqual("[REDACTED]", headers["Authorization"], "Authorization header redaction");
                    AssertEqual(captainId, JsonShape.TopLevelProperty(record.Detail.RequestBodyText!, "CaptainId")?.ScalarText, "captured body keeps the CaptainId");
                }
                finally
                {
                    if (!String.IsNullOrWhiteSpace(sessionId))
                        await _TenantAAdminClient!.DeleteAsync("/api/v1/objective-refinement-sessions/" + sessionId).ConfigureAwait(false);
                    if (!String.IsNullOrWhiteSpace(objectiveId))
                        await _TenantAAdminClient!.DeleteAsync("/api/v1/backlog/" + objectiveId).ConfigureAwait(false);
                    if (!String.IsNullOrWhiteSpace(captainId))
                        await _TenantAAdminClient!.DeleteAsync("/api/v1/captains/" + captainId).ConfigureAwait(false);
                }
            }));

            cases.Add(CaseAsync("request_history_redacts_github_token_override_in_vessel_payload", "RequestHistory_RedactsGitHubTokenOverrideInVesselPayload", TestTags.Positive, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                string fleetName = "rqh-github-fleet-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                HttpResponseMessage fleetResponse = await _TenantAAdminClient!.PostAsync(
                    "/api/v1/fleets",
                    JsonHelper.ToJsonContent(new
                    {
                        Name = fleetName
                    })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.Created, fleetResponse.StatusCode);
                Fleet fleet = await JsonHelper.DeserializeAsync<Fleet>(fleetResponse).ConfigureAwait(false);

                string trace = "vessel-github-token-" + Guid.NewGuid().ToString("N").Substring(0, 10);
                string token = "ghp_request_history_" + Guid.NewGuid().ToString("N").Substring(0, 10);
                HttpResponseMessage vesselResponse = await _TenantAAdminClient.PostAsync(
                    "/api/v1/vessels?trace=" + Uri.EscapeDataString(trace),
                    JsonHelper.ToJsonContent(new
                    {
                        Name = "RequestHistoryGitHubOverride",
                        FleetId = fleet.Id,
                        RepoUrl = "https://github.com/test/request-history-github-override",
                        GitHubTokenOverride = token
                    })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.Created, vesselResponse.StatusCode);

                RequestHistoryEntry? entry = await FindEntryByTraceAsync(
                    _TenantAAdminClient,
                    "/api/v1/vessels",
                    trace,
                    "POST").ConfigureAwait(false);
                AssertNotNull(entry, "Captured vessel-create request");

                RequestHistoryRecord record = await ReadEntryAsync(_TenantAAdminClient, entry!.Id).ConfigureAwait(false);
                AssertNotNull(record.Detail, "Vessel-create detail");
                AssertNotNull(record.Detail!.RequestBodyText, "Vessel-create request body");
                AssertContains("[REDACTED]", record.Detail.RequestBodyText!);
                AssertFalse(record.Detail.RequestBodyText!.Contains(token, StringComparison.Ordinal), "GitHub token override should be redacted");
            }));

            cases.Add(CaseAsync("request_history_scope_regular_user_only_sees_own_entries", "RequestHistory_Scope_RegularUser_OnlySeesOwnEntries", TestTags.Positive, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                _TenantUserTrace = "tenant-user-" + Guid.NewGuid().ToString("N").Substring(0, 10);
                _OtherTenantTrace = "other-tenant-" + Guid.NewGuid().ToString("N").Substring(0, 10);

                await InvokeStatusAsync(_TenantAUserClient!, _TenantUserTrace).ConfigureAwait(false);
                await InvokeStatusAsync(_TenantBAdminClient!, _OtherTenantTrace).ConfigureAwait(false);

                RequestHistoryEntry? otherTenantEntry = await FindEntryByTraceAsync(
                    _TenantBAdminClient!,
                    "/api/v1/status",
                    _OtherTenantTrace,
                    "GET").ConfigureAwait(false);
                AssertNotNull(otherTenantEntry, "Other-tenant entry");
                _OtherTenantEntryId = otherTenantEntry!.Id;

                HttpResponseMessage response = await _TenantAUserClient!.GetAsync(
                    "/api/v1/request-history?route=/api/v1/status&pageSize=250").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<RequestHistoryEntry> result =
                    await JsonHelper.DeserializeAsync<EnumerationResult<RequestHistoryEntry>>(response).ConfigureAwait(false);

                AssertTrue(result.Objects.Any(e => e.QueryString != null && e.QueryString.Contains(_TenantUserTrace!, StringComparison.Ordinal)),
                    "Regular user should see own request");
                AssertFalse(result.Objects.Any(e => e.QueryString != null && e.QueryString.Contains(_TenantAdminTrace!, StringComparison.Ordinal)),
                    "Regular user should not see tenant-admin sibling request");
                AssertFalse(result.Objects.Any(e => e.QueryString != null && e.QueryString.Contains(_OtherTenantTrace!, StringComparison.Ordinal)),
                    "Regular user should not see other-tenant request");
            }));

            cases.Add(CaseAsync("request_history_scope_tenant_admin_sees_tenant_not_other_tenant", "RequestHistory_Scope_TenantAdmin_SeesTenantNotOtherTenant", TestTags.Positive, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                HttpResponseMessage response = await _TenantAAdminClient!.GetAsync(
                    "/api/v1/request-history?route=/api/v1/status&pageSize=250").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<RequestHistoryEntry> result =
                    await JsonHelper.DeserializeAsync<EnumerationResult<RequestHistoryEntry>>(response).ConfigureAwait(false);

                AssertTrue(result.Objects.Any(e => e.QueryString != null && e.QueryString.Contains(_TenantAdminTrace!, StringComparison.Ordinal)),
                    "Tenant admin should see own request");
                AssertTrue(result.Objects.Any(e => e.QueryString != null && e.QueryString.Contains(_TenantUserTrace!, StringComparison.Ordinal)),
                    "Tenant admin should see same-tenant user request");
                AssertFalse(result.Objects.Any(e => e.QueryString != null && e.QueryString.Contains(_OtherTenantTrace!, StringComparison.Ordinal)),
                    "Tenant admin should not see other-tenant request");
            }));

            cases.Add(CaseAsync("request_history_scope_tenant_admin_cannot_read_other_tenant_entry", "RequestHistory_Scope_TenantAdmin_CannotReadOtherTenantEntry", TestTags.Negative, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                HttpResponseMessage response = await _TenantAAdminClient!.GetAsync("/api/v1/request-history/" + _OtherTenantEntryId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NotFound, response.StatusCode);
            }));

            cases.Add(CaseAsync("request_history_scope_global_admin_can_filter_by_tenant", "RequestHistory_Scope_GlobalAdmin_CanFilterByTenant", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient adminClient = fx.AuthClient;

                HttpResponseMessage response = await adminClient.GetAsync(
                    "/api/v1/request-history?route=/api/v1/status&tenantId=" + Uri.EscapeDataString(_TenantAId!) + "&pageSize=250").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<RequestHistoryEntry> result =
                    await JsonHelper.DeserializeAsync<EnumerationResult<RequestHistoryEntry>>(response).ConfigureAwait(false);

                AssertTrue(result.Objects.Count >= 2, "Expected tenant-A request history");
                AssertTrue(result.Objects.All(e => e.TenantId == _TenantAId), "Admin tenant filter should constrain rows");
            }));

            cases.Add(CaseAsync("request_history_delete_single_removes_entry", "RequestHistory_DeleteSingle_RemovesEntry", TestTags.Positive, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                string trace = "delete-single-" + Guid.NewGuid().ToString("N").Substring(0, 10);
                await InvokeStatusAsync(_TenantAAdminClient!, trace).ConfigureAwait(false);

                RequestHistoryEntry? entry = await FindEntryByTraceAsync(
                    _TenantAAdminClient!,
                    "/api/v1/status",
                    trace,
                    "GET").ConfigureAwait(false);
                AssertNotNull(entry, "Delete-single entry");

                HttpResponseMessage deleteResponse = await _TenantAAdminClient!.DeleteAsync("/api/v1/request-history/" + entry!.Id).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NoContent, deleteResponse.StatusCode);

                HttpResponseMessage readResponse = await _TenantAAdminClient.GetAsync("/api/v1/request-history/" + entry.Id).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NotFound, readResponse.StatusCode);
            }));

            cases.Add(CaseAsync("request_history_delete_multiple_removes_entries_and_skips_unknown", "RequestHistory_DeleteMultiple_RemovesEntries_AndSkipsUnknown", TestTags.Positive, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                string traceOne = "delete-multi-a-" + Guid.NewGuid().ToString("N").Substring(0, 10);
                string traceTwo = "delete-multi-b-" + Guid.NewGuid().ToString("N").Substring(0, 10);

                await InvokeStatusAsync(_TenantAAdminClient!, traceOne).ConfigureAwait(false);
                await InvokeStatusAsync(_TenantAAdminClient!, traceTwo).ConfigureAwait(false);

                RequestHistoryEntry? entryOne = await FindEntryByTraceAsync(_TenantAAdminClient!, "/api/v1/status", traceOne, "GET").ConfigureAwait(false);
                RequestHistoryEntry? entryTwo = await FindEntryByTraceAsync(_TenantAAdminClient!, "/api/v1/status", traceTwo, "GET").ConfigureAwait(false);
                AssertNotNull(entryOne, "Delete-multiple entry one");
                AssertNotNull(entryTwo, "Delete-multiple entry two");

                HttpResponseMessage response = await _TenantAAdminClient!.PostAsync(
                    "/api/v1/request-history/delete/multiple",
                    JsonHelper.ToJsonContent(new
                    {
                        Ids = new[] { entryOne!.Id, entryTwo!.Id, "req_missing" }
                    })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                DeleteMultipleResult result = await JsonHelper.DeserializeAsync<DeleteMultipleResult>(response).ConfigureAwait(false);
                AssertEqual(2, result.Deleted);
                AssertEqual(1, result.Skipped.Count);

                HttpResponseMessage readOne = await _TenantAAdminClient.GetAsync("/api/v1/request-history/" + entryOne.Id).ConfigureAwait(false);
                HttpResponseMessage readTwo = await _TenantAAdminClient.GetAsync("/api/v1/request-history/" + entryTwo.Id).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NotFound, readOne.StatusCode);
                AssertEqual(HttpStatusCode.NotFound, readTwo.StatusCode);
            }));

            cases.Add(CaseAsync("request_history_delete_by_filter_removes_scoped_entries", "RequestHistory_DeleteByFilter_RemovesScopedEntries", TestTags.Positive, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                string traceOne = "delete-filter-a-" + Guid.NewGuid().ToString("N").Substring(0, 10);
                string traceTwo = "delete-filter-b-" + Guid.NewGuid().ToString("N").Substring(0, 10);
                string fromUtc = DateTime.UtcNow.AddMinutes(-1).ToString("o");

                await InvokeStatusAsync(_TenantAUserClient!, traceOne).ConfigureAwait(false);
                await InvokeStatusAsync(_TenantAUserClient!, traceTwo).ConfigureAwait(false);
                string toUtc = DateTime.UtcNow.AddMinutes(1).ToString("o");

                HttpResponseMessage response = await _TenantAAdminClient!.PostAsync(
                    "/api/v1/request-history/delete/by-filter",
                    JsonHelper.ToJsonContent(new
                    {
                        UserId = _TenantAUserId,
                        Route = "/api/v1/status",
                        FromUtc = fromUtc,
                        ToUtc = toUtc
                    })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                DeleteMultipleResult result = await JsonHelper.DeserializeAsync<DeleteMultipleResult>(response).ConfigureAwait(false);
                AssertTrue(result.Deleted >= 2, "Expected at least the two scoped user requests to be deleted");

                HttpResponseMessage listResponse = await _TenantAUserClient!.GetAsync(
                    "/api/v1/request-history?route=/api/v1/status&pageSize=250").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, listResponse.StatusCode);

                EnumerationResult<RequestHistoryEntry> remaining =
                    await JsonHelper.DeserializeAsync<EnumerationResult<RequestHistoryEntry>>(listResponse).ConfigureAwait(false);

                AssertFalse(remaining.Objects.Any(e => e.QueryString != null && e.QueryString.Contains(traceOne, StringComparison.Ordinal)),
                    "Filtered delete should remove first scoped entry");
                AssertFalse(remaining.Objects.Any(e => e.QueryString != null && e.QueryString.Contains(traceTwo, StringComparison.Ordinal)),
                    "Filtered delete should remove second scoped entry");
            }));

            cases.Add(CaseAsync("cleanup_delete_request_history_tenants", "Cleanup_DeleteRequestHistoryTenants", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient adminClient = fx.AuthClient;

                _TenantAAdminClient?.Dispose();
                _TenantAUserClient?.Dispose();
                _TenantBAdminClient?.Dispose();

                if (_TenantAUserCredentialId != null)
                    await adminClient.DeleteAsync("/api/v1/credentials/" + _TenantAUserCredentialId).ConfigureAwait(false);
                if (_TenantAAdminCredentialId != null)
                    await adminClient.DeleteAsync("/api/v1/credentials/" + _TenantAAdminCredentialId).ConfigureAwait(false);
                if (_TenantBAdminCredentialId != null)
                    await adminClient.DeleteAsync("/api/v1/credentials/" + _TenantBAdminCredentialId).ConfigureAwait(false);

                if (_TenantAUserId != null)
                    await adminClient.DeleteAsync("/api/v1/users/" + _TenantAUserId).ConfigureAwait(false);
                if (_TenantAAdminUserId != null)
                    await adminClient.DeleteAsync("/api/v1/users/" + _TenantAAdminUserId).ConfigureAwait(false);
                if (_TenantBAdminUserId != null)
                    await adminClient.DeleteAsync("/api/v1/users/" + _TenantBAdminUserId).ConfigureAwait(false);

                if (_TenantAId != null)
                    await adminClient.DeleteAsync("/api/v1/tenants/" + _TenantAId).ConfigureAwait(false);
                if (_TenantBId != null)
                    await adminClient.DeleteAsync("/api/v1/tenants/" + _TenantBId).ConfigureAwait(false);
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Request History Routes",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task<string> CreateTenantAsync(HttpClient adminClient, string label)
        {
            HttpResponseMessage response = await adminClient.PostAsync("/api/v1/tenants",
                JsonHelper.ToJsonContent(new
                {
                    Name = "rqh-" + label + "-" + Guid.NewGuid().ToString("N").Substring(0, 8)
                })).ConfigureAwait(false);
            AssertEqual(HttpStatusCode.Created, response.StatusCode);

            TenantMetadata tenant = await JsonHelper.DeserializeAsync<TenantMetadata>(response).ConfigureAwait(false);
            return tenant.Id;
        }

        private static async Task<UserCredentialResult> CreateUserWithCredentialAsync(
            HttpClient adminClient,
            string tenantId,
            string label,
            bool isTenantAdmin)
        {
            string email = label + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + "@request-history.armada";
            HttpResponseMessage userResponse = await adminClient.PostAsync("/api/v1/users",
                JsonHelper.ToJsonContent(new
                {
                    TenantId = tenantId,
                    Email = email,
                    PasswordSha256 = UserMaster.ComputePasswordHash("testpass"),
                    IsTenantAdmin = isTenantAdmin
                })).ConfigureAwait(false);
            AssertEqual(HttpStatusCode.Created, userResponse.StatusCode);

            UserMaster user = await JsonHelper.DeserializeAsync<UserMaster>(userResponse).ConfigureAwait(false);

            HttpResponseMessage credentialResponse = await adminClient.PostAsync("/api/v1/credentials",
                JsonHelper.ToJsonContent(new
                {
                    TenantId = tenantId,
                    UserId = user.Id,
                    Name = label + "-credential"
                })).ConfigureAwait(false);
            AssertEqual(HttpStatusCode.Created, credentialResponse.StatusCode);

            Credential credential = await JsonHelper.DeserializeAsync<Credential>(credentialResponse).ConfigureAwait(false);
            AssertNotNull(credential.BearerToken, "Bearer token");
            return new UserCredentialResult
            {
                UserId = user.Id,
                CredentialId = credential.Id,
                BearerToken = credential.BearerToken
            };
        }

        private static HttpClient CreateBearerClient(string baseUrl, string bearerToken)
        {
            HttpClient client = new HttpClient();
            client.BaseAddress = new Uri(baseUrl);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
            return client;
        }

        private static async Task InvokeStatusAsync(HttpClient client, string trace)
        {
            HttpResponseMessage response = await client.GetAsync("/api/v1/status?trace=" + Uri.EscapeDataString(trace)).ConfigureAwait(false);
            AssertEqual(HttpStatusCode.OK, response.StatusCode);
        }

        private static async Task InvokeAuthenticateFailureAsync(HttpClient unauthClient, string trace, string password)
        {
            HttpResponseMessage response = await unauthClient.PostAsync(
                "/api/v1/authenticate?trace=" + Uri.EscapeDataString(trace),
                JsonHelper.ToJsonContent(new
                {
                    TenantId = "default",
                    Email = "admin@armada",
                    Password = password
                })).ConfigureAwait(false);
            AssertEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        private static async Task<RequestHistoryEntry?> FindEntryByTraceAsync(
            HttpClient client,
            string route,
            string trace,
            string method,
            int? statusCode = null)
        {
            EnumerationResult<RequestHistoryEntry>? lastResult = null;

            for (int attempt = 0; attempt < 15; attempt++)
            {
                string url = "/api/v1/request-history?route=" + route
                    + "&pageSize=250";

                if (statusCode.HasValue)
                    url += "&statusCode=" + statusCode.Value;

                HttpResponseMessage response = await client.GetAsync(url).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<RequestHistoryEntry> result =
                    await JsonHelper.DeserializeAsync<EnumerationResult<RequestHistoryEntry>>(response).ConfigureAwait(false);
                lastResult = result;

                RequestHistoryEntry? entry = result.Objects.FirstOrDefault(e =>
                    String.Equals(e.Method, method, StringComparison.OrdinalIgnoreCase)
                    && QueryString.Get(e.QueryString, "trace") == trace);

                if (entry != null) return entry;

                await Task.Delay(100).ConfigureAwait(false);
            }

            return null;
        }

        private static async Task<RequestHistoryRecord> ReadEntryAsync(HttpClient client, string id)
        {
            HttpResponseMessage response = await client.GetAsync("/api/v1/request-history/" + id).ConfigureAwait(false);
            AssertEqual(HttpStatusCode.OK, response.StatusCode);
            return await JsonHelper.DeserializeAsync<RequestHistoryRecord>(response).ConfigureAwait(false);
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

        #region Nested-Types

        private sealed class UserCredentialResult
        {
            public string UserId { get; set; } = String.Empty;

            public string CredentialId { get; set; } = String.Empty;

            public string BearerToken { get; set; } = String.Empty;
        }

        #endregion
    }
}

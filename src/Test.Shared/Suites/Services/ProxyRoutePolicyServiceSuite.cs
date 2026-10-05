namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Armada.Proxy.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="ProxyRoutePolicyService"/>: the central gate that decides which
    /// relayed dashboard requests may reach a remote Armada instance. Positive cases cover allowed
    /// read and login-bootstrap routes; negative cases cover null/non-API payloads, blocked
    /// high-risk operations, and blocked administrative writes.
    /// </summary>
    public sealed class ProxyRoutePolicyServiceSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Proxy Route Policy suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("try_authorize_allows_regular_dashboard_read_routes", "TryAuthorize AllowsRegularDashboardReadRoutes", TestTags.Positive, () =>
            {
                ProxyRoutePolicyService service = new ProxyRoutePolicyService();
                bool allowed = service.TryAuthorize(new RemoteTunnelHttpRelayRequest
                {
                    Method = "GET",
                    Path = "/api/v1/fleets"
                }, out int statusCode, out string? message);

                AssertTrue(allowed, message ?? "Fleet list relay should be allowed");
                AssertEqual(200, statusCode);
            }));

            cases.Add(Case("try_authorize_denies_high_risk_administrative_writes", "TryAuthorize DeniesHighRiskAdministrativeWrites", TestTags.Negative, () =>
            {
                ProxyRoutePolicyService service = new ProxyRoutePolicyService();

                bool settingsAllowed = service.TryAuthorize(new RemoteTunnelHttpRelayRequest
                {
                    Method = "PUT",
                    Path = "/api/v1/settings"
                }, out int settingsStatusCode, out string? settingsMessage);
                AssertFalse(settingsAllowed);
                AssertEqual(403, settingsStatusCode);
                AssertContains("blocked", settingsMessage ?? String.Empty, "Settings write should be blocked");

                bool restoreAllowed = service.TryAuthorize(new RemoteTunnelHttpRelayRequest
                {
                    Method = "POST",
                    Path = "/api/v1/restore"
                }, out int restoreStatusCode, out string? restoreMessage);
                AssertFalse(restoreAllowed);
                AssertEqual(403, restoreStatusCode);
                AssertContains("blocked", restoreMessage ?? String.Empty, "Restore should be blocked");
            }));

            cases.Add(Case("try_authorize_allows_remote_login_bootstrap_routes", "TryAuthorize AllowsRemoteLoginBootstrapRoutes", TestTags.Positive, () =>
            {
                ProxyRoutePolicyService service = new ProxyRoutePolicyService();

                bool tenantLookupAllowed = service.TryAuthorize(new RemoteTunnelHttpRelayRequest
                {
                    Method = "POST",
                    Path = "/api/v1/tenants/lookup"
                }, out int tenantLookupStatusCode, out string? tenantLookupMessage);
                AssertTrue(tenantLookupAllowed, tenantLookupMessage ?? "Tenant lookup should be allowed for remote login");
                AssertEqual(200, tenantLookupStatusCode);

                bool authenticateAllowed = service.TryAuthorize(new RemoteTunnelHttpRelayRequest
                {
                    Method = "POST",
                    Path = "/api/v1/authenticate"
                }, out int authenticateStatusCode, out string? authenticateMessage);
                AssertTrue(authenticateAllowed, authenticateMessage ?? "Authenticate should be allowed for remote login");
                AssertEqual(200, authenticateStatusCode);
            }));

            cases.Add(Case("try_authorize_rejects_null_request", "TryAuthorize RejectsNullRequest", TestTags.Negative, () =>
            {
                ProxyRoutePolicyService service = new ProxyRoutePolicyService();
                bool allowed = service.TryAuthorize(null, out int statusCode, out string? message);
                AssertFalse(allowed);
                AssertEqual(400, statusCode);
                AssertContains("required", message ?? String.Empty, "Null relay payload should be rejected");
            }));

            cases.Add(Case("try_authorize_rejects_non_api_path", "TryAuthorize RejectsNonApiPath", TestTags.Negative, () =>
            {
                ProxyRoutePolicyService service = new ProxyRoutePolicyService();
                bool allowed = service.TryAuthorize(new RemoteTunnelHttpRelayRequest
                {
                    Method = "GET",
                    Path = "/dashboard/index.html"
                }, out int statusCode, out string? message);
                AssertFalse(allowed);
                AssertEqual(400, statusCode);
                AssertContains("/api/v1/", message ?? String.Empty, "Only Armada API routes should be relayable");
            }));

            cases.Add(Case("try_authorize_blocks_shutdown_route", "TryAuthorize BlocksShutdownRoute", TestTags.Negative, () =>
            {
                ProxyRoutePolicyService service = new ProxyRoutePolicyService();
                bool allowed = service.TryAuthorize(new RemoteTunnelHttpRelayRequest
                {
                    Method = "POST",
                    Path = "/api/v1/status/shutdown"
                }, out int statusCode, out string? message);
                AssertFalse(allowed);
                AssertEqual(403, statusCode);
                AssertContains("blocked", message ?? String.Empty, "Shutdown should be blocked for remote access");
            }));

            cases.Add(Case("try_authorize_blocks_administrative_credential_writes", "TryAuthorize BlocksAdministrativeCredentialWrites", TestTags.Negative, () =>
            {
                ProxyRoutePolicyService service = new ProxyRoutePolicyService();
                bool allowed = service.TryAuthorize(new RemoteTunnelHttpRelayRequest
                {
                    Method = "PUT",
                    Path = "/api/v1/credentials/cred_abc"
                }, out int statusCode, out string? message);
                AssertFalse(allowed);
                AssertEqual(403, statusCode);
                AssertContains("administrative", message ?? String.Empty, "Credential writes should be blocked for remote access");
            }));

            cases.Add(Case("try_authorize_denies_non_canonical_spellings_of_blocked_routes", "TryAuthorize DeniesNonCanonicalSpellingsOfBlockedRoutes", TestTags.Negative, () =>
            {
                ProxyRoutePolicyService service = new ProxyRoutePolicyService();
                string[] blockedSpellings = new string[]
                {
                    "/api/v1/server/stop/",
                    "/api/v1/server//stop",
                    "//api/v1/settings",
                    "/api/v1/./status/factory-reset",
                    "/api/v1/status/../status/shutdown",
                    "/api/v1/%73ettings",
                    "/api/v1/%2e/status/shutdown",
                    "/api/v1/status%2fshutdown",
                    "/api/v1/status%2Fshutdown",
                    "/api/v1/status%5cshutdown",
                    "/api/v1/status\\shutdown",
                    "/api/v1/%2573ettings",
                    "/API/V1/RESTORE",
                    "/api/v1/restore;x=1",
                    "/api/v1/restore%00"
                };

                foreach (string path in blockedSpellings)
                {
                    bool allowed = service.TryAuthorize(new RemoteTunnelHttpRelayRequest
                    {
                        Method = "PUT",
                        Path = path
                    }, out int statusCode, out string? message);
                    AssertFalse(allowed, "non-canonical spelling '" + path + "' must not be allowed");
                    AssertTrue(statusCode == 400 || statusCode == 403, "'" + path + "' should be 400 or 403, was " + statusCode);
                }
            }));

            cases.Add(Case("try_authorize_rewrites_request_path_to_canonical_form", "TryAuthorize RewritesRequestPathToCanonicalForm", TestTags.Positive, () =>
            {
                ProxyRoutePolicyService service = new ProxyRoutePolicyService();
                RemoteTunnelHttpRelayRequest request = new RemoteTunnelHttpRelayRequest
                {
                    Method = "GET",
                    Path = "/api/v1//fleets/flt_abc/"
                };

                bool allowed = service.TryAuthorize(request, out int statusCode, out string? message);
                AssertTrue(allowed, message ?? "Collapsed fleet read should be allowed");
                AssertEqual(200, statusCode);
                AssertEqual("/api/v1/fleets/flt_abc", request.Path, "the relayed path must be the canonical path the policy evaluated");
            }));

            cases.Add(Case("try_authorize_segment_boundary_does_not_block_lookalike_routes", "TryAuthorize SegmentBoundaryOnAdministrativePrefixes", TestTags.Positive, () =>
            {
                ProxyRoutePolicyService service = new ProxyRoutePolicyService();
                bool allowed = service.TryAuthorize(new RemoteTunnelHttpRelayRequest
                {
                    Method = "GET",
                    Path = "/api/v1/settings"
                }, out int statusCode, out string? message);
                AssertTrue(allowed, message ?? "Settings read should be allowed");
                AssertEqual(200, statusCode);
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.ProxyRoutePolicyService",
                displayName: "Proxy Route Policy",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.ProxyRoutePolicyService",
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
                suiteId: "Services.ProxyRoutePolicyService",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion
    }
}

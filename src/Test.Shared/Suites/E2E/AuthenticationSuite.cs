namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// End-to-end descriptors for authentication, API key handling, and CORS headers, ported 1:1
    /// from the retired automated AuthenticationTests suite. Cases run against the shared e2e
    /// server fixture using its authenticated and unauthenticated clients, base URL, and API key.
    /// </summary>
    public sealed class AuthenticationSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "E2E.Authentication";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Authentication suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("valid_api_key_grants_access_to_protected_endpoint", "ValidApiKey_GrantsAccess_ToProtectedEndpoint", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/fleets").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);
            }));

            cases.Add(CaseAsync("valid_api_key_can_access_status", "ValidApiKey_CanAccessStatus", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/status").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);
            }));

            cases.Add(CaseAsync("valid_api_key_can_access_captains", "ValidApiKey_CanAccessCaptains", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/captains").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);
            }));

            cases.Add(CaseAsync("valid_api_key_can_access_missions", "ValidApiKey_CanAccessMissions", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/missions?pageSize=1").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);
            }));

            cases.Add(CaseAsync("valid_api_key_can_access_voyages", "ValidApiKey_CanAccessVoyages", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/voyages").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);
            }));

            cases.Add(CaseAsync("valid_api_key_can_access_signals", "ValidApiKey_CanAccessSignals", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/signals").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);
            }));

            cases.Add(CaseAsync("valid_api_key_can_access_vessels", "ValidApiKey_CanAccessVessels", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/vessels").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);
            }));

            cases.Add(CaseAsync("no_api_key_on_get_endpoint_returns_response", "NoApiKey_OnGetEndpoint_ReturnsResponse", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient unauthClient = fx.UnauthClient;

                HttpResponseMessage response = await unauthClient.GetAsync("/api/v1/fleets").ConfigureAwait(false);
                AssertNotNull(response);
            }));

            cases.Add(CaseAsync("no_api_key_on_status_returns_response", "NoApiKey_OnStatus_ReturnsResponse", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient unauthClient = fx.UnauthClient;

                HttpResponseMessage response = await unauthClient.GetAsync("/api/v1/status").ConfigureAwait(false);
                AssertNotNull(response);
            }));

            cases.Add(CaseAsync("no_api_key_on_captains_returns_response", "NoApiKey_OnCaptains_ReturnsResponse", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient unauthClient = fx.UnauthClient;

                HttpResponseMessage response = await unauthClient.GetAsync("/api/v1/captains").ConfigureAwait(false);
                AssertNotNull(response);
            }));

            cases.Add(CaseAsync("no_api_key_on_missions_returns_response", "NoApiKey_OnMissions_ReturnsResponse", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient unauthClient = fx.UnauthClient;

                HttpResponseMessage response = await unauthClient.GetAsync("/api/v1/missions?pageSize=1").ConfigureAwait(false);
                AssertNotNull(response);
            }));

            cases.Add(CaseAsync("no_api_key_on_voyages_returns_response", "NoApiKey_OnVoyages_ReturnsResponse", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient unauthClient = fx.UnauthClient;

                HttpResponseMessage response = await unauthClient.GetAsync("/api/v1/voyages").ConfigureAwait(false);
                AssertNotNull(response);
            }));

            cases.Add(CaseAsync("no_api_key_on_signals_returns_response", "NoApiKey_OnSignals_ReturnsResponse", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient unauthClient = fx.UnauthClient;

                HttpResponseMessage response = await unauthClient.GetAsync("/api/v1/signals").ConfigureAwait(false);
                AssertNotNull(response);
            }));

            cases.Add(CaseAsync("no_api_key_on_post_fleets_returns_response", "NoApiKey_OnPostFleets_ReturnsResponse", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient unauthClient = fx.UnauthClient;

                StringContent content = JsonHelper.ToJsonContent(new { Name = "UnauthFleet" });
                HttpResponseMessage response = await unauthClient.PostAsync("/api/v1/fleets", content).ConfigureAwait(false);
                AssertNotNull(response);
            }));

            cases.Add(CaseAsync("no_api_key_on_post_captains_returns_response", "NoApiKey_OnPostCaptains_ReturnsResponse", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient unauthClient = fx.UnauthClient;

                StringContent content = JsonHelper.ToJsonContent(new { Name = "UnauthCaptain" });
                HttpResponseMessage response = await unauthClient.PostAsync("/api/v1/captains", content).ConfigureAwait(false);
                AssertNotNull(response);
            }));

            cases.Add(CaseAsync("no_api_key_on_post_missions_returns_response", "NoApiKey_OnPostMissions_ReturnsResponse", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient unauthClient = fx.UnauthClient;

                StringContent content = JsonHelper.ToJsonContent(new { Title = "UnauthMission" });
                HttpResponseMessage response = await unauthClient.PostAsync("/api/v1/missions", content).ConfigureAwait(false);
                AssertNotNull(response);
            }));

            cases.Add(CaseAsync("wrong_api_key_on_get_endpoint_returns_response", "WrongApiKey_OnGetEndpoint_ReturnsResponse", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                string baseUrl = fx.BaseUrl;

                HttpClient wrongKeyClient = new HttpClient();
                wrongKeyClient.BaseAddress = new Uri(baseUrl);
                wrongKeyClient.DefaultRequestHeaders.Add("X-Api-Key", "wrong-key-value");

                HttpResponseMessage response = await wrongKeyClient.GetAsync("/api/v1/fleets").ConfigureAwait(false);
                AssertNotNull(response);

                wrongKeyClient.Dispose();
            }));

            cases.Add(CaseAsync("wrong_api_key_on_captains_returns_response", "WrongApiKey_OnCaptains_ReturnsResponse", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                string baseUrl = fx.BaseUrl;

                HttpClient wrongKeyClient = new HttpClient();
                wrongKeyClient.BaseAddress = new Uri(baseUrl);
                wrongKeyClient.DefaultRequestHeaders.Add("X-Api-Key", "definitely-not-the-right-key");

                HttpResponseMessage response = await wrongKeyClient.GetAsync("/api/v1/captains").ConfigureAwait(false);
                AssertNotNull(response);

                wrongKeyClient.Dispose();
            }));

            cases.Add(CaseAsync("wrong_api_key_on_missions_returns_response", "WrongApiKey_OnMissions_ReturnsResponse", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                string baseUrl = fx.BaseUrl;

                HttpClient wrongKeyClient = new HttpClient();
                wrongKeyClient.BaseAddress = new Uri(baseUrl);
                wrongKeyClient.DefaultRequestHeaders.Add("X-Api-Key", "nope-wrong");

                HttpResponseMessage response = await wrongKeyClient.GetAsync("/api/v1/missions?pageSize=1").ConfigureAwait(false);
                AssertNotNull(response);

                wrongKeyClient.Dispose();
            }));

            cases.Add(CaseAsync("wrong_api_key_on_status_returns_response", "WrongApiKey_OnStatus_ReturnsResponse", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                string baseUrl = fx.BaseUrl;

                HttpClient wrongKeyClient = new HttpClient();
                wrongKeyClient.BaseAddress = new Uri(baseUrl);
                wrongKeyClient.DefaultRequestHeaders.Add("X-Api-Key", "bad-key");

                HttpResponseMessage response = await wrongKeyClient.GetAsync("/api/v1/status").ConfigureAwait(false);
                AssertNotNull(response);

                wrongKeyClient.Dispose();
            }));

            cases.Add(CaseAsync("wrong_api_key_on_post_fleets_returns_response", "WrongApiKey_OnPostFleets_ReturnsResponse", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                string baseUrl = fx.BaseUrl;

                HttpClient wrongKeyClient = new HttpClient();
                wrongKeyClient.BaseAddress = new Uri(baseUrl);
                wrongKeyClient.DefaultRequestHeaders.Add("X-Api-Key", "invalid");

                StringContent content = JsonHelper.ToJsonContent(new { Name = "WrongKeyFleet" });
                HttpResponseMessage response = await wrongKeyClient.PostAsync("/api/v1/fleets", content).ConfigureAwait(false);
                AssertNotNull(response);

                wrongKeyClient.Dispose();
            }));

            cases.Add(CaseAsync("health_endpoint_accessible_without_key", "HealthEndpoint_AccessibleWithoutKey", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient unauthClient = fx.UnauthClient;

                HttpResponseMessage response = await unauthClient.GetAsync("/api/v1/status/health").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);
            }));

            cases.Add(CaseAsync("health_endpoint_accessible_with_wrong_key", "HealthEndpoint_AccessibleWithWrongKey", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                string baseUrl = fx.BaseUrl;

                HttpClient wrongKeyClient = new HttpClient();
                wrongKeyClient.BaseAddress = new Uri(baseUrl);
                wrongKeyClient.DefaultRequestHeaders.Add("X-Api-Key", "totally-invalid-key");

                HttpResponseMessage response = await wrongKeyClient.GetAsync("/api/v1/status/health").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                HealthResponse health = await JsonHelper.DeserializeAsync<HealthResponse>(response).ConfigureAwait(false);
                AssertEqual("healthy", health.Status);

                wrongKeyClient.Dispose();
            }));

            cases.Add(CaseAsync("health_endpoint_accessible_with_valid_key", "HealthEndpoint_AccessibleWithValidKey", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/status/health").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                HealthResponse health = await JsonHelper.DeserializeAsync<HealthResponse>(response).ConfigureAwait(false);
                AssertEqual("healthy", health.Status);
            }));

            cases.Add(CaseAsync("api_key_header_is_x_api_key", "ApiKeyHeader_IsXApiKey", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                string baseUrl = fx.BaseUrl;
                string apiKey = fx.ApiKey;

                HttpClient client = new HttpClient();
                client.BaseAddress = new Uri(baseUrl);
                client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

                HttpResponseMessage response = await client.GetAsync("/api/v1/fleets").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                client.Dispose();
            }));

            cases.Add(CaseAsync("api_key_header_case_insensitive_lower_case", "ApiKeyHeader_CaseInsensitive_LowerCase", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                string baseUrl = fx.BaseUrl;
                string apiKey = fx.ApiKey;

                HttpClient client = new HttpClient();
                client.BaseAddress = new Uri(baseUrl);
                client.DefaultRequestHeaders.Add("x-api-key", apiKey);

                HttpResponseMessage response = await client.GetAsync("/api/v1/fleets").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                client.Dispose();
            }));

            cases.Add(CaseAsync("cors_headers_present_in_response", "CorsHeaders_PresentInResponse", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/status").ConfigureAwait(false);

                Assert(
                    response.Headers.Contains("Access-Control-Allow-Origin") ||
                    response.Content.Headers.Contains("Access-Control-Allow-Origin"),
                    "Expected CORS Allow-Origin header in response");
            }));

            cases.Add(CaseAsync("cors_headers_allow_origin_is_wildcard", "CorsHeaders_AllowOriginIsWildcard", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/status").ConfigureAwait(false);

                string? origin = null;
                if (response.Headers.Contains("Access-Control-Allow-Origin"))
                {
                    origin = response.Headers.GetValues("Access-Control-Allow-Origin").FirstOrDefault();
                }
                else if (response.Content.Headers.Contains("Access-Control-Allow-Origin"))
                {
                    origin = response.Content.Headers.GetValues("Access-Control-Allow-Origin").FirstOrDefault();
                }

                AssertNotNull(origin);
                AssertEqual("*", origin);
            }));

            cases.Add(CaseAsync("cors_headers_allow_methods_present", "CorsHeaders_AllowMethodsPresent", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/status").ConfigureAwait(false);

                bool hasMethods =
                    response.Headers.Contains("Access-Control-Allow-Methods") ||
                    response.Content.Headers.Contains("Access-Control-Allow-Methods");

                Assert(hasMethods, "Expected CORS Allow-Methods header in response");
            }));

            cases.Add(CaseAsync("cors_headers_allow_headers_present", "CorsHeaders_AllowHeadersPresent", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/status").ConfigureAwait(false);

                bool hasHeaders =
                    response.Headers.Contains("Access-Control-Allow-Headers") ||
                    response.Content.Headers.Contains("Access-Control-Allow-Headers");

                Assert(hasHeaders, "Expected CORS Allow-Headers header in response");
            }));

            cases.Add(CaseAsync("cors_headers_allow_headers_includes_x_api_key", "CorsHeaders_AllowHeadersIncludesXApiKey", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/status").ConfigureAwait(false);

                string? allowHeaders = null;
                if (response.Headers.Contains("Access-Control-Allow-Headers"))
                {
                    allowHeaders = response.Headers.GetValues("Access-Control-Allow-Headers").FirstOrDefault();
                }
                else if (response.Content.Headers.Contains("Access-Control-Allow-Headers"))
                {
                    allowHeaders = response.Content.Headers.GetValues("Access-Control-Allow-Headers").FirstOrDefault();
                }

                AssertNotNull(allowHeaders);
                Assert(
                    allowHeaders == "*" || allowHeaders!.Split(',').Select(h => h.Trim()).Contains("X-Api-Key", StringComparer.OrdinalIgnoreCase),
                    "Expected Allow-Headers to be wildcard '*' or include 'X-Api-Key', but got: " + allowHeaders);
            }));

            cases.Add(CaseAsync("authenticated_client_can_perform_full_crud_cycle_fleet", "AuthenticatedClient_CanPerformFullCrudCycle_Fleet", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                // Create
                StringContent createContent = JsonHelper.ToJsonContent(new { Name = "CRUD Fleet" });
                HttpResponseMessage createResp = await authClient.PostAsync("/api/v1/fleets", createContent).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.Created, createResp.StatusCode);

                Fleet createdFleet = await JsonHelper.DeserializeAsync<Fleet>(createResp).ConfigureAwait(false);
                string fleetId = createdFleet.Id;

                // Read
                HttpResponseMessage readResp = await authClient.GetAsync("/api/v1/fleets/" + fleetId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, readResp.StatusCode);
                FleetDetailResponse fleetDetail = await JsonHelper.DeserializeAsync<FleetDetailResponse>(readResp).ConfigureAwait(false);
                AssertEqual("CRUD Fleet", fleetDetail.Fleet?.Name);

                // Update
                StringContent updateContent = JsonHelper.ToJsonContent(new { Name = "Updated CRUD Fleet", Description = "Updated" });
                HttpResponseMessage updateResp = await authClient.PutAsync("/api/v1/fleets/" + fleetId, updateContent).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, updateResp.StatusCode);
                Fleet updatedFleet = await JsonHelper.DeserializeAsync<Fleet>(updateResp).ConfigureAwait(false);
                AssertEqual("Updated CRUD Fleet", updatedFleet.Name);

                // Delete
                HttpResponseMessage deleteResp = await authClient.DeleteAsync("/api/v1/fleets/" + fleetId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NoContent, deleteResp.StatusCode);

                // Verify deleted
                HttpResponseMessage verifyResp = await authClient.GetAsync("/api/v1/fleets/" + fleetId).ConfigureAwait(false);
                await E2eApiErrorAssert.ExpectAsync(verifyResp, HttpStatusCode.NotFound, "Deleted fleet should return error on read").ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("authenticated_client_can_perform_full_crud_cycle_captain", "AuthenticatedClient_CanPerformFullCrudCycle_Captain", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                // Create
                StringContent createContent = JsonHelper.ToJsonContent(new { Name = "CRUD Captain", Runtime = "ClaudeCode" });
                HttpResponseMessage createResp = await authClient.PostAsync("/api/v1/captains", createContent).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.Created, createResp.StatusCode);

                Captain createdCaptain = await JsonHelper.DeserializeAsync<Captain>(createResp).ConfigureAwait(false);
                string captainId = createdCaptain.Id;

                // Read
                HttpResponseMessage readResp = await authClient.GetAsync("/api/v1/captains/" + captainId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, readResp.StatusCode);
                Captain readCaptain = await JsonHelper.DeserializeAsync<Captain>(readResp).ConfigureAwait(false);
                AssertEqual("CRUD Captain", readCaptain.Name);

                // Update
                StringContent updateContent = JsonHelper.ToJsonContent(new { Name = "Updated CRUD Captain" });
                HttpResponseMessage updateResp = await authClient.PutAsync("/api/v1/captains/" + captainId, updateContent).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, updateResp.StatusCode);

                // Delete
                HttpResponseMessage deleteResp = await authClient.DeleteAsync("/api/v1/captains/" + captainId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NoContent, deleteResp.StatusCode);

                // Verify deleted
                HttpResponseMessage verifyResp = await authClient.GetAsync("/api/v1/captains/" + captainId).ConfigureAwait(false);
                await E2eApiErrorAssert.ExpectAsync(verifyResp, HttpStatusCode.NotFound, "Deleted captain should return error on read").ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("empty_api_key_on_get_endpoint_returns_response", "EmptyApiKey_OnGetEndpoint_ReturnsResponse", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                string baseUrl = fx.BaseUrl;

                HttpClient emptyKeyClient = new HttpClient();
                emptyKeyClient.BaseAddress = new Uri(baseUrl);
                emptyKeyClient.DefaultRequestHeaders.Add("X-Api-Key", "");

                HttpResponseMessage response = await emptyKeyClient.GetAsync("/api/v1/fleets").ConfigureAwait(false);
                AssertNotNull(response);

                emptyKeyClient.Dispose();
            }));

            cases.Add(CaseAsync("multiple_endpoints_all_return_responses_without_auth", "MultipleEndpoints_AllReturnResponses_WithoutAuth", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient unauthClient = fx.UnauthClient;

                string[] endpoints = new string[]
                {
                    "/api/v1/fleets",
                    "/api/v1/captains",
                    "/api/v1/missions?pageSize=1",
                    "/api/v1/voyages",
                    "/api/v1/signals",
                    "/api/v1/vessels",
                    "/api/v1/status"
                };

                foreach (string endpoint in endpoints)
                {
                    HttpResponseMessage response = await unauthClient.GetAsync(endpoint).ConfigureAwait(false);
                    AssertNotNull(response);
                }
            }));

            cases.Add(CaseAsync("multiple_protected_endpoints_all_accessible_with_valid_key", "MultipleProtectedEndpoints_AllAccessibleWithValidKey", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                string[] endpoints = new string[]
                {
                    "/api/v1/fleets",
                    "/api/v1/captains",
                    "/api/v1/missions?pageSize=1",
                    "/api/v1/voyages",
                    "/api/v1/signals",
                    "/api/v1/vessels",
                    "/api/v1/status"
                };

                foreach (string endpoint in endpoints)
                {
                    HttpResponseMessage response = await authClient.GetAsync(endpoint).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.OK, response.StatusCode);
                }
            }));

            cases.Add(CaseAsync("api_key_is_unique_per_test_instance", "ApiKey_IsUniquePerTestInstance", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                string apiKey = fx.ApiKey;

                AssertStartsWith("test-key-", apiKey);
                Assert(apiKey.Length > 20, "API key should be sufficiently long");
            }));

            cases.Add(CaseAsync("unauth_client_post_endpoints_return_response", "UnauthClient_PostEndpoints_ReturnResponse", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient unauthClient = fx.UnauthClient;

                StringContent content = JsonHelper.ToJsonContent(new { Name = "UnauthTest" });

                HttpResponseMessage fleetResp = await unauthClient.PostAsync("/api/v1/fleets", content).ConfigureAwait(false);
                AssertNotNull(fleetResp);
            }));

            cases.Add(CaseAsync("unauth_client_delete_endpoints_return_response", "UnauthClient_DeleteEndpoints_ReturnResponse", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                HttpClient unauthClient = fx.UnauthClient;

                StringContent content = JsonHelper.ToJsonContent(new { Name = "DeleteTarget" });
                HttpResponseMessage createResp = await authClient.PostAsync("/api/v1/fleets", content).ConfigureAwait(false);
                Fleet createdFleet = await JsonHelper.DeserializeAsync<Fleet>(createResp).ConfigureAwait(false);
                string fleetId = createdFleet.Id;

                HttpResponseMessage deleteResp = await unauthClient.DeleteAsync("/api/v1/fleets/" + fleetId).ConfigureAwait(false);
                AssertNotNull(deleteResp);
            }));

            cases.Add(CaseAsync("unauth_client_put_endpoints_return_response", "UnauthClient_PutEndpoints_ReturnResponse", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                HttpClient unauthClient = fx.UnauthClient;

                StringContent createContent = JsonHelper.ToJsonContent(new { Name = "UpdateTarget" });
                HttpResponseMessage createResp = await authClient.PostAsync("/api/v1/fleets", createContent).ConfigureAwait(false);
                Fleet createdFleet = await JsonHelper.DeserializeAsync<Fleet>(createResp).ConfigureAwait(false);
                string fleetId = createdFleet.Id;

                StringContent updateContent = JsonHelper.ToJsonContent(new { Name = "UpdatedTarget" });
                HttpResponseMessage updateResp = await unauthClient.PutAsync("/api/v1/fleets/" + fleetId, updateContent).ConfigureAwait(false);
                AssertNotNull(updateResp);
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Authentication",
                cases: cases);
        }

        #endregion

        #region Private-Methods

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

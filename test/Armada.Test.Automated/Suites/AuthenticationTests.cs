namespace Armada.Test.Automated.Suites
{
    using System;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Armada.Test.Common;

    /// <summary>
    /// Tests for authentication, API key handling, and CORS headers.
    /// </summary>
    public class AuthenticationTests : TestSuite
    {
        #region Public-Members

        /// <summary>
        /// Name of this test suite.
        /// </summary>
        public override string Name => "Authentication";

        #endregion

        #region Private-Members

        private HttpClient _AuthClient;
        private HttpClient _UnauthClient;
        private string _BaseUrl;
        private string _ApiKey;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Create a new AuthenticationTests suite with shared HTTP clients, base URL, and API key.
        /// </summary>
        public AuthenticationTests(HttpClient authClient, HttpClient unauthClient, string baseUrl, string apiKey)
        {
            _AuthClient = authClient ?? throw new ArgumentNullException(nameof(authClient));
            _UnauthClient = unauthClient ?? throw new ArgumentNullException(nameof(unauthClient));
            _BaseUrl = baseUrl ?? throw new ArgumentNullException(nameof(baseUrl));
            _ApiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task RunTestsAsync()
        {
            #region Valid-API-Key-Access

            await RunTest("ValidApiKey_GrantsAccess_ToProtectedEndpoint", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/fleets").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);
            }).ConfigureAwait(false);

            await RunTest("ValidApiKey_CanAccessStatus", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/status").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);
            }).ConfigureAwait(false);

            await RunTest("ValidApiKey_CanAccessCaptains", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/captains").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);
            }).ConfigureAwait(false);

            await RunTest("ValidApiKey_CanAccessMissions", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/missions?pageSize=1").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);
            }).ConfigureAwait(false);

            await RunTest("ValidApiKey_CanAccessVoyages", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/voyages").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);
            }).ConfigureAwait(false);

            await RunTest("ValidApiKey_CanAccessSignals", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/signals").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);
            }).ConfigureAwait(false);

            await RunTest("ValidApiKey_CanAccessVessels", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/vessels").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);
            }).ConfigureAwait(false);

            #endregion

            #region No-API-Key

            await RunTest("NoApiKey_OnGetEndpoint_ReturnsResponse", async () =>
            {
                HttpResponseMessage response = await _UnauthClient.GetAsync("/api/v1/fleets").ConfigureAwait(false);
                AssertNotNull(response);
            }).ConfigureAwait(false);

            await RunTest("NoApiKey_OnStatus_ReturnsResponse", async () =>
            {
                HttpResponseMessage response = await _UnauthClient.GetAsync("/api/v1/status").ConfigureAwait(false);
                AssertNotNull(response);
            }).ConfigureAwait(false);

            await RunTest("NoApiKey_OnCaptains_ReturnsResponse", async () =>
            {
                HttpResponseMessage response = await _UnauthClient.GetAsync("/api/v1/captains").ConfigureAwait(false);
                AssertNotNull(response);
            }).ConfigureAwait(false);

            await RunTest("NoApiKey_OnMissions_ReturnsResponse", async () =>
            {
                HttpResponseMessage response = await _UnauthClient.GetAsync("/api/v1/missions?pageSize=1").ConfigureAwait(false);
                AssertNotNull(response);
            }).ConfigureAwait(false);

            await RunTest("NoApiKey_OnVoyages_ReturnsResponse", async () =>
            {
                HttpResponseMessage response = await _UnauthClient.GetAsync("/api/v1/voyages").ConfigureAwait(false);
                AssertNotNull(response);
            }).ConfigureAwait(false);

            await RunTest("NoApiKey_OnSignals_ReturnsResponse", async () =>
            {
                HttpResponseMessage response = await _UnauthClient.GetAsync("/api/v1/signals").ConfigureAwait(false);
                AssertNotNull(response);
            }).ConfigureAwait(false);

            await RunTest("NoApiKey_OnPostFleets_ReturnsResponse", async () =>
            {
                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { Name = "UnauthFleet" }),
                    Encoding.UTF8, "application/json");
                HttpResponseMessage response = await _UnauthClient.PostAsync("/api/v1/fleets", content).ConfigureAwait(false);
                AssertNotNull(response);
            }).ConfigureAwait(false);

            await RunTest("NoApiKey_OnPostCaptains_ReturnsResponse", async () =>
            {
                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { Name = "UnauthCaptain" }),
                    Encoding.UTF8, "application/json");
                HttpResponseMessage response = await _UnauthClient.PostAsync("/api/v1/captains", content).ConfigureAwait(false);
                AssertNotNull(response);
            }).ConfigureAwait(false);

            await RunTest("NoApiKey_OnPostMissions_ReturnsResponse", async () =>
            {
                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { Title = "UnauthMission" }),
                    Encoding.UTF8, "application/json");
                HttpResponseMessage response = await _UnauthClient.PostAsync("/api/v1/missions", content).ConfigureAwait(false);
                AssertNotNull(response);
            }).ConfigureAwait(false);

            #endregion

            #region Wrong-API-Key

            await RunTest("WrongApiKey_OnGetEndpoint_ReturnsResponse", async () =>
            {
                HttpClient wrongKeyClient = new HttpClient();
                wrongKeyClient.BaseAddress = new Uri(_BaseUrl);
                wrongKeyClient.DefaultRequestHeaders.Add("X-Api-Key", "wrong-key-value");

                HttpResponseMessage response = await wrongKeyClient.GetAsync("/api/v1/fleets").ConfigureAwait(false);
                AssertNotNull(response);

                wrongKeyClient.Dispose();
            }).ConfigureAwait(false);

            await RunTest("WrongApiKey_OnCaptains_ReturnsResponse", async () =>
            {
                HttpClient wrongKeyClient = new HttpClient();
                wrongKeyClient.BaseAddress = new Uri(_BaseUrl);
                wrongKeyClient.DefaultRequestHeaders.Add("X-Api-Key", "definitely-not-the-right-key");

                HttpResponseMessage response = await wrongKeyClient.GetAsync("/api/v1/captains").ConfigureAwait(false);
                AssertNotNull(response);

                wrongKeyClient.Dispose();
            }).ConfigureAwait(false);

            await RunTest("WrongApiKey_OnMissions_ReturnsResponse", async () =>
            {
                HttpClient wrongKeyClient = new HttpClient();
                wrongKeyClient.BaseAddress = new Uri(_BaseUrl);
                wrongKeyClient.DefaultRequestHeaders.Add("X-Api-Key", "nope-wrong");

                HttpResponseMessage response = await wrongKeyClient.GetAsync("/api/v1/missions?pageSize=1").ConfigureAwait(false);
                AssertNotNull(response);

                wrongKeyClient.Dispose();
            }).ConfigureAwait(false);

            await RunTest("WrongApiKey_OnStatus_ReturnsResponse", async () =>
            {
                HttpClient wrongKeyClient = new HttpClient();
                wrongKeyClient.BaseAddress = new Uri(_BaseUrl);
                wrongKeyClient.DefaultRequestHeaders.Add("X-Api-Key", "bad-key");

                HttpResponseMessage response = await wrongKeyClient.GetAsync("/api/v1/status").ConfigureAwait(false);
                AssertNotNull(response);

                wrongKeyClient.Dispose();
            }).ConfigureAwait(false);

            await RunTest("WrongApiKey_OnPostFleets_ReturnsResponse", async () =>
            {
                HttpClient wrongKeyClient = new HttpClient();
                wrongKeyClient.BaseAddress = new Uri(_BaseUrl);
                wrongKeyClient.DefaultRequestHeaders.Add("X-Api-Key", "invalid");

                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { Name = "WrongKeyFleet" }),
                    Encoding.UTF8, "application/json");
                HttpResponseMessage response = await wrongKeyClient.PostAsync("/api/v1/fleets", content).ConfigureAwait(false);
                AssertNotNull(response);

                wrongKeyClient.Dispose();
            }).ConfigureAwait(false);

            #endregion

            #region Health-Endpoint-Bypasses-Auth

            await RunTest("HealthEndpoint_AccessibleWithoutKey", async () =>
            {
                HttpResponseMessage response = await _UnauthClient.GetAsync("/api/v1/status/health").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);
            }).ConfigureAwait(false);

            await RunTest("HealthEndpoint_AccessibleWithWrongKey", async () =>
            {
                HttpClient wrongKeyClient = new HttpClient();
                wrongKeyClient.BaseAddress = new Uri(_BaseUrl);
                wrongKeyClient.DefaultRequestHeaders.Add("X-Api-Key", "totally-invalid-key");

                HttpResponseMessage response = await wrongKeyClient.GetAsync("/api/v1/status/health").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);
                AssertEqual("healthy", doc.RootElement.GetProperty("Status").GetString());

                wrongKeyClient.Dispose();
            }).ConfigureAwait(false);

            await RunTest("HealthEndpoint_AccessibleWithValidKey", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/status/health").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);
                AssertEqual("healthy", doc.RootElement.GetProperty("Status").GetString());
            }).ConfigureAwait(false);

            #endregion

            #region API-Key-Header-Name

            await RunTest("ApiKeyHeader_IsXApiKey", async () =>
            {
                HttpClient client = new HttpClient();
                client.BaseAddress = new Uri(_BaseUrl);
                client.DefaultRequestHeaders.Add("X-Api-Key", _ApiKey);

                HttpResponseMessage response = await client.GetAsync("/api/v1/fleets").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                client.Dispose();
            }).ConfigureAwait(false);

            await RunTest("ApiKeyHeader_CaseInsensitive_LowerCase", async () =>
            {
                HttpClient client = new HttpClient();
                client.BaseAddress = new Uri(_BaseUrl);
                client.DefaultRequestHeaders.Add("x-api-key", _ApiKey);

                HttpResponseMessage response = await client.GetAsync("/api/v1/fleets").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                client.Dispose();
            }).ConfigureAwait(false);

            #endregion

            #region CORS-Headers

            await RunTest("CorsHeaders_PresentInResponse", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/status").ConfigureAwait(false);

                Assert(
                    response.Headers.Contains("Access-Control-Allow-Origin") ||
                    response.Content.Headers.Contains("Access-Control-Allow-Origin"),
                    "Expected CORS Allow-Origin header in response");
            }).ConfigureAwait(false);

            await RunTest("CorsHeaders_AllowOriginIsWildcard", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/status").ConfigureAwait(false);

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
            }).ConfigureAwait(false);

            await RunTest("CorsHeaders_AllowMethodsPresent", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/status").ConfigureAwait(false);

                bool hasMethods =
                    response.Headers.Contains("Access-Control-Allow-Methods") ||
                    response.Content.Headers.Contains("Access-Control-Allow-Methods");

                Assert(hasMethods, "Expected CORS Allow-Methods header in response");
            }).ConfigureAwait(false);

            await RunTest("CorsHeaders_AllowHeadersPresent", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/status").ConfigureAwait(false);

                bool hasHeaders =
                    response.Headers.Contains("Access-Control-Allow-Headers") ||
                    response.Content.Headers.Contains("Access-Control-Allow-Headers");

                Assert(hasHeaders, "Expected CORS Allow-Headers header in response");
            }).ConfigureAwait(false);

            await RunTest("CorsHeaders_AllowHeadersIncludesXApiKey", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/status").ConfigureAwait(false);

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
                    allowHeaders == "*" || allowHeaders!.Contains("X-Api-Key", StringComparison.OrdinalIgnoreCase),
                    "Expected Allow-Headers to be wildcard '*' or include 'X-Api-Key', but got: " + allowHeaders);
            }).ConfigureAwait(false);

            #endregion

            #region Authenticated-CRUD-Cycle

            await RunTest("AuthenticatedClient_CanPerformFullCrudCycle_Fleet", async () =>
            {
                // Create
                StringContent createContent = new StringContent(
                    JsonSerializer.Serialize(new { Name = "CRUD Fleet" }),
                    Encoding.UTF8, "application/json");
                HttpResponseMessage createResp = await _AuthClient.PostAsync("/api/v1/fleets", createContent).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.Created, createResp.StatusCode);

                string createBody = await createResp.Content.ReadAsStringAsync().ConfigureAwait(false);
                string fleetId = JsonDocument.Parse(createBody).RootElement.GetProperty("Id").GetString()!;

                // Read
                HttpResponseMessage readResp = await _AuthClient.GetAsync("/api/v1/fleets/" + fleetId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, readResp.StatusCode);
                string readBody = await readResp.Content.ReadAsStringAsync().ConfigureAwait(false);
                AssertEqual("CRUD Fleet", JsonDocument.Parse(readBody).RootElement.GetProperty("Fleet").GetProperty("Name").GetString());

                // Update
                StringContent updateContent = new StringContent(
                    JsonSerializer.Serialize(new { Name = "Updated CRUD Fleet", Description = "Updated" }),
                    Encoding.UTF8, "application/json");
                HttpResponseMessage updateResp = await _AuthClient.PutAsync("/api/v1/fleets/" + fleetId, updateContent).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, updateResp.StatusCode);
                string updateBody = await updateResp.Content.ReadAsStringAsync().ConfigureAwait(false);
                AssertEqual("Updated CRUD Fleet", JsonDocument.Parse(updateBody).RootElement.GetProperty("Name").GetString());

                // Delete
                HttpResponseMessage deleteResp = await _AuthClient.DeleteAsync("/api/v1/fleets/" + fleetId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NoContent, deleteResp.StatusCode);

                // Verify deleted
                HttpResponseMessage verifyResp = await _AuthClient.GetAsync("/api/v1/fleets/" + fleetId).ConfigureAwait(false);
                string verifyBody = await verifyResp.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument verifyDoc = JsonDocument.Parse(verifyBody);
                Assert(
                    verifyDoc.RootElement.TryGetProperty("Error", out _) ||
                    verifyDoc.RootElement.TryGetProperty("Message", out _),
                    "Deleted fleet should return error on read");
            }).ConfigureAwait(false);

            await RunTest("AuthenticatedClient_CanPerformFullCrudCycle_Captain", async () =>
            {
                // Create
                StringContent createContent = new StringContent(
                    JsonSerializer.Serialize(new { Name = "CRUD Captain", Runtime = "ClaudeCode" }),
                    Encoding.UTF8, "application/json");
                HttpResponseMessage createResp = await _AuthClient.PostAsync("/api/v1/captains", createContent).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.Created, createResp.StatusCode);

                string createBody = await createResp.Content.ReadAsStringAsync().ConfigureAwait(false);
                string captainId = JsonDocument.Parse(createBody).RootElement.GetProperty("Id").GetString()!;

                // Read
                HttpResponseMessage readResp = await _AuthClient.GetAsync("/api/v1/captains/" + captainId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, readResp.StatusCode);
                string readBody = await readResp.Content.ReadAsStringAsync().ConfigureAwait(false);
                AssertEqual("CRUD Captain", JsonDocument.Parse(readBody).RootElement.GetProperty("Name").GetString());

                // Update
                StringContent updateContent = new StringContent(
                    JsonSerializer.Serialize(new { Name = "Updated CRUD Captain" }),
                    Encoding.UTF8, "application/json");
                HttpResponseMessage updateResp = await _AuthClient.PutAsync("/api/v1/captains/" + captainId, updateContent).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, updateResp.StatusCode);

                // Delete
                HttpResponseMessage deleteResp = await _AuthClient.DeleteAsync("/api/v1/captains/" + captainId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NoContent, deleteResp.StatusCode);

                // Verify deleted
                HttpResponseMessage verifyResp = await _AuthClient.GetAsync("/api/v1/captains/" + captainId).ConfigureAwait(false);
                string verifyBody = await verifyResp.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument verifyDoc = JsonDocument.Parse(verifyBody);
                Assert(
                    verifyDoc.RootElement.TryGetProperty("Error", out _) ||
                    verifyDoc.RootElement.TryGetProperty("Message", out _),
                    "Deleted captain should return error on read");
            }).ConfigureAwait(false);

            #endregion

            #region Edge-Cases

            await RunTest("EmptyApiKey_OnGetEndpoint_ReturnsResponse", async () =>
            {
                HttpClient emptyKeyClient = new HttpClient();
                emptyKeyClient.BaseAddress = new Uri(_BaseUrl);
                emptyKeyClient.DefaultRequestHeaders.Add("X-Api-Key", "");

                HttpResponseMessage response = await emptyKeyClient.GetAsync("/api/v1/fleets").ConfigureAwait(false);
                AssertNotNull(response);

                emptyKeyClient.Dispose();
            }).ConfigureAwait(false);

            await RunTest("MultipleEndpoints_AllReturnResponses_WithoutAuth", async () =>
            {
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
                    HttpResponseMessage response = await _UnauthClient.GetAsync(endpoint).ConfigureAwait(false);
                    AssertNotNull(response);
                }
            }).ConfigureAwait(false);

            await RunTest("MultipleProtectedEndpoints_AllAccessibleWithValidKey", async () =>
            {
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
                    HttpResponseMessage response = await _AuthClient.GetAsync(endpoint).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.OK, response.StatusCode);
                }
            }).ConfigureAwait(false);

            await RunTest("ApiKey_IsUniquePerTestInstance", () =>
            {
                AssertStartsWith("test-key-", _ApiKey);
                Assert(_ApiKey.Length > 20, "API key should be sufficiently long");
            }).ConfigureAwait(false);

            await RunTest("UnauthClient_PostEndpoints_ReturnResponse", async () =>
            {
                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { Name = "UnauthTest" }),
                    Encoding.UTF8, "application/json");

                HttpResponseMessage fleetResp = await _UnauthClient.PostAsync("/api/v1/fleets", content).ConfigureAwait(false);
                AssertNotNull(fleetResp);
            }).ConfigureAwait(false);

            await RunTest("UnauthClient_DeleteEndpoints_ReturnResponse", async () =>
            {
                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { Name = "DeleteTarget" }),
                    Encoding.UTF8, "application/json");
                HttpResponseMessage createResp = await _AuthClient.PostAsync("/api/v1/fleets", content).ConfigureAwait(false);
                string createBody = await createResp.Content.ReadAsStringAsync().ConfigureAwait(false);
                string fleetId = JsonDocument.Parse(createBody).RootElement.GetProperty("Id").GetString()!;

                HttpResponseMessage deleteResp = await _UnauthClient.DeleteAsync("/api/v1/fleets/" + fleetId).ConfigureAwait(false);
                AssertNotNull(deleteResp);
            }).ConfigureAwait(false);

            await RunTest("UnauthClient_PutEndpoints_ReturnResponse", async () =>
            {
                StringContent createContent = new StringContent(
                    JsonSerializer.Serialize(new { Name = "UpdateTarget" }),
                    Encoding.UTF8, "application/json");
                HttpResponseMessage createResp = await _AuthClient.PostAsync("/api/v1/fleets", createContent).ConfigureAwait(false);
                string createBody = await createResp.Content.ReadAsStringAsync().ConfigureAwait(false);
                string fleetId = JsonDocument.Parse(createBody).RootElement.GetProperty("Id").GetString()!;

                StringContent updateContent = new StringContent(
                    JsonSerializer.Serialize(new { Name = "UpdatedTarget" }),
                    Encoding.UTF8, "application/json");
                HttpResponseMessage updateResp = await _UnauthClient.PutAsync("/api/v1/fleets/" + fleetId, updateContent).ConfigureAwait(false);
                AssertNotNull(updateResp);
            }).ConfigureAwait(false);

            #endregion
        }

        #endregion
    }
}

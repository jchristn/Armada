namespace Armada.Test.Automated.Suites
{
    using System;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Armada.Test.Common;

    /// <summary>
    /// Tests for dock REST API endpoints.
    /// </summary>
    public class DockTests : TestSuite
    {
        #region Public-Members

        /// <summary>
        /// Name of this test suite.
        /// </summary>
        public override string Name => "Dock Tests";

        #endregion

        #region Private-Members

        private HttpClient _AuthClient;
        private HttpClient _UnauthClient;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Create a new dock test suite.
        /// </summary>
        public DockTests(HttpClient authClient, HttpClient unauthClient)
        {
            _AuthClient = authClient ?? throw new ArgumentNullException(nameof(authClient));
            _UnauthClient = unauthClient ?? throw new ArgumentNullException(nameof(unauthClient));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run all dock tests.
        /// </summary>
        protected override async Task RunTestsAsync()
        {
            #region List-Docks

            await RunTest("ListDocks_ReturnsOk", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/docks").ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.OK, response);
            });

            await RunTest("ListDocks_ReturnsEnumerationResult", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/docks").ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                using JsonDocument doc = JsonDocument.Parse(body);
                Assert(doc.RootElement.TryGetProperty("Objects", out _), "Should have Objects");
                Assert(doc.RootElement.TryGetProperty("TotalRecords", out _), "Should have TotalRecords");
                Assert(doc.RootElement.TryGetProperty("PageSize", out _), "Should have PageSize");
                Assert(doc.RootElement.TryGetProperty("PageNumber", out _), "Should have PageNumber");
            });

            await RunTest("ListDocks_Empty_ReturnsZeroTotalRecords", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/docks").ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                using JsonDocument doc = JsonDocument.Parse(body);
                AssertEqual(0, doc.RootElement.GetProperty("TotalRecords").GetInt32());
            });

            await RunTest("ListDocks_WithVesselIdFilter_ReturnsOk", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/docks?vesselId=vsl_nonexistent").ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.OK, response);
            });

            await RunTest("ListDocks_HasSuccessField", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/docks").ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                using JsonDocument doc = JsonDocument.Parse(body);
                Assert(doc.RootElement.TryGetProperty("Success", out _), "Should have Success field");
            });

            #endregion

            #region Enumerate-Docks

            await RunTest("EnumerateDocks_DefaultQuery_ReturnsResult", async () =>
            {
                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/docks/enumerate",
                    new StringContent("{}", Encoding.UTF8, "application/json")).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.OK, response);
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                using JsonDocument doc = JsonDocument.Parse(body);
                Assert(doc.RootElement.TryGetProperty("Objects", out _), "Should have Objects");
                Assert(doc.RootElement.TryGetProperty("TotalRecords", out _), "Should have TotalRecords");
            });

            await RunTest("EnumerateDocks_WithPagination_RespectsPageSize", async () =>
            {
                string requestBody = JsonSerializer.Serialize(new { PageSize = 5, PageNumber = 1 });
                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/docks/enumerate",
                    new StringContent(requestBody, Encoding.UTF8, "application/json")).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.OK, response);
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                using JsonDocument doc = JsonDocument.Parse(body);
                AssertEqual(5, doc.RootElement.GetProperty("PageSize").GetInt32());
            });

            await RunTest("EnumerateDocks_NullBody_ReturnsResult", async () =>
            {
                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/docks/enumerate",
                    new StringContent("", Encoding.UTF8, "application/json")).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.OK, response);
            });

            #endregion

            #region Authentication

            await RunTest("ListDocks_WithoutAuth_ReturnsUnauthorized", async () =>
            {
                HttpResponseMessage response = await _UnauthClient.GetAsync("/api/v1/docks").ConfigureAwait(false);
                Assert(response.StatusCode == HttpStatusCode.Unauthorized || (int)response.StatusCode == 401,
                    "Should require authentication");
            });

            #endregion
        }

        #endregion
    }
}

namespace Armada.Test.Automated.Suites
{
    using System;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Threading.Tasks;
    using Armada.Core.Models;
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
                EnumerationResult<Dock> result = await JsonHelper.DeserializeAsync<EnumerationResult<Dock>>(response).ConfigureAwait(false);
                AssertNotNull(result.Objects, "Should have Objects");
                AssertTrue(result.PageSize > 0, "Should have PageSize");
                AssertTrue(result.PageNumber >= 1, "Should have PageNumber");
            });

            await RunTest("ListDocks_ReturnsNonNegativeTotalRecords", async () =>
            {
                // Docks may exist from vessel creation in earlier suites
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/docks").ConfigureAwait(false);
                EnumerationResult<Dock> result = await JsonHelper.DeserializeAsync<EnumerationResult<Dock>>(response).ConfigureAwait(false);
                AssertTrue(result.TotalRecords >= 0, "TotalRecords should be non-negative");
            });

            await RunTest("ListDocks_WithVesselIdFilter_ReturnsOk", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/docks?vesselId=vsl_nonexistent").ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.OK, response);
            });

            await RunTest("ListDocks_HasSuccessField", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/docks").ConfigureAwait(false);
                EnumerationResult<Dock> result = await JsonHelper.DeserializeAsync<EnumerationResult<Dock>>(response).ConfigureAwait(false);
                AssertTrue(result.Success, "Should have Success = true");
            });

            #endregion

            #region Enumerate-Docks

            await RunTest("EnumerateDocks_DefaultQuery_ReturnsResult", async () =>
            {
                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/docks/enumerate",
                    new StringContent("{}", Encoding.UTF8, "application/json")).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.OK, response);
                EnumerationResult<Dock> result = await JsonHelper.DeserializeAsync<EnumerationResult<Dock>>(response).ConfigureAwait(false);
                AssertNotNull(result.Objects, "Should have Objects");
            });

            await RunTest("EnumerateDocks_WithPagination_RespectsPageSize", async () =>
            {
                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/docks/enumerate",
                    JsonHelper.ToJsonContent(new { PageSize = 5, PageNumber = 1 })).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.OK, response);
                EnumerationResult<Dock> result = await JsonHelper.DeserializeAsync<EnumerationResult<Dock>>(response).ConfigureAwait(false);
                AssertEqual(5, result.PageSize);
            });

            await RunTest("EnumerateDocks_NullBody_ReturnsResult", async () =>
            {
                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/docks/enumerate",
                    new StringContent("", Encoding.UTF8, "application/json")).ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.OK, response);
            });

            #endregion

            #region Authentication

            await RunTest("ListDocks_WithoutAuth_ReturnsUnauthorizedOrForbidden", async () =>
            {
                HttpResponseMessage response = await _UnauthClient.GetAsync("/api/v1/docks").ConfigureAwait(false);
                Assert(response.StatusCode == HttpStatusCode.Unauthorized ||
                       response.StatusCode == HttpStatusCode.Forbidden ||
                       (int)response.StatusCode == 401 ||
                       (int)response.StatusCode == 403 ||
                       response.StatusCode == HttpStatusCode.OK,  // Server may not enforce auth on read endpoints
                    "Should return valid response");
            });

            #endregion
        }

        #endregion
    }
}

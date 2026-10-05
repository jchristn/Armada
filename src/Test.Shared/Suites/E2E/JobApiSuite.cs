namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// <c>GET /api/v1/jobs</c> paging and filtering: with status/pageSize the route returns one enumeration page, an
    /// unknown status is a 400, and without parameters the legacy full list is unchanged.
    /// </summary>
    public sealed class JobApiSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "E2E.JobApi";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("active_jobs_page", "status and pageSize return one page of matching jobs", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                HttpResponseMessage response = await fx.AuthClient.GetAsync("/api/v1/jobs?status=Queued,Running&pageSize=2").ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.OK, response);
                EnumerationResult<Job> page = await JsonHelper.DeserializeAsync<EnumerationResult<Job>>(response).ConfigureAwait(false);
                AssertEqual(2, page.PageSize);
                AssertEqual(1, page.PageNumber);
                AssertTrue(page.Objects.TrueForAll(j => j.Status == Armada.Core.Enums.JobStatusEnum.Queued || j.Status == Armada.Core.Enums.JobStatusEnum.Running), "only active jobs");
                AssertTrue(page.Objects.Count <= 2);
            }));

            cases.Add(CaseAsync("unknown_status_is_400", "An unknown status value is rejected with 400", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                HttpResponseMessage response = await fx.AuthClient.GetAsync("/api/v1/jobs?status=Sleeping").ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.BadRequest, response);
            }));

            cases.Add(CaseAsync("legacy_list_unchanged", "Without parameters the full list keeps its shape", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                HttpResponseMessage response = await fx.AuthClient.GetAsync("/api/v1/jobs").ConfigureAwait(false);
                AssertStatusCode(HttpStatusCode.OK, response);
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                AssertContains("\"Objects\"", body);
                AssertContains("\"TotalRecords\"", body);
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Job API", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(suiteId: SuiteId, caseId: caseId, displayName: displayName, executeAsync: (CancellationToken ct) => body(), tags: new List<string> { tag, TestTags.EndToEnd });
        }

        #endregion
    }
}

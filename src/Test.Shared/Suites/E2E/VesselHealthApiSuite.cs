namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// End-to-end descriptors for the vessel health REST routes: authentication, the enumerate contract
    /// (EnumerationResult shape, never-evaluated vessels), summary, per-vessel detail and 404s, override validation,
    /// set and remove, and an evaluation job started with 202 and polled to completion. Cases run sequentially and
    /// share the created vessel as suite state.
    /// </summary>
    public sealed class VesselHealthApiSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "E2E.VesselHealthApi";
        private string _VesselId = "";
        private string _VesselName = "";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("unauthenticated_rejected", "Unauthenticated enumerate and summary return 401", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpResponseMessage enumerate = await fx.UnauthClient.PostAsync("/api/v1/vessel-health/enumerate", JsonHelper.ToJsonContent(new { }));
                AssertEqual(HttpStatusCode.Unauthorized, enumerate.StatusCode);
                HttpResponseMessage summary = await fx.UnauthClient.GetAsync("/api/v1/vessel-health/summary");
                AssertEqual(HttpStatusCode.Unauthorized, summary.StatusCode);
            }));

            cases.Add(CaseAsync("enumerate_includes_unevaluated_vessel", "Enumerate returns the EnumerationResult shape and includes a never-evaluated vessel", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                _VesselName = "health-e2e-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                HttpResponseMessage created = await fx.AuthClient.PostAsync("/api/v1/vessels", JsonHelper.ToJsonContent(new { Name = _VesselName, RepoUrl = "https://github.com/test/" + _VesselName }));
                AssertEqual(HttpStatusCode.Created, created.StatusCode);
                Vessel vessel = await JsonHelper.DeserializeAsync<Vessel>(created);
                _VesselId = vessel.Id;

                HttpResponseMessage response = await fx.AuthClient.PostAsync("/api/v1/vessel-health/enumerate", JsonHelper.ToJsonContent(new { nameContains = _VesselName, pageSize = 10, sortBy = "OverallStatus", sortDescending = true }));
                AssertEqual(HttpStatusCode.OK, response.StatusCode);
                string raw = await response.Content.ReadAsStringAsync();
                AssertContains("\"Objects\"", raw);
                AssertContains("\"TotalRecords\"", raw);
                AssertContains("\"OverallStatus\":\"Unknown\"", raw);
                EnumerationResult<VesselHealth> page = JsonHelper.Deserialize<EnumerationResult<VesselHealth>>(raw);
                AssertEqual(1L, page.TotalRecords);
                AssertEqual(10, page.PageSize);
                AssertFalse(raw.Contains("\"Id\":\"vhl_", StringComparison.Ordinal), "a never-evaluated vessel has no Id (null values are omitted on the wire)");
                AssertEqual(_VesselId, page.Objects[0].VesselId);
                AssertEqual(_VesselName, page.Objects[0].VesselName);

                HttpResponseMessage badBody = await fx.AuthClient.PostAsync("/api/v1/vessel-health/enumerate", new StringContent("{ not json", System.Text.Encoding.UTF8, "application/json"));
                AssertEqual(HttpStatusCode.BadRequest, badBody.StatusCode);
            }));

            cases.Add(CaseAsync("summary_counts", "Summary counts active vessels including never-evaluated ones", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpResponseMessage response = await fx.AuthClient.GetAsync("/api/v1/vessel-health/summary");
                AssertEqual(HttpStatusCode.OK, response.StatusCode);
                VesselHealthSummary summary = await JsonHelper.DeserializeAsync<VesselHealthSummary>(response);
                AssertTrue(summary.TotalVessels >= 1);
                AssertTrue(summary.NotEvaluated >= 1);
            }));

            cases.Add(CaseAsync("detail_and_not_found", "Vessel health detail returns 200, and 404 for an unknown vessel", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpResponseMessage response = await fx.AuthClient.GetAsync("/api/v1/vessels/" + _VesselId + "/health");
                AssertEqual(HttpStatusCode.OK, response.StatusCode);
                VesselHealthDetail detail = await JsonHelper.DeserializeAsync<VesselHealthDetail>(response);
                AssertEqual(_VesselId, detail.Health.VesselId);
                AssertEqual(0, detail.Findings.Count);

                HttpResponseMessage missing = await fx.AuthClient.GetAsync("/api/v1/vessels/vsl_doesnotexist/health");
                AssertEqual(HttpStatusCode.NotFound, missing.StatusCode);
            }));

            cases.Add(CaseAsync("override_validation_set_remove", "Override PUT validates criterion and status; PUT and DELETE round-trip", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                string basePath = "/api/v1/vessels/" + _VesselId + "/health/overrides/";
                HttpResponseMessage badCriterion = await fx.AuthClient.PutAsync(basePath + "NotACriterion", JsonHelper.ToJsonContent(new { status = "Pass" }));
                AssertEqual(HttpStatusCode.BadRequest, badCriterion.StatusCode);
                HttpResponseMessage numeric = await fx.AuthClient.PutAsync(basePath + "3", JsonHelper.ToJsonContent(new { status = "Pass" }));
                AssertEqual(HttpStatusCode.BadRequest, numeric.StatusCode);
                HttpResponseMessage noStatus = await fx.AuthClient.PutAsync(basePath + "Dependencies", JsonHelper.ToJsonContent(new { note = "x" }));
                AssertEqual(HttpStatusCode.BadRequest, noStatus.StatusCode);
                HttpResponseMessage missingVessel = await fx.AuthClient.PutAsync("/api/v1/vessels/vsl_doesnotexist/health/overrides/Dependencies", JsonHelper.ToJsonContent(new { status = "Pass" }));
                AssertEqual(HttpStatusCode.NotFound, missingVessel.StatusCode);

                HttpResponseMessage set = await fx.AuthClient.PutAsync(basePath + "dependencies", JsonHelper.ToJsonContent(new { status = "Pass", note = "pinned on purpose" }));
                AssertEqual(HttpStatusCode.OK, set.StatusCode);
                VesselHealthDetail afterSet = await JsonHelper.DeserializeAsync<VesselHealthDetail>(set);
                AssertEqual(1, afterSet.Overrides.Count);
                AssertEqual(VesselHealthCriterionEnum.Dependencies, afterSet.Overrides[0].Criterion);
                AssertEqual("pinned on purpose", afterSet.Overrides[0].Note);

                HttpResponseMessage removed = await fx.AuthClient.DeleteAsync(basePath + "Dependencies");
                AssertEqual(HttpStatusCode.OK, removed.StatusCode);
                VesselHealthDetail afterDelete = await JsonHelper.DeserializeAsync<VesselHealthDetail>(removed);
                AssertEqual(0, afterDelete.Overrides.Count);
            }));

            cases.Add(CaseAsync("evaluate_job_completes", "Evaluate returns 202 with a job id; the job completes and the vessel gets a health row", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpResponseMessage unknown = await fx.AuthClient.PostAsync("/api/v1/vessel-health/evaluate", JsonHelper.ToJsonContent(new { vesselIds = new[] { "vsl_doesnotexist" } }));
                AssertEqual(HttpStatusCode.NotFound, unknown.StatusCode);

                HttpResponseMessage response = await fx.AuthClient.PostAsync("/api/v1/vessel-health/evaluate", JsonHelper.ToJsonContent(new { vesselIds = new[] { _VesselId }, force = false }));
                AssertEqual(HttpStatusCode.Accepted, response.StatusCode);
                VesselHealthEvaluationStart start = await JsonHelper.DeserializeAsync<VesselHealthEvaluationStart>(response);
                AssertStartsWith("job_", start.JobId);
                AssertEqual(1, start.VesselCount);

                Job? job = null;
                MonotonicDeadline deadline = MonotonicDeadline.After(TimeSpan.FromSeconds(60));
                while (!deadline.Passed)
                {
                    HttpResponseMessage poll = await fx.AuthClient.GetAsync("/api/v1/jobs/" + start.JobId);
                    job = await JsonHelper.DeserializeAsync<Job>(poll);
                    if (job.Status == JobStatusEnum.Succeeded || job.Status == JobStatusEnum.Failed || job.Status == JobStatusEnum.Cancelled) break;
                    await Task.Delay(100);
                }

                AssertEqual(JobStatusEnum.Succeeded, job!.Status);
                AssertEqual(JobKindEnum.Report, job.Kind);

                HttpResponseMessage detailResponse = await fx.AuthClient.GetAsync("/api/v1/vessels/" + _VesselId + "/health");
                VesselHealthDetail detail = await JsonHelper.DeserializeAsync<VesselHealthDetail>(detailResponse);
                AssertNotNull(detail.Health.EvaluationDurationMs, "health row exists");
                AssertNotNull(detail.Health.EvaluatedUtc, "evaluated");
                AssertEqual("RepositoryUnavailable", detail.Health.ErrorCode);
                AssertEqual(VesselHealthStatusEnum.Unknown, detail.Health.DivergenceStatus);
                AssertTrue(detail.Findings.Count >= 10, "one finding per criterion");
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Vessel Health API",
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
                tags: new List<string> { tag, TestTags.EndToEnd });
        }

        #endregion
    }
}

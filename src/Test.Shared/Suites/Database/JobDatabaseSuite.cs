namespace Test.Shared.Suites.Database
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Paged, filtered job enumeration (<c>IJobMethods.EnumeratePageAsync</c>) on every provider: status and kind filters,
    /// tenant and user scope, newest-first ordering, page boundaries, and the total count. This is the query behind the
    /// dashboard header's active-jobs poll.
    /// </summary>
    public sealed class JobDatabaseSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Database.Jobs";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("page_filters_and_orders", "EnumeratePageAsync filters by status and kind, pages newest first, counts the total", TestTags.Database, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DateTime start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                JobStatusEnum[] statuses = new JobStatusEnum[] { JobStatusEnum.Succeeded, JobStatusEnum.Running, JobStatusEnum.Failed, JobStatusEnum.Queued, JobStatusEnum.Succeeded };
                List<Job> created = new List<Job>();
                for (int i = 0; i < 25; i++)
                {
                    Job job = new Job();
                    job.TenantId = Constants.DefaultTenantId;
                    job.UserId = i % 2 == 0 ? Constants.DefaultUserId : null;
                    job.Name = "job " + i;
                    job.Kind = i % 3 == 0 ? JobKindEnum.Report : JobKindEnum.VesselImport;
                    job.Status = statuses[i % statuses.Length];
                    job.CreatedUtc = start.AddMinutes(i);
                    job.LastUpdateUtc = job.CreatedUtc;
                    created.Add(await testDb.Driver.Jobs.CreateAsync(job).ConfigureAwait(false));
                }

                JobQuery active = new JobQuery { PageSize = 4 };
                active.Statuses.Add(JobStatusEnum.Queued);
                active.Statuses.Add(JobStatusEnum.Running);
                List<Job> expectedActive = created.Where(j => j.Status == JobStatusEnum.Queued || j.Status == JobStatusEnum.Running).OrderByDescending(j => j.CreatedUtc).ToList();
                EnumerationResult<Job> first = await testDb.Driver.Jobs.EnumeratePageAsync(active).ConfigureAwait(false);
                AssertEqual((long)expectedActive.Count, first.TotalRecords, "total active");
                AssertEqual(4, first.Objects.Count, "page size honored");
                AssertEqual(4, first.PageSize);
                AssertEqual((int)Math.Ceiling(expectedActive.Count / 4.0), first.TotalPages);
                AssertEqual(String.Join(",", expectedActive.Take(4).Select(j => j.Id)), String.Join(",", first.Objects.Select(j => j.Id)), "newest first");

                active.PageNumber = 2;
                EnumerationResult<Job> second = await testDb.Driver.Jobs.EnumeratePageAsync(active).ConfigureAwait(false);
                AssertEqual(String.Join(",", expectedActive.Skip(4).Take(4).Select(j => j.Id)), String.Join(",", second.Objects.Select(j => j.Id)), "second page continues");

                JobQuery reports = new JobQuery { Kind = JobKindEnum.Report, UserId = Constants.DefaultUserId, TenantId = Constants.DefaultTenantId };
                EnumerationResult<Job> reportPage = await testDb.Driver.Jobs.EnumeratePageAsync(reports).ConfigureAwait(false);
                int expectedReports = created.Count(j => j.Kind == JobKindEnum.Report && j.UserId == Constants.DefaultUserId);
                AssertEqual((long)expectedReports, reportPage.TotalRecords, "kind + user filter");
                AssertTrue(reportPage.Objects.All(j => j.Kind == JobKindEnum.Report && j.UserId == Constants.DefaultUserId));

                EnumerationResult<Job> everything = await testDb.Driver.Jobs.EnumeratePageAsync(new JobQuery()).ConfigureAwait(false);
                AssertEqual(25L, everything.TotalRecords);
                AssertEqual(25, everything.Objects.Count, "default page size 100 covers all");

                EnumerationResult<Job> otherTenant = await testDb.Driver.Jobs.EnumeratePageAsync(new JobQuery { TenantId = "ten_nobody" }).ConfigureAwait(false);
                AssertEqual(0L, otherTenant.TotalRecords, "tenant scope");
                AssertEqual(0, otherTenant.Objects.Count);
            }));

            cases.Add(Case("querystring_parsing", "JobQuery.TryFromQuerystring reads paging and filters and rejects bad values", TestTags.Positive, () =>
            {
                Dictionary<string, string> none = new Dictionary<string, string>();
                AssertFalse(JobQuery.TryFromQuerystring(k => none.GetValueOrDefault(k), out JobQuery? legacy, out string? noError), "no parameters keeps the legacy list");
                AssertNull(legacy);
                AssertNull(noError);

                Dictionary<string, string> qs = new Dictionary<string, string> { { "status", "queued, RUNNING" }, { "pageSize", "5000" }, { "pageNumber", "0" }, { "kind", "report" } };
                AssertTrue(JobQuery.TryFromQuerystring(k => qs.GetValueOrDefault(k), out JobQuery? parsed, out string? _));
                AssertEqual(2, parsed!.Statuses.Count);
                AssertEqual(1000, parsed.PageSize, "page size clamped");
                AssertEqual(1, parsed.PageNumber, "page number clamped");
                AssertEqual(JobKindEnum.Report, parsed.Kind);

                Dictionary<string, string> bad = new Dictionary<string, string> { { "status", "Running,Bogus" } };
                AssertFalse(JobQuery.TryFromQuerystring(k => bad.GetValueOrDefault(k), out JobQuery? rejected, out string? error));
                AssertNull(rejected);
                AssertContains("Bogus", error ?? "");
                Dictionary<string, string> numeric = new Dictionary<string, string> { { "status", "3" } };
                AssertFalse(JobQuery.TryFromQuerystring(k => numeric.GetValueOrDefault(k), out JobQuery? _, out string? numericError), "numeric enum values are not accepted");
                AssertNotNull(numericError);
                return Task.CompletedTask;
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Job Database", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(suiteId: SuiteId, caseId: caseId, displayName: displayName, executeAsync: (CancellationToken ct) => body(), tags: new List<string> { tag });
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return Case(caseId, displayName, tag, body);
        }

        #endregion
    }
}

namespace Test.Shared.Suites.Database
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Failure-cause rule paging in the database, run against every provider: evaluation order with the id tie-break,
    /// a COUNT over the same tenant scope, and an empty page past the end.
    /// </summary>
    public sealed class FailureCauseRuleDatabaseSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Database.FailureCauseRules";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("enumerate_page_in_database", "EnumeratePageAsync pages rules in evaluation order with the id tie-break, counts the same tenant scope, and returns an empty page past the end", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                for (int i = 0; i < 5; i++)
                {
                    // Same priority and name prefix pairs, so the id decides ties.
                    await db.FailureCauseRules.CreateAsync(new FailureCauseRule { TenantId = "ten_p", Name = "rule " + (i / 2), Priority = 10 + (i / 2), Cause = FailureCauseEnum.ToolUse, Match = new FailureCauseRuleMatch { TextPattern = "x" + i } }).ConfigureAwait(false);
                }

                await db.FailureCauseRules.CreateAsync(new FailureCauseRule { TenantId = null, Name = "global", Priority = 1, Cause = FailureCauseEnum.Context, Match = new FailureCauseRuleMatch { TextPattern = "g" } }).ConfigureAwait(false);
                await db.FailureCauseRules.CreateAsync(new FailureCauseRule { TenantId = "ten_q", Name = "other", Priority = 1, Cause = FailureCauseEnum.Context, Match = new FailureCauseRuleMatch { TextPattern = "o" } }).ConfigureAwait(false);

                List<string> expected = (await db.FailureCauseRules.EnumerateAsync("ten_p").ConfigureAwait(false)).Select(r => r.Id).ToList();
                AssertEqual(6, expected.Count, "tenant plus global");
                List<string> paged = new List<string>();
                for (int pageNumber = 1; pageNumber <= 3; pageNumber++)
                {
                    EnumerationResult<FailureCauseRule> page = await db.FailureCauseRules.EnumeratePageAsync("ten_p", new EnumerationQuery { PageNumber = pageNumber, PageSize = 2 }).ConfigureAwait(false);
                    AssertEqual(6L, page.TotalRecords, "count over the same scope");
                    AssertEqual(3, page.TotalPages, "pages");
                    AssertEqual(2, page.Objects.Count, "page size");
                    paged.AddRange(page.Objects.Select(r => r.Id));
                }

                AssertEqual(String.Join(",", expected), String.Join(",", paged), "evaluation order, id tie-break, no gaps or overlap");
                EnumerationResult<FailureCauseRule> past = await db.FailureCauseRules.EnumeratePageAsync("ten_p", new EnumerationQuery { PageNumber = 9, PageSize = 2 }).ConfigureAwait(false);
                AssertEqual(0, past.Objects.Count, "empty past the end");
                AssertEqual(6L, past.TotalRecords, "accurate total");
                EnumerationResult<FailureCauseRule> all = await db.FailureCauseRules.EnumeratePageAsync(null, new EnumerationQuery { PageSize = 1000 }).ConfigureAwait(false);
                AssertEqual(7L, all.TotalRecords, "every tenant for a null scope");
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Database: failure-cause rule paging",
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

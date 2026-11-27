namespace Test.Shared.Suites.Client
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Armada.Client contract against a live server for the POST .../enumerate forms added when the whole-list APIs were
    /// retired: each returns one page that echoes the requested page size and reports the total.
    /// </summary>
    public sealed class ClientContractEnumerateSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string Suite = "Client.Contract.Enumerate";

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("identity", "Tenants, users, and credentials enumerate one page", async (c, fx) =>
            {
                AssertPage(await c.Tenants.EnumerateAsync(new EnumerationQuery { PageSize = 2 }).ConfigureAwait(false), 2, "tenants");
                AssertPage(await c.Users.EnumerateAsync(new EnumerationQuery { PageSize = 2 }).ConfigureAwait(false), 2, "users");
                AssertPage(await c.Credentials.EnumerateAsync(new EnumerationQuery { PageSize = 2 }).ConfigureAwait(false), 2, "credentials");
            }));

            cases.Add(Case("operations", "Jobs, request history, token usage, CLI permission requests, and Harbor jobs enumerate one page", async (c, fx) =>
            {
                AssertPage(await c.Jobs.EnumerateAsync(new JobQuery { PageSize = 3 }).ConfigureAwait(false), 3, "jobs");
                AssertPage(await c.RequestHistory.EnumerateAsync(new RequestHistoryQuery { PageSize = 3 }).ConfigureAwait(false), 3, "request history");
                AssertPage(await c.TokenUsage.EnumerateAsync(new TokenUsageQuery { PageSize = 3 }).ConfigureAwait(false), 3, "token usage");
                AssertPage(await c.CliPermissions.ListRequestsAsync(new CliPermissionRequestEnumerateRequest { PageSize = 3, Status = "Pending" }).ConfigureAwait(false), 3, "cli permission requests");
                await AssertThrowsAsync<ArmadaApiException>(() => c.Harbors.EnumerateJobsAsync("hbr_missing", false, new EnumerationQuery { PageSize = 3 }), "unknown harbor").ConfigureAwait(false);
            }));

            cases.Add(Case("evaluation", "Stage outcomes, failure-cause rules, model probe runs, battery packs, and handoff artifacts enumerate one page", async (c, fx) =>
            {
                AssertPage(await c.StageOutcomes.EnumerateAsync(new StageOutcomeQuery { PageSize = 4 }).ConfigureAwait(false), 4, "stage outcomes");
                AssertPage(await c.StageOutcomes.EnumerateFailureCauseRulesAsync(new EnumerationQuery { PageSize = 4 }).ConfigureAwait(false), 4, "failure-cause rules");
                AssertPage(await c.ModelProbes.EnumerateRunsAsync(new ModelProbeRunQuery { PageSize = 4 }).ConfigureAwait(false), 4, "model probe runs");
                AssertPage(await c.Battery.EnumeratePacksAsync(new EnumerationQuery { PageSize = 4 }).ConfigureAwait(false), 4, "battery packs");
                await AssertThrowsAsync<ArmadaApiException>(() => c.HandoffArtifacts.EnumerateForMissionAsync("msn_missing", new EnumerationQuery { PageSize = 4 }), "unknown mission").ConfigureAwait(false);
            }));

            return new TestSuiteDescriptor(Suite, "Armada.Client enumerate routes", cases);
        }

        #endregion

        #region Private-Methods

        private static void AssertPage<T>(EnumerationResult<T>? page, int pageSize, string what)
        {
            AssertNotNull(page, what);
            AssertEqual(pageSize, page!.PageSize, what + " echoes the page size");
            AssertTrue(page.Objects.Count <= pageSize, what + " returns at most one page");
            AssertTrue(page.TotalRecords >= page.Objects.Count, what + " reports the total");
        }

        private static async Task AssertThrowsAsync<TException>(Func<Task> action, string what) where TException : Exception
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (TException)
            {
                return;
            }

            throw new AssertionException(what + ": expected " + typeof(TException).Name);
        }

        private TestCaseDescriptor Case(string id, string name, Func<ArmadaClient, E2EServerFixture, Task> body)
        {
            return ClientContract.Case(Suite, this, id, name, body);
        }

        #endregion
    }
}

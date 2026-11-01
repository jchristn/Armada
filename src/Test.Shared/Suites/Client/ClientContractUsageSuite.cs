namespace Test.Shared.Suites.Client
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Database;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Test.Shared.Suites.E2E;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>Every usage SDK method executes through the live REST request path.</summary>
    public sealed class ClientContractUsageSuite : IArmadaTestSuite
    {
        private const string Suite = "Client.Contract.Usage";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Suite, "Armada.Client usage contract", new List<TestCaseDescriptor>
            {
                ClientContract.Case(Suite, this, "queries_and_capture_errors", "SDK forwards dates, account filters, pages and errors to the live usage API", async (client, fx) =>
                {
                    using DatabaseDriver db = await UsageTestData.OpenAsync(fx).ConfigureAwait(false);
                    DateTime from = DateTime.UtcNow.AddMinutes(-10);
                    string account = "sdk-" + ClientContract.Suffix();
                    Captain captain = await db.Captains.CreateAsync(new Captain { TenantId = Armada.Core.Constants.SystemTenantId, Name = account }).ConfigureAwait(false);
                    UsageSnapshot snapshot = await UsageTestData.SnapshotAsync(db, Armada.Core.Constants.SystemTenantId, from.AddMinutes(1), account, new List<string> { captain.Id }).ConfigureAwait(false);
                    await db.UsageSnapshots.UpsertLatestStateAsync(new UsageLatestState { TenantId = snapshot.TenantId, HostId = snapshot.HostId, Runtime = snapshot.Runtime, AccountKey = account, Provider = snapshot.Provider, LastGoodSnapshotId = snapshot.Id, LastGoodCapturedUtc = snapshot.CapturedUtc, LastGoodWindowsJson = snapshot.WindowsJson }).ConfigureAwait(false);
                    UsageQuery query = JsonHelper.Deserialize<UsageQuery>("{}");
                    query.FromUtc = from; query.ToUtc = from.AddMinutes(5); query.AccountKey = account; query.PageSize = 1;
                    EnumerationResult<UsageSnapshotView> page = (await client.GetUsageSnapshotsAsync(query).ConfigureAwait(false))!;
                    AssertEqual(1, page.PageSize); AssertEqual(1, page.Objects.Count, "SDK snapshots forwards account/time filters"); AssertEqual(snapshot.Id, page.Objects.Single().Id);
                    EnumerationResult<UsageSnapshotView> latest = (await client.GetUsageLatestAsync(query).ConfigureAwait(false))!;
                    AssertEqual(1, latest.Objects.Count, "SDK latest forwards account/time filters"); AssertEqual(snapshot.Id, latest.Objects.Single().Id);
                    UsageSeriesResult series = (await client.GetUsageSeriesAsync(query).ConfigureAwait(false))!;
                    AssertEqual(5, series.BucketMinutes); AssertEqual("account", series.GroupBy); AssertEqual(1, series.Series.Count);
                    AssertEqual(1, series.Series.SelectMany(s => s.Points).Sum(p => p.ObservationCount));
                    query.Provider = "anthropic";
                    AssertEqual(0L, (await client.GetUsageMissionsAsync(query).ConfigureAwait(false))!.TotalRecords);
                    await ClientContract.ExpectErrorAsync(() => client.CaptureCaptainUsageAsync(captain.Id), "disabled capture", 409, 409).ConfigureAwait(false);
                    await ClientContract.ExpectErrorAsync(() => client.CaptureCaptainUsageAsync("cpt_usage_missing", "hbr_usage_missing"), "foreign ID resolved before host", 404, 404).ConfigureAwait(false);
                    AssertNotNull(await client.GetUsageSnapshotsAsync().ConfigureAwait(false), "null query keeps server defaults");
                    AssertNotNull(await client.GetUsageLatestAsync().ConfigureAwait(false));
                    await ClientContract.ExpectErrorAsync(() => client.GetUsageSeriesAsync(JsonHelper.Deserialize<UsageQuery>("{}")), "required dates", 400, 400).ConfigureAwait(false);
                    await ClientContract.ExpectErrorAsync(() => client.GetUsageMissionsAsync(JsonHelper.Deserialize<UsageQuery>("{}")), "required dates and provider", 400, 400).ConfigureAwait(false);
                }),
                ClientContract.Case(Suite, this, "passive_capture_preserves_original", "SDK preserves passive capture observations and their original timestamps", async (client, fx) =>
                {
                    using DatabaseDriver db = await UsageTestData.OpenAsync(fx).ConfigureAwait(false);
                    string account = "sdk-passive-" + ClientContract.Suffix();
                    Captain captain = await db.Captains.CreateAsync(new Captain { TenantId = Armada.Core.Constants.SystemTenantId, Name = account, Runtime = Armada.Core.Enums.AgentRuntimeEnum.ApiEndpoint }).ConfigureAwait(false);
                    DateTime observed = DateTime.UtcNow.AddMinutes(-20);
                    UsageSnapshot snapshot = await db.UsageSnapshots.CreateAsync(new UsageSnapshot { TenantId = captain.TenantId!, HostId = "host-passive", Runtime = captain.Runtime, AccountKey = account, CapturedUtc = observed, Status = Armada.Core.Enums.UsageSnapshotStatusEnum.Ok, WindowsJson = UsageWindowsJson.Serialize(new List<UsageWindow> { new UsageWindow { Name = "requests", UsedPercent = 25 } }) }, new List<string> { captain.Id }).ConfigureAwait(false);
                    await db.UsageSnapshots.UpsertLatestStateAsync(new UsageLatestState { TenantId = captain.TenantId!, HostId = snapshot.HostId, Runtime = captain.Runtime, AccountKey = account, LastGoodSnapshotId = snapshot.Id, LastGoodCapturedUtc = observed, LastGoodWindowsJson = snapshot.WindowsJson }).ConfigureAwait(false);
                    await db.UsageMemberships.CreateAsync(new UsageAccountMembership { TenantId = captain.TenantId!, CaptainId = captain.Id, HostId = snapshot.HostId, Runtime = captain.Runtime, AccountKey = account, EffectiveFromUtc = observed }).ConfigureAwait(false);
                    UsageCaptureResult result = (await client.CaptureCaptainUsageAsync(captain.Id).ConfigureAwait(false))!;
                    AssertEqual("Passive", result.Status); AssertNull(result.JobId, "passive sources launch no probe");
                    AssertEqual(snapshot.Id, result.Objects!.Single().Id);
                    AssertEqual(observed, result.Objects.Single().CapturedUtc, "capture does not refresh an old observation");
                    AssertEqual(25d, result.Objects.Single().Windows.Single().UsedPercent!.Value);
                    await ClientContract.ExpectErrorAsync(() => client.CaptureCaptainUsageAsync(captain.Id, "hbr_not_visible"), "passive capture validates host before returning", 404, 404).ConfigureAwait(false);
                })
            });
        }
    }
}

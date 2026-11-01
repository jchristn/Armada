namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>AC4 live REST query defaults, validation and account-owned historical attribution.</summary>
    public sealed class UsageSuite : IArmadaTestSuite
    {
        private const string Suite = "E2E.Usage";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Suite, "Usage queries and contracts", new List<TestCaseDescriptor>
            {
                new TestCaseDescriptor(Suite, "latest_batch_captains", "Latest batch filters before paging, deduplicates shared accounts and authorizes every ID", async ct =>
                {
                    E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                    E2ETenantUser owner = await E2ETenantUser.CreateAsync(fx.AuthClient, "usage-batch").ConfigureAwait(false);
                    E2ETenantUser foreign = await E2ETenantUser.CreateAsync(fx.AuthClient, "usage-batch-foreign").ConfigureAwait(false);
                    using HttpClient client = owner.CreateClient(fx.BaseUrl);
                    using DatabaseDriver db = await UsageTestData.OpenAsync(fx).ConfigureAwait(false);
                    Captain a = await db.Captains.CreateAsync(new Captain { TenantId = owner.TenantId, Name = "a" }).ConfigureAwait(false);
                    Captain b = await db.Captains.CreateAsync(new Captain { TenantId = owner.TenantId, Name = "b" }).ConfigureAwait(false);
                    Captain empty = await db.Captains.CreateAsync(new Captain { TenantId = owner.TenantId, Name = "empty" }).ConfigureAwait(false);
                    Captain hidden = await db.Captains.CreateAsync(new Captain { TenantId = foreign.TenantId, Name = "hidden" }).ConfigureAwait(false);
                    DateTime now = DateTime.UtcNow.AddMinutes(-1);
                    foreach (string account in new[] { "unrelated", "selected" })
                    {
                        UsageSnapshot sample = await UsageTestData.SnapshotAsync(db, owner.TenantId, now, account, new List<string> { a.Id, b.Id }).ConfigureAwait(false);
                        await db.UsageSnapshots.UpsertLatestStateAsync(new UsageLatestState { TenantId = owner.TenantId, HostId = sample.HostId, Runtime = sample.Runtime, Provider = sample.Provider, AccountKey = account, LastGoodStatus = UsageSnapshotStatusEnum.Ok, LastGoodSnapshotId = sample.Id, LastGoodCapturedUtc = now, LastGoodWindowsJson = sample.WindowsJson }).ConfigureAwait(false);
                        if (account == "selected")
                            foreach (Captain captain in new[] { a, b })
                                await db.UsageMemberships.CreateAsync(new UsageAccountMembership { TenantId = owner.TenantId, CaptainId = captain.Id, HostId = sample.HostId, Runtime = sample.Runtime, Provider = sample.Provider, AccountKey = account, EffectiveFromUtc = now.AddHours(-1) }).ConfigureAwait(false);
                    }
                    string path = "/api/v1/usage/latest?captainIds=" + a.Id + "," + b.Id + "," + empty.Id + "&pageSize=1";
                    EnumerationResult<UsageSnapshotView> result = await UsageTestData.GetAsync<EnumerationResult<UsageSnapshotView>>(client, path).ConfigureAwait(false);
                    AssertEqual(1L, result.TotalRecords, "batch filter happens before paging");
                    AssertEqual("selected", result.Objects.Single().AccountKey);
                    AssertEqual(2, result.Objects.Single().CaptainIds.Count, "shared account appears once and keeps membership");
                    EnumerationResult<UsageSnapshotView> missing = await UsageTestData.GetAsync<EnumerationResult<UsageSnapshotView>>(client, "/api/v1/usage/latest?captainIds=" + empty.Id).ConfigureAwait(false);
                    AssertEqual(0L, missing.TotalRecords, "no sample is not zero usage");
                    using (HttpResponseMessage openApiResponse = await fx.AuthClient.GetAsync("/openapi.json").ConfigureAwait(false))
                    {
                        openApiResponse.EnsureSuccessStatusCode();
                        using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(await openApiResponse.Content.ReadAsStringAsync().ConfigureAwait(false));
                        System.Text.Json.JsonElement parameter = document.RootElement.GetProperty("paths").GetProperty("/api/v1/usage/latest").GetProperty("get").GetProperty("parameters").EnumerateArray().Single(p => p.GetProperty("name").GetString() == "captainIds");
                        AssertEqual("query", parameter.GetProperty("in").GetString());
                        AssertTrue(parameter.GetProperty("description").GetString()!.Contains("1..100"), "batch bounds are published");
                    }
                    List<string> maximumBatch = new List<string> { a.Id, b.Id, empty.Id };
                    for (int n = maximumBatch.Count; n < 100; n++)
                        maximumBatch.Add((await db.Captains.CreateAsync(new Captain { TenantId = owner.TenantId, Name = "batch-boundary-" + n }).ConfigureAwait(false)).Id);
                    EnumerationResult<UsageSnapshotView> maximum = await UsageTestData.GetAsync<EnumerationResult<UsageSnapshotView>>(client, "/api/v1/usage/latest?captainIds=" + String.Join(",", maximumBatch)).ConfigureAwait(false);
                    AssertEqual(1L, maximum.TotalRecords, "100 visible IDs is accepted without inventing rows for missing observations");
                    await UsageTestData.StatusAsync(client, path + "&tenantId=" + foreign.TenantId, HttpStatusCode.Forbidden).ConfigureAwait(false);
                    await UsageTestData.StatusAsync(fx.AuthClient, path, HttpStatusCode.NotFound).ConfigureAwait(false);
                    EnumerationResult<UsageSnapshotView> admin = await UsageTestData.GetAsync<EnumerationResult<UsageSnapshotView>>(fx.AuthClient, path + "&tenantId=" + owner.TenantId).ConfigureAwait(false);
                    AssertEqual(1L, admin.TotalRecords, "global admin must explicitly select the batch tenant");
                    foreach (string ids in new[] { "", a.Id + ",", a.Id + "," + a.Id, String.Join(",", Enumerable.Range(0, 101).Select(n => "cpt_" + n)) })
                        await UsageTestData.StatusAsync(client, "/api/v1/usage/latest?captainIds=" + ids, HttpStatusCode.BadRequest).ConfigureAwait(false);
                    await UsageTestData.StatusAsync(client, path + "&captainId=" + a.Id, HttpStatusCode.BadRequest).ConfigureAwait(false);
                    await UsageTestData.StatusAsync(client, path.Replace(empty.Id, hidden.Id), HttpStatusCode.NotFound).ConfigureAwait(false);
                    await UsageTestData.StatusAsync(client, path.Replace(empty.Id, hidden.Id) + "&bucketMinutes=bad", HttpStatusCode.NotFound).ConfigureAwait(false);
                    EnumerationResult<UsageSnapshotView> encoded = await UsageTestData.GetAsync<EnumerationResult<UsageSnapshotView>>(client, path.Replace(",", "%2C")).ConfigureAwait(false);
                    AssertEqual(1L, encoded.TotalRecords, "encoded batch separator is decoded exactly once");
                    await UsageTestData.StatusAsync(client, path.Replace(empty.Id, "cpt_missing"), HttpStatusCode.NotFound).ConfigureAwait(false);
                    await UsageTestData.StatusAsync(fx.UnauthClient, path, HttpStatusCode.Unauthorized).ConfigureAwait(false);
                    await UsageTestData.StatusAsync(client, "/api/v1/usage/snapshots?captainIds=" + a.Id, HttpStatusCode.BadRequest).ConfigureAwait(false);
                    foreach (string route in new[] { "series", "missions" })
                        await UsageTestData.StatusAsync(client, "/api/v1/usage/" + route + "?captainIds=" + a.Id, HttpStatusCode.BadRequest).ConfigureAwait(false);
                }),
                new TestCaseDescriptor(Suite, "empty_defaults_and_errors", "Empty reads have bounded defaults and invalid inputs use HTTP errors", async ct =>
                {
                    E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                    E2ETenantUser user = await E2ETenantUser.CreateAsync(fx.AuthClient, "usage-empty").ConfigureAwait(false);
                    using HttpClient client = user.CreateClient(fx.BaseUrl);
                    EnumerationResult<UsageSnapshot> snapshots = await UsageTestData.GetAsync<EnumerationResult<UsageSnapshot>>(client, "/api/v1/usage/snapshots").ConfigureAwait(false);
                    AssertEqual(1, snapshots.PageNumber); AssertEqual(25, snapshots.PageSize); AssertEqual(0L, snapshots.TotalRecords);
                    AssertEqual(0, snapshots.Objects.Count);
                    EnumerationResult<UsageSnapshot> latest = await UsageTestData.GetAsync<EnumerationResult<UsageSnapshot>>(client, "/api/v1/usage/latest").ConfigureAwait(false);
                    AssertEqual(25, latest.PageSize); AssertEqual(0L, latest.TotalRecords);
                    string range = UsageTestData.Range(DateTime.UtcNow.AddHours(-1), DateTime.UtcNow);
                    UsageSeriesResult series = await UsageTestData.GetAsync<UsageSeriesResult>(client, "/api/v1/usage/series?" + range).ConfigureAwait(false);
                    AssertEqual(0, series.Series.Count, "no phantom utilization"); AssertEqual(5, series.BucketMinutes); AssertEqual("account", series.GroupBy);
                    UsageMissionResult missions = await UsageTestData.GetAsync<UsageMissionResult>(client, "/api/v1/usage/missions?provider=anthropic&" + range).ConfigureAwait(false);
                    AssertEqual(0L, missions.TotalRecords); AssertEqual(25, missions.PageSize);
                    foreach (string path in new[] { "/api/v1/usage/snapshots?pageSize=0", "/api/v1/usage/snapshots?pageSize=101", "/api/v1/usage/snapshots?pageNumber=0", "/api/v1/usage/snapshots?from=bad", "/api/v1/usage/series", "/api/v1/usage/missions?" + range, "/api/v1/usage/series?" + range + "&bucketMinutes=7", "/api/v1/usage/series?" + range + "&metric=bogus", "/api/v1/usage/missions?" + range + "&provider=anthropic&sortBy=bogus", "/api/v1/usage/snapshots?" + UsageTestData.Range(DateTime.UtcNow, DateTime.UtcNow.AddDays(-1)), "/api/v1/usage/snapshots?" + UsageTestData.Range(DateTime.UtcNow.AddDays(-91), DateTime.UtcNow) })
                        await UsageTestData.StatusAsync(client, path, HttpStatusCode.BadRequest).ConfigureAwait(false);
                    foreach (string path in new[] { "/api/v1/usage/snapshots", "/api/v1/usage/latest", "/api/v1/usage/series?" + range, "/api/v1/usage/missions?provider=anthropic&" + range })
                        await UsageTestData.StatusAsync(fx.UnauthClient, path, HttpStatusCode.Unauthorized).ConfigureAwait(false);
                }),
                new TestCaseDescriptor(Suite, "account_owned_samples_half_open", "Shared captains do not duplicate canonical samples and ranges are half-open", async ct =>
                {
                    E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                    E2ETenantUser user = await E2ETenantUser.CreateAsync(fx.AuthClient, "usage-account").ConfigureAwait(false);
                    using HttpClient client = user.CreateClient(fx.BaseUrl);
                    using DatabaseDriver db = await UsageTestData.OpenAsync(fx).ConfigureAwait(false);
                    Captain first = await db.Captains.CreateAsync(new Captain { TenantId = user.TenantId, Name = "first" }).ConfigureAwait(false);
                    Captain second = await db.Captains.CreateAsync(new Captain { TenantId = user.TenantId, Name = "second" }).ConfigureAwait(false);
                    DateTime from = DateTime.UtcNow.Date.AddHours(1);
                    UsageSnapshot included = await UsageTestData.SnapshotAsync(db, user.TenantId, from, "shared", new List<string> { first.Id, second.Id }).ConfigureAwait(false);
                    await UsageTestData.SnapshotAsync(db, user.TenantId, from.AddMinutes(5), "shared", new List<string> { first.Id, second.Id }).ConfigureAwait(false);
                    string range = UsageTestData.Range(from, from.AddMinutes(5));
                    EnumerationResult<UsageSnapshot> all = await UsageTestData.GetAsync<EnumerationResult<UsageSnapshot>>(client, "/api/v1/usage/snapshots?" + range).ConfigureAwait(false);
                    AssertEqual(1L, all.TotalRecords); AssertEqual(included.Id, all.Objects.Single().Id);
                    foreach (Captain captain in new[] { first, second })
                    {
                        EnumerationResult<UsageSnapshot> member = await UsageTestData.GetAsync<EnumerationResult<UsageSnapshot>>(client, "/api/v1/usage/snapshots?" + range + "&captainId=" + captain.Id).ConfigureAwait(false);
                        AssertEqual(included.Id, member.Objects.Single().Id, "both subscribers see the same canonical snapshot");
                    }
                    UsageSeriesResult series = await UsageTestData.GetAsync<UsageSeriesResult>(client, "/api/v1/usage/series?" + range + "&groupBy=provider").ConfigureAwait(false);
                    AssertEqual(1, series.Series.Count); AssertEqual(1, series.Series.Single().Points.Single().ObservationCount);
                    AssertEqual(62.0, series.Series.Single().Points.Single().MeanPercent);
                }),
                new TestCaseDescriptor(Suite, "latest_survives_raw_retention_and_respects_range", "Latest retains member/harbor identity after raw expiry and applies requested bounds", async ct =>
                {
                    E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                    E2ETenantUser owner = await E2ETenantUser.CreateAsync(fx.AuthClient, "usage-retained").ConfigureAwait(false);
                    using HttpClient client = owner.CreateClient(fx.BaseUrl);
                    using DatabaseDriver db = await UsageTestData.OpenAsync(fx).ConfigureAwait(false);
                    Captain captain = await db.Captains.CreateAsync(new Captain { TenantId = owner.TenantId, Name = "retained-captain" }).ConfigureAwait(false);
                    Harbor harbor = await db.Harbors.CreateAsync(new Harbor { TenantId = owner.TenantId, Name = "retained-host" }).ConfigureAwait(false);
                    DateTime observed = DateTime.UtcNow.AddDays(-8);
                    UsageSnapshot snapshot = new UsageSnapshot { TenantId = owner.TenantId, HostId = harbor.Id, HarborId = harbor.Id, AccountKey = "retained", Provider = "anthropic", CapturedUtc = observed, ReceivedUtc = observed, Status = UsageSnapshotStatusEnum.Ok, WindowsJson = UsageWindowsJson.Serialize(new List<UsageWindow> { new UsageWindow { Name = "five_hour", UsedPercent = 42 } }) };
                    await db.UsageSnapshots.CreateAsync(snapshot, new List<string> { captain.Id }).ConfigureAwait(false);
                    await db.UsageMemberships.CreateAsync(new UsageAccountMembership { TenantId = owner.TenantId, CaptainId = captain.Id, HostId = harbor.Id, AccountKey = "retained", Provider = "anthropic", EffectiveFromUtc = observed.AddHours(-1) }).ConfigureAwait(false);
                    await db.UsageSnapshots.UpsertLatestStateAsync(new UsageLatestState { TenantId = owner.TenantId, HostId = harbor.Id, AccountKey = "retained", Provider = "anthropic", LastGoodSnapshotId = snapshot.Id, LastGoodCapturedUtc = observed, LastGoodWindowsJson = snapshot.WindowsJson }).ConfigureAwait(false);
                    await db.UsageSnapshots.DeleteOlderThanAsync(DateTime.UtcNow.AddDays(-7)).ConfigureAwait(false);
                    AssertNull(await db.UsageSnapshots.ReadAsync(snapshot.Id, owner.TenantId).ConfigureAwait(false), "fixture actually expired raw snapshot");
                    string identity = "&captainId=" + captain.Id + "&harborId=" + harbor.Id;
                    EnumerationResult<UsageSnapshotView> retained = await UsageTestData.GetAsync<EnumerationResult<UsageSnapshotView>>(client, "/api/v1/usage/latest?" + UsageTestData.Range(observed.AddHours(-1), observed.AddHours(1)) + identity).ConfigureAwait(false);
                    AssertEqual(1L, retained.TotalRecords); AssertEqual(captain.Id, retained.Objects.Single().CaptainIds.Single());
                    AssertEqual(harbor.Id, retained.Objects.Single().HarborId); AssertEqual(observed, retained.Objects.Single().ObservedUtc); AssertTrue(retained.Objects.Single().Stale);
                    AssertEqual(42.0, retained.Objects.Single().Windows.Single().UsedPercent);
                    EnumerationResult<UsageSnapshotView> excluded = await UsageTestData.GetAsync<EnumerationResult<UsageSnapshotView>>(client, "/api/v1/usage/latest?" + UsageTestData.Range(observed.AddMinutes(1), observed.AddHours(1)) + identity).ConfigureAwait(false);
                    AssertEqual(0L, excluded.TotalRecords, "latest does not ignore explicit time filters");
                }),
                new TestCaseDescriptor(Suite, "mission_metadata_and_stable_pages", "Mission joins are tenant-scoped and bounded pages use stable ordering", async ct =>
                {
                    E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                    E2ETenantUser owner = await E2ETenantUser.CreateAsync(fx.AuthClient, "usage-joins").ConfigureAwait(false);
                    E2ETenantUser other = await E2ETenantUser.CreateAsync(fx.AuthClient, "usage-foreign-join").ConfigureAwait(false);
                    using HttpClient client = owner.CreateClient(fx.BaseUrl);
                    using HttpClient otherClient = other.CreateClient(fx.BaseUrl);
                    using DatabaseDriver db = await UsageTestData.OpenAsync(fx).ConfigureAwait(false);
                    Captain captain = await db.Captains.CreateAsync(new Captain { TenantId = owner.TenantId, Name = "own-captain" }).ConfigureAwait(false);
                    Vessel vessel = await db.Vessels.CreateAsync(new Vessel { TenantId = owner.TenantId, Name = "own-vessel", RepoUrl = "https://example.invalid/usage.git" }).ConfigureAwait(false);
                    Mission mission = await db.Missions.CreateAsync(new Mission { TenantId = owner.TenantId, Title = "private-mission-title", VesselId = vessel.Id, CaptainId = captain.Id, Status = MissionStatusEnum.Complete }).ConfigureAwait(false);
                    DateTime from = DateTime.UtcNow.AddHours(-1);
                    await UsageTestData.TokenAsync(db, owner.TenantId, captain.Id, from.AddMinutes(1), "joined", "membership", "mission", mission.Id, 20).ConfigureAwait(false);
                    await UsageTestData.TokenAsync(db, owner.TenantId, captain.Id, from.AddMinutes(2), "joined", "membership", "mission", "msn_deleted", 10).ConfigureAwait(false);
                    await UsageTestData.TokenAsync(db, other.TenantId, captain.Id, from.AddMinutes(1), "joined", "membership", "mission", mission.Id, 20).ConfigureAwait(false);
                    string path = "/api/v1/usage/missions?provider=anthropic&pageSize=1&" + UsageTestData.Range(from, from.AddMinutes(5));
                    UsageMissionResult first = await UsageTestData.GetAsync<UsageMissionResult>(client, path).ConfigureAwait(false);
                    AssertEqual(2L, first.TotalRecords); AssertEqual(2, first.TotalPages); AssertEqual(1, first.Objects.Count);
                    AssertEqual(mission.Id, first.Objects.Single().MissionId); AssertEqual("private-mission-title", first.Objects.Single().Title);
                    AssertEqual("own-vessel", first.Objects.Single().VesselName); AssertEqual("own-captain", first.Objects.Single().CaptainName);
                    AssertEqual("/missions/" + mission.Id, first.Objects.Single().MissionUrl);
                    UsageMissionResult second = await UsageTestData.GetAsync<UsageMissionResult>(client, path + "&pageNumber=2").ConfigureAwait(false);
                    AssertEqual("msn_deleted", second.Objects.Single().MissionId); AssertEqual(10L, second.Objects.Single().TotalTokens);
                    UsageMissionResult foreign = await UsageTestData.GetAsync<UsageMissionResult>(otherClient, path).ConfigureAwait(false);
                    AssertEqual(1L, foreign.TotalRecords); AssertNull(foreign.Objects.Single().Title); AssertNull(foreign.Objects.Single().CaptainName); AssertNull(foreign.Objects.Single().VesselName); AssertNull(foreign.Objects.Single().MissionUrl);
                }),
                new TestCaseDescriptor(Suite, "missions_use_persisted_attribution", "Completion buckets preserve old account attribution, exclude nonmission work and retain safe deleted rows", async ct =>
                {
                    E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                    E2ETenantUser user = await E2ETenantUser.CreateAsync(fx.AuthClient, "usage-missions").ConfigureAwait(false);
                    using HttpClient client = user.CreateClient(fx.BaseUrl);
                    using DatabaseDriver db = await UsageTestData.OpenAsync(fx).ConfigureAwait(false);
                    Captain captain = await db.Captains.CreateAsync(new Captain { TenantId = user.TenantId, Name = "account moved" }).ConfigureAwait(false);
                    DateTime from = DateTime.UtcNow.Date.AddHours(2);
                    UsageAccountMembership old = await db.UsageMemberships.CreateAsync(new UsageAccountMembership { TenantId = user.TenantId, CaptainId = captain.Id, HostId = "host", Provider = "anthropic", AccountKey = "old", EffectiveFromUtc = from.AddHours(-1), EffectiveToUtc = from.AddMinutes(1) }).ConfigureAwait(false);
                    await db.UsageMemberships.CreateAsync(new UsageAccountMembership { TenantId = user.TenantId, CaptainId = captain.Id, HostId = "host", Provider = "anthropic", AccountKey = "new", EffectiveFromUtc = from.AddMinutes(1) }).ConfigureAwait(false);
                    await UsageTestData.TokenAsync(db, user.TenantId, captain.Id, from, "old", old.Id, "mission", "msn_deleted", 30).ConfigureAwait(false);
                    await UsageTestData.TokenAsync(db, user.TenantId, captain.Id, from.AddSeconds(1), "old", old.Id, "mission", "msn_deleted", 20).ConfigureAwait(false);
                    await UsageTestData.TokenAsync(db, user.TenantId, captain.Id, from.AddMinutes(2), "old", old.Id, "chat", "chat_ignored", 7).ConfigureAwait(false);
                    await UsageTestData.TokenAsync(db, user.TenantId, captain.Id, from.AddMinutes(5), "old", old.Id, "mission", "msn_boundary", 99).ConfigureAwait(false);
                    await UsageTestData.TokenAsync(db, user.TenantId, captain.Id, from.AddMinutes(3), null, null, "mission", "msn_legacy", 11).ConfigureAwait(false);
                    string range = UsageTestData.Range(from, from.AddMinutes(5));
                    UsageMissionResult result = await UsageTestData.GetAsync<UsageMissionResult>(client, "/api/v1/usage/missions?" + range + "&provider=anthropic&accountKey=old").ConfigureAwait(false);
                    AssertEqual(1L, result.TotalRecords); AssertEqual(50L, result.Objects.Single().TotalTokens, "retries sum once per persisted token record");
                    AssertEqual(old.Id, result.Objects.Single().MembershipId); AssertEqual("old", result.Objects.Single().AccountKey);
                    AssertNull(result.Objects.Single().MissionUrl, "deleted mission has no invented detail link"); AssertNull(result.Objects.Single().Title);
                    AssertEqual(1L, result.Coverage.ExcludedUnattributedRecords);
                    UsageSeriesResult tokens = await UsageTestData.GetAsync<UsageSeriesResult>(client, "/api/v1/usage/series?" + range + "&provider=anthropic&accountKey=old&metric=tokens").ConfigureAwait(false);
                    AssertEqual(57L, tokens.Series.SelectMany(s => s.Points).Sum(p => p.TotalTokens), "cached tokens are not added a second time");
                    AssertEqual(1L, tokens.Coverage.NonMissionRecords);
                    UsageMissionResult moved = await UsageTestData.GetAsync<UsageMissionResult>(client, "/api/v1/usage/missions?" + range + "&provider=anthropic&accountKey=new").ConfigureAwait(false);
                    AssertEqual(0L, moved.TotalRecords, "current captain membership cannot rewrite historical work");
                }),
                new TestCaseDescriptor(Suite, "captain_usage_edit_lifecycle", "Captain create/edit/save keeps omitted usage fields, applies explicit ones and rejects invalid Custom commands before storage", async ct =>
                {
                    E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                    E2ETenantUser owner = await E2ETenantUser.CreateAsync(fx.AuthClient, "usage-edit").ConfigureAwait(false);
                    using HttpClient client = owner.CreateClient(fx.BaseUrl);
                    using DatabaseDriver db = await UsageTestData.OpenAsync(fx).ConfigureAwait(false);
                    string command = "{\"executable\":\"/opt/usage/read-quota\",\"arguments\":[\"--json\"]}";
                    Captain seeded = new Captain { TenantId = owner.TenantId, UserId = owner.UserId, Name = "usage-edit-probe", UsageCaptureEnabled = true, UsageCaptureIntervalSeconds = 120, UsageCaptureMode = UsageCaptureModeEnum.Custom, UsageCaptureCommand = command };
                    seeded = await db.Captains.CreateAsync(seeded).ConfigureAwait(false);
                    string path = "/api/v1/captains/" + seeded.Id;

                    // Unrelated edit: every usage field survives (this failed before the fix: enabled read back false).
                    await UsageTestData.PutAsync(client, path, "{\"Name\":\"usage-edit-probe-renamed\"}", HttpStatusCode.OK).ConfigureAwait(false);
                    Captain renamed = await UsageTestData.GetAsync<Captain>(client, path).ConfigureAwait(false);
                    AssertEqual("usage-edit-probe-renamed", renamed.Name);
                    AssertTrue(renamed.UsageCaptureEnabled, "unrelated name edit must preserve enabled capture");
                    AssertEqual(120, renamed.UsageCaptureIntervalSeconds, "unrelated edit preserves the interval");
                    AssertEqual(UsageCaptureModeEnum.Custom, renamed.UsageCaptureMode, "unrelated edit preserves Custom mode");
                    AssertEqual(command, renamed.UsageCaptureCommand, "unrelated edit preserves the Custom command");

                    // Invalid Custom commands are rejected before storage; the stored configuration is unchanged.
                    foreach (string invalid in new[]
                    {
                        "{\"UsageCaptureMode\":\"Custom\",\"UsageCaptureCommand\":null}",
                        "{\"UsageCaptureMode\":\"Custom\",\"UsageCaptureCommand\":\"not json\"}",
                        "{\"UsageCaptureMode\":\"Custom\",\"UsageCaptureCommand\":\"{\\\"executable\\\":\\\"read-quota; rm -rf /\\\"}\"}",
                        "{\"UsageCaptureMode\":\"Custom\",\"UsageCaptureCommand\":\"{\\\"executable\\\":\\\"  \\\"}\"}",
                        "{\"UsageCaptureEnabled\":false,\"UsageCaptureMode\":\"Custom\",\"UsageCaptureCommand\":\"{\\\"executable\\\":\\\"/bin/q\\\",\\\"arguments\\\":[1]}\"}"
                    })
                    {
                        await UsageTestData.PutAsync(client, path, invalid, HttpStatusCode.BadRequest).ConfigureAwait(false);
                        Captain unchanged = await UsageTestData.GetAsync<Captain>(client, path).ConfigureAwait(false);
                        AssertTrue(unchanged.UsageCaptureEnabled, "rejected edit stores nothing: " + invalid);
                        AssertEqual(command, unchanged.UsageCaptureCommand, "rejected edit keeps the command: " + invalid);
                    }

                    // Explicit values apply: interval clamps to the 60 second floor, disable is honored.
                    await UsageTestData.PutAsync(client, path, "{\"UsageCaptureEnabled\":false,\"UsageCaptureIntervalSeconds\":5}", HttpStatusCode.OK).ConfigureAwait(false);
                    Captain disabled = await UsageTestData.GetAsync<Captain>(client, path).ConfigureAwait(false);
                    AssertFalse(disabled.UsageCaptureEnabled, "explicit false disables capture");
                    AssertEqual(60, disabled.UsageCaptureIntervalSeconds, "explicit interval is clamped, not ignored");
                    AssertEqual(command, disabled.UsageCaptureCommand, "omitted command survives an explicit toggle");

                    // Leaving Custom clears the command; returning to Custom without a command is rejected.
                    await UsageTestData.PutAsync(client, path, "{\"UsageCaptureMode\":\"Auto\"}", HttpStatusCode.OK).ConfigureAwait(false);
                    Captain auto = await UsageTestData.GetAsync<Captain>(client, path).ConfigureAwait(false);
                    AssertEqual(UsageCaptureModeEnum.Auto, auto.UsageCaptureMode);
                    AssertNull(auto.UsageCaptureCommand, "Auto mode stores no command");
                    await UsageTestData.PutAsync(client, path, "{\"UsageCaptureMode\":\"Custom\"}", HttpStatusCode.BadRequest).ConfigureAwait(false);
                    AssertEqual(UsageCaptureModeEnum.Auto, (await UsageTestData.GetAsync<Captain>(client, path).ConfigureAwait(false)).UsageCaptureMode, "rejected switch to Custom stores nothing");

                    // Create: invalid Custom is rejected; a minimal body keeps disabled/300/Auto defaults.
                    await UsageTestData.PostAsync(client, "/api/v1/captains", "{\"Name\":\"usage-create-bad\",\"UsageCaptureEnabled\":true,\"UsageCaptureMode\":\"Custom\"}", HttpStatusCode.BadRequest).ConfigureAwait(false);
                    string created = await UsageTestData.PostAsync(client, "/api/v1/captains", "{\"Name\":\"usage-create-default\"}", HttpStatusCode.Created).ConfigureAwait(false);
                    Captain minimal = JsonHelper.Deserialize<Captain>(created);
                    AssertFalse(minimal.UsageCaptureEnabled, "capture defaults off");
                    AssertEqual(300, minimal.UsageCaptureIntervalSeconds);
                    AssertEqual(UsageCaptureModeEnum.Auto, minimal.UsageCaptureMode);
                    string createdCustom = await UsageTestData.PostAsync(client, "/api/v1/captains", "{\"Name\":\"usage-create-custom\",\"UsageCaptureEnabled\":true,\"UsageCaptureIntervalSeconds\":600,\"UsageCaptureMode\":\"Custom\",\"UsageCaptureCommand\":" + System.Text.Json.JsonSerializer.Serialize(command) + "}", HttpStatusCode.Created).ConfigureAwait(false);
                    Captain custom = await UsageTestData.GetAsync<Captain>(client, "/api/v1/captains/" + JsonHelper.Deserialize<Captain>(createdCustom).Id).ConfigureAwait(false);
                    AssertTrue(custom.UsageCaptureEnabled, "create stores enabled capture");
                    AssertEqual(600, custom.UsageCaptureIntervalSeconds);
                    AssertEqual(command, custom.UsageCaptureCommand, "create stores the Custom command");
                }),
                new TestCaseDescriptor(Suite, "captain_usage_edit_invalidates_capture", "Saved captain usage configuration changes invalidate in-flight capture and rebuild schedules", ct =>
                {
                    string server = File.ReadAllText(Path.Combine(DashboardSource.RepoRoot(), "src", "Armada.Server", "ArmadaServer.cs"));
                    int routes = server.IndexOf("new CaptainRoutes(", StringComparison.Ordinal);
                    AssertTrue(routes >= 0, "CaptainRoutes is composed");
                    string composition = server.Substring(routes, Math.Min(600, server.Length - routes));
                    AssertContains("OnUsageCaptureConfigurationChanged", composition, "captain routes receive the usage invalidation callback");
                    AssertContains("_UsageCapture.Invalidate()", composition, "callback discards in-flight attempts");
                    AssertContains("_UsageCaptureScheduler.RebuildAsync()", composition, "callback rebuilds schedules");
                    string routesSource = File.ReadAllText(Path.Combine(DashboardSource.RepoRoot(), "src", "Armada.Server", "Routes", "CaptainRoutes.cs"));
                    AssertContains("CaptainUsageCaptureValidator.ConfigurationChanged(existing, updated)", routesSource, "update detects configuration changes against the stored record");
                    return Task.CompletedTask;
                })
            });
        }
    }

    internal static class UsageTestData
    {
        internal static async Task<DatabaseDriver> OpenAsync(E2EServerFixture fx)
        {
            LoggingModule logging = new LoggingModule(); logging.Settings.EnableConsole = false;
            DatabaseDriver db = DatabaseDriverFactory.Create(new DatabaseSettings { Type = DatabaseTypeEnum.Sqlite, Filename = Path.Combine(fx.TempDir, "armada.db") }, logging);
            await db.InitializeAsync().ConfigureAwait(false); return db;
        }

        internal static string Range(DateTime from, DateTime to)
        {
            return "from=" + Uri.EscapeDataString(from.ToString("O")) + "&to=" + Uri.EscapeDataString(to.ToString("O"));
        }

        internal static async Task<T> GetAsync<T>(HttpClient client, string path)
        {
            using HttpResponseMessage response = await client.GetAsync(path).ConfigureAwait(false);
            AssertEqual(HttpStatusCode.OK, response.StatusCode, path + ": " + await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            return await JsonHelper.DeserializeAsync<T>(response).ConfigureAwait(false);
        }

        internal static async Task<string> PutAsync(HttpClient client, string path, string json, HttpStatusCode expected)
        {
            using StringContent content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await client.PutAsync(path, content).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            AssertEqual(expected, response.StatusCode, "PUT " + path + " " + json + ": " + body);
            return body;
        }

        internal static async Task<string> PostAsync(HttpClient client, string path, string json, HttpStatusCode expected)
        {
            using StringContent content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await client.PostAsync(path, content).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            AssertEqual(expected, response.StatusCode, "POST " + path + " " + json + ": " + body);
            return body;
        }

        internal static async Task StatusAsync(HttpClient client, string path, HttpStatusCode expected)
        {
            using HttpResponseMessage response = await client.GetAsync(path).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            AssertEqual(expected, response.StatusCode, path + ": " + body);
            UsageError error = JsonHelper.Deserialize<UsageError>(body);
            AssertEqual((int)expected, error.StatusCode, "ApiErrorResponse status matches HTTP status");
            AssertFalse(String.IsNullOrWhiteSpace(error.Error), "ApiErrorResponse has an error code");
            AssertFalse(String.IsNullOrWhiteSpace(error.Message), "ApiErrorResponse has a reason");
        }

        private sealed class UsageError
        {
            public string? Error { get; set; }
            public int StatusCode { get; set; }
            public string? Message { get; set; }
        }

        internal static Task<UsageSnapshot> SnapshotAsync(DatabaseDriver db, string tenant, DateTime time, string account, List<string> captains)
        {
            return db.UsageSnapshots.CreateAsync(new UsageSnapshot { TenantId = tenant, HostId = "host", AccountKey = account, Provider = "anthropic", CapturedUtc = time, ReceivedUtc = time, Status = UsageSnapshotStatusEnum.Ok, Source = UsageCaptureSourceEnum.Custom, WindowsJson = UsageWindowsJson.Serialize(new List<UsageWindow> { new UsageWindow { Name = "five_hour", UsedPercent = 62, Unit = "percent" } }), RawExcerpt = "tenant-admin-only-diagnostic" }, captains);
        }

        internal static Task<TokenUsageRecord> TokenAsync(DatabaseDriver db, string tenant, string captain, DateTime time, string? account, string? membership, string source, string sourceId, long total)
        {
            return db.TokenUsage.CreateAsync(new TokenUsageRecord { TenantId = tenant, CaptainId = captain, CreatedUtc = time, Provider = "anthropic", Runtime = "claudecode", HostId = "host", AccountKey = account, MembershipId = membership, Source = source, SourceId = sourceId, InputTokens = total - 1, OutputTokens = 1, CachedTokens = 5, TotalTokens = total });
        }
    }
}

namespace Test.Shared.Suites.Database
{
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Reflection;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Database.Sqlite.Queries;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using Microsoft.Data.Sqlite;
    using Microsoft.Data.SqlClient;
    using MySqlConnector;
    using Npgsql;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for account usage capture persistence (AC1 Database.UsagePersistence): migration 111 on
    /// new and existing databases for every provider, snapshot round-trips including every nullable field,
    /// member links, tenant isolation, effective-dated membership periods, idempotent hourly rollup
    /// upserts, separate latest-state good/error preservation, captain usage-capture settings round-trips
    /// and legacy token records surviving the upgrade with attribution left null. Cases run unchanged
    /// against SQLite, PostgreSQL, MySQL and SQL Server through the shared test database harness; the
    /// upgrade-from-v110 case replays migrations through v110 into an isolated database before invoking
    /// the production initializer, including on server providers.
    /// </summary>
    public sealed class UsagePersistenceSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Database.UsagePersistence";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the usage persistence suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("usage_schema_reaches_latest_migration_on_new_database", "A new database migrates to the usage capture schema", TestTags.Positive, async ct =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    DatabaseDriver driver = testDb.Driver;
                    int latest = driver.GetLatestSchemaVersion();
                    int applied = await driver.GetSchemaVersionAsync(ct).ConfigureAwait(false);
                    AssertTrue(latest >= 111, "latest migration includes usage capture (v111)");
                    AssertEqual(latest, applied, "fresh database is at the latest schema");
                    AssertEqual(0, await driver.GetPendingMigrationCountAsync(ct).ConfigureAwait(false), "no pending migrations after init");
                }
            }));

            cases.Add(Case("usage_snapshot_round_trips_every_nullable_field", "A snapshot round-trips fully populated and minimally populated", TestTags.Positive, async ct =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    DatabaseDriver driver = testDb.Driver;

                    List<UsageWindow> windows = new List<UsageWindow>();
                    windows.Add(new UsageWindow { Name = "five_hour", UsedPercent = 62.5, Used = 62500, Limit = 100000, Unit = "tokens", WindowMinutes = 300, ResetsUtc = new DateTime(2026, 10, 27, 14, 5, 0, DateTimeKind.Utc) });
                    windows.Add(new UsageWindow { Name = "claude-opnet", Model = "claude-opnet-4", UsedPercent = null, Used = 10, Limit = null, Unit = null, WindowMinutes = null, ResetsUtc = null });

                    UsageSnapshot full = new UsageSnapshot();
                    full.TenantId = "ten_usage_full";
                    full.CaptainId = "cpt_usage_trigger";
                    full.HarborId = "hbr_usage_host";
                    full.HostId = "hbr_usage_host";
                    full.Runtime = AgentRuntimeEnum.ClaudeCode;
                    full.Provider = "anthropic";
                    full.AccountKey = "usage-hmac-key-1";
                    full.CapturedUtc = new DateTime(2026, 10, 27, 13, 0, 0, DateTimeKind.Utc);
                    full.ReceivedUtc = new DateTime(2026, 10, 27, 13, 0, 2, DateTimeKind.Utc);
                    full.Status = UsageSnapshotStatusEnum.Ok;
                    full.Source = UsageCaptureSourceEnum.OAuthEndpoint;
                    full.WindowsJson = UsageWindowsJson.Serialize(windows);
                    full.RawExcerpt = "diagnostic: five_hour observed";
                    full.ErrorCode = null;
                    await driver.UsageSnapshots.CreateAsync(full, new List<string> { "cpt_usage_trigger" }, ct).ConfigureAwait(false);

                    UsageSnapshot? read = await driver.UsageSnapshots.ReadAsync(full.Id, "ten_usage_full", ct).ConfigureAwait(false);
                    AssertNotNull(read, "full snapshot readable");
                    AssertEqual(full.TenantId, read!.TenantId, "tenant");
                    AssertEqual(full.CaptainId, read.CaptainId, "triggering captain");
                    AssertEqual(full.HarborId, read.HarborId, "harbor");
                    AssertEqual(full.HostId, read.HostId, "stable host id");
                    AssertEqual(AgentRuntimeEnum.ClaudeCode, read.Runtime, "runtime");
                    AssertEqual("anthropic", read.Provider, "provider");
                    AssertEqual(full.AccountKey, read.AccountKey, "account key");
                    AssertEqual(full.CapturedUtc, read.CapturedUtc, "captured utc");
                    AssertEqual(full.ReceivedUtc, read.ReceivedUtc, "received utc");
                    AssertEqual(UsageSnapshotStatusEnum.Ok, read.Status, "status");
                    AssertEqual(UsageCaptureSourceEnum.OAuthEndpoint, read.Source, "source");
                    AssertContains("\"name\":\"five_hour\"", read.WindowsJson ?? String.Empty, "windows json keeps contract field names");
                    List<UsageWindow> parsed = UsageWindowsJson.Deserialize(read.WindowsJson);
                    AssertEqual(2, parsed.Count, "two windows round-trip");
                    AssertEqual(62.5, parsed[0].UsedPercent, "window percent");
                    AssertEqual("claude-opnet-4", parsed[1].Model, "per-model window keeps its model");
                    AssertNull(parsed[1].UsedPercent, "window null percent stays null");
                    AssertEqual("diagnostic: five_hour observed", read.RawExcerpt, "sanitized excerpt");
                    AssertNull(read.ErrorCode, "no error code on success");

                    UsageSnapshot minimal = new UsageSnapshot();
                    minimal.TenantId = "ten_usage_full";
                    minimal.HostId = "admiral";
                    minimal.Runtime = AgentRuntimeEnum.ApiEndpoint;
                    minimal.Status = UsageSnapshotStatusEnum.AuthMissing;
                    minimal.Source = UsageCaptureSourceEnum.Headers;
                    minimal.ErrorCode = "CredentialUnavailable";
                    await driver.UsageSnapshots.CreateAsync(minimal, null, ct).ConfigureAwait(false);

                    UsageSnapshot? readMinimal = await driver.UsageSnapshots.ReadAsync(minimal.Id, "ten_usage_full", ct).ConfigureAwait(false);
                    AssertNotNull(readMinimal, "minimal snapshot readable");
                    AssertNull(readMinimal!.CaptainId, "captain stays null");
                    AssertNull(readMinimal.HarborId, "harbor stays null");
                    AssertNull(readMinimal.Provider, "provider stays null");
                    AssertNull(readMinimal.AccountKey, "missing identity stays null");
                    AssertNull(readMinimal.WindowsJson, "no windows text");
                    AssertNull(readMinimal.RawExcerpt, "no excerpt");
                    AssertEqual(UsageSnapshotStatusEnum.AuthMissing, readMinimal.Status, "status");
                    AssertEqual("CredentialUnavailable", readMinimal.ErrorCode, "stable error code");
                }
            }));

            cases.Add(Case("usage_snapshot_member_links_cover_captain_filter", "Snapshot member links are stored once and cover the captain filter", TestTags.Positive, async ct =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    DatabaseDriver driver = testDb.Driver;

                    UsageSnapshot snapshot = new UsageSnapshot();
                    snapshot.TenantId = "ten_usage_members";
                    snapshot.HostId = "hbr_shared";
                    snapshot.Runtime = AgentRuntimeEnum.Codex;
                    snapshot.Status = UsageSnapshotStatusEnum.Ok;
                    snapshot.Source = UsageCaptureSourceEnum.SessionLog;
                    snapshot.WindowsJson = null;
                    await driver.UsageSnapshots.CreateAsync(snapshot, new List<string> { "cpt_a", "cpt_a", "cpt_b", " " }, ct).ConfigureAwait(false);

                    List<string> members = await driver.UsageSnapshots.ReadCaptainsAsync(snapshot.Id, "ten_usage_members", ct).ConfigureAwait(false);
                    AssertEqual(2, members.Count, "duplicate member ids stored once");
                    AssertEqual("cpt_a", members[0], "first member");
                    AssertEqual("cpt_b", members[1], "second member");

                    EnumerationResult<UsageSnapshot> byMember = await driver.UsageSnapshots.EnumerateAsync(new UsageSnapshotQuery
                    {
                        TenantId = "ten_usage_members",
                        CaptainId = "cpt_b"
                    }, ct).ConfigureAwait(false);
                    AssertEqual(1L, byMember.TotalRecords, "captain filter matches through the member link");
                    AssertEqual(snapshot.Id, byMember.Objects[0].Id, "member link resolves the snapshot");
                }
            }));

            cases.Add(Case("usage_snapshots_are_tenant_isolated", "Snapshot, latest-state and membership reads stay inside one tenant", TestTags.Negative, async ct =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    DatabaseDriver driver = testDb.Driver;

                    UsageSnapshot snapshot = new UsageSnapshot();
                    snapshot.TenantId = "ten_usage_a";
                    snapshot.HostId = "host_1";
                    snapshot.Runtime = AgentRuntimeEnum.ClaudeCode;
                    snapshot.AccountKey = "usage-hmac-key-tenant";
                    snapshot.Status = UsageSnapshotStatusEnum.Ok;
                    snapshot.Source = UsageCaptureSourceEnum.OAuthEndpoint;
                    await driver.UsageSnapshots.CreateAsync(snapshot, new List<string> { "cpt_tenant_a" }, ct).ConfigureAwait(false);

                    AssertNull(await driver.UsageSnapshots.ReadAsync(snapshot.Id, "ten_usage_b", ct).ConfigureAwait(false), "another tenant cannot read the snapshot by id");

                    EnumerationResult<UsageSnapshot> foreignPage = await driver.UsageSnapshots.EnumerateAsync(new UsageSnapshotQuery { TenantId = "ten_usage_b" }, ct).ConfigureAwait(false);
                    AssertEqual(0L, foreignPage.TotalRecords, "another tenant lists zero snapshots");

                    UsageLatestState state = new UsageLatestState();
                    state.TenantId = "ten_usage_a";
                    state.HostId = "host_1";
                    state.Runtime = AgentRuntimeEnum.ClaudeCode;
                    state.AccountKey = "usage-hmac-key-tenant";
                    state.LastGoodSnapshotId = snapshot.Id;
                    await driver.UsageSnapshots.UpsertLatestStateAsync(state, ct).ConfigureAwait(false);

                    EnumerationResult<UsageLatestState> foreignStates = await driver.UsageSnapshots.EnumerateLatestStatesAsync(new UsageLatestStateQuery { TenantId = "ten_usage_b" }, ct).ConfigureAwait(false);
                    AssertEqual(0L, foreignStates.TotalRecords, "another tenant lists zero latest-state rows");

                    UsageAccountMembership membership = new UsageAccountMembership();
                    membership.TenantId = "ten_usage_a";
                    membership.CaptainId = "cpt_tenant_a";
                    membership.HostId = "host_1";
                    membership.Runtime = AgentRuntimeEnum.ClaudeCode;
                    membership.AccountKey = "usage-hmac-key-tenant";
                    await driver.UsageMemberships.CreateAsync(membership, ct).ConfigureAwait(false);

                    UsageAccountMembership? foreignMembership = await driver.UsageMemberships.ReadAsync(membership.Id, "ten_usage_b", ct).ConfigureAwait(false);
                    AssertNull(foreignMembership, "another tenant cannot read the membership");

                    int deleted = await driver.UsageSnapshots.DeleteByTenantAsync("ten_usage_b", ct).ConfigureAwait(false);
                    AssertEqual(0, deleted, "tenant deletion cascade removes nothing from a foreign tenant");
                    AssertNotNull(await driver.UsageSnapshots.ReadAsync(snapshot.Id, "ten_usage_a", ct).ConfigureAwait(false), "original tenant data survives the foreign cascade");
                }
            }));

            cases.Add(Case("usage_membership_periods_open_close_and_effective_at", "Memberships open, close and answer effective-at queries", TestTags.Positive, async ct =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    DatabaseDriver driver = testDb.Driver;

                    DateTime from = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
                    UsageAccountMembership membership = new UsageAccountMembership();
                    membership.TenantId = "ten_usage_membership";
                    membership.CaptainId = "cpt_moving";
                    membership.HostId = "host_1";
                    membership.Runtime = AgentRuntimeEnum.OpenCode;
                    membership.Provider = "configured";
                    membership.AccountKey = "usage-hmac-key-move";
                    membership.ConfigurationRevision = "rev-1";
                    membership.EffectiveFromUtc = from;
                    await driver.UsageMemberships.CreateAsync(membership, ct).ConfigureAwait(false);

                    List<UsageAccountMembership> active = await driver.UsageMemberships.ReadActiveAsync("ten_usage_membership", "cpt_moving", ct).ConfigureAwait(false);
                    AssertEqual(1, active.Count, "membership is open after creation");
                    AssertNull(active[0].EffectiveToUtc, "open membership has no effective-to");
                    AssertEqual("rev-1", active[0].ConfigurationRevision, "configuration revision round-trips");

                    EnumerationResult<UsageAccountMembership> duringPeriod = await driver.UsageMemberships.EnumerateAsync(new UsageMembershipQuery
                    {
                        TenantId = "ten_usage_membership",
                        CaptainId = "cpt_moving",
                        EffectiveAtUtc = from.AddHours(1)
                    }, ct).ConfigureAwait(false);
                    AssertEqual(1L, duringPeriod.TotalRecords, "membership covers a later instant");

                    DateTime to = new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc);
                    int closed = await driver.UsageMemberships.CloseAsync("ten_usage_membership", "cpt_moving", to, ct).ConfigureAwait(false);
                    AssertEqual(1, closed, "close stamps the open membership");

                    int closedAgain = await driver.UsageMemberships.CloseAsync("ten_usage_membership", "cpt_moving", to, ct).ConfigureAwait(false);
                    AssertEqual(0, closedAgain, "closing again is a no-op");

                    List<UsageAccountMembership> activeAfter = await driver.UsageMemberships.ReadActiveAsync("ten_usage_membership", "cpt_moving", ct).ConfigureAwait(false);
                    AssertEqual(0, activeAfter.Count, "no open membership remains after close");

                    EnumerationResult<UsageAccountMembership> insidePeriod = await driver.UsageMemberships.EnumerateAsync(new UsageMembershipQuery
                    {
                        TenantId = "ten_usage_membership",
                        CaptainId = "cpt_moving",
                        EffectiveAtUtc = from.AddHours(1)
                    }, ct).ConfigureAwait(false);
                    AssertEqual(1L, insidePeriod.TotalRecords, "closed membership still covers its period");

                    EnumerationResult<UsageAccountMembership> beforePeriod = await driver.UsageMemberships.EnumerateAsync(new UsageMembershipQuery
                    {
                        TenantId = "ten_usage_membership",
                        CaptainId = "cpt_moving",
                        EffectiveAtUtc = from.AddHours(-1)
                    }, ct).ConfigureAwait(false);
                    AssertEqual(0L, beforePeriod.TotalRecords, "membership does not cover instants before it opened");

                    EnumerationResult<UsageAccountMembership> afterPeriod = await driver.UsageMemberships.EnumerateAsync(new UsageMembershipQuery
                    {
                        TenantId = "ten_usage_membership",
                        CaptainId = "cpt_moving",
                        EffectiveAtUtc = to.AddHours(1)
                    }, ct).ConfigureAwait(false);
                    AssertEqual(0L, afterPeriod.TotalRecords, "membership does not cover instants after it closed (half-open)");

                    UsageAccountMembership? history = await driver.UsageMemberships.ReadAsync(membership.Id, "ten_usage_membership", ct).ConfigureAwait(false);
                    AssertNotNull(history, "closed membership stays readable as attribution history");
                    AssertEqual(to, history!.EffectiveToUtc, "effective-to persisted");
                }
            }));

            cases.Add(Case("usage_rollup_upsert_is_idempotent_per_natural_key", "Rollup upserts replace the natural key row instead of accumulating", TestTags.Positive, async ct =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    DatabaseDriver driver = testDb.Driver;

                    DateTime hour = new DateTime(2026, 10, 27, 13, 0, 0, DateTimeKind.Utc);
                    UsageHourlyRollup first = NewRollup("ten_usage_rollup", hour, 10, 40.0);
                    await driver.UsageRollups.UpsertAsync(first, ct).ConfigureAwait(false);

                    UsageHourlyRollup second = NewRollup("ten_usage_rollup", hour, 12, 55.0);
                    second.OkCount = 12;
                    second.CoverageSeconds = 900.0;
                    await driver.UsageRollups.UpsertAsync(second, ct).ConfigureAwait(false);

                    EnumerationResult<UsageHourlyRollup> page = await driver.UsageRollups.EnumerateAsync(new UsageRollupQuery
                    {
                        TenantId = "ten_usage_rollup",
                        AccountKey = "usage-hmac-key-rollup",
                        FromHourUtc = hour,
                        ToHourUtc = hour.AddHours(1)
                    }, ct).ConfigureAwait(false);

                    AssertEqual(1L, page.TotalRecords, "natural key upsert does not add a second row");
                    UsageHourlyRollup stored = page.Objects[0];
                    AssertEqual(12, stored.SampleCount, "sample count replaced with the recomputed value");
                    AssertEqual(55.0, stored.LastPercent, "last percent replaced with the recomputed value");
                    AssertEqual(12, stored.OkCount, "ok count replaced");
                    AssertEqual(900.0, stored.CoverageSeconds, "coverage seconds replaced");

                    UsageHourlyRollup distinct = NewRollup("ten_usage_rollup", hour, 3, 30.0);
                    distinct.Model = "claude-opnet-4";
                    await driver.UsageRollups.UpsertAsync(distinct, ct).ConfigureAwait(false);

                    EnumerationResult<UsageHourlyRollup> both = await driver.UsageRollups.EnumerateAsync(new UsageRollupQuery
                    {
                        TenantId = "ten_usage_rollup",
                        AccountKey = "usage-hmac-key-rollup",
                        FromHourUtc = hour,
                        ToHourUtc = hour.AddHours(1)
                    }, ct).ConfigureAwait(false);
                    AssertEqual(2L, both.TotalRecords, "a per-model rollup is a distinct natural key row");
                }
            }));

            cases.Add(Case("usage_latest_state_keeps_error_separate_from_last_good", "Latest state preserves last-good windows when an error lands", TestTags.Positive, async ct =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    DatabaseDriver driver = testDb.Driver;

                    UsageSnapshot good = new UsageSnapshot();
                    good.TenantId = "ten_usage_latest";
                    good.HostId = "host_1";
                    good.Runtime = AgentRuntimeEnum.ClaudeCode;
                    good.AccountKey = "usage-hmac-key-latest";
                    good.Status = UsageSnapshotStatusEnum.Ok;
                    good.Source = UsageCaptureSourceEnum.OAuthEndpoint;
                    good.WindowsJson = "[{\"name\":\"seven_day\",\"usedPercent\":33}]";
                    await driver.UsageSnapshots.CreateAsync(good, null, ct).ConfigureAwait(false);

                    UsageLatestState goodState = new UsageLatestState();
                    goodState.TenantId = "ten_usage_latest";
                    goodState.HostId = "host_1";
                    goodState.Runtime = AgentRuntimeEnum.ClaudeCode;
                    goodState.Provider = "anthropic";
                    goodState.AccountKey = "usage-hmac-key-latest";
                    goodState.LastGoodSnapshotId = good.Id;
                    goodState.LastGoodCapturedUtc = good.CapturedUtc;
                    goodState.LastGoodStatus = UsageSnapshotStatusEnum.Ok;
                    goodState.LastGoodWindowsJson = good.WindowsJson;
                    await driver.UsageSnapshots.UpsertLatestStateAsync(goodState, ct).ConfigureAwait(false);

                    UsageLatestState errorState = new UsageLatestState();
                    errorState.TenantId = "ten_usage_latest";
                    errorState.HostId = "host_1";
                    errorState.Runtime = AgentRuntimeEnum.ClaudeCode;
                    errorState.AccountKey = "usage-hmac-key-latest";
                    errorState.LastErrorSnapshotId = "usg_error_probe";
                    errorState.LastErrorCapturedUtc = good.CapturedUtc.AddMinutes(5);
                    errorState.LastErrorStatus = UsageSnapshotStatusEnum.Failed;
                    errorState.LastErrorCode = "ProbeTimeout";
                    await driver.UsageSnapshots.UpsertLatestStateAsync(errorState, ct).ConfigureAwait(false);

                    UsageLatestState? merged = await driver.UsageSnapshots.ReadLatestStateAsync("ten_usage_latest", "host_1", AgentRuntimeEnum.ClaudeCode, "usage-hmac-key-latest", ct).ConfigureAwait(false);
                    AssertNotNull(merged, "latest state exists");
                    AssertEqual(good.Id, merged!.LastGoodSnapshotId, "failed capture does not erase the last good snapshot reference");
                    AssertEqual(good.CapturedUtc, merged.LastGoodCapturedUtc, "failed capture does not erase the last good observation time");
                    AssertEqual(UsageSnapshotStatusEnum.Ok, merged.LastGoodStatus, "failed capture does not erase the last good status");
                    AssertEqual(good.WindowsJson, merged.LastGoodWindowsJson, "failed capture does not erase the last good windows");
                    AssertEqual("usg_error_probe", merged.LastErrorSnapshotId, "latest error stored separately");
                    AssertEqual(UsageSnapshotStatusEnum.Failed, merged.LastErrorStatus, "latest error status stored separately");
                    AssertEqual("ProbeTimeout", merged.LastErrorCode, "latest error code stored separately");

                    DateTime expiredAt = new DateTime(2026, 10, 28, 0, 0, 0, DateTimeKind.Utc);
                    UsageLatestState expiredState = new UsageLatestState();
                    expiredState.TenantId = "ten_usage_latest";
                    expiredState.HostId = "host_1";
                    expiredState.Runtime = AgentRuntimeEnum.ClaudeCode;
                    expiredState.AccountKey = "usage-hmac-key-latest";
                    expiredState.ExpiredUtc = expiredAt;
                    await driver.UsageSnapshots.UpsertLatestStateAsync(expiredState, ct).ConfigureAwait(false);

                    UsageLatestState? expiredRead = await driver.UsageSnapshots.ReadLatestStateAsync("ten_usage_latest", "host_1", AgentRuntimeEnum.ClaudeCode, "usage-hmac-key-latest", ct).ConfigureAwait(false);
                    AssertNotNull(expiredRead, "expired state stays readable by key");
                    AssertEqual(expiredAt, expiredRead!.ExpiredUtc, "expiry time stored");
                    AssertEqual(good.WindowsJson, expiredRead.LastGoodWindowsJson, "expiry does not refresh or erase the bounded metadata");

                    EnumerationResult<UsageLatestState> defaultPage = await driver.UsageSnapshots.EnumerateLatestStatesAsync(new UsageLatestStateQuery { TenantId = "ten_usage_latest" }, ct).ConfigureAwait(false);
                    AssertEqual(0L, defaultPage.TotalRecords, "expired latest state is excluded by default");

                    EnumerationResult<UsageLatestState> withExpired = await driver.UsageSnapshots.EnumerateLatestStatesAsync(new UsageLatestStateQuery { TenantId = "ten_usage_latest", IncludeExpired = true }, ct).ConfigureAwait(false);
                    AssertEqual(1L, withExpired.TotalRecords, "expired latest state is included on request");
                }
            }));

            cases.Add(Case("usage_rollup_natural_key_includes_provider", "Rollups that differ only by provider are separate rows; null provider is its own key", TestTags.Positive, async ct =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    DatabaseDriver driver = testDb.Driver;

                    DateTime hour = new DateTime(2026, 10, 27, 14, 0, 0, DateTimeKind.Utc);
                    UsageHourlyRollup anthropic = NewRollup("ten_usage_provider", hour, 4, 20.0);
                    anthropic.Provider = "anthropic";
                    await driver.UsageRollups.UpsertAsync(anthropic, ct).ConfigureAwait(false);

                    UsageHourlyRollup openai = NewRollup("ten_usage_provider", hour, 6, 80.0);
                    openai.Provider = "openai";
                    await driver.UsageRollups.UpsertAsync(openai, ct).ConfigureAwait(false);

                    UsageRollupQuery all = new UsageRollupQuery
                    {
                        TenantId = "ten_usage_provider",
                        AccountKey = "usage-hmac-key-rollup",
                        FromHourUtc = hour,
                        ToHourUtc = hour.AddHours(1)
                    };
                    EnumerationResult<UsageHourlyRollup> page = await driver.UsageRollups.EnumerateAsync(all, ct).ConfigureAwait(false);
                    AssertEqual(2L, page.TotalRecords, "a different provider is a distinct natural key row");

                    UsageHourlyRollup? anthropicRead = await driver.UsageRollups.ReadAsync(anthropic.Id, "ten_usage_provider", ct).ConfigureAwait(false);
                    AssertNotNull(anthropicRead, "first provider row kept");
                    AssertEqual("anthropic", anthropicRead!.Provider, "first provider row not relabeled");
                    AssertEqual(20.0, anthropicRead.LastPercent, "first provider utilization not overwritten");
                    AssertEqual(4, anthropicRead.SampleCount, "first provider sample count not overwritten");

                    UsageHourlyRollup? openaiRead = await driver.UsageRollups.ReadAsync(openai.Id, "ten_usage_provider", ct).ConfigureAwait(false);
                    AssertNotNull(openaiRead, "second provider row stored separately");
                    AssertEqual("openai", openaiRead!.Provider, "second provider stored");
                    AssertEqual(80.0, openaiRead.LastPercent, "second provider utilization stored");

                    UsageHourlyRollup unknown = NewRollup("ten_usage_provider", hour, 2, 10.0);
                    unknown.Provider = null;
                    await driver.UsageRollups.UpsertAsync(unknown, ct).ConfigureAwait(false);
                    UsageHourlyRollup unknownAgain = NewRollup("ten_usage_provider", hour, 3, 15.0);
                    unknownAgain.Provider = String.Empty;
                    await driver.UsageRollups.UpsertAsync(unknownAgain, ct).ConfigureAwait(false);

                    EnumerationResult<UsageHourlyRollup> withUnknown = await driver.UsageRollups.EnumerateAsync(all, ct).ConfigureAwait(false);
                    AssertEqual(3L, withUnknown.TotalRecords, "null and empty provider share one unknown-provider row, distinct from named providers");
                    UsageHourlyRollup? unknownRead = await driver.UsageRollups.ReadAsync(unknown.Id, "ten_usage_provider", ct).ConfigureAwait(false);
                    AssertNotNull(unknownRead, "unknown-provider row keeps its first id");
                    AssertNull(unknownRead!.Provider, "unknown provider reads back as null");
                    AssertEqual(3, unknownRead.SampleCount, "unknown-provider retry replaced the row instead of adding one");
                    AssertEqual(15.0, unknownRead.LastPercent, "unknown-provider retry replaced utilization");

                    UsageHourlyRollup anthropicRetry = NewRollup("ten_usage_provider", hour, 5, 25.0);
                    anthropicRetry.Provider = "anthropic";
                    await driver.UsageRollups.UpsertAsync(anthropicRetry, ct).ConfigureAwait(false);
                    EnumerationResult<UsageHourlyRollup> afterRetry = await driver.UsageRollups.EnumerateAsync(all, ct).ConfigureAwait(false);
                    AssertEqual(3L, afterRetry.TotalRecords, "same-provider retry stays idempotent");
                    UsageHourlyRollup? anthropicRetried = await driver.UsageRollups.ReadAsync(anthropic.Id, "ten_usage_provider", ct).ConfigureAwait(false);
                    AssertEqual(25.0, anthropicRetried!.LastPercent, "same-provider retry replaced its own row");
                    UsageHourlyRollup? openaiAfter = await driver.UsageRollups.ReadAsync(openai.Id, "ten_usage_provider", ct).ConfigureAwait(false);
                    AssertEqual(80.0, openaiAfter!.LastPercent, "other provider untouched by the retry");
                }
            }));

            cases.Add(Case("usage_latest_state_replaces_last_good_summary_as_a_unit", "Selecting a new good snapshot replaces every last-good field, including nulls; error-only writes preserve it", TestTags.Positive, async ct =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    DatabaseDriver driver = testDb.Driver;

                    DateTime t1 = new DateTime(2026, 10, 27, 9, 0, 0, DateTimeKind.Utc);
                    DateTime t2 = new DateTime(2026, 10, 27, 10, 0, 0, DateTimeKind.Utc);

                    UsageLatestState first = NewLatestState();
                    first.LastGoodSnapshotId = "usg_good_t1";
                    first.LastGoodCapturedUtc = t1;
                    first.LastGoodReceivedUtc = t1.AddSeconds(2);
                    first.LastGoodStatus = UsageSnapshotStatusEnum.Ok;
                    first.LastGoodWindowsJson = "[{\"name\":\"five_hour\",\"usedPercent\":42}]";
                    await driver.UsageSnapshots.UpsertLatestStateAsync(first, ct).ConfigureAwait(false);

                    UsageLatestState firstError = NewLatestState();
                    firstError.LastErrorSnapshotId = "usg_err_1";
                    firstError.LastErrorCapturedUtc = t1.AddMinutes(10);
                    firstError.LastErrorStatus = UsageSnapshotStatusEnum.Failed;
                    firstError.LastErrorCode = "ProbeTimeout";
                    await driver.UsageSnapshots.UpsertLatestStateAsync(firstError, ct).ConfigureAwait(false);

                    UsageLatestState second = NewLatestState();
                    second.LastGoodSnapshotId = "usg_good_t2";
                    second.LastGoodCapturedUtc = t2;
                    second.LastGoodReceivedUtc = null;
                    second.LastGoodStatus = UsageSnapshotStatusEnum.Estimated;
                    second.LastGoodWindowsJson = null;
                    await driver.UsageSnapshots.UpsertLatestStateAsync(second, ct).ConfigureAwait(false);

                    UsageLatestState? replaced = await driver.UsageSnapshots.ReadLatestStateAsync("ten_usage_latest_unit", "host_1", AgentRuntimeEnum.Codex, "usage-hmac-key-unit", ct).ConfigureAwait(false);
                    AssertNotNull(replaced, "latest state exists");
                    AssertEqual("usg_good_t2", replaced!.LastGoodSnapshotId, "new good snapshot selected");
                    AssertEqual(t2, replaced.LastGoodCapturedUtc, "observation time of the selected snapshot");
                    AssertNull(replaced.LastGoodReceivedUtc, "null received time of the selected snapshot is not merged with T1's");
                    AssertEqual(UsageSnapshotStatusEnum.Estimated, replaced.LastGoodStatus, "status of the selected snapshot");
                    AssertNull(replaced.LastGoodWindowsJson, "unknown windows of the selected snapshot are not merged with T1's windows");
                    AssertEqual("usg_err_1", replaced.LastErrorSnapshotId, "a good-only write preserves the latest error");
                    AssertEqual("ProbeTimeout", replaced.LastErrorCode, "a good-only write preserves the latest error code");

                    UsageLatestState secondError = NewLatestState();
                    secondError.LastGoodSnapshotId = String.Empty;
                    secondError.LastErrorSnapshotId = "usg_err_2";
                    secondError.LastErrorCapturedUtc = t2.AddMinutes(10);
                    secondError.LastErrorStatus = UsageSnapshotStatusEnum.AuthMissing;
                    secondError.LastErrorCode = null;
                    await driver.UsageSnapshots.UpsertLatestStateAsync(secondError, ct).ConfigureAwait(false);

                    UsageLatestState? afterError = await driver.UsageSnapshots.ReadLatestStateAsync("ten_usage_latest_unit", "host_1", AgentRuntimeEnum.Codex, "usage-hmac-key-unit", ct).ConfigureAwait(false);
                    AssertNotNull(afterError, "latest state still exists");
                    AssertEqual("usg_good_t2", afterError!.LastGoodSnapshotId, "error-only write keeps the last good snapshot");
                    AssertEqual(t2, afterError.LastGoodCapturedUtc, "error-only write keeps the last good observation time");
                    AssertNull(afterError.LastGoodReceivedUtc, "error-only write keeps the null received time");
                    AssertEqual(UsageSnapshotStatusEnum.Estimated, afterError.LastGoodStatus, "error-only write keeps the last good status");
                    AssertNull(afterError.LastGoodWindowsJson, "error-only write keeps the unknown windows");
                    AssertEqual("usg_err_2", afterError.LastErrorSnapshotId, "new error selected");
                    AssertEqual(UsageSnapshotStatusEnum.AuthMissing, afterError.LastErrorStatus, "new error status");
                    AssertNull(afterError.LastErrorCode, "null code of the new error is not merged with the previous code");

                    foreach (string? absentId in new string?[] { null, String.Empty })
                    {
                        UsageLatestState unselected = NewLatestState();
                        unselected.LastGoodSnapshotId = absentId;
                        unselected.LastGoodCapturedUtc = t1;
                        unselected.LastGoodReceivedUtc = t1;
                        unselected.LastGoodStatus = UsageSnapshotStatusEnum.Ok;
                        unselected.LastGoodWindowsJson = first.LastGoodWindowsJson;
                        unselected.LastErrorSnapshotId = absentId;
                        unselected.LastErrorCapturedUtc = t1;
                        unselected.LastErrorStatus = UsageSnapshotStatusEnum.Failed;
                        unselected.LastErrorCode = "UnselectedDiagnostic";
                        await driver.UsageSnapshots.UpsertLatestStateAsync(unselected, ct).ConfigureAwait(false);

                        UsageLatestState? unchanged = await driver.UsageSnapshots.ReadLatestStateAsync("ten_usage_latest_unit", "host_1", AgentRuntimeEnum.Codex, "usage-hmac-key-unit", ct).ConfigureAwait(false);
                        AssertNotNull(unchanged, "unselected metadata does not remove latest state");
                        AssertEqual("usg_good_t2", unchanged!.LastGoodSnapshotId, "absent good id preserves selection");
                        AssertEqual(t2, unchanged.LastGoodCapturedUtc, "absent good id ignores supplied observation time");
                        AssertNull(unchanged.LastGoodReceivedUtc, "absent good id ignores supplied received time");
                        AssertEqual(UsageSnapshotStatusEnum.Estimated, unchanged.LastGoodStatus, "absent good id ignores supplied status");
                        AssertNull(unchanged.LastGoodWindowsJson, "absent good id ignores supplied windows");
                        AssertEqual("usg_err_2", unchanged.LastErrorSnapshotId, "absent error id preserves selection");
                        AssertEqual(t2.AddMinutes(10), unchanged.LastErrorCapturedUtc, "absent error id ignores supplied observation time");
                        AssertEqual(UsageSnapshotStatusEnum.AuthMissing, unchanged.LastErrorStatus, "absent error id ignores supplied status");
                        AssertNull(unchanged.LastErrorCode, "absent error id ignores supplied diagnostic");
                    }
                }
            }));

            cases.Add(Case("usage_retention_deletes_snapshots_links_and_rollups_by_cutoff", "Retention cutoffs remove old snapshots, links and rollups only", TestTags.Positive, async ct =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    DatabaseDriver driver = testDb.Driver;

                    DateTime oldCaptured = new DateTime(2026, 10, 10, 8, 0, 0, DateTimeKind.Utc);
                    DateTime newCaptured = new DateTime(2026, 10, 27, 8, 0, 0, DateTimeKind.Utc);

                    UsageSnapshot oldSnapshot = new UsageSnapshot();
                    oldSnapshot.TenantId = "ten_usage_retention";
                    oldSnapshot.HostId = "host_1";
                    oldSnapshot.Runtime = AgentRuntimeEnum.ClaudeCode;
                    oldSnapshot.AccountKey = "usage-hmac-key-retention";
                    oldSnapshot.CapturedUtc = oldCaptured;
                    oldSnapshot.Status = UsageSnapshotStatusEnum.Ok;
                    oldSnapshot.Source = UsageCaptureSourceEnum.OAuthEndpoint;
                    await driver.UsageSnapshots.CreateAsync(oldSnapshot, new List<string> { "cpt_retained" }, ct).ConfigureAwait(false);

                    UsageSnapshot newSnapshot = new UsageSnapshot();
                    newSnapshot.TenantId = "ten_usage_retention";
                    newSnapshot.HostId = "host_1";
                    newSnapshot.Runtime = AgentRuntimeEnum.ClaudeCode;
                    newSnapshot.AccountKey = "usage-hmac-key-retention";
                    newSnapshot.CapturedUtc = newCaptured;
                    newSnapshot.Status = UsageSnapshotStatusEnum.Ok;
                    newSnapshot.Source = UsageCaptureSourceEnum.OAuthEndpoint;
                    await driver.UsageSnapshots.CreateAsync(newSnapshot, new List<string> { "cpt_retained" }, ct).ConfigureAwait(false);

                    int deleted = await driver.UsageSnapshots.DeleteOlderThanAsync(new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc), ct).ConfigureAwait(false);
                    AssertEqual(1, deleted, "only the old snapshot is deleted");

                    AssertNull(await driver.UsageSnapshots.ReadAsync(oldSnapshot.Id, "ten_usage_retention", ct).ConfigureAwait(false), "old snapshot gone");
                    AssertEqual(0, (await driver.UsageSnapshots.ReadCaptainsAsync(oldSnapshot.Id, "ten_usage_retention", ct).ConfigureAwait(false)).Count, "old member links deleted with the snapshot");
                    AssertNotNull(await driver.UsageSnapshots.ReadAsync(newSnapshot.Id, "ten_usage_retention", ct).ConfigureAwait(false), "fresh snapshot retained");
                    AssertEqual(1, (await driver.UsageSnapshots.ReadCaptainsAsync(newSnapshot.Id, "ten_usage_retention", ct).ConfigureAwait(false)).Count, "fresh member links retained");

                    UsageHourlyRollup oldRollup = NewRollup("ten_usage_retention", new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc), 5, 50.0);
                    await driver.UsageRollups.UpsertAsync(oldRollup, ct).ConfigureAwait(false);
                    UsageHourlyRollup newRollup = NewRollup("ten_usage_retention", new DateTime(2026, 10, 27, 9, 0, 0, DateTimeKind.Utc), 5, 50.0);
                    await driver.UsageRollups.UpsertAsync(newRollup, ct).ConfigureAwait(false);

                    int rollupsDeleted = await driver.UsageRollups.DeleteOlderThanAsync(new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc), ct).ConfigureAwait(false);
                    AssertEqual(1, rollupsDeleted, "only the old rollup is deleted");
                    AssertNull(await driver.UsageRollups.ReadAsync(oldRollup.Id, "ten_usage_retention", ct).ConfigureAwait(false), "old rollup gone");
                    AssertNotNull(await driver.UsageRollups.ReadAsync(newRollup.Id, "ten_usage_retention", ct).ConfigureAwait(false), "fresh rollup retained");
                }
            }));

            cases.Add(Case("captain_usage_capture_settings_round_trip_and_defaults", "Captain usage settings persist, default to disabled/300/Auto and clear on mode change", TestTags.Positive, async ct =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    DatabaseDriver driver = testDb.Driver;

                    Captain plain = await driver.Captains.CreateAsync(new Captain("usage-default-captain"), ct).ConfigureAwait(false);
                    AssertFalse(plain.UsageCaptureEnabled, "new captain keeps capture disabled");
                    AssertEqual(300, plain.UsageCaptureIntervalSeconds, "new captain requests the default interval");
                    AssertEqual(UsageCaptureModeEnum.Auto, plain.UsageCaptureMode, "new captain defaults to Auto");
                    AssertNull(plain.UsageCaptureCommand, "new captain has no command");
                    AssertFalse(plain.UsageCaptureActive, "new captain is not effectively active");

                    Captain? readPlain = await driver.Captains.ReadAsync(plain.Id, ct).ConfigureAwait(false);
                    AssertNotNull(readPlain, "plain captain readable");
                    AssertFalse(readPlain!.UsageCaptureEnabled, "disabled default survives storage");
                    AssertEqual(300, readPlain.UsageCaptureIntervalSeconds, "interval default survives storage");
                    AssertEqual(UsageCaptureModeEnum.Auto, readPlain.UsageCaptureMode, "Auto mode survives storage");
                    AssertNull(readPlain.UsageCaptureCommand, "null command survives storage");

                    Captain configured = new Captain("usage-custom-captain");
                    configured.UsageCaptureEnabled = true;
                    configured.UsageCaptureIntervalSeconds = 900;
                    configured.UsageCaptureMode = UsageCaptureModeEnum.Custom;
                    configured.UsageCaptureCommand = "{\"executable\":\"/opt/usage/read-quota\",\"arguments\":[\"--json\"]}";
                    configured = await driver.Captains.CreateAsync(configured, ct).ConfigureAwait(false);

                    Captain? readConfigured = await driver.Captains.ReadAsync(configured.Id, ct).ConfigureAwait(false);
                    AssertNotNull(readConfigured, "configured captain readable");
                    AssertTrue(readConfigured!.UsageCaptureEnabled, "enabled flag round-trips");
                    AssertEqual(900, readConfigured.UsageCaptureIntervalSeconds, "interval round-trips");
                    AssertEqual(UsageCaptureModeEnum.Custom, readConfigured.UsageCaptureMode, "Custom mode round-trips");
                    AssertEqual(configured.UsageCaptureCommand, readConfigured.UsageCaptureCommand, "command round-trips");
                    AssertTrue(readConfigured.UsageCaptureActive, "enabled Custom captain is effectively active");

                    readConfigured.UsageCaptureMode = UsageCaptureModeEnum.Auto;
                    Captain? updated = await driver.Captains.UpdateAsync(readConfigured, ct).ConfigureAwait(false);
                    AssertNotNull(updated, "updated captain readable");
                    AssertEqual(UsageCaptureModeEnum.Auto, updated!.UsageCaptureMode, "mode update round-trips");
                    AssertNull(updated.UsageCaptureCommand, "leaving Custom clears the command");

                    Captain optedOut = new Captain("usage-disabled-captain");
                    optedOut.UsageCaptureEnabled = true;
                    optedOut.UsageCaptureMode = UsageCaptureModeEnum.Disabled;
                    optedOut = await driver.Captains.CreateAsync(optedOut, ct).ConfigureAwait(false);
                    AssertFalse(optedOut.UsageCaptureActive, "Disabled mode always wins over enabled");
                }
            }));

            cases.Add(Case("captain_non_custom_json_command_is_not_persisted", "JSON-supplied commands for Auto/Disabled captains never reach storage in either property order", TestTags.Positive, async ct =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    DatabaseDriver driver = testDb.Driver;
                    string[] payloads = new string[]
                    {
                        "{\"Name\":\"auto-mode-first\",\"UsageCaptureEnabled\":true,\"UsageCaptureMode\":\"Auto\",\"UsageCaptureCommand\":\"quota\"}",
                        "{\"Name\":\"auto-command-first\",\"UsageCaptureEnabled\":true,\"UsageCaptureCommand\":\"quota\",\"UsageCaptureMode\":\"Auto\"}",
                        "{\"Name\":\"disabled-mode-first\",\"UsageCaptureMode\":\"Disabled\",\"UsageCaptureCommand\":\"quota\"}",
                        "{\"Name\":\"disabled-command-first\",\"UsageCaptureCommand\":\"quota\",\"UsageCaptureMode\":\"Disabled\"}",
                        "{\"Name\":\"all-null\",\"UsageCaptureEnabled\":null,\"UsageCaptureIntervalSeconds\":null,\"UsageCaptureMode\":null,\"UsageCaptureCommand\":null}"
                    };

                    foreach (string payload in payloads)
                    {
                        Captain parsed = System.Text.Json.JsonSerializer.Deserialize<Captain>(payload)!;
                        Captain created = await driver.Captains.CreateAsync(parsed, ct).ConfigureAwait(false);
                        Captain? read = await driver.Captains.ReadAsync(created.Id, ct).ConfigureAwait(false);
                        AssertNotNull(read, created.Name + " readable");
                        AssertNull(read!.UsageCaptureCommand, created.Name + ": no command stored");
                        AssertEqual(300, read.UsageCaptureIntervalSeconds, created.Name + ": default interval stored");
                        if (created.Name == "all-null")
                        {
                            AssertFalse(read.UsageCaptureEnabled, "explicit null settings persist as disabled");
                            AssertEqual(UsageCaptureModeEnum.Auto, read.UsageCaptureMode, "explicit null settings persist as Auto");
                        }

                        read.UsageCaptureMode = UsageCaptureModeEnum.Custom;
                        AssertNull(read.UsageCaptureCommand, created.Name + ": switching the stored captain to Custom finds no stale command");
                        Captain? updated = await driver.Captains.UpdateAsync(read, ct).ConfigureAwait(false);
                        AssertNull(updated!.UsageCaptureCommand, created.Name + ": Custom update without a command stores none");
                    }

                }
            }));

            cases.Add(Case("token_usage_attribution_round_trips_and_legacy_records_survive", "Token records keep new nullable attribution and legacy token counts", TestTags.Positive, async ct =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    DatabaseDriver driver = testDb.Driver;

                    TokenUsageRecord attributed = new TokenUsageRecord();
                    attributed.TenantId = "ten_usage_tokens";
                    attributed.Model = "claude-sonnet-4";
                    attributed.Runtime = "claudecode";
                    attributed.Source = "mission";
                    attributed.SourceId = "msn_attribution";
                    attributed.CaptainId = "cpt_attribution";
                    attributed.InputTokens = 1000;
                    attributed.OutputTokens = 200;
                    attributed.CachedTokens = 300;
                    attributed.TotalTokens = 1500;
                    attributed.Provider = "anthropic";
                    attributed.AccountKey = "usage-hmac-key-tokens";
                    attributed.HostId = "host_1";
                    attributed.MembershipId = "usm_attribution";
                    attributed.ReportedCost = 0.0125;
                    attributed.CostCurrency = "usd";
                    await driver.TokenUsage.CreateAsync(attributed, ct).ConfigureAwait(false);

                    TokenUsageRecord? read = await driver.TokenUsage.ReadAsync(attributed.Id, new TokenUsageQuery { TenantId = "ten_usage_tokens" }, ct).ConfigureAwait(false);
                    AssertNotNull(read, "attributed token record readable");
                    AssertEqual(1500, read!.TotalTokens, "token counts keep their provider definition");
                    AssertEqual("anthropic", read.Provider, "provider attribution round-trips");
                    AssertEqual("usage-hmac-key-tokens", read.AccountKey, "account key round-trips");
                    AssertEqual("host_1", read.HostId, "host attribution round-trips");
                    AssertEqual("usm_attribution", read.MembershipId, "membership attribution round-trips");
                    AssertEqual(0.0125, read.ReportedCost, "reported cost round-trips");
                    AssertEqual("USD", read.CostCurrency, "currency normalizes to upper case");

                    TokenUsageRecord legacy = new TokenUsageRecord();
                    legacy.TenantId = "ten_usage_tokens";
                    legacy.Model = "gpt-5";
                    legacy.Runtime = "codex";
                    legacy.Source = "chat";
                    legacy.InputTokens = 50;
                    legacy.OutputTokens = 10;
                    legacy.TotalTokens = 60;
                    await driver.TokenUsage.CreateAsync(legacy, ct).ConfigureAwait(false);

                    TokenUsageRecord? readLegacy = await driver.TokenUsage.ReadAsync(legacy.Id, new TokenUsageQuery { TenantId = "ten_usage_tokens" }, ct).ConfigureAwait(false);
                    AssertNotNull(readLegacy, "legacy-shaped record readable");
                    AssertEqual(50, readLegacy!.InputTokens, "legacy input tokens survive");
                    AssertEqual(60, readLegacy.TotalTokens, "legacy total tokens survive");
                    AssertNull(readLegacy.Provider, "legacy record has null provider, not a guess");
                    AssertNull(readLegacy.AccountKey, "legacy record has null account key");
                    AssertNull(readLegacy.HostId, "legacy record has null host");
                    AssertNull(readLegacy.MembershipId, "legacy record has null membership");
                    AssertNull(readLegacy.ReportedCost, "legacy record has unknown cost, not zero");
                    AssertNull(readLegacy.CostCurrency, "legacy record has no currency");
                }
            }));

            cases.Add(Case("usage_existing_database_upgrades_to_v111", "An existing v110 database upgrades to v111 and keeps its rows", TestTags.Positive, async ct =>
            {
                string path = TestTemp.NewFile("usage_upgrade", ".db");
                string databaseName = "usage_upgrade_" + Guid.NewGuid().ToString("N");
                LoggingModule logging = new LoggingModule();
                logging.Settings.EnableConsole = false;
                try
                {
                    DatabaseSettings settings;
                    if (TestDatabaseConfig.IsSqlite)
                    {
                        settings = new DatabaseSettings();
                        settings.Type = DatabaseTypeEnum.Sqlite;
                        settings.Filename = path;
                        await ApplyMigrationsUpToV110(path, ct).ConfigureAwait(false);
                    }
                    else
                    {
                        await TestDatabaseProvisioner.CreateDatabaseAsync(databaseName, ct).ConfigureAwait(false);
                        settings = TestDatabaseConfig.BuildSettings(databaseName);
                        await ApplyServerMigrationsUpToV110(settings, logging, ct).ConfigureAwait(false);
                    }

                    using (DbConnection conn = CreateUpgradeConnection(settings))
                    {
                        await conn.OpenAsync(ct).ConfigureAwait(false);
                        string active = TestDatabaseConfig.Type == DatabaseTypeEnum.Postgresql ? "TRUE" : "1";
                        string estimated = TestDatabaseConfig.Type == DatabaseTypeEnum.Postgresql ? "FALSE" : "0";
                        // A real initialized v110 database has the built-in tenant. Retain it alongside
                        // the legacy tenant so InitializeAsync can repair the default user/credential.
                        await ExecuteUpgradeSql(conn, "INSERT INTO tenants (id, name, active, created_utc, last_update_utc) "
                            + "SELECT '" + Armada.Core.Constants.DefaultTenantId + "', 'Default Tenant', " + active + ", '2026-10-01T00:00:00', '2026-10-01T00:00:00' "
                            + "WHERE NOT EXISTS (SELECT 1 FROM tenants WHERE id = '" + Armada.Core.Constants.DefaultTenantId + "');", ct).ConfigureAwait(false);
                        await ExecuteUpgradeSql(conn, "INSERT INTO tenants (id, name, active, created_utc, last_update_utc) "
                            + "VALUES ('ten_legacy', 'Legacy Tenant', " + active + ", '2026-10-01T00:00:00', '2026-10-01T00:00:00');", ct).ConfigureAwait(false);
                        await ExecuteUpgradeSql(conn, @"INSERT INTO captains (id, tenant_id, name, runtime, state, recovery_attempts, created_utc, last_update_utc)
                            VALUES ('cpt_legacy_upgrade', 'ten_legacy', 'Legacy Captain', 'ClaudeCode', 'Idle', 0, '2026-10-01T00:00:00', '2026-10-01T00:00:00');", ct).ConfigureAwait(false);
                        await ExecuteUpgradeSql(conn, "INSERT INTO token_usage (id, tenant_id, model, runtime, source, input_tokens, output_tokens, cached_tokens, total_tokens, estimated, created_utc) "
                            + "VALUES ('tku_legacy_upgrade', 'ten_legacy', 'claude-sonnet-4', 'claudecode', 'mission', 1234, 567, 89, 1901, " + estimated + ", '2026-10-01T01:00:00');", ct).ConfigureAwait(false);
                    }

                    using (DatabaseDriver driver = DatabaseDriverFactory.Create(settings, logging))
                    {
                        AssertEqual(110, await driver.GetSchemaVersionAsync(ct).ConfigureAwait(false), "fixture starts at v110");
                        AssertTrue(await driver.GetPendingMigrationCountAsync(ct).ConfigureAwait(false) > 0, "usage migration is pending");
                        await driver.InitializeAsync(ct).ConfigureAwait(false);

                        int version = await driver.GetSchemaVersionAsync(ct).ConfigureAwait(false);
                        AssertTrue(version >= 111, "upgraded database reaches the usage capture migration");
                        AssertEqual(0, await driver.GetPendingMigrationCountAsync(ct).ConfigureAwait(false), "nothing pending after upgrade");

                        Captain? legacyCaptain = await driver.Captains.ReadAsync("cpt_legacy_upgrade", ct).ConfigureAwait(false);
                        AssertNotNull(legacyCaptain, "legacy captain survives the upgrade");
                        AssertEqual("Legacy Captain", legacyCaptain!.Name, "legacy captain name survives");
                        AssertFalse(legacyCaptain.UsageCaptureEnabled, "legacy captain stays disabled after the upgrade");
                        AssertEqual(300, legacyCaptain.UsageCaptureIntervalSeconds, "legacy captain gets the default interval");
                        AssertEqual(UsageCaptureModeEnum.Auto, legacyCaptain.UsageCaptureMode, "legacy captain gets the default mode");
                        AssertNull(legacyCaptain.UsageCaptureCommand, "legacy captain gets no command");

                        TokenUsageRecord? legacyTokens = await driver.TokenUsage.ReadAsync("tku_legacy_upgrade", new TokenUsageQuery { TenantId = "ten_legacy" }, ct).ConfigureAwait(false);
                        AssertNotNull(legacyTokens, "legacy token record survives the upgrade");
                        AssertEqual(1234, legacyTokens!.InputTokens, "legacy token input count is not lost");
                        AssertEqual(1901, legacyTokens.TotalTokens, "legacy token total is not lost");
                        AssertNull(legacyTokens.Provider, "upgraded legacy record has null provider");
                        AssertEqual(567, legacyTokens.OutputTokens, "legacy output tokens survive");
                        AssertEqual(89, legacyTokens.CachedTokens, "legacy cached tokens survive");
                        AssertNull(legacyTokens.AccountKey, "legacy account remains unattributed");
                        AssertNull(legacyTokens.HostId, "legacy host remains unattributed");
                        AssertNull(legacyTokens.MembershipId, "legacy membership remains unattributed");
                        AssertNull(legacyTokens.ReportedCost, "upgraded legacy record has unknown cost");
                        AssertNull(legacyTokens.CostCurrency, "legacy currency remains unknown");

                        UsageSnapshot upgraded = new UsageSnapshot();
                        upgraded.TenantId = "ten_legacy";
                        upgraded.HostId = "admiral";
                        upgraded.Runtime = AgentRuntimeEnum.ClaudeCode;
                        upgraded.AccountKey = "usage-hmac-key-upgrade";
                        upgraded.Status = UsageSnapshotStatusEnum.Ok;
                        upgraded.Source = UsageCaptureSourceEnum.OAuthEndpoint;
                        upgraded.WindowsJson = "[{\"name\":\"five_hour\",\"usedPercent\":12}]";
                        await driver.UsageSnapshots.CreateAsync(upgraded, new List<string> { "cpt_legacy_upgrade" }, ct).ConfigureAwait(false);

                        UsageSnapshot? readUpgraded = await driver.UsageSnapshots.ReadAsync(upgraded.Id, "ten_legacy", ct).ConfigureAwait(false);
                        AssertNotNull(readUpgraded, "usage storage works on the upgraded database");
                        AssertEqual("usage-hmac-key-upgrade", readUpgraded!.AccountKey, "upgraded database round-trips account keys");

                        int migrated = await driver.UsageSnapshots.DeleteByTenantAsync("ten_legacy", ct).ConfigureAwait(false);
                        AssertEqual(1, migrated, "tenant-scoped storage deletion removes usage rows");
                    }
                }
                finally
                {
                    if (TestDatabaseConfig.IsSqlite)
                    {
                        SqliteConnection.ClearAllPools();
                        TestTemp.TryDelete(path);
                    }
                    else
                    {
                        await TestDatabaseProvisioner.DropDatabaseAsync(databaseName).ConfigureAwait(false);
                    }
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Usage Persistence",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static UsageHourlyRollup NewRollup(string tenantId, DateTime hour, int sampleCount, double lastPercent)
        {
            UsageHourlyRollup rollup = new UsageHourlyRollup();
            rollup.TenantId = tenantId;
            rollup.HostId = "host_1";
            rollup.Runtime = AgentRuntimeEnum.ClaudeCode;
            rollup.Provider = "anthropic";
            rollup.AccountKey = "usage-hmac-key-rollup";
            rollup.WindowName = "five_hour";
            rollup.Model = String.Empty;
            rollup.HourUtc = hour;
            rollup.SampleCount = sampleCount;
            rollup.LastPercent = lastPercent;
            rollup.MinPercent = lastPercent - 10.0;
            rollup.MaxPercent = lastPercent + 5.0;
            rollup.MeanPercent = lastPercent - 2.0;
            rollup.OkCount = sampleCount;
            return rollup;
        }

        private static UsageLatestState NewLatestState()
        {
            UsageLatestState state = new UsageLatestState();
            state.TenantId = "ten_usage_latest_unit";
            state.HostId = "host_1";
            state.Runtime = AgentRuntimeEnum.Codex;
            state.Provider = "openai";
            state.AccountKey = "usage-hmac-key-unit";
            return state;
        }

        private static DbConnection CreateUpgradeConnection(DatabaseSettings settings)
        {
            switch (settings.Type)
            {
                case DatabaseTypeEnum.Postgresql: return new NpgsqlConnection(settings.GetConnectionString());
                case DatabaseTypeEnum.Mysql: return new MySqlConnection(settings.GetConnectionString());
                case DatabaseTypeEnum.SqlServer: return new SqlConnection(settings.GetConnectionString());
                default: return new SqliteConnection(settings.GetConnectionString());
            }
        }

        private static async Task ExecuteUpgradeSql(DbConnection connection, string sql, CancellationToken token)
        {
            using (DbCommand command = connection.CreateCommand())
            {
                command.CommandText = sql;
                await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }
        }

        private static async Task ApplyServerMigrationsUpToV110(DatabaseSettings settings, LoggingModule logging, CancellationToken token)
        {
            using (DatabaseDriver driver = DatabaseDriverFactory.Create(settings, logging))
            using (DbConnection connection = CreateUpgradeConnection(settings))
            {
                await connection.OpenAsync(token).ConfigureAwait(false);
                string schemaSql = settings.Type == DatabaseTypeEnum.Mysql
                    ? Armada.Core.Database.Mysql.Queries.TableQueries.SchemaMigrations
                    : settings.Type == DatabaseTypeEnum.SqlServer
                        ? Armada.Core.Database.SqlServer.Queries.TableQueries.SchemaMigrations
                        : "CREATE TABLE schema_migrations (version INTEGER PRIMARY KEY, description TEXT NOT NULL, applied_utc TIMESTAMP NOT NULL);";
                await ExecuteUpgradeSql(connection, schemaSql, token).ConfigureAwait(false);

                // Reuse the provider's executor (including duplicate-column guards and MySQL statement
                // splitting). Reflection is confined to fixture construction so production initialization
                // remains unchanged and the upgrade itself always uses InitializeAsync.
                MethodInfo executor = driver.GetType().GetMethod("ExecuteMigrationAsync", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
                    ?? throw new InvalidOperationException("Provider migration executor missing");
                foreach (SchemaMigration migration in driver.GetMigrationsForVerification())
                {
                    if (migration.Version > 110) continue;
                    using (DbTransaction transaction = await connection.BeginTransactionAsync(token).ConfigureAwait(false))
                    {
                        Task execution = (Task)(executor.Invoke(executor.IsStatic ? null : driver, new object[] { connection, transaction, migration, token })
                            ?? throw new InvalidOperationException("Provider migration executor returned no task"));
                        await execution.ConfigureAwait(false);
                        using (DbCommand record = connection.CreateCommand())
                        {
                            record.Transaction = transaction;
                            record.CommandText = "INSERT INTO schema_migrations (version, description, applied_utc) VALUES (@v, @d, @t);";
                            DbParameter version = record.CreateParameter();
                            version.ParameterName = "@v";
                            version.Value = migration.Version;
                            record.Parameters.Add(version);
                            DbParameter description = record.CreateParameter();
                            description.ParameterName = "@d";
                            description.Value = migration.Description;
                            record.Parameters.Add(description);
                            DbParameter applied = record.CreateParameter();
                            applied.ParameterName = "@t";
                            applied.Value = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
                            record.Parameters.Add(applied);
                            await record.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                        }
                        await transaction.CommitAsync(token).ConfigureAwait(false);
                    }
                }
            }
        }

        private static async Task ApplyMigrationsUpToV110(string path, CancellationToken token)
        {
            using (SqliteConnection conn = new SqliteConnection("Data Source=" + path))
            {
                conn.Open();

                using (SqliteCommand schemaTable = conn.CreateCommand())
                {
                    schemaTable.CommandText = @"CREATE TABLE IF NOT EXISTS schema_migrations (
                        version INTEGER PRIMARY KEY,
                        description TEXT NOT NULL,
                        applied_utc TEXT NOT NULL
                    );";
                    schemaTable.ExecuteNonQuery();
                }

                List<SchemaMigration> migrations = TableQueries.GetMigrations();
                foreach (SchemaMigration migration in migrations)
                {
                    if (migration.Version >= 111) continue;

                    using (SqliteTransaction tx = conn.BeginTransaction())
                    {
                        foreach (string sql in migration.Statements)
                        {
                            // The driver's own executor skips ALTER TABLE ADD COLUMN statements whose column
                            // already exists (a later CREATE TABLE definition may already include it); mirror
                            // that pre-check so the raw replay matches a real v110 database exactly.
                            if (SqliteDatabaseDriver.TryParseAddColumn(sql, out string addTable, out string addColumn)
                                && await SqliteDatabaseDriver.ColumnExistsAsync(conn, tx, addTable, addColumn, token).ConfigureAwait(false))
                            {
                                continue;
                            }

                            using (SqliteCommand cmd = conn.CreateCommand())
                            {
                                cmd.Transaction = tx;
                                cmd.CommandText = sql;
                                cmd.ExecuteNonQuery();
                            }
                        }

                        using (SqliteCommand record = conn.CreateCommand())
                        {
                            record.Transaction = tx;
                            record.CommandText = "INSERT INTO schema_migrations (version, description, applied_utc) VALUES (@v, @d, @t);";
                            record.Parameters.AddWithValue("@v", migration.Version);
                            record.Parameters.AddWithValue("@d", migration.Description);
                            record.Parameters.AddWithValue("@t", SqliteDatabaseDriver.ToIso8601(DateTime.UtcNow));
                            record.ExecuteNonQuery();
                        }

                        tx.Commit();
                    }
                }
            }
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: body,
                tags: new List<string> { tag, TestTags.Database });
        }

        #endregion
    }
}

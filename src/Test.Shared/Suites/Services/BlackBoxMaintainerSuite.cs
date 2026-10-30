namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.BlackBox;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// The maintainer loop: the bundle (grouped by fingerprint, capped at 50 entries, Markdown with the mission-brief
    /// preamble or JSON, host redaction, tenant scoping) and fingerprint trends per Armada version.
    /// </summary>
    public sealed class BlackBoxMaintainerSuite : IArmadaTestSuite
    {
        private const string SuiteId = "Services.BlackBoxMaintainer";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("bundle_grouping_and_cap", "The bundle groups entries by fingerprint, most frequent first, caps at 50 entries, and carries the preamble asking for ranked Armada mission briefs", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver);
                await SeedAsync(t.Driver, 35, "fp_often", "1.1.0", null).ConfigureAwait(false);
                await SeedAsync(t.Driver, 20, "fp_rare", "1.2.0", null).ConfigureAwait(false);
                AuthContext admin = BlackBoxTestKit.Admin();
                BlackBoxBundleResult md = await bb.Service.GetBundleAsync(admin, new BlackBoxBundleRequest()).ConfigureAwait(false);
                AssertEqual(BlackBoxBundleFormatEnum.Markdown, md.Format);
                AssertEqual(50, md.EntryCount, "capped at 50");
                AssertTrue(md.Truncated, "55 entries matched, so the bundle says it is truncated");
                AssertEqual(2, md.FingerprintCount, "two failure modes");
                AssertContains("mission brief", md.Content, "preamble asks for mission briefs");
                AssertContains("Rank the Armada improvements", md.Content);
                AssertContains("Fingerprint: fp_often", md.Content); AssertContains("Fingerprint: fp_rare", md.Content);
                AssertTrue(md.Content.IndexOf("fp_often", StringComparison.Ordinal) < md.Content.IndexOf("fp_rare", StringComparison.Ordinal), "most frequent first");
                AssertContains("Other entries with this fingerprint", md.Content, "one-liners for the rest");
                AssertContains("Armada versions: 1.1.0", md.Content);
                AssertContains("### FIX THIS", md.Content, "each failure mode carries the single brief, demoted under its group");
                AssertFalse(md.Content.Contains("maintainer brief", StringComparison.Ordinal), "no separate maintainer brief");
                AssertFalse(md.Content.Contains('—'), "no em-dashes");
                AssertEqual(5, (await bb.Service.GetBundleAsync(admin, new BlackBoxBundleRequest { Max = 5 }).ConfigureAwait(false)).EntryCount, "max honored");
                AssertEqual(50, (await bb.Service.GetBundleAsync(admin, new BlackBoxBundleRequest { Max = 500 }).ConfigureAwait(false)).EntryCount, "max clamped to 50");

                BlackBoxBundleResult json = await bb.Service.GetBundleAsync(admin, new BlackBoxBundleRequest { Format = BlackBoxBundleFormatEnum.Json, Fingerprint = "fp_rare" }).ConfigureAwait(false);
                BlackBoxBundleDocument doc = JsonSerializer.Deserialize<BlackBoxBundleDocument>(json.Content, BlackBoxJson.Options)!;
                AssertEqual(1, doc.Groups.Count, "filtered by fingerprint");
                AssertEqual(20, doc.Groups[0].Count);
                AssertEqual(19, doc.Groups[0].Others.Count, "newest plus the others");
                AssertEqual("1.2.0", doc.Groups[0].ArmadaVersions.Single());
                AssertContains("mission brief", doc.Preamble);
                AssertTrue(doc.Groups[0].FirstSeenUtc <= doc.Groups[0].LastSeenUtc, "first before last");
            }));

            cases.Add(Case("bundle_scope_and_redaction", "A tenant user's bundle holds only their entries; redactHosts strips host paths", TestTags.Negative, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver);
                await SeedAsync(t.Driver, 3, "fp_mine", "1.2.0", "ten_a").ConfigureAwait(false);
                await SeedAsync(t.Driver, 4, "fp_theirs", "1.2.0", "ten_z").ConfigureAwait(false);
                BlackBoxBundleResult mine = await bb.Service.GetBundleAsync(BlackBoxTestKit.User("ten_a", "usr_ten_a"), null).ConfigureAwait(false);
                AssertEqual(3, mine.EntryCount, "tenant-scoped");
                AssertFalse(mine.Content.Contains("fp_theirs"), "no other tenant's entries");
                AssertContains("/Users/someone", mine.Content, "host paths kept by default");
                BlackBoxBundleResult redacted = await bb.Service.GetBundleAsync(BlackBoxTestKit.User("ten_a", "usr_ten_a"), new BlackBoxBundleRequest { RedactHosts = true }).ConfigureAwait(false);
                AssertFalse(redacted.Content.Contains("/Users/someone"), "redactHosts strips host paths");
                await AssertThrowsAsync<UnauthorizedAccessException>(() => bb.Service.GetBundleAsync(new AuthContext(), null), "unauthenticated").ConfigureAwait(false);
            }));

            cases.Add(Case("fingerprint_trend", "A fingerprint's occurrences are counted per Armada version (ordered by first sighting) with open and ArmadaFixed counts, scoped to the caller", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver);
                List<BlackBoxEntry> old = await SeedAsync(t.Driver, 4, "fp_trend", "1.1.0", null, -10).ConfigureAwait(false);
                await SeedAsync(t.Driver, 2, "fp_trend", "1.2.0", null, -2).ConfigureAwait(false);
                await SeedAsync(t.Driver, 1, "fp_trend", null, null, -1).ConfigureAwait(false);
                await t.Driver.BlackBoxEntries.UpdateResolutionAsync(old[0].Id, BlackBoxResolutionEnum.ArmadaFixed, "fixed in 1.2.0", null, DateTime.UtcNow, null).ConfigureAwait(false);
                BlackBoxFingerprintTrend trend = await bb.Service.GetFingerprintTrendAsync(BlackBoxTestKit.Admin(), "fp_trend").ConfigureAwait(false);
                AssertEqual(7L, trend.Total);
                AssertEqual("1.1.0,1.2.0,unknown", String.Join(",", trend.Versions.Select(v => v.ArmadaVersion)), "versions by first sighting");
                BlackBoxVersionCount v11 = trend.Versions[0];
                AssertEqual(4L, v11.Count); AssertEqual(3L, v11.OpenCount); AssertEqual(1L, v11.ArmadaFixedCount);
                AssertEqual(2L, trend.Versions[1].Count);
                AssertFalse(trend.Truncated);
                AssertEqual(BlackBoxPhaseEnum.Landing, trend.Phase);
                BlackBoxFingerprintTrend none = await bb.Service.GetFingerprintTrendAsync(BlackBoxTestKit.User("ten_q", "usr_q"), "fp_trend").ConfigureAwait(false);
                AssertEqual(0L, none.Total, "other tenants' entries are not counted");
                await AssertThrowsAsync<ArgumentException>(() => bb.Service.GetFingerprintTrendAsync(BlackBoxTestKit.Admin(), " "), "fingerprint required").ConfigureAwait(false);
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Black box maintainer bundle and trends", cases: cases);
        }

        private static async Task<List<BlackBoxEntry>> SeedAsync(DatabaseDriver db, int count, string fingerprint, string? version, string? tenantId, int daysAgo = -1)
        {
            List<BlackBoxEntry> created = new List<BlackBoxEntry>();
            for (int i = 0; i < count; i++)
            {
                BlackBoxEntry e = BlackBoxTestKit.Entry(tenantId, "msn_" + fingerprint + "_" + i, DateTime.UtcNow.AddDays(daysAgo).AddMinutes(i));
                e.Fingerprint = fingerprint;
                e.ArmadaVersion = version;
                e.ReasonExcerpt = "merge conflict in /Users/someone/Code/app/src/a.cs";
                created.Add(await db.BlackBoxEntries.CreateAsync(e).ConfigureAwait(false));
            }
            return created;
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(suiteId: SuiteId, caseId: caseId, displayName: displayName, executeAsync: (CancellationToken ct) => body(), tags: new List<string> { tag });
        }
    }
}

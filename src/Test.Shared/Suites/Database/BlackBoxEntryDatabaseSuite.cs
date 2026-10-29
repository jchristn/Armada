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
    /// Black box entry persistence (migration 103) on every provider: round trip, tenant scoping, attempt numbering and
    /// supersede, filters, paging bounds and the id tie-break, state, resolution, pin and expiry updates, cascade delete,
    /// expiry candidates, fingerprint aggregation, byte sums, uncaptured subjects and the narrow last-entry update.
    /// </summary>
    public sealed class BlackBoxEntryDatabaseSuite : IArmadaTestSuite
    {
        private const string SuiteId = "Database.BlackBoxEntries";
        private static readonly DateTime _T0 = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("roundtrip_all_fields", "Every column round-trips", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                BlackBoxEntry e = BlackBoxTestKit.Entry("ten_a", "msn_rt", _T0);
                e.VoyageId = "vyg_rt"; e.FleetId = "flt_rt"; e.CaptainId = "cpt_rt"; e.HarborId = "hbr_rt"; e.DockId = "dck_rt";
                e.MergeEntryId = "mrg_rt"; e.IncidentId = "inc_rt"; e.StageOutcomeId = "sto_rt"; e.ParentMissionId = "msn_parent";
                e.VesselName = "Vessel é"; e.Triage = BlackBoxTriageEnum.UserConfig; e.CaptureMode = BlackBoxCaptureModeEnum.Sweeper;
                e.State = BlackBoxStateEnum.Analyzed; e.Resolution = BlackBoxResolutionEnum.Duplicate; e.ResolutionNote = "dup"; e.ResolutionLink = "msn_other";
                e.Pinned = true; e.StorageTarget = BlackBoxStorageTargetEnum.S3; e.StoragePrefix = "armada/blackbox/x"; e.TotalBytes = 123456789012L;
                e.ObjectCount = 7; e.RulesVersion = 3; e.ArmadaVersion = "1.2.0+abc"; e.ArmadaCommit = "abc"; e.Summary = "sum";
                e.EnrichmentAttempts = 2; e.LastError = "err"; e.NextAttemptUtc = _T0.AddMinutes(4); e.ExpiresUtc = _T0.AddDays(180);
                e.EnrichedUtc = _T0.AddMinutes(1); e.AnalyzedUtc = _T0.AddMinutes(2); e.ResolvedUtc = _T0.AddMinutes(3); e.SupersedesId = "bbx_prev"; e.IsCurrent = false; e.Attempt = 4;
                await t.Driver.BlackBoxEntries.CreateAsync(e).ConfigureAwait(false);
                BlackBoxEntry r = (await t.Driver.BlackBoxEntries.ReadAsync(e.Id).ConfigureAwait(false))!;
                AssertTrue(r.Id.StartsWith("bbx_", StringComparison.Ordinal), "prefix");
                AssertEqual("ten_a", r.TenantId); AssertEqual("usr_ten_a", r.UserId); AssertEqual("msn_rt", r.MissionId); AssertEqual("vyg_rt", r.VoyageId);
                AssertEqual("flt_rt", r.FleetId); AssertEqual("cpt_rt", r.CaptainId); AssertEqual("hbr_rt", r.HarborId); AssertEqual("dck_rt", r.DockId);
                AssertEqual("mrg_rt", r.MergeEntryId); AssertEqual("inc_rt", r.IncidentId); AssertEqual("sto_rt", r.StageOutcomeId); AssertEqual("msn_parent", r.ParentMissionId);
                AssertEqual("Vessel é", r.VesselName); AssertEqual(4, r.Attempt); AssertFalse(r.IsCurrent, "is_current"); AssertEqual("bbx_prev", r.SupersedesId);
                AssertEqual(BlackBoxPhaseEnum.Landing, r.Phase); AssertEqual(BlackBoxStepEnum.TargetIntegration, r.Step); AssertEqual((MissionFailureKindEnum?)MissionFailureKindEnum.LandingConflict, r.FailureKind);
                AssertEqual("WorkProduced", r.StatusBefore); AssertEqual("LandingFailed", r.StatusAfter); AssertEqual(BlackBoxTriageEnum.UserConfig, r.Triage);
                AssertEqual("fp_default", r.Fingerprint); AssertEqual(BlackBoxCaptureModeEnum.Sweeper, r.CaptureMode); AssertEqual(BlackBoxStateEnum.Analyzed, r.State);
                AssertEqual(BlackBoxResolutionEnum.Duplicate, r.Resolution); AssertEqual("dup", r.ResolutionNote); AssertEqual("msn_other", r.ResolutionLink);
                AssertTrue(r.Pinned, "pinned"); AssertEqual(BlackBoxStorageTargetEnum.S3, r.StorageTarget); AssertEqual("armada/blackbox/x", r.StoragePrefix);
                AssertEqual(123456789012L, r.TotalBytes); AssertEqual(7, r.ObjectCount); AssertEqual(3, r.RulesVersion); AssertEqual("1.2.0+abc", r.ArmadaVersion);
                AssertEqual("abc", r.ArmadaCommit); AssertEqual("merge conflict in src/a.cs", r.ReasonExcerpt); AssertEqual("sum", r.Summary); AssertEqual(2, r.EnrichmentAttempts);
                AssertEqual("err", r.LastError); AssertEqual(_T0.AddMinutes(4), r.NextAttemptUtc!.Value.ToUniversalTime()); AssertEqual(_T0.AddDays(180), r.ExpiresUtc!.Value.ToUniversalTime());
                AssertEqual(_T0, r.CreatedUtc.ToUniversalTime()); AssertEqual(_T0.AddMinutes(3), r.ResolvedUtc!.Value.ToUniversalTime());
                AssertNull(r.Record, "record never stored in the row");
            }));

            cases.Add(Case("tenant_scoped_reads", "A read scoped to another tenant returns null; a missing id returns null", TestTags.Negative, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                BlackBoxEntry e = await t.Driver.BlackBoxEntries.CreateAsync(BlackBoxTestKit.Entry("ten_a", "msn_t")).ConfigureAwait(false);
                AssertNotNull(await t.Driver.BlackBoxEntries.ReadAsync(e.Id, "ten_a").ConfigureAwait(false), "own tenant");
                AssertNull(await t.Driver.BlackBoxEntries.ReadAsync(e.Id, "ten_b").ConfigureAwait(false), "other tenant");
                AssertNull(await t.Driver.BlackBoxEntries.ReadAsync("bbx_missing").ConfigureAwait(false), "missing");
                e.TenantId = "ten_a";
                BlackBoxEnumerateRequest other = new BlackBoxEnumerateRequest { TenantId = "ten_b" };
                AssertEqual(0L, (await t.Driver.BlackBoxEntries.EnumerateAsync(other).ConfigureAwait(false)).TotalRecords, "other tenant page");
                AssertNull(await t.Driver.BlackBoxEntries.ReadCurrentByMissionAsync("msn_t", "ten_b").ConfigureAwait(false), "current other tenant");
            }));

            cases.Add(Case("create_current_numbers_attempts", "CreateCurrent numbers attempts, supersedes the previous entry and keeps exactly one current entry per subject", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = t.Driver;
                BlackBoxEntry a1 = await db.BlackBoxEntries.CreateCurrentAsync(BlackBoxTestKit.Entry("ten_a", "msn_att", _T0)).ConfigureAwait(false);
                BlackBoxEntry a2 = await db.BlackBoxEntries.CreateCurrentAsync(BlackBoxTestKit.Entry("ten_a", "msn_att", _T0.AddMinutes(1))).ConfigureAwait(false);
                BlackBoxEntry other = await db.BlackBoxEntries.CreateCurrentAsync(BlackBoxTestKit.Entry("ten_a", "msn_other", _T0)).ConfigureAwait(false);
                AssertEqual(1, a1.Attempt); AssertNull(a1.SupersedesId, "first has nothing to supersede");
                AssertEqual(2, a2.Attempt); AssertEqual(a1.Id, a2.SupersedesId, "supersedes");
                AssertEqual(1, other.Attempt, "attempts are per subject");
                AssertFalse((await db.BlackBoxEntries.ReadAsync(a1.Id).ConfigureAwait(false))!.IsCurrent, "attempt 1 no longer current");
                AssertEqual(a2.Id, (await db.BlackBoxEntries.ReadCurrentByMissionAsync("msn_att").ConfigureAwait(false))!.Id, "current is attempt 2");
                EnumerationResult<BlackBoxEntry> attempts = await db.BlackBoxEntries.EnumerateByMissionAsync("msn_att", null, new EnumerationQuery()).ConfigureAwait(false);
                AssertEqual(2L, attempts.TotalRecords, "both attempts kept");
                AssertEqual(a2.Id, attempts.Objects[0].Id, "newest first");

                List<Task<BlackBoxEntry>> concurrent = Enumerable.Range(0, 5).Select(_ => db.BlackBoxEntries.CreateCurrentAsync(BlackBoxTestKit.Entry("ten_a", "msn_conc"))).ToList();
                await Task.WhenAll(concurrent).ConfigureAwait(false);
                long current = await db.BlackBoxEntries.CountAsync(new BlackBoxEnumerateRequest { MissionId = "msn_conc", IsCurrent = true }).ConfigureAwait(false);
                AssertEqual(1L, current, "exactly one current entry after concurrent captures");
                AssertEqual("1,2,3,4,5", String.Join(",", concurrent.Select(c => c.Result.Attempt).OrderBy(x => x)), "distinct attempt numbers");

                BlackBoxEntry voyage = BlackBoxTestKit.Entry("ten_a", null);
                voyage.SubjectType = BlackBoxSubjectTypeEnum.Voyage; voyage.VoyageId = "vyg_cur";
                await db.BlackBoxEntries.CreateCurrentAsync(voyage).ConfigureAwait(false);
                AssertEqual(voyage.Id, (await db.BlackBoxEntries.ReadCurrentByVoyageAsync("vyg_cur").ConfigureAwait(false))!.Id, "voyage current");
                await AssertThrowsAsync<ArgumentException>(() => db.BlackBoxEntries.CreateCurrentAsync(BlackBoxTestKit.Entry("ten_a", null)), "no subject id").ConfigureAwait(false);
            }));

            cases.Add(Case("enumerate_filters_and_paging", "Filters narrow in SQL; paging is bounded with exact totals and ties break by id", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = t.Driver;
                List<string> ids = new List<string>();
                for (int i = 0; i < 12; i++)
                {
                    BlackBoxEntry e = BlackBoxTestKit.Entry("ten_p", "msn_p" + (i % 3), _T0);
                    e.Phase = i % 2 == 0 ? BlackBoxPhaseEnum.Landing : BlackBoxPhaseEnum.Completion;
                    e.Step = i % 2 == 0 ? BlackBoxStepEnum.DirtyCheckout : BlackBoxStepEnum.ScopeViolation;
                    e.Triage = i < 4 ? BlackBoxTriageEnum.ArmadaDefect : BlackBoxTriageEnum.Unknown;
                    e.Pinned = i == 0;
                    e.Fingerprint = "fp_" + (i % 4);
                    e.VesselId = i < 6 ? "vsl_one" : "vsl_two";
                    e.Resolution = i == 11 ? BlackBoxResolutionEnum.Resolved : BlackBoxResolutionEnum.Open;
                    await db.BlackBoxEntries.CreateAsync(e).ConfigureAwait(false);
                    ids.Add(e.Id);
                }
                await db.BlackBoxEntries.CreateAsync(BlackBoxTestKit.Entry("ten_other", "msn_x", _T0)).ConfigureAwait(false);

                BlackBoxEnumerateRequest all = new BlackBoxEnumerateRequest { TenantId = "ten_p", PageSize = 5 };
                EnumerationResult<BlackBoxEntry> p1 = await db.BlackBoxEntries.EnumerateAsync(all).ConfigureAwait(false);
                AssertEqual(12L, p1.TotalRecords); AssertEqual(3, p1.TotalPages); AssertEqual(5, p1.Objects.Count);
                List<string> seen = new List<string>();
                for (int page = 1; page <= 3; page++)
                {
                    all.PageNumber = page;
                    seen.AddRange((await db.BlackBoxEntries.EnumerateAsync(all).ConfigureAwait(false)).Objects.Select(o => o.Id));
                }
                AssertEqual(12, seen.Distinct().Count(), "equal timestamps: no overlap or gap across pages");
                BlackBoxEnumerateRequest single = new BlackBoxEnumerateRequest { TenantId = "ten_p", PageSize = 100 };
                AssertEqual(String.Join(",", (await db.BlackBoxEntries.EnumerateAsync(single).ConfigureAwait(false)).Objects.Select(o => o.Id)), String.Join(",", seen), "pages concatenate to the single-page order (id tie-break in the provider's collation)");
                AssertEqual(String.Join(",", ids.OrderBy(x => x, StringComparer.Ordinal)), String.Join(",", seen.OrderBy(x => x, StringComparer.Ordinal)), "every row once");
                all.PageNumber = 4;
                EnumerationResult<BlackBoxEntry> past = await db.BlackBoxEntries.EnumerateAsync(all).ConfigureAwait(false);
                AssertEqual(0, past.Objects.Count, "page past the end is empty"); AssertEqual(12L, past.TotalRecords, "with totals");

                BlackBoxEnumerateRequest big = new BlackBoxEnumerateRequest { TenantId = "ten_p", PageSize = 5000 };
                AssertEqual(1000, big.PageSize, "page size normalized to 1000");

                AssertEqual(6L, await Count(db, new BlackBoxEnumerateRequest { TenantId = "ten_p", Phase = BlackBoxPhaseEnum.Landing }), "phase");
                AssertEqual(6L, await Count(db, new BlackBoxEnumerateRequest { TenantId = "ten_p", Step = BlackBoxStepEnum.ScopeViolation }), "step");
                AssertEqual(4L, await Count(db, new BlackBoxEnumerateRequest { TenantId = "ten_p", Triage = BlackBoxTriageEnum.ArmadaDefect }), "triage");
                AssertEqual(1L, await Count(db, new BlackBoxEnumerateRequest { TenantId = "ten_p", Pinned = true }), "pinned");
                AssertEqual(3L, await Count(db, new BlackBoxEnumerateRequest { TenantId = "ten_p", Fingerprint = "fp_1" }), "fingerprint");
                AssertEqual(6L, await Count(db, new BlackBoxEnumerateRequest { TenantId = "ten_p", VesselId = "vsl_two" }), "vessel");
                AssertEqual(4L, await Count(db, new BlackBoxEnumerateRequest { TenantId = "ten_p", MissionId = "msn_p1" }), "mission");
                AssertEqual(1L, await Count(db, new BlackBoxEnumerateRequest { TenantId = "ten_p", Resolution = BlackBoxResolutionEnum.Resolved }), "resolution");
                AssertEqual(12L, await Count(db, new BlackBoxEnumerateRequest { TenantId = "ten_p", Kind = MissionFailureKindEnum.LandingConflict }), "kind");
                AssertEqual(0L, await Count(db, new BlackBoxEnumerateRequest { TenantId = "ten_p", CreatedAfter = _T0.AddMinutes(1) }), "created after");
                AssertEqual(13L, await Count(db, new BlackBoxEnumerateRequest()), "no tenant filter sees all");

                BlackBoxEnumerateRequest search = new BlackBoxEnumerateRequest { TenantId = "ten_p", SearchText = "CONFLICT IN SRC" };
                AssertEqual(12L, await Count(db, search), "case-insensitive search over the reason");
                search.SearchText = "100%_[x]";
                AssertEqual(0L, await Count(db, search), "LIKE wildcards in the query are literal");
            }));

            cases.Add(Case("state_resolution_pin_expiry_updates", "Narrow updates change only their columns; Deleting entries are hidden unless asked for", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = t.Driver;
                BlackBoxEntry e = await db.BlackBoxEntries.CreateCurrentAsync(BlackBoxTestKit.Entry("ten_a", "msn_u")).ConfigureAwait(false);
                AssertTrue(await db.BlackBoxEntries.UpdateStateAsync(e.Id, BlackBoxStateEnum.EnrichmentFailed, "boom", 3, _T0).ConfigureAwait(false), "state");
                BlackBoxEntry r = (await db.BlackBoxEntries.ReadAsync(e.Id).ConfigureAwait(false))!;
                AssertEqual(BlackBoxStateEnum.EnrichmentFailed, r.State); AssertEqual("boom", r.LastError); AssertEqual(3, r.EnrichmentAttempts);
                await db.BlackBoxEntries.UpdateResolutionAsync(e.Id, BlackBoxResolutionEnum.Resolved, "fixed", "https://example.test/pr/1", _T0, _T0.AddDays(30)).ConfigureAwait(false);
                r = (await db.BlackBoxEntries.ReadAsync(e.Id).ConfigureAwait(false))!;
                AssertEqual(BlackBoxResolutionEnum.Resolved, r.Resolution); AssertEqual("fixed", r.ResolutionNote); AssertEqual(_T0.AddDays(30), r.ExpiresUtc!.Value.ToUniversalTime());
                await db.BlackBoxEntries.UpdatePinnedAsync(e.Id, true, null).ConfigureAwait(false);
                r = (await db.BlackBoxEntries.ReadAsync(e.Id).ConfigureAwait(false))!;
                AssertTrue(r.Pinned, "pinned"); AssertNull(r.ExpiresUtc, "pinned never expires");
                await db.BlackBoxEntries.UpdateExpiresAsync(e.Id, _T0).ConfigureAwait(false);
                AssertEqual(_T0, (await db.BlackBoxEntries.ReadAsync(e.Id).ConfigureAwait(false))!.ExpiresUtc!.Value.ToUniversalTime());
                AssertTrue(await db.BlackBoxEntries.MarkSupersededAsync(e.Id).ConfigureAwait(false), "superseded");
                AssertNull(await db.BlackBoxEntries.ReadCurrentByMissionAsync("msn_u").ConfigureAwait(false), "no current after supersede");
                AssertFalse(await db.BlackBoxEntries.UpdateStateAsync("bbx_missing", BlackBoxStateEnum.Analyzed).ConfigureAwait(false), "missing id changes nothing");

                await db.BlackBoxEntries.UpdateStateAsync(e.Id, BlackBoxStateEnum.Deleting).ConfigureAwait(false);
                AssertNull(await db.BlackBoxEntries.ReadAsync(e.Id).ConfigureAwait(false), "Deleting hidden");
                AssertNotNull(await db.BlackBoxEntries.ReadAsync(e.Id, null, true).ConfigureAwait(false), "Deleting readable when asked");
                AssertEqual(0L, await Count(db, new BlackBoxEnumerateRequest { MissionId = "msn_u" }), "Deleting hidden from lists");
                AssertEqual(1L, await Count(db, new BlackBoxEnumerateRequest { MissionId = "msn_u", State = BlackBoxStateEnum.Deleting }), "listed when filtered by Deleting");
                AssertEqual(1L, (await db.BlackBoxEntries.EnumerateDeletingAsync(new EnumerationQuery()).ConfigureAwait(false)).TotalRecords, "deleting page");
            }));

            cases.Add(Case("delete_cascade", "Deleting an entry removes its objects, files, links and notes and clears pointers to it", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = t.Driver;
                Mission mission = await BlackBoxTestKit.FailedMissionAsync(db).ConfigureAwait(false);
                BlackBoxEntry keep = await db.BlackBoxEntries.CreateCurrentAsync(BlackBoxTestKit.Entry("ten_a", "msn_keep")).ConfigureAwait(false);
                BlackBoxEntry e = await db.BlackBoxEntries.CreateCurrentAsync(BlackBoxTestKit.Entry("ten_a", mission.Id)).ConfigureAwait(false);
                BlackBoxEntry next = await db.BlackBoxEntries.CreateCurrentAsync(BlackBoxTestKit.Entry("ten_a", mission.Id)).ConfigureAwait(false);
                await db.BlackBoxEntries.SetMissionLastEntryAsync(mission.Id, e.Id).ConfigureAwait(false);
                foreach (BlackBoxEntry owner in new[] { e, keep })
                {
                    await db.BlackBoxObjects.UpsertAsync(new BlackBoxObject { EntryId = owner.Id, Name = "record.json", Data = new byte[] { 1, 2, 3 } }).ConfigureAwait(false);
                    await db.BlackBoxFiles.CreateManyAsync(new List<BlackBoxFile> { new BlackBoxFile { EntryId = owner.Id, Path = "src/a.cs", Directory = "src" } }).ConfigureAwait(false);
                    await db.BlackBoxLinks.CreateManyAsync(new List<BlackBoxLink> { new BlackBoxLink { EntryId = owner.Id, MissionId = "msn_link" } }).ConfigureAwait(false);
                    await db.BlackBoxNotes.CreateAsync(new BlackBoxNote { EntryId = owner.Id, Body = "note" }).ConfigureAwait(false);
                }
                AssertTrue(await db.BlackBoxEntries.DeleteCascadeAsync(e.Id).ConfigureAwait(false), "deleted");
                AssertNull(await db.BlackBoxEntries.ReadAsync(e.Id, null, true).ConfigureAwait(false), "entry gone");
                AssertEqual(0L, (await db.BlackBoxObjects.EnumerateAsync(e.Id, new EnumerationQuery()).ConfigureAwait(false)).TotalRecords, "objects gone");
                AssertEqual(0L, (await db.BlackBoxFiles.EnumerateAsync(e.Id, null, new EnumerationQuery()).ConfigureAwait(false)).TotalRecords, "files gone");
                AssertEqual(0L, (await db.BlackBoxLinks.EnumerateAsync(e.Id, new EnumerationQuery()).ConfigureAwait(false)).TotalRecords, "links gone");
                AssertEqual(0L, await db.BlackBoxNotes.CountByEntryAsync(e.Id).ConfigureAwait(false), "notes gone");
                AssertNull((await db.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!.LastBlackBoxEntryId, "mission pointer cleared");
                AssertNull((await db.BlackBoxEntries.ReadAsync(next.Id).ConfigureAwait(false))!.SupersedesId, "supersedes pointer cleared");
                AssertEqual(1L, (await db.BlackBoxObjects.EnumerateAsync(keep.Id, new EnumerationQuery()).ConfigureAwait(false)).TotalRecords, "other entry's objects kept");
                AssertEqual(1L, await db.BlackBoxNotes.CountByEntryAsync(keep.Id).ConfigureAwait(false), "other entry's notes kept");
                AssertFalse(await db.BlackBoxEntries.DeleteCascadeAsync(e.Id).ConfigureAwait(false), "second delete is a no-op");
            }));

            cases.Add(Case("expired_candidates", "Expiry candidates skip pinned, Deleting and (with keep-latest) the current entry of an existing mission", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = t.Driver;
                Mission live = await BlackBoxTestKit.FailedMissionAsync(db).ConfigureAwait(false);
                DateTime past = DateTime.UtcNow.AddDays(-1);
                BlackBoxEntry old = BlackBoxTestKit.Entry("ten_a", live.Id); old.ExpiresUtc = past;
                await db.BlackBoxEntries.CreateCurrentAsync(old).ConfigureAwait(false);
                BlackBoxEntry current = BlackBoxTestKit.Entry("ten_a", live.Id); current.ExpiresUtc = past;
                await db.BlackBoxEntries.CreateCurrentAsync(current).ConfigureAwait(false);
                BlackBoxEntry orphan = BlackBoxTestKit.Entry("ten_a", "msn_purged"); orphan.ExpiresUtc = past;
                await db.BlackBoxEntries.CreateCurrentAsync(orphan).ConfigureAwait(false);
                BlackBoxEntry pinned = BlackBoxTestKit.Entry("ten_a", "msn_pin"); pinned.ExpiresUtc = past; pinned.Pinned = true;
                await db.BlackBoxEntries.CreateAsync(pinned).ConfigureAwait(false);
                BlackBoxEntry future = BlackBoxTestKit.Entry("ten_a", "msn_future"); future.ExpiresUtc = DateTime.UtcNow.AddDays(10);
                await db.BlackBoxEntries.CreateAsync(future).ConfigureAwait(false);
                BlackBoxEntry never = BlackBoxTestKit.Entry("ten_a", "msn_never"); never.ExpiresUtc = null;
                await db.BlackBoxEntries.CreateAsync(never).ConfigureAwait(false);

                EnumerationResult<BlackBoxEntry> keep = await db.BlackBoxEntries.EnumerateExpiredAsync(DateTime.UtcNow, true, new EnumerationQuery { PageSize = 100 }).ConfigureAwait(false);
                AssertEqual(String.Join(",", new[] { old.Id, orphan.Id }.OrderBy(x => x)), String.Join(",", keep.Objects.Select(o => o.Id).OrderBy(x => x)), "old attempt and purged-mission entry expire; current of live mission kept");
                EnumerationResult<BlackBoxEntry> nokeep = await db.BlackBoxEntries.EnumerateExpiredAsync(DateTime.UtcNow, false, new EnumerationQuery { PageSize = 100 }).ConfigureAwait(false);
                AssertEqual(3L, nokeep.TotalRecords, "without keep-latest the current entry expires too");
                AssertFalse(nokeep.Objects.Any(o => o.Id == pinned.Id || o.Id == future.Id || o.Id == never.Id), "pinned, future and never excluded");
            }));

            cases.Add(Case("fingerprints_and_sums", "Fingerprint aggregation pages in SQL with the newest entry; byte sums and counts per vessel and target", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = t.Driver;
                for (int i = 0; i < 5; i++)
                {
                    BlackBoxEntry e = BlackBoxTestKit.Entry("ten_f", "msn_f" + i, _T0.AddMinutes(i));
                    e.Fingerprint = i < 3 ? "fp_big" : "fp_small";
                    e.Step = i == 2 ? BlackBoxStepEnum.Push : BlackBoxStepEnum.TargetIntegration;
                    e.TotalBytes = 100 * (i + 1);
                    e.Resolution = i == 0 ? BlackBoxResolutionEnum.Resolved : BlackBoxResolutionEnum.Open;
                    e.StorageTarget = i == 4 ? BlackBoxStorageTargetEnum.Disk : BlackBoxStorageTargetEnum.Database;
                    await db.BlackBoxEntries.CreateAsync(e).ConfigureAwait(false);
                }
                BlackBoxEntry none = BlackBoxTestKit.Entry("ten_f", "msn_nofp"); none.Fingerprint = null; none.TotalBytes = 1;
                await db.BlackBoxEntries.CreateAsync(none).ConfigureAwait(false);
                BlackBoxEntry other = BlackBoxTestKit.Entry("ten_g", "msn_g"); other.Fingerprint = "fp_big"; other.TotalBytes = 5000;
                await db.BlackBoxEntries.CreateAsync(other).ConfigureAwait(false);

                EnumerationResult<BlackBoxFingerprintSummary> fps = await db.BlackBoxEntries.EnumerateFingerprintsAsync(new BlackBoxFingerprintEnumerateRequest { TenantId = "ten_f" }).ConfigureAwait(false);
                AssertEqual(2L, fps.TotalRecords, "two fingerprints (entries without one skipped)");
                BlackBoxFingerprintSummary big = fps.Objects[0];
                AssertEqual("fp_big", big.Fingerprint, "largest first"); AssertEqual(3L, big.Count); AssertEqual(2L, big.OpenCount);
                AssertEqual(BlackBoxStepEnum.Push, big.Step, "newest entry's step"); AssertEqual(_T0, big.FirstSeenUtc.ToUniversalTime()); AssertEqual(_T0.AddMinutes(2), big.LastSeenUtc.ToUniversalTime());
                EnumerationResult<BlackBoxFingerprintSummary> page2 = await db.BlackBoxEntries.EnumerateFingerprintsAsync(new BlackBoxFingerprintEnumerateRequest { TenantId = "ten_f", PageSize = 1, PageNumber = 2 }).ConfigureAwait(false);
                AssertEqual("fp_small", page2.Objects.Single().Fingerprint, "second page");

                AssertEqual(1501L, await db.BlackBoxEntries.SumBytesAsync("ten_f").ConfigureAwait(false), "tenant bytes");
                AssertEqual(6501L, await db.BlackBoxEntries.SumBytesAsync().ConfigureAwait(false), "global bytes");
                AssertEqual(0L, await db.BlackBoxEntries.SumBytesAsync("ten_none").ConfigureAwait(false), "no rows sums to zero");
                AssertEqual(6L, await db.BlackBoxEntries.CountByVesselAsync("vsl_bb", "ten_f").ConfigureAwait(false), "per vessel");
                List<BlackBoxStorageTargetUsage> usage = await db.BlackBoxEntries.SumBytesByTargetAsync("ten_f").ConfigureAwait(false);
                AssertEqual(500L, usage.Single(u => u.Target == BlackBoxStorageTargetEnum.Disk).Bytes, "disk bytes");
                AssertEqual(5L, usage.Single(u => u.Target == BlackBoxStorageTargetEnum.Database).Entries, "database entries");
            }));

            cases.Add(Case("uncaptured_subjects_and_pointer", "Failed missions without a current entry are listed until captured; the narrow pointer update keeps last_update_utc", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = t.Driver;
                Mission failed = await BlackBoxTestKit.FailedMissionAsync(db).ConfigureAwait(false);
                Mission operatorCancel = await BlackBoxTestKit.FailedMissionAsync(db, null, null, MissionFailureKindEnum.OperatorAction, "stopped", MissionStatusEnum.Cancelled).ConfigureAwait(false);
                Mission plainCancel = await BlackBoxTestKit.FailedMissionAsync(db, null, null, null, "cancelled", MissionStatusEnum.Cancelled).ConfigureAwait(false);
                Mission dependencyCancel = await BlackBoxTestKit.FailedMissionAsync(db, null, null, MissionFailureKindEnum.DependencyFailed, "dependency", MissionStatusEnum.Cancelled).ConfigureAwait(false);
                Mission complete = await BlackBoxTestKit.FailedMissionAsync(db, null, null, null, "", MissionStatusEnum.Complete).ConfigureAwait(false);
                EnumerationResult<string> ids = await db.BlackBoxEntries.EnumerateUncapturedMissionIdsAsync(null, new EnumerationQuery { PageSize = 100 }).ConfigureAwait(false);
                AssertTrue(ids.Objects.Contains(failed.Id), "failed listed");
                AssertTrue(ids.Objects.Contains(dependencyCancel.Id), "non-operator cancel listed");
                AssertFalse(ids.Objects.Contains(operatorCancel.Id), "operator cancel not listed");
                AssertFalse(ids.Objects.Contains(plainCancel.Id), "cancel without a kind not listed");
                AssertFalse(ids.Objects.Contains(complete.Id), "complete not listed");

                DateTime before = (await db.Missions.ReadAsync(failed.Id).ConfigureAwait(false))!.LastUpdateUtc;
                BlackBoxEntry e = await db.BlackBoxEntries.CreateCurrentAsync(BlackBoxTestKit.Entry("ten_s", failed.Id)).ConfigureAwait(false);
                AssertTrue(await db.BlackBoxEntries.SetMissionLastEntryAsync(failed.Id, e.Id).ConfigureAwait(false), "pointer set");
                Mission reread = (await db.Missions.ReadAsync(failed.Id).ConfigureAwait(false))!;
                AssertEqual(e.Id, reread.LastBlackBoxEntryId, "pointer read back");
                AssertEqual(before, reread.LastUpdateUtc, "last_update_utc untouched");
                ids = await db.BlackBoxEntries.EnumerateUncapturedMissionIdsAsync(null, new EnumerationQuery { PageSize = 100 }).ConfigureAwait(false);
                AssertFalse(ids.Objects.Contains(failed.Id), "captured mission drops out");

                reread.Title = "edited";
                reread.LastBlackBoxEntryId = null;
                await db.Missions.UpdateAsync(reread).ConfigureAwait(false);
                AssertEqual(e.Id, (await db.Missions.ReadAsync(failed.Id).ConfigureAwait(false))!.LastBlackBoxEntryId, "a stale full-row update never clears the pointer");

                Voyage voyage = new Voyage("bb voyage") { Status = VoyageStatusEnum.Failed };
                await db.Voyages.CreateAsync(voyage).ConfigureAwait(false);
                AssertTrue((await db.BlackBoxEntries.EnumerateUncapturedVoyageIdsAsync(null, new EnumerationQuery { PageSize = 100 }).ConfigureAwait(false)).Objects.Contains(voyage.Id), "failed voyage listed");
                BlackBoxEntry ve = BlackBoxTestKit.Entry("ten_s", null); ve.SubjectType = BlackBoxSubjectTypeEnum.Voyage; ve.VoyageId = voyage.Id;
                await db.BlackBoxEntries.CreateCurrentAsync(ve).ConfigureAwait(false);
                await db.BlackBoxEntries.SetVoyageLastEntryAsync(voyage.Id, ve.Id).ConfigureAwait(false);
                AssertEqual(ve.Id, (await db.Voyages.ReadAsync(voyage.Id).ConfigureAwait(false))!.LastBlackBoxEntryId, "voyage pointer");
                AssertFalse((await db.BlackBoxEntries.EnumerateUncapturedVoyageIdsAsync(null, new EnumerationQuery { PageSize = 100 }).ConfigureAwait(false)).Objects.Contains(voyage.Id), "captured voyage drops out");
            }));

            cases.Add(Case("retryable_entries", "EnrichmentFailed entries are due when under the attempt cap and past their next attempt", TestTags.Negative, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = t.Driver;
                BlackBoxEntry due = await db.BlackBoxEntries.CreateAsync(BlackBoxTestKit.Entry("ten_r", "msn_due")).ConfigureAwait(false);
                BlackBoxEntry later = await db.BlackBoxEntries.CreateAsync(BlackBoxTestKit.Entry("ten_r", "msn_later")).ConfigureAwait(false);
                BlackBoxEntry exhausted = await db.BlackBoxEntries.CreateAsync(BlackBoxTestKit.Entry("ten_r", "msn_exhausted")).ConfigureAwait(false);
                await db.BlackBoxEntries.UpdateStateAsync(due.Id, BlackBoxStateEnum.EnrichmentFailed, "x", 1, DateTime.UtcNow.AddMinutes(-1)).ConfigureAwait(false);
                await db.BlackBoxEntries.UpdateStateAsync(later.Id, BlackBoxStateEnum.EnrichmentFailed, "x", 1, DateTime.UtcNow.AddHours(1)).ConfigureAwait(false);
                await db.BlackBoxEntries.UpdateStateAsync(exhausted.Id, BlackBoxStateEnum.EnrichmentFailed, "x", 5, null).ConfigureAwait(false);
                EnumerationResult<BlackBoxEntry> retry = await db.BlackBoxEntries.EnumerateRetryableAsync(DateTime.UtcNow, 5, new EnumerationQuery()).ConfigureAwait(false);
                AssertEqual(due.Id, retry.Objects.Single().Id, "only the due entry");
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Black box entry persistence", cases: cases);
        }

        private static async Task<long> Count(DatabaseDriver db, BlackBoxEnumerateRequest q)
        {
            long count = await db.BlackBoxEntries.CountAsync(q).ConfigureAwait(false);
            long paged = (await db.BlackBoxEntries.EnumerateAsync(q).ConfigureAwait(false)).TotalRecords;
            AssertEqual(count, paged, "CountAsync and the page total agree");
            return count;
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(suiteId: SuiteId, caseId: caseId, displayName: displayName, executeAsync: (CancellationToken ct) => body(), tags: new List<string> { tag });
        }
    }
}

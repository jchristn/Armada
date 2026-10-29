namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.BlackBox;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Black box prevention (plan 13.1): overlap warnings and the overlap preview, soft directory reservations, the brief
    /// linter, dispatch advice (warnings on the dispatched mission and voyage, chaining with SerializeOverlappingMissions)
    /// and the configuration doctor.
    /// </summary>
    public sealed class BlackBoxPreventionSuite : IArmadaTestSuite
    {
        private const string SuiteId = "Services.BlackBoxPrevention";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("lint_flags_dependency_noop_and_paths", "The linter flags dependency language without DependsOnMissionId, verification-only briefs, host paths and paths outside the vessel", TestTags.Positive, () =>
            {
                VesselTrackedFiles tracked = VesselTrackedFiles.From(new[] { "src/Armada.Core/A.cs", "src/Armada.Core/B.cs", "docs/README.md" });
                BriefLintResult dep = MissionBriefLinter.Lint("Wire the API", "Do this after mission 2 lands. It depends on the schema change.", null, tracked);
                AssertTrue(dep.Findings.Any(f => f.RuleId == MissionBriefLinter.DependencyRule), "dependency language flagged");
                AssertFalse(dep.Clean);
                AssertTrue(dep.Findings.All(f => f.Suggestion != null && f.Message.Length > 0), "every finding has a message and suggestion");
                BriefLintResult noop = MissionBriefLinter.Lint("Check the build", "Verify that the build passes and report whether tests are green.", null, tracked);
                AssertEqual(MissionBriefLinter.NoOpRule, noop.Findings.Single().RuleId, "verification-only brief");
                BriefLintResult noChange = MissionBriefLinter.Lint("Review", "Read src/Armada.Core/A.cs. Do not change anything.", null, tracked);
                AssertTrue(noChange.Findings.Any(f => f.RuleId == MissionBriefLinter.NoOpRule), "no-change phrase");
                BriefLintResult host = MissionBriefLinter.Lint("Fix", "Fix the bug in /Users/joel/Code/Armada/src/x.cs and ../other/y.cs", null, tracked);
                AssertEqual(2, host.Findings.Count(f => f.RuleId == MissionBriefLinter.HostPathRule), "host and parent-relative paths");
                BriefLintResult outside = MissionBriefLinter.Lint("Fix", "Change frontend/app/main.ts and src/Armada.Core/A.cs and src/Armada.Core/New.cs", null, tracked);
                BriefLintFinding path = outside.Findings.Single(f => f.RuleId == MissionBriefLinter.OutsidePathRule);
                AssertEqual("frontend/app/main.ts", path.Path, "only the path whose top-level directory is missing");
                AssertTrue(outside.PathsChecked);
            }));

            cases.Add(Case("lint_quiet_on_clean_briefs", "The linter is quiet on a clean brief, an explicit dependency, an Audit mission and without tracked files", TestTags.Negative, () =>
            {
                VesselTrackedFiles tracked = VesselTrackedFiles.From(new[] { "src/a.cs" });
                AssertTrue(MissionBriefLinter.Lint("Add retry", "Add a retry to src/a.cs and a test.", null, tracked).Clean, "clean brief");
                AssertTrue(MissionBriefLinter.Lint("Add retry", "After mission 2 lands, add a retry to src/a.cs.", "msn_dependency", tracked).Clean, "explicit dependency");
                AssertTrue(MissionBriefLinter.Lint("Audit", "Verify that the build passes and report.", null, tracked, MissionModeEnum.Audit).Clean, "Audit mode expects no changes");
                BriefLintResult unchecked_ = MissionBriefLinter.Lint("Fix", "Change frontend/app/main.ts", null, null);
                AssertTrue(unchecked_.Clean, "paths are not checked without tracked files"); AssertFalse(unchecked_.PathsChecked);
                BriefLintResult many = MissionBriefLinter.Lint("x", String.Join(" ", Enumerable.Range(0, 200).Select(i => "/tmp/f" + i + ".txt")), null, null);
                AssertEqual(BlackBoxInsightLimits.MaxLintFindings, many.Findings.Count, "at most 50 findings");
            }));

            cases.Add(Case("file_set_overlap", "Mission file sets overlap by path, bare file name and directory containment, case-insensitively", TestTags.Positive, () =>
            {
                MissionFileSet a = new MissionFileSet();
                a.AddFile("./src/Core/A.cs"); a.AddFile("README.md"); a.AddDirectory("docs/");
                MissionFileSet b = new MissionFileSet();
                b.AddFile("SRC/core/a.cs"); b.AddFile("tools/README.md"); b.AddFile("docs/guide/x.md");
                List<string> overlap = a.OverlapWith(b);
                AssertTrue(overlap.Contains("src/Core/A.cs"), "path"); AssertTrue(overlap.Contains("README.md"), "bare name"); AssertTrue(overlap.Contains("docs/"), "directory");
                MissionFileSet c = new MissionFileSet(); c.AddFile("lib/z.cs");
                AssertEqual(0, a.OverlapWith(c).Count, "no overlap");
                AssertEqual("src/Core", MissionFileSet.DirectoryOf("src/Core/A.cs"));
                AssertEqual(1, a.EntriesIn("src/Core").Count, "entries in a directory");
            }));

            cases.Add(Case("preview_reports_active_mission_overlap", "A preview reports an active mission sharing scoped files, verified brief paths and the suggested dependency; a mission elsewhere is quiet", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver);
                bb.Insights.Analyzer.TrackedFilesOverride = v => new List<string> { "src/core/a.cs", "src/core/b.cs", "src/ui/c.ts" };
                Vessel vessel = await t.Driver.Vessels.CreateAsync(new Vessel("bb-prev-" + Suffix(), "https://example.invalid/prev.git")).ConfigureAwait(false);
                Mission older = await ActiveMissionAsync(t.Driver, vessel, "Older", "Touch only src/core/a.cs", MissionStatusEnum.InProgress, -20).ConfigureAwait(false);
                Mission newer = await ActiveMissionAsync(t.Driver, vessel, "Newer", "Touch only src/core/a.cs, src/core/b.cs", MissionStatusEnum.Pending, -5).ConfigureAwait(false);
                await ActiveMissionAsync(t.Driver, vessel, "Elsewhere", "Touch only src/ui/c.ts", MissionStatusEnum.InProgress, -3).ConfigureAwait(false);
                await ActiveMissionAsync(t.Driver, vessel, "Done", "Touch only src/core/a.cs", MissionStatusEnum.Complete, -30).ConfigureAwait(false);

                MissionOverlapPreview preview = await bb.Insights.Service.PreviewOverlapAsync(BlackBoxTestKit.Admin(), vessel.Id, new MissionOverlapPreviewRequest
                {
                    Title = "Refactor core",
                    Description = "Change src/core/b.cs and src/core/a.cs; also mention not/a/real/path.cs."
                }).ConfigureAwait(false);
                AssertTrue(preview.TrackedFilesKnown);
                AssertEqual(2, preview.BriefPathCount, "two brief paths exist in the vessel");
                AssertFalse(preview.LikelyFiles.Contains("not/a/real/path.cs"), "unverified paths are not likely files");
                AssertEqual(3, preview.ActiveMissionsCompared, "only active missions are compared");
                List<MissionOverlapWarning> active = preview.Warnings.Where(w => w.Kind == MissionOverlapKindEnum.ActiveMission).ToList();
                AssertEqual(2, active.Count, "the two core missions overlap; the ui mission does not");
                AssertEqual(newer.Id, active[0].OverlappingMissionId, "the larger overlap first");
                AssertEqual(2, active[0].FileCount);
                AssertEqual(newer.Id, preview.SuggestedDependsOnMissionId, "suggests the strongest overlap");
                AssertFalse(preview.SerializeOverlappingMissions);
                AssertNotNull(preview.BriefLint, "lint included");

                MissionOverlapPreview withDependency = await bb.Insights.Service.PreviewOverlapAsync(BlackBoxTestKit.Admin(), vessel.Id, new MissionOverlapPreviewRequest { Description = "Touch only src/core/a.cs", DependsOnMissionId = older.Id }).ConfigureAwait(false);
                AssertNull(withDependency.SuggestedDependsOnMissionId, "no suggestion when a dependency is set");
                MissionOverlapPreview excluded = await bb.Insights.Service.PreviewOverlapAsync(BlackBoxTestKit.Admin(), vessel.Id, new MissionOverlapPreviewRequest { Description = "Touch only src/core/b.cs", ExcludeMissionId = newer.Id }).ConfigureAwait(false);
                AssertEqual(0, excluded.WarningCount, "the excluded mission is not compared");
                MissionOverlapPreview quiet = await bb.Insights.Service.PreviewOverlapAsync(BlackBoxTestKit.Admin(), vessel.Id, new MissionOverlapPreviewRequest { Description = "Touch only lib/other.cs" }).ConfigureAwait(false);
                AssertEqual(0, quiet.WarningCount, "no overlap");
            }));

            cases.Add(Case("preview_refusals", "Preview refuses an empty request, an unknown or foreign vessel, and an unauthenticated caller", TestTags.Negative, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver);
                Vessel vessel = new Vessel("bb-prev-ref-" + Suffix(), "https://example.invalid/prev.git");
                vessel.TenantId = Armada.Core.Constants.DefaultTenantId;
                vessel = await t.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);
                BlackBoxInsightService svc = bb.Insights.Service;
                await AssertThrowsAsync<ArgumentException>(() => svc.PreviewOverlapAsync(BlackBoxTestKit.Admin(), vessel.Id, new MissionOverlapPreviewRequest()), "empty").ConfigureAwait(false);
                await AssertThrowsAsync<KeyNotFoundException>(() => svc.PreviewOverlapAsync(BlackBoxTestKit.Admin(), "vsl_missing", new MissionOverlapPreviewRequest { Title = "x" }), "unknown").ConfigureAwait(false);
                await AssertThrowsAsync<KeyNotFoundException>(() => svc.PreviewOverlapAsync(BlackBoxTestKit.User("ten_other", "usr_other", true), vessel.Id, new MissionOverlapPreviewRequest { Title = "x" }), "another tenant's vessel reads as not found").ConfigureAwait(false);
                await AssertThrowsAsync<UnauthorizedAccessException>(() => svc.PreviewOverlapAsync(new AuthContext(), vessel.Id, new MissionOverlapPreviewRequest { Title = "x" }), "unauthenticated").ConfigureAwait(false);
                await AssertThrowsAsync<KeyNotFoundException>(() => svc.LintBriefAsync(BlackBoxTestKit.User("ten_other", "usr_other", true), new BriefLintRequest { Title = "x", VesselId = vessel.Id }), "lint with a foreign vessel").ConfigureAwait(false);
                await AssertThrowsAsync<ArgumentException>(() => svc.LintBriefAsync(BlackBoxTestKit.Admin(), new BriefLintRequest()), "lint without text").ConfigureAwait(false);
            }));

            cases.Add(Case("hotspots_and_reservations", "Hot directories warn; an active mission's scoped files in a hot directory reserve it; threshold 0 turns reservations off", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = BlackBoxTestKit.Settings();
                settings.BlackBox.HotspotReservationThreshold = 2;
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver, settings);
                bb.Insights.Analyzer.TrackedFilesOverride = v => null;
                Vessel vessel = await t.Driver.Vessels.CreateAsync(new Vessel("bb-hot-" + Suffix(), "https://example.invalid/hot.git")).ConfigureAwait(false);
                await SeedHotspotAsync(t.Driver, vessel, "src/hot", 3).ConfigureAwait(false);
                await SeedHotspotAsync(t.Driver, vessel, "src/warm", 1).ConfigureAwait(false);
                Mission holder = await ActiveMissionAsync(t.Driver, vessel, "Holder", "Touch only src/hot/x.cs", MissionStatusEnum.InProgress, -10).ConfigureAwait(false);

                VesselReservationPage reservations = await bb.Insights.Service.EnumerateReservationsAsync(BlackBoxTestKit.Admin(), vessel.Id, new EnumerationQuery()).ConfigureAwait(false);
                AssertEqual(2, reservations.Threshold);
                AssertEqual(1L, reservations.TotalRecords, "one reservation");
                VesselReservation r = reservations.Objects.Single();
                AssertEqual("src/hot", r.Directory); AssertEqual(holder.Id, r.MissionId); AssertEqual(3L, r.HotspotEntryCount); AssertEqual(1, r.FileCount);
                AssertEqual(1, reservations.HotDirectoryCount, "warm directory below the threshold");

                MissionOverlapPreview preview = await bb.Insights.Service.PreviewOverlapAsync(BlackBoxTestKit.Admin(), vessel.Id, new MissionOverlapPreviewRequest { Description = "Touch only src/hot/y.cs, src/warm/z.cs" }).ConfigureAwait(false);
                MissionOverlapWarning reserved = preview.Warnings.Single(w => w.Kind == MissionOverlapKindEnum.Reservation);
                AssertEqual(holder.Id, reserved.OverlappingMissionId); AssertEqual("src/hot", reserved.Directory);
                AssertFalse(preview.Warnings.Any(w => w.Kind == MissionOverlapKindEnum.Hotspot && w.Directory == "src/hot"), "the reservation replaces the hotspot warning for its directory");
                AssertFalse(preview.Warnings.Any(w => w.Directory == "src/warm"), "a directory below the threshold is not hot");
                AssertEqual(holder.Id, preview.SuggestedDependsOnMissionId, "a reservation can chain the mission");

                settings.BlackBox.HotspotReservationThreshold = 0;
                VesselReservationPage off = await bb.Insights.Service.EnumerateReservationsAsync(BlackBoxTestKit.Admin(), vessel.Id, null).ConfigureAwait(false);
                AssertEqual(0L, off.TotalRecords, "reservations off");
                MissionOverlapPreview hotOnly = await bb.Insights.Service.PreviewOverlapAsync(BlackBoxTestKit.Admin(), vessel.Id, new MissionOverlapPreviewRequest { Description = "Touch only src/hot/y.cs" }).ConfigureAwait(false);
                AssertEqual(MissionOverlapKindEnum.Hotspot, hotOnly.Warnings.Single().Kind, "without reservations the directory still warns as a hotspot (default threshold 3)");
                AssertNull(hotOnly.SuggestedDependsOnMissionId, "a hotspot alone never chains");
                await AssertThrowsAsync<KeyNotFoundException>(() => bb.Insights.Service.EnumerateReservationsAsync(BlackBoxTestKit.User("ten_x", "usr_x"), vessel.Id, null), "invisible vessel").ConfigureAwait(false);
            }));

            cases.Add(Case("dispatch_warns_and_serializes", "Dispatch returns overlap warnings and lint findings on the mission and voyage; with SerializeOverlappingMissions the mission is chained after the overlapping one, never replacing an explicit dependency", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver);
                bb.Insights.Analyzer.TrackedFilesOverride = v => new List<string> { "src/a.cs", "src/b.cs" };
                AdmiralService admiral = CreateAdmiral(t.Driver);
                admiral.DispatchAdvisor = bb.Insights.DispatchAdvisor;
                Vessel vessel = await t.Driver.Vessels.CreateAsync(new Vessel("bb-disp-" + Suffix(), "https://example.invalid/disp.git")).ConfigureAwait(false);

                Mission first = await admiral.DispatchMissionAsync(NewMission(vessel, "First", "Touch only src/a.cs")).ConfigureAwait(false);
                AssertNull(first.OverlapWarnings, "nothing to overlap yet");
                Mission second = await admiral.DispatchMissionAsync(NewMission(vessel, "Second", "Touch only src/a.cs. Do this after mission 1 lands.")).ConfigureAwait(false);
                AssertNotNull(second.OverlapWarnings, "overlap reported");
                MissionOverlapWarning w = second.OverlapWarnings!.Single();
                AssertEqual(first.Id, w.OverlappingMissionId); AssertEqual(second.Id, w.MissionId, "warning names the new mission"); AssertFalse(w.Serialized);
                AssertTrue(second.BriefLintWarnings!.Any(f => f.RuleId == MissionBriefLinter.DependencyRule), "lint findings returned");
                AssertNull(second.DependsOnMissionId, "not chained without the vessel setting");

                vessel.SerializeOverlappingMissions = true;
                vessel = await t.Driver.Vessels.UpdateAsync(vessel).ConfigureAwait(false);
                AssertTrue((await t.Driver.Vessels.ReadAsync(vessel.Id).ConfigureAwait(false))!.SerializeOverlappingMissions, "setting persisted");
                Mission third = await admiral.DispatchMissionAsync(NewMission(vessel, "Third", "Touch only src/a.cs")).ConfigureAwait(false);
                AssertNotNull(third.DependsOnMissionId, "chained");
                AssertTrue(third.DependsOnMissionId == first.Id || third.DependsOnMissionId == second.Id, "after an overlapping mission");
                AssertTrue(third.OverlapWarnings!.Any(x => x.Serialized && x.OverlappingMissionId == third.DependsOnMissionId), "the chained overlap is marked Serialized");
                AssertEqual(third.DependsOnMissionId, (await t.Driver.Missions.ReadAsync(third.Id).ConfigureAwait(false))!.DependsOnMissionId, "dependency stored");

                Mission explicitDep = NewMission(vessel, "Fourth", "Touch only src/a.cs");
                explicitDep.DependsOnMissionId = first.Id;
                explicitDep = await admiral.DispatchMissionAsync(explicitDep).ConfigureAwait(false);
                AssertEqual(first.Id, explicitDep.DependsOnMissionId, "an explicit dependency is never replaced");
                Mission unrelated = await admiral.DispatchMissionAsync(NewMission(vessel, "Fifth", "Touch only src/b.cs")).ConfigureAwait(false);
                AssertNull(unrelated.DependsOnMissionId, "no overlap, no chain");

                Voyage voyage = await admiral.DispatchVoyageAsync("Voyage", "v", vessel.Id, new List<MissionDescription>
                {
                    new MissionDescription("V1", "Touch only src/b.cs"),
                    new MissionDescription("V2", "Touch only src/b.cs")
                }).ConfigureAwait(false);
                AssertNotNull(voyage.OverlapWarnings, "voyage carries its missions' warnings");
                AssertTrue(voyage.OverlapWarnings!.Count >= 2, "both voyage missions overlap the unrelated mission or each other");
                List<Mission> voyageMissions = await t.Driver.Missions.EnumerateByVoyageAsync(voyage.Id).ConfigureAwait(false);
                AssertTrue(voyageMissions.Any(m => m.DependsOnMissionId != null), "a voyage mission is chained");
            }));

            cases.Add(Case("dispatch_survives_a_failing_advisor", "A throwing advisor is logged and the mission is still dispatched without warnings", TestTags.Negative, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                AdmiralService admiral = CreateAdmiral(t.Driver);
                admiral.DispatchAdvisor = new ThrowingDispatchAdvisor();
                Vessel vessel = await t.Driver.Vessels.CreateAsync(new Vessel("bb-throw-" + Suffix(), "https://example.invalid/throw.git")).ConfigureAwait(false);
                Mission m = await admiral.DispatchMissionAsync(NewMission(vessel, "Survives", "Touch only src/a.cs")).ConfigureAwait(false);
                AssertNotNull(await t.Driver.Missions.ReadAsync(m.Id).ConfigureAwait(false), "created");
                AssertNull(m.OverlapWarnings); AssertNull(m.BriefLintWarnings);
            }));

            cases.Add(Case("doctor_groups_recurring_recommendations", "The configuration doctor groups recommendations by rule and setting across entries, most frequent first, scoped to the caller", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver);
                string tenant = Armada.Core.Constants.DefaultTenantId;
                string owner = Armada.Core.Constants.DefaultUserId;
                Vessel vessel = new Vessel("bb-doctor-" + Suffix(), "https://example.invalid/doctor.git");
                vessel.TenantId = tenant;
                vessel.UserId = owner;
                vessel = await t.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);
                for (int i = 0; i < 3; i++) await DirtyCheckoutEntryAsync(t.Driver, bb, vessel, tenant, owner).ConfigureAwait(false);
                await DirtyCheckoutEntryAsync(t.Driver, bb, vessel, tenant, "usr_other").ConfigureAwait(false);

                BlackBoxDoctorPage tenantView = await bb.Insights.Service.GetDoctorAsync(BlackBoxTestKit.User(tenant, "usr_admin", true), vessel.Id, null, null).ConfigureAwait(false);
                AssertEqual(4L, tenantView.EntriesInWindow); AssertEqual(4, tenantView.EntriesScanned); AssertFalse(tenantView.ScanTruncated);
                AssertTrue(tenantView.TotalRecords >= 1, "a recurring recommendation");
                BlackBoxDoctorFinding top = tenantView.Objects.First();
                AssertEqual(4, top.Count, "counted across the tenant's entries");
                AssertEqual("BBX-LAND-001", top.RuleId);
                AssertTrue(top.FirstSeenUtc <= top.LastSeenUtc);
                AssertStartsWith("bbx_", top.ExampleEntryId);

                BlackBoxDoctorPage userView = await bb.Insights.Service.GetDoctorAsync(BlackBoxTestKit.User(tenant, owner), vessel.Id, 7, new EnumerationQuery { PageSize = 1 }).ConfigureAwait(false);
                AssertEqual(3L, userView.EntriesInWindow, "a user sees their own entries");
                AssertEqual(3, userView.Objects.First().Count);
                AssertEqual(1, userView.Objects.Count, "paged");
                await AssertThrowsAsync<ArgumentOutOfRangeException>(() => bb.Insights.Service.GetDoctorAsync(BlackBoxTestKit.Admin(), vessel.Id, 0, null), "window too small").ConfigureAwait(false);
                await AssertThrowsAsync<ArgumentOutOfRangeException>(() => bb.Insights.Service.GetDoctorAsync(BlackBoxTestKit.Admin(), vessel.Id, 366, null), "window too large").ConfigureAwait(false);
                await AssertThrowsAsync<KeyNotFoundException>(() => bb.Insights.Service.GetDoctorAsync(BlackBoxTestKit.User("ten_other", "usr_x", true), vessel.Id, null, null), "foreign vessel").ConfigureAwait(false);
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Black box prevention: overlap, reservations, brief lint, dispatch advice, doctor", cases: cases);
        }

        internal static string Suffix()
        {
            return Guid.NewGuid().ToString("N").Substring(0, 8);
        }

        internal static async Task<Mission> ActiveMissionAsync(DatabaseDriver db, Vessel vessel, string title, string description, MissionStatusEnum status, int minutesAgo)
        {
            Mission m = new Mission(title + " " + Suffix(), description);
            m.VesselId = vessel.Id;
            m.TenantId = vessel.TenantId;
            m.Status = status;
            m.CreatedUtc = DateTime.UtcNow.AddMinutes(minutesAgo);
            return await db.Missions.CreateAsync(m).ConfigureAwait(false);
        }

        internal static async Task SeedHotspotAsync(DatabaseDriver db, Vessel vessel, string directory, int entries)
        {
            for (int i = 0; i < entries; i++)
            {
                BlackBoxEntry e = BlackBoxTestKit.Entry(vessel.TenantId, "msn_hot_" + Suffix());
                e.VesselId = vessel.Id;
                e = await db.BlackBoxEntries.CreateAsync(e).ConfigureAwait(false);
                await db.BlackBoxFiles.CreateManyAsync(new List<BlackBoxFile>
                {
                    new BlackBoxFile { EntryId = e.Id, TenantId = vessel.TenantId, VesselId = vessel.Id, Path = directory + "/f" + i + ".cs", Directory = directory, Role = BlackBoxFileRoleEnum.Conflicted }
                }).ConfigureAwait(false);
            }
        }

        internal static async Task<string> DirtyCheckoutEntryAsync(DatabaseDriver db, BlackBoxRuntime bb, Vessel vessel, string tenantId, string userId)
        {
            Mission mission = await BlackBoxTestKit.FailedMissionAsync(db, null, null, MissionFailureKindEnum.Infra, "dirty checkout", MissionStatusEnum.LandingFailed).ConfigureAwait(false);
            mission.VesselId = vessel.Id;
            mission = await db.Missions.UpdateAsync(mission).ConfigureAwait(false);
            mission.TenantId = tenantId;
            mission.UserId = userId;
            string id = (await bb.Capture.RecordMissionFailureAsync(mission, MissionStatusEnum.WorkProduced, BlackBoxStepEnum.DirtyCheckout, c =>
            {
                c.Vessel = vessel;
                c.GitFacts = new BlackBoxGitFacts { CheckoutPath = "/work/checkout" };
                c.GitFacts.DirtyPaths.AddRange(new[] { "src/a.cs" });
            }).ConfigureAwait(false))!;
            await bb.Pipeline.ProcessAsync(id).ConfigureAwait(false);
            return id;
        }

        internal static AdmiralService CreateAdmiral(DatabaseDriver db)
        {
            LoggingModule logging = BlackBoxTestKit.Logging();
            ArmadaSettings settings = BlackBoxTestKit.Settings();
            DirCreatingGitService git = new DirCreatingGitService();
            IDockService dockService = new DockService(logging, db, settings, git);
            CaptainService captainService = new CaptainService(logging, db, settings, git, dockService);
            MissionService missionService = new MissionService(logging, db, settings, dockService, captainService, git: git);
            IVoyageService voyageService = new VoyageService(logging, db);
            return new AdmiralService(logging, db, settings, captainService, missionService, voyageService, dockService);
        }

        private static Mission NewMission(Vessel vessel, string title, string description)
        {
            Mission m = new Mission(title, description);
            m.VesselId = vessel.Id;
            return m;
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(suiteId: SuiteId, caseId: caseId, displayName: displayName, executeAsync: (CancellationToken ct) => body(), tags: new List<string> { tag });
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(suiteId: SuiteId, caseId: caseId, displayName: displayName, executeAsync: (CancellationToken ct) => { body(); return Task.CompletedTask; }, tags: new List<string> { tag });
        }
    }
}

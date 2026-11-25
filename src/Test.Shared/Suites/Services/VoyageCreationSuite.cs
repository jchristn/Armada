namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
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
    /// RELIABILITY_SIMPLICITY_PLAN.md RS-4.1: every surface creates voyages through <see cref="VoyageCreationService"/>.
    /// These cases cover what used to drift between surfaces: the voyage owner, caller scope, a bare voyage's landing
    /// mode and overrides, objective linking, and the planning source written with the voyage.
    /// </summary>
    public sealed class VoyageCreationSuite : IArmadaTestSuite
    {
        private const string SuiteId = "Services.VoyageCreation";

        /// <summary>
        /// Build the descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("voyage_belongs_to_caller", "A tenant admin dispatching on another user's vessel owns the voyage and its missions", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Scope scope = await Scope.CreateAsync(testDb.Driver).ConfigureAwait(false);
                AuthContext admin = AuthContext.Authenticated(scope.Tenant.Id, scope.Admin.Id, false, true, "Test");

                Voyage voyage = await scope.Admiral.CreateVoyageAsync(admin, scope.Request(new MissionDescription("m", "d"))).ConfigureAwait(false);

                AssertEqual(scope.Admin.Id, (await testDb.Driver.Voyages.ReadAsync(voyage.Id).ConfigureAwait(false))!.UserId, "voyage owner");
                List<Mission> missions = await testDb.Driver.Missions.EnumerateByVoyageAsync(voyage.Id).ConfigureAwait(false);
                AssertEqual(1, missions.Count);
                AssertEqual(scope.Admin.Id, missions[0].UserId, "mission owner");
                AssertEqual(scope.Tenant.Id, missions[0].TenantId, "mission tenant");
            }));

            cases.Add(CaseAsync("invisible_vessel_creates_nothing", "A regular user naming another user's vessel gets not found and nothing is created", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Scope scope = await Scope.CreateAsync(testDb.Driver).ConfigureAwait(false);
                AuthContext user = AuthContext.Authenticated(scope.Tenant.Id, scope.Admin.Id, false, false, "Test");
                int before = (await testDb.Driver.Voyages.EnumerateAsync().ConfigureAwait(false)).Count;

                await AssertThrowsAsync<KeyNotFoundException>(() => scope.Admiral.CreateVoyageAsync(user, scope.Request(new MissionDescription("m", "d")))).ConfigureAwait(false);

                AssertEqual(before, (await testDb.Driver.Voyages.EnumerateAsync().ConfigureAwait(false)).Count, "no voyage");
            }));

            cases.Add(CaseAsync("bare_voyage_keeps_settings", "A bare voyage keeps its landing mode and captain overrides from the one create write", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Scope scope = await Scope.CreateAsync(testDb.Driver).ConfigureAwait(false);
                AuthContext admin = AuthContext.Authenticated(scope.Tenant.Id, scope.Admin.Id, false, true, "Test");
                VoyageCreationRequest request = new VoyageCreationRequest
                {
                    Title = "Bare",
                    LandingMode = LandingModeEnum.PullRequest,
                    CaptainOverrides = new List<CaptainAssignmentOverride> { new CaptainAssignmentOverride("Worker", null, null) },
                    AllowBareVoyage = true
                };

                Voyage voyage = await scope.Admiral.CreateVoyageAsync(admin, request).ConfigureAwait(false);

                Voyage stored = (await testDb.Driver.Voyages.ReadAsync(voyage.Id).ConfigureAwait(false))!;
                AssertEqual(LandingModeEnum.PullRequest, stored.LandingMode, "landing mode (MCP used to drop it)");
                AssertNotNull(stored.CaptainOverridesJson, "overrides");
                AssertEqual(scope.Tenant.Id, stored.TenantId, "tenant");
                AssertEqual(scope.Admin.Id, stored.UserId, "owner");
                AssertEqual(0, (await testDb.Driver.Missions.EnumerateByVoyageAsync(voyage.Id).ConfigureAwait(false)).Count, "no missions");

                request.AllowBareVoyage = false;
                await AssertThrowsAsync<ArgumentException>(() => scope.Admiral.CreateVoyageAsync(admin, request)).ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("objective_linked_on_create", "An objective named in the request is linked with the voyage and its missions; an unknown one creates nothing", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Scope scope = await Scope.CreateAsync(testDb.Driver).ConfigureAwait(false);
                AuthContext admin = AuthContext.Authenticated(scope.Tenant.Id, scope.Admin.Id, false, true, "Test");
                Objective objective = new Objective { Title = "Goal", TenantId = scope.Tenant.Id, UserId = scope.Admin.Id };
                objective = await testDb.Driver.Objectives.CreateAsync(objective).ConfigureAwait(false);

                VoyageCreationRequest request = scope.Request(new MissionDescription("m", "d"));
                request.ObjectiveId = objective.Id;
                Voyage voyage = await scope.Admiral.CreateVoyageAsync(admin, request).ConfigureAwait(false);

                Objective linked = (await testDb.Driver.Objectives.ReadAsync(objective.Id).ConfigureAwait(false))!;
                AssertTrue(linked.VoyageIds.Contains(voyage.Id), "voyage linked");
                Mission mission = (await testDb.Driver.Missions.EnumerateByVoyageAsync(voyage.Id).ConfigureAwait(false)).Single();
                AssertTrue(linked.MissionIds.Contains(mission.Id), "mission linked (MCP used to link only the voyage)");

                int before = (await testDb.Driver.Voyages.EnumerateAsync().ConfigureAwait(false)).Count;
                request.ObjectiveId = "obj_missing";
                await AssertThrowsAsync<KeyNotFoundException>(() => scope.Admiral.CreateVoyageAsync(admin, request)).ConfigureAwait(false);
                AssertEqual(before, (await testDb.Driver.Voyages.EnumerateAsync().ConfigureAwait(false)).Count, "no voyage for an unknown objective");
            }));

            cases.Add(CaseAsync("planning_source_written_with_voyage", "The planning session link is part of the create, not a second update", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Scope scope = await Scope.CreateAsync(testDb.Driver).ConfigureAwait(false);
                VoyageCreationRequest request = scope.Request(new MissionDescription("m", "d"));
                request.SourcePlanningSessionId = "pss_source";
                request.SourcePlanningMessageId = "psm_source";

                Voyage voyage = await scope.Admiral.CreateVoyageAsync(null, request).ConfigureAwait(false);

                Voyage stored = (await testDb.Driver.Voyages.ReadAsync(voyage.Id).ConfigureAwait(false))!;
                if (testDb.Driver is Armada.Core.Database.Sqlite.SqliteDatabaseDriver)
                {
                    // Planning sessions run on SQLite only; the server providers have no planning source columns.
                    AssertEqual("pss_source", stored.SourcePlanningSessionId);
                    AssertEqual("psm_source", stored.SourcePlanningMessageId);
                }

                AssertEqual(scope.Owner.Id, stored.UserId, "Armada's own dispatch belongs to the vessel owner");
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Voyage creation: one path for every surface", cases: cases);
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        private sealed class Scope
        {
            public AdmiralService Admiral = null!;
            public TenantMetadata Tenant = null!;
            public UserMaster Owner = null!;
            public UserMaster Admin = null!;
            public Vessel Vessel = null!;

            public static async Task<Scope> CreateAsync(DatabaseDriver db)
            {
                LoggingModule logging = new LoggingModule();
                logging.Settings.EnableConsole = false;
                ArmadaSettings settings = new ArmadaSettings();
                StubGitService git = new StubGitService();
                IDockService docks = new DockService(logging, db, settings, git);
                ICaptainService captains = new CaptainService(logging, db, settings, git, docks);
                IMissionService missions = new MissionService(logging, db, settings, docks, captains);
                Scope scope = new Scope();
                scope.Admiral = new AdmiralService(logging, db, settings, captains, missions, new VoyageService(logging, db), docks);
                scope.Tenant = await db.Tenants.CreateAsync(new TenantMetadata("Voyage creation " + Guid.NewGuid().ToString("N").Substring(0, 8))).ConfigureAwait(false);
                scope.Owner = await db.Users.CreateAsync(new UserMaster(scope.Tenant.Id, "owner-" + Guid.NewGuid().ToString("N").Substring(0, 8) + "@example.com", "fixture-pass")).ConfigureAwait(false);
                scope.Admin = await db.Users.CreateAsync(new UserMaster(scope.Tenant.Id, "admin-" + Guid.NewGuid().ToString("N").Substring(0, 8) + "@example.com", "fixture-pass")).ConfigureAwait(false);
                Vessel vessel = new Vessel("creation-" + Guid.NewGuid().ToString("N").Substring(0, 8), "https://example.invalid/creation.git");
                vessel.TenantId = scope.Tenant.Id;
                vessel.UserId = scope.Owner.Id;
                scope.Vessel = await db.Vessels.CreateAsync(vessel).ConfigureAwait(false);
                return scope;
            }

            public VoyageCreationRequest Request(params MissionDescription[] missions)
            {
                return new VoyageCreationRequest { Title = "Voyage", Description = "Objective", VesselId = Vessel.Id, Missions = missions.ToList() };
            }
        }
    }
}

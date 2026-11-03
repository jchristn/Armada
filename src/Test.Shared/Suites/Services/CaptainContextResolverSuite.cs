namespace Test.Shared.Suites.Services
{
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>Host discovery never picks a representative assignment or borrows another session's dock.</summary>
    public sealed class CaptainContextResolverSuite : IArmadaTestSuite
    {
        public TestSuiteDescriptor Build()
        {
            const string suite = "Services.CaptainContextResolver";
            List<TestCaseDescriptor> cases = new();
            foreach (bool sameHost in new[] { true, false })
                cases.Add(new(suite, sameHost ? "common_host" : "ambiguous_hosts", "Captain host discovery: " + (sameHost ? "common host without a guessed worktree" : "explicit Harbor required for different hosts"), async ct =>
                {
                    using TestDatabase f = await TestDatabaseHelper.CreateDatabaseAsync(); var db = f.Driver;
                    Vessel vessel = await db.Vessels.CreateAsync(new Vessel("context", "https://example.invalid/repo"), ct);
                    Captain captain = await db.Captains.CreateAsync(new Captain("context", AgentRuntimeEnum.Mux), ct);
                    Dock first = await db.Docks.CreateAsync(new Dock(vessel.Id) { HarborId = "hbr_first", WorktreePath = "/first" }, ct);
                    Dock second = await db.Docks.CreateAsync(new Dock(vessel.Id) { HarborId = sameHost ? first.HarborId : "hbr_second", WorktreePath = "/second" }, ct);
                    Mission mission = await db.Missions.CreateAsync(new Mission("first") { VesselId = vessel.Id, CaptainId = captain.Id, DockId = first.Id, Status = MissionStatusEnum.InProgress }, ct);
                    await db.Missions.CreateAsync(new Mission("second") { VesselId = vessel.Id, CaptainId = captain.Id, DockId = second.Id, Status = MissionStatusEnum.InProgress }, ct);
                    if (sameHost)
                    {
                        Dock common = (await CaptainContextResolver.ReadDockAsync(db, captain, ct))!;
                        AssertEqual(first.HarborId, common.HarborId); AssertNull(common.WorktreePath);
                    }
                    else await AssertThrowsAsync<InvalidOperationException>(() => CaptainContextResolver.ReadDockAsync(db, captain, ct));
                    AssertEqual(first.Id, (await CaptainContextResolver.ReadDockAsync(db, captain.ForMission(mission), ct))!.Id);
                }));
            foreach (bool refinement in new[] { false, true })
                cases.Add(new(suite, refinement ? "refinement_dock" : "planning_dock", refinement ? "Refinement has no dock and cannot borrow a mission worktree" : "Planning resolves only its recorded dock and refuses a foreign owner", async ct =>
                {
                    using TestDatabase f = await TestDatabaseHelper.CreateDatabaseAsync(); var db = f.Driver;
                    Vessel vessel = await db.Vessels.CreateAsync(new Vessel("session", "https://example.invalid/repo"), ct);
                    Captain captain = await db.Captains.CreateAsync(new Captain("session"), ct);
                    Dock dock = await db.Docks.CreateAsync(new Dock(vessel.Id) { WorktreePath = "/session" }, ct);
                    if (refinement)
                    {
                        Objective objective = await db.Objectives.CreateAsync(new Objective { Title = "refine" }, ct);
                        ObjectiveRefinementSession session = await db.ObjectiveRefinementSessions.CreateAsync(new ObjectiveRefinementSession { ObjectiveId = objective.Id, CaptainId = captain.Id, VesselId = vessel.Id, Status = ObjectiveRefinementSessionStatusEnum.Responding }, ct);
                        captain.CurrentSessionId = session.Id;
                    }
                    else
                    {
                        PlanningSession session = await db.PlanningSessions.CreateAsync(new PlanningSession { CaptainId = captain.Id, VesselId = vessel.Id, DockId = dock.Id, Status = PlanningSessionStatusEnum.Responding }, ct);
                        captain.CurrentSessionId = session.Id;
                    }
                    if (refinement) AssertNull(await CaptainContextResolver.ReadDockAsync(db, captain, ct));
                    else AssertEqual(dock.Id, (await CaptainContextResolver.ReadDockAsync(db, captain, ct))!.Id);
                    captain.Id = "another-captain"; AssertNull(await CaptainContextResolver.ReadDockAsync(db, captain, ct));
                }));
            return new(suite, "Captain execution context", cases);
        }
    }
}

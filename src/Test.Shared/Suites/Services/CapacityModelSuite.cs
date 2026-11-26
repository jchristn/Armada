namespace Test.Shared.Suites.Services
{
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>One capacity model for the Admiral's dispatch check and mission admission (RS-4.8).</summary>
    public sealed class CapacityModelSuite : IArmadaTestSuite
    {
        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            const string suite = "Services.CapacityModel";
            List<TestCaseDescriptor> cases = new();
            void Add(string id, Func<Task> test) => cases.Add(new TestCaseDescriptor(suite, id, id, _ => test()));

            Add("the_dispatch_check_counts_the_global_limit_like_admission", async () =>
            {
                using Harness h = await Harness.CreateAsync(); h.Settings.MaxConcurrentMissions = 1;
                await h.Db.Captains.CreateAsync(new Captain("idle"));
                AssertTrue((await h.Service.ReadDispatchCapacityAsync()).AnyCaptainHasRoom, "an idle captain and an empty slot");
                // An Assigned mission holds a slot even when no captain is Working (dock provisioning, a released captain).
                Mission held = await h.Db.Missions.CreateAsync(new Mission("held"));
                held.Status = MissionStatusEnum.Assigned; await h.Db.Missions.UpdateAsync(held);
                AssertTrue((await h.Service.ReadDispatchCapacityAsync()).GlobalLimitReached, "the Admiral sees the slot admission counts");
                Mission next = await h.Db.Missions.CreateAsync(new Mission("next") { VesselId = h.Vessel.Id });
                AssertFalse(await h.Service.TryAssignAsync(next, h.Vessel));
                AssertEqual(MissionAssignmentBlockerReasonEnum.GlobalMissionLimit, h.Service.GetAdmissionResult(next.Id)!.Blocker.Reason);
            });

            Add("every_captain_at_its_runtime_cap_leaves_no_dispatch_capacity", async () =>
            {
                using Harness h = await Harness.CreateAsync();
                h.Settings.Agents = new() { new AgentSettings { Runtime = AgentRuntimeEnum.Mux, MaxConcurrent = 1 } };
                Captain busy = await h.Db.Captains.CreateAsync(new Captain("busy", AgentRuntimeEnum.Mux)); await h.Occupy(busy);
                await h.Db.Captains.CreateAsync(new Captain("idle same runtime", AgentRuntimeEnum.Mux));
                AssertFalse((await h.Service.ReadDispatchCapacityAsync()).AnyCaptainHasRoom, "the only idle captain's runtime is full");
                await h.Db.Captains.CreateAsync(new Captain("other runtime", AgentRuntimeEnum.Codex));
                AssertTrue((await h.Service.ReadDispatchCapacityAsync()).AnyCaptainHasRoom, "a captain on another runtime has room");
            });

            Add("captains_sharing_an_endpoint_share_its_cap", async () =>
            {
                using Harness h = await Harness.CreateAsync();
                // Two endpoint records for one server count against one budget.
                ModelEndpoint first = await h.Endpoint("first", "http://127.0.0.1:9/v1", 1);
                ModelEndpoint second = await h.Endpoint("second", "HTTP://127.0.0.1:9/v1/", null);
                Captain busy = await h.Db.Captains.CreateAsync(new Captain("busy", AgentRuntimeEnum.Mux) { ModelEndpointId = second.Id, MaxConcurrency = 1 }); await h.Occupy(busy);
                Captain idle = await h.Db.Captains.CreateAsync(new Captain("idle", AgentRuntimeEnum.Mux) { ModelEndpointId = first.Id, MaxConcurrency = 1 });
                AssertFalse((await h.Service.ReadDispatchCapacityAsync()).AnyCaptainHasRoom, "the shared server is at its cap of one");
                Mission next = await h.Db.Missions.CreateAsync(new Mission("next") { VesselId = h.Vessel.Id });
                AssertFalse(await h.Service.TryAssignAsync(next, h.Vessel));
                AssertEqual(MissionAssignmentBlockerReasonEnum.EndpointMissionLimit, h.Service.GetAdmissionResult(next.Id)!.Blocker.Reason, "admission names the same cap");
                await h.Db.Captains.CreateAsync(new Captain("no endpoint", AgentRuntimeEnum.Codex));
                AssertTrue((await h.Service.ReadDispatchCapacityAsync()).AnyCaptainHasRoom, "a captain on no endpoint has room");
                AssertEqual(idle.ModelEndpointId, first.Id);
            });

            Add("a_full_global_limit_skips_the_mission_before_the_shared_lock", async () =>
            {
                using Harness h = await Harness.CreateAsync(); h.Settings.MaxConcurrentMissions = 1;
                await h.Db.Captains.CreateAsync(new Captain("idle"));
                Mission held = await h.Db.Missions.CreateAsync(new Mission("held"));
                held.Status = MissionStatusEnum.InProgress; await h.Db.Missions.UpdateAsync(held);
                Mission next = await h.Db.Missions.CreateAsync(new Mission("next") { VesselId = h.Vessel.Id });
                using (h.Service.BeginDispatchPass())
                {
                    AssertFalse(await h.Service.TryAssignAsync(next, h.Vessel));
                }
                MissionAdmissionResult result = h.Service.GetAdmissionResult(next.Id)!;
                AssertEqual(MissionAssignmentBlockerReasonEnum.GlobalMissionLimit, result.Blocker.Reason);
                AssertContains("1", result.Blocker.Summary);
            });

            Add("a_claim_outside_the_pass_is_counted_under_the_lock", async () =>
            {
                using Harness h = await Harness.CreateAsync(launching: true); h.Settings.MaxConcurrentMissions = 1;
                await h.Db.Captains.CreateAsync(new Captain("one")); await h.Db.Captains.CreateAsync(new Captain("two"));
                Vessel other = await h.Db.Vessels.CreateAsync(new Vessel("other", "https://example.invalid/other"));
                Mission outside = await h.Db.Missions.CreateAsync(new Mission("outside") { VesselId = other.Id, Mode = MissionModeEnum.Research });
                Mission inside = await h.Db.Missions.CreateAsync(new Mission("inside") { VesselId = h.Vessel.Id, Mode = MissionModeEnum.Research });
                using (h.Service.BeginDispatchPass())
                {
                    AssertTrue((await h.Service.ReadDispatchCapacityAsync()).AnyCaptainHasRoom, "the pass reads an empty slot");
                    // A caller outside the pass (voyage creation, an API dispatch) takes the only slot.
                    Task<bool> claim;
                    using (ExecutionContext.SuppressFlow()) claim = Task.Run(() => h.Service.TryAssignAsync(outside, other));
                    AssertTrue(await claim, "the outside caller takes the slot");
                    AssertFalse(await h.Service.TryAssignAsync(inside, h.Vessel), "the pass re-reads the slot count under the lock");
                }
                AssertEqual(MissionAssignmentBlockerReasonEnum.GlobalMissionLimit, h.Service.GetAdmissionResult(inside.Id)!.Blocker.Reason);
                AssertEqual(MissionStatusEnum.Pending, (await h.Db.Missions.ReadAsync(inside.Id))!.Status);
            });

            return new TestSuiteDescriptor(suite, "One capacity model", cases);
        }

        private sealed class Harness : IDisposable
        {
            internal TestDatabase Fixture = null!;
            internal Armada.Core.Database.DatabaseDriver Db = null!;
            internal ArmadaSettings Settings = null!;
            internal MissionService Service = null!;
            internal Vessel Vessel = null!;

            internal static async Task<Harness> CreateAsync(bool launching = false)
            {
                Harness h = new Harness();
                h.Fixture = await TestDatabaseHelper.CreateDatabaseAsync(); h.Db = h.Fixture.Driver;
                LoggingModule log = new(); log.Settings.EnableConsole = false;
                h.Settings = new ArmadaSettings { DocksDirectory = TestTemp.NewDirectory("capacity_docks"), ReposDirectory = TestTemp.NewDirectory("capacity_repos") };
                DirCreatingGitService git = new(); DockService docks = new(log, h.Db, h.Settings, git); CaptainService captains = new(log, h.Db, h.Settings, git, docks);
                h.Service = new MissionService(log, h.Db, h.Settings, docks, captains, git: git);
                if (launching) captains.OnLaunchAgent = (_, _, _) => Task.FromResult(8001);
                h.Vessel = await h.Db.Vessels.CreateAsync(new Vessel("capacity", "https://example.invalid/capacity"));
                return h;
            }

            internal async Task Occupy(Captain captain)
            {
                Mission held = await Db.Missions.CreateAsync(new Mission("held"));
                AssertTrue(await Db.Captains.TryClaimAsync(captain.Id, held.Id, null));
            }

            internal Task<ModelEndpoint> Endpoint(string name, string baseUrl, int? cap)
                => Db.ModelEndpoints.CreateAsync(new ModelEndpoint { Name = name, BaseUrl = baseUrl,
                    InferenceOptions = cap.HasValue ? new InferenceOptions { MaxConcurrentMissions = cap } : null });

            public void Dispose()
            {
                TestTemp.TryDelete(Settings.DocksDirectory); TestTemp.TryDelete(Settings.ReposDirectory);
                Fixture.Dispose();
            }
        }
    }
}

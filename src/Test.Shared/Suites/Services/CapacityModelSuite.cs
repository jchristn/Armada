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

            // One captain with room for six, so the claim write never refuses and only the shared caps can stop a pass.
            Add("parallel_passes_never_admit_past_the_global_limit", async () =>
            {
                using Harness h = await Harness.CreateAsync(launching: true); h.Settings.MaxConcurrentMissions = 2;
                await h.Db.Captains.CreateAsync(new Captain("wide") { MaxConcurrency = 6 });
                List<Mission> missions = await h.MissionsOnOwnVessels(6);
                bool[] admitted = await h.RaceAsync(missions);
                AssertEqual(2, admitted.Count(a => a), "every pass chose the captain from an empty count, and only two claims fit");
                AssertEqual(2, await h.Db.Missions.CountExecutionSlotsAsync());
                AssertEqual(4, missions.Count(m => h.Service.GetAdmissionResult(m.Id)?.Blocker.Reason == MissionAssignmentBlockerReasonEnum.GlobalMissionLimit));
            });

            Add("parallel_passes_never_admit_past_a_runtime_or_endpoint_cap", async () =>
            {
                using Harness h = await Harness.CreateAsync(launching: true);
                h.Settings.Agents = new() { new AgentSettings { Runtime = AgentRuntimeEnum.Mux, MaxConcurrent = 1 } };
                await h.Db.Captains.CreateAsync(new Captain("mux", AgentRuntimeEnum.Mux) { MaxConcurrency = 6 });
                List<Mission> runtimeRace = await h.MissionsOnOwnVessels(3);
                AssertEqual(1, (await h.RaceAsync(runtimeRace)).Count(a => a), "the Mux runtime cap of one held");
                AssertEqual(2, runtimeRace.Count(m => h.Service.GetAdmissionResult(m.Id)?.Blocker.Reason == MissionAssignmentBlockerReasonEnum.RuntimeMissionLimit));

                ModelEndpoint shared = await h.Endpoint("shared", "http://127.0.0.1:9/v1", 1);
                await h.Db.Captains.CreateAsync(new Captain("endpoint", AgentRuntimeEnum.Codex) { ModelEndpointId = shared.Id, MaxConcurrency = 6 });
                List<Mission> endpointRace = await h.MissionsOnOwnVessels(3);
                AssertEqual(1, (await h.RaceAsync(endpointRace)).Count(a => a), "the endpoint cap of one held");
                AssertEqual(2, endpointRace.Count(m => h.Service.GetAdmissionResult(m.Id)?.Blocker.Reason == MissionAssignmentBlockerReasonEnum.EndpointMissionLimit));
            });

            Add("a_slow_placement_does_not_hold_the_capacity_lock", async () =>
            {
                using Harness h = await Harness.CreateAsync(launching: true);
                await h.Db.Captains.CreateAsync(new Captain("wide") { MaxConcurrency = 2 });
                List<Mission> missions = await h.MissionsOnOwnVessels(2);
                TaskCompletionSource<bool> placing = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                TaskCompletionSource<bool> release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                h.Service.ResolveDockPlacementAsync = async (mission, _, _) =>
                {
                    if (mission.Id == missions[0].Id) { placing.TrySetResult(true); await release.Task; }
                    return DockPlacement.OnAdmiral("test");
                };
                Task<bool> slow = h.AssignInOwnPassAsync(missions[0]);
                await placing.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Task<bool> fast = h.AssignInOwnPassAsync(missions[1]);
                Task first = await Task.WhenAny(fast, Task.Delay(TimeSpan.FromSeconds(10)));
                release.TrySetResult(true);
                AssertTrue(first == fast, "another vessel is admitted while the first one's placement is still running");
                AssertTrue(await fast, "the other vessel's mission is admitted");
                AssertTrue(await slow, "the slow mission is admitted once its placement returns");
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
            private readonly Dictionary<string, Vessel> _Vessels = new Dictionary<string, Vessel>();

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

            internal async Task<List<Mission>> MissionsOnOwnVessels(int count)
            {
                List<Mission> missions = new List<Mission>();
                for (int i = 0; i < count; i++)
                {
                    string name = "race" + _Vessels.Count;
                    Vessel vessel = await Db.Vessels.CreateAsync(new Vessel(name, "https://example.invalid/" + name));
                    _Vessels[vessel.Id] = vessel;
                    missions.Add(await Db.Missions.CreateAsync(new Mission(name) { VesselId = vessel.Id, Mode = MissionModeEnum.Research }));
                }
                return missions;
            }

            // Each mission runs in its own dispatch pass. Every pass reads capacity, then waits at placement until all
            // have chosen a captain, so every claim races the others with a count read before any of them landed.
            internal async Task<bool[]> RaceAsync(List<Mission> missions)
            {
                int arrived = 0;
                TaskCompletionSource<bool> allChose = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                Service.ResolveDockPlacementAsync = async (_, _, _) =>
                {
                    if (Interlocked.Increment(ref arrived) == missions.Count) allChose.TrySetResult(true);
                    await allChose.Task.WaitAsync(TimeSpan.FromSeconds(10));
                    return DockPlacement.OnAdmiral("test");
                };
                return await Task.WhenAll(missions.Select(m => AssignInOwnPassAsync(m)));
            }

            internal Task<bool> AssignInOwnPassAsync(Mission mission)
            {
                using (ExecutionContext.SuppressFlow())
                    return Task.Run(async () =>
                    {
                        using IDisposable pass = Service.BeginDispatchPass();
                        await Service.ReadDispatchCapacityAsync();
                        return await Service.TryAssignAsync(mission, _Vessels[mission.VesselId!]);
                    });
            }

            internal Task<ModelEndpoint> Endpoint(string name, string baseUrl, int? cap)
                => Db.ModelEndpoints.CreateAsync(new ModelEndpoint { Name = name, BaseUrl = baseUrl,
                    InferenceOptions = cap.HasValue ? new InferenceOptions { MaxConcurrentMissions = cap } : new InferenceOptions() });

            public void Dispose()
            {
                TestTemp.TryDelete(Settings.DocksDirectory); TestTemp.TryDelete(Settings.ReposDirectory);
                Fixture.Dispose();
            }
        }
    }
}

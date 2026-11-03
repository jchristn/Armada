namespace Test.Shared.Suites.Services
{
    using System.Diagnostics.Metrics;
    using System.Text.Json;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>Deployment-wide capacity accounting matches admission without leaking aggregate usage.</summary>
    public sealed class OuterGateInvariantSuite : IArmadaTestSuite
    {
        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            const string suite = "Services.OuterGateInvariant";
            List<TestCaseDescriptor> cases = new();
            void Add(string id, Func<Task> test) => cases.Add(new TestCaseDescriptor(suite, id, id, _ => test()));
            foreach (string gate in new[] { "global", "runtime", "endpoint" })
                Add(gate + "_lowering_drains_and_is_admin_only", async () =>
                {
                    using TestDatabase fixture = await TestDatabaseHelper.CreateDatabaseAsync();
                    var db = fixture.Driver;
                    ArmadaSettings settings = new();
                    if (gate == "global") settings.MaxConcurrentMissions = 4;
                    if (gate == "runtime") settings.Agents = new() { new AgentSettings { Runtime = AgentRuntimeEnum.ApiEndpoint, MaxConcurrent = 4 } };
                    ModelEndpoint endpoint = await db.ModelEndpoints.CreateAsync(new ModelEndpoint { Name = "capacity", BaseUrl = "http://capacity.invalid/v1", InferenceOptions = new InferenceOptions { MaxConcurrentMissions = 4 } });
                    Captain captain = await db.Captains.CreateAsync(new Captain("three slots", AgentRuntimeEnum.ApiEndpoint) { MaxConcurrency = 4, ModelEndpointId = endpoint.Id });
                    for (int i = 0; i < 3; i++) { Mission m = await db.Missions.CreateAsync(new Mission("work")); AssertTrue(await db.Captains.TryClaimAsync(captain.Id, m.Id, null)); }
                    ConcurrencyInvariantMonitor monitor = Monitor(db, settings); await monitor.CheckAsync();
                    AssertEqual(0, (await Events(db)).Count);
                    if (gate == "global") settings.MaxConcurrentMissions = 1;
                    if (gate == "runtime") settings.Agents[0].MaxConcurrent = 1;
                    if (gate == "endpoint") { endpoint.InferenceOptions!.MaxConcurrentMissions = 1; await db.ModelEndpoints.UpdateAsync(endpoint); }
                    await monitor.CheckAsync(); await monitor.CheckAsync();
                    ArmadaEvent evt = (await Events(db)).Single();
                    using JsonDocument payload = JsonDocument.Parse(evt.Payload!);
                    AssertEqual(gate, payload.RootElement.GetProperty("Gate").GetString());
                    AssertEqual(3, payload.RootElement.GetProperty("Count").GetInt32());
                    AssertEqual("limit_lowered", payload.RootElement.GetProperty("Cause").GetString());
                    AssertNull(evt.TenantId); AssertNull(evt.UserId);
                    AssertNull(await db.Events.ReadAsync(Constants.DefaultTenantId, Constants.DefaultUserId, evt.Id));
                    AssertEqual(3, (await db.Captains.ReadAsync(captain.Id))!.ActiveMissionCount);
                });
            Add("runtime_duplicate_settings_use_strictest_positive_limit", async () =>
            {
                using TestDatabase fixture = await TestDatabaseHelper.CreateDatabaseAsync();
                var db = fixture.Driver;
                ArmadaSettings settings = new() { Agents = new() { new AgentSettings { Runtime = AgentRuntimeEnum.Mux, MaxConcurrent = 4 }, new AgentSettings { Runtime = AgentRuntimeEnum.Mux, MaxConcurrent = 0 }, new AgentSettings { Runtime = AgentRuntimeEnum.Mux, MaxConcurrent = 1 } } };
                Captain captain = await db.Captains.CreateAsync(new Captain("mux", AgentRuntimeEnum.Mux) { MaxConcurrency = 4 });
                for (int i = 0; i < 2; i++) { Mission m = await db.Missions.CreateAsync(new Mission("work")); AssertTrue(await db.Captains.TryClaimAsync(captain.Id, m.Id, null)); }
                await Monitor(db, settings).CheckAsync();
                using JsonDocument payload = JsonDocument.Parse((await Events(db)).Single().Payload!);
                AssertEqual(1, payload.RootElement.GetProperty("Limit").GetInt32());
                AssertEqual(2, payload.RootElement.GetProperty("Count").GetInt32());
            });
            Add("endpoint_aliases_share_normalized_usage_and_mixed_limits_stay_unknown", async () =>
            {
                using TestDatabase fixture = await TestDatabaseHelper.CreateDatabaseAsync();
                var db = fixture.Driver;
                ModelEndpoint low = await db.ModelEndpoints.CreateAsync(new ModelEndpoint { Name = "lower", BaseUrl = "http://CAPACITY.invalid/v1/", InferenceOptions = new InferenceOptions { MaxConcurrentMissions = 1 } });
                ModelEndpoint high = await db.ModelEndpoints.CreateAsync(new ModelEndpoint { Name = "higher", BaseUrl = "http://capacity.invalid/v1", InferenceOptions = new InferenceOptions { MaxConcurrentMissions = 8 } });
                ConcurrencyInvariantMonitor monitor = Monitor(db, new()); await monitor.CheckAsync();
                foreach (ModelEndpoint endpoint in new[] { low, high })
                {
                    Captain captain = await db.Captains.CreateAsync(new Captain(endpoint.Name, AgentRuntimeEnum.ApiEndpoint) { ModelEndpointId = endpoint.Id });
                    Mission mission = await db.Missions.CreateAsync(new Mission("work")); AssertTrue(await db.Captains.TryClaimAsync(captain.Id, mission.Id, null));
                }
                await monitor.CheckAsync();
                ArmadaEvent evt = (await Events(db)).Single();
                using JsonDocument payload = JsonDocument.Parse(evt.Payload!);
                AssertEqual(2, payload.RootElement.GetProperty("Count").GetInt32());
                AssertEqual("unknown", payload.RootElement.GetProperty("Cause").GetString());
                AssertContains("aliases", evt.Message); AssertEqual(low.Id, evt.EntityId);
            });
            Add("verification_is_not_outer_execution_occupancy", async () =>
            {
                using TestDatabase fixture = await TestDatabaseHelper.CreateDatabaseAsync();
                var db = fixture.Driver;
                ArmadaSettings settings = new() { MaxConcurrentMissions = 1, Agents = new() { new AgentSettings { Runtime = AgentRuntimeEnum.Mux, MaxConcurrent = 1 } } };
                Captain captain = await db.Captains.CreateAsync(new Captain("checks", AgentRuntimeEnum.Mux));
                for (int i = 0; i < 3; i++) await db.Missions.CreateAsync(new Mission("verify") { CaptainId = captain.Id, Status = MissionStatusEnum.Verifying });
                await db.Captains.ReconcileMissionSlotsAsync(); await Monitor(db, settings).CheckAsync();
                AssertEqual(0, (await Events(db)).Count);
            });
            Add("execution_count_uses_statuses_and_ignores_artifact_size", async () =>
            {
                using TestDatabase fixture = await TestDatabaseHelper.CreateDatabaseAsync(); var db = fixture.Driver;
                int expected = 0;
                foreach (MissionStatusEnum status in Enum.GetValues<MissionStatusEnum>())
                {
                    await db.Missions.CreateAsync(new Mission("state") { Status = status, DiffSnapshot = new string('x', 100000) });
                    if (status == MissionStatusEnum.Assigned || status == MissionStatusEnum.InProgress) expected++;
                }
                AssertEqual(expected, await db.Missions.CountExecutionSlotsAsync());
            });
            Add("outer_metric_labels_never_contain_runtime_endpoint_or_entity_ids", async () =>
            {
                using TestDatabase fixture = await TestDatabaseHelper.CreateDatabaseAsync();
                var db = fixture.Driver;
                List<string> gates = new();
                using MeterListener listener = new();
                listener.InstrumentPublished = (instrument, current) => { if (instrument.Name == "armada.concurrency.limit_violations") current.EnableMeasurementEvents(instrument); };
                listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
                {
                    foreach (var tag in tags) { AssertTrue(tag.Key == "gate" || tag.Key == "cause"); if (tag.Key == "gate") gates.Add((string)tag.Value!); }
                }); listener.Start();
                ArmadaSettings settings = new() { MaxConcurrentMissions = 1 };
                for (int i = 0; i < 2; i++) await db.Missions.CreateAsync(new Mission("active") { Status = MissionStatusEnum.InProgress });
                await Monitor(db, settings).CheckAsync(); AssertEqual("global", gates.Single());
            });
            Add("runtime_unlimited_to_finite_is_an_observed_reduction", async () =>
            {
                using TestDatabase fixture = await TestDatabaseHelper.CreateDatabaseAsync(); var db = fixture.Driver;
                ArmadaSettings settings = new();
                Captain captain = await db.Captains.CreateAsync(new Captain("unlimited", AgentRuntimeEnum.Mux) { MaxConcurrency = 4 });
                for (int i = 0; i < 3; i++) { Mission mission = await db.Missions.CreateAsync(new Mission("work")); AssertTrue(await db.Captains.TryClaimAsync(captain.Id, mission.Id, null)); }
                ConcurrencyInvariantMonitor monitor = Monitor(db, settings); await monitor.CheckAsync();
                settings.Agents = new() { new AgentSettings { Runtime = AgentRuntimeEnum.Mux, MaxConcurrent = 1 } };
                await monitor.CheckAsync();
                using JsonDocument payload = JsonDocument.Parse((await Events(db)).Single().Payload!);
                AssertEqual("limit_lowered", payload.RootElement.GetProperty("Cause").GetString());
            });
            return new TestSuiteDescriptor(suite, "Outer capacity gate invariants", cases);
        }
        private static ConcurrencyInvariantMonitor Monitor(DatabaseDriver db, ArmadaSettings settings)
        { LoggingModule log = new(); log.Settings.EnableConsole = false; return new(db, settings, log); }
        private static Task<List<ArmadaEvent>> Events(DatabaseDriver db) => db.Events.EnumerateByTypeAsync("concurrency.limit_exceeded");
    }
}

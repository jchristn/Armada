namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>Boundary, normalization and persistence contracts for the first v1.3 concurrency increment.</summary>
    public sealed class ConcurrencySettingsSuite : IArmadaTestSuite
    {
        /// <summary>Build the deterministic, model-free suite.</summary>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            void Add(string id, string name, Action test) => cases.Add(new TestCaseDescriptor("Services.ConcurrencySettings", id, name,
                ct => { test(); return Task.CompletedTask; }, tags: new List<string> { TestTags.Positive }));

            Add("vessel_limits_persist_bounds", "Vessel model uses the shared bounds", () =>
            {
                foreach (int value in new[] { int.MinValue, -1, 0, 1, 1000, int.MaxValue })
                {
                    Vessel vessel = new Vessel { MaxParallelVoyages = value, MaxParallelMissionsPerVoyage = value };
                    AssertEqual(ConcurrencyClamp.VesselLimit(value), vessel.MaxParallelVoyages);
                    AssertEqual(ConcurrencyClamp.VesselLimit(value), vessel.MaxParallelMissionsPerVoyage);
                }
            });

            Add("voyage_capacity_status_matrix", "Review and landing keep admission; settled and blocked work release it", () =>
            {
                Vessel vessel = new Vessel { MaxParallelVoyages = 1, MaxParallelMissionsPerVoyage = 1 };
                Mission candidate = new Mission { Id = "candidate", VoyageId = "new" };
                foreach (MissionStatusEnum status in Enum.GetValues<MissionStatusEnum>())
                {
                    Mission existing = new Mission { Id = "existing", VoyageId = "old", Status = status };
                    bool holds = status == MissionStatusEnum.Assigned || status == MissionStatusEnum.InProgress
                        || status == MissionStatusEnum.WorkProduced || status == MissionStatusEnum.PullRequestOpen
                        || status == MissionStatusEnum.Testing || status == MissionStatusEnum.Review || status == MissionStatusEnum.Verifying;
                    MissionAssignmentBlocker? blocker = VesselAdmission.Evaluate(candidate, vessel, new[] { candidate, existing }, Array.Empty<Voyage>());
                    AssertEqual(holds, blocker != null, status.ToString());
                }
                Mission completed = new Mission { Id = "completed", VoyageId = "old", Status = MissionStatusEnum.Complete };
                Mission next = new Mission { Id = "next", VoyageId = "old" };
                AssertNotNull(VesselAdmission.Evaluate(candidate, vessel, new[] { completed, next, candidate }, Array.Empty<Voyage>()), "Pending next stage retains an admitted voyage");
                AssertEqual<MissionAssignmentBlocker?>(null, VesselAdmission.Evaluate(next, vessel, new[] { completed, next }, Array.Empty<Voyage>()), "Admitted voyage's last pending stage can run");
                vessel.MaxParallelVoyages = 0;
                vessel.MaxParallelMissionsPerVoyage = 0;
                AssertEqual<MissionAssignmentBlocker?>(null, VesselAdmission.Evaluate(candidate, vessel, new[] { completed, next, candidate }, Array.Empty<Voyage>()));
            });

            Add("unlimited_is_explicit", "Negative vessel/global limits become one; only zero means unlimited", () =>
            {
                foreach (int value in new[] { int.MinValue, -1, 0, 1, 1000, 1001, int.MaxValue })
                {
                    int expected = value < 0 ? 1 : Math.Min(value, 1000);
                    AssertEqual(expected, ConcurrencyClamp.VesselLimit(value));
                    ConcurrencySettings settings = new ConcurrencySettings
                    {
                        DefaultVesselMaxParallelVoyages = value,
                        DefaultVesselMaxParallelMissionsPerVoyage = value
                    };
                    AssertEqual(expected, settings.DefaultVesselMaxParallelVoyages);
                    AssertEqual(expected, settings.DefaultVesselMaxParallelMissionsPerVoyage);
                }
                foreach (int value in new[] { int.MinValue, -1, 0, 1, 10000, 10001, int.MaxValue })
                {
                    int expected = value < 0 ? 1 : Math.Min(value, 10000);
                    ArmadaSettings settings = new ArmadaSettings { MaxConcurrentMissions = value };
                    AssertEqual(expected, settings.MaxConcurrentMissions);
                    AssertEqual(expected, ConcurrencyClamp.GlobalMissionLimit(value));
                }
            });

            Add("finite_bounds", "Every finite concurrency setting clamps at both boundaries", () =>
            {
                ConcurrencySettings settings = new ConcurrencySettings();
                var fields = new (Action<int> Set, Func<int> Get, int Max)[]
                {
                    (v => settings.MaxCaptainConcurrency = v, () => settings.MaxCaptainConcurrency, 256),
                    (v => settings.DefaultCliCaptainMaxConcurrency = v, () => settings.DefaultCliCaptainMaxConcurrency, 256),
                    (v => settings.DefaultEndpointCaptainMaxConcurrency = v, () => settings.DefaultEndpointCaptainMaxConcurrency, 256),
                    (v => settings.MaxHierarchyDepth = v, () => settings.MaxHierarchyDepth, 16),
                    (v => settings.StarvationWarningMinutes = v, () => settings.StarvationWarningMinutes, 10080),
                    (v => settings.AdmissionLockTimeoutSeconds = v, () => settings.AdmissionLockTimeoutSeconds, 600),
                    (v => settings.WaitIntervalRetentionDays = v, () => settings.WaitIntervalRetentionDays, 3650)
                };
                foreach (var field in fields)
                    foreach (int value in new[] { int.MinValue, -1, 0, 1, field.Max, field.Max + 1, int.MaxValue })
                    {
                        field.Set(value);
                        AssertEqual(Math.Clamp(value, 1, field.Max), field.Get());
                    }
                AssertEqual(1, ConcurrencyClamp.EndpointMissionLimit(int.MinValue));
                AssertEqual(64, ConcurrencyClamp.EndpointMissionLimit(int.MaxValue));
                AssertEqual(2, ConcurrencyClamp.CaptainLimit(256, 2));
                AssertEqual(1, ConcurrencyClamp.CaptainLimit(256, 0));
            });

            Add("runtime_defaults", "ApiEndpoint and Mux default to four; all CLI runtimes including pi default to one", () =>
            {
                foreach (AgentRuntimeEnum runtime in Enum.GetValues<AgentRuntimeEnum>())
                    AssertEqual(runtime == AgentRuntimeEnum.ApiEndpoint || runtime == AgentRuntimeEnum.Mux ? 4 : 1, CaptainConcurrencyDefaults.For(runtime), runtime.ToString());
                ConcurrencySettings settings = new ConcurrencySettings { MaxCaptainConcurrency = 2, DefaultCliCaptainMaxConcurrency = 3 };
                AssertEqual(2, CaptainConcurrencyDefaults.For(AgentRuntimeEnum.Mux, settings));
                AssertEqual(2, CaptainConcurrencyDefaults.For(AgentRuntimeEnum.Pi, settings));
                AssertEqual(1, CaptainConcurrencyDefaults.For((AgentRuntimeEnum)int.MaxValue));
            });

            Add("json_order", "File parsing normalizes cross-field defaults after binding in either property order", () =>
            {
                foreach (string group in new[]
                {
                    "\"MaxCaptainConcurrency\":2,\"DefaultEndpointCaptainMaxConcurrency\":4,\"DefaultCliCaptainMaxConcurrency\":5",
                    "\"DefaultCliCaptainMaxConcurrency\":5,\"DefaultEndpointCaptainMaxConcurrency\":4,\"MaxCaptainConcurrency\":2"
                })
                {
                    ArmadaSettings settings = ArmadaSettings.Parse("{\"Concurrency\":{" + group + "}}");
                    AssertEqual(2, settings.Concurrency.DefaultCliCaptainMaxConcurrency);
                    AssertEqual(2, settings.Concurrency.DefaultEndpointCaptainMaxConcurrency);
                }
                AssertEqual(4, ArmadaSettings.Parse("{\"Concurrency\":null}").Concurrency.DefaultEndpointCaptainMaxConcurrency);
                AssertEqual(1, ArmadaSettings.Parse("{\"MaxConcurrentMissions\":-1}").MaxConcurrentMissions);
            });

            Add("adjustments", "Normalization reports original/effective values once and does not raise defaults later", () =>
            {
                ConcurrencySettings settings = new ConcurrencySettings { MaxCaptainConcurrency = 2, DefaultVesselMaxParallelVoyages = -5 };
                IReadOnlyList<string> warnings = settings.Normalize();
                AssertTrue(warnings.Any(w => w.Contains("DefaultVesselMaxParallelVoyages clamped from -5 to 1", StringComparison.Ordinal)));
                AssertTrue(warnings.Any(w => w.Contains("DefaultEndpointCaptainMaxConcurrency clamped from 4 to 2", StringComparison.Ordinal)));
                AssertEqual(0, settings.Normalize().Count);
                settings.MaxCaptainConcurrency = 256;
                settings.Normalize();
                AssertEqual(2, settings.DefaultEndpointCaptainMaxConcurrency);
                ArmadaSettings root = new ArmadaSettings { MaxConcurrentMissions = int.MaxValue };
                List<string> rootWarnings = new List<string>();
                root.NormalizeConcurrency(rootWarnings.Add);
                AssertTrue(rootWarnings.Any(w => w.Contains("2147483647 to 10000", StringComparison.Ordinal)));
            });

            cases.Add(new TestCaseDescriptor("Services.ConcurrencySettings", "file_round_trip", "Save/load preserves normalized settings and does not mutate unrelated settings", async ct =>
            {
                string dir = TestTemp.NewDirectory("concurrency_settings");
                string path = Path.Combine(dir, "settings.json");
                try
                {
                    ArmadaSettings settings = new ArmadaSettings { DataDirectory = dir, MaxConcurrentMissions = 7 };
                    settings.Concurrency.MaxCaptainConcurrency = 2;
                    settings.Concurrency.DefaultVesselMaxParallelVoyages = 0;
                    settings.UsageCapture.MinimumIntervalSeconds = 120;
                    await settings.SaveAsync(path).ConfigureAwait(false);
                    ArmadaSettings loaded = await ArmadaSettings.LoadAsync(path).ConfigureAwait(false);
                    AssertEqual(2, loaded.Concurrency.DefaultEndpointCaptainMaxConcurrency);
                    AssertEqual(0, loaded.Concurrency.DefaultVesselMaxParallelVoyages);
                    AssertEqual(7, loaded.MaxConcurrentMissions);
                    AssertEqual(120, loaded.UsageCapture.MinimumIntervalSeconds);
                    loaded.WriteBack(path);
                    AssertEqual(2, ArmadaSettings.Parse(await File.ReadAllTextAsync(path, ct).ConfigureAwait(false)).Concurrency.MaxCaptainConcurrency);
                }
                finally { Directory.Delete(dir, recursive: true); }
            }, tags: new List<string> { TestTags.Positive }));
            return new TestSuiteDescriptor("Services.ConcurrencySettings", "Concurrency configuration foundation", cases);
        }
    }
}

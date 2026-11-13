namespace Test.Shared.Suites.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="ArmadaStatus"/> and <see cref="VoyageProgress"/>: default
    /// values, null-setter reset (defensive) behavior, collection population, and serialization.
    /// </summary>
    public sealed class ArmadaStatusSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the ArmadaStatus model suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("constructor_default_values", "ArmadaStatus constructor default values", TestTags.Positive, () =>
            {
                ArmadaStatus status = new ArmadaStatus();
                AssertEqual(0, status.TotalCaptains);
                AssertEqual(0, status.IdleCaptains);
                AssertEqual(0, status.WorkingCaptains);
                AssertEqual(0, status.StalledCaptains);
                AssertEqual(0, status.ActiveVoyages);
                AssertNotNull(status.MissionsByStatus);
                AssertEqual(0, status.MissionsByStatus.Count);
                AssertEqual(0, status.InProgressVoyages);
                AssertEqual(0, status.OpenVoyages);
                AssertNotNull(status.RecentSignals);
                AssertEqual(0, status.RecentSignals.Count);
                AssertTrue(status.TimestampUtc <= DateTime.UtcNow);
                AssertNotNull(status.RemoteTunnel);
                AssertEqual(RemoteTunnelStateEnum.Disabled, status.RemoteTunnel.State);
            }));

            cases.Add(Case("missionsbystatus_null_setter_resets_to_empty", "ArmadaStatus MissionsByStatus null setter resets to empty", TestTags.Negative, () =>
            {
                ArmadaStatus status = new ArmadaStatus();
                status.MissionsByStatus = null!;
                AssertNotNull(status.MissionsByStatus);
                AssertEqual(0, status.MissionsByStatus.Count);
            }));

            cases.Add(Case("status_has_no_voyage_list", "ArmadaStatus carries voyage counts, never a voyage list", TestTags.Positive, () =>
            {
                AssertNull(typeof(ArmadaStatus).GetProperty("Voyages"), "the active voyages are paged by POST /api/v1/voyages/progress/enumerate");
                string json = System.Text.Json.JsonSerializer.Serialize(new ArmadaStatus { ActiveVoyages = 3, InProgressVoyages = 2, OpenVoyages = 1 });
                AssertFalse(json.Contains("\"Voyages\"", StringComparison.Ordinal), "no Voyages array is serialized: " + json);
                AssertTrue(json.Contains("\"InProgressVoyages\":2", StringComparison.Ordinal), "per-status counts are serialized");
            }));

            cases.Add(Case("status_bounds_are_declared", "ArmadaStatus declares the bounds of its server-produced lists", TestTags.Positive, () =>
            {
                AssertEqual(ArmadaStatus.RecentSignalCount, Armada.Core.Services.CollectionBounds.BoundOf(typeof(ArmadaStatus).GetProperty("RecentSignals")!)!.Limit);
                AssertTrue(Enum.GetValues(typeof(MissionStatusEnum)).Length <= VoyageProgress.MaxStatusEntries, "MissionStatusEnum fits the declared status-dictionary bound");
            }));

            cases.Add(Case("recentsignals_null_setter_resets_to_empty", "ArmadaStatus RecentSignals null setter resets to empty", TestTags.Negative, () =>
            {
                ArmadaStatus status = new ArmadaStatus();
                status.RecentSignals = null!;
                AssertNotNull(status.RecentSignals);
                AssertEqual(0, status.RecentSignals.Count);
            }));

            cases.Add(Case("remotetunnel_null_setter_resets_to_default", "ArmadaStatus RemoteTunnel null setter resets to default", TestTags.Negative, () =>
            {
                ArmadaStatus status = new ArmadaStatus();
                status.RemoteTunnel = null!;
                AssertNotNull(status.RemoteTunnel);
                AssertEqual(RemoteTunnelStateEnum.Disabled, status.RemoteTunnel.State);
            }));

            cases.Add(Case("missionsbystatus_populate_and_read", "ArmadaStatus MissionsByStatus populate and read", TestTags.Positive, () =>
            {
                ArmadaStatus status = new ArmadaStatus();
                status.MissionsByStatus["Pending"] = 5;
                status.MissionsByStatus["InProgress"] = 3;
                status.MissionsByStatus["Complete"] = 10;

                AssertEqual(5, status.MissionsByStatus["Pending"]);
                AssertEqual(3, status.MissionsByStatus["InProgress"]);
                AssertEqual(10, status.MissionsByStatus["Complete"]);
            }));

            cases.Add(Case("voyage_progress_counts_only", "VoyageProgress carries counts and no vessel id list", TestTags.Positive, () =>
            {
                VoyageProgress vp = new VoyageProgress();
                vp.TotalMissions = 10;
                vp.CompletedMissions = 7;
                vp.FailedMissions = 1;
                vp.InProgressMissions = 2;
                vp.VesselId = "vsl_a";
                vp.VesselCount = 2;
                vp.MissionCountsByStatus["Complete"] = 7;
                vp.Voyage = new Voyage("Test Voyage");

                AssertNull(typeof(VoyageProgress).GetProperty("VesselIds"), "the distinct vessel list is replaced by VesselId and VesselCount");
                AssertEqual(10, vp.TotalMissions);
                AssertEqual(7, vp.MissionCountsByStatus["Complete"]);
                AssertEqual(2, vp.VesselCount);
            }));

            cases.Add(Case("serialization_round_trip", "ArmadaStatus serialization round trip", TestTags.Positive, () =>
            {
                ArmadaStatus status = new ArmadaStatus();
                status.TotalCaptains = 5;
                status.IdleCaptains = 2;
                status.WorkingCaptains = 2;
                status.StalledCaptains = 1;
                status.ActiveVoyages = 3;
                status.MissionsByStatus["Pending"] = 4;
                status.RemoteTunnel.State = RemoteTunnelStateEnum.Connected;
                status.RemoteTunnel.TunnelUrl = "wss://control.example.com/tunnel";
                status.RemoteTunnel.InstanceId = "armada-abc123";

                string json = JsonSerializer.Serialize(status);
                ArmadaStatus? deserialized = JsonSerializer.Deserialize<ArmadaStatus>(json);

                AssertNotNull(deserialized);
                AssertEqual(5, deserialized!.TotalCaptains);
                AssertEqual(2, deserialized.IdleCaptains);
                AssertEqual(2, deserialized.WorkingCaptains);
                AssertEqual(1, deserialized.StalledCaptains);
                AssertEqual(3, deserialized.ActiveVoyages);
                AssertEqual(RemoteTunnelStateEnum.Connected, deserialized.RemoteTunnel.State);
                AssertEqual("wss://control.example.com/tunnel", deserialized.RemoteTunnel.TunnelUrl);
            }));

            cases.Add(Case("voyageprogress_constructor_default_values", "VoyageProgress constructor default values", TestTags.Positive, () =>
            {
                VoyageProgress vp = new VoyageProgress();
                AssertEqual(0, vp.TotalMissions);
                AssertEqual(0, vp.CompletedMissions);
                AssertEqual(0, vp.FailedMissions);
                AssertEqual(0, vp.InProgressMissions);
                AssertNull(vp.Voyage);
            }));

            cases.Add(Case("voyageprogress_set_properties", "VoyageProgress set properties", TestTags.Positive, () =>
            {
                Voyage voyage = new Voyage("Progress Test");
                VoyageProgress vp = new VoyageProgress();
                vp.Voyage = voyage;
                vp.TotalMissions = 20;
                vp.CompletedMissions = 15;
                vp.FailedMissions = 2;
                vp.InProgressMissions = 3;

                AssertEqual("Progress Test", vp.Voyage.Title);
                AssertEqual(20, vp.TotalMissions);
                AssertEqual(15, vp.CompletedMissions);
                AssertEqual(2, vp.FailedMissions);
                AssertEqual(3, vp.InProgressMissions);
            }));

            cases.Add(Case("voyageprogress_serialization_round_trip", "VoyageProgress serialization round trip", TestTags.Positive, () =>
            {
                VoyageProgress vp = new VoyageProgress();
                vp.TotalMissions = 10;
                vp.CompletedMissions = 5;

                string json = JsonSerializer.Serialize(vp);
                VoyageProgress? deserialized = JsonSerializer.Deserialize<VoyageProgress>(json);

                AssertNotNull(deserialized);
                AssertEqual(10, deserialized!.TotalMissions);
                AssertEqual(5, deserialized.CompletedMissions);
            }));

            return new TestSuiteDescriptor(
                suiteId: "Models.ArmadaStatus",
                displayName: "ArmadaStatus Model",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: "Models.ArmadaStatus",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) =>
                {
                    body();
                    return Task.CompletedTask;
                },
                tags: new List<string> { tag });
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: "Models.ArmadaStatus",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion
    }
}

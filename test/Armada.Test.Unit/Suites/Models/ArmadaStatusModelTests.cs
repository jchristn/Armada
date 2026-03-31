namespace Armada.Test.Unit.Suites.Models
{
    using System.Text.Json;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Test.Common;

    public class ArmadaStatusModelTests : TestSuite
    {
        public override string Name => "ArmadaStatus Model";

        protected override async Task RunTestsAsync()
        {
            await RunTest("ArmadaStatus Constructor DefaultValues", () =>
            {
                ArmadaStatus status = new ArmadaStatus();
                AssertEqual(0, status.TotalCaptains);
                AssertEqual(0, status.IdleCaptains);
                AssertEqual(0, status.WorkingCaptains);
                AssertEqual(0, status.StalledCaptains);
                AssertEqual(0, status.ActiveVoyages);
                AssertNotNull(status.MissionsByStatus);
                AssertEqual(0, status.MissionsByStatus.Count);
                AssertNotNull(status.Voyages);
                AssertEqual(0, status.Voyages.Count);
                AssertNotNull(status.RecentSignals);
                AssertEqual(0, status.RecentSignals.Count);
                AssertTrue(status.TimestampUtc <= DateTime.UtcNow);
            });

            await RunTest("ArmadaStatus MissionsByStatus NullSetterResetsToEmpty", () =>
            {
                ArmadaStatus status = new ArmadaStatus();
                status.MissionsByStatus = null!;
                AssertNotNull(status.MissionsByStatus);
                AssertEqual(0, status.MissionsByStatus.Count);
            });

            await RunTest("ArmadaStatus Voyages NullSetterResetsToEmpty", () =>
            {
                ArmadaStatus status = new ArmadaStatus();
                status.Voyages = null!;
                AssertNotNull(status.Voyages);
                AssertEqual(0, status.Voyages.Count);
            });

            await RunTest("ArmadaStatus RecentSignals NullSetterResetsToEmpty", () =>
            {
                ArmadaStatus status = new ArmadaStatus();
                status.RecentSignals = null!;
                AssertNotNull(status.RecentSignals);
                AssertEqual(0, status.RecentSignals.Count);
            });

            await RunTest("ArmadaStatus MissionsByStatus PopulateAndRead", () =>
            {
                ArmadaStatus status = new ArmadaStatus();
                status.MissionsByStatus["Pending"] = 5;
                status.MissionsByStatus["InProgress"] = 3;
                status.MissionsByStatus["Complete"] = 10;

                AssertEqual(5, status.MissionsByStatus["Pending"]);
                AssertEqual(3, status.MissionsByStatus["InProgress"]);
                AssertEqual(10, status.MissionsByStatus["Complete"]);
            });

            await RunTest("ArmadaStatus Voyages PopulateAndRead", () =>
            {
                ArmadaStatus status = new ArmadaStatus();
                VoyageProgress vp = new VoyageProgress();
                vp.TotalMissions = 10;
                vp.CompletedMissions = 7;
                vp.FailedMissions = 1;
                vp.InProgressMissions = 2;
                vp.Voyage = new Voyage("Test Voyage");

                status.Voyages.Add(vp);

                AssertEqual(1, status.Voyages.Count);
                AssertEqual(10, status.Voyages[0].TotalMissions);
                AssertEqual(7, status.Voyages[0].CompletedMissions);
                AssertEqual(1, status.Voyages[0].FailedMissions);
                AssertEqual(2, status.Voyages[0].InProgressMissions);
            });

            await RunTest("ArmadaStatus SerializationRoundTrip", () =>
            {
                ArmadaStatus status = new ArmadaStatus();
                status.TotalCaptains = 5;
                status.IdleCaptains = 2;
                status.WorkingCaptains = 2;
                status.StalledCaptains = 1;
                status.ActiveVoyages = 3;
                status.MissionsByStatus["Pending"] = 4;

                string json = JsonSerializer.Serialize(status);
                ArmadaStatus? deserialized = JsonSerializer.Deserialize<ArmadaStatus>(json);

                AssertNotNull(deserialized);
                AssertEqual(5, deserialized!.TotalCaptains);
                AssertEqual(2, deserialized.IdleCaptains);
                AssertEqual(2, deserialized.WorkingCaptains);
                AssertEqual(1, deserialized.StalledCaptains);
                AssertEqual(3, deserialized.ActiveVoyages);
            });

            await RunTest("VoyageProgress Constructor DefaultValues", () =>
            {
                VoyageProgress vp = new VoyageProgress();
                AssertEqual(0, vp.TotalMissions);
                AssertEqual(0, vp.CompletedMissions);
                AssertEqual(0, vp.FailedMissions);
                AssertEqual(0, vp.InProgressMissions);
                AssertNull(vp.Voyage);
            });

            await RunTest("VoyageProgress SetProperties", () =>
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
            });

            await RunTest("VoyageProgress SerializationRoundTrip", () =>
            {
                VoyageProgress vp = new VoyageProgress();
                vp.TotalMissions = 10;
                vp.CompletedMissions = 5;

                string json = JsonSerializer.Serialize(vp);
                VoyageProgress? deserialized = JsonSerializer.Deserialize<VoyageProgress>(json);

                AssertNotNull(deserialized);
                AssertEqual(10, deserialized!.TotalMissions);
                AssertEqual(5, deserialized.CompletedMissions);
            });
        }
    }
}

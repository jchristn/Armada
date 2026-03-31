namespace Armada.Test.Unit.Suites.Models
{
    using System.Text.Json;
    using Armada.Core;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Test.Common;

    /// <summary>
    /// Extended model tests for Mission covering newer properties and edge cases.
    /// </summary>
    public class MissionExtendedModelTests : TestSuite
    {
        /// <summary>
        /// Test suite name.
        /// </summary>
        public override string Name => "Mission Extended Model";

        /// <summary>
        /// Run all extended mission model tests.
        /// </summary>
        protected override async Task RunTestsAsync()
        {
            await RunTest("Mission Persona DefaultsToNull", () =>
            {
                Mission mission = new Mission();
                AssertNull(mission.Persona);
            });

            await RunTest("Mission Persona CanBeSet", () =>
            {
                Mission mission = new Mission();
                mission.Persona = "Worker";
                AssertEqual("Worker", mission.Persona);
            });

            await RunTest("Mission DependsOnMissionId DefaultsToNull", () =>
            {
                Mission mission = new Mission();
                AssertNull(mission.DependsOnMissionId);
            });

            await RunTest("Mission DependsOnMissionId CanBeSet", () =>
            {
                Mission mission = new Mission();
                mission.DependsOnMissionId = "msn_parent_123";
                AssertEqual("msn_parent_123", mission.DependsOnMissionId);
            });

            await RunTest("Mission FailureReason DefaultsToNull", () =>
            {
                Mission mission = new Mission();
                AssertNull(mission.FailureReason);
            });

            await RunTest("Mission FailureReason CanBeSetAndCleared", () =>
            {
                Mission mission = new Mission();
                mission.FailureReason = "Process exited with code 1";
                AssertEqual("Process exited with code 1", mission.FailureReason);
                mission.FailureReason = null;
                AssertNull(mission.FailureReason);
            });

            await RunTest("Mission AgentOutput DefaultsToNull", () =>
            {
                Mission mission = new Mission();
                AssertNull(mission.AgentOutput);
            });

            await RunTest("Mission AgentOutput CanBeSet", () =>
            {
                Mission mission = new Mission();
                mission.AgentOutput = "[ARMADA:PROGRESS] 100\nDone.";
                AssertEqual("[ARMADA:PROGRESS] 100\nDone.", mission.AgentOutput);
            });

            await RunTest("Mission CommitHash DefaultsToNull", () =>
            {
                Mission mission = new Mission();
                AssertNull(mission.CommitHash);
            });

            await RunTest("Mission CommitHash CanBeSet", () =>
            {
                Mission mission = new Mission();
                mission.CommitHash = "abc123def456";
                AssertEqual("abc123def456", mission.CommitHash);
            });

            await RunTest("Mission PrUrl DefaultsToNull", () =>
            {
                Mission mission = new Mission();
                AssertNull(mission.PrUrl);
            });

            await RunTest("Mission PrUrl CanBeSet", () =>
            {
                Mission mission = new Mission();
                mission.PrUrl = "https://github.com/test/repo/pull/42";
                AssertEqual("https://github.com/test/repo/pull/42", mission.PrUrl);
            });

            await RunTest("Mission ProcessId DefaultsToNull", () =>
            {
                Mission mission = new Mission();
                AssertNull(mission.ProcessId);
            });

            await RunTest("Mission ProcessId CanBeSet", () =>
            {
                Mission mission = new Mission();
                mission.ProcessId = 54321;
                AssertEqual(54321, mission.ProcessId);
            });

            await RunTest("Mission DockId DefaultsToNull", () =>
            {
                Mission mission = new Mission();
                AssertNull(mission.DockId);
            });

            await RunTest("Mission ParentMissionId DefaultsToNull", () =>
            {
                Mission mission = new Mission();
                AssertNull(mission.ParentMissionId);
            });

            await RunTest("Mission SetId Null Throws", () =>
            {
                Mission mission = new Mission();
                AssertThrows<ArgumentNullException>(() => mission.Id = null!);
            });

            await RunTest("Mission SetId Empty Throws", () =>
            {
                Mission mission = new Mission();
                AssertThrows<ArgumentNullException>(() => mission.Id = "");
            });

            await RunTest("Mission Serialization IncludesAllProperties", () =>
            {
                Mission mission = new Mission("Full Mission", "Full description");
                mission.VoyageId = "vyg_test";
                mission.VesselId = "vsl_test";
                mission.CaptainId = "cpt_test";
                mission.Status = MissionStatusEnum.Complete;
                mission.Priority = 1;
                mission.ParentMissionId = "msn_parent";
                mission.BranchName = "feature/test";
                mission.DockId = "dck_test";
                mission.ProcessId = 12345;
                mission.PrUrl = "https://github.com/test/pull/1";
                mission.CommitHash = "deadbeef";
                mission.DiffSnapshot = "diff --git a/test";
                mission.AgentOutput = "output text";
                mission.Persona = "Architect";
                mission.DependsOnMissionId = "msn_dep";
                mission.FailureReason = "timeout";
                mission.StartedUtc = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);
                mission.CompletedUtc = new DateTime(2025, 6, 1, 1, 0, 0, DateTimeKind.Utc);

                string json = JsonSerializer.Serialize(mission);
                Mission deserialized = JsonSerializer.Deserialize<Mission>(json)!;

                AssertEqual(mission.Id, deserialized.Id);
                AssertEqual("Full Mission", deserialized.Title);
                AssertEqual("Full description", deserialized.Description);
                AssertEqual("vyg_test", deserialized.VoyageId);
                AssertEqual("vsl_test", deserialized.VesselId);
                AssertEqual("cpt_test", deserialized.CaptainId);
                AssertEqual(MissionStatusEnum.Complete, deserialized.Status);
                AssertEqual(1, deserialized.Priority);
                AssertEqual("msn_parent", deserialized.ParentMissionId);
                AssertEqual("feature/test", deserialized.BranchName);
                AssertEqual("dck_test", deserialized.DockId);
                AssertEqual(12345, deserialized.ProcessId);
                AssertEqual("https://github.com/test/pull/1", deserialized.PrUrl);
                AssertEqual("deadbeef", deserialized.CommitHash);
                AssertEqual("diff --git a/test", deserialized.DiffSnapshot);
                AssertEqual("output text", deserialized.AgentOutput);
                AssertEqual("Architect", deserialized.Persona);
                AssertEqual("msn_dep", deserialized.DependsOnMissionId);
                AssertEqual("timeout", deserialized.FailureReason);
                AssertNotNull(deserialized.StartedUtc);
                AssertNotNull(deserialized.CompletedUtc);
            });

            await RunTest("Mission AllStatusEnums SerializeAsString", () =>
            {
                MissionStatusEnum[] statuses = new MissionStatusEnum[]
                {
                    MissionStatusEnum.Pending,
                    MissionStatusEnum.Assigned,
                    MissionStatusEnum.InProgress,
                    MissionStatusEnum.WorkProduced,
                    MissionStatusEnum.Testing,
                    MissionStatusEnum.Review,
                    MissionStatusEnum.Complete,
                    MissionStatusEnum.Failed,
                    MissionStatusEnum.LandingFailed,
                    MissionStatusEnum.Cancelled
                };

                foreach (MissionStatusEnum status in statuses)
                {
                    Mission mission = new Mission();
                    mission.Status = status;
                    string json = JsonSerializer.Serialize(mission);
                    AssertContains("\"" + status.ToString() + "\"", json);
                }
            });

            await RunTest("Mission CreatedUtc IsSetOnConstruction", () =>
            {
                DateTime before = DateTime.UtcNow.AddSeconds(-1);
                Mission mission = new Mission();
                DateTime after = DateTime.UtcNow.AddSeconds(1);
                AssertTrue(mission.CreatedUtc >= before);
                AssertTrue(mission.CreatedUtc <= after);
            });

            await RunTest("Mission StartedUtc DefaultsToNull", () =>
            {
                Mission mission = new Mission();
                AssertNull(mission.StartedUtc);
            });

            await RunTest("Mission CompletedUtc DefaultsToNull", () =>
            {
                Mission mission = new Mission();
                AssertNull(mission.CompletedUtc);
            });
        }
    }
}

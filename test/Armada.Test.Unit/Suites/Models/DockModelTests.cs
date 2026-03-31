namespace Armada.Test.Unit.Suites.Models
{
    using System.Text.Json;
    using Armada.Core;
    using Armada.Core.Models;
    using Armada.Test.Common;

    public class DockModelTests : TestSuite
    {
        public override string Name => "Dock Model";

        protected override async Task RunTestsAsync()
        {
            await RunTest("Dock DefaultConstructor GeneratesIdWithPrefix", () =>
            {
                Dock dock = new Dock();
                AssertStartsWith(Constants.DockIdPrefix, dock.Id);
            });

            await RunTest("Dock VesselIdConstructor SetsVesselId", () =>
            {
                Dock dock = new Dock("vsl_test123");
                AssertEqual("vsl_test123", dock.VesselId);
            });

            await RunTest("Dock DefaultValues AreCorrect", () =>
            {
                Dock dock = new Dock();
                AssertTrue(dock.Active);
                AssertNull(dock.CaptainId);
                AssertNull(dock.WorktreePath);
                AssertNull(dock.BranchName);
            });

            await RunTest("Dock SetId Null Throws", () =>
            {
                Dock dock = new Dock();
                AssertThrows<ArgumentNullException>(() => dock.Id = null!);
            });

            await RunTest("Dock SetVesselId Empty Throws", () =>
            {
                Dock dock = new Dock();
                AssertThrows<ArgumentNullException>(() => dock.VesselId = "");
            });

            await RunTest("Dock Serialization RoundTrip", () =>
            {
                Dock dock = new Dock("vsl_test");
                dock.CaptainId = "cpt_test";
                dock.WorktreePath = "/tmp/worktree";
                dock.BranchName = "armada/test/msn_123";
                dock.Active = false;

                string json = JsonSerializer.Serialize(dock);
                Dock deserialized = JsonSerializer.Deserialize<Dock>(json)!;

                AssertEqual(dock.Id, deserialized.Id);
                AssertEqual(dock.VesselId, deserialized.VesselId);
                AssertEqual(dock.CaptainId, deserialized.CaptainId);
                AssertEqual(dock.WorktreePath, deserialized.WorktreePath);
                AssertEqual(dock.Active, deserialized.Active);
            });
        }
    }
}

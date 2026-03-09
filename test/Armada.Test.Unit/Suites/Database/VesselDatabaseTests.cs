namespace Armada.Test.Unit.Suites.Database
{
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Models;
    using Armada.Test.Common;
    using Armada.Test.Unit.TestHelpers;

    public class VesselDatabaseTests : TestSuite
    {
        public override string Name => "Vessel Database";

        protected override async Task RunTestsAsync()
        {
            await RunTest("CreateAsync returns vessel", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Vessel vessel = new Vessel("TestVessel", "https://github.com/test/repo");
                    Vessel result = await db.Vessels.CreateAsync(vessel);

                    AssertNotNull(result);
                    AssertEqual("TestVessel", result.Name);
                }
            });

            await RunTest("ReadAsync returns created vessel", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Vessel vessel = new Vessel("ReadTest", "https://github.com/test/repo");
                    await db.Vessels.CreateAsync(vessel);

                    Vessel? result = await db.Vessels.ReadAsync(vessel.Id);
                    AssertNotNull(result);
                    AssertEqual("ReadTest", result!.Name);
                    AssertEqual("main", result.DefaultBranch);
                }
            });

            await RunTest("ReadByNameAsync returns correct vessel", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Vessel vessel = new Vessel("NameLookup", "https://github.com/test/repo");
                    await db.Vessels.CreateAsync(vessel);

                    Vessel? result = await db.Vessels.ReadByNameAsync("NameLookup");
                    AssertNotNull(result);
                    AssertEqual(vessel.Id, result!.Id);
                }
            });

            await RunTest("EnumerateByFleetAsync filters correctly", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Fleet fleet = new Fleet("TestFleet");
                    await db.Fleets.CreateAsync(fleet);

                    Vessel v1 = new Vessel("InFleet", "https://github.com/test/repo1");
                    v1.FleetId = fleet.Id;
                    Vessel v2 = new Vessel("NoFleet", "https://github.com/test/repo2");

                    await db.Vessels.CreateAsync(v1);
                    await db.Vessels.CreateAsync(v2);

                    List<Vessel> fleetVessels = await db.Vessels.EnumerateByFleetAsync(fleet.Id);
                    AssertEqual(1, fleetVessels.Count);
                    AssertEqual("InFleet", fleetVessels[0].Name);
                }
            });

            await RunTest("UpdateAsync modifies vessel", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Vessel vessel = new Vessel("Original", "https://github.com/test/repo");
                    await db.Vessels.CreateAsync(vessel);

                    vessel.Name = "Updated";
                    vessel.DefaultBranch = "develop";
                    vessel.Active = false;
                    await db.Vessels.UpdateAsync(vessel);

                    Vessel? result = await db.Vessels.ReadAsync(vessel.Id);
                    AssertEqual("Updated", result!.Name);
                    AssertEqual("develop", result.DefaultBranch);
                    AssertFalse(result.Active);
                }
            });

            await RunTest("DeleteAsync removes vessel", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Vessel vessel = new Vessel("ToDelete", "https://github.com/test/repo");
                    await db.Vessels.CreateAsync(vessel);

                    await db.Vessels.DeleteAsync(vessel.Id);
                    AssertNull(await db.Vessels.ReadAsync(vessel.Id));
                }
            });

            await RunTest("ExistsAsync works correctly", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Vessel vessel = new Vessel("ExistsTest", "https://github.com/test/repo");
                    await db.Vessels.CreateAsync(vessel);

                    AssertTrue(await db.Vessels.ExistsAsync(vessel.Id));
                    AssertFalse(await db.Vessels.ExistsAsync("vsl_nonexistent"));
                }
            });

            await RunTest("CreateAsync with ProjectContext and StyleGuide persists both fields", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Vessel vessel = new Vessel("ContextVessel", "https://github.com/test/repo");
                    vessel.ProjectContext = "This is a .NET 8 web API with PostgreSQL backend.";
                    vessel.StyleGuide = "Use PascalCase for public members, camelCase for private.";
                    await db.Vessels.CreateAsync(vessel);

                    Vessel? result = await db.Vessels.ReadAsync(vessel.Id);
                    AssertNotNull(result);
                    AssertEqual("This is a .NET 8 web API with PostgreSQL backend.", result!.ProjectContext);
                    AssertEqual("Use PascalCase for public members, camelCase for private.", result.StyleGuide);
                }
            });

            await RunTest("CreateAsync with null ProjectContext and StyleGuide persists nulls", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Vessel vessel = new Vessel("NullContextVessel", "https://github.com/test/repo");
                    await db.Vessels.CreateAsync(vessel);

                    Vessel? result = await db.Vessels.ReadAsync(vessel.Id);
                    AssertNotNull(result);
                    AssertNull(result!.ProjectContext);
                    AssertNull(result.StyleGuide);
                }
            });

            await RunTest("UpdateAsync modifies ProjectContext and StyleGuide", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Vessel vessel = new Vessel("UpdateContextVessel", "https://github.com/test/repo");
                    await db.Vessels.CreateAsync(vessel);

                    vessel.ProjectContext = "Updated project context";
                    vessel.StyleGuide = "Updated style guide";
                    await db.Vessels.UpdateAsync(vessel);

                    Vessel? result = await db.Vessels.ReadAsync(vessel.Id);
                    AssertEqual("Updated project context", result!.ProjectContext);
                    AssertEqual("Updated style guide", result.StyleGuide);
                }
            });

            await RunTest("UpdateAsync can set ProjectContext and StyleGuide to null", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Vessel vessel = new Vessel("ClearContextVessel", "https://github.com/test/repo");
                    vessel.ProjectContext = "Initial context";
                    vessel.StyleGuide = "Initial style";
                    await db.Vessels.CreateAsync(vessel);

                    vessel.ProjectContext = null;
                    vessel.StyleGuide = null;
                    await db.Vessels.UpdateAsync(vessel);

                    Vessel? result = await db.Vessels.ReadAsync(vessel.Id);
                    AssertNull(result!.ProjectContext);
                    AssertNull(result.StyleGuide);
                }
            });

            await RunTest("ReadByNameAsync returns ProjectContext and StyleGuide", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Vessel vessel = new Vessel("NameLookupContext", "https://github.com/test/repo");
                    vessel.ProjectContext = "Context via name lookup";
                    vessel.StyleGuide = "Style via name lookup";
                    await db.Vessels.CreateAsync(vessel);

                    Vessel? result = await db.Vessels.ReadByNameAsync("NameLookupContext");
                    AssertNotNull(result);
                    AssertEqual("Context via name lookup", result!.ProjectContext);
                    AssertEqual("Style via name lookup", result.StyleGuide);
                }
            });

            await RunTest("EnumerateByFleetAsync returns ProjectContext and StyleGuide", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    Fleet fleet = new Fleet("ContextFleet");
                    await db.Fleets.CreateAsync(fleet);

                    Vessel vessel = new Vessel("FleetContextVessel", "https://github.com/test/repo");
                    vessel.FleetId = fleet.Id;
                    vessel.ProjectContext = "Fleet vessel context";
                    vessel.StyleGuide = "Fleet vessel style";
                    await db.Vessels.CreateAsync(vessel);

                    List<Vessel> results = await db.Vessels.EnumerateByFleetAsync(fleet.Id);
                    AssertEqual(1, results.Count);
                    AssertEqual("Fleet vessel context", results[0].ProjectContext);
                    AssertEqual("Fleet vessel style", results[0].StyleGuide);
                }
            });
        }
    }
}

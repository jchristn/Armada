namespace Armada.Test.Unit.Suites.Database
{
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Test.Common;
    using Armada.Test.Unit.TestHelpers;

    /// <summary>
    /// CRUD and query tests for Vessel database operations.
    /// </summary>
    public class VesselTests : TestSuite
    {
        /// <summary>
        /// Name of this test suite.
        /// </summary>
        public override string Name => "Vessel CRUD";

        /// <summary>
        /// Run all vessel CRUD tests.
        /// </summary>
        protected override async Task RunTestsAsync()
        {
            await RunTest("Vessel_Create", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    Fleet fleet = new Fleet("CreateFleet");
                    await db.Fleets.CreateAsync(fleet);

                    Vessel vessel = new Vessel("CreateVessel", "https://github.com/test/create-repo");
                    vessel.FleetId = fleet.Id;
                    Vessel result = await db.Vessels.CreateAsync(vessel);

                    AssertNotNull(result);
                    AssertEqual("CreateVessel", result.Name);
                    AssertEqual("https://github.com/test/create-repo", result.RepoUrl);
                    AssertEqual(fleet.Id, result.FleetId);
                    AssertTrue(result.Active);
                    AssertEqual("main", result.DefaultBranch);
                    AssertStartsWith("vsl_", result.Id);
                }
            });

            await RunTest("Vessel_Read", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    Fleet fleet = new Fleet("ReadFleet");
                    await db.Fleets.CreateAsync(fleet);

                    Vessel vessel = new Vessel("ReadVessel", "https://github.com/test/read-repo");
                    vessel.FleetId = fleet.Id;
                    vessel.DefaultBranch = "develop";
                    vessel.ProjectContext = "A test project context.";
                    vessel.StyleGuide = "A test style guide.";
                    await db.Vessels.CreateAsync(vessel);

                    Vessel? result = await db.Vessels.ReadAsync(vessel.Id);
                    AssertNotNull(result);
                    AssertEqual(vessel.Id, result!.Id);
                    AssertEqual("ReadVessel", result.Name);
                    AssertEqual("https://github.com/test/read-repo", result.RepoUrl);
                    AssertEqual(fleet.Id, result.FleetId);
                    AssertEqual("develop", result.DefaultBranch);
                    AssertEqual("A test project context.", result.ProjectContext);
                    AssertEqual("A test style guide.", result.StyleGuide);
                    AssertTrue(result.Active);
                }
            });

            await RunTest("Vessel_ReadByName", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    Fleet fleet = new Fleet("ReadByNameFleet");
                    await db.Fleets.CreateAsync(fleet);

                    Vessel vessel = new Vessel("UniqueVesselName", "https://github.com/test/name-repo");
                    vessel.FleetId = fleet.Id;
                    await db.Vessels.CreateAsync(vessel);

                    Vessel? result = await db.Vessels.ReadByNameAsync("UniqueVesselName");
                    AssertNotNull(result);
                    AssertEqual(vessel.Id, result!.Id);
                    AssertEqual("UniqueVesselName", result.Name);
                    AssertEqual(fleet.Id, result.FleetId);
                }
            });

            await RunTest("Vessel_Update", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    Fleet fleet = new Fleet("UpdateFleet");
                    await db.Fleets.CreateAsync(fleet);

                    Vessel vessel = new Vessel("OriginalName", "https://github.com/test/original-repo");
                    vessel.FleetId = fleet.Id;
                    await db.Vessels.CreateAsync(vessel);

                    vessel.Name = "UpdatedName";
                    vessel.RepoUrl = "https://github.com/test/updated-repo";
                    vessel.DefaultBranch = "release";
                    vessel.Active = false;
                    vessel.ProjectContext = "Updated context";
                    vessel.StyleGuide = "Updated style";
                    vessel.LocalPath = "/tmp/updated";
                    vessel.WorkingDirectory = "/work/updated";
                    await db.Vessels.UpdateAsync(vessel);

                    Vessel? result = await db.Vessels.ReadAsync(vessel.Id);
                    AssertNotNull(result);
                    AssertEqual("UpdatedName", result!.Name);
                    AssertEqual("https://github.com/test/updated-repo", result.RepoUrl);
                    AssertEqual("release", result.DefaultBranch);
                    AssertFalse(result.Active);
                    AssertEqual("Updated context", result.ProjectContext);
                    AssertEqual("Updated style", result.StyleGuide);
                    AssertEqual("/tmp/updated", result.LocalPath);
                    AssertEqual("/work/updated", result.WorkingDirectory);
                }
            });

            await RunTest("Vessel_Exists", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    Fleet fleet = new Fleet("ExistsFleet");
                    await db.Fleets.CreateAsync(fleet);

                    Vessel vessel = new Vessel("ExistsVessel", "https://github.com/test/exists-repo");
                    vessel.FleetId = fleet.Id;
                    await db.Vessels.CreateAsync(vessel);

                    bool exists = await db.Vessels.ExistsAsync(vessel.Id);
                    AssertTrue(exists);
                }
            });

            await RunTest("Vessel_Enumerate", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    Fleet fleet = new Fleet("EnumFleet");
                    await db.Fleets.CreateAsync(fleet);

                    Vessel v1 = new Vessel("EnumVessel1", "https://github.com/test/enum1");
                    v1.FleetId = fleet.Id;
                    Vessel v2 = new Vessel("EnumVessel2", "https://github.com/test/enum2");
                    v2.FleetId = fleet.Id;
                    Vessel v3 = new Vessel("EnumVessel3", "https://github.com/test/enum3");

                    await db.Vessels.CreateAsync(v1);
                    await db.Vessels.CreateAsync(v2);
                    await db.Vessels.CreateAsync(v3);

                    List<Vessel> results = await db.Vessels.EnumerateAsync();
                    AssertEqual(3, results.Count);
                }
            });

            await RunTest("Vessel_EnumeratePaginated", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    Fleet fleet = new Fleet("PaginateFleet");
                    await db.Fleets.CreateAsync(fleet);

                    for (int i = 1; i <= 5; i++)
                    {
                        Vessel vessel = new Vessel("PageVessel" + i, "https://github.com/test/page" + i);
                        vessel.FleetId = fleet.Id;
                        await db.Vessels.CreateAsync(vessel);
                    }

                    EnumerationQuery page1Query = new EnumerationQuery();
                    page1Query.PageNumber = 1;
                    page1Query.PageSize = 2;
                    page1Query.Order = EnumerationOrderEnum.CreatedAscending;
                    EnumerationResult<Vessel> page1 = await db.Vessels.EnumerateAsync(page1Query);

                    AssertEqual(1, page1.PageNumber);
                    AssertEqual(2, page1.PageSize);
                    AssertEqual(5, page1.TotalRecords);
                    AssertEqual(3, page1.TotalPages);
                    AssertEqual(2, page1.Objects.Count);

                    EnumerationQuery page3Query = new EnumerationQuery();
                    page3Query.PageNumber = 3;
                    page3Query.PageSize = 2;
                    page3Query.Order = EnumerationOrderEnum.CreatedAscending;
                    EnumerationResult<Vessel> page3 = await db.Vessels.EnumerateAsync(page3Query);

                    AssertEqual(3, page3.PageNumber);
                    AssertEqual(1, page3.Objects.Count);
                }
            });

            await RunTest("Vessel_EnumerateByFleet", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    Fleet fleetA = new Fleet("FleetAlpha");
                    Fleet fleetB = new Fleet("FleetBeta");
                    await db.Fleets.CreateAsync(fleetA);
                    await db.Fleets.CreateAsync(fleetB);

                    Vessel v1 = new Vessel("AlphaVessel1", "https://github.com/test/alpha1");
                    v1.FleetId = fleetA.Id;
                    Vessel v2 = new Vessel("AlphaVessel2", "https://github.com/test/alpha2");
                    v2.FleetId = fleetA.Id;
                    Vessel v3 = new Vessel("BetaVessel1", "https://github.com/test/beta1");
                    v3.FleetId = fleetB.Id;
                    Vessel v4 = new Vessel("NoFleetVessel", "https://github.com/test/nofleet");

                    await db.Vessels.CreateAsync(v1);
                    await db.Vessels.CreateAsync(v2);
                    await db.Vessels.CreateAsync(v3);
                    await db.Vessels.CreateAsync(v4);

                    List<Vessel> alphaVessels = await db.Vessels.EnumerateByFleetAsync(fleetA.Id);
                    AssertEqual(2, alphaVessels.Count);
                    AssertTrue(alphaVessels.Exists(v => v.Name == "AlphaVessel1"));
                    AssertTrue(alphaVessels.Exists(v => v.Name == "AlphaVessel2"));

                    List<Vessel> betaVessels = await db.Vessels.EnumerateByFleetAsync(fleetB.Id);
                    AssertEqual(1, betaVessels.Count);
                    AssertEqual("BetaVessel1", betaVessels[0].Name);
                }
            });

            await RunTest("Vessel_Delete", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    Fleet fleet = new Fleet("DeleteFleet");
                    await db.Fleets.CreateAsync(fleet);

                    Vessel vessel = new Vessel("DeleteVessel", "https://github.com/test/delete-repo");
                    vessel.FleetId = fleet.Id;
                    await db.Vessels.CreateAsync(vessel);

                    AssertTrue(await db.Vessels.ExistsAsync(vessel.Id));

                    await db.Vessels.DeleteAsync(vessel.Id);

                    Vessel? result = await db.Vessels.ReadAsync(vessel.Id);
                    AssertNull(result);
                    AssertFalse(await db.Vessels.ExistsAsync(vessel.Id));
                }
            });

            await RunTest("Vessel_ReadNotFound", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    Vessel? result = await db.Vessels.ReadAsync("vsl_nonexistent_id");
                    AssertNull(result);
                }
            });

            await RunTest("Vessel_ExistsNotFound", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;

                    bool exists = await db.Vessels.ExistsAsync("vsl_nonexistent_id");
                    AssertFalse(exists);
                }
            });
        }
    }
}

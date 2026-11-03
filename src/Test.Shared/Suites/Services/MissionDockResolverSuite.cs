namespace Test.Shared.Suites.Services
{
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    public sealed class MissionDockResolverSuite : IArmadaTestSuite
    {
        public TestSuiteDescriptor Build()
        {
            const string suite = "Services.MissionDockResolver";
            List<TestCaseDescriptor> cases = new();
            foreach (string mode in new[] { "own", "inactive", "missing", "deleted", "wrong-vessel", "wrong-tenant", "wrong-user", "tenant-admin" })
                cases.Add(new(suite, mode, "Mission dock ownership: " + mode, async _ =>
                {
                    using TestDatabase f = await TestDatabaseHelper.CreateDatabaseAsync(); var db = f.Driver;
                    TenantMetadata tenant = await db.Tenants.CreateAsync(new TenantMetadata("dock scope"));
                    TenantMetadata other = await db.Tenants.CreateAsync(new TenantMetadata("other scope"));
                    UserMaster user = await db.Users.CreateAsync(new UserMaster(tenant.Id, "reader@example.invalid", "password"));
                    UserMaster owner = await db.Users.CreateAsync(new UserMaster(tenant.Id, "owner@example.invalid", "password"));
                    Vessel vessel = await db.Vessels.CreateAsync(new Vessel("own", "https://example.invalid/repo") { TenantId = tenant.Id });
                    Vessel otherVessel = await db.Vessels.CreateAsync(new Vessel("other", "https://example.invalid/other") { TenantId = tenant.Id });
                    Dock dock = await db.Docks.CreateAsync(new Dock(mode == "wrong-vessel" ? otherVessel.Id : vessel.Id)
                    { TenantId = mode == "wrong-tenant" ? other.Id : tenant.Id, UserId = owner.Id, Active = mode != "inactive" });
                    Captain captain = await db.Captains.CreateAsync(new Captain("not authoritative") { TenantId = tenant.Id, CurrentDockId = dock.Id });
                    Mission mission = new Mission("target") { TenantId = tenant.Id, UserId = user.Id, VesselId = vessel.Id, CaptainId = captain.Id,
                        DockId = mode == "missing" ? null : mode == "deleted" ? "dck_deleted" : dock.Id };
                    AuthContext? caller = mode == "wrong-user" || mode == "tenant-admin"
                        ? new AuthContext { TenantId = tenant.Id, UserId = user.Id, IsTenantAdmin = mode == "tenant-admin" } : null;
                    Dock? result = await MissionDockResolver.ReadAsync(db, mission, caller: caller);
                    bool valid = mode == "own" || mode == "inactive" || mode == "tenant-admin";
                    AssertEqual(valid, result != null); if (valid) AssertEqual(dock.Id, result!.Id);
                    AssertEqual(dock.Id, (await db.Captains.ReadAsync(captain.Id))!.CurrentDockId);
                }));
            return new(suite, "Mission dock resolution", cases);
        }
    }
}

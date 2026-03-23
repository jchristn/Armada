namespace Armada.Test.Database
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Models;

    /// <summary>
    /// Integration tests that verify tenant isolation across fleets, vessels,
    /// and missions. Creates two independent tenants with their own entity
    /// hierarchies and verifies that scoped reads, enumerations, and deletes
    /// cannot cross tenant boundaries. Also verifies that admin (unscoped)
    /// enumeration sees all records.
    /// </summary>
    public class MultiTenantScopingTests
    {
        #region Private-Members

        private DatabaseDriver _Driver;
        private bool _NoCleanup;
        private List<TestResult> _Results = new List<TestResult>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with a database driver.
        /// </summary>
        /// <param name="driver">Initialized database driver.</param>
        /// <param name="noCleanup">When true, do not clean up test data after execution.</param>
        public MultiTenantScopingTests(DatabaseDriver driver, bool noCleanup = false)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _NoCleanup = noCleanup;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run all multi-tenant scoping tests.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>List of test results.</returns>
        public async Task<List<TestResult>> RunAllAsync(CancellationToken token = default)
        {
            _Results = new List<TestResult>();

            Console.WriteLine();
            Console.WriteLine("--- Multi-Tenant Scoping Tests ---");

            // Set up two tenants with full entity hierarchies
            TenantMetadata tenant1 = new TenantMetadata("ScopeTenant1-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            TenantMetadata tenant2 = new TenantMetadata("ScopeTenant2-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            await _Driver.Tenants.CreateAsync(tenant1);
            await _Driver.Tenants.CreateAsync(tenant2);

            // Create fleets in each tenant
            Fleet fleet1a = new Fleet("Fleet1A-" + Guid.NewGuid().ToString("N").Substring(0, 6)) { TenantId = tenant1.Id };
            Fleet fleet1b = new Fleet("Fleet1B-" + Guid.NewGuid().ToString("N").Substring(0, 6)) { TenantId = tenant1.Id };
            Fleet fleet2a = new Fleet("Fleet2A-" + Guid.NewGuid().ToString("N").Substring(0, 6)) { TenantId = tenant2.Id };
            await _Driver.Fleets.CreateAsync(fleet1a, token);
            await _Driver.Fleets.CreateAsync(fleet1b, token);
            await _Driver.Fleets.CreateAsync(fleet2a, token);

            // Create vessels in each tenant
            Vessel vessel1 = new Vessel("Vessel1-" + Guid.NewGuid().ToString("N").Substring(0, 6), "https://github.com/t1/repo1")
            {
                TenantId = tenant1.Id,
                FleetId = fleet1a.Id
            };
            Vessel vessel2 = new Vessel("Vessel2-" + Guid.NewGuid().ToString("N").Substring(0, 6), "https://github.com/t2/repo1")
            {
                TenantId = tenant2.Id,
                FleetId = fleet2a.Id
            };
            await _Driver.Vessels.CreateAsync(vessel1, token);
            await _Driver.Vessels.CreateAsync(vessel2, token);

            // Create voyages in each tenant
            Voyage voyage1 = new Voyage("Voyage1-" + Guid.NewGuid().ToString("N").Substring(0, 6)) { TenantId = tenant1.Id };
            Voyage voyage2 = new Voyage("Voyage2-" + Guid.NewGuid().ToString("N").Substring(0, 6)) { TenantId = tenant2.Id };
            await _Driver.Voyages.CreateAsync(voyage1, token);
            await _Driver.Voyages.CreateAsync(voyage2, token);

            // Create missions in each tenant
            Mission mission1a = new Mission("Mission1A-" + Guid.NewGuid().ToString("N").Substring(0, 6))
            {
                TenantId = tenant1.Id,
                VesselId = vessel1.Id,
                VoyageId = voyage1.Id
            };
            Mission mission1b = new Mission("Mission1B-" + Guid.NewGuid().ToString("N").Substring(0, 6))
            {
                TenantId = tenant1.Id,
                VesselId = vessel1.Id,
                VoyageId = voyage1.Id
            };
            Mission mission2a = new Mission("Mission2A-" + Guid.NewGuid().ToString("N").Substring(0, 6))
            {
                TenantId = tenant2.Id,
                VesselId = vessel2.Id,
                VoyageId = voyage2.Id
            };
            await _Driver.Missions.CreateAsync(mission1a, token);
            await _Driver.Missions.CreateAsync(mission1b, token);
            await _Driver.Missions.CreateAsync(mission2a, token);

            // ── Fleet scoping ──────────────────────────────────────────────

            await RunTest("Fleet", "Enumerate with tenantId=tenant1 returns only tenant1 fleets", async () =>
            {
                List<Fleet> t1Fleets = await _Driver.Fleets.EnumerateAsync(tenant1.Id, token);
                if (t1Fleets.Count != 2)
                    throw new Exception("Expected 2 fleets for tenant1 but got " + t1Fleets.Count);
                foreach (Fleet f in t1Fleets)
                {
                    if (f.TenantId != tenant1.Id)
                        throw new Exception("Fleet " + f.Id + " has TenantId " + f.TenantId + " but expected " + tenant1.Id);
                }
            });

            await RunTest("Fleet", "Read fleet from tenant2 using tenant1 scope returns null", async () =>
            {
                Fleet? crossRead = await _Driver.Fleets.ReadAsync(tenant1.Id, fleet2a.Id);
                if (crossRead != null)
                    throw new Exception("Expected null when reading tenant2 fleet via tenant1 scope, but got fleet " + crossRead.Id);
            });

            await RunTest("Fleet", "Delete fleet from tenant2 using tenant1 scope has no effect", async () =>
            {
                await _Driver.Fleets.DeleteAsync(tenant1.Id, fleet2a.Id, token);

                // fleet2a should still exist when read via its own tenant
                Fleet? stillThere = await _Driver.Fleets.ReadAsync(tenant2.Id, fleet2a.Id);
                if (stillThere == null)
                    throw new Exception("Fleet in tenant2 was deleted via tenant1 scope — isolation violated");
            });

            // ── Mission scoping ────────────────────────────────────────────

            await RunTest("Mission", "Enumerate with tenantId=tenant1 returns only tenant1 missions", async () =>
            {
                List<Mission> t1Missions = await _Driver.Missions.EnumerateAsync(tenant1.Id, token);
                if (t1Missions.Count != 2)
                    throw new Exception("Expected 2 missions for tenant1 but got " + t1Missions.Count);
                foreach (Mission m in t1Missions)
                {
                    if (m.TenantId != tenant1.Id)
                        throw new Exception("Mission " + m.Id + " has TenantId " + m.TenantId + " but expected " + tenant1.Id);
                }
            });

            await RunTest("Mission", "Enumerate with tenantId=tenant2 returns only tenant2 missions", async () =>
            {
                List<Mission> t2Missions = await _Driver.Missions.EnumerateAsync(tenant2.Id, token);
                if (t2Missions.Count != 1)
                    throw new Exception("Expected 1 mission for tenant2 but got " + t2Missions.Count);
                if (t2Missions[0].TenantId != tenant2.Id)
                    throw new Exception("Mission TenantId mismatch: expected " + tenant2.Id + " got " + t2Missions[0].TenantId);
            });

            await RunTest("Mission", "Read mission from tenant2 using tenant1 scope returns null", async () =>
            {
                Mission? crossRead = await _Driver.Missions.ReadAsync(tenant1.Id, mission2a.Id);
                if (crossRead != null)
                    throw new Exception("Expected null when reading tenant2 mission via tenant1 scope, but got mission " + crossRead.Id);
            });

            // ── Admin (unscoped) enumeration ───────────────────────────────

            await RunTest("Fleet", "Admin enumerate (no tenant filter) sees all fleets", async () =>
            {
                List<Fleet> allFleets = await _Driver.Fleets.EnumerateAsync(token);
                // Should include at least the 3 we created
                bool found1a = false, found1b = false, found2a = false;
                foreach (Fleet f in allFleets)
                {
                    if (f.Id == fleet1a.Id) found1a = true;
                    if (f.Id == fleet1b.Id) found1b = true;
                    if (f.Id == fleet2a.Id) found2a = true;
                }
                if (!found1a || !found1b || !found2a)
                    throw new Exception("Admin enumerate did not return all expected fleets: found1a=" + found1a + " found1b=" + found1b + " found2a=" + found2a);
            });

            await RunTest("Mission", "Admin enumerate (no tenant filter) sees all missions", async () =>
            {
                List<Mission> allMissions = await _Driver.Missions.EnumerateAsync(token);
                bool found1a = false, found1b = false, found2a = false;
                foreach (Mission m in allMissions)
                {
                    if (m.Id == mission1a.Id) found1a = true;
                    if (m.Id == mission1b.Id) found1b = true;
                    if (m.Id == mission2a.Id) found2a = true;
                }
                if (!found1a || !found1b || !found2a)
                    throw new Exception("Admin enumerate did not return all expected missions: found1a=" + found1a + " found1b=" + found1b + " found2a=" + found2a);
            });

            // ── Cleanup ────────────────────────────────────────────────────

            if (!_NoCleanup)
            {
                try
                {
                    await _Driver.Missions.DeleteAsync(mission1a.Id, token);
                    await _Driver.Missions.DeleteAsync(mission1b.Id, token);
                    await _Driver.Missions.DeleteAsync(mission2a.Id, token);
                    await _Driver.Voyages.DeleteAsync(voyage1.Id, token);
                    await _Driver.Voyages.DeleteAsync(voyage2.Id, token);
                    await _Driver.Vessels.DeleteAsync(vessel1.Id, token);
                    await _Driver.Vessels.DeleteAsync(vessel2.Id, token);
                    await _Driver.Fleets.DeleteAsync(fleet1a.Id, token);
                    await _Driver.Fleets.DeleteAsync(fleet1b.Id, token);
                    await _Driver.Fleets.DeleteAsync(fleet2a.Id, token);
                    await _Driver.Tenants.DeleteAsync(tenant1.Id);
                    await _Driver.Tenants.DeleteAsync(tenant2.Id);
                }
                catch
                {
                    // Best-effort cleanup
                }
            }

            return _Results;
        }

        #endregion

        #region Private-Methods

        private async Task RunTest(string category, string testName, Func<Task> action)
        {
            TestResult result = new TestResult(category + " / " + testName, category);
            Stopwatch sw = Stopwatch.StartNew();

            try
            {
                await action().ConfigureAwait(false);
                sw.Stop();
                result.MarkPassed(sw.Elapsed);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("  [PASS] ");
                Console.ResetColor();
                Console.WriteLine(category + " / " + testName + " (" + sw.ElapsedMilliseconds + "ms)");
            }
            catch (Exception ex)
            {
                sw.Stop();
                result.MarkFailed(sw.Elapsed, ex.Message, ex);

                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write("  [FAIL] ");
                Console.ResetColor();
                Console.WriteLine(category + " / " + testName + " (" + sw.ElapsedMilliseconds + "ms) - " + ex.Message);
            }

            _Results.Add(result);
        }

        #endregion
    }
}

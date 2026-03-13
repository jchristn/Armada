#nullable enable

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
    /// Runs integration tests against a live database driver.
    /// </summary>
    public class DatabaseTestRunner
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
        public DatabaseTestRunner(DatabaseDriver driver, bool noCleanup = false)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _NoCleanup = noCleanup;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run all database integration tests.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>List of test results.</returns>
        public async Task<List<TestResult>> RunAllAsync(CancellationToken token = default)
        {
            _Results = new List<TestResult>();

            Console.WriteLine("--- Fleet CRUD ---");
            await RunTest("Fleet_Create_Read", "Fleet", () => TestFleetCreateReadAsync(token), token);
            await RunTest("Fleet_Enumerate", "Fleet", () => TestFleetEnumerateAsync(token), token);

            Console.WriteLine();
            Console.WriteLine("--- Vessel CRUD ---");
            await RunTest("Vessel_Create_Read", "Vessel", () => TestVesselCreateReadAsync(token), token);

            Console.WriteLine();
            Console.WriteLine("--- Captain CRUD ---");
            await RunTest("Captain_Create_Read", "Captain", () => TestCaptainCreateReadAsync(token), token);

            Console.WriteLine();
            Console.WriteLine("--- Mission CRUD ---");
            await RunTest("Mission_Create_Read", "Mission", () => TestMissionCreateReadAsync(token), token);

            Console.WriteLine();
            Console.WriteLine("--- Voyage CRUD ---");
            await RunTest("Voyage_Create_Read", "Voyage", () => TestVoyageCreateReadAsync(token), token);

            Console.WriteLine();
            Console.WriteLine("--- Dock CRUD ---");
            await RunTest("Dock_Create_Read", "Dock", () => TestDockCreateReadAsync(token), token);

            return _Results;
        }

        #endregion

        #region Private-Methods

        private async Task RunTest(string name, string category, Func<Task> action, CancellationToken token)
        {
            TestResult result = new TestResult(name, category);
            Stopwatch sw = Stopwatch.StartNew();

            try
            {
                token.ThrowIfCancellationRequested();
                await action().ConfigureAwait(false);
                sw.Stop();
                result.MarkPassed(sw.Elapsed);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("  [PASS] ");
                Console.ResetColor();
                Console.WriteLine(name + " (" + sw.ElapsedMilliseconds + "ms)");
            }
            catch (Exception ex)
            {
                sw.Stop();
                result.MarkFailed(sw.Elapsed, ex.Message, ex);

                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write("  [FAIL] ");
                Console.ResetColor();
                Console.WriteLine(name + " (" + sw.ElapsedMilliseconds + "ms) - " + ex.Message);
            }

            _Results.Add(result);
        }

        private async Task TestFleetCreateReadAsync(CancellationToken token)
        {
            Fleet fleet = new Fleet("test-fleet-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Fleet created = await _Driver.Fleets.CreateAsync(fleet, token).ConfigureAwait(false);
            if (created == null) throw new Exception("CreateAsync returned null");

            Fleet? read = await _Driver.Fleets.ReadAsync(created.Id, token).ConfigureAwait(false);
            if (read == null) throw new Exception("ReadAsync returned null for ID: " + created.Id);
            if (read.Name != fleet.Name) throw new Exception("Name mismatch: expected " + fleet.Name + " got " + read.Name);

            if (!_NoCleanup)
            {
                await _Driver.Fleets.DeleteAsync(created.Id, token).ConfigureAwait(false);
            }
        }

        private async Task TestFleetEnumerateAsync(CancellationToken token)
        {
            Fleet fleet = new Fleet("test-enum-fleet-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            await _Driver.Fleets.CreateAsync(fleet, token).ConfigureAwait(false);

            EnumerationResult<Fleet> results = await _Driver.Fleets.EnumerateAsync(new EnumerationQuery(), token).ConfigureAwait(false);
            if (results == null) throw new Exception("EnumerateAsync returned null");
            if (results.Objects == null || results.Objects.Count == 0) throw new Exception("EnumerateAsync returned no results");

            if (!_NoCleanup)
            {
                await _Driver.Fleets.DeleteAsync(fleet.Id, token).ConfigureAwait(false);
            }
        }

        private async Task TestVesselCreateReadAsync(CancellationToken token)
        {
            Vessel vessel = new Vessel("test-vessel-" + Guid.NewGuid().ToString("N").Substring(0, 8), "https://github.com/test/repo.git");
            Vessel created = await _Driver.Vessels.CreateAsync(vessel, token).ConfigureAwait(false);
            if (created == null) throw new Exception("CreateAsync returned null");

            Vessel? read = await _Driver.Vessels.ReadAsync(created.Id, token).ConfigureAwait(false);
            if (read == null) throw new Exception("ReadAsync returned null");

            if (!_NoCleanup)
            {
                await _Driver.Vessels.DeleteAsync(created.Id, token).ConfigureAwait(false);
            }
        }

        private async Task TestCaptainCreateReadAsync(CancellationToken token)
        {
            Captain captain = new Captain("test-captain-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Captain created = await _Driver.Captains.CreateAsync(captain, token).ConfigureAwait(false);
            if (created == null) throw new Exception("CreateAsync returned null");

            Captain? read = await _Driver.Captains.ReadAsync(created.Id, token).ConfigureAwait(false);
            if (read == null) throw new Exception("ReadAsync returned null");

            if (!_NoCleanup)
            {
                await _Driver.Captains.DeleteAsync(created.Id, token).ConfigureAwait(false);
            }
        }

        private async Task TestMissionCreateReadAsync(CancellationToken token)
        {
            Mission mission = new Mission("test-mission-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Mission created = await _Driver.Missions.CreateAsync(mission, token).ConfigureAwait(false);
            if (created == null) throw new Exception("CreateAsync returned null");

            Mission? read = await _Driver.Missions.ReadAsync(created.Id, token).ConfigureAwait(false);
            if (read == null) throw new Exception("ReadAsync returned null");

            if (!_NoCleanup)
            {
                await _Driver.Missions.DeleteAsync(created.Id, token).ConfigureAwait(false);
            }
        }

        private async Task TestVoyageCreateReadAsync(CancellationToken token)
        {
            Voyage voyage = new Voyage("test-voyage-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Voyage created = await _Driver.Voyages.CreateAsync(voyage, token).ConfigureAwait(false);
            if (created == null) throw new Exception("CreateAsync returned null");

            Voyage? read = await _Driver.Voyages.ReadAsync(created.Id, token).ConfigureAwait(false);
            if (read == null) throw new Exception("ReadAsync returned null");

            if (!_NoCleanup)
            {
                await _Driver.Voyages.DeleteAsync(created.Id, token).ConfigureAwait(false);
            }
        }

        private async Task TestDockCreateReadAsync(CancellationToken token)
        {
            // Dock requires a vessel
            Vessel vessel = new Vessel("test-dock-vessel-" + Guid.NewGuid().ToString("N").Substring(0, 8), "https://github.com/test/repo.git");
            await _Driver.Vessels.CreateAsync(vessel, token).ConfigureAwait(false);

            Dock dock = new Dock(vessel.Id);
            Dock created = await _Driver.Docks.CreateAsync(dock, token).ConfigureAwait(false);
            if (created == null) throw new Exception("CreateAsync returned null");

            Dock? read = await _Driver.Docks.ReadAsync(created.Id, token).ConfigureAwait(false);
            if (read == null) throw new Exception("ReadAsync returned null");

            if (!_NoCleanup)
            {
                await _Driver.Docks.DeleteAsync(created.Id, token).ConfigureAwait(false);
                await _Driver.Vessels.DeleteAsync(vessel.Id, token).ConfigureAwait(false);
            }
        }

        #endregion
    }
}

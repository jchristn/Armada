namespace Armada.Test.Common
{
    using System.Diagnostics;
    using Armada.Core.Database;

    /// <summary>
    /// Runs all database integration tests against a given DatabaseDriver instance.
    /// Prints per-test PASS/FAIL results and a final summary.
    /// </summary>
    public class DatabaseTestRunner
    {
        #region Private-Members

        private DatabaseDriver _Driver;
        private bool _NoCleanup;
        private List<TestResult> _Results = new List<TestResult>();
        private Stopwatch _TotalStopwatch = new Stopwatch();
        private int _TestCounter = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Create a new database test runner.
        /// </summary>
        /// <param name="driver">The database driver to test against.</param>
        /// <param name="noCleanup">When true, skip cleanup after tests to allow manual inspection.</param>
        public DatabaseTestRunner(DatabaseDriver driver, bool noCleanup)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _NoCleanup = noCleanup;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run all database test categories sequentially and print a summary.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Exit code: 0 for all passed, 1 for any failures.</returns>
        public async Task<int> RunAllTestsAsync(CancellationToken token = default)
        {
            Console.WriteLine();
            Console.WriteLine("================================================================================");
            Console.WriteLine("DATABASE TEST RUNNER");
            Console.WriteLine("================================================================================");
            Console.WriteLine("  Driver:     " + _Driver.GetType().Name);
            Console.WriteLine("  No Cleanup: " + _NoCleanup);
            Console.WriteLine("================================================================================");

            _TotalStopwatch = Stopwatch.StartNew();

            await RunFleetTestsAsync(token).ConfigureAwait(false);
            await RunVesselTestsAsync(token).ConfigureAwait(false);
            await RunCaptainTestsAsync(token).ConfigureAwait(false);
            await RunMissionTestsAsync(token).ConfigureAwait(false);
            await RunVoyageTestsAsync(token).ConfigureAwait(false);
            await RunDockTestsAsync(token).ConfigureAwait(false);
            await RunSignalTestsAsync(token).ConfigureAwait(false);
            await RunEventTestsAsync(token).ConfigureAwait(false);
            await RunMergeEntryTestsAsync(token).ConfigureAwait(false);

            return PrintSummary();
        }

        #endregion

        #region Private-Methods

        private async Task RunTestAsync(string category, string testName, Func<Task> testAction)
        {
            _TestCounter++;
            string fullName = category + " / " + testName;
            Stopwatch sw = Stopwatch.StartNew();
            TestResult result = new TestResult { Name = fullName };

            try
            {
                await testAction().ConfigureAwait(false);
                sw.Stop();
                result.MarkPassed(sw.ElapsedMilliseconds);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("  [PASS] ");
                Console.ResetColor();
                Console.WriteLine(fullName + " (" + result.ElapsedMs + "ms)");
            }
            catch (Exception ex)
            {
                sw.Stop();
                result.MarkFailed(sw.ElapsedMilliseconds, ex.GetType().Name + " — " + ex.Message, ex);

                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write("  [FAIL] ");
                Console.ResetColor();
                Console.WriteLine(fullName + " (" + result.ElapsedMs + "ms)");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("         " + result.Message);
                Console.ResetColor();
            }

            _Results.Add(result);
        }

        private int PrintSummary()
        {
            _TotalStopwatch.Stop();

            int total = _Results.Count;
            int passed = _Results.Count(r => r.Passed);
            int failed = total - passed;
            List<TestResult> failedTests = _Results.Where(r => !r.Passed).ToList();

            Console.WriteLine();
            Console.WriteLine("================================================================================");
            Console.WriteLine("TEST SUMMARY");
            Console.WriteLine("================================================================================");
            Console.WriteLine("  Total:   " + total);
            Console.WriteLine("  Passed:  " + passed);
            Console.WriteLine("  Failed:  " + failed);
            Console.WriteLine("  Runtime: " + _TotalStopwatch.ElapsedMilliseconds + "ms");

            if (failedTests.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("Failed Tests:");
                foreach (TestResult result in failedTests)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Write("  - " + result.Name);
                    Console.ResetColor();
                    Console.WriteLine(": " + result.Message);
                }
            }

            Console.WriteLine();
            Console.WriteLine("================================================================================");

            if (failed == 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("RESULT: PASS");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("RESULT: FAIL");
            }

            Console.ResetColor();
            Console.WriteLine("================================================================================");
            Console.WriteLine();

            return failed == 0 ? 0 : 1;
        }

        private async Task RunFleetTestsAsync(CancellationToken token)
        {
            Console.WriteLine();
            Console.WriteLine("--- Fleet Tests ---");
        }

        private async Task RunVesselTestsAsync(CancellationToken token)
        {
            Console.WriteLine();
            Console.WriteLine("--- Vessel Tests ---");
        }

        private async Task RunCaptainTestsAsync(CancellationToken token)
        {
            Console.WriteLine();
            Console.WriteLine("--- Captain Tests ---");
        }

        private async Task RunMissionTestsAsync(CancellationToken token)
        {
            Console.WriteLine();
            Console.WriteLine("--- Mission Tests ---");
        }

        private async Task RunVoyageTestsAsync(CancellationToken token)
        {
            Console.WriteLine();
            Console.WriteLine("--- Voyage Tests ---");
        }

        private async Task RunDockTestsAsync(CancellationToken token)
        {
            Console.WriteLine();
            Console.WriteLine("--- Dock Tests ---");
        }

        private async Task RunSignalTestsAsync(CancellationToken token)
        {
            Console.WriteLine();
            Console.WriteLine("--- Signal Tests ---");
        }

        private async Task RunEventTestsAsync(CancellationToken token)
        {
            Console.WriteLine();
            Console.WriteLine("--- Event Tests ---");
        }

        private async Task RunMergeEntryTestsAsync(CancellationToken token)
        {
            Console.WriteLine();
            Console.WriteLine("--- Merge Entry Tests ---");
        }

        #endregion
    }
}

namespace Armada.Test.Common
{
    using System.Diagnostics;

    /// <summary>
    /// Orchestrates test suite execution, collects results, and prints a summary.
    /// </summary>
    public class TestRunner
    {
        #region Private-Members

        private List<TestSuite> _Suites = new List<TestSuite>();
        private List<TestResult> _AllResults = new List<TestResult>();
        private string _Title;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Create a new test runner with the given title.
        /// </summary>
        public TestRunner(string title)
        {
            _Title = title ?? throw new ArgumentNullException(nameof(title));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Add a test suite to the runner.
        /// </summary>
        public void AddSuite(TestSuite suite)
        {
            _Suites.Add(suite ?? throw new ArgumentNullException(nameof(suite)));
        }

        /// <summary>
        /// Run all suites sequentially, print results, and return the exit code (0 = pass, 1 = fail).
        /// </summary>
        public async Task<int> RunAllAsync()
        {
            Console.WriteLine();
            Console.WriteLine("================================================================================");
            Console.WriteLine(_Title);
            Console.WriteLine("================================================================================");

            Stopwatch totalTimer = Stopwatch.StartNew();

            foreach (TestSuite suite in _Suites)
            {
                Console.WriteLine();
                Console.WriteLine("--- " + suite.Name + " ---");

                List<TestResult> results = await suite.RunAsync().ConfigureAwait(false);
                _AllResults.AddRange(results);
            }

            totalTimer.Stop();

            return PrintSummary(totalTimer.ElapsedMilliseconds);
        }

        #endregion

        #region Private-Methods

        private int PrintSummary(long totalMs)
        {
            int total = _AllResults.Count;
            int passed = _AllResults.Count(r => r.Passed);
            int failed = total - passed;

            List<TestResult> failedTests = _AllResults.Where(r => !r.Passed).ToList();

            Console.WriteLine();
            Console.WriteLine("================================================================================");
            Console.WriteLine("TEST SUMMARY");
            Console.WriteLine("================================================================================");
            Console.WriteLine("Total: " + total + "  Passed: " + passed + "  Failed: " + failed + "  Runtime: " + totalMs + "ms");

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

        #endregion
    }
}

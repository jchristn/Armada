namespace Armada.Test.Common
{
    using System.Diagnostics;
    using System.Net;

    /// <summary>
    /// Abstract base class for test suites. Provides assertion helpers and test execution wrappers.
    /// </summary>
    public abstract class TestSuite
    {
        #region Public-Members

        /// <summary>
        /// Name of this test suite.
        /// </summary>
        public abstract string Name { get; }

        #endregion

        #region Private-Members

        private List<TestResult> _Results = new List<TestResult>();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run all tests in this suite and return results.
        /// </summary>
        public async Task<List<TestResult>> RunAsync()
        {
            _Results = new List<TestResult>();
            await RunTestsAsync().ConfigureAwait(false);
            return _Results;
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Override to define and run tests using RunTest().
        /// </summary>
        protected abstract Task RunTestsAsync();

        /// <summary>
        /// Wraps a test in try/catch with timing. Prints PASS/FAIL with elapsed milliseconds.
        /// </summary>
        protected async Task RunTest(string name, Func<Task> action)
        {
            Stopwatch sw = Stopwatch.StartNew();
            TestResult result = new TestResult { Name = name };

            try
            {
                await action().ConfigureAwait(false);
                sw.Stop();
                result.Passed = true;
                result.ElapsedMs = sw.ElapsedMilliseconds;

                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("  PASS  ");
                Console.ResetColor();
                Console.WriteLine(name + " (" + result.ElapsedMs + "ms)");
            }
            catch (Exception ex)
            {
                sw.Stop();
                result.Passed = false;
                result.ElapsedMs = sw.ElapsedMilliseconds;
                result.Exception = ex;
                result.Message = ex.GetType().Name + " — " + ex.Message;

                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write("  FAIL  ");
                Console.ResetColor();
                Console.WriteLine(name + " (" + result.ElapsedMs + "ms)");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("         " + result.Message);
                Console.ResetColor();
            }

            _Results.Add(result);
        }

        /// <summary>
        /// Wraps a synchronous test in try/catch with timing.
        /// </summary>
        protected async Task RunTest(string name, Action action)
        {
            await RunTest(name, () =>
            {
                action();
                return Task.CompletedTask;
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Assert a condition is true. Throws if false.
        /// </summary>
        protected void Assert(bool condition, string message)
        {
            if (!condition) throw new Exception("Assertion failed: " + message);
        }

        /// <summary>
        /// Assert two values are equal.
        /// </summary>
        protected void AssertEqual<T>(T expected, T actual, string? label = null)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                string msg = label != null
                    ? label + ": expected <" + expected + "> but got <" + actual + ">"
                    : "Expected <" + expected + "> but got <" + actual + ">";
                throw new Exception("Assertion failed: " + msg);
            }
        }

        /// <summary>
        /// Assert a value is not null.
        /// </summary>
        protected void AssertNotNull(object? value, string? label = null)
        {
            if (value == null)
            {
                string msg = label != null
                    ? label + " was null"
                    : "Value was null";
                throw new Exception("Assertion failed: " + msg);
            }
        }

        /// <summary>
        /// Assert a value is null.
        /// </summary>
        protected void AssertNull(object? value, string? label = null)
        {
            if (value != null)
            {
                string msg = label != null
                    ? label + " was not null (was: " + value + ")"
                    : "Value was not null (was: " + value + ")";
                throw new Exception("Assertion failed: " + msg);
            }
        }

        /// <summary>
        /// Assert a condition is true.
        /// </summary>
        protected void AssertTrue(bool condition, string? label = null)
        {
            if (!condition)
            {
                string msg = label != null
                    ? label + " was false"
                    : "Condition was false";
                throw new Exception("Assertion failed: " + msg);
            }
        }

        /// <summary>
        /// Assert a condition is false.
        /// </summary>
        protected void AssertFalse(bool condition, string? label = null)
        {
            if (condition)
            {
                string msg = label != null
                    ? label + " was true"
                    : "Condition was true";
                throw new Exception("Assertion failed: " + msg);
            }
        }

        /// <summary>
        /// Assert an HTTP response has the expected status code.
        /// </summary>
        protected void AssertStatusCode(HttpStatusCode expected, HttpResponseMessage response, string? label = null)
        {
            if (response.StatusCode != expected)
            {
                string msg = label != null
                    ? label + ": expected " + (int)expected + " " + expected + " but got " + (int)response.StatusCode + " " + response.StatusCode
                    : "Expected " + (int)expected + " " + expected + " but got " + (int)response.StatusCode + " " + response.StatusCode;
                throw new Exception("Assertion failed: " + msg);
            }
        }

        /// <summary>
        /// Assert that the given action throws the specified exception type.
        /// </summary>
        protected void AssertThrows<TException>(Action action, string? label = null) where TException : Exception
        {
            try
            {
                action();
                string msg = label != null
                    ? label + ": expected " + typeof(TException).Name + " but no exception was thrown"
                    : "Expected " + typeof(TException).Name + " but no exception was thrown";
                throw new Exception("Assertion failed: " + msg);
            }
            catch (TException)
            {
                // Expected
            }
        }

        /// <summary>
        /// Assert that the given async action throws the specified exception type.
        /// </summary>
        protected async Task AssertThrowsAsync<TException>(Func<Task> action, string? label = null) where TException : Exception
        {
            try
            {
                await action().ConfigureAwait(false);
                string msg = label != null
                    ? label + ": expected " + typeof(TException).Name + " but no exception was thrown"
                    : "Expected " + typeof(TException).Name + " but no exception was thrown";
                throw new Exception("Assertion failed: " + msg);
            }
            catch (TException)
            {
                // Expected
            }
        }

        /// <summary>
        /// Assert a string starts with the expected prefix.
        /// </summary>
        protected void AssertStartsWith(string expected, string actual, string? label = null)
        {
            if (actual == null || !actual.StartsWith(expected))
            {
                string msg = label != null
                    ? label + ": expected to start with <" + expected + "> but got <" + actual + ">"
                    : "Expected to start with <" + expected + "> but got <" + actual + ">";
                throw new Exception("Assertion failed: " + msg);
            }
        }

        /// <summary>
        /// Assert a string contains the expected substring.
        /// </summary>
        protected void AssertContains(string expected, string actual, string? label = null)
        {
            if (actual == null || !actual.Contains(expected))
            {
                string msg = label != null
                    ? label + ": expected to contain <" + expected + "> but got <" + actual + ">"
                    : "Expected to contain <" + expected + "> but got <" + actual + ">";
                throw new Exception("Assertion failed: " + msg);
            }
        }

        /// <summary>
        /// Assert two values are not equal.
        /// </summary>
        protected void AssertNotEqual<T>(T unexpected, T actual, string? label = null)
        {
            if (EqualityComparer<T>.Default.Equals(unexpected, actual))
            {
                string msg = label != null
                    ? label + ": values should not be equal but both were <" + actual + ">"
                    : "Values should not be equal but both were <" + actual + ">";
                throw new Exception("Assertion failed: " + msg);
            }
        }

        #endregion
    }
}

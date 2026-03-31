namespace Armada.Test.Database
{
    using System;
    using System.Diagnostics;

    /// <summary>
    /// Holds the result of a single database test execution.
    /// </summary>
    public class TestResult
    {
        #region Public-Members

        /// <summary>
        /// Name of the test.
        /// </summary>
        public string TestName
        {
            get => _TestName;
            set { if (!String.IsNullOrEmpty(value)) _TestName = value; }
        }

        /// <summary>
        /// Category grouping for the test.
        /// </summary>
        public string Category
        {
            get => _Category;
            set { if (!String.IsNullOrEmpty(value)) _Category = value; }
        }

        /// <summary>
        /// Whether the test passed.
        /// </summary>
        public bool Passed { get; private set; } = false;

        /// <summary>
        /// Duration of the test execution.
        /// </summary>
        public TimeSpan Duration { get; set; } = TimeSpan.Zero;

        /// <summary>
        /// Error message describing the failure, if any.
        /// </summary>
        public string ErrorMessage { get; set; } = "";

        /// <summary>
        /// Exception that caused the failure, if any.
        /// </summary>
        public Exception Exception { get; set; } = null;

        #endregion

        #region Private-Members

        private string _TestName = "";
        private string _Category = "";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with defaults.
        /// </summary>
        public TestResult()
        {
        }

        /// <summary>
        /// Instantiate with a test name and category.
        /// </summary>
        /// <param name="testName">Name of the test.</param>
        /// <param name="category">Category grouping.</param>
        public TestResult(string testName, string category)
        {
            TestName = testName ?? throw new ArgumentNullException(nameof(testName));
            Category = category ?? throw new ArgumentNullException(nameof(category));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Mark the test as passed with the given duration.
        /// </summary>
        /// <param name="duration">Time taken to execute the test.</param>
        public void MarkPassed(TimeSpan duration)
        {
            Passed = true;
            Duration = duration;
            ErrorMessage = "";
            Exception = null;
        }

        /// <summary>
        /// Mark the test as failed with an error message and optional exception.
        /// </summary>
        /// <param name="duration">Time taken before the failure.</param>
        /// <param name="errorMessage">Description of the failure.</param>
        /// <param name="exception">Exception that caused the failure, if any.</param>
        public void MarkFailed(TimeSpan duration, string errorMessage, Exception exception = null)
        {
            Passed = false;
            Duration = duration;
            ErrorMessage = errorMessage ?? "";
            Exception = exception;
        }

        /// <summary>
        /// Returns a formatted string showing [PASS] or [FAIL] with duration in milliseconds.
        /// </summary>
        /// <returns>Formatted result string.</returns>
        public override string ToString()
        {
            string status = Passed ? "[PASS]" : "[FAIL]";
            string ms = Duration.TotalMilliseconds.ToString("F1") + "ms";
            string result = status + " " + _TestName + " (" + ms + ")";

            if (!Passed && !String.IsNullOrEmpty(ErrorMessage))
            {
                result += " - " + ErrorMessage;
            }

            return result;
        }

        #endregion
    }
}

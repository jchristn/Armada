namespace Test.Shared.Infrastructure
{
    using System;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;

    /// <summary>
    /// Timing helpers for the Tui.Performance suite: runs an action a number of times and returns the mean and worst
    /// milliseconds, and reports measurements to standard output (and to the file named by
    /// <c>ARMADA_TUI_PERF_REPORT</c> when set) so the numbers in docs/TUI_PERFORMANCE.md can be reproduced.
    /// Thread-safe (stateless apart from the report file append).
    /// </summary>
    public static class TuiPerfProbe
    {
        #region Public-Members

        /// <summary>
        /// Environment variable naming a file that measurements are appended to.
        /// </summary>
        public const string ReportEnvironmentVariable = "ARMADA_TUI_PERF_REPORT";

        #endregion

        #region Private-Members

        private static readonly object _Lock = new object();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run an action <paramref name="iterations"/> times after one warm-up run.
        /// </summary>
        /// <param name="iterations">Timed runs (at least 1).</param>
        /// <param name="action">Action.</param>
        /// <returns>Mean and worst milliseconds.</returns>
        public static TuiPerfSample Measure(int iterations, Action action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            int runs = Math.Max(1, iterations);
            action();
            double total = 0;
            double worst = 0;
            Stopwatch sw = new Stopwatch();
            for (int i = 0; i < runs; i++)
            {
                sw.Restart();
                action();
                sw.Stop();
                double ms = sw.Elapsed.TotalMilliseconds;
                total += ms;
                if (ms > worst) worst = ms;
            }

            return new TuiPerfSample(total / runs, worst, runs);
        }

        /// <summary>
        /// Time one run of an action (no warm-up).
        /// </summary>
        /// <param name="action">Action.</param>
        /// <returns>Milliseconds.</returns>
        public static double Once(Action action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            Stopwatch sw = Stopwatch.StartNew();
            action();
            sw.Stop();
            return sw.Elapsed.TotalMilliseconds;
        }

        /// <summary>
        /// Report a measurement.
        /// </summary>
        /// <param name="name">Measurement name.</param>
        /// <param name="value">Value.</param>
        /// <param name="unit">Unit.</param>
        public static void Report(string name, double value, string unit)
        {
            string line = "[tui-perf] " + name + ": " + value.ToString("0.###", CultureInfo.InvariantCulture) + " " + unit;
            Console.WriteLine(line);
            string? path = Environment.GetEnvironmentVariable(ReportEnvironmentVariable);
            if (String.IsNullOrWhiteSpace(path)) return;
            lock (_Lock)
            {
                File.AppendAllText(path!, line + Environment.NewLine);
            }
        }

        /// <summary>
        /// Report a sample's mean and worst.
        /// </summary>
        /// <param name="name">Measurement name.</param>
        /// <param name="sample">Sample.</param>
        public static void Report(string name, TuiPerfSample sample)
        {
            if (sample == null) throw new ArgumentNullException(nameof(sample));
            Report(name + " (mean)", sample.MeanMs, "ms");
            Report(name + " (worst)", sample.WorstMs, "ms");
        }

        #endregion
    }
}

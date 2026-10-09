namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using Armada.Core.Database.Sqlite;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;

    /// <summary>
    /// Central aggregator for every Armada test suite. Discovers all
    /// <see cref="IArmadaTestSuite"/> implementations in this assembly by reflection and
    /// builds their descriptors. Reflection-based discovery replaces the previous manual
    /// registration list, eliminating the class of bug where a written suite was never run
    /// because it was not hand-registered.
    /// </summary>
    public static class ArmadaTestSuites
    {
        #region Public-Members

        /// <summary>
        /// All discovered suites, ordered deterministically by suite identifier so runs are
        /// stable across hosts and machines.
        /// </summary>
        public static IReadOnlyList<TestSuiteDescriptor> All
        {
            get { return _Build(); }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Turn on the SQLite write audit for every test run: a write transaction committed by a flow that does not
        /// hold the database's write gate fails, so a provider write path that bypasses the gate fails its test.
        /// </summary>
        static ArmadaTestSuites()
        {
            SqliteWriteGate.StrictAudit = true;
        }

        #endregion

        #region Private-Methods

        private static IReadOnlyList<TestSuiteDescriptor> _Build()
        {
            List<TestSuiteDescriptor> descriptors = new List<TestSuiteDescriptor>();

            IEnumerable<Type> suiteTypes = typeof(ArmadaTestSuites).Assembly
                .GetTypes()
                .Where(t => !t.IsAbstract && !t.IsInterface && typeof(IArmadaTestSuite).IsAssignableFrom(t));

            foreach (Type suiteType in suiteTypes)
            {
                ConstructorInfo? ctor = suiteType.GetConstructor(Type.EmptyTypes);
                if (ctor == null)
                {
                    throw new InvalidOperationException(
                        "Test suite " + suiteType.FullName + " must expose a public parameterless constructor.");
                }

                IArmadaTestSuite suite = (IArmadaTestSuite)ctor.Invoke(Array.Empty<object>());
                descriptors.Add(suite.Build());
            }

            List<TestSuiteDescriptor> ordered = descriptors
                .OrderBy(d => d.SuiteId, StringComparer.Ordinal)
                .ToList();

            // Optional diagnostic/CI filter: ARMADA_TEST_SUITES=E2E,Database restricts the run
            // to suites whose SuiteId starts with one of the comma-separated prefixes.
            string? filter = Environment.GetEnvironmentVariable("ARMADA_TEST_SUITES");
            if (!String.IsNullOrWhiteSpace(filter))
            {
                string[] prefixes = filter.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                ordered = ordered
                    .Where(d => prefixes.Any(p => d.SuiteId.StartsWith(p.Trim(), StringComparison.OrdinalIgnoreCase)))
                    .ToList();
            }

            return ordered;
        }

        #endregion
    }
}

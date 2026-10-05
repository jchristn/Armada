namespace Test.Shared.Suites.Client
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Text;
    using System.Text.RegularExpressions;
    using Test.Shared.Infrastructure;
    using Test.Shared.Infrastructure.ApiSurface;
    using Test.Shared.Suites.Tui;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Every public Armada.Client API method is called by a Client.Contract.* suite (the live-server contract tests in
    /// src/Test.Shared/Suites/Client/ClientContract*.cs) or listed here with where it is covered instead. A new client
    /// method without a contract test fails, and so does a stale list entry.
    /// </summary>
    public sealed class ClientCoverageSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string Suite = "Client.Coverage";

        private const string Destructive = "Destructive to the shared live test server; request and reply shape covered by Client.Destructive (recording stub), the route by Client.RouteSurface, and the screen flow by Tui.Admin.ServerSettings";

        /// <summary>
        /// Client methods not called by a live contract suite, with where they are covered.
        /// </summary>
        private static readonly Dictionary<string, string> _CoveredElsewhere = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["StopServerAsync"] = Destructive,
            ["RestartServerAsync"] = Destructive,
            ["ResetServerAsync"] = Destructive,
            ["RebuildServerAsync"] = Destructive,
            ["RollbackServerAsync"] = Destructive,
            ["RestoreBackupAsync"] = Destructive + "; the live restore path is drilled by E2E.BackupRestoreDrill"
        };

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "every_method_exercised", "Every client method is exercised against the live server or listed as covered elsewhere", () =>
            {
                List<string> methods = ClientRouteMap.ApiMethods().Select(m => m.Name).Distinct().ToList();
                string folder = Path.Combine(ApiSurfaceFiles.FindRepositoryRoot(), "src", "Test.Shared", "Suites", "Client");
                HashSet<string> called = new HashSet<string>(StringComparer.Ordinal);
                foreach (string file in Directory.GetFiles(folder, "ClientContract*.cs"))
                {
                    foreach (Match match in Regex.Matches(File.ReadAllText(file), @"\.(\w+Async)\("))
                    {
                        called.Add(match.Groups[1].Value);
                    }
                }

                StringBuilder problems = new StringBuilder();
                foreach (string method in methods.OrderBy(m => m, StringComparer.Ordinal))
                {
                    bool exercised = called.Contains(method);
                    bool listed = _CoveredElsewhere.ContainsKey(method);
                    if (!exercised && !listed) problems.Append("not exercised by a Client.Contract suite: ").Append(method).Append('\n');
                    if (exercised && listed) problems.Append("stale entry (a contract suite calls it now): ").Append(method).Append('\n');
                }

                foreach (string listed in _CoveredElsewhere.Keys)
                {
                    if (!methods.Contains(listed)) problems.Append("stale entry (no such client method): ").Append(listed).Append('\n');
                }

                if (problems.Length > 0) throw new AssertionException("Client contract coverage:\n" + problems.ToString());
                AssertTrue(methods.Count - _CoveredElsewhere.Count >= 300, "live-exercised methods: " + (methods.Count - _CoveredElsewhere.Count));
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "Armada.Client contract coverage", cases: cases);
        }

        #endregion
    }
}

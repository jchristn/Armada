namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Models;
    using Armada.Core.Services.Health;
    using Armada.Proxy.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="PathContainment"/> and the proxy's <see cref="DashboardAssetResolver"/>: descendants
    /// are inside, while sibling directories that share a name prefix, parents, and rooted inputs are not (the case a
    /// string prefix check gets wrong).
    /// </summary>
    public sealed class PathContainmentSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.PathContainment";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the path containment suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("descendants_are_inside", "Descendants are inside; the root only when allowed", TestTags.Positive, () =>
            {
                string root = Path.Combine(TestTemp.NewDirectory("containment"), "app");
                AssertTrue(PathContainment.IsInside(root, Path.Combine(root, "a", "b.txt")), "nested file");
                AssertTrue(PathContainment.IsInside(root, Path.Combine(root, "..foo")), "a name starting with two dots is not a parent reference");
                AssertTrue(PathContainment.IsInside(root, Path.Combine(root, "a", "..", "b")), "dot segments resolve inside");
                AssertFalse(PathContainment.IsInside(root, root), "root excluded by default");
                AssertTrue(PathContainment.IsInside(root, root + Path.DirectorySeparatorChar, allowRoot: true), "root allowed when asked");
                AssertEqual(Path.Combine(root, "x", "y.css"), PathContainment.ResolveInside(root, Path.Combine("x", "y.css")));
            }));

            cases.Add(Case("siblings_parents_and_rooted_inputs_are_outside", "Sibling prefix directories, parents and rooted inputs are outside", TestTags.Negative, () =>
            {
                string parent = TestTemp.NewDirectory("containment");
                string root = Path.Combine(parent, "app");
                string sibling = Path.Combine(parent, "app-secrets", "key.pem");

                AssertTrue(sibling.StartsWith(root, StringComparison.OrdinalIgnoreCase), "precondition: the sibling shares the root's string prefix");
                AssertFalse(PathContainment.IsInside(root, sibling), "sibling with a shared prefix is outside");
                AssertFalse(PathContainment.IsInside(root, parent), "parent is outside");
                AssertFalse(PathContainment.IsInside(root, Path.Combine(root, "..", "app-secrets", "key.pem")), "climbing out is outside");
                AssertNull(PathContainment.ResolveInside(root, Path.Combine("..", "app-secrets", "key.pem")), "relative escape refused");
                AssertNull(PathContainment.ResolveInside(root, sibling), "rooted input refused");
                AssertNull(PathContainment.ResolveInside(root, ""), "empty input refused");
            }));

            cases.Add(Case("dashboard_asset_resolver_stays_inside_directory", "Dashboard asset resolver stays inside the dashboard directory", TestTags.Negative, () =>
            {
                string parent = TestTemp.NewDirectory("dashassets");
                string dashboard = Path.Combine(parent, "dashboard");
                Directory.CreateDirectory(Path.Combine(dashboard, "assets"));
                Directory.CreateDirectory(Path.Combine(parent, "dashboard-secrets"));

                AssertEqual(Path.Combine(dashboard, "index.html"), DashboardAssetResolver.Resolve(dashboard, UrlPathCanonicalizer.Canonicalize("/dashboard")));
                AssertEqual(Path.Combine(dashboard, "index.html"), DashboardAssetResolver.Resolve(dashboard, UrlPathCanonicalizer.Canonicalize("/dashboard/")));
                AssertEqual(Path.Combine(dashboard, "assets", "app.js"), DashboardAssetResolver.Resolve(dashboard, UrlPathCanonicalizer.Canonicalize("/dashboard/assets/app.js")));

                string[] escapes = new string[]
                {
                    "/dashboard/..%2f..%2fdashboard-secrets/key.pem",
                    "/dashboard/../dashboard-secrets/key.pem",
                    "/dashboard/%2e%2e/dashboard-secrets/key.pem",
                    "/dashboard/..%5cdashboard-secrets%5ckey.pem"
                };
                foreach (string escape in escapes)
                {
                    UrlPathCanonicalizationResult canonical = UrlPathCanonicalizer.Canonicalize(escape);
                    AssertNull(DashboardAssetResolver.Resolve(dashboard, canonical), "'" + escape + "' must not resolve");
                }
            }));

            cases.Add(Case("repository_inventory_read_text_refuses_sibling_directory", "RepositoryFileInventory.ReadText refuses a sibling directory with a shared prefix", TestTags.Negative, () =>
            {
                string parent = TestTemp.NewDirectory("inventory");
                string root = Path.Combine(parent, "repo");
                string sibling = Path.Combine(parent, "repo-secrets");
                Directory.CreateDirectory(root);
                Directory.CreateDirectory(sibling);
                File.WriteAllText(Path.Combine(root, "inside.txt"), "inside");
                File.WriteAllText(Path.Combine(sibling, "key.txt"), "secret");

                RepositoryFileInventory inventory = RepositoryFileInventory.FromDirectory(root, null);
                AssertEqual("inside", inventory.ReadText("inside.txt"));
                AssertNull(inventory.ReadText("../repo-secrets/key.txt"), "sibling directory must be refused");
                AssertNull(inventory.ReadText("../repo/../repo-secrets/key.txt"), "climbing through the root must be refused");
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Path Containment",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) =>
                {
                    body();
                    return Task.CompletedTask;
                },
                tags: new List<string> { tag });
        }

        #endregion
    }
}

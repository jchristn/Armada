namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Bundled battery packs: the build output ships every repository pack, a managed battery directory (settings or
    /// ARMADA_BATTERY_DIR) is seeded from the bundle on start, and reseeding after an upgrade keeps edited and custom
    /// packs and does not recreate a bundled pack an operator deleted.
    /// </summary>
    public sealed class BatteryPackSeederSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.BatteryPackSeeder";

        private static readonly string[] _LanguagePacks = new[] { "csharp", "go", "java", "javascript", "php", "python", "ruby", "rust" };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("build_output_bundles_valid_packs", "The application's bundled battery folder holds every language pack, all valid", TestTags.Positive, () =>
            {
                string? bundled = BatteryPackCatalog.BundledDirectory(Path.GetDirectoryName(typeof(BatteryPackCatalog).Assembly.Location));
                AssertNotNull(bundled, "a battery folder with packs is next to the built Armada assemblies");
                List<BatteryPack> packs = BatteryPackLoader.LoadAll(bundled);
                foreach (string id in _LanguagePacks)
                {
                    BatteryPack? pack = packs.FirstOrDefault(p => p.Id == id);
                    AssertNotNull(pack, "bundled pack " + id);
                    AssertTrue(pack!.IsValid, id + " is valid: " + String.Join(" | ", pack.Errors));
                }
            }));

            cases.Add(Case("explicit_directory_precedence", "Settings win over ARMADA_BATTERY_DIR, which wins over the search; neither means no explicit directory", TestTags.Positive, () =>
            {
                string root = TempRoot();
                try
                {
                    string fromSettings = Path.Combine(root, "settings");
                    string fromEnvironment = Path.Combine(root, "env");
                    AssertEqual(Path.GetFullPath(fromSettings), new BatteryPackCatalog(() => fromSettings, () => fromEnvironment).ResolveDirectory(), "settings win");
                    AssertEqual(Path.GetFullPath(fromEnvironment), new BatteryPackCatalog(() => "  ", () => fromEnvironment).ResolveDirectory(), "environment used when settings are blank");
                    AssertEqual(Path.GetFullPath(fromEnvironment), new BatteryPackCatalog(() => null, () => fromEnvironment).ResolveExplicitDirectory(), "explicit environment directory");
                    AssertNull(new BatteryPackCatalog(() => null, () => "").ResolveExplicitDirectory(), "no explicit directory");
                    AssertEqual(BatteryPackCatalog.DirectoryEnvironmentVariable, "ARMADA_BATTERY_DIR", "variable name the server image sets");
                }
                finally { Delete(root); }
            }));

            cases.Add(Case("fresh_seed_copies_all_bundled_packs", "A fresh managed directory receives every bundled pack, fixture dotfiles included, and loads as valid packs", TestTags.Positive, () =>
            {
                string root = TempRoot();
                try
                {
                    string bundled = RepositoryBattery();
                    string target = Path.Combine(root, "managed");
                    BatteryPackSeedResult result = BatteryPackSeeder.Seed(bundled, target);
                    AssertNull(result.SkippedReason, "seed ran");
                    AssertEqual(0, result.Failed.Count, "no failures: " + String.Join(" | ", result.Failed));
                    foreach (string id in _LanguagePacks) AssertTrue(result.Added.Contains(id), "added " + id);

                    List<BatteryPack> packs = new BatteryPackCatalog(() => target, () => null).LoadAll();
                    AssertEqual(result.Added.Count, packs.Count, "every seeded pack loads; the marker file is not a pack");
                    AssertTrue(packs.All(p => p.IsValid), "seeded packs are valid");
                    AssertEqual(
                        File.ReadAllText(Path.Combine(bundled, "csharp", BatteryPackLoader.ManifestFileName)),
                        File.ReadAllText(Path.Combine(target, "csharp", BatteryPackLoader.ManifestFileName)),
                        "manifest copied byte for byte");
                    AssertTrue(File.Exists(Path.Combine(target, "csharp", "fixture", ".gitignore")), "fixture dotfiles are copied");
                    AssertFalse(Directory.GetDirectories(target).Any(d => Path.GetFileName(d).StartsWith(".", StringComparison.Ordinal)), "no staging folders remain");
                }
                finally { Delete(root); }
            }));

            cases.Add(Case("reseed_preserves_custom_and_edited_packs", "Reseeding (an upgrade) keeps edited and custom packs, does not recreate a deleted bundled pack, and adds a newly bundled one", TestTags.Positive, () =>
            {
                string root = TempRoot();
                try
                {
                    string bundled = Path.Combine(root, "bundled");
                    WritePack(bundled, "alpha", "{\"id\":\"alpha\",\"version\":1}");
                    WritePack(bundled, "beta", "{\"id\":\"beta\",\"version\":1}");
                    string target = Path.Combine(root, "managed");
                    BatteryPackSeedResult first = BatteryPackSeeder.Seed(bundled, target);
                    AssertEqual("alpha,beta", String.Join(",", first.Added), "first start seeds both");

                    // Operator edits alpha, deletes beta and adds a custom pack.
                    File.WriteAllText(Path.Combine(target, "alpha", BatteryPackLoader.ManifestFileName), "{\"id\":\"alpha\",\"version\":7}");
                    Directory.Delete(Path.Combine(target, "beta"), true);
                    WritePack(target, "custom", "{\"id\":\"custom\",\"version\":1}");

                    // Upgrade: the bundle changes alpha and gains gamma.
                    File.WriteAllText(Path.Combine(bundled, "alpha", BatteryPackLoader.ManifestFileName), "{\"id\":\"alpha\",\"version\":2}");
                    WritePack(bundled, "gamma", "{\"id\":\"gamma\",\"version\":1}");
                    BatteryPackSeedResult second = BatteryPackSeeder.Seed(bundled, target);
                    AssertEqual("gamma", String.Join(",", second.Added), "only the newly bundled pack is added");
                    AssertEqual("alpha", String.Join(",", second.Preserved), "the edited pack is preserved");
                    AssertEqual("beta", String.Join(",", second.Retired), "the deleted pack stays deleted");
                    AssertContains("\"version\":7", File.ReadAllText(Path.Combine(target, "alpha", BatteryPackLoader.ManifestFileName)), "edit kept");
                    AssertFalse(Directory.Exists(Path.Combine(target, "beta")), "beta not recreated");
                    AssertTrue(File.Exists(Path.Combine(target, "custom", BatteryPackLoader.ManifestFileName)), "custom pack kept");

                    BatteryPackSeedResult third = BatteryPackSeeder.Seed(bundled, target);
                    AssertEqual(0, third.Added.Count, "repeat start adds nothing");
                }
                finally { Delete(root); }
            }));

            cases.Add(Case("seed_skips_without_target_or_bundle", "No target, no bundle, or the bundle itself as target: nothing is written", TestTags.Negative, () =>
            {
                string root = TempRoot();
                try
                {
                    string bundled = Path.Combine(root, "bundled");
                    WritePack(bundled, "alpha", "{\"id\":\"alpha\"}");
                    AssertNotNull(BatteryPackSeeder.Seed(bundled, null).SkippedReason, "no target");
                    AssertNotNull(BatteryPackSeeder.Seed(Path.Combine(root, "missing"), Path.Combine(root, "t1")).SkippedReason, "no bundle");
                    AssertFalse(Directory.Exists(Path.Combine(root, "t1")), "target not created without a bundle");
                    AssertNotNull(BatteryPackSeeder.Seed(bundled, bundled + Path.DirectorySeparatorChar).SkippedReason, "bundle as target");
                    AssertFalse(File.Exists(Path.Combine(bundled, BatteryPackSeeder.MarkerFileName)), "no marker written into the bundle");

                    string blocked = Path.Combine(root, "blocked");
                    File.WriteAllText(blocked, "a file, not a directory");
                    BatteryPackSeedResult result = BatteryPackSeeder.Seed(bundled, blocked);
                    AssertNotNull(result.SkippedReason, "an unusable target is reported, not thrown");
                }
                finally { Delete(root); }
            }));

            cases.Add(Case("seed_removes_stale_staging", "A staging folder left by an interrupted seed is removed and the pack is seeded", TestTags.Negative, () =>
            {
                string root = TempRoot();
                try
                {
                    string bundled = Path.Combine(root, "bundled");
                    WritePack(bundled, "alpha", "{\"id\":\"alpha\"}");
                    string target = Path.Combine(root, "managed");
                    WritePack(target, ".seeding-alpha-0123", "{\"id\":\"alpha\"}");
                    BatteryPackSeedResult result = BatteryPackSeeder.Seed(bundled, target);
                    AssertEqual("alpha", String.Join(",", result.Added), "seeded");
                    AssertFalse(Directory.Exists(Path.Combine(target, ".seeding-alpha-0123")), "stale staging removed");
                }
                finally { Delete(root); }
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Battery: bundled packs ship with the build and seed a managed directory without overwriting",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static string RepositoryBattery()
        {
            string? directory = new BatteryPackCatalog(() => null, () => null).ResolveDirectory();
            AssertNotNull(directory, "a battery folder is found from the test host");
            return directory!;
        }

        private static string TempRoot()
        {
            string root = Path.Combine(Path.GetTempPath(), "armada-seed-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        private static void WritePack(string battery, string id, string manifest)
        {
            string directory = Path.Combine(battery, id);
            Directory.CreateDirectory(Path.Combine(directory, "fixture"));
            File.WriteAllText(Path.Combine(directory, BatteryPackLoader.ManifestFileName), manifest);
            File.WriteAllText(Path.Combine(directory, "fixture", "README.md"), id);
        }

        private static void Delete(string root)
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch (Exception) { }
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => { body(); return Task.CompletedTask; },
                tags: new List<string> { tag });
        }

        #endregion
    }
}

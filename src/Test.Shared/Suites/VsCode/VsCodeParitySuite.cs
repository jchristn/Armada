namespace Test.Shared.Suites.VsCode
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Hosting;
    using Test.Shared.Infrastructure;
    using Test.Shared.Infrastructure.ApiSurface;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Parity tracking for the VS Code extension (VSCODE_EXTENSION_PLAN.md section 6), modeled on the TUI parity suite:
    /// every dashboard route, hub tab, client.ts server call, WebSocket event and Server settings field, every TUI-only
    /// surface, every Harbor menu command and window, and the CLI commands in scope have an entry in
    /// src/Armada.VSCode/parity.json; no entry names a surface that no longer exists; and every implemented or extension
    /// entry resolves to a real contribution in the extension's package.json (a command, a view or a setting). Planned
    /// entries are allowed during the milestones; the zero-planned release gate starts at milestone V8.
    /// </summary>
    public sealed class VsCodeParitySuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string Suite = "VsCode.Parity";

        private static readonly HashSet<string> _Statuses = new HashSet<string>(StringComparer.Ordinal) { "implemented", "planned", "not-applicable", "extension" };

        private static readonly Regex _Surface = new Regex(@"^(command|view|setting):(\S+)$", RegexOptions.Compiled);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the VsCode.Parity suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("dashboard_surfaces_have_entries", "Every dashboard route, tab, client.ts server call, event and settings field has an entry", () =>
            {
                VsCodeParityManifest manifest = Load();
                List<string> routes = DashboardSource.Routes();
                List<string> tabs = DashboardSource.Tabs();
                List<string> exports = DashboardSource.ApiExports();
                AssertTrue(routes.Count >= 80, "parsed routes: " + routes.Count);
                AssertTrue(tabs.Count >= 37, "parsed tabs: " + tabs.Count);
                AssertTrue(exports.Count >= 400, "parsed client.ts exports: " + exports.Count);
                Missing(manifest, "route", routes);
                Missing(manifest, "tab", tabs);
                Missing(manifest, "api", exports);
                Missing(manifest, "event", DashboardSource.Events());
                Missing(manifest, "setting", DashboardSource.SettingsFields());
            }));

            cases.Add(Case("tui_extensions_have_entries", "Every TUI-only surface has a tui entry", () =>
            {
                VsCodeParityManifest manifest = Load();
                List<string> tuiOnly = TuiExtensionKeys();
                AssertTrue(tuiOnly.Count > 0, "the TUI manifest has extension entries");
                Missing(manifest, "tui", tuiOnly);
            }));

            cases.Add(Case("harbor_surfaces_have_entries", "Every Harbor menu command and window has a harbor entry", () =>
            {
                VsCodeParityManifest manifest = Load();
                Missing(manifest, "harbor", HarborKeys());
            }));

            cases.Add(Case("cli_entries_are_real_commands", "Every cli entry names a Helm command or command group", () =>
            {
                VsCodeParityManifest manifest = Load();
                List<string> cli = ApiSurfaceFiles.LoadBaseline().Cli.Select(c => c.Command).ToList();
                List<VsCodeParityEntry> entries = manifest.Entries.Where(e => e.Kind == "cli").ToList();
                AssertTrue(entries.Count >= 8, "cli entries: " + entries.Count);
                foreach (VsCodeParityEntry e in entries)
                {
                    bool exists = cli.Any(c => c == e.Key || c.StartsWith(e.Key + " ", StringComparison.Ordinal));
                    AssertTrue(exists, "Helm command or group for cli entry " + e.Key);
                }
            }));

            cases.Add(Case("no_stale_entries", "Every entry names a surface that still exists", () =>
            {
                VsCodeParityManifest manifest = Load();
                Dictionary<string, HashSet<string>> surfaces = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
                {
                    ["route"] = new HashSet<string>(DashboardSource.Routes(), StringComparer.Ordinal),
                    ["tab"] = new HashSet<string>(DashboardSource.Tabs(), StringComparer.Ordinal),
                    ["api"] = new HashSet<string>(DashboardSource.ApiExports(), StringComparer.Ordinal),
                    ["event"] = new HashSet<string>(DashboardSource.Events(), StringComparer.Ordinal),
                    ["setting"] = new HashSet<string>(DashboardSource.SettingsFields(), StringComparer.Ordinal),
                    ["tui"] = new HashSet<string>(TuiExtensionKeys(), StringComparer.Ordinal),
                    ["harbor"] = new HashSet<string>(HarborKeys(), StringComparer.Ordinal),
                };
                List<string> stale = manifest.Entries
                    .Where(e => surfaces.ContainsKey(e.Kind) && !surfaces[e.Kind].Contains(e.Key))
                    .Select(e => e.Kind + " " + e.Key).ToList();
                List<string> unknownKinds = manifest.Entries
                    .Where(e => !surfaces.ContainsKey(e.Kind) && e.Kind != "cli" && e.Kind != "vscode")
                    .Select(e => e.Kind + " " + e.Key).ToList();
                if (stale.Count > 0) throw new AssertionException("parity.json entries for surfaces that no longer exist (run scripts/vscode/generate-parity-manifest.py): " + String.Join(", ", stale));
                if (unknownKinds.Count > 0) throw new AssertionException("parity.json entries of an unknown kind: " + String.Join(", ", unknownKinds));
            }));

            cases.Add(Case("entries_are_well_formed", "Statuses are known; not-applicable and extension entries say why", () =>
            {
                VsCodeParityManifest manifest = Load();
                List<string> problems = Problems(manifest, false);
                if (problems.Count > 0) throw new AssertionException("parity.json problems: " + String.Join("; ", problems));
                AssertTrue(manifest.Entries.Count(e => e.Status == "implemented") > 0, "some entries are implemented (the black box)");
            }));

            cases.Add(Case("implemented_entries_resolve", "Implemented and extension entries resolve to a command, view or setting in package.json", () =>
            {
                VsCodeParityManifest manifest = Load();
                VsCodePackageManifest package = LoadPackage();
                HashSet<string> commands = new HashSet<string>(package.Contributes.Commands.Select(c => c.Command), StringComparer.Ordinal);
                HashSet<string> views = new HashSet<string>(package.Contributes.Views.Values.SelectMany(v => v).Select(v => v.Id), StringComparer.Ordinal);
                HashSet<string> settings = new HashSet<string>(package.Contributes.Configuration.Properties.Keys, StringComparer.Ordinal);
                List<string> unresolved = new List<string>();
                foreach (VsCodeParityEntry e in manifest.Entries.Where(x => x.Status == "implemented" || x.Status == "extension"))
                {
                    Match m = _Surface.Match(e.Surface);
                    bool ok = m.Success && (m.Groups[1].Value switch
                    {
                        "command" => commands.Contains(m.Groups[2].Value),
                        "view" => views.Contains(m.Groups[2].Value),
                        _ => settings.Contains(m.Groups[2].Value),
                    });
                    if (!ok) unresolved.Add(e.Kind + " " + e.Key + " -> '" + e.Surface + "'");
                }

                if (unresolved.Count > 0) throw new AssertionException("parity.json surfaces with no package.json contribution: " + String.Join(", ", unresolved));
            }));

            cases.Add(Case("release_gate_rejects", "The checks report a bad status, a missing surface, missing notes, and (at the release gate) planned entries", () =>
            {
                VsCodeParityManifest manifest = new VsCodeParityManifest();
                manifest.Entries.Add(new VsCodeParityEntry { Kind = "route", Key = "/a", Status = "implemented", Surface = "command:armada.x" });
                manifest.Entries.Add(new VsCodeParityEntry { Kind = "route", Key = "/b", Status = "planned" });
                manifest.Entries.Add(new VsCodeParityEntry { Kind = "api", Key = "c", Status = "implemented" });
                manifest.Entries.Add(new VsCodeParityEntry { Kind = "harbor", Key = "menu:Undo", Status = "not-applicable", Notes = " " });
                manifest.Entries.Add(new VsCodeParityEntry { Kind = "vscode", Key = "d", Status = "done" });
                AssertEqual("implemented without a command:, view: or setting: surface: api c|not-applicable without notes: harbor menu:Undo|unknown status done: vscode d", String.Join("|", Problems(manifest, false)), "milestone problems");
                AssertTrue(Problems(manifest, true).Contains("planned: route /b"), "the release gate reports planned entries");
            }));

            cases.Add(Case("extension_id_lives_in_package_json", "The extension ID is read from package.json, and the integration test does not hard-code it", () =>
            {
                VsCodePackageManifest package = LoadPackage();
                AssertTrue(Regex.IsMatch(package.Publisher, "^[A-Za-z0-9][A-Za-z0-9-]*$"), "publisher id: " + package.Publisher);
                AssertTrue(Regex.IsMatch(package.Name, "^[a-z0-9-]+$"), "extension name: " + package.Name);
                string id = package.Publisher + "." + package.Name;
                string tests = Path.Combine(DashboardSource.RepoRoot(), "src", "Armada.VSCode", "test");
                List<string> hardCoded = Directory.EnumerateFiles(tests, "*.ts", SearchOption.AllDirectories)
                    .Where(f => File.ReadAllText(f).Contains("'" + id + "'", StringComparison.Ordinal))
                    .ToList();
                AssertTrue(hardCoded.Count == 0, "tests that hard-code " + id + ": " + String.Join(", ", hardCoded));
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "VS Code parity manifest", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string displayName, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: Suite,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => { body(); return Task.CompletedTask; },
                tags: new List<string> { TestTags.Positive });
        }

        private static VsCodeParityManifest Load()
        {
            string path = Path.Combine(DashboardSource.RepoRoot(), "src", "Armada.VSCode", "parity.json");
            return JsonHelper.Deserialize<VsCodeParityManifest>(File.ReadAllText(path));
        }

        private static VsCodePackageManifest LoadPackage()
        {
            string path = Path.Combine(DashboardSource.RepoRoot(), "src", "Armada.VSCode", "package.json");
            return JsonHelper.Deserialize<VsCodePackageManifest>(File.ReadAllText(path));
        }

        private static List<string> TuiExtensionKeys()
        {
            string path = Path.Combine(DashboardSource.RepoRoot(), "src", "Armada.Tui", "parity.json");
            ParityManifest tui = JsonHelper.Deserialize<ParityManifest>(File.ReadAllText(path));
            return tui.Entries.Where(e => e.Status == "extension").Select(e => e.Kind + ":" + e.Key).ToList();
        }

        private static List<string> HarborKeys()
        {
            List<string> keys = Enum.GetNames(typeof(HarborMenuCommandEnum)).Select(n => "menu:" + n).ToList();
            string harbor = Path.Combine(DashboardSource.RepoRoot(), "src", "Armada.Harbor");
            Regex window = new Regex(@"\bclass\s+(\w+)\s*:\s*Window\b");
            List<string> windows = Directory.EnumerateFiles(harbor, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                    && !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                .SelectMany(f => window.Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value))
                .Distinct()
                .Select(n => "window:" + n)
                .ToList();
            AssertTrue(windows.Count >= 3, "Harbor window classes: " + windows.Count);
            keys.AddRange(windows);
            return keys;
        }

        /// <summary>
        /// Manifest problems: an unknown status, an implemented or extension entry without a surface, a not-applicable or
        /// extension entry without notes, and (with forbidPlanned, the release gate) a planned entry.
        /// </summary>
        /// <param name="manifest">Manifest.</param>
        /// <param name="forbidPlanned">True at the release gate.</param>
        /// <returns>Problems, empty when well formed.</returns>
        private static List<string> Problems(VsCodeParityManifest manifest, bool forbidPlanned)
        {
            List<string> problems = new List<string>();
            foreach (VsCodeParityEntry e in manifest.Entries)
            {
                if (!_Statuses.Contains(e.Status))
                {
                    problems.Add("unknown status " + e.Status + ": " + e.Kind + " " + e.Key);
                    continue;
                }

                if ((e.Status == "implemented" || e.Status == "extension") && !_Surface.IsMatch(e.Surface ?? ""))
                    problems.Add(e.Status + " without a command:, view: or setting: surface: " + e.Kind + " " + e.Key);
                if ((e.Status == "not-applicable" || e.Status == "extension") && String.IsNullOrWhiteSpace(e.Notes))
                    problems.Add(e.Status + " without notes: " + e.Kind + " " + e.Key);
                if (forbidPlanned && e.Status == "planned") problems.Add("planned: " + e.Kind + " " + e.Key);
            }

            return problems;
        }

        private static void Missing(VsCodeParityManifest manifest, string kind, IEnumerable<string> keys)
        {
            HashSet<string> present = new HashSet<string>(manifest.Entries.Where(e => e.Kind == kind).Select(e => e.Key), StringComparer.Ordinal);
            List<string> missing = keys.Where(k => !present.Contains(k)).Distinct().ToList();
            if (missing.Count > 0)
                throw new AssertionException(kind + " surfaces with no src/Armada.VSCode/parity.json entry (run scripts/vscode/generate-parity-manifest.py): " + String.Join(", ", missing));
        }

        #endregion
    }
}

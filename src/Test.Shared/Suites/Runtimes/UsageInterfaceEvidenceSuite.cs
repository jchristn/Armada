namespace Test.Shared.Suites.Runtimes
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Protocol;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// AC0 (Usage.InterfaceEvidence): docs/captain-usage-plan/PROBE_INTERFACES.md records the verified,
    /// read-only, non-prompt usage interface for all eight runtimes, every sanitized fixture under
    /// src/Test.Shared/Fixtures/Usage is inventoried with its provenance, an unavailable interface is
    /// stated as unavailable instead of being invented, and the fixtures that mirror an existing Armada
    /// parser still parse with it.
    /// </summary>
    public sealed class UsageInterfaceEvidenceSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Usage.InterfaceEvidence";

        private static readonly string[] RequiredRuntimes =
        {
            "ClaudeCode",
            "Codex",
            "Gemini",
            "Cursor",
            "Mux",
            "OpenCode",
            "ApiEndpoint",
            "Custom"
        };

        private static readonly string[] RequiredEvidenceFields =
        {
            "- Interface:",
            "- Timestamps:",
            "- Auth mode:",
            "- Confidence:",
            "- Fixtures:"
        };

        private static readonly string[] ConfidenceLabels =
        {
            "Official",
            "Undocumented",
            "Estimated",
            "Unavailable",
            "Derived"
        };

        private static readonly string[] ProvenanceValues =
        {
            "InstalledSchema",
            "InstalledBinaryString",
            "ArmadaSource",
            "VendorDocumented"
        };

        private static readonly string[] ObservedProvenance =
        {
            "InstalledSchema",
            "InstalledBinaryString"
        };

        private static readonly string[] UnavailableRuntimes =
        {
            "Gemini",
            "Cursor",
            "OpenCode"
        };

        /// <summary>
        /// Runtimes with no usage interface at all. Gemini keeps a fixture for the exhaustion error it
        /// emits, which is not an account-limit read, so it is not in this list.
        /// </summary>
        private static readonly string[] RuntimesWithoutInterfaceFixture =
        {
            "Cursor",
            "OpenCode"
        };

        private static readonly Regex LongHexPattern = new Regex("[0-9a-fA-F]{24,}", RegexOptions.Compiled);

        private static readonly string[] ForbiddenSecretMarkers =
        {
            "sk-",
            "Bearer",
            "eyJ",
            "AKIA",
            "@",
            "/Users/",
            "/home/",
            "C:\\\\"
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(new TestCaseDescriptor(SuiteId, "doc_records_all_eight_runtimes", "PROBE_INTERFACES.md carries interface, timestamp, auth mode, confidence and fixture evidence for each of the eight runtimes", (CancellationToken ct) =>
            {
                Dictionary<string, string> sections = ReadDocSections();
                List<string> problems = new List<string>();

                foreach (string name in Enum.GetNames(typeof(AgentRuntimeEnum)))
                {
                    if (name == "Pi") continue;
                    if (!RequiredRuntimes.Contains(name)) problems.Add("AgentRuntimeEnum value " + name + " has no phase 0 evidence row");
                }

                foreach (string runtime in RequiredRuntimes)
                {
                    if (!sections.TryGetValue(runtime, out string? body))
                    {
                        problems.Add("PROBE_INTERFACES.md has no '### " + runtime + "' section");
                        continue;
                    }

                    foreach (string field in RequiredEvidenceFields)
                    {
                        if (!body.Contains(field, StringComparison.Ordinal)) problems.Add(runtime + ": missing evidence line '" + field + "'");
                    }

                    string? confidence = ReadFieldLine(body, "- Confidence:");
                    if (confidence == null) continue;
                    if (!ConfidenceLabels.Any(label => confidence.StartsWith(label, StringComparison.Ordinal)))
                    {
                        problems.Add(runtime + ": confidence '" + confidence + "' does not start with an allowed label");
                    }
                }

                AssertTrue(problems.Count == 0, "usage interface evidence is incomplete:\n" + String.Join("\n", problems));
                return Task.CompletedTask;
            }));

            cases.Add(new TestCaseDescriptor(SuiteId, "fixtures_are_inventoried_and_sanitized", "Every usage fixture is listed in manifest.json with a known provenance, a sanitized name and no credential-shaped content", (CancellationToken ct) =>
            {
                string directory = FixtureDirectory();
                AssertTrue(Directory.Exists(directory), "usage fixture directory is missing: " + directory);

                UsageFixtureManifest manifest = ReadManifest();
                List<string> problems = new List<string>();

                List<string> files = Directory
                    .GetFiles(directory)
                    .Where(f => String.Equals(Path.GetExtension(f), ".json", StringComparison.OrdinalIgnoreCase)
                        || String.Equals(Path.GetExtension(f), ".jsonl", StringComparison.OrdinalIgnoreCase))
                    .Where(f => !String.Equals(Path.GetFileName(f), "manifest.json", StringComparison.Ordinal))
                    .OrderBy(f => f, StringComparer.Ordinal)
                    .ToList();

                foreach (string file in files)
                {
                    string name = Path.GetFileName(file);
                    if (!manifest.Fixtures.Any(e => String.Equals(e.File, name, StringComparison.Ordinal))) problems.Add(name + " is not listed in manifest.json");
                }

                foreach (UsageFixtureEntry entry in manifest.Fixtures)
                {
                    if (String.IsNullOrWhiteSpace(entry.File)) { problems.Add("a fixture entry has no file name"); continue; }
                    if (!File.Exists(Path.Combine(directory, entry.File))) { problems.Add(entry.File + " is listed but missing"); continue; }
                    if (!entry.File.Contains(".sanitized.", StringComparison.Ordinal)) problems.Add(entry.File + " is not named as sanitized");
                    if (!RequiredRuntimes.Contains(entry.Runtime)) problems.Add(entry.File + " names an unknown runtime '" + entry.Runtime + "'");
                    if (!ProvenanceValues.Contains(entry.Provenance)) problems.Add(entry.File + " has an unknown provenance '" + entry.Provenance + "'");
                    if (String.IsNullOrWhiteSpace(entry.Interface)) problems.Add(entry.File + " documents no interface");
                    if (String.IsNullOrWhiteSpace(entry.Notes)) problems.Add(entry.File + " has no notes explaining what it proves");
                    if (entry.Verified && !ObservedProvenance.Contains(entry.Provenance)) problems.Add(entry.File + " claims verification from unobserved provenance '" + entry.Provenance + "'");
                }

                List<string> scanned = new List<string>(files);
                scanned.Add(Path.Combine(directory, "manifest.json"));

                foreach (string file in scanned)
                {
                    string text = File.ReadAllText(file);
                    foreach (string marker in ForbiddenSecretMarkers)
                    {
                        if (text.Contains(marker, StringComparison.Ordinal)) problems.Add(Path.GetFileName(file) + " contains the forbidden marker '" + marker + "'");
                    }

                    if (LongHexPattern.IsMatch(text)) problems.Add(Path.GetFileName(file) + " contains a long hex string that could be a real identifier");
                }

                AssertTrue(problems.Count == 0, "usage fixtures are not sanitized or inventoried:\n" + String.Join("\n", problems));
                return Task.CompletedTask;
            }));

            cases.Add(new TestCaseDescriptor(SuiteId, "fixtures_parse_with_existing_armada_parsers", "The Claude rate-limit and Mux run usage fixtures deserialize with the production parser types Armada already ships", (CancellationToken ct) =>
            {
                string directory = FixtureDirectory();

                string[] claudeLines = File.ReadAllLines(Path.Combine(directory, "claude-rate-limit-event.sanitized.jsonl"))
                    .Where(line => !String.IsNullOrWhiteSpace(line))
                    .ToArray();
                AssertTrue(claudeLines.Length == 2, "the Claude fixture should hold two rate_limit_event lines, found " + claudeLines.Length);

                AssertTrue(ClaudeRateLimitEvent.TryParse(claudeLines[0], out ClaudeRateLimitEvent? warning), "the Claude fixture line is not a rate_limit_event");
                AssertNotNull(warning!.Info, "the Claude fixture line has no rate_limit_info");
                AssertEqual("allowed_warning", warning.Info!.Status);
                AssertEqual("five_hour", warning.Info.RateLimitType);
                AssertEqual(0.62d, warning.Info.Utilization!.Value);
                AssertEqual(1793468700L, warning.Info.ResetsAt!.Value);

                AssertTrue(ClaudeRateLimitEvent.TryParse(claudeLines[1], out ClaudeRateLimitEvent? allowed), "the second Claude fixture line is not a rate_limit_event");
                AssertEqual("seven_day", allowed!.Info!.RateLimitType);

                string muxJson = File.ReadAllText(Path.Combine(directory, "mux-run-completed-usage.sanitized.json"));
                MuxRunUsage? mux = JsonSerializer.Deserialize<MuxRunUsage>(muxJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                AssertNotNull(mux, "the Mux usage fixture did not deserialize");
                AssertEqual(1200L, mux!.InputTokens!.Value);
                AssertEqual(1540L, mux.TotalTokens!.Value);
                AssertEqual(0.0042d, mux.CostUsd!.Value);
                AssertNull(mux.EstimatedTokens, "the Mux fixture should leave estimatedTokens null");
                return Task.CompletedTask;
            }));

            cases.Add(new TestCaseDescriptor(SuiteId, "unavailable_interfaces_are_explicit", "Gemini, Cursor and OpenCode are recorded as unavailable with evidence and retry conditions, and no fixture pretends to be their interface", (CancellationToken ct) =>
            {
                UsageFixtureManifest manifest = ReadManifest();
                Dictionary<string, string> sections = ReadDocSections();
                List<string> problems = new List<string>();

                foreach (string runtime in UnavailableRuntimes)
                {
                    UsageUnavailableEntry? entry = manifest.Unavailable.FirstOrDefault(e => String.Equals(e.Runtime, runtime, StringComparison.Ordinal));
                    if (entry == null) { problems.Add(runtime + " has no unavailable entry in manifest.json"); }
                    else
                    {
                        if (String.IsNullOrWhiteSpace(entry.Reason)) problems.Add(runtime + ": no reason");
                        if (String.IsNullOrWhiteSpace(entry.Evidence)) problems.Add(runtime + ": no evidence");
                        if (String.IsNullOrWhiteSpace(entry.RetryCondition)) problems.Add(runtime + ": no retry condition");
                    }

                    if (RuntimesWithoutInterfaceFixture.Contains(runtime)
                        && manifest.Fixtures.Any(e => String.Equals(e.Runtime, runtime, StringComparison.Ordinal)))
                    {
                        problems.Add(runtime + " has no usage interface at all but carries a fixture, which would imply one");
                    }

                    if (!sections.TryGetValue(runtime, out string? body) || !body.Contains("Unavailable", StringComparison.Ordinal))
                    {
                        problems.Add(runtime + ": PROBE_INTERFACES.md does not state that the interface is Unavailable");
                    }
                }

                AssertTrue(problems.Count == 0, "unavailable usage interfaces are not explicit:\n" + String.Join("\n", problems));
                return Task.CompletedTask;
            }));

            cases.Add(new TestCaseDescriptor(SuiteId, "doc_is_ascii_and_free_of_credentials", "PROBE_INTERFACES.md is ASCII-only and contains no credential, token or absolute home path", (CancellationToken ct) =>
            {
                string path = DocPath();
                string text = File.ReadAllText(path);
                List<string> problems = new List<string>();

                foreach (char c in text)
                {
                    if (c > 127) { problems.Add("non-ASCII character U+" + ((int)c).ToString("X4", System.Globalization.CultureInfo.InvariantCulture) + " in PROBE_INTERFACES.md"); break; }
                }

                foreach (string marker in ForbiddenSecretMarkers)
                {
                    if (text.Contains(marker, StringComparison.Ordinal)) problems.Add("PROBE_INTERFACES.md contains the forbidden marker '" + marker + "'");
                }

                if (LongHexPattern.IsMatch(text)) problems.Add("PROBE_INTERFACES.md contains a long hex string that could be a real identifier");
                if (!text.Contains("Usage.InterfaceEvidence", StringComparison.Ordinal)) problems.Add("PROBE_INTERFACES.md does not name its AC0 named check");

                AssertTrue(problems.Count == 0, "PROBE_INTERFACES.md is not clean:\n" + String.Join("\n", problems));
                return Task.CompletedTask;
            }));

            return new TestSuiteDescriptor(SuiteId, "Phase 0 usage interface evidence (AC0)", cases);
        }

        #endregion

        #region Private-Methods

        private static string DocPath()
        {
            return Path.Combine(DashboardSource.RepoRoot(), "docs", "captain-usage-plan", "PROBE_INTERFACES.md");
        }

        private static string FixtureDirectory()
        {
            return Path.Combine(DashboardSource.RepoRoot(), "src", "Test.Shared", "Fixtures", "Usage");
        }

        private static UsageFixtureManifest ReadManifest()
        {
            string path = Path.Combine(FixtureDirectory(), "manifest.json");
            AssertTrue(File.Exists(path), "the usage fixture manifest is missing: " + path);
            UsageFixtureManifest? manifest = JsonSerializer.Deserialize<UsageFixtureManifest>(
                File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            AssertNotNull(manifest, "the usage fixture manifest did not deserialize");
            AssertEqual(1, manifest!.SchemaVersion, "usage fixture manifest schema version");
            return manifest;
        }

        private static Dictionary<string, string> ReadDocSections()
        {
            string path = DocPath();
            AssertTrue(File.Exists(path), "the phase 0 evidence document is missing: " + path);

            Dictionary<string, string> sections = new Dictionary<string, string>(StringComparer.Ordinal);
            string? current = null;
            List<string> lines = new List<string>();

            foreach (string line in File.ReadAllLines(path))
            {
                if (line.StartsWith("### ", StringComparison.Ordinal))
                {
                    if (current != null) sections[current] = String.Join("\n", lines);
                    current = line.Substring(4).Trim();
                    lines = new List<string>();
                    continue;
                }

                if (line.StartsWith("#", StringComparison.Ordinal))
                {
                    if (current != null) sections[current] = String.Join("\n", lines);
                    current = null;
                    lines = new List<string>();
                    continue;
                }

                if (current != null) lines.Add(line);
            }

            if (current != null) sections[current] = String.Join("\n", lines);
            return sections;
        }

        private static string? ReadFieldLine(string body, string prefix)
        {
            foreach (string line in body.Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith(prefix, StringComparison.Ordinal)) return trimmed.Substring(prefix.Length).Trim();
            }

            return null;
        }

        #endregion
    }
}

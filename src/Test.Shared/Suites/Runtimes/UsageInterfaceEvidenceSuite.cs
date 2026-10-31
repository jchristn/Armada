namespace Test.Shared.Suites.Runtimes
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Protocol;
    using Armada.Core.Services;
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
            "- Binary:",
            "- Version:",
            "- Host OS:",
            "- Fields:",
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
                        if (String.IsNullOrWhiteSpace(ReadFieldLine(body, field))) problems.Add(runtime + ": missing or empty evidence line '" + field + "'");
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

            cases.Add(new TestCaseDescriptor(SuiteId, "codex_notification_uses_exact_wire_method", "Codex passive updates use the installed schema's method enum rather than its descriptive title", (CancellationToken ct) =>
            {
                string body = ReadDocSections()["Codex"];
                AssertContains("`account/rateLimits/updated` is the wire method", body);
                AssertContains("`Account/rateLimits/updatedNotification` is the schema title", body);
                AssertContains("`account/rateLimits/read`", body);
                AssertFalse(body.Contains("`Account/rateLimits/updatedNotification` pushes", StringComparison.Ordinal), "the schema title must not be documented as a wire method");
                string document = File.ReadAllText(DocPath());
                AssertFalse(document.Contains("`account/rateLimits/updatedNotification`", StringComparison.Ordinal), "the adjacent summary must not invent a Notification-suffixed wire method");
                AssertContains("`account/rateLimits/updated` push", document);
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

            cases.Add(new TestCaseDescriptor(SuiteId, "fixture_inventory_matches_documented_interfaces", "AC0/D4: each fixture is unique, valid JSON and referenced by its runtime evidence; unavailable sources never claim verified data", (CancellationToken ct) =>
            {
                UsageFixtureManifest manifest = ReadManifest();
                Dictionary<string, string> sections = ReadDocSections();
                HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);

                foreach (UsageFixtureEntry entry in manifest.Fixtures)
                {
                    AssertTrue(names.Add(entry.File), "duplicate fixture inventory entry: " + entry.File);
                    AssertEqual(Path.GetFileName(entry.File), entry.File, "fixture names must stay inside the fixture directory");
                    AssertTrue(sections.TryGetValue(entry.Runtime, out string? section)
                        && section.Contains(entry.File, StringComparison.Ordinal), entry.File + " is not referenced by its runtime evidence");
                    if (UnavailableRuntimes.Contains(entry.Runtime))
                    {
                        AssertFalse(entry.Verified, entry.Runtime + " cannot claim verified account-limit evidence");
                    }

                    string path = Path.Combine(FixtureDirectory(), entry.File);
                    string[] records = entry.File.EndsWith(".jsonl", StringComparison.Ordinal)
                        ? File.ReadAllLines(path).Where(line => !String.IsNullOrWhiteSpace(line)).ToArray()
                        : new[] { File.ReadAllText(path) };
                    AssertTrue(records.Length > 0, entry.File + " contains no evidence records");
                    foreach (string record in records)
                    {
                        // Deserialization validates syntax without inspecting untyped JSON fields.
                        AssertNotNull(JsonSerializer.Deserialize<object>(record), entry.File + " contains a null evidence record");
                    }
                }

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

            cases.Add(new TestCaseDescriptor(SuiteId, "codex_fixtures_match_installed_schema_contract", "AC0/D4: the Codex read fixture keeps accountId null, every window carries the schema-required usedPercent with epoch-second resets, and initialize needs only clientInfo without experimentalApi", (CancellationToken ct) =>
            {
                string directory = FixtureDirectory();
                JsonSerializerOptions options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

                CodexRateLimitsReadFixture? read = JsonSerializer.Deserialize<CodexRateLimitsReadFixture>(
                    File.ReadAllText(Path.Combine(directory, "codex-account-ratelimits-read.sanitized.json")), options);
                AssertNotNull(read, "the Codex read fixture did not deserialize");
                AssertNull(read!.AccountId, "the sanitized Codex fixture must not carry an account id; phase 1 HMACs it on the host");
                AssertNotNull(read.RateLimits, "rateLimits is required by GetAccountRateLimitsResponse");

                List<string> problems = new List<string>();
                List<CodexRateLimitSnapshotFixture> snapshots = new List<CodexRateLimitSnapshotFixture> { read.RateLimits! };
                if (read.RateLimitsByLimitId != null) snapshots.AddRange(read.RateLimitsByLimitId.Values);
                int windows = 0;
                foreach (CodexRateLimitSnapshotFixture snapshot in snapshots)
                {
                    foreach (CodexRateLimitWindowFixture? window in new[] { snapshot.Primary, snapshot.Secondary })
                    {
                        if (window == null) continue;
                        windows++;
                        string label = (snapshot.LimitId ?? "(no limitId)") + " window";
                        if (window.UsedPercent == null) problems.Add(label + " has no usedPercent, which the installed schema requires");
                        else if (window.UsedPercent < 0 || window.UsedPercent > 100) problems.Add(label + " usedPercent " + window.UsedPercent + " is outside 0..100");
                        if (window.WindowDurationMins != null && window.WindowDurationMins <= 0) problems.Add(label + " has a non-positive windowDurationMins");
                        if (window.ResetsAt != null && !IsEpochSeconds(window.ResetsAt.Value)) problems.Add(label + " resetsAt " + window.ResetsAt + " is not Unix epoch seconds");
                    }
                }

                foreach (KeyValuePair<string, CodexRateLimitSnapshotFixture> pair in read.RateLimitsByLimitId ?? new Dictionary<string, CodexRateLimitSnapshotFixture>())
                {
                    if (pair.Value.LimitId != null && !String.Equals(pair.Key, pair.Value.LimitId, StringComparison.Ordinal)) problems.Add("rateLimitsByLimitId key " + pair.Key + " does not match its limitId " + pair.Value.LimitId);
                }

                AssertTrue(windows >= 2, "the Codex read fixture should exercise at least two windows, found " + windows);

                CodexInitializeFixture? init = JsonSerializer.Deserialize<CodexInitializeFixture>(
                    File.ReadAllText(Path.Combine(directory, "codex-app-server-initialize.sanitized.json")), options);
                AssertNotNull(init, "the Codex initialize fixture did not deserialize");
                AssertNotNull(init!.ClientInfo, "initialize requires clientInfo");
                if (String.IsNullOrWhiteSpace(init.ClientInfo!.Name)) problems.Add("initialize clientInfo.name is required");
                if (String.IsNullOrWhiteSpace(init.ClientInfo.Version)) problems.Add("initialize clientInfo.version is required");
                if (init.Capabilities?.ExperimentalApi == true) problems.Add("the usage probe must not opt into experimentalApi");

                AssertTrue(problems.Count == 0, "Codex fixtures drift from the installed schema contract:\n" + String.Join("\n", problems));
                return Task.CompletedTask;
            }));

            cases.Add(new TestCaseDescriptor(SuiteId, "claude_fixture_timestamps_are_epoch_seconds", "Claude rate_limit_event resetsAt values are absolute epoch seconds (not milliseconds) and utilization is a 0..1 fraction", (CancellationToken ct) =>
            {
                string[] lines = File.ReadAllLines(Path.Combine(FixtureDirectory(), "claude-rate-limit-event.sanitized.jsonl"))
                    .Where(line => !String.IsNullOrWhiteSpace(line))
                    .ToArray();
                foreach (string line in lines)
                {
                    AssertTrue(ClaudeRateLimitEvent.TryParse(line, out ClaudeRateLimitEvent? evt) && evt!.Info != null, "unparseable Claude fixture line");
                    AssertNotNull(evt!.Info!.ResetsAt, "Claude fixture line has no resetsAt");
                    AssertTrue(IsEpochSeconds(evt.Info.ResetsAt!.Value), "Claude resetsAt " + evt.Info.ResetsAt + " is not Unix epoch seconds");
                    AssertNotNull(evt.Info.Utilization, "Claude fixture line has no utilization");
                    AssertTrue(evt.Info.Utilization >= 0d && evt.Info.Utilization <= 1d, "Claude utilization " + evt.Info.Utilization + " is not a 0..1 fraction");
                }

                return Task.CompletedTask;
            }));

            cases.Add(new TestCaseDescriptor(SuiteId, "apiendpoint_header_fixtures_are_allowlisted", "ApiEndpoint header fixtures hold only allowlisted rate-limit headers (no credential or request-id header), Anthropic resets are RFC 3339 UTC, OpenAI resets are durations and retry-after is delay seconds", (CancellationToken ct) =>
            {
                Regex anthropicName = new Regex("^anthropic-ratelimit-(requests|input-tokens|output-tokens|tokens)-(limit|remaining|reset)$");
                Regex openAiName = new Regex("^x-ratelimit-(limit|remaining|reset)-(requests|tokens)$");
                Regex duration = new Regex("^(?=.)([0-9]+h)?([0-9]+m(?!s))?([0-9]+(\\.[0-9]+)?(ms|s))?$");
                List<string> problems = new List<string>();

                foreach (string file in new[] { "anthropic-ratelimit-headers.sanitized.json", "openai-compatible-ratelimit-headers.sanitized.json" })
                {
                    Dictionary<string, string>? headers = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(FixtureDirectory(), file)));
                    AssertNotNull(headers, file + " did not deserialize as a header map");
                    AssertTrue(headers!.Count > 0, file + " holds no headers");
                    foreach (KeyValuePair<string, string> header in headers)
                    {
                        string name = header.Key;
                        bool anthropic = anthropicName.IsMatch(name);
                        bool openAi = openAiName.IsMatch(name);
                        bool retryAfter = String.Equals(name, "retry-after", StringComparison.Ordinal);
                        if (!anthropic && !openAi && !retryAfter) { problems.Add(file + ": header '" + name + "' is outside the rate-limit allowlist"); continue; }

                        if (name.EndsWith("-reset", StringComparison.Ordinal))
                        {
                            if (!DateTimeOffset.TryParseExact(header.Value, "yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out DateTimeOffset _))
                                problems.Add(file + ": " + name + " '" + header.Value + "' is not an RFC 3339 UTC timestamp");
                        }
                        else if (openAi && name.StartsWith("x-ratelimit-reset-", StringComparison.Ordinal))
                        {
                            if (!duration.IsMatch(header.Value)) problems.Add(file + ": " + name + " '" + header.Value + "' is not a duration string");
                        }
                        else if (!Int64.TryParse(header.Value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long count) || count < 0)
                        {
                            problems.Add(file + ": " + name + " '" + header.Value + "' is not a non-negative integer");
                        }
                    }
                }

                AssertTrue(problems.Count == 0, "ApiEndpoint header fixtures are not allowlisted evidence:\n" + String.Join("\n", problems));
                return Task.CompletedTask;
            }));

            cases.Add(new TestCaseDescriptor(SuiteId, "apiendpoint_provider_default_seam_gap_matches_source", "The documented ProviderDefault seam gap matches ModelEndpointAuth: no Armada HttpClient for ProviderDefault without headers, an Armada handler chain otherwise", (CancellationToken ct) =>
            {
                ModelEndpoint endpoint = new ModelEndpoint
                {
                    Name = "usage-seam",
                    Provider = ModelProviderEnum.Ollama,
                    Kind = ModelEndpointKindEnum.Inference,
                    BaseUrl = "http://127.0.0.1:11434",
                    AuthType = ModelEndpointAuthTypeEnum.ProviderDefault,
                    Headers = new List<ModelEndpointHeader>()
                };

                string apiSection = ReadDocSections()["ApiEndpoint"];
                bool gapDocumented = apiSection.Contains("`ModelEndpointAuth.CreateHttpClient` returns null when `AuthType` is", StringComparison.Ordinal);
                bool gapInCode = ModelEndpointAuth.CreateHttpClient(endpoint) == null;
                AssertEqual(gapInCode, gapDocumented, "PROBE_INTERFACES.md must describe the ProviderDefault seam gap exactly when ModelEndpointAuth still has it");

                endpoint.Headers = new List<ModelEndpointHeader> { new ModelEndpointHeader("X-Usage-Seam", "placeholder") };
                using (HttpClient? withHeaders = ModelEndpointAuth.CreateHttpClient(endpoint))
                    AssertNotNull(withHeaders, "a ProviderDefault endpoint with extra headers goes through the Armada handler chain");

                endpoint.Headers = new List<ModelEndpointHeader>();
                endpoint.AuthType = ModelEndpointAuthTypeEnum.Bearer;
                using (HttpClient? bearer = ModelEndpointAuth.CreateHttpClient(endpoint))
                    AssertNotNull(bearer, "a configured-auth endpoint goes through the Armada handler chain");
                return Task.CompletedTask;
            }));

            cases.Add(new TestCaseDescriptor(SuiteId, "gemini_fixture_yields_no_reset", "The Gemini RESOURCE_EXHAUSTED fixture is recognized as a rate limit by RuntimeProviderErrorParser and yields no reset or retry value", (CancellationToken ct) =>
            {
                string text = File.ReadAllText(Path.Combine(FixtureDirectory(), "gemini-resource-exhausted.sanitized.json"));
                AssertFalse(text.Contains("reset", StringComparison.OrdinalIgnoreCase), "the Gemini fixture must not carry a reset value");
                AssertFalse(text.Contains("retry", StringComparison.OrdinalIgnoreCase), "the Gemini fixture must not carry a retry delay");

                string line = String.Join(" ", text.Split('\n').Select(l => l.Trim()));
                RuntimeProviderError? error = RuntimeProviderErrorParser.TryParseCliStderrLine(line, "Gemini");
                AssertNotNull(error, "RuntimeProviderErrorParser does not recognize the Gemini fixture");
                AssertTrue(String.Equals(error!.ErrorType, "rate_limit_exceeded", StringComparison.Ordinal) || error.HttpStatusCode == 429,
                    "the Gemini fixture should classify as a rate limit, got type '" + error.ErrorType + "' status " + error.HttpStatusCode);
                AssertNull(error.ResetUtc, "no reset may be inferred from the Gemini exhaustion error");
                AssertNull(error.RetryAfterSeconds, "no retry delay may be inferred from the Gemini exhaustion error");
                return Task.CompletedTask;
            }));

            cases.Add(new TestCaseDescriptor(SuiteId, "custom_contract_fixture_within_plan_bounds", "The Custom contract fixture is schemaVersion 1, at most 64 KiB, 1..32 windows with sane percentages, positive window lengths and resets after capture, and a placeholder account key", (CancellationToken ct) =>
            {
                string path = Path.Combine(FixtureDirectory(), "custom-usage-contract.sanitized.json");
                AssertTrue(new FileInfo(path).Length <= 64 * 1024, "the Custom contract fixture exceeds the 64 KiB stdout cap");
                CustomUsageContractFixture? contract = JsonSerializer.Deserialize<CustomUsageContractFixture>(File.ReadAllText(path));
                AssertNotNull(contract, "the Custom contract fixture did not deserialize");
                AssertEqual(1, contract!.SchemaVersion ?? 0, "Custom contract schemaVersion");
                AssertNotNull(contract.CapturedUtc, "Custom contract capturedUtc is required");
                AssertTrue(!String.IsNullOrWhiteSpace(contract.AccountKey) && contract.AccountKey!.Contains("placeholder", StringComparison.Ordinal),
                    "the Custom contract accountKey must stay a placeholder; the host owns the final HMAC");
                AssertNotNull(contract.Windows, "Custom contract windows are required");
                AssertTrue(contract.Windows!.Count >= 1 && contract.Windows.Count <= 32, "Custom contract must carry 1..32 windows, found " + contract.Windows.Count);

                List<string> problems = new List<string>();
                foreach (CustomUsageWindowFixture window in contract.Windows)
                {
                    string label = window.Name ?? "(unnamed)";
                    if (String.IsNullOrWhiteSpace(window.Name)) problems.Add("a window has no name");
                    if (window.UsedPercent != null && (Double.IsNaN(window.UsedPercent.Value) || window.UsedPercent < 0 || window.UsedPercent > 100)) problems.Add(label + ": impossible usedPercent");
                    if (window.Used != null && window.Used < 0) problems.Add(label + ": negative used");
                    if (window.Limit != null && window.Limit <= 0) problems.Add(label + ": non-positive limit");
                    if (window.WindowMinutes == null || window.WindowMinutes <= 0) problems.Add(label + ": windowMinutes must be positive");
                    if (window.ResetsUtc != null && window.ResetsUtc <= contract.CapturedUtc) problems.Add(label + ": resetsUtc is not after capturedUtc");
                    if (window.ResetsUtc != null && window.ResetsUtc.Value.Offset != TimeSpan.Zero) problems.Add(label + ": resetsUtc is not UTC");
                }

                AssertTrue(problems.Count == 0, "the Custom contract fixture violates the plan bounds:\n" + String.Join("\n", problems));
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

        private static bool IsEpochSeconds(long value)
        {
            // 2001-09-09 .. 2286-11-20 in seconds; millisecond values are three orders of magnitude larger.
            return value >= 1000000000L && value < 10000000000L;
        }

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

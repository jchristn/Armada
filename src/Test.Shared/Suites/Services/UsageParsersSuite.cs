namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Protocol;
    using Armada.Core.Services;
    using Armada.Runtimes;
    using Armada.Runtimes.Usage;
    using PolyPrompt.Clients;
    using PolyPrompt.Models;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// AC3 Services.UsageParsers: the phase 3 runtime probes and typed versioned parsers. Codex app-server
    /// normalization over the sanitized fixture (success, partial, null and missing windows, malformed values)
    /// and through a real stdio JSON-RPC session (no thread or turn, auth and provider errors, truncated EOF,
    /// oversize, forced process-tree cleanup on timeout and cancellation, CLI absent); the Claude passive
    /// rate_limit_event (success, unknown version, null utilization, truncated line); the Custom contract
    /// (unknown version, duplicate objects, inconsistent units, empty object, oversize); allowlisted rate-limit
    /// headers with relative and absolute resets and 401/403/429/5xx/absent mapping; and the ApiEndpoint
    /// transport proven against a local HTTP fixture: capture attached before a known request sees its
    /// headers on the provider-default and configured-auth paths, on error statuses and on a streamed
    /// response that ends early. Built-in probes never invoke a model and unsupported interfaces stay
    /// Unsupported.
    /// </summary>
    public sealed class UsageParsersSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.UsageParsers";
        private const string TenantId = "ten_usage_parsers";
        private static readonly DateTime _CodexObserved = new DateTime(2026, 10, 31, 17, 0, 0, DateTimeKind.Utc);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("codex_fixture_success_keeps_every_window_and_duration", "The Codex fixture normalizes primary and secondary windows with their returned durations and absolute resets", TestTags.Positive, () =>
            {
                CodexRateLimitsReadResponse? response = JsonSerializer.Deserialize<CodexRateLimitsReadResponse>(Fixture("codex-account-ratelimits-read.sanitized.json"));
                UsageProbeResult result = CodexRateLimitUsage.ToResult(response, _CodexObserved);
                AssertEqual(UsageSnapshotStatusEnum.Ok, result.Status, "the fixture is a successful observation");
                AssertEqual(UsageCaptureSourceEnum.AppServer, result.Source, "the source is the app-server");
                AssertEqual(_CodexObserved, result.CapturedUtc, "the observation keeps the time it was received");
                AssertEqual(2, result.Windows.Count, "the per-limit primary and the snapshot secondary are both kept, once each");
                UsageWindow primary = result.Windows.Find(w => w.Name == "codex:primary")!;
                UsageWindow secondary = result.Windows.Find(w => w.Name == "codex:secondary")!;
                AssertNotNull(primary, "the primary window exists");
                AssertNotNull(secondary, "the secondary window exists");
                AssertEqual(42.0, primary.UsedPercent, "the primary percentage is kept");
                AssertEqual(300, primary.WindowMinutes, "the primary duration is the returned value");
                AssertEqual(10080, secondary.WindowMinutes, "the secondary duration is the returned value, never assumed");
                AssertEqual(new DateTime(2026, 10, 31, 17, 45, 0, DateTimeKind.Utc), primary.ResetsUtc, "the absolute epoch reset converts to UTC");
                AssertEqual(new DateTime(2026, 11, 7, 17, 45, 0, DateTimeKind.Utc), secondary.ResetsUtc, "a weekly reset is accepted without widening the 24-hour error policy");
                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("codex_partial_null_and_missing_windows", "Partial snapshots keep what they have, null windows are skipped, an empty answer is Unsupported, never zero", TestTags.Negative, () =>
            {
                UsageProbeResult partial = CodexRateLimitUsage.ToResult(Codex("{\"rateLimits\":{\"limitId\":\"codex\",\"primary\":{\"usedPercent\":10,\"windowDurationMins\":300,\"resetsAt\":1793468700},\"secondary\":null}}"), _CodexObserved);
                AssertEqual(UsageSnapshotStatusEnum.Ok, partial.Status, "a partial snapshot is still an observation");
                AssertEqual(1, partial.Windows.Count, "only the reported window is kept");

                UsageProbeResult nullPercent = CodexRateLimitUsage.ToResult(Codex("{\"rateLimits\":{\"primary\":{\"usedPercent\":null},\"secondary\":{\"usedPercent\":5}}}"), _CodexObserved);
                AssertEqual(1, nullPercent.Windows.Count, "a window without a percentage is skipped, never zero");
                AssertNull(nullPercent.Windows[0].WindowMinutes, "an absent duration stays unknown");

                foreach (string json in new[] { "{}", "{\"rateLimits\":null,\"rateLimitsByLimitId\":null}", "{\"rateLimits\":{\"primary\":null,\"secondary\":null}}", "{\"rateLimitsByLimitId\":{\"codex\":null}}" })
                {
                    UsageProbeResult missing = CodexRateLimitUsage.ToResult(Codex(json), _CodexObserved);
                    AssertEqual(UsageSnapshotStatusEnum.Unsupported, missing.Status, "no window at all is Unsupported: " + json);
                    AssertEqual("NoRateLimitWindows", missing.ErrorCode, "the stable code names the missing windows");
                    AssertEqual(0, missing.Windows.Count, "nothing is invented");
                }

                UsageProbeResult absent = CodexRateLimitUsage.ToResult(null, _CodexObserved);
                AssertEqual(UsageSnapshotStatusEnum.Failed, absent.Status, "a null result is a failure, not a crash");
                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("codex_malformed_values_reject_the_observation", "An impossible percentage, duration or reset rejects the whole Codex observation", TestTags.Negative, () =>
            {
                string[] malformed = new[]
                {
                    "{\"rateLimits\":{\"primary\":{\"usedPercent\":140,\"windowDurationMins\":300}}}",
                    "{\"rateLimits\":{\"primary\":{\"usedPercent\":-1,\"windowDurationMins\":300}}}",
                    "{\"rateLimits\":{\"primary\":{\"usedPercent\":10,\"windowDurationMins\":0}}}",
                    "{\"rateLimits\":{\"primary\":{\"usedPercent\":10,\"resetsAt\":4102444800}}}",
                    "{\"rateLimits\":{\"primary\":{\"usedPercent\":10,\"resetsAt\":1600000000}}}"
                };
                foreach (string json in malformed)
                {
                    UsageProbeResult result = CodexRateLimitUsage.ToResult(Codex(json), _CodexObserved);
                    AssertEqual(UsageSnapshotStatusEnum.Failed, result.Status, "rejected: " + json);
                    AssertEqual("MalformedOutput", result.ErrorCode, "the stable code is MalformedOutput: " + json);
                    AssertEqual(0, result.Windows.Count, "a rejected observation carries no windows");
                }

                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("codex_app_server_session_reads_limits_without_a_turn", "A real stdio session initializes, reads account/rateLimits/read, never creates a thread or turn, and HMACs the account id", TestTags.Positive, async () =>
            {
                if (OperatingSystem.IsWindows()) return;
                using FakeAppServer server = new FakeAppServer("ok");
                UsageAccountResolver accounts = new UsageAccountResolver("host-parsers-1", new string('d', 64));
                CodexUsageProbe probe = server.Probe(accounts);
                UsageProbeResult result = await probe.CaptureAsync(Host(AgentRuntimeEnum.Codex)).ConfigureAwait(false);
                AssertEqual(UsageSnapshotStatusEnum.Ok, result.Status, "the session answers an observation: " + result.ErrorCode + " " + result.RawExcerpt);
                AssertEqual(2, result.Windows.Count, "both fixture windows are read through the session");
                AssertNotNull(result.AccountKey, "a reported account id becomes a host-derived key");
                AssertFalse(result.AccountKey!.Contains("acct-parsers-raw", StringComparison.Ordinal), "the raw account id never leaves the host");
                AssertEqual(accounts.DeriveAccountKey(TenantId, AgentRuntimeEnum.Codex, "acct-parsers-raw"), result.AccountKey, "the key is the host HMAC of the account id");

                List<string> sent = server.SentLines();
                AssertEqual(3, sent.Count, "exactly initialize, initialized and the rate-limit read are sent");
                AssertContains("\"method\":\"initialize\"", sent[0], "the session starts with initialize");
                AssertContains("\"clientInfo\"", sent[0], "initialize carries clientInfo");
                AssertContains("\"experimentalApi\":false", sent[0], "no experimental capability is requested");
                AssertEqual("{\"method\":\"initialized\"}", sent[1], "the initialized notification follows");
                AssertContains("\"method\":\"account/rateLimits/read\"", sent[2], "the only request after initialize is the rate-limit read");
                foreach (string line in sent)
                {
                    AssertFalse(line.Contains("thread/", StringComparison.Ordinal) || line.Contains("turn/", StringComparison.Ordinal) || line.Contains("sendUserMessage", StringComparison.Ordinal),
                        "no thread or turn is created, so no model is invoked: " + line);
                }

                AssertTrue(await WaitExitedAsync(probe.LastProcessId).ConfigureAwait(false), "the helper process is gone after the capture");
            }));

            cases.Add(CaseAsync("codex_app_server_errors_truncation_and_oversize", "Auth errors are AuthMissing, provider errors redact, truncated EOF and oversize lines are refused", TestTags.Negative, async () =>
            {
                if (OperatingSystem.IsWindows()) return;
                UsageAccountResolver accounts = new UsageAccountResolver("host-parsers-2", new string('e', 64));
                Dictionary<string, string> expected = new Dictionary<string, string>
                {
                    { "autherror", "CredentialUnavailable" },
                    { "error", "ProviderError" },
                    { "truncated", "TruncatedOutput" },
                    { "exit", "TruncatedOutput" },
                    { "oversize", "MalformedOutput" },
                    { "garbage", "MalformedOutput" },
                    { "nullresult", "MalformedOutput" }
                };
                foreach (KeyValuePair<string, string> mode in expected)
                {
                    using FakeAppServer server = new FakeAppServer(mode.Key);
                    CodexUsageProbe probe = server.Probe(accounts);
                    UsageProbeResult result = await probe.CaptureAsync(Host(AgentRuntimeEnum.Codex)).ConfigureAwait(false);
                    AssertEqual(mode.Value, result.ErrorCode, "mode " + mode.Key + " maps to its stable code");
                    AssertEqual(mode.Key == "autherror" ? UsageSnapshotStatusEnum.AuthMissing : UsageSnapshotStatusEnum.Failed, result.Status, "mode " + mode.Key + " status");
                    AssertEqual(0, result.Windows.Count, "mode " + mode.Key + " invents no windows");
                    AssertNull(result.AccountKey, "mode " + mode.Key + " invents no account");
                    if (mode.Key == "error")
                        AssertFalse(result.RawExcerpt!.Contains("abcdefghijklmnop123456", StringComparison.Ordinal), "a provider error message is redacted");
                    AssertTrue(await WaitExitedAsync(probe.LastProcessId).ConfigureAwait(false), "mode " + mode.Key + " leaves no helper process");
                }

                CodexUsageProbe absent = new CodexUsageProbe(accounts, "/nonexistent/armada-codex-" + Guid.NewGuid().ToString("N"));
                UsageProbeResult notInstalled = await absent.CaptureAsync(Host(AgentRuntimeEnum.Codex)).ConfigureAwait(false);
                AssertEqual(UsageSnapshotStatusEnum.Unsupported, notInstalled.Status, "a missing CLI is Unsupported, not Failed");
                AssertEqual("RuntimeNotInstalled", notInstalled.ErrorCode, "the stable code names the missing CLI");
            }));

            cases.Add(CaseAsync("codex_hung_session_is_force_cleaned_on_timeout_and_cancel", "A hung app-server and its child are terminated at the host deadline and on cancellation", TestTags.Reliability, async () =>
            {
                if (OperatingSystem.IsWindows()) return;
                UsageAccountResolver accounts = new UsageAccountResolver("host-parsers-3", new string('f', 64));
                using (FakeAppServer server = new FakeAppServer("hang"))
                {
                    CodexUsageProbe probe = server.Probe(accounts);
                    UsageProbeHostRunner runner = new UsageProbeHostRunner(CreateLogging(), accounts, runtime => runtime == AgentRuntimeEnum.Codex ? probe : null);
                    UsageProbeHost host = Host(AgentRuntimeEnum.Codex);
                    host.TimeoutSeconds = 1;
                    Stopwatch watch = Stopwatch.StartNew();
                    UsageProbeResult result = await runner.CaptureAsync(host).ConfigureAwait(false);
                    watch.Stop();
                    AssertEqual("ProbeTimeout", result.ErrorCode, "the host deadline stops the hung session");
                    AssertTrue(watch.Elapsed < TimeSpan.FromSeconds(15), "the deadline bounds the capture: " + watch.Elapsed);
                    AssertTrue(await WaitExitedAsync(probe.LastProcessId).ConfigureAwait(false), "the app-server process is terminated");
                    int child = await server.ChildPidAsync().ConfigureAwait(false);
                    AssertTrue(child > 0, "the fake app-server started a child");
                    AssertTrue(await WaitExitedAsync(child).ConfigureAwait(false), "the app-server's child is terminated with the tree");
                }

                using (FakeAppServer server = new FakeAppServer("hang"))
                {
                    CodexUsageProbe probe = server.Probe(accounts);
                    using CancellationTokenSource cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(800));
                    bool cancelled = false;
                    try
                    {
                        await probe.CaptureAsync(Host(AgentRuntimeEnum.Codex), cancel.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        cancelled = true;
                    }

                    AssertTrue(cancelled, "cancellation surfaces as cancellation, never as a result");
                    AssertTrue(await WaitExitedAsync(probe.LastProcessId).ConfigureAwait(false), "a cancelled session's process is terminated in finally");
                    int child = await server.ChildPidAsync().ConfigureAwait(false);
                    AssertTrue(await WaitExitedAsync(child).ConfigureAwait(false), "a cancelled session's child is terminated");
                }
            }));

            cases.Add(CaseAsync("claude_rate_limit_event_is_harvested_not_transcribed", "The Claude fixture events become typed observations and never reach output or transcript", TestTags.Positive, () =>
            {
                ClaudeMissionStreamDecoder decoder = new ClaudeMissionStreamDecoder();
                DateTime observed = new DateTime(2026, 10, 31, 16, 0, 0, DateTimeKind.Utc);
                string[] lines = Fixture("claude-rate-limit-event.sanitized.jsonl").Split('\n', StringSplitOptions.RemoveEmptyEntries);
                AssertEqual(2, lines.Length, "the fixture has two events");
                MissionStreamDecodeResult first = decoder.Decode(lines[0], observed);
                AssertEqual(0, first.OutputLines.Count, "a rate limit event is never output");
                AssertEqual(0, first.TranscriptLines.Count, "a rate limit event is never transcript");
                AssertEqual(1, first.UsageObservations.Count, "the event is harvested as one observation");
                UsageProbeResult five = first.UsageObservations[0];
                AssertEqual(UsageSnapshotStatusEnum.Ok, five.Status, "the event is a valid observation");
                AssertEqual(UsageCaptureSourceEnum.HarnessEvent, five.Source, "the source is the harness event");
                AssertEqual(observed, five.CapturedUtc, "the observation keeps the read time, not a manufactured one");
                AssertEqual("five_hour", five.Windows[0].Name, "the window name comes from rateLimitType");
                AssertEqual(62.0, five.Windows[0].UsedPercent, "the fraction becomes a percentage");
                AssertEqual(new DateTime(2026, 10, 31, 17, 45, 0, DateTimeKind.Utc), five.Windows[0].ResetsUtc, "resetsAt converts from epoch seconds");
                MissionStreamDecodeResult second = decoder.Decode(lines[1], observed);
                AssertEqual("seven_day", second.UsageObservations[0].Windows[0].Name, "the weekly window is harvested");
                AssertEqual(31.0, second.UsageObservations[0].Windows[0].UsedPercent, "the weekly fraction becomes a percentage");

                MissionStreamDecodeResult truncated = decoder.Decode(lines[0].Substring(0, lines[0].Length / 2), observed);
                AssertEqual(0, truncated.UsageObservations.Count, "a truncated line is never parsed as an observation");
                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("claude_rate_limit_event_versions_and_nulls", "Unknown statuses and percent-form utilization are rejected; null utilization stays null, never 100", TestTags.Negative, () =>
            {
                DateTime observed = new DateTime(2026, 10, 31, 16, 0, 0, DateTimeKind.Utc);
                AssertEqual("UnknownEventVersion", Claude("{\"type\":\"rate_limit_event\",\"rate_limit_info\":{\"status\":\"throttled\",\"rateLimitType\":\"five_hour\",\"utilization\":0.5}}", observed).ErrorCode, "an unknown status is a different version");
                AssertEqual("UnknownEventVersion", Claude("{\"type\":\"rate_limit_event\",\"rate_limit_info\":{\"status\":\"allowed\",\"rateLimitType\":\"five_hour\",\"utilization\":62}}", observed).ErrorCode, "a percent-form utilization is a different version, never guessed");
                AssertEqual("MalformedOutput", Claude("{\"type\":\"rate_limit_event\"}", observed).ErrorCode, "an event without info is malformed");
                AssertEqual("MalformedOutput", Claude("{\"type\":\"rate_limit_event\",\"rate_limit_info\":{\"status\":\"allowed\",\"rateLimitType\":\"Five Hour!\",\"utilization\":0.1}}", observed).ErrorCode, "an invalid window name is refused");
                AssertEqual("MalformedOutput", Claude("{\"type\":\"rate_limit_event\",\"rate_limit_info\":{\"status\":\"allowed\",\"rateLimitType\":\"five_hour\",\"utilization\":0.1,\"resetsAt\":4102444800}}", observed).ErrorCode, "a reset beyond the bound is refused");
                UsageProbeResult rejected = Claude("{\"type\":\"rate_limit_event\",\"rate_limit_info\":{\"status\":\"rejected\",\"rateLimitType\":\"seven_day\",\"utilization\":null}}", observed);
                AssertEqual(UsageSnapshotStatusEnum.Ok, rejected.Status, "a rejected status is still a structured observation");
                AssertNull(rejected.Windows[0].UsedPercent, "a null utilization is never invented as 100 percent");
                AssertEqual("rejected", rejected.Windows[0].CoverageNote, "the status is kept as coverage context");
                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("custom_contract_versions_duplicates_units_and_oversize", "The Custom contract refuses unknown versions, duplicate objects, inconsistent units and oversize output; {} never crashes", TestTags.Negative, async () =>
            {
                if (OperatingSystem.IsWindows()) return;
                UsageAccountResolver accounts = new UsageAccountResolver("host-parsers-4", new string('9', 64));
                CustomUsageProbe probe = new CustomUsageProbe(CreateLogging(), accounts);
                string good = Fixture("custom-usage-contract.sanitized.json").Replace("2026-10-27T13:00:00Z", "{{NOW}}").Replace("2026-10-27T14:05:00Z", "{{RESET}}");
                DateTime now = DateTime.UtcNow;
                string fresh = good.Replace("{{NOW}}", now.ToString("yyyy-MM-ddTHH:mm:ssZ")).Replace("{{RESET}}", now.AddHours(1).ToString("yyyy-MM-ddTHH:mm:ssZ"));

                UsageProbeResult ok = await probe.CaptureAsync(CustomHost(fresh)).ConfigureAwait(false);
                AssertEqual(UsageSnapshotStatusEnum.Ok, ok.Status, "the fixture contract succeeds: " + ok.ErrorCode + " " + ok.RawExcerpt);
                AssertEqual(UsageCaptureSourceEnum.Custom, ok.Source, "the host owns the source");
                AssertFalse(ok.AccountKey == "host-hmac-placeholder", "the host HMACs the claimed account key");

                AssertEqual("UnsupportedSchemaVersion", (await probe.CaptureAsync(CustomHost(fresh.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2"))).ConfigureAwait(false)).ErrorCode, "an unknown schema version is refused");
                AssertEqual("UnsupportedSchemaVersion", (await probe.CaptureAsync(CustomHost("{}")).ConfigureAwait(false)).ErrorCode, "an empty object deserializes and is refused without crashing");
                AssertEqual("MalformedOutput", (await probe.CaptureAsync(CustomHost(Compact(fresh) + Compact(fresh))).ConfigureAwait(false)).ErrorCode, "duplicate JSON objects are refused");
                AssertEqual("MalformedOutput", (await probe.CaptureAsync(CustomHost(fresh.Replace("\"used\": null", "\"used\": 90000").Replace("\"limit\": null", "\"limit\": 100000"))).ConfigureAwait(false)).ErrorCode, "a percentage that disagrees with its counts has inconsistent units");
                UsageProbeResult consistent = await probe.CaptureAsync(CustomHost(fresh.Replace("\"used\": null", "\"used\": 62500").Replace("\"limit\": null", "\"limit\": 100000"))).ConfigureAwait(false);
                AssertEqual(UsageSnapshotStatusEnum.Ok, consistent.Status, "a percentage that agrees with its counts is accepted");
                AssertEqual("MalformedOutput", (await probe.CaptureAsync(CustomHost(fresh.Replace("\"five_hour\"", "\"\""))).ConfigureAwait(false)).ErrorCode, "an unnamed window is refused");
                AssertEqual("MalformedOutput", (await probe.CaptureAsync(CustomHost(fresh.Replace("\"usedPercent\": 62", "\"usedPercent\": 101"))).ConfigureAwait(false)).ErrorCode, "an impossible percentage is refused");
                UsageProbeResult nullWindows = await probe.CaptureAsync(CustomHost(fresh.Replace("\"windows\": [", "\"unused\": [").Replace("\"source\": \"Custom\",", "\"source\": \"Custom\", \"windows\": null,"))).ConfigureAwait(false);
                AssertEqual(UsageSnapshotStatusEnum.Ok, nullWindows.Status, "explicit null windows deserialize without crashing");
                AssertEqual(0, nullWindows.Windows.Count, "null windows become an empty list");

                string oversized = "{\"schemaVersion\":1,\"status\":\"Ok\",\"pad\":\"" + new string('x', 70 * 1024) + "\"}";
                UsageProbeResult tooBig = await probe.CaptureAsync(CustomHost(oversized)).ConfigureAwait(false);
                AssertEqual("MalformedOutput", tooBig.ErrorCode, "output beyond 64 KiB is refused, never parsed from a prefix");
            }));

            cases.Add(CaseAsync("headers_absolute_and_relative_resets", "Anthropic RFC 3339 resets stay absolute; OpenAI durations and Retry-After count from the observation time", TestTags.Positive, () =>
            {
                DateTime observed = new DateTime(2026, 10, 27, 13, 0, 0, DateTimeKind.Utc);
                UsageProbeResult anthropic = HeaderUsageCapture.Parse(Observation(200, observed, FixtureHeaders("anthropic-ratelimit-headers.sanitized.json")));
                AssertEqual(UsageSnapshotStatusEnum.Ok, anthropic.Status, "the Anthropic fixture is an observation");
                AssertEqual(UsageCaptureSourceEnum.Headers, anthropic.Source, "the source is headers");
                AssertEqual("anthropic", anthropic.Provider, "the provider is named from the header family");
                AssertEqual(4, anthropic.Windows.Count, "requests, tokens, input and output token windows");
                UsageWindow requests = anthropic.Windows.Find(w => w.Name == "requests")!;
                AssertEqual(7L, requests.Used, "used is limit minus remaining");
                AssertEqual(1000L, requests.Limit, "the limit is kept");
                AssertEqual(0.7, requests.UsedPercent, "the percentage is derived from the denominator");
                AssertEqual(new DateTime(2026, 10, 27, 13, 0, 5, DateTimeKind.Utc), requests.ResetsUtc, "the RFC 3339 reset is absolute");
                AssertEqual(observed.AddSeconds(30), anthropic.RetryAfterUtc, "Retry-After counts from the observation");
                AssertEqual(observed, anthropic.CapturedUtc, "the capture time is the observation time, never now");

                foreach (DateTime when in new[] { observed, observed.AddHours(3) })
                {
                    UsageProbeResult openai = HeaderUsageCapture.Parse(Observation(200, when, FixtureHeaders("openai-compatible-ratelimit-headers.sanitized.json")));
                    AssertEqual("openai-compatible", openai.Provider, "the OpenAI-style family is named");
                    AssertEqual(when.AddMinutes(6), openai.Windows.Find(w => w.Name == "requests")!.ResetsUtc, "6m0s counts from this observation");
                    AssertEqual(when.AddSeconds(9), openai.Windows.Find(w => w.Name == "tokens")!.ResetsUtc, "9s counts from this observation");
                }

                AssertEqual(observed.AddSeconds(42), UsageResetConverter.FromRelativeSeconds(42, observed), "relative seconds count from the event");
                AssertEqual(observed.AddHours(1).AddMinutes(2).AddSeconds(3.5), UsageResetConverter.FromDuration("1h2m3.5s", observed), "compound durations parse");
                AssertEqual(observed.AddMilliseconds(20), UsageResetConverter.FromDuration("20ms", observed), "millisecond durations parse");
                AssertNull(UsageResetConverter.FromDuration("6 minutes", observed), "an unknown duration format is refused");
                AssertNull(UsageResetConverter.FromRfc3339("2026-10-27T13:00:05", observed), "an offset-less timestamp is refused, never guessed");
                AssertNull(UsageResetConverter.FromRelativeSeconds(9 * 86400, observed), "a reset beyond eight days is refused");
                AssertNull(UsageResetConverter.FromRelativeSeconds(-5, observed), "a negative relative reset is refused");
                AssertEqual(new DateTime(2026, 10, 27, 13, 5, 0, DateTimeKind.Utc), UsageResetConverter.FromRetryAfter("Tue, 27 Oct 2026 13:05:00 GMT", observed), "an HTTP-date Retry-After is absolute");
                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("headers_status_mapping_and_malformed_values", "Absent headers are Unsupported, 401/403 AuthMissing, 429 keeps Retry-After, 5xx Failed, malformed numbers rejected", TestTags.Negative, () =>
            {
                DateTime observed = new DateTime(2026, 10, 27, 13, 0, 0, DateTimeKind.Utc);
                Dictionary<string, string> none = new Dictionary<string, string>();
                UsageProbeResult absent = HeaderUsageCapture.Parse(Observation(200, observed, none));
                AssertEqual(UsageSnapshotStatusEnum.Unsupported, absent.Status, "absent headers are Unsupported");
                AssertEqual("HeadersAbsent", absent.ErrorCode, "never AuthMissing for absence");
                AssertEqual(UsageSnapshotStatusEnum.Unsupported, HeaderUsageCapture.Parse(null).Status, "no observation at all is Unsupported");
                foreach (int status in new[] { 401, 403 })
                {
                    UsageProbeResult auth = HeaderUsageCapture.Parse(Observation(status, observed, FixtureHeaders("anthropic-ratelimit-headers.sanitized.json")));
                    AssertEqual(UsageSnapshotStatusEnum.AuthMissing, auth.Status, status + " is a rejected credential");
                    AssertEqual(0, auth.Windows.Count, status + " carries no windows");
                }

                UsageProbeResult limited = HeaderUsageCapture.Parse(Observation(429, observed, new Dictionary<string, string> { { "retry-after", "12" } }));
                AssertEqual(UsageSnapshotStatusEnum.Failed, limited.Status, "a bare 429 is a failure, never an invented 100 percent");
                AssertEqual("ProviderRateLimited", limited.ErrorCode, "the 429 has its stable code");
                AssertEqual(observed.AddSeconds(12), limited.RetryAfterUtc, "the 429 keeps its Retry-After");
                UsageProbeResult limitedWithWindows = HeaderUsageCapture.Parse(Observation(429, observed, FixtureHeaders("openai-compatible-ratelimit-headers.sanitized.json")));
                AssertEqual(UsageSnapshotStatusEnum.Ok, limitedWithWindows.Status, "a 429 with limit headers is an observation");
                AssertEqual(observed.AddSeconds(9), limitedWithWindows.RetryAfterUtc, "and keeps its Retry-After");
                foreach (int status in new[] { 500, 502, 503 })
                {
                    UsageProbeResult server = HeaderUsageCapture.Parse(Observation(status, observed, FixtureHeaders("openai-compatible-ratelimit-headers.sanitized.json")));
                    AssertEqual(UsageSnapshotStatusEnum.Failed, server.Status, status + " is a failure");
                    AssertEqual("ProviderUnavailable", server.ErrorCode, status + " has its stable code");
                    AssertEqual(0, server.Windows.Count, status + " headers are not authoritative");
                }

                Dictionary<string, Dictionary<string, string>> malformed = new Dictionary<string, Dictionary<string, string>>
                {
                    { "negative", new Dictionary<string, string> { { "x-ratelimit-limit-requests", "-1" }, { "x-ratelimit-remaining-requests", "0" } } },
                    { "zero limit", new Dictionary<string, string> { { "x-ratelimit-limit-requests", "0" }, { "x-ratelimit-remaining-requests", "0" } } },
                    { "remaining above limit", new Dictionary<string, string> { { "x-ratelimit-limit-requests", "10" }, { "x-ratelimit-remaining-requests", "11" } } },
                    { "incomplete", new Dictionary<string, string> { { "anthropic-ratelimit-tokens-limit", "10" } } },
                    { "bad reset", new Dictionary<string, string> { { "anthropic-ratelimit-tokens-limit", "10" }, { "anthropic-ratelimit-tokens-remaining", "1" }, { "anthropic-ratelimit-tokens-reset", "soon" } } },
                    { "fraction", new Dictionary<string, string> { { "x-ratelimit-limit-tokens", "1.5" }, { "x-ratelimit-remaining-tokens", "1" } } }
                };
                foreach (KeyValuePair<string, Dictionary<string, string>> entry in malformed)
                {
                    UsageProbeResult result = HeaderUsageCapture.Parse(Observation(200, observed, entry.Value));
                    AssertEqual(UsageSnapshotStatusEnum.Failed, result.Status, entry.Key + " is rejected");
                    AssertEqual("MalformedHeaders", result.ErrorCode, entry.Key + " has the stable code");
                }

                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("api_transport_capture_sees_known_requests_on_every_path", "Capture attached before a known request against a local HTTP fixture sees its headers on provider-default and configured auth, errors and streamed early EOF", TestTags.Positive, async () =>
            {
                LoggingModule logging = CreateLogging();
                using RateLimitFixtureServer server = new RateLimitFixtureServer();

                // Provider-default auth with no extra headers: the path where the provider would otherwise build
                // its own transport. A capture attached before the request still sees it.
                server.Reset(200, OpenAiHeaders(), streamTruncated: false);
                HeaderUsageCapture capture = new HeaderUsageCapture();
                ModelEndpoint openAi = new ModelEndpoint { Name = "usage-openai", Provider = ModelProviderEnum.OpenAICompatible, BaseUrl = server.BaseUrl, Model = "fixture-model", ApiKey = "sk-fixture-key-0001" };
                using (CompletionClientBase client = ModelEndpointClientFactory.CreateCompletion(openAi, logging, capture.Record))
                {
                    ChatResponse reply = await client.ChatAsync("usage capture probe text", new CompletionOptions(), CancellationToken.None).ConfigureAwait(false);
                    AssertTrue(reply.Success, "the known request succeeds through the capturing transport: " + reply.Error);
                    AssertEqual("fixture reply", reply.Text, "the response body is handled unchanged");
                }

                RateLimitFixtureRequest sentOpenAi = server.LastRequest()!;
                AssertNotNull(sentOpenAi, "the fixture saw the request");
                AssertEqual("POST", sentOpenAi.Method, "the request is a POST");
                AssertTrue(sentOpenAi.Path.EndsWith("/chat/completions", StringComparison.Ordinal), "the request URL is the chat completions path: " + sentOpenAi.Path);
                AssertEqual("Bearer sk-fixture-key-0001", sentOpenAi.Authorization, "the provider still applies its own default credential");
                AssertContains("usage capture probe text", sentOpenAi.Body, "the payload carries the prompt");
                AssertEqual(1, capture.Count, "exactly one observation per response");
                UsageProbeResult seen = capture.Latest()!;
                AssertEqual(UsageSnapshotStatusEnum.Ok, seen.Status, "the captured headers parse");
                AssertEqual(2, seen.Windows.Count, "both OpenAI windows were seen");
                AssertFalse(capture.LatestObservation!.Headers.ContainsKey("x-unrelated-header"), "headers outside the allowlist are never kept");
                AssertFalse(capture.LatestObservation!.Headers.ContainsKey("set-cookie"), "cookies are never kept");

                // Without a sink the factory keeps its previous behaviour: nothing is captured.
                HeaderUsageCapture detached = new HeaderUsageCapture();
                using (CompletionClientBase client = ModelEndpointClientFactory.CreateCompletion(openAi, logging))
                {
                    await client.ChatAsync("no capture attached", new CompletionOptions(), CancellationToken.None).ConfigureAwait(false);
                }

                AssertEqual(0, detached.Count, "a transport without an attached capture observes nothing");

                // Configured authentication (custom header) through ModelEndpointAuthHandler, Anthropic headers.
                server.Reset(200, AnthropicHeaders(), streamTruncated: false);
                HeaderUsageCapture anthropicCapture = new HeaderUsageCapture();
                ModelEndpoint anthropic = new ModelEndpoint
                {
                    Name = "usage-anthropic",
                    Provider = ModelProviderEnum.Anthropic,
                    BaseUrl = server.BaseUrl,
                    Model = "fixture-model",
                    AuthType = ModelEndpointAuthTypeEnum.Header,
                    AuthHeaderName = "X-Fixture-Key",
                    ApiKey = "fixture-header-secret"
                };
                using (CompletionClientBase client = ModelEndpointClientFactory.CreateCompletion(anthropic, logging, anthropicCapture.Record))
                {
                    ChatResponse reply = await client.ChatAsync("anthropic capture text", new CompletionOptions(), CancellationToken.None).ConfigureAwait(false);
                    AssertTrue(reply.Success, "the Anthropic request succeeds: " + reply.Error);
                }

                RateLimitFixtureRequest sentAnthropic = server.LastRequest()!;
                AssertTrue(sentAnthropic.Path.EndsWith("/messages", StringComparison.Ordinal), "the Anthropic request URL is the messages path: " + sentAnthropic.Path);
                AssertEqual("fixture-header-secret", sentAnthropic.CustomKey, "the configured auth handler still applies the credential");
                AssertContains("anthropic capture text", sentAnthropic.Body, "the Anthropic payload carries the prompt");
                AssertEqual(4, anthropicCapture.Latest()!.Windows.Count, "the configured-auth path is captured too");

                // Error statuses still report their headers to the capture.
                Dictionary<int, UsageSnapshotStatusEnum> errors = new Dictionary<int, UsageSnapshotStatusEnum>
                {
                    { 401, UsageSnapshotStatusEnum.AuthMissing },
                    { 403, UsageSnapshotStatusEnum.AuthMissing },
                    { 429, UsageSnapshotStatusEnum.Ok },
                    { 503, UsageSnapshotStatusEnum.Failed }
                };
                foreach (KeyValuePair<int, UsageSnapshotStatusEnum> error in errors)
                {
                    server.Reset(error.Key, OpenAiHeaders(), streamTruncated: false);
                    HeaderUsageCapture errorCapture = new HeaderUsageCapture();
                    using (CompletionClientBase client = ModelEndpointClientFactory.CreateCompletion(openAi, logging, errorCapture.Record))
                    {
                        ChatResponse reply = await client.ChatAsync("error path", new CompletionOptions(), CancellationToken.None).ConfigureAwait(false);
                        AssertFalse(reply.Success, error.Key + " is a failed call");
                    }

                    AssertTrue(errorCapture.Count >= 1, error.Key + " response headers were observed");
                    AssertEqual(error.Key, errorCapture.LatestObservation!.StatusCode, "the observed status is " + error.Key);
                    AssertEqual(error.Value, errorCapture.Latest()!.Status, error.Key + " maps to " + error.Value);
                }

                // A streamed response that ends early (no [DONE], connection aborted) still retains its headers.
                server.Reset(200, OpenAiHeaders(), streamTruncated: true);
                HeaderUsageCapture streamCapture = new HeaderUsageCapture();
                bool completed = false;
                using (CompletionClientBase client = ModelEndpointClientFactory.CreateCompletion(openAi, logging, streamCapture.Record))
                {
                    try
                    {
                        ChatStreamingResponse stream = await client.ChatStreamingAsync("stream capture text", new CompletionOptions(), CancellationToken.None).ConfigureAwait(false);
                        if (stream.Success)
                        {
                            await foreach (ChatStreamingChunk chunk in stream.Chunks.ConfigureAwait(false))
                            {
                            }

                            completed = stream.State == StreamingState.Completed;
                        }
                    }
                    catch (Exception)
                    {
                        // The truncated body may surface as an exception; the headers were observed before it.
                    }
                }

                AssertFalse(completed, "the fixture stream really ended early");
                AssertContains("stream capture text", server.LastRequest()!.Body, "the streamed request carried its payload");
                AssertEqual(1, streamCapture.Count, "the streamed response's headers were observed before the body ended");
                AssertEqual(UsageSnapshotStatusEnum.Ok, streamCapture.Latest()!.Status, "the streamed headers parse after EOF");
                AssertEqual(2, streamCapture.Latest()!.Windows.Count, "both windows were retained from the streamed response");
            }));

            cases.Add(CaseAsync("builtin_probes_never_prompt_and_unsupported_stays_unsupported", "Every built-in probe except the verified Codex read answers Unsupported without starting anything; PTY is never a fallback", TestTags.Negative, async () =>
            {
                UsageAccountResolver accounts = new UsageAccountResolver("host-parsers-5", new string('1', 64));
                UsageProbeRegistry registry = new UsageProbeRegistry(CreateLogging(), accounts, "/nonexistent/armada-codex");
                foreach (AgentRuntimeEnum runtime in Enum.GetValues<AgentRuntimeEnum>())
                {
                    if (runtime == AgentRuntimeEnum.Codex) continue;
                    foreach (bool pty in new[] { false, true })
                    {
                        UsageProbeHost host = Host(runtime);
                        host.AllowPty = pty;
                        UsageProbeResult result = await registry.Resolve(runtime).CaptureAsync(host).ConfigureAwait(false);
                        AssertEqual(UsageSnapshotStatusEnum.Unsupported, result.Status, runtime + " (pty " + pty + ") stays Unsupported");
                        AssertFalse(String.IsNullOrWhiteSpace(result.ErrorCode), runtime + " has a stable code");
                        AssertFalse(String.IsNullOrWhiteSpace(result.RawExcerpt), runtime + " explains why");
                        AssertEqual(0, result.Windows.Count, runtime + " invents no windows");
                        AssertNull(result.AccountKey, runtime + " invents no account");
                    }
                }

                UsageProbeHost claude = Host(AgentRuntimeEnum.ClaudeCode);
                claude.AllowPty = true;
                UsageProbeResult claudeResult = await registry.Resolve(AgentRuntimeEnum.ClaudeCode).CaptureAsync(claude).ConfigureAwait(false);
                AssertEqual(ClaudeUsageProbe.UnverifiedCode, claudeResult.ErrorCode, "the unverified Claude endpoint is not enabled");
                AssertContains("never sent as a prompt", claudeResult.RawExcerpt!, "the opted-in PTY source is not a prompt");

                UsageProbeHostRunner runner = new UsageProbeHostRunner(CreateLogging(), accounts, registry.Resolve);
                UsageProbeResult codexAbsent = await runner.CaptureAsync(Host(AgentRuntimeEnum.Codex)).ConfigureAwait(false);
                AssertEqual("RuntimeNotInstalled", codexAbsent.ErrorCode, "the production registry runs the Codex probe through the host runner");

                string api = File.ReadAllText(Path.Combine(DashboardSource.RepoRoot(), "src", "Armada.Runtimes", "ApiAgentRuntime.cs"));
                AssertEqual(2, CountOccurrences(api, "ModelEndpointClientFactory.CreateCompletion(ep, log, UsageHeaders.Record)"), "both ApiAgentRuntime constructors attach the passive capture to the production transport");
                foreach (string path in new[] { Path.Combine("src", "Armada.Server", "ArmadaServer.cs"), Path.Combine("src", "Armada.Harbor", "HarborLinkHost.cs") })
                {
                    string source = File.ReadAllText(Path.Combine(DashboardSource.RepoRoot(), path));
                    AssertContains("new Armada.Runtimes.Usage.UsageProbeRegistry(", source, path + " composes the production probe registry");
                    AssertContains("usageProbes.Resolve)", source, path + " hands the registry to the host runner");
                }
            }));

            cases.Add(CaseAsync("rate_limit_handler_reports_headers_before_the_body_is_read", "The handler reports allowlisted headers once, before a single body byte is read, bounded by count and length; failures and cancellation report nothing", TestTags.Reliability, async () =>
            {
                DateTime fixedNow = new DateTime(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);
                List<UsageHeaderObservation> seen = new List<UsageHeaderObservation>();
                using GatedBodyStream body = new GatedBodyStream(Encoding.UTF8.GetBytes("streamed body"));
                StubResponseHandler inner = new StubResponseHandler(request =>
                {
                    HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body), RequestMessage = request };
                    response.Headers.TryAddWithoutValidation("x-ratelimit-limit-requests", "100");
                    response.Headers.TryAddWithoutValidation("x-ratelimit-remaining-requests", "0");
                    response.Headers.TryAddWithoutValidation("x-ratelimit-reset-requests", "30s");
                    response.Headers.TryAddWithoutValidation("x-ratelimit-oversized", new string('9', UsageRateLimitHandler.MaximumValueChars + 1));
                    response.Headers.TryAddWithoutValidation("x-api-key", "sk-response-echo-secret-0001");
                    response.Headers.TryAddWithoutValidation("set-cookie", "session=secret-cookie-0001");
                    for (int i = 0; i < 40; i++) response.Headers.TryAddWithoutValidation("x-ratelimit-extra-" + i, i.ToString());
                    return response;
                });

                using (HttpClient client = new HttpClient(new UsageRateLimitHandler(o => { lock (seen) seen.Add(o); }, inner, () => fixedNow)))
                {
                    using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "http://usage-fixture.invalid/v1/chat/completions");
                    using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                    AssertEqual(1, seen.Count, "exactly one observation is reported as soon as the headers arrive");
                    AssertEqual(0, body.Reads, "no body byte was read before the observation was reported");
                    UsageHeaderObservation observation = seen[0];
                    AssertEqual(200, observation.StatusCode, "the status is recorded");
                    AssertEqual(fixedNow, observation.ObservedUtc, "the observation time comes from the handler clock");
                    AssertEqual("usage-fixture.invalid", observation.RequestHost, "the request host is recorded without its path");
                    AssertTrue(observation.Headers.Count <= UsageRateLimitHandler.MaximumHeaders, "the header count is bounded: " + observation.Headers.Count);
                    AssertFalse(observation.Headers.ContainsKey("x-ratelimit-oversized"), "an oversized value is dropped");
                    AssertFalse(observation.Headers.ContainsKey("x-api-key"), "a credential-shaped response header is never kept");
                    AssertFalse(observation.Headers.ContainsKey("set-cookie"), "a cookie is never kept");
                    foreach (KeyValuePair<string, string> header in observation.Headers)
                        AssertFalse(header.Value.Contains("secret", StringComparison.OrdinalIgnoreCase), "no kept header carries a secret: " + header.Key);

                    UsageProbeResult parsed = HeaderUsageCapture.Parse(observation);
                    AssertEqual(UsageSnapshotStatusEnum.Ok, parsed.Status, "the kept headers parse");
                    AssertEqual(fixedNow.AddSeconds(30), parsed.Windows[0].ResetsUtc, "a relative reset counts from the observation time");

                    body.Open();
                    string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    AssertEqual("streamed body", text, "the body is delivered unchanged after capture");
                    AssertEqual(1, seen.Count, "reading the body reports nothing more");
                }

                // A success without any rate-limit header reports nothing; an error status reports status only.
                List<UsageHeaderObservation> quiet = new List<UsageHeaderObservation>();
                HttpStatusCode next = HttpStatusCode.OK;
                StubResponseHandler plain = new StubResponseHandler(request => new HttpResponseMessage(next) { Content = new StringContent("{}"), RequestMessage = request });
                using (HttpClient client = new HttpClient(new UsageRateLimitHandler(o => quiet.Add(o), plain, () => fixedNow)))
                {
                    using (HttpResponseMessage ok = await client.GetAsync("http://usage-fixture.invalid/ok").ConfigureAwait(false)) { }
                    AssertEqual(0, quiet.Count, "a success without rate-limit headers reports nothing");
                    next = HttpStatusCode.ServiceUnavailable;
                    using (HttpResponseMessage failed = await client.GetAsync("http://usage-fixture.invalid/down").ConfigureAwait(false)) { }
                    AssertEqual(1, quiet.Count, "an error status is reported even without headers");
                    AssertEqual(0, quiet[0].Headers.Count, "nothing beyond the status is kept");
                    AssertEqual(UsageSnapshotStatusEnum.Failed, HeaderUsageCapture.Parse(quiet[0]).Status, "a header-less 503 is Failed, never Ok");
                    next = HttpStatusCode.TooManyRequests;
                    using (HttpResponseMessage limited = await client.GetAsync("http://usage-fixture.invalid/limited").ConfigureAwait(false)) { }
                    AssertEqual("ProviderRateLimited", HeaderUsageCapture.Parse(quiet[1]).ErrorCode, "a header-less 429 is a rate-limit failure, never an empty success");
                    AssertNotEqual(UsageSnapshotStatusEnum.Ok, HeaderUsageCapture.Parse(quiet[1]).Status, "a header-less 429 never reads as Ok");
                }

                // A transport failure and a cancelled request report nothing and still surface to the caller.
                List<UsageHeaderObservation> none = new List<UsageHeaderObservation>();
                StubResponseHandler throwing = new StubResponseHandler(request => throw new HttpRequestException("connection refused"));
                using (HttpClient client = new HttpClient(new UsageRateLimitHandler(o => none.Add(o), throwing, () => fixedNow)))
                {
                    await AssertThrowsAsync<HttpRequestException>(() => client.GetAsync("http://usage-fixture.invalid/refused"), "a transport failure surfaces unchanged").ConfigureAwait(false);
                }

                using CancellationTokenSource cts = new CancellationTokenSource();
                StubResponseHandler hanging = new StubResponseHandler(null, async (request, ct) =>
                {
                    await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
                    return new HttpResponseMessage(HttpStatusCode.OK);
                });
                using (HttpClient client = new HttpClient(new UsageRateLimitHandler(o => none.Add(o), hanging, () => fixedNow)))
                {
                    Task<HttpResponseMessage> pending = client.GetAsync("http://usage-fixture.invalid/hang", cts.Token);
                    cts.Cancel();
                    await AssertThrowsAsync<OperationCanceledException>(() => pending, "cancellation surfaces to the caller").ConfigureAwait(false);
                }

                AssertEqual(0, none.Count, "neither a failed transport nor a cancelled request is reported");
                AssertThrows<ArgumentNullException>(() => new UsageRateLimitHandler(null!, new StubResponseHandler(r => new HttpResponseMessage())), "a null sink is refused");
                AssertThrows<ArgumentNullException>(() => new UsageRateLimitHandler(o => { }, null!), "a null inner handler is refused");
            }));

            cases.Add(CaseAsync("reset_converter_bounds_and_header_failures", "Reset bounds are inclusive at -24h and +8d from the observation; offsets convert to UTC; incomplete or malformed headers reject the whole observation", TestTags.Negative, () =>
            {
                DateTime observed = new DateTime(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);
                long epoch = new DateTimeOffset(observed).ToUnixTimeSeconds();
                AssertEqual(observed.AddDays(8), UsageResetConverter.FromEpochSeconds(epoch + 8 * 86400, observed), "exactly eight days ahead is accepted");
                AssertNull(UsageResetConverter.FromEpochSeconds(epoch + 8 * 86400 + 1, observed), "one second beyond eight days is refused");
                AssertEqual(observed.AddHours(-24), UsageResetConverter.FromEpochSeconds(epoch - 86400, observed), "exactly 24 hours in the past is accepted");
                AssertNull(UsageResetConverter.FromEpochSeconds(epoch - 86400 - 1, observed), "one second older than 24 hours is refused");
                AssertNull(UsageResetConverter.FromEpochSeconds(0, observed), "a zero epoch is absent, not 1970");
                AssertNull(UsageResetConverter.FromEpochSeconds(-5, observed), "a negative epoch is refused");
                AssertNull(UsageResetConverter.FromEpochSeconds(Int64.MaxValue, observed), "an unrepresentable epoch is refused, never thrown");
                AssertNull(UsageResetConverter.FromEpochSeconds(null, observed), "null stays null, never zero");
                AssertEqual(observed.AddHours(1), UsageResetConverter.FromRfc3339("2026-10-31T15:00:00+02:00", observed), "an offset timestamp converts to UTC");
                AssertEqual(observed.AddHours(1), UsageResetConverter.FromRfc3339("2026-10-31T13:00:00Z", observed), "a Z timestamp stays absolute");
                AssertNull(UsageResetConverter.FromRfc3339("not-a-timeZ", observed), "garbage ending in Z is refused");
                AssertNull(UsageResetConverter.FromRfc3339("2026-11-30T13:00:00Z", observed), "an absolute reset a month out is refused");
                AssertEqual(observed.AddSeconds(120), UsageResetConverter.FromRetryAfter("120", observed), "numeric Retry-After counts from the observation");
                AssertNull(UsageResetConverter.FromRetryAfter("-1", observed), "a negative Retry-After is refused");
                AssertNull(UsageResetConverter.FromRetryAfter("NaN", observed), "a NaN Retry-After is refused");
                AssertNull(UsageResetConverter.FromRetryAfter("soon", observed), "a free-text Retry-After is refused");
                AssertNull(UsageResetConverter.FromDuration("", observed), "an empty duration is absent");
                AssertNull(UsageResetConverter.FromDuration("5", observed), "a unitless duration is refused");
                AssertNull(UsageResetConverter.FromDuration("200h", observed), "a duration beyond eight days is refused");
                AssertNull(UsageResetConverter.FromRelativeSeconds(Double.PositiveInfinity, observed), "an infinite relative reset is refused");

                Dictionary<string, string> incomplete = new Dictionary<string, string> { { "x-ratelimit-limit-requests", "100" } };
                UsageProbeResult partial = HeaderUsageCapture.Parse(Headers(observed, 200, incomplete));
                AssertEqual(UsageSnapshotStatusEnum.Failed, partial.Status, "a limit without its remaining header is rejected, never a guessed window");
                AssertEqual("MalformedHeaders", partial.ErrorCode, "the rejection is named");
                AssertEqual(0, partial.Windows.Count, "a rejected observation carries no windows");

                Dictionary<string, string> overLimit = new Dictionary<string, string> { { "x-ratelimit-limit-tokens", "100" }, { "x-ratelimit-remaining-tokens", "101" } };
                AssertEqual("MalformedHeaders", HeaderUsageCapture.Parse(Headers(observed, 200, overLimit)).ErrorCode, "remaining above the limit is rejected");
                Dictionary<string, string> zeroLimit = new Dictionary<string, string> { { "x-ratelimit-limit-tokens", "0" }, { "x-ratelimit-remaining-tokens", "0" } };
                AssertEqual("MalformedHeaders", HeaderUsageCapture.Parse(Headers(observed, 200, zeroLimit)).ErrorCode, "a zero limit is rejected, never a divide by zero");
                Dictionary<string, string> signed = new Dictionary<string, string> { { "x-ratelimit-limit-tokens", "100" }, { "x-ratelimit-remaining-tokens", "-1" } };
                AssertEqual("MalformedHeaders", HeaderUsageCapture.Parse(Headers(observed, 200, signed)).ErrorCode, "a signed remaining count is rejected");
                Dictionary<string, string> badReset = new Dictionary<string, string>
                {
                    { "anthropic-ratelimit-requests-limit", "50" },
                    { "anthropic-ratelimit-requests-remaining", "10" },
                    { "anthropic-ratelimit-requests-reset", "2026-10-31T13:00:00" }
                };
                AssertEqual("MalformedHeaders", HeaderUsageCapture.Parse(Headers(observed, 200, badReset)).ErrorCode, "an offset-less reset rejects the observation instead of dropping the reset");
                AssertEqual(UsageSnapshotStatusEnum.Unsupported, HeaderUsageCapture.Parse(null).Status, "no observation at all is Unsupported, never Ok");

                Dictionary<string, string> exhausted = new Dictionary<string, string> { { "x-ratelimit-limit-requests", "100" }, { "x-ratelimit-remaining-requests", "0" }, { "x-ratelimit-reset-requests", "1m" } };
                UsageProbeResult full = HeaderUsageCapture.Parse(Headers(observed, 200, exhausted));
                AssertEqual(100.0, full.Windows[0].UsedPercent, "remaining zero is 100 percent used");
                AssertEqual(observed, full.CapturedUtc, "the observation time is the capture time");
                AssertEqual(UsageCaptureSourceEnum.Headers, full.Source, "the source is Headers");
                return Task.CompletedTask;
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Usage Capture Runtime Parsers",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static string Fixture(string name)
        {
            return File.ReadAllText(Path.Combine(DashboardSource.RepoRoot(), "src", "Test.Shared", "Fixtures", "Usage", name));
        }

        private static Dictionary<string, string> FixtureHeaders(string name)
        {
            Dictionary<string, string>? headers = JsonSerializer.Deserialize<Dictionary<string, string>>(Fixture(name));
            return headers ?? new Dictionary<string, string>();
        }

        private static Dictionary<string, string> OpenAiHeaders()
        {
            Dictionary<string, string> headers = FixtureHeaders("openai-compatible-ratelimit-headers.sanitized.json");
            headers["x-unrelated-header"] = "drop-me";
            return headers;
        }

        private static Dictionary<string, string> AnthropicHeaders()
        {
            // Resets relative to now so they stay inside the accepted bounds whenever the suite runs.
            Dictionary<string, string> headers = FixtureHeaders("anthropic-ratelimit-headers.sanitized.json");
            string reset = DateTime.UtcNow.AddMinutes(1).ToString("yyyy-MM-ddTHH:mm:ssZ");
            foreach (string key in new List<string>(headers.Keys))
            {
                if (key.EndsWith("-reset", StringComparison.Ordinal)) headers[key] = reset;
            }

            return headers;
        }

        private static UsageHeaderObservation Observation(int status, DateTime observed, Dictionary<string, string> headers)
        {
            return new UsageHeaderObservation
            {
                StatusCode = status,
                ObservedUtc = observed,
                Headers = new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase)
            };
        }

        private static CodexRateLimitsReadResponse? Codex(string json)
        {
            return JsonSerializer.Deserialize<CodexRateLimitsReadResponse>(json);
        }

        private static UsageProbeResult Claude(string line, DateTime observed)
        {
            MissionStreamDecodeResult decoded = new ClaudeMissionStreamDecoder().Decode(line, observed);
            AssertEqual(1, decoded.UsageObservations.Count, "the event line yields one observation: " + line);
            AssertEqual(0, decoded.OutputLines.Count, "the event line is never output");
            return decoded.UsageObservations[0];
        }

        private static string Compact(string json)
        {
            return json.Replace("\r", String.Empty).Replace("\n", String.Empty);
        }

        private static UsageProbeHost Host(AgentRuntimeEnum runtime)
        {
            return new UsageProbeHost { TenantId = TenantId, HostId = "host-parsers", Runtime = runtime, Mode = UsageCaptureModeEnum.Auto, TimeoutSeconds = 10 };
        }

        private static UsageProbeHost CustomHost(string stdout)
        {
            string file = Path.Combine(Path.GetTempPath(), "armada-usage-custom-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(file, stdout);
            string command = JsonSerializer.Serialize(new CustomCommand { Executable = "/bin/cat", Arguments = new List<string> { file } });
            return new UsageProbeHost { TenantId = TenantId, HostId = "host-parsers", Runtime = AgentRuntimeEnum.Codex, Mode = UsageCaptureModeEnum.Custom, CommandJson = command, TimeoutSeconds = 10 };
        }

        private static async Task<bool> WaitExitedAsync(int pid)
        {
            if (pid <= 0) return false;
            for (int i = 0; i < 50; i++)
            {
                if (!IsAlive(pid)) return true;
                await Task.Delay(100).ConfigureAwait(false);
            }

            return !IsAlive(pid);
        }

        private static bool IsAlive(int pid)
        {
            try
            {
                using Process process = Process.GetProcessById(pid);
                return !process.HasExited;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private static int CountOccurrences(string haystack, string needle)
        {
            int count = 0;
            int index = 0;
            while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += needle.Length;
            }

            return count;
        }

        private static UsageHeaderObservation Headers(DateTime observed, int status, Dictionary<string, string> headers)
        {
            UsageHeaderObservation observation = new UsageHeaderObservation { StatusCode = status, ObservedUtc = observed };
            foreach (KeyValuePair<string, string> header in headers) observation.Headers[header.Key] = header.Value;
            return observation;
        }

        private static LoggingModule CreateLogging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion

        #region Private-Types

        /// <summary>
        /// An inner HTTP handler that answers from a delegate without any network.
        /// </summary>
        private sealed class StubResponseHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage>? _Respond;
            private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? _RespondAsync;

            public StubResponseHandler(Func<HttpRequestMessage, HttpResponseMessage>? respond, Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? respondAsync = null)
            {
                _Respond = respond;
                _RespondAsync = respondAsync;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                if (_RespondAsync != null) return _RespondAsync(request, cancellationToken);
                return Task.FromResult(_Respond!(request));
            }
        }

        /// <summary>
        /// A body stream that counts reads and refuses to deliver bytes until opened, so a test can prove the
        /// headers were reported before any body read.
        /// </summary>
        private sealed class GatedBodyStream : Stream
        {
            private readonly MemoryStream _Inner;
            private readonly SemaphoreSlim _Gate = new SemaphoreSlim(0, 1);
            private int _Reads = 0;

            public GatedBodyStream(byte[] bytes)
            {
                _Inner = new MemoryStream(bytes);
            }

            public int Reads => Volatile.Read(ref _Reads);

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

            public void Open()
            {
                _Gate.Release();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                Interlocked.Increment(ref _Reads);
                if (!_Gate.Wait(TimeSpan.FromSeconds(10))) throw new IOException("the body was read before the test opened it");
                _Gate.Release();
                return _Inner.Read(buffer, offset, count);
            }

            public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref _Reads);
                if (!await _Gate.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false)) throw new IOException("the body was read before the test opened it");
                _Gate.Release();
                return _Inner.Read(buffer, offset, count);
            }

            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref _Reads);
                if (!await _Gate.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false)) throw new IOException("the body was read before the test opened it");
                _Gate.Release();
                return _Inner.Read(buffer.Span);
            }

            public override void Flush()
            {
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _Inner.Dispose();
                    _Gate.Dispose();
                }

                base.Dispose(disposing);
            }
        }

        private sealed class CustomCommand
        {
            [System.Text.Json.Serialization.JsonPropertyName("executable")]
            public string Executable { get; set; } = String.Empty;

            [System.Text.Json.Serialization.JsonPropertyName("arguments")]
            public List<string> Arguments { get; set; } = new List<string>();
        }

        /// <summary>
        /// A fake <c>codex app-server</c>: a POSIX shell script that records every line it receives and answers
        /// the JSON-RPC exchange according to its mode.
        /// </summary>
        private sealed class FakeAppServer : IDisposable
        {
            private readonly string _Directory;
            private readonly string _Script;
            private readonly string _Log;
            private readonly string _ChildPid;

            public FakeAppServer(string mode)
            {
                _Directory = Path.Combine(Path.GetTempPath(), "armada-usage-appserver-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(_Directory);
                _Script = Path.Combine(_Directory, "app-server.sh");
                _Log = Path.Combine(_Directory, "stdin.log");
                _ChildPid = Path.Combine(_Directory, "child.pid");
                string result = Compact(Fixture("codex-account-ratelimits-read.sanitized.json")).Replace("\"accountId\": null", "\"accountId\": \"acct-parsers-raw\"");
                File.WriteAllText(Path.Combine(_Directory, "result.json"), result + "\n");
                StringBuilder script = new StringBuilder();
                script.Append("dir=\"$(dirname \"$0\")\"\n");
                script.Append("mode=\"" + mode + "\"\n");
                script.Append("while IFS= read -r line; do\n");
                script.Append("  printf '%s\\n' \"$line\" >> \"$dir/stdin.log\"\n");
                script.Append("  case \"$line\" in\n");
                script.Append("    *'\"method\":\"initialize\"'*)\n");
                script.Append("      if [ \"$mode\" = hang ]; then sleep 60 & echo $! > \"$dir/child.pid\"; wait; exit 0; fi\n");
                script.Append("      printf '%s\\n' '{\"method\":\"codex/event\",\"params\":{}}'\n");
                script.Append("      printf '%s\\n' '{\"id\":1,\"result\":{\"userAgent\":\"codex/0.163.0\"}}' ;;\n");
                script.Append("    *'\"method\":\"account/rateLimits/read\"'*)\n");
                script.Append("      case \"$mode\" in\n");
                script.Append("        ok) printf '%s\\n' '{\"method\":\"account/rateLimits/updated\",\"params\":{\"rateLimits\":null}}'; printf '{\"id\":2,\"result\":%s}\\n' \"$(cat \"$dir/result.json\")\" ;;\n");
                script.Append("        autherror) printf '%s\\n' '{\"id\":2,\"error\":{\"code\":-32600,\"message\":\"not logged in: run codex login\"}}' ;;\n");
                script.Append("        error) printf '%s\\n' '{\"id\":2,\"error\":{\"code\":-32603,\"message\":\"backend failed for Bearer abcdefghijklmnop123456\"}}' ;;\n");
                script.Append("        truncated) printf '%s' '{\"id\":2,\"result\":{\"rateLimits\":'; exit 0 ;;\n");
                script.Append("        exit) exit 0 ;;\n");
                script.Append("        oversize) head -c 70000 /dev/zero | tr '\\0' 'a'; printf '\\n' ;;\n");
                script.Append("        garbage) printf '%s\\n' 'this is not json' ;;\n");
                script.Append("        nullresult) printf '%s\\n' '{\"id\":2,\"result\":null}' ;;\n");
                script.Append("      esac ;;\n");
                script.Append("  esac\n");
                script.Append("done\n");
                File.WriteAllText(_Script, script.ToString());
            }

            public CodexUsageProbe Probe(UsageAccountResolver accounts)
            {
                return new CodexUsageProbe(accounts, "/bin/sh", new List<string> { _Script }, () => _CodexObserved);
            }

            public List<string> SentLines()
            {
                if (!File.Exists(_Log)) return new List<string>();
                return new List<string>(File.ReadAllLines(_Log));
            }

            public async Task<int> ChildPidAsync()
            {
                for (int i = 0; i < 30; i++)
                {
                    if (File.Exists(_ChildPid))
                    {
                        string text = File.ReadAllText(_ChildPid).Trim();
                        if (Int32.TryParse(text, out int pid)) return pid;
                    }

                    await Task.Delay(100).ConfigureAwait(false);
                }

                return 0;
            }

            public void Dispose()
            {
                try
                {
                    Directory.Delete(_Directory, true);
                }
                catch
                {
                }
            }
        }

        private sealed class RateLimitFixtureRequest
        {
            public string Method { get; set; } = String.Empty;

            public string Path { get; set; } = String.Empty;

            public string? Authorization { get; set; } = null;

            public string? CustomKey { get; set; } = null;

            public string Body { get; set; } = String.Empty;
        }

        /// <summary>
        /// A local HTTP provider fixture: records each request's method, path, credential headers and body,
        /// and answers with configured rate-limit headers, a provider-shaped body, an error status, or a
        /// streamed body that is aborted before it completes.
        /// </summary>
        private sealed class RateLimitFixtureServer : IDisposable
        {
            public string BaseUrl { get; }

            private readonly System.Net.HttpListener _Listener;
            private readonly Task _Loop;
            private readonly object _Lock = new object();
            private readonly List<RateLimitFixtureRequest> _Requests = new List<RateLimitFixtureRequest>();
            private int _Status = 200;
            private Dictionary<string, string> _Headers = new Dictionary<string, string>();
            private bool _StreamTruncated = false;

            public RateLimitFixtureServer()
            {
                _Listener = TestPorts.StartHttpListener(p => new[] { "http://127.0.0.1:" + p + "/" }, out int port);
                BaseUrl = "http://127.0.0.1:" + port;
                _Loop = Task.Run(LoopAsync);
            }

            public void Reset(int status, Dictionary<string, string> headers, bool streamTruncated)
            {
                lock (_Lock)
                {
                    _Status = status;
                    _Headers = headers;
                    _StreamTruncated = streamTruncated;
                }
            }

            public RateLimitFixtureRequest? LastRequest()
            {
                lock (_Lock) return _Requests.Count == 0 ? null : _Requests[_Requests.Count - 1];
            }

            public void Dispose()
            {
                try { _Listener.Stop(); } catch (ObjectDisposedException) { }
                _Listener.Close();
                try { _Loop.Wait(2000); } catch (AggregateException) { }
            }

            private async Task LoopAsync()
            {
                while (_Listener.IsListening)
                {
                    System.Net.HttpListenerContext context;
                    try { context = await _Listener.GetContextAsync().ConfigureAwait(false); }
                    catch (Exception) { return; }
                    try
                    {
                        await HandleAsync(context).ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        try { context.Response.Abort(); } catch (Exception) { }
                    }
                }
            }

            private async Task HandleAsync(System.Net.HttpListenerContext context)
            {
                string body;
                using (StreamReader reader = new StreamReader(context.Request.InputStream, Encoding.UTF8)) body = await reader.ReadToEndAsync().ConfigureAwait(false);
                string path = context.Request.Url!.AbsolutePath;
                int status;
                Dictionary<string, string> headers;
                bool truncated;
                lock (_Lock)
                {
                    _Requests.Add(new RateLimitFixtureRequest
                    {
                        Method = context.Request.HttpMethod,
                        Path = path,
                        Authorization = context.Request.Headers["Authorization"],
                        CustomKey = context.Request.Headers["X-Fixture-Key"],
                        Body = body
                    });
                    status = _Status;
                    headers = _Headers;
                    truncated = _StreamTruncated;
                }

                context.Response.StatusCode = status;
                foreach (KeyValuePair<string, string> header in headers) context.Response.AddHeader(header.Key, header.Value);
                context.Response.AddHeader("Set-Cookie", "session=fixture-cookie");

                if (truncated)
                {
                    context.Response.ContentType = "text/event-stream";
                    context.Response.SendChunked = true;
                    byte[] chunk = Encoding.UTF8.GetBytes("data: {\"id\":\"c1\",\"object\":\"chat.completion.chunk\",\"model\":\"fixture-model\",\"choices\":[{\"index\":0,\"delta\":{\"content\":\"partial\"},\"finish_reason\":null}]}\n\n");
                    await context.Response.OutputStream.WriteAsync(chunk, 0, chunk.Length).ConfigureAwait(false);
                    await context.Response.OutputStream.FlushAsync().ConfigureAwait(false);
                    await Task.Delay(100).ConfigureAwait(false);
                    context.Response.Abort();
                    return;
                }

                string json;
                if (status != 200) json = "{\"error\":{\"message\":\"fixture failure " + status + "\",\"type\":\"fixture\"}}";
                else if (path.EndsWith("/messages", StringComparison.Ordinal))
                    json = "{\"id\":\"msg_fixture\",\"type\":\"message\",\"role\":\"assistant\",\"model\":\"fixture-model\",\"content\":[{\"type\":\"text\",\"text\":\"fixture reply\"}],\"stop_reason\":\"end_turn\",\"usage\":{\"input_tokens\":1,\"output_tokens\":1}}";
                else
                    json = "{\"id\":\"chatcmpl-fixture\",\"object\":\"chat.completion\",\"created\":1,\"model\":\"fixture-model\",\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\"fixture reply\"},\"finish_reason\":\"stop\"}],\"usage\":{\"prompt_tokens\":1,\"completion_tokens\":1,\"total_tokens\":2}}";
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
                context.Response.Close();
            }
        }

        #endregion
    }
}

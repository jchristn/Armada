namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Runtimes;
    using Armada.Runtimes.Usage;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// AC3 Services.UsageSecrets: credentials and raw identities never leave the credential host or reach a
    /// log, an excerpt or a fixture. The transport capture keeps only the rate-limit allowlist (no request
    /// header, cookie or unrelated response header, bounded values) and a failing sink never fails a call;
    /// runtime probe excerpts are redacted on the way out of the host runner; helper stderr is discarded;
    /// provider account ids leave the host only as an HMAC; and every sanitized fixture is free of
    /// credential-shaped text and raw identities.
    /// </summary>
    public sealed class UsageSecretsSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.UsageSecrets";
        private const string TenantId = "ten_usage_secrets";
        private const string Secret = "abcdefghijklmnop123456";
        private const string Bearer = "Bear" + "er " + Secret;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("transport_capture_keeps_only_the_allowlist", "Only rate-limit headers are kept; request credentials, cookies and long values never are", TestTags.Negative, async () =>
            {
                AssertTrue(UsageRateLimitHandler.IsAllowed("Anthropic-RateLimit-Tokens-Limit"), "allowlisted names match case-insensitively");
                AssertTrue(UsageRateLimitHandler.IsAllowed("retry-after"), "retry-after is allowlisted");
                AssertFalse(UsageRateLimitHandler.IsAllowed("x-ratelimit-"), "a bare prefix is not a header");
                foreach (string name in new[] { "authorization", "x-api-key", "set-cookie", "www-authenticate", "anthropic-organization-id", "openai-organization", "request-id", "" })
                    AssertFalse(UsageRateLimitHandler.IsAllowed(name), name + " is never captured");

                List<UsageHeaderObservation> seen = new List<UsageHeaderObservation>();
                StubHandler inner = new StubHandler(request =>
                {
                    HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
                    response.Headers.TryAddWithoutValidation("x-ratelimit-limit-requests", "500");
                    response.Headers.TryAddWithoutValidation("x-ratelimit-remaining-requests", "499");
                    response.Headers.TryAddWithoutValidation("x-ratelimit-reset-requests", new string('9', 200));
                    response.Headers.TryAddWithoutValidation("set-cookie", "session=" + Secret);
                    response.Headers.TryAddWithoutValidation("www-authenticate", Bearer);
                    response.Headers.TryAddWithoutValidation("openai-organization", "org-" + Secret);
                    for (int i = 0; i < 40; i++) response.Headers.TryAddWithoutValidation("x-ratelimit-extra-" + i, "1");
                    return response;
                });
                using HttpClient client = new HttpClient(new UsageRateLimitHandler(o => seen.Add(o), inner), true);
                using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "https://provider.invalid/v1/chat/completions?key=" + Secret);
                request.Headers.TryAddWithoutValidation("Authorization", Bearer);
                request.Headers.TryAddWithoutValidation("x-api-key", Secret);
                using HttpResponseMessage reply = await client.SendAsync(request).ConfigureAwait(false);
                AssertEqual(1, seen.Count, "one observation per response");
                UsageHeaderObservation observation = seen[0];
                string serialized = JsonSerializer.Serialize(observation);
                AssertFalse(serialized.Contains(Secret, StringComparison.Ordinal), "no credential, cookie, organization or query value is captured: " + serialized);
                AssertEqual("provider.invalid", observation.RequestHost, "only the host name is kept, never the path or query");
                AssertTrue(observation.Headers.Count <= UsageRateLimitHandler.MaximumHeaders, "the header count is bounded");
                AssertFalse(observation.Headers.ContainsKey("x-ratelimit-reset-requests"), "an over-long value is dropped, never truncated into a different number");
                AssertEqual("500", observation.Headers["x-ratelimit-limit-requests"], "the allowlisted value is kept");
            }));

            cases.Add(CaseAsync("failing_sink_never_fails_the_inference_call", "A throwing capture sink leaves the provider response untouched", TestTags.Reliability, async () =>
            {
                StubHandler inner = new StubHandler(request =>
                {
                    HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("{\"error\":\"slow down\"}") };
                    response.Headers.TryAddWithoutValidation("retry-after", "3");
                    return response;
                });
                using HttpClient client = new HttpClient(new UsageRateLimitHandler(o => throw new InvalidOperationException("sink exploded"), inner), true);
                using HttpResponseMessage reply = await client.GetAsync("https://provider.invalid/v1/models").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.TooManyRequests, reply.StatusCode, "the provider status reaches the caller");
                AssertEqual("{\"error\":\"slow down\"}", await reply.Content.ReadAsStringAsync().ConfigureAwait(false), "the body is untouched");
            }));

            cases.Add(CaseAsync("probe_excerpts_are_redacted_and_stderr_discarded", "Runtime probe excerpts are redacted by the host runner and helper stderr never surfaces", TestTags.Negative, async () =>
            {
                UsageAccountResolver accounts = new UsageAccountResolver("host-secrets-1", new string('7', 64));
                LeakyProbe leaky = new LeakyProbe();
                UsageProbeHostRunner runner = new UsageProbeHostRunner(CreateLogging(), accounts, runtime => leaky);
                UsageProbeResult result = await runner.CaptureAsync(Host()).ConfigureAwait(false);
                AssertFalse(result.RawExcerpt!.Contains(Secret, StringComparison.Ordinal), "the host runner redacts a runtime probe excerpt");
                AssertContains("[REDACTED]", result.RawExcerpt!, "the redaction is visible");
                AssertTrue(Encoding.UTF8.GetByteCount(result.RawExcerpt!) <= 2048, "the excerpt is capped");

                if (OperatingSystem.IsWindows()) return;
                string script = Path.Combine(Path.GetTempPath(), "armada-usage-secrets-" + Guid.NewGuid().ToString("N") + ".sh");
                File.WriteAllText(script,
                    "echo \"ANTHROPIC_API_KEY=sk-ant-" + Secret + "\" 1>&2\n" +
                    "while IFS= read -r line; do\n" +
                    "  case \"$line\" in\n" +
                    "    *initialize\\\"*) printf '%s\\n' '{\"id\":1,\"result\":{}}' ;;\n" +
                    "    *rateLimits*) printf '%s\\n' '{\"id\":2,\"error\":{\"code\":-32603,\"message\":\"upstream rejected " + Bearer + "\"}}' ;;\n" +
                    "  esac\n" +
                    "done\n");
                try
                {
                    CodexUsageProbe codex = new CodexUsageProbe(accounts, "/bin/sh", new List<string> { script });
                    UsageProbeResult codexResult = await codex.CaptureAsync(Host()).ConfigureAwait(false);
                    AssertEqual("ProviderError", codexResult.ErrorCode, "the provider error is reported with its stable code");
                    string serialized = codexResult.Serialize();
                    AssertFalse(serialized.Contains(Secret, StringComparison.Ordinal), "neither the stderr credential nor the error credential leaks: " + serialized);
                    AssertFalse(serialized.Contains("ANTHROPIC_API_KEY", StringComparison.Ordinal), "helper stderr is discarded, never an excerpt");
                }
                finally
                {
                    File.Delete(script);
                }
            }));

            cases.Add(CaseAsync("account_ids_leave_the_host_only_as_an_hmac", "Raw provider account ids never appear in a probe result; the key is stable and tenant-scoped", TestTags.Positive, () =>
            {
                UsageAccountResolver accounts = new UsageAccountResolver("host-secrets-2", new string('8', 64));
                string? key = accounts.DeriveAccountKey(TenantId, AgentRuntimeEnum.Codex, "acct-raw-identity");
                AssertNotNull(key, "an account id derives a key");
                AssertFalse(key!.Contains("acct-raw-identity", StringComparison.Ordinal), "the key is opaque");
                AssertEqual(key, accounts.DeriveAccountKey(TenantId, AgentRuntimeEnum.Codex, "acct-raw-identity"), "the key is stable for the same identity");
                AssertFalse(key == accounts.DeriveAccountKey("ten_other", AgentRuntimeEnum.Codex, "acct-raw-identity"), "the tenant namespaces the key");
                AssertNull(accounts.DeriveAccountKey(TenantId, AgentRuntimeEnum.Codex, null), "a missing identity never becomes a shared unknown key");

                UsageProbeResult claude = ClaudeRateLimitUsage.ToResult(new Armada.Core.Protocol.ClaudeRateLimitEvent
                {
                    Type = "rate_limit_event",
                    Info = new Armada.Core.Protocol.ClaudeRateLimitInfo { Status = "allowed", RateLimitType = "five_hour", Utilization = 0.1 }
                }, DateTime.UtcNow);
                AssertNull(claude.AccountKey, "a passive harness event never invents an account identity");
                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("sanitized_fixtures_hold_no_credentials_or_identities", "Every usage fixture is free of credential-shaped text, emails and raw account ids", TestTags.Negative, () =>
            {
                string directory = Path.Combine(DashboardSource.RepoRoot(), "src", "Test.Shared", "Fixtures", "Usage");
                string[] files = Directory.GetFiles(directory);
                AssertTrue(files.Length >= 8, "the fixtures are present");
                Regex credential = new Regex("(sk-[A-Za-z0-9_-]{8,}|eyJ[A-Za-z0-9_-]{10,}|[Bb]earer\\s+[A-Za-z0-9._-]{8,}|[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\\.[A-Za-z]{2,})");
                foreach (string file in files)
                {
                    string text = File.ReadAllText(file);
                    AssertFalse(credential.IsMatch(text), Path.GetFileName(file) + " contains credential-shaped or identity text");
                    SecretRedactor.Redact(text, out bool redacted);
                    AssertFalse(redacted, Path.GetFileName(file) + " contains text the secret redactor would remove");
                }

                string codex = File.ReadAllText(Path.Combine(directory, "codex-account-ratelimits-read.sanitized.json"));
                AssertContains("\"accountId\": null", codex, "the Codex fixture never carries a raw account id");
                return Task.CompletedTask;
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Usage Capture Secrets",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static UsageProbeHost Host()
        {
            return new UsageProbeHost { TenantId = TenantId, HostId = "host-secrets", Runtime = AgentRuntimeEnum.Codex, Mode = UsageCaptureModeEnum.Auto, TimeoutSeconds = 10 };
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

        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _Respond;

            public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
            {
                _Respond = respond;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                HttpResponseMessage response = _Respond(request);
                response.RequestMessage = request;
                return Task.FromResult(response);
            }
        }

        private sealed class LeakyProbe : IUsageProbe
        {
            public Task<UsageProbeResult> CaptureAsync(UsageProbeHost host, CancellationToken token = default)
            {
                UsageProbeResult result = UsageProbeResult.Failed("ProviderError", "provider said: " + Bearer + " " + new string('z', 4000));
                return Task.FromResult(result);
            }
        }

        #endregion
    }
}

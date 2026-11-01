namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using Armada.Runtimes.Usage;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>Default-on upgrade, quota parser and bounded raw retention regressions.</summary>
    public class UsagePhase6Suite : IArmadaTestSuite
    {
        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            const string suite = "Services.UsagePhase6";
            return new TestSuiteDescriptor(suite, "Phase 6 capture", new List<TestCaseDescriptor>
            {
                new TestCaseDescriptor(suite, "settings_upgrade_once", "Legacy false choices reset once; revision 2 preserves explicit opt-outs and null write-back", async token =>
                {
                    string directory = TestTemp.NewDirectory("usage_revision");
                    string path = Path.Combine(directory, "settings.json");
                    try
                    {
                        foreach (string json in new[] { "{}", "{\"UsageCapture\":null}", "{\"UsageCapture\":{\"Enabled\":false,\"AllowPtyFallback\":false}}" })
                        {
                            await File.WriteAllTextAsync(path, json, token).ConfigureAwait(false);
                            ArmadaSettings settings = await ArmadaSettings.LoadAsync(path).ConfigureAwait(false);
                            AssertTrue(settings.UsageCapture.EffectiveEnabled);
                            AssertTrue(settings.UsageCapture.EffectiveAllowPtyFallback);
                            AssertNull(settings.UsageCapture.Enabled);
                            AssertEqual(2, settings.UsageCapture.DefaultsRevision);
                            ArmadaSettings reread = await ArmadaSettings.LoadAsync(path).ConfigureAwait(false);
                            AssertNull(reread.UsageCapture.Enabled);
                        }
                        await File.WriteAllTextAsync(path, "{\"UsageCapture\":{\"Enabled\":false,\"AllowPtyFallback\":false,\"DefaultsRevision\":2}}", token).ConfigureAwait(false);
                        ArmadaSettings optedOut = await ArmadaSettings.LoadAsync(path).ConfigureAwait(false);
                        AssertFalse(optedOut.UsageCapture.EffectiveEnabled);
                        AssertFalse(optedOut.UsageCapture.EffectiveAllowPtyFallback);
                        AssertTrue((await ArmadaSettings.LoadAsync(Path.Combine(directory, "missing.json")).ConfigureAwait(false)).UsageCapture.EffectiveEnabled);
                        AssertTrue(JsonSerializer.Deserialize<Captain>("{\"UsageCaptureEnabled\":null}")!.UsageCaptureActive);
                        AssertFalse(JsonSerializer.Deserialize<Captain>("{\"UsageCaptureEnabled\":false}")!.UsageCaptureActive);
                    }
                    finally { Directory.Delete(directory, true); }
                }),
                new TestCaseDescriptor(suite, "quota_parsers", "Claude and Gemini keep partial values, skip unknown shapes and reject malformed or impossible quotas", token =>
                {
                    DateTime now = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
                    UsageProbeResult claude = UsageQuotaParsers.Claude("{\"five_hour\":{\"utilization\":75,\"resets_at\":\"2026-11-01T05:00:00Z\"},\"seven_day\":null,\"limits\":[],\"member_dashboard_available\":true}", now);
                    AssertEqual(UsageSnapshotStatusEnum.Ok, claude.Status);
                    AssertEqual(1, claude.Windows.Count);
                    AssertEqual(75.0, claude.Windows[0].UsedPercent!.Value);
                    AssertEqual(UsageCaptureSourceEnum.OAuthEndpoint, claude.Source);
                    AssertNull(claude.AccountKey, "parsers cannot invent host identity");
                    UsageProbeResult scoped = UsageQuotaParsers.Claude("{\"seven_day_sonnet\":{\"utilization\":100},\"seven_day_cowork\":{\"utilization\":100},\"seven_day_oauth_apps\":{\"utilization\":100}}", now);
                    AssertEqual(1, scoped.Windows.Count, "unmapped application buckets never become global CLI quota");
                    AssertEqual("sonnet", scoped.Windows[0].Model, "model-family quota never becomes a global window");
                    UsageProbeResult gemini = UsageQuotaParsers.Gemini("{\"buckets\":[{\"modelId\":\"gemini-test\",\"tokenType\":\"REQUESTS\",\"remainingFraction\":0.25,\"resetTime\":\"2026-11-02T00:00:00Z\"},{\"modelId\":\"gemini-unknown\"}]}", now);
                    AssertEqual(UsageSnapshotStatusEnum.Ok, gemini.Status);
                    AssertEqual(75.0, gemini.Windows[0].UsedPercent!.Value);
                    AssertNull(gemini.Windows[1].UsedPercent);
                    foreach (string json in new[] { "{", "null", "[]", "{\"buckets\":null}" }) AssertFalse(UsageQuotaParsers.Gemini(json, now).Status == UsageSnapshotStatusEnum.Ok);
                    AssertEqual(UsageSnapshotStatusEnum.Failed, UsageQuotaParsers.Claude("{\"five_hour\":{\"utilization\":101}}", now).Status);
                    AssertEqual(UsageSnapshotStatusEnum.Failed, UsageQuotaParsers.Gemini("{\"buckets\":[{\"modelId\":\"test\",\"remainingFraction\":-1}]}", now).Status);
                    AssertEqual(UsageSnapshotStatusEnum.Unsupported, UsageQuotaParsers.Claude("{}", now).Status);
                    AssertEqual(UsageSnapshotStatusEnum.Failed, UsageQuotaParsers.Claude("{\"seven_day\":{\"utilization\":100,\"resets_at\":\"2026-11-15T00:00:00Z\"}}", now).Status, "a weekly reset cannot hold a captain beyond the supported eight-day horizon");
                    AssertEqual(UsageSnapshotStatusEnum.Unsupported, InteractiveUsageReader.ParseScreen("Session tokens: 1000", AgentRuntimeEnum.Gemini, now).Status);
                    UsageProbeResult quotaUi = InteractiveUsageReader.ParseScreen("75% used (Limit resets in 3h 30m)\nUsage limit: 1000\nUsage limits span all sessions and reset daily.", AgentRuntimeEnum.Gemini, now);
                    AssertEqual(UsageSnapshotStatusEnum.Ok, quotaUi.Status);
                    AssertEqual(75.0, quotaUi.Windows[0].UsedPercent!.Value);
                    AssertEqual(now.AddHours(3.5), quotaUi.Windows[0].ResetsUtc!.Value);
                    AssertFalse(InteractiveUsageReader.IsIdle("Do you trust this folder?", true));
                    return Task.CompletedTask;
                }),
                new TestCaseDescriptor(suite, "quota_transports", "Active readers call quota-only operations, HMAC on host and preserve authentication and throttling errors", async token =>
                {
                    UsageAccountResolver accounts = new UsageAccountResolver("quota-host", new string('3', 64));
                    UsageProbeHost host = new UsageProbeHost { HostId = "quota-host", TenantId = "ten_quota", Runtime = AgentRuntimeEnum.ClaudeCode, AllowPty = false };
                    UsageQuotaHttpHandler handler = new UsageQuotaHttpHandler();
                    handler.Responses.Enqueue("{\"five_hour\":{\"utilization\":10}}");
                    using HttpClient http = new HttpClient(handler);
                    Func<System.Threading.CancellationToken, Task<UsageProbeCredential>> credentials = ignored => Task.FromResult(new UsageProbeCredential { AccessToken = "fixture-only", AccountIdentity = "provider-fixture", Profile = "fixture-profile" });
                    UsageProbeResult claude = await new ClaudeUsageProbe(accounts, http, credentials).CaptureAsync(host, token).ConfigureAwait(false);
                    AssertEqual(UsageSnapshotStatusEnum.Ok, claude.Status);
                    AssertFalse(claude.AccountKey!.Contains("provider-fixture", StringComparison.Ordinal));
                    AssertEqual("GET /api/oauth/usage", handler.Paths[0]);
                    handler.Status = HttpStatusCode.TooManyRequests;
                    UsageProbeResult throttled = await new ClaudeUsageProbe(accounts, http, credentials).CaptureAsync(host, token).ConfigureAwait(false);
                    AssertEqual("ProviderThrottled", throttled.ErrorCode);
                    AssertTrue(throttled.RetryAfterUtc > DateTime.UtcNow.AddMinutes(9));
                    handler.Status = HttpStatusCode.Unauthorized;
                    AssertEqual(UsageSnapshotStatusEnum.AuthMissing, (await new ClaudeUsageProbe(accounts, http, credentials).CaptureAsync(host, token).ConfigureAwait(false)).Status);
                    handler.Status = HttpStatusCode.OK;
                    handler.Responses.Enqueue("{\"buckets\":[{\"modelId\":\"fixture\",\"remainingFraction\":0.5}]}");
                    host.Runtime = AgentRuntimeEnum.Gemini;
                    string? previous = Environment.GetEnvironmentVariable("GOOGLE_CLOUD_PROJECT");
                    try
                    {
                        Environment.SetEnvironmentVariable("GOOGLE_CLOUD_PROJECT", "fixture-project");
                        UsageProbeResult gemini = await new GeminiUsageProbe(accounts, http, credentials).CaptureAsync(host, token).ConfigureAwait(false);
                        AssertEqual(UsageSnapshotStatusEnum.Ok, gemini.Status);
                        AssertEqual("POST /v1internal:retrieveUserQuota", handler.Paths[handler.Paths.Count - 1]);
                        Environment.SetEnvironmentVariable("GOOGLE_CLOUD_PROJECT", "another-fixture-project");
                        handler.Responses.Enqueue("{\"buckets\":[{\"modelId\":\"fixture\",\"remainingFraction\":0.5}]}");
                        UsageProbeResult anotherProject = await new GeminiUsageProbe(accounts, http, credentials).CaptureAsync(host, token).ConfigureAwait(false);
                        AssertFalse(gemini.AccountKey == anotherProject.AccountKey, "project budgets remain separate account series");
                    }
                    finally { Environment.SetEnvironmentVariable("GOOGLE_CLOUD_PROJECT", previous); }
                }),
                new TestCaseDescriptor(suite, "gemini_credential_interop", "Gemini 0.66 encrypted credentials interoperate with Node, reject tampering and never return unrelated secrets", token =>
                {
                    string path = Path.Combine(DashboardSource.RepoRoot(), "src", "Test.Shared", "Fixtures", "Usage", "gemini-encrypted-credentials-v066.sanitized.json");
                    GeminiCredentialFixture fixture = JsonSerializer.Deserialize<GeminiCredentialFixture>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
                    string encrypted = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(fixture.EncryptedBase64));
                    string? account = GeminiCredentialStore.ReadEncryptedAccount(encrypted, fixture.HostName, fixture.UserName);
                    AssertNotNull(account);
                    AssertContains("fixture-token", account!);
                    AssertFalse(account!.Contains("must-not-be-returned", StringComparison.Ordinal));
                    AssertNull(GeminiCredentialStore.ReadEncryptedAccount(encrypted, "another-host", fixture.UserName));
                    AssertNull(GeminiCredentialStore.ReadEncryptedAccount((encrypted[0] == '0' ? "1" : "0") + encrypted.Substring(1), fixture.HostName, fixture.UserName));
                    AssertNull(GeminiCredentialStore.ReadEncryptedAccount("malformed", fixture.HostName, fixture.UserName));
                    return Task.CompletedTask;
                }),
                new TestCaseDescriptor(suite, "gemini_active_identity", "A stale stored login cannot be attributed to a different active Gemini profile", async token =>
                {
                    string directory = TestTemp.NewDirectory("gemini_identity");
                    Dictionary<string, string?> previous = new Dictionary<string, string?>();
                    foreach (string name in new[] { "GEMINI_CLI_HOME", "GEMINI_API_KEY", "GOOGLE_API_KEY", "GOOGLE_GENAI_USE_VERTEXAI", "GOOGLE_CLOUD_PROJECT" }) previous[name] = Environment.GetEnvironmentVariable(name);
                    try
                    {
                        string config = Path.Combine(directory, ".gemini");
                        Directory.CreateDirectory(config);
                        await File.WriteAllTextAsync(Path.Combine(config, "oauth_creds.json"), "{\"access_token\":\"fixture-token\"}", token).ConfigureAwait(false);
                        await File.WriteAllTextAsync(Path.Combine(config, "google_accounts.json"), "{\"active\":\"fixture-profile\"}", token).ConfigureAwait(false);
                        foreach (string name in previous.Keys) Environment.SetEnvironmentVariable(name, null);
                        Environment.SetEnvironmentVariable("GEMINI_CLI_HOME", directory);
                        Environment.SetEnvironmentVariable("GOOGLE_CLOUD_PROJECT", "fixture-project");
                        UsageQuotaHttpHandler handler = new UsageQuotaHttpHandler();
                        handler.Responses.Enqueue("{\"id\":\"wrong-account\",\"email\":\"other-profile\"}");
                        using HttpClient http = new HttpClient(handler);
                        UsageAccountResolver accounts = new UsageAccountResolver("identity-host", new string('5', 64));
                        UsageProbeHost host = new UsageProbeHost { HostId = "identity-host", TenantId = "ten_identity", Runtime = AgentRuntimeEnum.Gemini };
                        AssertEqual("AccountMismatch", (await new GeminiUsageProbe(accounts, http).CaptureAsync(host, token).ConfigureAwait(false)).ErrorCode);
                        AssertEqual(1, handler.Paths.Count, "mismatch never reads or persists quota");
                        handler.Responses.Enqueue("{\"id\":\"fixture-account\",\"email\":\"fixture-profile\"}");
                        handler.Responses.Enqueue("{\"buckets\":[{\"modelId\":\"fixture\",\"remainingFraction\":0.5}]}");
                        UsageProbeResult captured = await new GeminiUsageProbe(accounts, http).CaptureAsync(host, token).ConfigureAwait(false);
                        AssertEqual(UsageSnapshotStatusEnum.Ok, captured.Status);
                        AssertFalse(captured.Serialize().Contains("fixture-profile", StringComparison.Ordinal));
                        AssertEqual("GET /oauth2/v2/userinfo", handler.Paths[1]);
                    }
                    finally
                    {
                        foreach (KeyValuePair<string, string?> entry in previous) Environment.SetEnvironmentVariable(entry.Key, entry.Value);
                        Directory.Delete(directory, true);
                    }
                }),
                new TestCaseDescriptor(suite, "selected_api_modes", "API-key CLI selections never borrow an older OAuth login or execute a key helper", async token =>
                {
                    string directory = TestTemp.NewDirectory("usage_cli_modes");
                    string? previousClaude = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
                    string? previousGemini = Environment.GetEnvironmentVariable("GEMINI_CLI_HOME");
                    try
                    {
                        Directory.CreateDirectory(Path.Combine(directory, ".gemini"));
                        await File.WriteAllTextAsync(Path.Combine(directory, "settings.json"), "{\"apiKeyHelper\":\"never-execute-this-helper\"}", token).ConfigureAwait(false);
                        await File.WriteAllTextAsync(Path.Combine(directory, ".gemini", "settings.json"), "{\"security\":{\"auth\":{\"selectedType\":\"gemini-api-key\"}}}", token).ConfigureAwait(false);
                        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", directory);
                        Environment.SetEnvironmentVariable("GEMINI_CLI_HOME", directory);
                        UsageQuotaHttpHandler handler = new UsageQuotaHttpHandler();
                        using HttpClient http = new HttpClient(handler);
                        UsageAccountResolver accounts = new UsageAccountResolver("mode-host", new string('4', 64));
                        UsageProbeHost host = new UsageProbeHost { HostId = "mode-host", TenantId = "ten_modes", Runtime = AgentRuntimeEnum.ClaudeCode };
                        AssertEqual("ApiKeyMode", (await new ClaudeUsageProbe(accounts, http).CaptureAsync(host, token).ConfigureAwait(false)).ErrorCode);
                        host.Runtime = AgentRuntimeEnum.Gemini;
                        AssertEqual("ApiKeyMode", (await new GeminiUsageProbe(accounts, http).CaptureAsync(host, token).ConfigureAwait(false)).ErrorCode);
                        AssertEqual(0, handler.Paths.Count, "neither selection sends a quota request");
                    }
                    finally
                    {
                        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", previousClaude);
                        Environment.SetEnvironmentVariable("GEMINI_CLI_HOME", previousGemini);
                        Directory.Delete(directory, true);
                    }
                }),
                new TestCaseDescriptor(suite, "raw_retention_batches", "Raw pruning crosses batch boundaries, is retry-safe, and preserves latest-state observation age", async token =>
                {
                    using TestDatabase db = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                    DateTime now = DateTime.UtcNow;
                    for (int index = 0; index < 501; index++)
                        await db.Driver.UsageSnapshots.CreateAsync(new UsageSnapshot { TenantId = "ten_retention6", HostId = "host6", AccountKey = "account6", CapturedUtc = now.AddDays(-8), Runtime = AgentRuntimeEnum.ClaudeCode }, new List<string> { "cpt_retention6" }, token).ConfigureAwait(false);
                    UsageSnapshot kept = await db.Driver.UsageSnapshots.CreateAsync(new UsageSnapshot { TenantId = "ten_retention6", HostId = "host6", AccountKey = "account6", CapturedUtc = now.AddDays(-7), Runtime = AgentRuntimeEnum.ClaudeCode }, null, token).ConfigureAwait(false);
                    await db.Driver.UsageSnapshots.UpsertLatestStateAsync(new UsageLatestState { TenantId = "ten_retention6", HostId = "host6", AccountKey = "account6", Runtime = AgentRuntimeEnum.ClaudeCode, LastGoodSnapshotId = kept.Id, LastGoodCapturedUtc = kept.CapturedUtc }, token).ConfigureAwait(false);
                    RetentionService service = new RetentionService(db.Driver, new ArmadaSettings(), new LoggingModule(), () => now);
                    AssertEqual(501, await service.PruneUsageSnapshotsAsync(token).ConfigureAwait(false));
                    AssertEqual(0, await service.PruneUsageSnapshotsAsync(token).ConfigureAwait(false));
                    AssertNotNull(await db.Driver.UsageSnapshots.ReadAsync(kept.Id, "ten_retention6", token).ConfigureAwait(false));
                    UsageLatestState? latest = await db.Driver.UsageSnapshots.ReadLatestStateAsync("ten_retention6", "host6", AgentRuntimeEnum.ClaudeCode, "account6", token).ConfigureAwait(false);
                    AssertEqual(kept.CapturedUtc, latest!.LastGoodCapturedUtc!.Value);
                })
            });
        }
    }
}

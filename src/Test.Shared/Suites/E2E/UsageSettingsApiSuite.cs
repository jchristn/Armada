namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Reflection;
    using System.Text;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using Armada.Server;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>AC5 prerequisite: usage settings over the real authenticated HTTP settings path.</summary>
    public sealed class UsageSettingsApiSuite : IArmadaTestSuite
    {
        private const string Suite = "E2E.UsageSettingsApi";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Suite, "Usage capture settings API", new List<TestCaseDescriptor>
            {
                new TestCaseDescriptor(Suite, "get_defaults", "GET exposes the disabled-by-default capture settings group", async ct =>
                {
                    string dir = TestTemp.NewDirectory("usage_settings_defaults");
                    using InProcessArmadaServer server = await InProcessArmadaServer.StartAsync(dir,
                        new DatabaseSettings { Type = DatabaseTypeEnum.Sqlite, Filename = Path.Combine(dir, "armada.db") }).ConfigureAwait(false);
                    using HttpResponseMessage response = await server.Client.GetAsync("/api/v1/settings", ct).ConfigureAwait(false);
                    UsageCaptureSettings actual = await ReadAsync(response).ConfigureAwait(false);
                    AssertFalse(actual.Enabled); AssertFalse(actual.AllowPtyFallback);
                    AssertEqual(60, actual.MinimumIntervalSeconds); AssertEqual(20, actual.ProbeTimeoutSeconds);
                    AssertEqual(4, actual.MaximumConcurrentProbes); AssertEqual(1, actual.MaximumConcurrentProbesPerHost);
                    AssertEqual(3600, actual.MaximumBackoffSeconds); AssertEqual(2048, actual.MaximumExcerptBytes);
                    AssertEqual(7, actual.RawSnapshotRetentionDays); AssertEqual(90, actual.RollupRetentionDays);
                }),
                new TestCaseDescriptor(Suite, "put_persist_apply_and_errors", "PUT binds, applies in place, reschedules, persists and rejects invalid or unauthorized writes", async ct =>
                {
                    string dir = TestTemp.NewDirectory("usage_settings_put");
                    using InProcessArmadaServer server = await InProcessArmadaServer.StartAsync(dir,
                        new DatabaseSettings { Type = DatabaseTypeEnum.Sqlite, Filename = Path.Combine(dir, "armada.db") }).ConfigureAwait(false);
                    UsageCaptureSettings live = server.Settings.UsageCapture;
                    ArmadaServer runtime = (ArmadaServer)typeof(InProcessArmadaServer).GetField("_Server", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(server)!;
                    UsageCaptureService capture = (UsageCaptureService)typeof(ArmadaServer).GetField("_UsageCapture", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(runtime)!;
                    long generation = capture.Generation;
                    using (HttpResponseMessage response = await server.Client.PutAsync("/api/v1/settings",
                        JsonHelper.ToJsonContent(new { UsageCapture = new { Enabled = true } }), ct).ConfigureAwait(false))
                    {
                        UsageCaptureSettings actual = await ReadAsync(response).ConfigureAwait(false);
                        AssertTrue(actual.Enabled, "minimal request binds Enabled");
                        AssertEqual(60, actual.MinimumIntervalSeconds, "omitted members take model defaults");
                    }
                    AssertTrue(Object.ReferenceEquals(live, server.Settings.UsageCapture), "retained passive/access references stay live");
                    AssertTrue(live.Enabled); AssertTrue(capture.Generation > generation, "update invalidates in-flight work immediately");
                    using (HttpResponseMessage response = await server.Client.GetAsync("/api/v1/settings", ct).ConfigureAwait(false))
                        AssertTrue((await ReadAsync(response).ConfigureAwait(false)).Enabled, "GET reload observes enabled capture");
                    ArmadaSettings persisted = await ArmadaSettings.LoadAsync(Path.Combine(dir, "settings.json")).ConfigureAwait(false);
                    AssertTrue(persisted.UsageCapture.Enabled, "persisted enable survives file reload");

                    using (HttpResponseMessage response = await server.Client.PutAsync("/api/v1/settings", JsonHelper.ToJsonContent(new
                    {
                        UsageCapture = new { Enabled = true, MinimumIntervalSeconds = 1, AllowPtyFallback = true, ProbeTimeoutSeconds = 999,
                            MaximumConcurrentProbes = 0, MaximumConcurrentProbesPerHost = 999, MaximumBackoffSeconds = -1,
                            MaximumExcerptBytes = 999999, RawSnapshotRetentionDays = 0, RollupRetentionDays = 99999 }
                    }), ct).ConfigureAwait(false))
                    {
                        UsageCaptureSettings bounded = await ReadAsync(response).ConfigureAwait(false);
                        AssertEqual(60, bounded.MinimumIntervalSeconds); AssertEqual(300, bounded.ProbeTimeoutSeconds);
                        AssertEqual(1, bounded.MaximumConcurrentProbes); AssertEqual(64, bounded.MaximumConcurrentProbesPerHost);
                        AssertEqual(60, bounded.MaximumBackoffSeconds); AssertEqual(65536, bounded.MaximumExcerptBytes);
                        AssertEqual(1, bounded.RawSnapshotRetentionDays); AssertEqual(1825, bounded.RollupRetentionDays);
                        AssertTrue(bounded.AllowPtyFallback);
                    }
                    persisted = await ArmadaSettings.LoadAsync(Path.Combine(dir, "settings.json")).ConfigureAwait(false);
                    AssertEqual(JsonSerializer.Serialize(live), JsonSerializer.Serialize(persisted.UsageCapture), "all effective bounds persist");
                    generation = capture.Generation;
                    foreach (string body in new[] { "{}", "{\"UsageCapture\":null}", "{\"MaxCaptains\":3}" })
                    {
                        using HttpResponseMessage response = await server.Client.PutAsync("/api/v1/settings", new StringContent(body, Encoding.UTF8, "application/json"), ct).ConfigureAwait(false);
                        AssertTrue((await ReadAsync(response).ConfigureAwait(false)).AllowPtyFallback, "omission/null leaves prior group intact");
                    }
                    AssertEqual(generation, capture.Generation, "unrelated edits do not invalidate probes");
                    int captains = server.Settings.MaxCaptains;
                    foreach (string body in new[] { "null", "{", "{\"MaxCaptains\":10,\"UsageCapture\":{\"Enabled\":\"yes\"}}",
                        "{\"UsageCapture\":{\"ProbeTimeoutSeconds\":null}}", "{\"UsageCapture\":{\"MinimumIntervalSeconds\":1.5}}" })
                    {
                        using HttpResponseMessage response = await server.Client.PutAsync("/api/v1/settings", new StringContent(body, Encoding.UTF8, "application/json"), ct).ConfigureAwait(false);
                        AssertStatusCode(HttpStatusCode.BadRequest, response, "invalid JSON/type rejected");
                        AssertEqual(captains, server.Settings.MaxCaptains, "no partial mutation");
                        AssertTrue(live.Enabled); AssertEqual(generation, capture.Generation);
                    }
                    E2ETenantUser tenant = await E2ETenantUser.CreateAsync(server.Client, "usage-settings-admin").ConfigureAwait(false);
                    using HttpClient tenantClient = tenant.CreateClient(server.BaseUrl);
                    using HttpClient anonymous = new HttpClient { BaseAddress = new Uri(server.BaseUrl) };
                    foreach ((HttpClient client, HttpStatusCode status) in new[] { (tenantClient, HttpStatusCode.Forbidden), (anonymous, HttpStatusCode.Unauthorized) })
                    {
                        using HttpResponseMessage read = await client.GetAsync("/api/v1/settings", ct).ConfigureAwait(false);
                        AssertStatusCode(status, read, "server-wide settings require global admin");
                        using HttpResponseMessage write = await client.PutAsync("/api/v1/settings", JsonHelper.ToJsonContent(new { UsageCapture = new { Enabled = false } }), ct).ConfigureAwait(false);
                        AssertStatusCode(status, write, "unauthorized update rejected");
                        AssertTrue(live.Enabled); AssertEqual(generation, capture.Generation);
                    }
                    using (HttpResponseMessage response = await server.Client.PutAsync("/api/v1/settings", JsonHelper.ToJsonContent(new { UsageCapture = new { Enabled = false } }), ct).ConfigureAwait(false))
                        AssertFalse((await ReadAsync(response).ConfigureAwait(false)).Enabled, "explicit disable applies");
                    AssertFalse(live.Enabled, "existing passive/access references observe disable");
                    AssertTrue(capture.Generation > generation, "disable cancels old generation immediately");
                    persisted = await ArmadaSettings.LoadAsync(Path.Combine(dir, "settings.json")).ConfigureAwait(false);
                    AssertFalse(persisted.UsageCapture.Enabled);
                })
            });
        }

        private static async Task<UsageCaptureSettings> ReadAsync(HttpResponseMessage response)
        {
            AssertStatusCode(HttpStatusCode.OK, response, "settings HTTP request");
            using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            List<JsonProperty> groups = json.RootElement.EnumerateObject().Where(p => String.Equals(p.Name, "UsageCapture", StringComparison.OrdinalIgnoreCase)).ToList();
            AssertEqual(1, groups.Count, "settings response includes the UsageCapture group");
            return JsonHelper.Deserialize<UsageCaptureSettings>(groups[0].Value.GetRawText());
        }
    }
}

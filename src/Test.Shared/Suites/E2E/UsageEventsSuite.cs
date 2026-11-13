namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Reflection;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Server;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>Live post-commit usage notifications, tenant fanout and reconnect read recovery.</summary>
    public sealed class UsageEventsSuite : IArmadaTestSuite
    {
        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            const string suite = "E2E.UsageEvents";
            return new TestSuiteDescriptor(suite, "Committed usage events", new List<TestCaseDescriptor>
            {
                new TestCaseDescriptor(suite, "commit_fanout_reconnect", "Committed observations are readable when notified, stay within tenant and survive reconnect", async token =>
                {
                    E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                    E2ETenantUser owner = await E2ETenantUser.CreateAsync(fx.AuthClient, "usage-events", false).ConfigureAwait(false);
                    E2ETenantUser admin = await E2ETenantUser.CreateAsync(fx.AuthClient, "usage-events-admin", true, owner.TenantId).ConfigureAwait(false);
                    E2ETenantUser other = await E2ETenantUser.CreateAsync(fx.AuthClient, "usage-events-other").ConfigureAwait(false);
                    using HttpClient client = owner.CreateClient(fx.BaseUrl);
                    using WebSocketTestClient socket = await WebSocketTestClient.ConnectAsync(fx.RestPort, owner.BearerToken).ConfigureAwait(false);
                    using WebSocketTestClient adminSocket = await WebSocketTestClient.ConnectAsync(fx.RestPort, admin.BearerToken).ConfigureAwait(false);
                    using WebSocketTestClient otherSocket = await WebSocketTestClient.ConnectAsync(fx.RestPort, other.BearerToken).ConfigureAwait(false);
                    using WebSocketTestClient globalSocket = await WebSocketTestClient.ConnectAsync(fx.RestPort, null, new Dictionary<string, string> { ["X-Api-Key"] = fx.ApiKey }).ConfigureAwait(false);
                    foreach (WebSocketTestClient subscriber in new[] { socket, adminSocket, otherSocket, globalSocket })
                    {
                        await subscriber.SendAsync(new { Route = "subscribe" }).ConfigureAwait(false);
                        AssertNotNull(await subscriber.WaitForAsync(m => E2eWebSocketFrame.IsType(m, "status.snapshot")).ConfigureAwait(false), "capture attached and sees subscribe reply before commit");
                    }
                    UsageCaptureService capture = (UsageCaptureService)typeof(ArmadaServer).GetField("_UsageCapture", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fx.Server)!;
                    UsageCaptureGroup group = new UsageCaptureGroup { TenantId = owner.TenantId, HostId = "usage-event-host", Runtime = AgentRuntimeEnum.Codex, GroupKey = "usage-event-" + Guid.NewGuid().ToString("N") };
                    UsageProbeResult probe = new UsageProbeResult { Provider = "openai", AccountKey = "event-account-" + Guid.NewGuid().ToString("N"), CapturedUtc = DateTime.UtcNow, Status = UsageSnapshotStatusEnum.Ok, RawExcerpt = "diagnostic-must-not-be-in-event", Windows = new List<UsageWindow> { new UsageWindow { Name = "daily", UsedPercent = 42 } } };
                    // Exercise the production persistence/notification path without invoking an external provider CLI.
                    MethodInfo commit = typeof(UsageCaptureService).GetMethod("CommitAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    Task<UsageSnapshot> pending = (Task<UsageSnapshot>)commit.Invoke(capture, new object?[] { group, new List<string>(), probe, null, DateTime.UtcNow, CancellationToken.None, null })!;
                    UsageSnapshot snapshot = await pending.ConfigureAwait(false);
                    string? frame = await socket.WaitForAsync(m => E2eWebSocketFrame.IsType(m, "usage.snapshot") && m.Contains(snapshot.Id, StringComparison.Ordinal)).ConfigureAwait(false);
                    AssertNotNull(frame, "regular user sees own tenant observation");
                    AssertFalse(frame!.Contains(probe.RawExcerpt!, StringComparison.Ordinal), "event excludes diagnostic excerpt");
                    AssertFalse(frame.Contains(probe.AccountKey!, StringComparison.Ordinal), "event excludes account key");
                    AssertNotNull(await adminSocket.WaitForAsync(m => m.Contains(snapshot.Id, StringComparison.Ordinal)).ConfigureAwait(false), "tenant admin sees own tenant");
                    AssertNull(await otherSocket.WaitForAsync(m => m.Contains(snapshot.Id, StringComparison.Ordinal), 500).ConfigureAwait(false), "other tenant receives no observation");
                    AssertNull(await globalSocket.WaitForAsync(m => m.Contains(snapshot.Id, StringComparison.Ordinal), 500).ConfigureAwait(false), "global admin omission stays in current tenant");
                    using (HttpResponseMessage response = await client.GetAsync("/api/v1/usage/latest?accountKey=" + probe.AccountKey, token).ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();
                        EnumerationResult<UsageSnapshotView> latest = await JsonHelper.DeserializeAsync<EnumerationResult<UsageSnapshotView>>(response).ConfigureAwait(false);
                        AssertEqual(snapshot.Id, latest.Objects.Single().Id, "latest projection committed before notification");
                        AssertEqual(42d, latest.Objects.Single().Windows.Single().UsedPercent!.Value);
                    }
                    socket.Dispose();
                    using WebSocketTestClient reconnected = await WebSocketTestClient.ConnectAsync(fx.RestPort, owner.BearerToken).ConfigureAwait(false);
                    await reconnected.SendAsync(new { Route = "subscribe" }).ConfigureAwait(false);
                    AssertNotNull(await reconnected.WaitForAsync(m => E2eWebSocketFrame.IsType(m, "status.snapshot")).ConfigureAwait(false));
                    using HttpResponseMessage read = await client.GetAsync("/api/v1/usage/snapshots?accountKey=" + probe.AccountKey, token).ConfigureAwait(false);
                    read.EnsureSuccessStatusCode();
                    EnumerationResult<UsageSnapshotView> recovered = await JsonHelper.DeserializeAsync<EnumerationResult<UsageSnapshotView>>(read).ConfigureAwait(false);
                    AssertEqual(snapshot.Id, recovered.Objects.Single().Id, "reconnect refetch recovers canonical row once");
                })
            });
        }
    }
}

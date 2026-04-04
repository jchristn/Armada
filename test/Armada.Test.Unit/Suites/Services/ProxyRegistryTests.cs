namespace Armada.Test.Unit.Suites.Services
{
    using Armada.Proxy.Services;
    using Armada.Proxy.Settings;
    using Armada.Core;
    using Armada.Core.Models;
    using Armada.Test.Common;

    public class ProxyRegistryTests : TestSuite
    {
        public override string Name => "Proxy Registry";

        protected override async Task RunTestsAsync()
        {
            await RunTest("TryValidateHandshake EnforcesRequiredFieldsAndTokens", () =>
            {
                ProxySettings settings = new ProxySettings
                {
                    RequireEnrollmentToken = true,
                    EnrollmentTokens = new List<string> { "secret-token" }
                };
                InstanceRegistry registry = new InstanceRegistry(settings);

                AssertFalse(registry.TryValidateHandshake(null, out string? missingPayloadError));
                AssertContains("required", missingPayloadError ?? String.Empty, "Null payload should be rejected");

                AssertFalse(registry.TryValidateHandshake(new RemoteTunnelHandshakePayload
                {
                    ProtocolVersion = Constants.RemoteTunnelProtocolVersion
                }, out string? missingInstanceError));
                AssertContains("instanceId", missingInstanceError ?? String.Empty, "Missing instanceId should be rejected");

                AssertFalse(registry.TryValidateHandshake(new RemoteTunnelHandshakePayload
                {
                    InstanceId = "armada-test"
                }, out string? missingProtocolError));
                AssertContains("protocolVersion", missingProtocolError ?? String.Empty, "Missing protocolVersion should be rejected");

                AssertFalse(registry.TryValidateHandshake(
                    CreateHandshakePayload("armada-test", "wrong-token"),
                    out string? badTokenError));
                AssertContains("invalid", badTokenError ?? String.Empty, "Invalid token should be rejected");

                AssertTrue(registry.TryValidateHandshake(
                    CreateHandshakePayload("armada-test", "secret-token"),
                    out string? _), "Valid handshake should be accepted");
            });

            await RunTest("RegisterHandshake TracksConnectedStaleAndOfflineStates", () =>
            {
                DateTime nowUtc = new DateTime(2026, 4, 3, 12, 0, 0, DateTimeKind.Utc);
                ProxySettings settings = new ProxySettings
                {
                    StaleAfterSeconds = 30
                };
                InstanceRegistry registry = new InstanceRegistry(settings, () => nowUtc);
                RemoteInstanceSession session = new RemoteInstanceSession((envelope, token) => Task.CompletedTask);

                registry.RegisterHandshake(
                    CreateHandshakePayload(
                        "armada-123",
                        null,
                        Constants.DefaultRemoteTunnelPassword,
                        Constants.ProductVersion,
                        new List<string> { "status.snapshot" }),
                    "127.0.0.1",
                    session);

                var connected = registry.ListSummaries().Single();
                AssertEqual("connected", connected.State);
                AssertEqual("armada-123", connected.InstanceId);
                AssertEqual(Constants.ProductVersion, connected.ArmadaVersion);

                nowUtc = nowUtc.AddSeconds(45);
                var stale = registry.ListSummaries().Single();
                AssertEqual("stale", stale.State);

                registry.MarkDisconnected("armada-123");
                var offline = registry.ListSummaries().Single();
                AssertEqual("offline", offline.State);
            });

            await RunTest("SendRequestAsync CompletesMatchingResponses", async () =>
            {
                ProxySettings settings = new ProxySettings
                {
                    RequestTimeoutSeconds = 5
                };
                InstanceRegistry registry = new InstanceRegistry(settings);
                RemoteTunnelEnvelope? sentEnvelope = null;
                RemoteInstanceSession session = new RemoteInstanceSession((envelope, token) =>
                {
                    sentEnvelope = envelope;
                    return Task.CompletedTask;
                });

                registry.RegisterHandshake(
                    CreateHandshakePayload("armada-req", null, Constants.DefaultRemoteTunnelPassword, Constants.ProductVersion),
                    "127.0.0.1",
                    session);

                Task<RemoteTunnelEnvelope> pending = registry.SendRequestAsync("armada-req", "armada.status.snapshot", null, CancellationToken.None);
                AssertNotNull(sentEnvelope, "Request should have been sent over the session");
                AssertEqual("request", sentEnvelope!.Type);
                AssertEqual("armada.status.snapshot", sentEnvelope.Method);

                registry.TryCompleteResponse("armada-req", new RemoteTunnelEnvelope
                {
                    Type = "response",
                    CorrelationId = sentEnvelope.CorrelationId,
                    StatusCode = 200,
                    Success = true,
                    Payload = RemoteTunnelProtocol.SerializePayload(new { ok = true })
                });

                RemoteTunnelEnvelope response = await pending.ConfigureAwait(false);
                AssertEqual(200, response.StatusCode);
                AssertTrue(response.Success ?? false, "Matched response should complete successfully");
                AssertTrue(response.Payload.HasValue, "Matched response should preserve payload");
                AssertContains("\"ok\":true", response.Payload!.Value.GetRawText(), "Payload should round-trip through the response");
            });

            await RunTest("RecordEvent RetainsRecentActivityWithinConfiguredLimit", () =>
            {
                ProxySettings settings = new ProxySettings
                {
                    MaxRecentEvents = 2
                };
                InstanceRegistry registry = new InstanceRegistry(settings);
                RemoteInstanceSession session = new RemoteInstanceSession((envelope, token) => Task.CompletedTask);

                registry.RegisterHandshake(
                    CreateHandshakePayload("armada-events", null, Constants.DefaultRemoteTunnelPassword, Constants.ProductVersion),
                    "127.0.0.1",
                    session);

                registry.RecordEvent("armada-events", RemoteTunnelProtocol.CreateEvent("mission.started", new { title = "One" }));
                registry.RecordEvent("armada-events", RemoteTunnelProtocol.CreateEvent("mission.progress", new { title = "Two" }));
                registry.RecordEvent("armada-events", RemoteTunnelProtocol.CreateEvent("mission.completed", new { title = "Three" }));

                var record = registry.GetRecord("armada-events");
                AssertNotNull(record);
                var recentEvents = record!.GetRecentEvents();
                AssertEqual(2, recentEvents.Count);
                AssertEqual("mission.progress", recentEvents[0].Method);
                AssertEqual("mission.completed", recentEvents[1].Method);
            });
        }

        private static RemoteTunnelHandshakePayload CreateHandshakePayload(
            string instanceId,
            string? enrollmentToken = null,
            string? password = null,
            string? armadaVersion = null,
            List<string>? capabilities = null)
        {
            string timestampUtc = DateTime.UtcNow.ToString("O");
            string nonce = RemoteTunnelAuth.CreateNonce();
            return new RemoteTunnelHandshakePayload
            {
                InstanceId = instanceId,
                ProtocolVersion = Constants.RemoteTunnelProtocolVersion,
                ArmadaVersion = armadaVersion,
                EnrollmentToken = enrollmentToken,
                PasswordNonce = nonce,
                PasswordTimestampUtc = timestampUtc,
                PasswordProofSha256 = RemoteTunnelAuth.ComputeTunnelHandshakeProof(password ?? Constants.DefaultRemoteTunnelPassword, instanceId, timestampUtc, nonce),
                Capabilities = capabilities ?? new List<string>()
            };
        }
    }
}

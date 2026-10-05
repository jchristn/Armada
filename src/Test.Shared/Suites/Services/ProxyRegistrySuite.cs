namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Models;
    using Armada.Proxy.Enums;
    using Armada.Proxy.Models;
    using Armada.Proxy.Services;
    using Armada.Proxy.Settings;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="InstanceRegistry"/>: handshake validation, connected/stale/offline
    /// lifecycle tracking, request/response correlation, requester-IP propagation, and bounded event
    /// history. Negative cases cover rejected handshakes, invalid password proofs, and requests to
    /// instances that are not connected.
    /// </summary>
    public sealed class ProxyRegistrySuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Proxy Registry suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("try_validate_handshake_enforces_required_fields_and_tokens", "TryValidateHandshake EnforcesRequiredFieldsAndTokens", TestTags.Negative, () =>
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
            }));

            cases.Add(Case("try_validate_handshake_rejects_invalid_password_proof", "TryValidateHandshake RejectsInvalidPasswordProof", TestTags.Negative, () =>
            {
                ProxySettings settings = new ProxySettings();
                InstanceRegistry registry = new InstanceRegistry(settings);

                AssertFalse(registry.TryValidateHandshake(
                    CreateHandshakePayload("armada-badpass", null, "definitely-the-wrong-password", Constants.ProductVersion),
                    out string? error), "A handshake computed with the wrong password should be rejected");
                AssertContains("invalid", error ?? String.Empty, "Invalid password proof should explain the failure");
            }));

            cases.Add(Case("register_handshake_tracks_connected_stale_and_offline_states", "RegisterHandshake TracksConnectedStaleAndOfflineStates", TestTags.Positive, () =>
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

                RemoteInstanceSummary connected = registry.ListSummaries().Single();
                AssertEqual(RemoteInstanceStateEnum.Connected, connected.State);
                AssertEqual("armada-123", connected.InstanceId);
                AssertEqual(Constants.ProductVersion, connected.ArmadaVersion);

                nowUtc = nowUtc.AddSeconds(45);
                RemoteInstanceSummary stale = registry.ListSummaries().Single();
                AssertEqual(RemoteInstanceStateEnum.Stale, stale.State);

                registry.MarkDisconnected("armada-123");
                RemoteInstanceSummary offline = registry.ListSummaries().Single();
                AssertEqual(RemoteInstanceStateEnum.Offline, offline.State);
            }));

            cases.Add(CaseAsync("send_request_async_completes_matching_responses", "SendRequestAsync CompletesMatchingResponses", TestTags.Positive, async () =>
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
            }));

            cases.Add(CaseAsync("send_request_async_includes_requester_ip_when_provided", "SendRequestAsync IncludesRequesterIpWhenProvided", TestTags.Positive, async () =>
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
                    CreateHandshakePayload("armada-ip", null, Constants.DefaultRemoteTunnelPassword, Constants.ProductVersion),
                    "127.0.0.1",
                    session);

                Task<RemoteTunnelEnvelope> pending = registry.SendRequestAsync(
                    "armada-ip",
                    "armada.status.snapshot",
                    null,
                    CancellationToken.None,
                    "203.0.113.10");

                AssertNotNull(sentEnvelope, "Request should have been sent over the session");
                AssertEqual("203.0.113.10", sentEnvelope!.RequesterIp);

                registry.TryCompleteResponse("armada-ip", new RemoteTunnelEnvelope
                {
                    Type = "response",
                    CorrelationId = sentEnvelope.CorrelationId,
                    StatusCode = 200,
                    Success = true
                });

                await pending.ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("send_request_async_unknown_instance_throws", "SendRequestAsync UnknownInstanceThrows", TestTags.Negative, async () =>
            {
                ProxySettings settings = new ProxySettings
                {
                    RequestTimeoutSeconds = 5
                };
                InstanceRegistry registry = new InstanceRegistry(settings);

                await AssertThrowsAsync<InvalidOperationException>(() => registry.SendRequestAsync(
                    "armada-not-connected",
                    "armada.status.snapshot",
                    null,
                    CancellationToken.None)).ConfigureAwait(false);
            }));

            cases.Add(Case("record_event_retains_recent_activity_within_configured_limit", "RecordEvent RetainsRecentActivityWithinConfiguredLimit", TestTags.Positive, () =>
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

                RemoteInstanceRecord? record = registry.GetRecord("armada-events");
                AssertNotNull(record);
                IReadOnlyList<RemoteInstanceEventRecord> recentEvents = record!.GetRecentEvents();
                AssertEqual(2, recentEvents.Count);
                AssertEqual("mission.progress", recentEvents[0].Method);
                AssertEqual("mission.completed", recentEvents[1].Method);
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.ProxyRegistry",
                displayName: "Proxy Registry",
                cases: cases);
        }

        #endregion

        #region Private-Methods

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

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.ProxyRegistry",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) =>
                {
                    body();
                    return Task.CompletedTask;
                },
                tags: new List<string> { tag });
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.ProxyRegistry",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion
    }
}

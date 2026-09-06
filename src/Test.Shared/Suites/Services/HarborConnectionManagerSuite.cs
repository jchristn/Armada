namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Harbor;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="HarborConnectionManager"/>: handshakes register/refresh a Harbor and are
    /// acknowledged with the advertised MCP URL, heartbeats advance liveness and the live-job set,
    /// disconnects mark the Harbor offline, and messages for an unknown Harbor are safe no-ops. Positive
    /// cases assert the registry and persisted state; negative cases assert the missing-id/name and null
    /// guards.
    /// </summary>
    public sealed class HarborConnectionManagerSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the HarborConnectionManager suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("handshake_registers_and_acks", "OnHandshakeAsync registers, persists connected, and acks with MCP URL", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                HarborService harbors = new HarborService(testDb.Driver, CreateLogging());
                HarborConnectionManager manager = new HarborConnectionManager(harbors, CreateLogging(), "http://127.0.0.1:7891/mcp");

                HarborHandshake handshake = new HarborHandshake
                {
                    CorrelationId = "c1",
                    HarborId = "hbr_cm1",
                    Name = "Rig",
                    ProtocolVersion = HarborProtocol.Version,
                    OsPlatform = "Linux",
                    Architecture = "X64",
                    MaxConcurrentJobs = 6,
                    Capabilities = new List<HarborCapability> { new HarborCapability { Name = "claude" } }
                };

                HarborHandshakeAck ack = await manager.OnHandshakeAsync(handshake, "ten_cm", "usr_cm", NoopSend).ConfigureAwait(false);
                AssertTrue(ack.Accepted, "Expected the handshake to be accepted.");
                AssertEqual("http://127.0.0.1:7891/mcp", ack.McpBaseUrl);
                AssertEqual("c1", ack.CorrelationId);
                AssertTrue(manager.IsConnected("hbr_cm1"), "Expected the harbor to be tracked as connected.");

                Harbor? stored = await testDb.Driver.Harbors.ReadAsync("hbr_cm1").ConfigureAwait(false);
                AssertNotNull(stored, "Expected the harbor to be persisted.");
                AssertEqual(HarborConnectionStatusEnum.Connected, stored!.ConnectionStatus);
                AssertEqual("ten_cm", stored.TenantId);
            }));

            cases.Add(CaseAsync("eligible_harbor_is_scoped_to_the_owning_user", "HasEligibleHarborForUserAsync matches only the owning user's connected Harbor", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                HarborService harbors = new HarborService(testDb.Driver, CreateLogging());
                HarborConnectionManager manager = new HarborConnectionManager(harbors, CreateLogging(), null);

                HarborHandshake handshake = new HarborHandshake
                {
                    HarborId = "hbr_userscoped",
                    Name = "Rig",
                    ProtocolVersion = HarborProtocol.Version,
                    MaxConcurrentJobs = 4,
                    Capabilities = new List<HarborCapability> { new HarborCapability { Name = "ClaudeCode", Available = true } }
                };
                await manager.OnHandshakeAsync(handshake, "ten_us", "usr_owner", NoopSend).ConfigureAwait(false);

                Armada.Core.Services.HarborRoutingRequest request = new Armada.Core.Services.HarborRoutingRequest { RequestedRuntime = "ClaudeCode" };

                bool owner = await manager.HasEligibleHarborForUserAsync("usr_owner", request).ConfigureAwait(false);
                bool other = await manager.HasEligibleHarborForUserAsync("usr_other", request).ConfigureAwait(false);

                AssertTrue(owner, "Expected the owning user's connected Harbor to be eligible.");
                AssertTrue(!other, "Expected a different user to have no eligible Harbor.");
            }));

            cases.Add(CaseAsync("heartbeat_marks_seen_and_tracks_jobs", "OnMessageAsync heartbeat advances liveness and job set", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                HarborService harbors = new HarborService(testDb.Driver, CreateLogging());
                HarborConnectionManager manager = new HarborConnectionManager(harbors, CreateLogging(), null);

                HarborHandshake handshake = new HarborHandshake { HarborId = "hbr_cm2", Name = "Rig", ProtocolVersion = "1.0" };
                await manager.OnHandshakeAsync(handshake, "ten_cm2", "usr_cm2", NoopSend).ConfigureAwait(false);

                HarborHeartbeat heartbeat = new HarborHeartbeat { LiveJobIds = new List<string> { "job-a", "job-b" } };
                await manager.OnMessageAsync("hbr_cm2", heartbeat).ConfigureAwait(false);

                AssertEqual(2, manager.InFlightJobs("hbr_cm2"));
                Harbor? stored = await testDb.Driver.Harbors.ReadAsync("hbr_cm2").ConfigureAwait(false);
                AssertNotNull(stored, "Expected the harbor to be persisted.");
                AssertEqual(HarborConnectionStatusEnum.Connected, stored!.ConnectionStatus);
            }));

            cases.Add(CaseAsync("disconnect_marks_disconnected", "OnDisconnectedAsync marks the harbor offline", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                HarborService harbors = new HarborService(testDb.Driver, CreateLogging());
                HarborConnectionManager manager = new HarborConnectionManager(harbors, CreateLogging(), null);

                await manager.OnHandshakeAsync(new HarborHandshake { HarborId = "hbr_cm3", Name = "Rig", ProtocolVersion = "1.0" }, "ten_cm3", "usr_cm3", NoopSend).ConfigureAwait(false);
                await manager.OnDisconnectedAsync("hbr_cm3").ConfigureAwait(false);

                AssertTrue(!manager.IsConnected("hbr_cm3"), "Expected the harbor to be removed from the registry.");
                Harbor? stored = await testDb.Driver.Harbors.ReadAsync("hbr_cm3").ConfigureAwait(false);
                AssertNotNull(stored, "Expected the harbor to be persisted.");
                AssertEqual(HarborConnectionStatusEnum.Disconnected, stored!.ConnectionStatus);
            }));

            cases.Add(CaseAsync("message_for_unknown_harbor_is_noop", "OnMessageAsync for an unknown harbor is a safe no-op", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                HarborService harbors = new HarborService(testDb.Driver, CreateLogging());
                HarborConnectionManager manager = new HarborConnectionManager(harbors, CreateLogging(), null);

                await manager.OnMessageAsync("hbr_ghost", new HarborHeartbeat { LiveJobIds = new List<string>() }).ConfigureAwait(false);
                await manager.OnMessageAsync("hbr_ghost", new HarborError { Message = "noop" }).ConfigureAwait(false);
                AssertTrue(!manager.IsConnected("hbr_ghost"), "Expected no phantom registration.");
            }));

            cases.Add(CaseAsync("handshake_blank_id_throws", "OnHandshakeAsync rejects a blank harbor id", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                HarborService harbors = new HarborService(testDb.Driver, CreateLogging());
                HarborConnectionManager manager = new HarborConnectionManager(harbors, CreateLogging(), null);
                await AssertThrowsAsync<ArgumentException>(() => manager.OnHandshakeAsync(new HarborHandshake { HarborId = "", Name = "X", ProtocolVersion = "1.0" }, "t", "u", NoopSend));
            }));

            cases.Add(CaseAsync("handshake_null_send_throws", "OnHandshakeAsync rejects a null send channel", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                HarborService harbors = new HarborService(testDb.Driver, CreateLogging());
                HarborConnectionManager manager = new HarborConnectionManager(harbors, CreateLogging(), null);
                await AssertThrowsAsync<ArgumentNullException>(() => manager.OnHandshakeAsync(new HarborHandshake { HarborId = "hbr_x", Name = "X", ProtocolVersion = "1.0" }, "t", "u", null!));
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.HarborConnectionManager",
                displayName: "Harbor Connection Manager",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static Task NoopSend(HarborMessage message, CancellationToken token) => Task.CompletedTask;

        private static LoggingModule CreateLogging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.HarborConnectionManager",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion
    }
}

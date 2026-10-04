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
    using Armada.Runtimes;
    using Armada.Runtimes.Interfaces;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// O-04 and W1.5 for Harbor split mode (experimental): a Harbor id stays bound to the identity that registered it
    /// (another credential reusing the id is refused and the existing link is kept), and a delegated launch carries the
    /// auto-approve decision resolved on the Admiral so the Harbor applies the captain and vessel setting.
    /// </summary>
    public sealed class HarborLaunchSecuritySuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.HarborLaunchSecurity";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("same_identity_relinks", "The registering identity can re-link its Harbor id", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                HarborConnectionManager manager = new HarborConnectionManager(new HarborService(testDb.Driver, Logging()), Logging(), null);
                HarborHandshakeAck first = await manager.OnHandshakeAsync(Handshake("hbr_bind_same"), "ten_owner", "usr_owner", NoopSend).ConfigureAwait(false);
                HarborHandshakeAck second = await manager.OnHandshakeAsync(Handshake("hbr_bind_same"), "ten_owner", "usr_owner", NoopSend).ConfigureAwait(false);
                AssertTrue(first.Accepted, "first link");
                AssertTrue(second.Accepted, "re-link by the same identity");
                AssertTrue(manager.IsConnected("hbr_bind_same"), "connected");
            }));

            cases.Add(CaseAsync("other_identity_cannot_take_over", "Another tenant or user reusing a Harbor id is refused and the owner's link is kept", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                HarborConnectionManager manager = new HarborConnectionManager(new HarborService(testDb.Driver, Logging()), Logging(), null);

                List<string> ownerReceived = new List<string>();
                List<string> attackerReceived = new List<string>();
                HarborSendDelegate ownerSend = (message, token) => { ownerReceived.Add(message.GetType().Name); return Task.CompletedTask; };
                HarborSendDelegate attackerSend = (message, token) => { attackerReceived.Add(message.GetType().Name); return Task.CompletedTask; };

                HarborHandshakeAck owner = await manager.OnHandshakeAsync(Handshake("hbr_bind_owned"), "ten_owner", "usr_owner", ownerSend).ConfigureAwait(false);
                AssertTrue(owner.Accepted, "owner link");

                HarborHandshakeAck otherTenant = await manager.OnHandshakeAsync(Handshake("hbr_bind_owned"), "ten_attacker", "usr_attacker", attackerSend).ConfigureAwait(false);
                AssertFalse(otherTenant.Accepted, "another tenant is refused");
                AssertNotNull(otherTenant.Reason, "refusal reason");

                HarborHandshakeAck otherUser = await manager.OnHandshakeAsync(Handshake("hbr_bind_owned"), "ten_owner", "usr_other", attackerSend).ConfigureAwait(false);
                AssertFalse(otherUser.Accepted, "another user of the same tenant is refused");

                HarborHandshakeAck anonymous = await manager.OnHandshakeAsync(Handshake("hbr_bind_owned"), null, null, attackerSend).ConfigureAwait(false);
                AssertFalse(anonymous.Accepted, "an unauthenticated link cannot claim an owned id");

                Harbor? stored = await testDb.Driver.Harbors.ReadAsync("hbr_bind_owned").ConfigureAwait(false);
                AssertEqual("ten_owner", stored!.TenantId);
                AssertEqual("usr_owner", stored.UserId);

                // Jobs for the Harbor still go to the owner's link.
                await manager.LaunchAsync("hbr_bind_owned", new HarborLaunchRequest { JobId = "job1", Runtime = "ClaudeCode", WorkingDirectory = "/w" }, new NoopListener()).ConfigureAwait(false);
                AssertTrue(ownerReceived.Contains(nameof(HarborLaunchRequest)), "launch reached the owner's link");
                AssertFalse(attackerReceived.Contains(nameof(HarborLaunchRequest)), "launch never reached the refused link");
            }));

            cases.Add(CaseAsync("delegated_launch_carries_auto_approve", "A delegated launch carries the captain's effective auto-approve decision", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                HarborConnectionManager manager = new HarborConnectionManager(new HarborService(testDb.Driver, Logging()), Logging(), null);
                List<HarborLaunchRequest> launches = new List<HarborLaunchRequest>();
                HarborSendDelegate simulated = async (message, cancellation) =>
                {
                    if (message is HarborLaunchRequest launch)
                    {
                        launches.Add(launch);
                        await manager.OnMessageAsync("hbr_aa", new HarborStarted { JobId = launch.JobId, ProcessId = 5000 + launches.Count }, cancellation).ConfigureAwait(false);
                    }
                };
                await manager.OnHandshakeAsync(Handshake("hbr_aa"), "ten_aa", "usr_aa", simulated).ConfigureAwait(false);
                RemoteHostProcessExecutor executor = new RemoteHostProcessExecutor(manager, "hbr_aa");

                Captain on = new Captain("aa-on");
                Captain off = CaptainRuntimeOptions.WithEffectiveAutoApprove(new Captain("aa-off"), false);

                IAgentRuntime first = executor.CreateRuntime(AgentRuntimeEnum.ClaudeCode);
                await first.StartAsync("/repo", "work", captain: off).ConfigureAwait(false);
                IAgentRuntime second = executor.CreateRuntime(AgentRuntimeEnum.ClaudeCode);
                await second.StartAsync("/repo", "work", captain: on).ConfigureAwait(false);
                IAgentRuntime third = executor.CreateRuntime(AgentRuntimeEnum.ClaudeCode);
                await third.StartAsync("/repo", "work").ConfigureAwait(false);

                AssertEqual(3, launches.Count);
                AssertEqual(false, launches[0].AutoApprove, "forced off");
                AssertEqual(true, launches[1].AutoApprove, "captain default on");
                AssertNull(launches[2].AutoApprove, "no captain: Harbor default");
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Harbor launch security (O-04, experimental split mode)",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static HarborHandshake Handshake(string id)
        {
            return new HarborHandshake
            {
                HarborId = id,
                Name = "Rig",
                ProtocolVersion = HarborProtocol.Version,
                MaxConcurrentJobs = 2,
                Capabilities = new List<HarborCapability> { new HarborCapability { Name = "ClaudeCode", Available = true } }
            };
        }

        private static Task NoopSend(HarborMessage message, CancellationToken token) => Task.CompletedTask;

        private static LoggingModule Logging()
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

        #region Nested-Types

        private sealed class NoopListener : IHarborJobListener
        {
            public void OnStarted(int processId) { }

            public void OnOutput(HarborOutputStreamEnum stream, string data) { }

            public void OnExited(int exitCode) { }
        }

        #endregion
    }
}

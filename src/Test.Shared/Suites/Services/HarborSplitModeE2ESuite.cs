namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Harbor;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Split-mode end-to-end descriptors. These stand in for the containerized "Admiral in Docker + host
    /// Harbor" acceptance run (TST-03/TST-08) with a deterministic, in-process loopback that wires the real
    /// Admiral-side seam to a real host-side executor: <see cref="RemoteHostCommandExecutor"/> ->
    /// <see cref="HarborConnectionManager"/> git request/reply correlation -> a simulated Harbor whose send
    /// channel runs the delegated command against <see cref="LocalHostCommandExecutor"/> (real git) on the
    /// host and routes the result back over the link, exactly as the WebSocket transport would.
    ///
    /// The positive path proves a genuine "git land through the Harbor": a feature branch is created and
    /// committed on the host filesystem, and the merge that lands it is issued only through the delegated seam,
    /// then confirmed to have taken effect on disk. The authenticated handshake advertises the Admiral's MCP
    /// base URL back to the Harbor (so a launched captain can call home), and the link is scoped to the owning
    /// user -- the "MCP authenticated end-to-end, per-user" guarantees. Negative cases cover a Harbor that goes
    /// dark mid-link and a mismatched-user eligibility check.
    /// </summary>
    public sealed class HarborSplitModeE2ESuite : IArmadaTestSuite
    {
        #region Public-Members

        private const string _McpBaseUrl = "http://127.0.0.1:7891/mcp";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Harbor split-mode E2E suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("authenticated_handshake_advertises_mcp_url_and_scopes_to_user", "Handshake acks the advertised MCP URL and the link is scoped to the owning user", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                HarborService harbors = new HarborService(testDb.Driver, CreateLogging());
                HarborConnectionManager manager = new HarborConnectionManager(harbors, CreateLogging(), _McpBaseUrl);

                LocalHostCommandExecutor host = new LocalHostCommandExecutor();
                HarborSendDelegate link = BuildLoopback(manager, "hbr_e2e_auth", host);

                HarborHandshake handshake = new HarborHandshake
                {
                    CorrelationId = "hs1",
                    HarborId = "hbr_e2e_auth",
                    Name = "Rig",
                    ProtocolVersion = HarborProtocol.Version,
                    OsPlatform = "Linux",
                    Architecture = "X64",
                    MaxConcurrentJobs = 4,
                    Capabilities = new List<HarborCapability> { new HarborCapability { Name = "ClaudeCode", Available = true } }
                };

                HarborHandshakeAck ack = await manager.OnHandshakeAsync(handshake, "ten_e2e", "usr_owner", link).ConfigureAwait(false);

                AssertTrue(ack.Accepted, "Expected the authenticated handshake to be accepted.");
                AssertEqual(_McpBaseUrl, ack.McpBaseUrl);
                AssertEqual("hs1", ack.CorrelationId);

                Harbor? stored = await testDb.Driver.Harbors.ReadAsync("hbr_e2e_auth").ConfigureAwait(false);
                AssertNotNull(stored, "Expected the harbor to be persisted under the resolved tenant/user.");
                AssertEqual("ten_e2e", stored!.TenantId);
                AssertEqual("usr_owner", stored.UserId);

                HarborRoutingRequest request = new HarborRoutingRequest { RequestedRuntime = "ClaudeCode" };
                bool owner = await manager.HasEligibleHarborForUserAsync("usr_owner", request).ConfigureAwait(false);
                bool other = await manager.HasEligibleHarborForUserAsync("usr_intruder", request).ConfigureAwait(false);

                AssertTrue(owner, "Expected the owning user to have an eligible Harbor.");
                AssertTrue(!other, "Expected a different user to have no eligible Harbor (per-user scoping).");
            }));

            cases.Add(CaseAsync("git_land_executes_on_the_harbor", "A branch is landed entirely through the delegated Harbor seam and takes effect on the host", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                HarborService harbors = new HarborService(testDb.Driver, CreateLogging());
                HarborConnectionManager manager = new HarborConnectionManager(harbors, CreateLogging(), _McpBaseUrl);

                LocalHostCommandExecutor host = new LocalHostCommandExecutor();
                HarborSendDelegate link = BuildLoopback(manager, "hbr_e2e_land", host);

                await manager.OnHandshakeAsync(
                    new HarborHandshake { HarborId = "hbr_e2e_land", Name = "Rig", ProtocolVersion = HarborProtocol.Version },
                    "ten_land",
                    "usr_land",
                    link).ConfigureAwait(false);

                // Every host operation runs only through the delegated seam, exactly as Split mode would.
                RemoteHostCommandExecutor remote = new RemoteHostCommandExecutor(manager, "hbr_e2e_land");

                string repo = CreateTempDir("split-land");
                try
                {
                    await RunGitAsync(remote, repo, "init");
                    // Deterministic default branch name across git versions.
                    await RunGitAsync(remote, repo, "checkout", "-b", "main");
                    await RunGitAsync(remote, repo, "config", "user.email", "armada@example.com");
                    await RunGitAsync(remote, repo, "config", "user.name", "Armada");
                    await RunGitAsync(remote, repo, "commit", "--allow-empty", "-m", "base");

                    await RunGitAsync(remote, repo, "checkout", "-b", "feature");
                    await RunGitAsync(remote, repo, "commit", "--allow-empty", "-m", "feature-work");

                    await RunGitAsync(remote, repo, "checkout", "main");

                    // The land itself: a no-fast-forward merge issued through the Harbor link.
                    HostCommandResult land = await remote.RunAsync(new HostCommandRequest
                    {
                        Executable = "git",
                        WorkingDirectory = repo,
                        Arguments = new List<string> { "merge", "--no-ff", "feature", "-m", "land feature" },
                        TimeoutMs = 30000
                    }).ConfigureAwait(false);

                    AssertTrue(land.Success, "Expected the delegated land (merge) to succeed. stderr: " + land.StandardError);

                    // Confirm the land took effect on the host filesystem, again observed through the seam.
                    HostCommandResult log = await remote.RunAsync(new HostCommandRequest
                    {
                        Executable = "git",
                        WorkingDirectory = repo,
                        Arguments = new List<string> { "log", "--oneline" },
                        TimeoutMs = 30000
                    }).ConfigureAwait(false);

                    AssertTrue(log.Success, "Expected git log to succeed after the land.");
                    AssertContains("feature-work", log.StandardOutput);
                    AssertContains("land feature", log.StandardOutput);
                }
                finally
                {
                    TryDeleteDir(repo);
                }
            }));

            cases.Add(CaseAsync("land_stalls_when_owning_harbor_disconnects", "A delegated host operation fails cleanly once the owning Harbor drops its link", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                HarborService harbors = new HarborService(testDb.Driver, CreateLogging());
                HarborConnectionManager manager = new HarborConnectionManager(harbors, CreateLogging(), _McpBaseUrl);

                LocalHostCommandExecutor host = new LocalHostCommandExecutor();
                HarborSendDelegate link = BuildLoopback(manager, "hbr_e2e_drop", host);

                await manager.OnHandshakeAsync(
                    new HarborHandshake { HarborId = "hbr_e2e_drop", Name = "Rig", ProtocolVersion = HarborProtocol.Version },
                    "ten_drop",
                    "usr_drop",
                    link).ConfigureAwait(false);

                RemoteHostCommandExecutor remote = new RemoteHostCommandExecutor(manager, "hbr_e2e_drop");

                // While connected the delegated command works.
                HostCommandResult ok = await remote.RunAsync(new HostCommandRequest
                {
                    Executable = "git",
                    Arguments = new List<string> { "--version" },
                    TimeoutMs = 30000
                }).ConfigureAwait(false);
                AssertTrue(ok.Success, "Expected the delegated command to succeed while linked.");

                // The Harbor goes dark: the owning mission's host operations can no longer be delegated.
                await manager.OnDisconnectedAsync("hbr_e2e_drop").ConfigureAwait(false);

                await AssertThrowsAsync<InvalidOperationException>(() => remote.RunAsync(new HostCommandRequest
                {
                    Executable = "git",
                    Arguments = new List<string> { "status" },
                    TimeoutMs = 30000
                }));
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.HarborSplitModeE2E",
                displayName: "Harbor Split-Mode E2E",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Build a send channel that simulates the Harbor host over the link: a delegated git request is run
        /// against the real host executor on a background task and the result is routed back through the
        /// manager, so the request/reply correlation and the actual host execution are exercised end to end.
        /// </summary>
        private static HarborSendDelegate BuildLoopback(HarborConnectionManager manager, string harborId, LocalHostCommandExecutor host)
        {
            return (message, token) =>
            {
                if (message is HarborGitRequest git)
                {
                    // Model the remote host: accept the send now, execute and reply asynchronously.
                    _ = Task.Run(async () =>
                    {
                        HostCommandResult result = await host.RunAsync(new HostCommandRequest
                        {
                            Executable = git.Executable,
                            WorkingDirectory = git.WorkingDirectory,
                            Arguments = git.Arguments,
                            TimeoutMs = 30000
                        }, token).ConfigureAwait(false);

                        HarborGitResult reply = new HarborGitResult
                        {
                            CorrelationId = git.CorrelationId,
                            RequestId = git.RequestId,
                            ExitCode = result.ExitCode,
                            StandardOutput = result.StandardOutput,
                            StandardError = result.StandardError
                        };

                        await manager.OnMessageAsync(harborId, reply, token).ConfigureAwait(false);
                    }, token);
                }

                return Task.CompletedTask;
            };
        }

        private static async Task RunGitAsync(RemoteHostCommandExecutor remote, string workingDirectory, params string[] arguments)
        {
            HostCommandResult result = await remote.RunAsync(new HostCommandRequest
            {
                Executable = "git",
                WorkingDirectory = workingDirectory,
                Arguments = new List<string>(arguments),
                TimeoutMs = 30000
            }).ConfigureAwait(false);

            AssertTrue(result.Success, "Expected 'git " + String.Join(" ", arguments) + "' to succeed via the Harbor. stderr: " + result.StandardError);
        }

        private static string CreateTempDir(string prefix)
        {
            string path = Path.Combine(Path.GetTempPath(), "armada-" + prefix + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void TryDeleteDir(string path)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, true);
            }
            catch
            {
            }
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
                suiteId: "Services.HarborSplitModeE2E",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion
    }
}

namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using Armada.Runtimes;
    using Armada.Server;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for runtime model validation and launch passthrough in <see cref="AgentLifecycleHandler"/>.
    /// Cases cover model validation forwarding the requested model to the runtime, invalid-model and
    /// timeout error extraction, Mux endpoint and runtime-options JSON validation, launch model
    /// passthrough, heartbeat propagation to mission and voyage timestamps, silent-process liveness
    /// heartbeats, and final-message artifact preference over streamed output. Each case builds a fresh
    /// SQLite store; runtime-launching cases install a temporary cursor-agent shim on PATH.
    /// </summary>
    public sealed class AgentLifecycleHandlerSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.AgentLifecycleHandler";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Agent Lifecycle Handler suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("validate_model_async_returns_null_and_forwards_model", "ValidateModelAsync returns null and forwards model to runtime", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                using (CursorShimScope shim = CursorShimScope.Create())
                {
                    AgentLifecycleHandler handler = CreateHandler(testDb.Driver, out _, shim.ShimPath);

                    string? error = await handler.ValidateModelAsync(AgentRuntimeEnum.Cursor, "gpt-5.4-mini").ConfigureAwait(false);
                    string args = await WaitForRecordedArgsAsync(shim.ArgsFile, "gpt-5.4-mini").ConfigureAwait(false);

                    AssertNull(error, "Valid model should pass validation");
                    AssertModelArgument(args, "gpt-5.4-mini", "Validation runtime args should pass --model gpt-5.4-mini");
                }
            }));

            cases.Add(CaseAsync("validate_captain_model_async_returns_runtime_error_for_invalid_model", "ValidateCaptainModelAsync returns extracted runtime error for invalid model", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                using (CursorShimScope shim = CursorShimScope.Create())
                {
                    AgentLifecycleHandler handler = CreateHandler(testDb.Driver, out _, shim.ShimPath);
                    Captain captain = new Captain("validation-captain", AgentRuntimeEnum.Cursor)
                    {
                        Model = "bad-model"
                    };

                    CaptainModelValidationFailure? failure = await handler.ValidateCaptainModelDetailedAsync(captain).ConfigureAwait(false);
                    string args = await WaitForRecordedArgsAsync(shim.ArgsFile, "bad-model").ConfigureAwait(false);

                    AssertNotNull(failure, "Invalid model should return a failure");
                    AssertEqual(CaptainModelValidationFailureEnum.ModelRejected, failure!.Reason, "typed reason for a rejected model");
                    // The message forwards the runtime's own diagnostic line to the operator.
                    AssertContains("unknown model 'bad-model'", failure.Message, "Message should include runtime output");
                    AssertModelArgument(args, "bad-model", "Captain validation should launch runtime with --model bad-model");
                }
            }));

            cases.Add(CaseAsync("validate_captain_model_async_returns_timeout_error", "ValidateCaptainModelAsync returns timeout error when runtime does not exit", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                using (CursorShimScope shim = CursorShimScope.Create())
                {
                    AgentLifecycleHandler handler = CreateHandler(testDb.Driver, out _, shim.ShimPath);
                    Captain captain = new Captain("timeout-captain", AgentRuntimeEnum.Cursor)
                    {
                        Model = "hang-model"
                    };

                    CaptainModelValidationFailure? failure = await handler.ValidateCaptainModelDetailedAsync(captain).ConfigureAwait(false);
                    string args = await WaitForRecordedArgsAsync(shim.ArgsFile, "hang-model").ConfigureAwait(false);

                    AssertNotNull(failure, "Timed-out validation should return a failure");
                    AssertEqual(CaptainModelValidationFailureEnum.TimedOut, failure!.Reason, "typed reason for a validation timeout");
                    AssertModelArgument(args, "hang-model", "Timed-out validation should still launch runtime with --model hang-model");
                }
            }));

            cases.Add(CaseAsync("validate_captain_model_async_requires_mux_endpoint", "ValidateCaptainModelAsync requires Mux endpoint", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    AgentLifecycleHandler handler = CreateHandler(testDb.Driver, out _);
                    Captain captain = new Captain("mux-captain", AgentRuntimeEnum.Mux)
                    {
                        RuntimeOptionsJson = CaptainRuntimeOptions.Serialize(new MuxCaptainOptions())
                    };

                    CaptainModelValidationFailure? failure = await handler.ValidateCaptainModelDetailedAsync(captain).ConfigureAwait(false);

                    AssertNotNull(failure, "Mux validation should fail without an endpoint");
                    AssertEqual(CaptainModelValidationFailureEnum.NamedEndpointRequired, failure!.Reason, "typed reason for a missing Mux endpoint");
                }
            }));

            cases.Add(CaseAsync("validate_captain_model_async_rejects_invalid_mux_options_json", "ValidateCaptainModelAsync rejects invalid Mux runtime options JSON", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    AgentLifecycleHandler handler = CreateHandler(testDb.Driver, out _);
                    Captain captain = new Captain("mux-captain", AgentRuntimeEnum.Mux)
                    {
                        RuntimeOptionsJson = "{not valid json}"
                    };

                    CaptainModelValidationFailure? failure = await handler.ValidateCaptainModelDetailedAsync(captain).ConfigureAwait(false);

                    AssertNotNull(failure, "Mux validation should fail when runtime options JSON is invalid");
                    AssertEqual(CaptainModelValidationFailureEnum.InvalidRuntimeOptions, failure!.Reason, "typed reason for invalid Mux options");
                    AssertEqual(failure.Message, await handler.ValidateCaptainModelAsync(captain).ConfigureAwait(false), "the string API returns the same message");
                }
            }));

            cases.Add(CaseAsync("handle_launch_agent_async_passes_captain_model_to_runtime", "HandleLaunchAgentAsync passes captain model to runtime startup", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                using (CursorShimScope shim = CursorShimScope.Create())
                {
                    AgentLifecycleHandler handler = CreateHandler(testDb.Driver, out ArmadaSettings settings, shim.ShimPath);
                    string worktreePath = Path.Combine(Path.GetTempPath(), "armada_cursor_launch_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(worktreePath);

                    try
                    {
                        Captain captain = new Captain("launch-captain", AgentRuntimeEnum.Cursor)
                        {
                            Model = "cursor-model"
                        };

                        Mission mission = new Mission("Launch mission")
                        {
                            Persona = "Test Engineer",
                            BranchName = "feature/model-pass"
                        };

                        Dock dock = new Dock
                        {
                            BranchName = "feature/model-pass",
                            WorktreePath = worktreePath
                        };
                        string logFilePath = Path.Combine(settings.LogDirectory, "missions", mission.Id + ".log");

                        int processId = await handler.HandleLaunchAgentAsync(captain, mission, dock).ConfigureAwait(false);
                        string logContents = await WaitForFileContainsAsync(logFilePath, "cursor-model").ConfigureAwait(false);

                        AssertTrue(processId > 0, "Launch should return a process id");
                        AssertContains("--model cursor-model", logContents, "Launch log should include captain model flag");
                        AssertModelArgument(await WaitForRecordedArgsAsync(shim.ArgsFile, "cursor-model").ConfigureAwait(false), "cursor-model", "Launched runtime receives --model cursor-model");
                    }
                    finally
                    {
                        try { Directory.Delete(worktreePath, true); } catch { }
                    }
                }
            }));

            cases.Add(CaseAsync("launched_process_is_tracked_until_its_exit_is_received", "A launched process (synthetic id) is tracked while running and handed to the exit callback, so the health check never probes it", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    AgentRuntimeFactory runtimeFactory = new AgentRuntimeFactory(logging);
                    TaskCompletionSource<bool> release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    StubCaptainBehavior behavior = new StubCaptainBehavior { MissionExitGate = release.Task };
                    runtimeFactory.Override(AgentRuntimeEnum.ClaudeCode, () => new StubCaptainRuntime(logging, behavior));
                    StubAdmiralService admiral = new StubAdmiralService();
                    AgentLifecycleHandler handler = new AgentLifecycleHandler(
                        logging, testDb.Driver, settings, runtimeFactory, admiral, new MessageTemplateService(logging), null, null,
                        (eventType, message, entityType, entityId, captainId, missionId, vesselId, voyageId) => Task.CompletedTask);

                    string worktreePath = Path.Combine(Path.GetTempPath(), "armada_tracked_launch_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(worktreePath);
                    try
                    {
                        Captain captain = new Captain("tracked-launch-captain", AgentRuntimeEnum.ClaudeCode);
                        Mission mission = new Mission("Tracked launch mission") { BranchName = "feature/tracked" };
                        Dock dock = new Dock { BranchName = "feature/tracked", WorktreePath = worktreePath };

                        int processId = await handler.HandleLaunchAgentAsync(captain, mission, dock).ConfigureAwait(false);
                        AssertTrue(handler.IsProcessTracked(processId), "A running launched process is tracked");
                        AssertFalse(handler.IsProcessExitHandled(processId), "Its exit has not been received");

                        release.TrySetResult(true);
                        DateTime deadline = DateTime.UtcNow.AddSeconds(30);
                        while (!handler.IsProcessExitHandled(processId) && DateTime.UtcNow < deadline)
                            await Task.Delay(20).ConfigureAwait(false);

                        AssertTrue(handler.IsProcessExitHandled(processId), "The exit callback received the exit");
                        AssertFalse(handler.IsProcessTracked(processId), "An exited process is no longer tracked");
                    }
                    finally
                    {
                        release.TrySetResult(true);
                        try { Directory.Delete(worktreePath, true); } catch { }
                    }
                }
            }));

            cases.Add(CaseAsync("handle_launch_agent_async_binds_mission_scoped_mcp_token", "A mission launch carries a mission-scoped MCP token bound to the mission, owner, and captain (O-20)", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                using (CursorShimScope shim = CursorShimScope.Create())
                {
                    AgentLifecycleHandler handler = CreateHandler(testDb.Driver, out ArmadaSettings settings, shim.ShimPath);
                    SessionTokenService tokens = new SessionTokenService();
                    handler.SetSessionTokenService(tokens);
                    string worktreePath = Path.Combine(Path.GetTempPath(), "armada_cursor_token_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(worktreePath);
                    try
                    {
                        Captain captain = new Captain("token-captain", AgentRuntimeEnum.Cursor) { Model = "token-model" };
                        Mission mission = new Mission("Token mission")
                        {
                            TenantId = Constants.DefaultTenantId,
                            UserId = Constants.DefaultUserId,
                            CaptainId = captain.Id,
                            BranchName = "feature/token"
                        };
                        Dock dock = new Dock { BranchName = "feature/token", WorktreePath = worktreePath };

                        await handler.HandleLaunchAgentAsync(captain, mission, dock).ConfigureAwait(false);
                        string recorded = await WaitForRecordedArgsAsync(shim.ArgsFile, "token-model").ConfigureAwait(false);
                        string? tokenLine = recorded.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("ARMADA_MCP_TOKEN=", StringComparison.Ordinal));
                        AssertNotNull(tokenLine, "the captain process receives ARMADA_MCP_TOKEN");
                        AuthContext? ctx = tokens.ValidateToken(tokenLine!.Substring("ARMADA_MCP_TOKEN=".Length));
                        AssertNotNull(ctx, "the token is a valid session token");
                        AssertEqual(mission.Id, ctx!.MissionId, "bound to the mission");
                        AssertEqual(captain.Id, ctx.MissionCaptainId, "bound to the captain");
                        AssertEqual(Constants.DefaultUserId, ctx.UserId, "acts as the mission owner");
                        AssertNull(ctx.AskThreadId, "not an Ask thread token");
                        AssertFalse(Directory.Exists(Path.Combine(worktreePath, ".cursor")), "no client configuration is written into the worktree");
                    }
                    finally
                    {
                        try { Directory.Delete(worktreePath, true); } catch { }
                    }
                }
            }));

            cases.Add(CaseAsync("handle_launch_agent_async_no_token_when_disabled", "Mcp.MissionScopedTokens false launches without a token", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                using (CursorShimScope shim = CursorShimScope.Create())
                {
                    AgentLifecycleHandler handler = CreateHandler(testDb.Driver, out ArmadaSettings settings, shim.ShimPath);
                    settings.Mcp.MissionScopedTokens = false;
                    handler.SetSessionTokenService(new SessionTokenService());
                    string worktreePath = Path.Combine(Path.GetTempPath(), "armada_cursor_notoken_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(worktreePath);
                    try
                    {
                        Captain captain = new Captain("notoken-captain", AgentRuntimeEnum.Cursor) { Model = "notoken-model" };
                        Mission mission = new Mission("No token mission")
                        {
                            TenantId = Constants.DefaultTenantId,
                            UserId = Constants.DefaultUserId,
                            CaptainId = captain.Id,
                            BranchName = "feature/notoken"
                        };
                        Dock dock = new Dock { BranchName = "feature/notoken", WorktreePath = worktreePath };

                        AssertNull(handler.MintMissionToken(captain, mission), "no token is minted when disabled");
                        await handler.HandleLaunchAgentAsync(captain, mission, dock).ConfigureAwait(false);
                        string recorded = await WaitForRecordedArgsAsync(shim.ArgsFile, "notoken-model").ConfigureAwait(false);
                        AssertFalse(recorded.Contains("ARMADA_MCP_TOKEN=", StringComparison.Ordinal), "the captain process gets no token");
                    }
                    finally
                    {
                        try { Directory.Delete(worktreePath, true); } catch { }
                    }
                }
            }));

            cases.Add(CaseAsync("process_exit_carries_structured_provider_error", "A reported provider error decides the typed exit outcome handed to the admiral", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    AgentLifecycleHandler handler = CreateHandler(testDb.Driver, out _, out StubAdmiralService admiral);

                    RuntimeProviderError error = new RuntimeProviderError { HttpStatusCode = 429, ErrorType = "rate_limit_error", RetryAfterSeconds = 120 };
                    handler.HandleAgentProviderError(9101, error);
                    await handler.HandleAgentProcessExitedAsync(9101, 1, "cpt_test", "msn_test").ConfigureAwait(false);

                    AssertEqual(1, admiral.ExitInfos.Count, "one typed exit outcome reaches the admiral");
                    AssertEqual(1, admiral.ExitInfos[0].ExitCode);
                    AssertEqual(RuntimeFailureKindEnum.UsageLimit, admiral.ExitInfos[0].FailureKind);
                    AssertEqual(120, admiral.ExitInfos[0].ProviderError!.RetryAfterSeconds);
                }
            }));

            cases.Add(CaseAsync("process_exit_without_provider_error_is_crash", "A non-zero exit with no provider error is a crash, whatever the output said", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    AgentLifecycleHandler handler = CreateHandler(testDb.Driver, out _, out StubAdmiralService admiral);

                    // Output that the old substring classifier read as a usage limit or an auth failure.
                    handler.HandleAgentOutput(9102, "error CS0403: billing quota 429 permission denied in BillingService.cs");
                    await handler.HandleAgentProcessExitedAsync(9102, 1, "cpt_test", "msn_test").ConfigureAwait(false);

                    // A provider error from an earlier process id never leaks into this one.
                    handler.HandleAgentProviderError(9103, new RuntimeProviderError { HttpStatusCode = 401 });
                    await handler.HandleAgentProcessExitedAsync(9103, 0, "cpt_test", "msn_test").ConfigureAwait(false);
                    await handler.HandleAgentProcessExitedAsync(9103, 2, "cpt_test", "msn_test").ConfigureAwait(false);

                    AssertEqual(3, admiral.ExitInfos.Count);
                    AssertEqual(RuntimeFailureKindEnum.Crash, admiral.ExitInfos[0].FailureKind, "output text never classifies");
                    AssertNull(admiral.ExitInfos[0].ProviderError);
                    AssertEqual(RuntimeFailureKindEnum.Clean, admiral.ExitInfos[1].FailureKind, "a zero exit is clean even with a provider error");
                    AssertEqual(RuntimeFailureKindEnum.Crash, admiral.ExitInfos[2].FailureKind, "the provider error is consumed by the exit that read it");
                }
            }));

            cases.Add(CaseAsync("agent_status_line_cannot_complete_fail_or_review", "An [ARMADA:STATUS] line cannot complete, fail, cancel, or review a mission", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    AgentLifecycleHandler handler = CreateHandler(testDb.Driver, out _);

                    Mission mission = new Mission("Status mission");
                    mission.Status = MissionStatusEnum.InProgress;
                    await testDb.Driver.Missions.CreateAsync(mission).ConfigureAwait(false);

                    // Before the fix any state-machine-valid status named in output was applied, so printing a doc
                    // containing "[ARMADA:STATUS] Complete" completed the mission without landing.
                    MissionStatusEnum[] refused = new MissionStatusEnum[]
                    {
                        MissionStatusEnum.Complete,
                        MissionStatusEnum.Failed,
                        MissionStatusEnum.Cancelled,
                        MissionStatusEnum.Review,
                        MissionStatusEnum.WorkProduced
                    };
                    foreach (MissionStatusEnum status in refused)
                    {
                        bool changed = await handler.ApplyAgentReportedStatusAsync(mission.Id, status).ConfigureAwait(false);
                        AssertFalse(changed, status + " must not be applied from an agent status line");
                    }

                    Mission? reread = await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.InProgress, reread!.Status, "mission stays InProgress");

                    AssertTrue(await handler.ApplyAgentReportedStatusAsync(mission.Id, MissionStatusEnum.Testing).ConfigureAwait(false), "Testing is agent-reportable");
                    reread = await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.Testing, reread!.Status);
                    AssertTrue(await handler.ApplyAgentReportedStatusAsync(mission.Id, MissionStatusEnum.InProgress).ConfigureAwait(false), "InProgress is agent-reportable from Testing");
                }
            }));

            cases.Add(CaseAsync("combined_output_status_line_does_not_change_status", "A status line on the combined (stderr) channel is informational only", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    AgentLifecycleHandler handler = CreateHandler(testDb.Driver, out _);

                    Captain captain = new Captain("status-captain", AgentRuntimeEnum.Codex);
                    Mission mission = new Mission("Status mission");
                    mission.Status = MissionStatusEnum.InProgress;
                    mission.CaptainId = captain.Id;
                    captain.CurrentMissionId = mission.Id;
                    await testDb.Driver.Captains.CreateAsync(captain).ConfigureAwait(false);
                    await testDb.Driver.Missions.CreateAsync(mission).ConfigureAwait(false);

                    int processId = 838383;
                    RegisterTrackedProcess(handler, processId, captain.Id, mission.Id);

                    // Codex prints command output (here: cat of a doc) on stderr, which reaches only the combined channel.
                    handler.HandleAgentOutput(processId, "[ARMADA:STATUS] Testing");

                    DateTime deadline = DateTime.UtcNow.AddSeconds(5);
                    List<Signal> signals = new List<Signal>();
                    while (DateTime.UtcNow < deadline)
                    {
                        signals = await testDb.Driver.Signals.EnumerateRecentAsync(50).ConfigureAwait(false);
                        if (signals.Count > 0) break;
                        await Task.Delay(25).ConfigureAwait(false);
                    }

                    AssertEqual(1, signals.Count, "the line is still recorded as an informational progress signal");
                    Mission? reread = await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.InProgress, reread!.Status, "only the agent's stdout may toggle the phase");
                }
            }));

            cases.Add(CaseAsync("handle_agent_heartbeat_updates_mission_and_voyage_timestamps", "HandleAgentHeartbeat updates mission and voyage timestamps", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    AgentLifecycleHandler handler = CreateHandler(testDb.Driver, out _);

                    Captain captain = new Captain("heartbeat-captain", AgentRuntimeEnum.Cursor);
                    await testDb.Driver.Captains.CreateAsync(captain).ConfigureAwait(false);

                    Voyage voyage = new Voyage("Heartbeat voyage", "Telemetry proof");
                    await testDb.Driver.Voyages.CreateAsync(voyage).ConfigureAwait(false);

                    Mission mission = new Mission("Heartbeat mission")
                    {
                        VoyageId = voyage.Id
                    };
                    await testDb.Driver.Missions.CreateAsync(mission).ConfigureAwait(false);

                    Mission? beforeMission = await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                    Voyage? beforeVoyage = await testDb.Driver.Voyages.ReadAsync(voyage.Id).ConfigureAwait(false);
                    AssertNotNull(beforeMission);
                    AssertNotNull(beforeVoyage);

                    RegisterTrackedProcess(handler, 424242, captain.Id, mission.Id);

                    await Task.Delay(20).ConfigureAwait(false);
                    handler.HandleAgentHeartbeat(424242, "still running");

                    await WaitForConditionAsync(async () =>
                    {
                        Captain? refreshedCaptain = await testDb.Driver.Captains.ReadAsync(captain.Id).ConfigureAwait(false);
                        Mission? refreshedMission = await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                        Voyage? refreshedVoyage = await testDb.Driver.Voyages.ReadAsync(voyage.Id).ConfigureAwait(false);

                        return refreshedCaptain?.LastHeartbeatUtc.HasValue == true
                            && refreshedMission != null
                            && refreshedMission.LastUpdateUtc > beforeMission!.LastUpdateUtc
                            && refreshedVoyage != null
                            && refreshedVoyage.LastUpdateUtc > beforeVoyage!.LastUpdateUtc;
                    }).ConfigureAwait(false);
                }
            }));

            cases.Add(CaseAsync("silent_process_refreshes_liveness_not_heartbeat", "Silent running process refreshes process-liveness but not the output heartbeat", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    AgentLifecycleHandler handler = CreateHandler(testDb.Driver, out ArmadaSettings settings);
                    settings.HeartbeatIntervalSeconds = 5;

                    Captain captain = new Captain("silent-heartbeat-captain", AgentRuntimeEnum.Cursor);
                    await testDb.Driver.Captains.CreateAsync(captain).ConfigureAwait(false);

                    Voyage voyage = new Voyage("Silent heartbeat voyage", "Telemetry proof");
                    await testDb.Driver.Voyages.CreateAsync(voyage).ConfigureAwait(false);

                    Mission mission = new Mission("Silent heartbeat mission")
                    {
                        VoyageId = voyage.Id
                    };
                    await testDb.Driver.Missions.CreateAsync(mission).ConfigureAwait(false);

                    Mission? beforeMission = await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                    Voyage? beforeVoyage = await testDb.Driver.Voyages.ReadAsync(voyage.Id).ConfigureAwait(false);
                    AssertNotNull(beforeMission);
                    AssertNotNull(beforeVoyage);

                    using Process process = StartSilentProcess();
                    RegisterTrackedProcess(handler, process.Id, captain.Id, mission.Id);
                    StartTrackedProcessHeartbeat(handler, process.Id, captain.Id, mission.Id);

                    try
                    {
                        await WaitForConditionAsync(async () =>
                        {
                            Captain? refreshedCaptain = await testDb.Driver.Captains.ReadAsync(captain.Id).ConfigureAwait(false);
                            Mission? refreshedMission = await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                            Voyage? refreshedVoyage = await testDb.Driver.Voyages.ReadAsync(voyage.Id).ConfigureAwait(false);

                            // The liveness loop refreshes process-liveness for a merely-alive process.
                            return refreshedCaptain?.LastProcessAliveUtc.HasValue == true;
                        }, TimeSpan.FromSeconds(8)).ConfigureAwait(false);

                        // Invariant behind the stall-detection fix: a silent-but-alive process
                        // refreshes process-liveness but must NOT advance the output heartbeat,
                        // otherwise a stalled agent would be masked.
                        Captain? afterCaptain = await testDb.Driver.Captains.ReadAsync(captain.Id).ConfigureAwait(false);
                        AssertNotNull(afterCaptain);
                        AssertNotNull(afterCaptain!.LastProcessAliveUtc, "process-liveness refreshed for a silent process");
                        AssertNull(afterCaptain.LastHeartbeatUtc, "output heartbeat NOT advanced by a silent process");
                    }
                    finally
                    {
                        try
                        {
                            if (!process.HasExited)
                            {
                                process.Kill(entireProcessTree: true);
                                process.WaitForExit(5000);
                            }
                        }
                        catch { }
                    }
                }
            }));

            cases.Add(CaseAsync("get_and_clear_mission_output_prefers_final_artifact", "GetAndClearMissionOutput prefers final message artifact over streamed output", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    AgentLifecycleHandler handler = CreateHandler(testDb.Driver, out _);
                    string missionId = "msn_final_output_prefers_artifact";
                    string artifactDirectory = Path.Combine(Path.GetTempPath(), "armada_final_output_" + Guid.NewGuid().ToString("N"));
                    string artifactPath = Path.Combine(artifactDirectory, missionId + ".txt");
                    Directory.CreateDirectory(artifactDirectory);

                    try
                    {
                        SeedMissionOutput(handler, missionId, "streamed intermediate output");
                        RegisterFinalMessageArtifact(handler, missionId, artifactPath);
                        await File.WriteAllTextAsync(artifactPath, "[ARMADA:RESULT] COMPLETE\ncanonical final response").ConfigureAwait(false);

                        string? output = handler.GetAndClearMissionOutput(missionId);

                        AssertNotNull(output);
                        AssertContains("canonical final response", output!, "Canonical final response should win over streamed output");
                        AssertFalse(output!.Contains("streamed intermediate output", StringComparison.Ordinal), "Stream noise should not be persisted as AgentOutput when a final artifact exists");
                        AssertFalse(File.Exists(artifactPath), "Final message artifact should be deleted after retrieval");
                    }
                    finally
                    {
                        try { Directory.Delete(artifactDirectory, true); } catch { }
                    }
                }
            }));

            cases.Add(CaseAsync("handle_agent_output_stores_papercut_marker_as_event", "HandleAgentOutput stores a papercut marker as an event", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    AgentLifecycleHandler handler = CreateHandler(testDb.Driver, out _);

                    Vessel vessel = new Vessel("PapercutVessel", "https://github.com/test/papercut");
                    Voyage voyage = new Voyage("Papercut voyage");
                    Captain captain = new Captain("papercut-captain", AgentRuntimeEnum.Cursor);
                    Mission mission = new Mission("Papercut mission");
                    mission.VesselId = vessel.Id;
                    mission.VoyageId = voyage.Id;
                    mission.Persona = "Worker";
                    mission.CaptainId = captain.Id;
                    captain.CurrentMissionId = mission.Id;

                    await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);
                    await testDb.Driver.Voyages.CreateAsync(voyage).ConfigureAwait(false);
                    await testDb.Driver.Captains.CreateAsync(captain).ConfigureAwait(false);
                    await testDb.Driver.Missions.CreateAsync(mission).ConfigureAwait(false);

                    int processId = 828282;
                    RegisterTrackedProcess(handler, processId, captain.Id, mission.Id);

                    handler.HandleAgentOutput(
                        processId,
                        "[ARMADA:PAPERCUT] {\"category\":\"MissingDoc\",\"severity\":\"Medium\",\"title\":\"README names a build command that does not exist\",\"path\":\"README.md\"}");

                    List<Papercut> stored = await WaitForPapercutEventsAsync(testDb.Driver, 1).ConfigureAwait(false);

                    AssertEqual(1, stored.Count, "The marker line should produce exactly one papercut event");

                    Papercut papercut = stored[0];
                    AssertEqual(PapercutCategoryEnum.MissingDoc, papercut.Category, "Stored papercut category");
                    AssertEqual(PapercutSeverityEnum.Medium, papercut.Severity, "Stored papercut severity");
                    AssertEqual("README.md", papercut.Path, "Stored papercut path");

                    // The admiral supplies the context, never the captain.
                    AssertEqual(mission.Id, papercut.MissionId, "Admiral-supplied mission id");
                    AssertEqual(captain.Id, papercut.CaptainId, "Admiral-supplied captain id");
                    AssertEqual(vessel.Id, papercut.VesselId, "Admiral-supplied vessel id");
                    AssertEqual(voyage.Id, papercut.VoyageId, "Admiral-supplied voyage id");
                    AssertEqual("Cursor", papercut.Runtime, "Admiral-supplied runtime");

                    // A papercut is not progress: it must not reach the progress signal stream.
                    List<Signal> signals = await testDb.Driver.Signals.EnumerateRecentAsync(50).ConfigureAwait(false);
                    AssertEqual(0, signals.Count, "A papercut must not be recorded as a progress signal");
                }
            }));

            cases.Add(CaseAsync("handle_agent_output_ignores_papercut_from_judge_mission", "HandleAgentOutput ignores a papercut from a judge mission", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    AgentLifecycleHandler handler = CreateHandler(testDb.Driver, out _);

                    Vessel vessel = new Vessel("PapercutJudgeVessel", "https://github.com/test/papercut-judge");
                    Captain captain = new Captain("papercut-judge-captain", AgentRuntimeEnum.Cursor);
                    Mission mission = new Mission("Judge mission");
                    mission.VesselId = vessel.Id;
                    mission.Persona = "Judge";
                    mission.CaptainId = captain.Id;
                    captain.CurrentMissionId = mission.Id;

                    await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);
                    await testDb.Driver.Captains.CreateAsync(captain).ConfigureAwait(false);
                    await testDb.Driver.Missions.CreateAsync(mission).ConfigureAwait(false);

                    int processId = 838383;
                    RegisterTrackedProcess(handler, processId, captain.Id, mission.Id);

                    handler.HandleAgentOutput(
                        processId,
                        "[ARMADA:PAPERCUT] {\"category\":\"RepoFriction\",\"severity\":\"Low\",\"title\":\"the code under review is hard to follow\"}");

                    await Task.Delay(750).ConfigureAwait(false);

                    List<Papercut> stored = await CurrentPapercutEventsAsync(testDb.Driver).ConfigureAwait(false);

                    AssertEqual(0, stored.Count, "A judge reports through its verdict, not through papercuts");
                }
            }));

            cases.Add(CaseAsync("handle_agent_output_caps_stored_papercuts_per_mission", "HandleAgentOutput caps stored papercuts per mission", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    AgentLifecycleHandler handler = CreateHandler(testDb.Driver, out _);

                    Vessel vessel = new Vessel("PapercutCapVessel", "https://github.com/test/papercut-cap");
                    Captain captain = new Captain("papercut-cap-captain", AgentRuntimeEnum.Cursor);
                    Mission mission = new Mission("Capped mission");
                    mission.VesselId = vessel.Id;
                    mission.Persona = "Worker";
                    mission.CaptainId = captain.Id;
                    captain.CurrentMissionId = mission.Id;

                    await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);
                    await testDb.Driver.Captains.CreateAsync(captain).ConfigureAwait(false);
                    await testDb.Driver.Missions.CreateAsync(mission).ConfigureAwait(false);

                    int processId = 848484;
                    RegisterTrackedProcess(handler, processId, captain.Id, mission.Id);

                    for (int i = 0; i < 18; i++)
                    {
                        handler.HandleAgentOutput(
                            processId,
                            "[ARMADA:PAPERCUT] {\"category\":\"Other\",\"severity\":\"Low\",\"title\":\"complaint number " + i + "\"}");
                    }

                    await WaitForPapercutEventsAsync(testDb.Driver, 10).ConfigureAwait(false);
                    await Task.Delay(500).ConfigureAwait(false);

                    List<Papercut> stored = await CurrentPapercutEventsAsync(testDb.Driver).ConfigureAwait(false);

                    AssertEqual(10, stored.Count, "The per-mission cap must bound how many reports one mission can store");
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Agent Lifecycle Handler",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static AgentLifecycleHandler CreateHandler(DatabaseDriver database, out ArmadaSettings settings)
        {
            return CreateHandler(database, out settings, out _);
        }

        private static AgentLifecycleHandler CreateHandler(DatabaseDriver database, out ArmadaSettings settings, string cursorExecutable)
        {
            return CreateHandler(database, out settings, out _, cursorExecutable);
        }

        private static AgentLifecycleHandler CreateHandler(DatabaseDriver database, out ArmadaSettings settings, out StubAdmiralService stubAdmiral, string? cursorExecutable = null)
        {
            LoggingModule logging = CreateLogging();
            settings = CreateSettings();
            AgentRuntimeFactory runtimeFactory = new AgentRuntimeFactory(logging);
            if (cursorExecutable != null)
            {
                // Launch the shim by absolute path, so nothing is written to the user's profile (%APPDATA%\npm on
                // Windows) or looked up through the host PATH.
                runtimeFactory.Override(AgentRuntimeEnum.Cursor, () => new CursorRuntime(logging) { ExecutablePath = cursorExecutable });
            }

            stubAdmiral = new StubAdmiralService();
            IAdmiralService admiral = stubAdmiral;
            IMessageTemplateService templateService = new MessageTemplateService(logging);

            return new AgentLifecycleHandler(
                logging,
                database,
                settings,
                runtimeFactory,
                admiral,
                templateService,
                null,
                null,
                (eventType, message, entityType, entityId, captainId, missionId, vesselId, voyageId) => Task.CompletedTask);
        }

        private static LoggingModule CreateLogging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private static ArmadaSettings CreateSettings()
        {
            ArmadaSettings settings = new ArmadaSettings();
            settings.LogDirectory = Path.Combine(Path.GetTempPath(), "armada_lifecycle_logs_" + Guid.NewGuid().ToString("N"));
            return settings;
        }

        private static void AssertModelArgument(string recordedArgs, string model, string label)
        {
            // The shim writes "$*" on the first line and then one argument per line; check the argv sequence.
            string[] lines = recordedArgs.Replace("\r", "").Split('\n');
            bool found = false;
            for (int i = 1; i + 1 < lines.Length; i++)
            {
                if (lines[i] == "--model" && lines[i + 1] == model) found = true;
            }

            AssertTrue(found, label + "; recorded:\n" + recordedArgs);
        }

        private static async Task<string> WaitForRecordedArgsAsync(string argsFile, string? expectedSubstring = null)
        {
            MonotonicDeadline deadline = MonotonicDeadline.After(TimeSpan.FromSeconds(5));

            while (!deadline.Passed)
            {
                if (File.Exists(argsFile))
                {
                    string contents = await File.ReadAllTextAsync(argsFile).ConfigureAwait(false);
                    if (!String.IsNullOrWhiteSpace(contents) &&
                        (String.IsNullOrEmpty(expectedSubstring) || contents.Contains(expectedSubstring, StringComparison.Ordinal)))
                    {
                        return contents;
                    }
                }

                await Task.Delay(50).ConfigureAwait(false);
            }

            throw new TimeoutException("Timed out waiting for runtime shim args file: " + argsFile);
        }

        private static async Task<string> WaitForFileContainsAsync(string path, string expectedSubstring)
        {
            MonotonicDeadline deadline = MonotonicDeadline.After(TimeSpan.FromSeconds(5));

            while (!deadline.Passed)
            {
                if (File.Exists(path))
                {
                    string contents = await ReadSharedTextAsync(path).ConfigureAwait(false);
                    if (contents.Contains(expectedSubstring, StringComparison.Ordinal))
                    {
                        return contents;
                    }
                }

                await Task.Delay(50).ConfigureAwait(false);
            }

            throw new TimeoutException("Timed out waiting for expected content in file: " + path);
        }

        private static async Task<string> ReadSharedTextAsync(string path)
        {
            using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using StreamReader reader = new StreamReader(stream);
            return await reader.ReadToEndAsync().ConfigureAwait(false);
        }

        private static void RegisterTrackedProcess(AgentLifecycleHandler handler, int processId, string captainId, string missionId)
        {
            FieldInfo captainField = typeof(AgentLifecycleHandler).GetField("_ProcessToCaptain", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Could not find _ProcessToCaptain field");
            FieldInfo missionField = typeof(AgentLifecycleHandler).GetField("_ProcessToMission", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Could not find _ProcessToMission field");

            Dictionary<int, string> captainMap = (Dictionary<int, string>)(captainField.GetValue(handler)
                ?? throw new InvalidOperationException("Captain process map was null"));
            Dictionary<int, string> missionMap = (Dictionary<int, string>)(missionField.GetValue(handler)
                ?? throw new InvalidOperationException("Mission process map was null"));

            lock (captainMap)
            {
                captainMap[processId] = captainId;
                missionMap[processId] = missionId;
            }
        }

        private static void SeedMissionOutput(AgentLifecycleHandler handler, string missionId, string output)
        {
            FieldInfo outputField = typeof(AgentLifecycleHandler).GetField("_MissionOutput", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Could not find _MissionOutput field");

            System.Collections.Concurrent.ConcurrentDictionary<string, System.Text.StringBuilder> outputMap =
                (System.Collections.Concurrent.ConcurrentDictionary<string, System.Text.StringBuilder>)(outputField.GetValue(handler)
                ?? throw new InvalidOperationException("Mission output map was null"));

            outputMap[missionId] = new System.Text.StringBuilder(output);
        }

        private static void RegisterFinalMessageArtifact(AgentLifecycleHandler handler, string missionId, string artifactPath)
        {
            FieldInfo artifactField = typeof(AgentLifecycleHandler).GetField("_MissionFinalMessageFiles", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Could not find _MissionFinalMessageFiles field");

            System.Collections.Concurrent.ConcurrentDictionary<string, string> artifactMap =
                (System.Collections.Concurrent.ConcurrentDictionary<string, string>)(artifactField.GetValue(handler)
                ?? throw new InvalidOperationException("Mission final message map was null"));

            artifactMap[missionId] = artifactPath;
        }

        private static void StartTrackedProcessHeartbeat(AgentLifecycleHandler handler, int processId, string captainId, string missionId)
        {
            MethodInfo method = typeof(AgentLifecycleHandler).GetMethod("StartProcessLivenessHeartbeat", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Could not find StartProcessLivenessHeartbeat method");
            method.Invoke(handler, new object[] { processId, captainId, missionId });
        }

        private static Process StartSilentProcess()
        {
            ProcessStartInfo startInfo;
            if (OperatingSystem.IsWindows())
            {
                startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c ping 127.0.0.1 -n 10 >nul",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
            }
            else
            {
                startInfo = new ProcessStartInfo
                {
                    FileName = "/bin/sh",
                    Arguments = "-c \"sleep 10\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
            }

            return Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start silent heartbeat test process");
        }

        /// <summary>
        /// Enumerate the papercuts currently stored as events, reading each candidate back through
        /// <see cref="PapercutService.TryFromEvent"/> so only genuine papercut events are counted.
        /// </summary>
        /// <param name="database">Database driver to read events from.</param>
        /// <returns>The papercuts read back from stored events.</returns>
        private static async Task<List<Papercut>> CurrentPapercutEventsAsync(DatabaseDriver database)
        {
            List<ArmadaEvent> events = await database.Events
                .EnumerateByTypeAsync(PapercutParser.EventType, 50)
                .ConfigureAwait(false);

            List<Papercut> papercuts = new List<Papercut>();
            foreach (ArmadaEvent evt in events)
            {
                Papercut? papercut = PapercutService.TryFromEvent(evt);
                if (papercut != null) papercuts.Add(papercut);
            }

            return papercuts;
        }

        /// <summary>
        /// Poll until at least the expected number of papercut events are stored, then return them.
        /// The handler writes papercuts on a background task, so a read taken immediately after the
        /// output line races the write.
        /// </summary>
        /// <param name="database">Database driver to read events from.</param>
        /// <param name="expected">Minimum number of papercuts to wait for.</param>
        /// <returns>The papercuts read back from stored events.</returns>
        private static async Task<List<Papercut>> WaitForPapercutEventsAsync(DatabaseDriver database, int expected)
        {
            MonotonicDeadline deadline = MonotonicDeadline.After(TimeSpan.FromSeconds(10));
            List<Papercut> stored = new List<Papercut>();

            while (!deadline.Passed)
            {
                stored = await CurrentPapercutEventsAsync(database).ConfigureAwait(false);
                if (stored.Count >= expected) return stored;
                await Task.Delay(100).ConfigureAwait(false);
            }

            return stored;
        }

        private static async Task WaitForConditionAsync(Func<Task<bool>> predicate, TimeSpan? timeout = null)
        {
            MonotonicDeadline deadline = MonotonicDeadline.After(timeout ?? TimeSpan.FromSeconds(3));

            while (!deadline.Passed)
            {
                if (await predicate().ConfigureAwait(false))
                    return;

                await Task.Delay(50).ConfigureAwait(false);
            }

            throw new TimeoutException("Timed out waiting for asynchronous condition");
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
        /// Admiral service stub whose process-exit handler is a no-op and whose other operations throw.
        /// </summary>
        private sealed class StubAdmiralService : IAdmiralService
        {
            public Func<Captain, Mission, Dock, Task<int>>? OnLaunchAgent { get; set; }
            public Func<Captain, Task>? OnStopAgent { get; set; }
            public Func<Mission, Dock, Task>? OnCaptureDiff { get; set; }
            public Func<Mission, Dock, Task>? OnMissionComplete { get; set; }
            public Func<Voyage, Task>? OnVoyageComplete { get; set; }
            public Func<Mission, Task<bool>>? OnReconcilePullRequest { get; set; }
            public Func<int, bool>? OnIsProcessExitHandled { get; set; }

            public Task<Voyage> DispatchVoyageAsync(string title, string description, string vesselId, List<MissionDescription> missionDescriptions, CancellationToken token = default)
            {
                throw new NotImplementedException();
            }

            public Task<Voyage> DispatchVoyageAsync(string title, string description, string vesselId, List<MissionDescription> missionDescriptions, List<SelectedPlaybook>? selectedPlaybooks, CancellationToken token = default)
            {
                throw new NotImplementedException();
            }

            public Task<Voyage> DispatchVoyageAsync(string title, string description, string vesselId, List<MissionDescription> missionDescriptions, string? pipelineId, CancellationToken token = default)
            {
                throw new NotImplementedException();
            }

            public Task<Voyage> DispatchVoyageAsync(string title, string description, string vesselId, List<MissionDescription> missionDescriptions, string? pipelineId, List<SelectedPlaybook>? selectedPlaybooks, string? captainOverridesJson = null, CancellationToken token = default)
            {
                throw new NotImplementedException();
            }

            public Task<Mission> DispatchMissionAsync(Mission mission, CancellationToken token = default)
            {
                throw new NotImplementedException();
            }

            public Task<ArmadaStatus> GetStatusAsync(CancellationToken token = default)
            {
                throw new NotImplementedException();
            }

            public Task RecallCaptainAsync(string captainId, CancellationToken token = default)
            {
                throw new NotImplementedException();
            }

            public Task RecallAllAsync(CancellationToken token = default)
            {
                throw new NotImplementedException();
            }

            public Task StopAllAgentProcessesAsync(CancellationToken token = default)
            {
                return Task.CompletedTask;
            }

            public Task HealthCheckAsync(CancellationToken token = default)
            {
                throw new NotImplementedException();
            }

            public Task CleanupStaleCaptainsAsync(CancellationToken token = default)
            {
                throw new NotImplementedException();
            }

            public List<RuntimeExitInfo> ExitInfos { get; } = new List<RuntimeExitInfo>();

            public Task HandleProcessExitAsync(int processId, int? exitCode, string captainId, string missionId, CancellationToken token = default)
            {
                return Task.CompletedTask;
            }

            public Task HandleProcessExitAsync(int processId, RuntimeExitInfo exitInfo, string captainId, string missionId, CancellationToken token = default)
            {
                lock (ExitInfos) ExitInfos.Add(exitInfo);
                return Task.CompletedTask;
            }

            public Task<Armada.Core.Services.AutoLandDecision?> EvaluateAutoLandAsync(string missionId, CancellationToken token = default)
            {
                return Task.FromResult<Armada.Core.Services.AutoLandDecision?>(null);
            }

            public Task<Armada.Core.Services.DispatchValidationResult> ValidateDispatchAsync(string? objectiveId, string? pipelineId, string? pipelineName, string? vesselId, int missionCount, bool allowBareVoyage, CancellationToken token = default)
            {
                return Task.FromResult(Armada.Core.Services.DispatchValidationResult.Valid(pipelineId, allowBareVoyage && (string.IsNullOrEmpty(vesselId) || missionCount == 0)));
            }
        }

        /// <summary>
        /// Writes a temporary cursor-agent shim (launched by absolute path through a runtime factory override) that
        /// records its arguments and simulates success, invalid-model, and hang behaviors. Nothing outside the temp
        /// directory is touched: no PATH change and no file under the user profile.
        /// </summary>
        private sealed class CursorShimScope : IDisposable
        {
            public string ArgsFile { get; }

            public string ShimPath { get; }

            private readonly string _tempDirectory;

            private CursorShimScope(string tempDirectory, string argsFile, string shimPath)
            {
                _tempDirectory = tempDirectory;
                ArgsFile = argsFile;
                ShimPath = shimPath;
            }

            public static CursorShimScope Create()
            {
                string tempDirectory = Path.Combine(Path.GetTempPath(), "armada_cursor_shim_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDirectory);

                string argsFile = Path.Combine(tempDirectory, "cursor-args.txt");
                Environment.SetEnvironmentVariable("ARMADA_TEST_CURSOR_ARGS_FILE", argsFile);

                string shimPath;
                if (OperatingSystem.IsWindows())
                {
                    shimPath = Path.Combine(tempDirectory, "cursor-agent.cmd");
                    File.WriteAllText(shimPath, BuildWindowsShim());
                }
                else
                {
                    shimPath = Path.Combine(tempDirectory, "cursor-agent");
                    File.WriteAllText(shimPath, BuildUnixShim());
                    File.SetUnixFileMode(
                        shimPath,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                        UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                        UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                }

                return new CursorShimScope(tempDirectory, argsFile, shimPath);
            }

            public void Dispose()
            {
                Environment.SetEnvironmentVariable("ARMADA_TEST_CURSOR_ARGS_FILE", null);
                try { Directory.Delete(_tempDirectory, true); } catch { }
            }

            private static string BuildWindowsShim()
            {
                return "@echo off\r\n" +
                    "setlocal EnableExtensions EnableDelayedExpansion\r\n" +
                    "set \"ARGS_FILE=%ARMADA_TEST_CURSOR_ARGS_FILE%\"\r\n" +
                    "set \"ALL_ARGS=%*\"\r\n" +
                    "if defined ARMADA_MCP_TOKEN >> \"%ARGS_FILE%\" echo ARMADA_MCP_TOKEN=!ARMADA_MCP_TOKEN!\r\n" +
                    ">> \"%ARGS_FILE%\" echo(!ALL_ARGS!\r\n" +
                    "set \"MODEL=\"\r\n" +
                    ":loop\r\n" +
                    "if \"%~1\"==\"\" goto done\r\n" +
                    ">> \"%ARGS_FILE%\" echo %~1\r\n" +
                    "if /I \"%~1\"==\"--model\" set \"MODEL=%~2\"\r\n" +
                    "shift\r\n" +
                    "goto loop\r\n" +
                    ":done\r\n" +
                    "if /I \"%MODEL%\"==\"bad-model\" (\r\n" +
                    "  >&2 echo unknown model '%MODEL%'\r\n" +
                    "  exit /b 3\r\n" +
                    ")\r\n" +
                    "if /I \"%MODEL%\"==\"hang-model\" (\r\n" +
                    "  ping 127.0.0.1 -n 10 >nul\r\n" +
                    "  exit /b 0\r\n" +
                    ")\r\n" +
                    "echo ok\r\n" +
                    "exit /b 0\r\n";
            }

            private static string BuildUnixShim()
            {
                return "#!/usr/bin/env sh\n" +
                    "args_file=\"$ARMADA_TEST_CURSOR_ARGS_FILE\"\n" +
                    "if [ -n \"$ARMADA_MCP_TOKEN\" ]; then printf 'ARMADA_MCP_TOKEN=%s\\n' \"$ARMADA_MCP_TOKEN\" >> \"$args_file\"; fi\n" +
                    "printf '%s\\n' \"$*\" >> \"$args_file\"\n" +
                    "prev=\"\"\n" +
                    "model=\"\"\n" +
                    "for arg in \"$@\"; do\n" +
                    "  printf '%s\\n' \"$arg\" >> \"$args_file\"\n" +
                    "  if [ \"$prev\" = \"--model\" ]; then\n" +
                    "    model=\"$arg\"\n" +
                    "  fi\n" +
                    "  prev=\"$arg\"\n" +
                    "done\n" +
                    "if [ \"$model\" = \"bad-model\" ]; then\n" +
                    "  printf '%s\\n' \"unknown model '$model'\" >&2\n" +
                    "  exit 3\n" +
                    "fi\n" +
                    "if [ \"$model\" = \"hang-model\" ]; then\n" +
                    "  sleep 10\n" +
                    "  exit 0\n" +
                    "fi\n" +
                    "printf '%s\\n' ok\n" +
                    "exit 0\n";
            }
        }

        #endregion
    }
}

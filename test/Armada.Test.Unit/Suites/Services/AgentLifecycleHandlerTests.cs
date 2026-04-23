namespace Armada.Test.Unit.Suites.Services
{
    using System.Diagnostics;
    using System.IO;
    using System.Reflection;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using Armada.Runtimes;
    using Armada.Server;
    using Armada.Test.Common;
    using Armada.Test.Unit.TestHelpers;
    using SyslogLogging;

    /// <summary>
    /// Tests for runtime model validation and launch passthrough in AgentLifecycleHandler.
    /// </summary>
    public class AgentLifecycleHandlerTests : TestSuite
    {
        /// <summary>
        /// Suite name.
        /// </summary>
        public override string Name => "Agent Lifecycle Handler";

        /// <summary>
        /// Run all tests.
        /// </summary>
        protected override async Task RunTestsAsync()
        {
            await RunTest("ValidateModelAsync returns null and forwards model to runtime", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                using (CursorShimScope shim = CursorShimScope.Create())
                {
                    AgentLifecycleHandler handler = CreateHandler(testDb.Driver, out _);

                    string? error = await handler.ValidateModelAsync(AgentRuntimeEnum.Cursor, "gpt-5.4-mini").ConfigureAwait(false);
                    string args = await WaitForRecordedArgsAsync(shim.ArgsFile, "gpt-5.4-mini").ConfigureAwait(false);

                    AssertNull(error, "Valid model should pass validation");
                    AssertContains("--model", args, "Validation runtime args should include model flag");
                    AssertContains("gpt-5.4-mini", args, "Validation runtime args should include requested model");
                }
            });

            await RunTest("ValidateCaptainModelAsync returns extracted runtime error for invalid model", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                using (CursorShimScope shim = CursorShimScope.Create())
                {
                    AgentLifecycleHandler handler = CreateHandler(testDb.Driver, out _);
                    Captain captain = new Captain("validation-captain", AgentRuntimeEnum.Cursor)
                    {
                        Model = "bad-model"
                    };

                    string? error = await handler.ValidateCaptainModelAsync(captain).ConfigureAwait(false);
                    string args = await WaitForRecordedArgsAsync(shim.ArgsFile, "bad-model").ConfigureAwait(false);

                    AssertNotNull(error, "Invalid model should return an error");
                    AssertContains("bad-model", error!, "Error should include invalid model");
                    AssertContains("unknown model 'bad-model'", error!, "Error should include runtime output");
                    AssertContains("--model", args, "Captain validation should launch runtime with model flag");
                }
            });

            await RunTest("ValidateCaptainModelAsync returns timeout error when runtime does not exit", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                using (CursorShimScope shim = CursorShimScope.Create())
                {
                    AgentLifecycleHandler handler = CreateHandler(testDb.Driver, out _);
                    Captain captain = new Captain("timeout-captain", AgentRuntimeEnum.Cursor)
                    {
                        Model = "hang-model"
                    };

                    string? error = await handler.ValidateCaptainModelAsync(captain).ConfigureAwait(false);
                    string args = await WaitForRecordedArgsAsync(shim.ArgsFile, "hang-model").ConfigureAwait(false);

                    AssertNotNull(error, "Timed-out validation should return an error");
                    AssertContains("hang-model", error!, "Error should include requested model");
                    AssertContains("timed out", error!, "Error should report validation timeout");
                    AssertContains("--model", args, "Timed-out validation should still launch runtime with model flag");
                }
            });

            await RunTest("HandleLaunchAgentAsync passes captain model to runtime startup", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                using (CursorShimScope shim = CursorShimScope.Create())
                {
                    AgentLifecycleHandler handler = CreateHandler(testDb.Driver, out ArmadaSettings settings);
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
                            Persona = "TestEngineer",
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
                    }
                    finally
                    {
                        try { Directory.Delete(worktreePath, true); } catch { }
                    }
                }
            });

            await RunTest("HandleAgentHeartbeat updates mission and voyage timestamps", async () =>
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
            });

            await RunTest("Silent running process still refreshes captain and mission heartbeats", async () =>
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

                            return refreshedCaptain?.LastHeartbeatUtc.HasValue == true
                                && refreshedMission != null
                                && refreshedMission.LastUpdateUtc > beforeMission!.LastUpdateUtc
                                && refreshedVoyage != null
                                && refreshedVoyage.LastUpdateUtc > beforeVoyage!.LastUpdateUtc;
                        }, TimeSpan.FromSeconds(8)).ConfigureAwait(false);
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
            });

            await RunTest("GetAndClearMissionOutput prefers final message artifact over streamed output", async () =>
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
            });
        }

        private AgentLifecycleHandler CreateHandler(DatabaseDriver database, out ArmadaSettings settings)
        {
            LoggingModule logging = CreateLogging();
            settings = CreateSettings();
            AgentRuntimeFactory runtimeFactory = new AgentRuntimeFactory(logging);
            IAdmiralService admiral = new StubAdmiralService();
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

        private static async Task<string> WaitForRecordedArgsAsync(string argsFile, string? expectedSubstring = null)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);

            while (DateTime.UtcNow < deadline)
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
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);

            while (DateTime.UtcNow < deadline)
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

        private static async Task WaitForConditionAsync(Func<Task<bool>> predicate, TimeSpan? timeout = null)
        {
            DateTime deadline = DateTime.UtcNow.Add(timeout ?? TimeSpan.FromSeconds(3));

            while (DateTime.UtcNow < deadline)
            {
                if (await predicate().ConfigureAwait(false))
                    return;

                await Task.Delay(50).ConfigureAwait(false);
            }

            throw new TimeoutException("Timed out waiting for asynchronous condition");
        }

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

            public Task<Voyage> DispatchVoyageAsync(string title, string description, string vesselId, List<MissionDescription> missionDescriptions, string? pipelineId, List<SelectedPlaybook>? selectedPlaybooks, CancellationToken token = default)
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

            public Task HealthCheckAsync(CancellationToken token = default)
            {
                throw new NotImplementedException();
            }

            public Task CleanupStaleCaptainsAsync(CancellationToken token = default)
            {
                throw new NotImplementedException();
            }

            public Task HandleProcessExitAsync(int processId, int? exitCode, string captainId, string missionId, CancellationToken token = default)
            {
                return Task.CompletedTask;
            }
        }

        private sealed class CursorShimScope : IDisposable
        {
            public string ArgsFile { get; }

            private readonly string _tempDirectory;
            private readonly string _originalPath;
            private readonly string? _windowsShimPath;
            private readonly string? _windowsShimBackupPath;

            private CursorShimScope(string tempDirectory, string argsFile, string originalPath, string? windowsShimPath, string? windowsShimBackupPath)
            {
                _tempDirectory = tempDirectory;
                ArgsFile = argsFile;
                _originalPath = originalPath;
                _windowsShimPath = windowsShimPath;
                _windowsShimBackupPath = windowsShimBackupPath;
            }

            public static CursorShimScope Create()
            {
                string tempDirectory = Path.Combine(Path.GetTempPath(), "armada_cursor_shim_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDirectory);

                string argsFile = Path.Combine(tempDirectory, "cursor-args.txt");
                string originalPath = Environment.GetEnvironmentVariable("PATH") ?? String.Empty;
                string? windowsShimPath = null;
                string? windowsShimBackupPath = null;

                Environment.SetEnvironmentVariable("ARMADA_TEST_CURSOR_ARGS_FILE", argsFile);

                if (OperatingSystem.IsWindows())
                {
                    string npmDirectory = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "npm");
                    Directory.CreateDirectory(npmDirectory);

                    windowsShimPath = Path.Combine(npmDirectory, "cursor-agent.cmd");
                    if (File.Exists(windowsShimPath))
                    {
                        windowsShimBackupPath = Path.Combine(tempDirectory, "cursor-agent.original.cmd");
                        File.Copy(windowsShimPath, windowsShimBackupPath, true);
                    }

                    File.WriteAllText(windowsShimPath, BuildWindowsShim());
                }
                else
                {
                    string shimPath = Path.Combine(tempDirectory, "cursor-agent");
                    File.WriteAllText(shimPath, BuildUnixShim());
                    File.SetUnixFileMode(
                        shimPath,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                        UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                        UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                    Environment.SetEnvironmentVariable("PATH", tempDirectory + Path.PathSeparator + originalPath);
                }

                return new CursorShimScope(tempDirectory, argsFile, originalPath, windowsShimPath, windowsShimBackupPath);
            }

            public void Dispose()
            {
                Environment.SetEnvironmentVariable("ARMADA_TEST_CURSOR_ARGS_FILE", null);
                Environment.SetEnvironmentVariable("PATH", _originalPath);

                if (OperatingSystem.IsWindows() && !String.IsNullOrEmpty(_windowsShimPath))
                {
                    try
                    {
                        if (!String.IsNullOrEmpty(_windowsShimBackupPath) && File.Exists(_windowsShimBackupPath))
                        {
                            File.Copy(_windowsShimBackupPath, _windowsShimPath, true);
                        }
                        else if (File.Exists(_windowsShimPath))
                        {
                            File.Delete(_windowsShimPath);
                        }
                    }
                    catch { }
                }

                try { Directory.Delete(_tempDirectory, true); } catch { }
            }

            private static string BuildWindowsShim()
            {
                return "@echo off\r\n" +
                    "setlocal EnableExtensions EnableDelayedExpansion\r\n" +
                    "set \"ARGS_FILE=%ARMADA_TEST_CURSOR_ARGS_FILE%\"\r\n" +
                    "set \"ALL_ARGS=%*\"\r\n" +
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
    }
}

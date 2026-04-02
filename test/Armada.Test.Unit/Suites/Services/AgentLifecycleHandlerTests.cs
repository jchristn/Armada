namespace Armada.Test.Unit.Suites.Services
{
    using System.Diagnostics;
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

    public class AgentLifecycleHandlerTests : TestSuite
    {
        public override string Name => "Agent Lifecycle Handler Tests";

        private sealed class CaptainWithModel : Captain
        {
            public string? Model { get; set; } = null;
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
                throw new NotSupportedException();
            }

            public Task<Voyage> DispatchVoyageAsync(string title, string description, string vesselId, List<MissionDescription> missionDescriptions, string? pipelineId, CancellationToken token = default)
            {
                throw new NotSupportedException();
            }

            public Task<Mission> DispatchMissionAsync(Mission mission, CancellationToken token = default)
            {
                throw new NotSupportedException();
            }

            public Task<ArmadaStatus> GetStatusAsync(CancellationToken token = default)
            {
                throw new NotSupportedException();
            }

            public Task RecallCaptainAsync(string captainId, CancellationToken token = default)
            {
                throw new NotSupportedException();
            }

            public Task RecallAllAsync(CancellationToken token = default)
            {
                throw new NotSupportedException();
            }

            public Task HealthCheckAsync(CancellationToken token = default)
            {
                throw new NotSupportedException();
            }

            public Task CleanupStaleCaptainsAsync(CancellationToken token = default)
            {
                throw new NotSupportedException();
            }

            public Task HandleProcessExitAsync(int processId, int? exitCode, string captainId, string missionId, CancellationToken token = default)
            {
                return Task.CompletedTask;
            }
        }

        private sealed class RuntimeCommandShim : IDisposable
        {
            public string ArgsFilePath { get; }

            private readonly string _RootPath;
            private readonly string _OriginalPath;
            private readonly string? _OriginalAppData;
            private readonly string _WrapperPath;
            private readonly string? _WrapperBackupPath;

            public RuntimeCommandShim(string commandName)
            {
                _RootPath = Path.Combine(Path.GetTempPath(), "armada_runtime_shim_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(_RootPath);
                ArgsFilePath = Path.Combine(_RootPath, "args.txt");

                _OriginalPath = Environment.GetEnvironmentVariable("PATH") ?? "";
                _OriginalAppData = OperatingSystem.IsWindows()
                    ? Environment.GetEnvironmentVariable("APPDATA")
                    : null;

                string shimDirectory;
                if (OperatingSystem.IsWindows())
                {
                    string shimAppData = Path.Combine(_RootPath, "appdata");
                    Environment.SetEnvironmentVariable("APPDATA", shimAppData);
                    string resolvedAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    shimDirectory = Path.Combine(resolvedAppData, "npm");
                    Directory.CreateDirectory(shimDirectory);
                    _WrapperPath = Path.Combine(shimDirectory, commandName + ".cmd");
                    _WrapperBackupPath = File.Exists(_WrapperPath)
                        ? Path.Combine(_RootPath, commandName + ".cmd.bak")
                        : null;

                    if (_WrapperBackupPath != null)
                        File.Copy(_WrapperPath, _WrapperBackupPath, true);

                    File.WriteAllText(_WrapperPath,
                        "@echo off\r\n" +
                        "echo %* > \"" + ArgsFilePath + "\"\r\n" +
                        "ping 127.0.0.1 -n 2 > nul\r\n" +
                        "exit /b 0\r\n");
                }
                else
                {
                    shimDirectory = Path.Combine(_RootPath, "bin");
                    Directory.CreateDirectory(shimDirectory);
                    _WrapperPath = Path.Combine(shimDirectory, commandName);
                    _WrapperBackupPath = null;

                    File.WriteAllText(_WrapperPath,
                        "#!/bin/sh\n" +
                        "printf '%s\\n' \"$*\" > '" + ArgsFilePath.Replace("'", "'\"'\"'") + "'\n" +
                        "sleep 1\n");

                    ProcessStartInfo chmodStartInfo = new ProcessStartInfo
                    {
                        FileName = "chmod",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    chmodStartInfo.ArgumentList.Add("+x");
                    chmodStartInfo.ArgumentList.Add(_WrapperPath);
                    Process chmod = Process.Start(chmodStartInfo)
                        ?? throw new InvalidOperationException("Failed to start chmod for runtime shim.");
                    chmod.WaitForExit();
                    if (chmod.ExitCode != 0)
                        throw new InvalidOperationException("chmod failed for runtime shim.");
                }

                Environment.SetEnvironmentVariable("PATH", shimDirectory + Path.PathSeparator + _OriginalPath);
            }

            public void Dispose()
            {
                Environment.SetEnvironmentVariable("PATH", _OriginalPath);

                if (OperatingSystem.IsWindows())
                    Environment.SetEnvironmentVariable("APPDATA", _OriginalAppData);

                try
                {
                    if (_WrapperBackupPath != null && File.Exists(_WrapperBackupPath))
                    {
                        File.Copy(_WrapperBackupPath, _WrapperPath, true);
                    }
                    else if (File.Exists(_WrapperPath))
                    {
                        File.Delete(_WrapperPath);
                    }
                }
                catch
                {
                }

                try
                {
                    if (Directory.Exists(_RootPath))
                        Directory.Delete(_RootPath, true);
                }
                catch
                {
                }
            }
        }

        private LoggingModule CreateLogging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private static ArmadaSettings CreateSettings(string rootPath)
        {
            return new ArmadaSettings
            {
                DataDirectory = rootPath,
                DatabasePath = Path.Combine(rootPath, "armada.db"),
                LogDirectory = Path.Combine(rootPath, "logs"),
                DocksDirectory = Path.Combine(rootPath, "docks"),
                ReposDirectory = Path.Combine(rootPath, "repos")
            };
        }

        private AgentLifecycleHandler CreateHandler(LoggingModule logging, ArmadaSettings settings, TestDatabase database)
        {
            AgentRuntimeFactory runtimeFactory = new AgentRuntimeFactory(logging);
            IMessageTemplateService templateService = new MessageTemplateService(logging);

            return new AgentLifecycleHandler(
                logging,
                database.Driver,
                settings,
                runtimeFactory,
                new StubAdmiralService(),
                templateService,
                null,
                null,
                (eventType, message, entityType, entityId, captainId, missionId, vesselId, voyageId) => Task.CompletedTask);
        }

        private static async Task<string> ReadFileWhenReadyAsync(string filePath)
        {
            for (int attempt = 0; attempt < 50; attempt++)
            {
                if (File.Exists(filePath))
                {
                    try
                    {
                        return await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
                    }
                    catch (IOException)
                    {
                    }
                }

                await Task.Delay(100).ConfigureAwait(false);
            }

            throw new Exception("Timed out waiting for expected file content: " + filePath);
        }

        protected override async Task RunTestsAsync()
        {
            await RunTest("HandleLaunchAgentAsync Blank Model Throws", async () =>
            {
                using TestDatabase database = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                string rootPath = Path.Combine(Path.GetTempPath(), "armada_lifecycle_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(rootPath);

                try
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings(rootPath);
                    AgentLifecycleHandler handler = CreateHandler(logging, settings, database);

                    CaptainWithModel captain = new CaptainWithModel
                    {
                        Name = "Lifecycle Captain",
                        Runtime = AgentRuntimeEnum.Cursor,
                        Model = "   "
                    };

                    Mission mission = new Mission("Launch mission")
                    {
                        Persona = "TestEngineer",
                        BranchName = "feature/test",
                        VesselId = "vsl_test"
                    };

                    Dock dock = new Dock("vsl_test")
                    {
                        WorktreePath = rootPath,
                        BranchName = "feature/test"
                    };

                    InvalidOperationException ex = await AssertThrowsAsyncWithResult<InvalidOperationException>(
                        () => handler.HandleLaunchAgentAsync(captain, mission, dock)).ConfigureAwait(false);
                    AssertContains("invalid blank model value", ex.Message);
                }
                finally
                {
                    try
                    {
                        if (Directory.Exists(rootPath))
                            Directory.Delete(rootPath, true);
                    }
                    catch
                    {
                    }
                }
            });

            await RunTest("HandleLaunchAgentAsync Trims Model Before Runtime Start", async () =>
            {
                using TestDatabase database = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using RuntimeCommandShim shim = new RuntimeCommandShim("cursor-agent");
                string rootPath = Path.Combine(Path.GetTempPath(), "armada_lifecycle_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(rootPath);

                try
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings(rootPath);
                    AgentLifecycleHandler handler = CreateHandler(logging, settings, database);

                    CaptainWithModel captain = new CaptainWithModel
                    {
                        Name = "Lifecycle Captain",
                        Runtime = AgentRuntimeEnum.Cursor,
                        Model = "  cursor-sonnet  "
                    };

                    Mission mission = new Mission("Launch mission")
                    {
                        Persona = "TestEngineer",
                        BranchName = "feature/test",
                        VesselId = "vsl_test"
                    };

                    Dock dock = new Dock("vsl_test")
                    {
                        WorktreePath = rootPath,
                        BranchName = "feature/test"
                    };

                    int processId = await handler.HandleLaunchAgentAsync(captain, mission, dock).ConfigureAwait(false);
                    AssertTrue(processId > 0, "Expected a valid process ID");

                    string missionLogPath = Path.Combine(settings.LogDirectory, "missions", mission.Id + ".log");
                    string logContents = await ReadFileWhenReadyAsync(missionLogPath).ConfigureAwait(false);
                    AssertContains("--model cursor-sonnet", logContents);
                    AssertFalse(logContents.Contains("--model   cursor-sonnet"), "Expected trimmed model value");
                    AssertFalse(logContents.Contains("cursor-sonnet  "), "Expected trailing whitespace to be trimmed");

                    try
                    {
                        Process process = Process.GetProcessById(processId);
                        process.WaitForExit(5000);
                    }
                    catch
                    {
                    }
                }
                finally
                {
                    try
                    {
                        if (Directory.Exists(rootPath))
                            Directory.Delete(rootPath, true);
                    }
                    catch
                    {
                    }
                }
            });
        }

        private async Task<TException> AssertThrowsAsyncWithResult<TException>(Func<Task> action) where TException : Exception
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (TException ex)
            {
                return ex;
            }

            throw new Exception("Assertion failed: Expected " + typeof(TException).Name + " but no exception was thrown");
        }
    }
}

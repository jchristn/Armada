namespace Armada.Test.Unit.Suites.Services
{
    using System.Net;
    using System.Net.Http;
    using System.Net.Sockets;
    using System.Text.Json;
    using System.IO;
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using Armada.Runtimes;
    using Armada.Server;
    using Armada.Server.Mcp;
    using Armada.Server.Mcp.Tools;
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

            await RunTest("Captain REST create rejects invalid model with validation error", async () =>
            {
                using (CursorShimScope shim = CursorShimScope.Create())
                await using (LocalServerScope server = await LocalServerScope.CreateAsync().ConfigureAwait(false))
                {
                    HttpResponseMessage response = await server.Client.PostAsync(
                        "/api/v1/captains",
                        JsonHelper.ToJsonContent(new
                        {
                            Name = "invalid-rest-captain",
                            Runtime = "Cursor",
                            Model = "bad-model"
                        })).ConfigureAwait(false);

                    ArmadaErrorResponse error = await JsonHelper.DeserializeAsync<ArmadaErrorResponse>(response).ConfigureAwait(false);

                    AssertEqual(HttpStatusCode.BadRequest, response.StatusCode);
                    AssertContains("bad-model", error.Message ?? String.Empty, "Error should include the rejected model");
                    AssertContains("unknown model 'bad-model'", error.Message ?? String.Empty, "Error should include runtime validation output");
                    string args = await WaitForRecordedArgsAsync(shim.ArgsFile, "bad-model").ConfigureAwait(false);
                    AssertContains("--model", args, "REST create should launch validation with model flag");
                }
            });

            await RunTest("Captain REST update skips validation when model is unchanged", async () =>
            {
                using (CursorShimScope shim = CursorShimScope.Create())
                await using (LocalServerScope server = await LocalServerScope.CreateAsync().ConfigureAwait(false))
                {
                    Captain captain = await CreateCaptainViaRestAsync(server.Client, "rest-update-same-model", "gpt-5.4-mini").ConfigureAwait(false);
                    if (File.Exists(shim.ArgsFile))
                        File.Delete(shim.ArgsFile);

                    HttpResponseMessage response = await server.Client.PutAsync(
                        "/api/v1/captains/" + captain.Id,
                        JsonHelper.ToJsonContent(new
                        {
                            Name = "rest-update-same-model-renamed",
                            Runtime = "Cursor",
                            Model = "gpt-5.4-mini"
                        })).ConfigureAwait(false);

                    Captain updated = await JsonHelper.DeserializeAsync<Captain>(response).ConfigureAwait(false);

                    AssertEqual(HttpStatusCode.OK, response.StatusCode);
                    AssertEqual("gpt-5.4-mini", updated.Model, "Model should remain unchanged");
                    AssertEqual("rest-update-same-model-renamed", updated.Name, "Name should update");
                    await Task.Delay(200).ConfigureAwait(false);
                    AssertFalse(File.Exists(shim.ArgsFile), "Unchanged model should not trigger validation");
                }
            });

            await RunTest("Captain REST update rejects changed invalid model and preserves stored captain", async () =>
            {
                using (CursorShimScope shim = CursorShimScope.Create())
                await using (LocalServerScope server = await LocalServerScope.CreateAsync().ConfigureAwait(false))
                {
                    Captain captain = await CreateCaptainViaRestAsync(server.Client, "rest-update-bad-model", "gpt-5.4-mini").ConfigureAwait(false);
                    if (File.Exists(shim.ArgsFile))
                        File.Delete(shim.ArgsFile);

                    HttpResponseMessage response = await server.Client.PutAsync(
                        "/api/v1/captains/" + captain.Id,
                        JsonHelper.ToJsonContent(new
                        {
                            Name = captain.Name,
                            Runtime = "Cursor",
                            Model = "bad-model"
                        })).ConfigureAwait(false);

                    ArmadaErrorResponse error = await JsonHelper.DeserializeAsync<ArmadaErrorResponse>(response).ConfigureAwait(false);
                    HttpResponseMessage getResponse = await server.Client.GetAsync("/api/v1/captains/" + captain.Id).ConfigureAwait(false);
                    Captain persisted = await JsonHelper.DeserializeAsync<Captain>(getResponse).ConfigureAwait(false);

                    AssertEqual(HttpStatusCode.BadRequest, response.StatusCode);
                    AssertContains("bad-model", error.Message ?? String.Empty, "Error should include the rejected model");
                    AssertEqual("gpt-5.4-mini", persisted.Model, "Failed update should not change the stored model");
                    string args = await WaitForRecordedArgsAsync(shim.ArgsFile, "bad-model").ConfigureAwait(false);
                    AssertContains("--model", args, "Changed model should trigger validation");
                }
            });

            await RunTest("MCP captain tools expose model schema and persist validated model", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                using (CursorShimScope shim = CursorShimScope.Create())
                {
                    Dictionary<string, RegisteredTool> tools = RegisterCaptainTools(testDb.Driver, CreateSettings());
                    JsonElement createSchema = ToJsonElement(tools["armada_create_captain"].InputSchema);
                    JsonElement updateSchema = ToJsonElement(tools["armada_update_captain"].InputSchema);

                    AssertTrue(createSchema.GetProperty("properties").TryGetProperty("model", out JsonElement createModel), "Create captain schema should include model");
                    AssertTrue(updateSchema.GetProperty("properties").TryGetProperty("model", out JsonElement updateModel), "Update captain schema should include model");
                    AssertContains("runtime selects its default", createModel.GetProperty("description").GetString() ?? String.Empty, "Create schema should describe model defaulting");
                    AssertContains("runtime selects its default", updateModel.GetProperty("description").GetString() ?? String.Empty, "Update schema should describe model defaulting");

                    object result = await tools["armada_create_captain"].Handler(ToJsonElement(new
                    {
                        name = "mcp-model-captain",
                        runtime = "Cursor",
                        model = "gpt-5.4-mini"
                    })).ConfigureAwait(false);

                    Captain captain = DeserializeResult<Captain>(result);
                    AssertEqual("gpt-5.4-mini", captain.Model, "Create tool should persist the requested model");
                    string args = await WaitForRecordedArgsAsync(shim.ArgsFile, "gpt-5.4-mini").ConfigureAwait(false);
                    AssertContains("--model", args, "Create tool should validate with model flag");
                }
            });

            await RunTest("MCP captain update preserves model when omitted and rejects invalid model changes", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                using (CursorShimScope shim = CursorShimScope.Create())
                {
                    Dictionary<string, RegisteredTool> tools = RegisterCaptainTools(testDb.Driver, CreateSettings());
                    Captain captain = new Captain("mcp-existing", AgentRuntimeEnum.Cursor)
                    {
                        Model = "gpt-5.4-mini"
                    };
                    captain = await testDb.Driver.Captains.CreateAsync(captain).ConfigureAwait(false);

                    if (File.Exists(shim.ArgsFile))
                        File.Delete(shim.ArgsFile);

                    object omitModelResult = await tools["armada_update_captain"].Handler(ToJsonElement(new
                    {
                        captainId = captain.Id,
                        name = "mcp-renamed",
                        runtime = "Cursor"
                    })).ConfigureAwait(false);

                    Captain unchanged = DeserializeResult<Captain>(omitModelResult);
                    AssertEqual("mcp-renamed", unchanged.Name, "Update without model should still update other fields");
                    AssertEqual("gpt-5.4-mini", unchanged.Model, "Omitted model should preserve the stored value");
                    await Task.Delay(200).ConfigureAwait(false);
                    AssertFalse(File.Exists(shim.ArgsFile), "Omitted model should not trigger validation");

                    object invalidModelResult = await tools["armada_update_captain"].Handler(ToJsonElement(new
                    {
                        captainId = captain.Id,
                        runtime = "Cursor",
                        model = "bad-model"
                    })).ConfigureAwait(false);

                    JsonElement error = ToJsonElement(invalidModelResult);
                    Captain persisted = await testDb.Driver.Captains.ReadAsync(captain.Id).ConfigureAwait(false)
                        ?? throw new InvalidOperationException("Captain should still exist after failed MCP update.");

                    AssertContains("bad-model", error.GetProperty("Error").GetString() ?? String.Empty, "Invalid model should return an error");
                    AssertEqual("gpt-5.4-mini", persisted.Model, "Failed MCP update should not change the stored model");
                    string args = await WaitForRecordedArgsAsync(shim.ArgsFile, "bad-model").ConfigureAwait(false);
                    AssertContains("--model", args, "Changed MCP model should trigger validation");
                }
            });

            await RunTest("MCP mission tool descriptions mention totalRuntimeMs", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    Dictionary<string, RegisteredTool> tools = RegisterMissionTools(testDb.Driver);
                    string[] missionTools =
                    {
                        "armada_mission_status",
                        "armada_create_mission",
                        "armada_update_mission",
                        "armada_cancel_mission",
                        "armada_restart_mission",
                        "armada_retry_landing",
                        "armada_transition_mission_status"
                    };

                    foreach (string toolName in missionTools)
                    {
                        AssertContains("totalRuntimeMs", tools[toolName].Description, toolName + " description should mention mission runtime exposure");
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

        private static Dictionary<string, RegisteredTool> RegisterCaptainTools(DatabaseDriver database, ArmadaSettings settings)
        {
            Dictionary<string, RegisteredTool> tools = new Dictionary<string, RegisteredTool>(StringComparer.Ordinal);
            McpCaptainTools.Register(
                (name, description, inputSchema, handler) => tools[name] = new RegisteredTool(description, inputSchema, handler),
                database,
                new StubAdmiralService(),
                settings);
            return tools;
        }

        private static Dictionary<string, RegisteredTool> RegisterMissionTools(DatabaseDriver database)
        {
            Dictionary<string, RegisteredTool> tools = new Dictionary<string, RegisteredTool>(StringComparer.Ordinal);
            McpMissionTools.Register(
                (name, description, inputSchema, handler) => tools[name] = new RegisteredTool(description, inputSchema, handler),
                database,
                new StubAdmiralService(),
                null,
                null,
                null);
            return tools;
        }

        private static JsonElement ToJsonElement(object value)
        {
            using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(value));
            return document.RootElement.Clone();
        }

        private static T DeserializeResult<T>(object value)
        {
            return JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))
                ?? throw new InvalidOperationException("Unable to deserialize tool result as " + typeof(T).Name + ".");
        }

        private static async Task<Captain> CreateCaptainViaRestAsync(HttpClient client, string name, string model)
        {
            HttpResponseMessage response = await client.PostAsync(
                "/api/v1/captains",
                JsonHelper.ToJsonContent(new
                {
                    Name = name,
                    Runtime = "Cursor",
                    Model = model
                })).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();
            return await JsonHelper.DeserializeAsync<Captain>(response).ConfigureAwait(false);
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

        private sealed class RegisteredTool
        {
            public string Description { get; }
            public object InputSchema { get; }
            public Func<JsonElement?, Task<object>> Handler { get; }

            public RegisteredTool(string description, object inputSchema, Func<JsonElement?, Task<object>> handler)
            {
                Description = description;
                InputSchema = inputSchema;
                Handler = handler;
            }
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

            public Task<Voyage> DispatchVoyageAsync(string title, string description, string vesselId, List<MissionDescription> missionDescriptions, string? pipelineId, CancellationToken token = default)
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

        private sealed class LocalServerScope : IAsyncDisposable
        {
            public HttpClient Client { get; }

            private readonly ArmadaServer _server;
            private readonly string _tempDirectory;

            private LocalServerScope(ArmadaServer server, HttpClient client, string tempDirectory)
            {
                _server = server;
                Client = client;
                _tempDirectory = tempDirectory;
            }

            public static async Task<LocalServerScope> CreateAsync()
            {
                string tempDirectory = Path.Combine(Path.GetTempPath(), "armada_lifecycle_server_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDirectory);

                ArmadaSettings settings = new ArmadaSettings
                {
                    DataDirectory = tempDirectory,
                    DatabasePath = Path.Combine(tempDirectory, "armada.db"),
                    Database = new DatabaseSettings
                    {
                        Type = DatabaseTypeEnum.Sqlite,
                        Filename = Path.Combine(tempDirectory, "armada.db")
                    },
                    LogDirectory = Path.Combine(tempDirectory, "logs"),
                    DocksDirectory = Path.Combine(tempDirectory, "docks"),
                    ReposDirectory = Path.Combine(tempDirectory, "repos"),
                    AdmiralPort = GetAvailablePort(),
                    McpPort = GetAvailablePort(),
                    WebSocketPort = GetAvailablePort(),
                    ApiKey = "test-key-" + Guid.NewGuid().ToString("N"),
                    HeartbeatIntervalSeconds = 300
                };
                settings.InitializeDirectories();

                ArmadaServer server = new ArmadaServer(CreateLogging(), settings, quiet: true);
                await server.StartAsync().ConfigureAwait(false);
                await Task.Delay(500).ConfigureAwait(false);

                HttpClient client = new HttpClient
                {
                    BaseAddress = new Uri("http://localhost:" + settings.AdmiralPort)
                };
                client.DefaultRequestHeaders.Add("X-Api-Key", settings.ApiKey);

                return new LocalServerScope(server, client, tempDirectory);
            }

            public async ValueTask DisposeAsync()
            {
                Client.Dispose();
                try { _server.Stop(); } catch { }
                await Task.Delay(200).ConfigureAwait(false);
                try { Directory.Delete(_tempDirectory, true); } catch { }
            }
        }

        private static int GetAvailablePort()
        {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
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

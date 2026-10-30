namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Settings;
    using Armada.Helm;
    using Armada.Helm.Commands;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Helm blocks when a child fills a redirected pipe that Helm does not read.
    /// An MCP client command waits for exit without reading either stream.
    /// Unix server start redirects the Admiral's streams and then polls health.
    /// The server build reads standard output to the end before it reads standard error.
    /// </summary>
    public sealed class HelmChildProcessSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.HelmChildProcess";
        private const string BuildMarker = "HELM-BUILD-STDERR-MARKER";
        private static readonly TimeSpan _Deadline = TimeSpan.FromSeconds(15);
        private static readonly string[] _ServerAssemblyNames = new string[]
        {
            "Armada.Server.dll",
            "Armada.Server.pdb",
            "Armada.Server",
            "Armada.Server.exe",
            "Armada.Server.runtimeconfig.json",
            "Armada.Server.deps.json"
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the Helm child-process cases.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            cases.Add(CaseAsync(
                "mcp_client_returns_after_stderr_then_stdout_exceeds_the_pipe",
                "Helm MCP client commands return when the client fills stderr and then stdout",
                TestTags.Negative,
                McpClientReturnsAfterPipeFillAsync));
            cases.Add(CaseAsync(
                "unix_server_start_reaches_health_after_output_exceeds_the_pipe",
                "Unix server start reaches health after the stand-in fills both streams",
                TestTags.Negative,
                UnixServerStartReachesHealthAsync));
            cases.Add(CaseAsync(
                "server_build_reports_stderr_when_stderr_exceeds_the_pipe",
                "Server build finishes and reports stderr when the child fills stderr",
                TestTags.Negative,
                ServerBuildReportsStderrAsync));
            return new TestSuiteDescriptor(SuiteId, "Helm child processes", cases);
        }

        #endregion

        #region Private-Methods

        private static async Task McpClientReturnsAfterPipeFillAsync()
        {
            string root = TestTemp.NewDirectory("helm-mcp-pipe");
            string pidFile = Path.Combine(root, "pid");
            string script = "echo $$ > '" + pidFile + "'\n"
                + "dd if=/dev/zero bs=1024 count=128 2>/dev/null | tr '\\0' 'e' >&2\n"
                + "dd if=/dev/zero bs=1024 count=128 2>/dev/null | tr '\\0' 'o'\n"
                + "exit 0\n";
            McpConfigHelper.ConfigTarget target = new McpConfigHelper.ConfigTarget(
                "Example Client",
                Path.Combine(root, "unused.json"),
                CliCommand: "/bin/sh",
                InstallArgs: new string[] { "-c", script });
            try
            {
                Task<McpConfigHelper.ApplyResult> run = McpConfigHelper.InstallTargetAsync(target);
                Task winner = await Task.WhenAny(run, Task.Delay(_Deadline)).ConfigureAwait(false);
                if (!Object.ReferenceEquals(winner, run))
                {
                    KillPidFile(pidFile);
                    AssertTrue(false, "The MCP client command did not return within 15 seconds while the child wrote more than a pipe buffer to stderr and then stdout.");
                }

                McpConfigHelper.ApplyResult result = await run.ConfigureAwait(false);
                AssertTrue(result.Changed, "A client command that exits 0 is reported as configured.");
            }
            finally
            {
                KillPidFile(pidFile);
            }
        }

        private static async Task UnixServerStartReachesHealthAsync()
        {
            string perl = FindPerl();
            AssertTrue(perl.Length > 0, "The Linux host must provide perl so the stand-in can serve health.");
            string root = TestTemp.NewDirectory("helm-server-pipe");
            string work = Path.Combine(root, "work");
            string data = Path.Combine(root, "data");
            string home = Path.Combine(root, "home");
            string bin = Path.Combine(root, "bin");
            Directory.CreateDirectory(work);
            Directory.CreateDirectory(data);
            Directory.CreateDirectory(home);
            Directory.CreateDirectory(bin);
            int port = TestPorts.Reserve(1)[0];
            string pidFile = Path.Combine(root, "pid");
            string marker = Path.Combine(root, "past-output");
            string server = Path.Combine(bin, "armada-server");
            File.WriteAllText(server, StandInServerScript(perl, port, pidFile, marker));
            MakeExecutable(server);
            ArmadaSettings settings = new ArmadaSettings();
            settings.AdmiralPort = port;
            settings.DataDirectory = data;
            await settings.SaveAsync(Path.Combine(data, "settings.json")).ConfigureAwait(false);
            PreparedHelm helm = PrepareHelm(root);
            try
            {
                HelmCapture capture = await RunServerStartAsync(helm, work, data, home, bin, false).ConfigureAwait(false);
                if (!capture.Returned)
                {
                    AssertTrue(false, "Server start did not return within 15 seconds. Output: " + Trim(capture.Output));
                }

                AssertEqual(0, capture.ExitCode, "Server start exits 0. Output: " + Trim(capture.Output));
                AssertContains("Admiral server is running", capture.Output, "Health polling finishes after the stand-in writes both streams.");
                AssertTrue(File.Exists(marker), "The stand-in serves health only after it gets past the pipe fill.");
                AssertTrue(PidIsAlive(pidFile), "A ready server keeps running after Helm returns.");
            }
            finally
            {
                KillPidFile(pidFile);
            }
        }

        private static async Task ServerBuildReportsStderrAsync()
        {
            string root = TestTemp.NewDirectory("helm-build-pipe");
            string work = Path.Combine(root, "work");
            string data = Path.Combine(root, "data");
            string home = Path.Combine(root, "home");
            string bin = Path.Combine(root, "bin");
            Directory.CreateDirectory(Path.Combine(work, "src", "Armada.Server"));
            Directory.CreateDirectory(data);
            Directory.CreateDirectory(home);
            Directory.CreateDirectory(bin);
            File.WriteAllText(Path.Combine(work, "src", "Armada.Server", "Armada.Server.csproj"), "<Project></Project>\n");
            string dotnet = Path.Combine(bin, "dotnet");
            // The marker is first so a console that shortens a long line still shows it. The fill comes
            // before stdout, so a parent that reads stdout to the end first blocks on the full stderr pipe.
            File.WriteAllText(dotnet, "#!/bin/sh\n"
                + "echo " + BuildMarker + " >&2\n"
                + "dd if=/dev/zero bs=1024 count=128 2>/dev/null | tr '\\0' 'e' >&2\n"
                + "echo stdout-done\n"
                + "exit 3\n");
            MakeExecutable(dotnet);
            int port = TestPorts.Reserve(1)[0];
            ArmadaSettings settings = new ArmadaSettings();
            settings.AdmiralPort = port;
            settings.DataDirectory = data;
            await settings.SaveAsync(Path.Combine(data, "settings.json")).ConfigureAwait(false);
            PreparedHelm helm = PrepareHelm(root);
            AssertTrue(helm.AppHost.Length > 0 && File.Exists(helm.AppHost), "The Helm apphost must be available so the build stand-in is reached through PATH.");
            HelmCapture capture = await RunServerStartAsync(helm, work, data, home, bin, true).ConfigureAwait(false);
            if (!capture.Returned)
            {
                AssertTrue(false, "The server build did not return within 15 seconds while the child filled stderr. Output: " + Trim(capture.Output));
            }

            AssertEqual(1, capture.ExitCode, "A failed build leaves no server to launch. Output: " + Trim(capture.Output));
            AssertContains("Server build failed", capture.Output, "Helm reports the build failure.");
            AssertContains(BuildMarker, capture.Output, "Helm reports the child's stderr.");
        }

        private static string StandInServerScript(string perl, int port, string pidFile, string marker)
        {
            return "#!/bin/sh\n"
                + "echo $$ > '" + pidFile + "'\n"
                + "dd if=/dev/zero bs=1024 count=128 2>/dev/null | tr '\\0' 'o'\n"
                + "dd if=/dev/zero bs=1024 count=128 2>/dev/null | tr '\\0' 'e' >&2\n"
                + "touch '" + marker + "'\n"
                + "exec '" + perl + "' -e '"
                + "use IO::Socket::INET; "
                + "my $port = shift; "
                + "my $s = IO::Socket::INET->new(LocalHost => \"127.0.0.1\", LocalPort => $port, Listen => 8, Reuse => 1) or die $!; "
                + "while (my $c = $s->accept()) { "
                + "while (defined($_ = $c->getline())) { last if $_ eq \"\\r\\n\"; } "
                + "print $c \"HTTP/1.1 200 OK\\r\\nContent-Length: 2\\r\\nConnection: close\\r\\n\\r\\nOK\"; "
                + "close $c; }' " + port.ToString() + "\n";
        }

        private static PreparedHelm PrepareHelm(string root)
        {
            string source = Path.GetDirectoryName(typeof(Program).Assembly.Location) ?? "";
            AssertTrue(source.Length > 0 && File.Exists(Path.Combine(source, "Armada.Helm.dll")), "The Helm assembly must be next to the test host.");
            string app = Path.Combine(root, "app");
            string probe = Path.Combine(root, "probe");
            CopyTree(source, app);
            Directory.CreateDirectory(probe);
            RelocateServerAssembly(app, probe);
            // Process.Start resolves a bare "dotnet" beside the running executable before PATH. A file of
            // that name in the application directory would hide the stand-in.
            string bundledDotnet = Path.Combine(app, "dotnet");
            if (File.Exists(bundledDotnet)) File.Delete(bundledDotnet);
            string appHost = CopyHelmAppHost(app);

            string runtimeConfig = Path.Combine(app, "Armada.Helm.runtimeconfig.json");
            AssertTrue(File.Exists(runtimeConfig), "Helm runtimeconfig.json must be copied with the assembly.");
            AddProbingPath(runtimeConfig, probe);
            string dotnet = ResolveDotnetHost();
            AssertTrue(File.Exists(dotnet), "The dotnet host must be an absolute executable.");
            PreparedHelm prepared = new PreparedHelm();
            prepared.DotnetHost = dotnet;
            prepared.HelmAssembly = Path.Combine(app, "Armada.Helm.dll");
            prepared.ProbeDirectory = probe;
            prepared.AppHost = appHost;
            return prepared;
        }

        private static void RelocateServerAssembly(string appDirectory, string probeDirectory)
        {
            // The host resolves a project asset outside the application directory at
            // <probe>/<library name>/<version>/<asset path>, or at the deps.json library path when that path is set.
            // server start selects Armada.Server.dll only when that file sits in the application directory.
            string depsPath = Path.Combine(appDirectory, "Armada.Helm.deps.json");
            AssertTrue(File.Exists(depsPath), "Helm deps.json must be copied with the assembly.");
            JsonNode? parsed = JsonNode.Parse(File.ReadAllText(depsPath));
            JsonObject root = parsed as JsonObject ?? new JsonObject();
            JsonObject libraries = root["libraries"] as JsonObject ?? new JsonObject();
            string libraryKey = "";
            foreach (KeyValuePair<string, JsonNode?> entry in libraries)
            {
                if (entry.Key.StartsWith("Armada.Server/", StringComparison.Ordinal))
                {
                    libraryKey = entry.Key;
                    break;
                }
            }

            AssertTrue(libraryKey.Length > 0, "Helm deps.json must name the Armada.Server project dependency.");
            int slash = libraryKey.IndexOf('/');
            string libraryName = libraryKey.Substring(0, slash);
            string libraryVersion = libraryKey.Substring(slash + 1);
            string libraryPath = libraryName + "/" + libraryVersion;
            JsonObject libraryObject = libraries[libraryKey] as JsonObject ?? new JsonObject();
            JsonNode? pathNode = libraryObject["path"];
            if (pathNode != null)
            {
                string recordedPath = pathNode.GetValue<string>();
                if (recordedPath.Length > 0) libraryPath = recordedPath;
            }

            JsonObject targets = root["targets"] as JsonObject ?? new JsonObject();
            List<string> assets = new List<string>();
            foreach (KeyValuePair<string, JsonNode?> target in targets)
            {
                JsonObject? targetObject = target.Value as JsonObject;
                if (targetObject == null) continue;
                JsonObject? libraryTarget = targetObject[libraryKey] as JsonObject;
                if (libraryTarget == null) continue;
                JsonObject? runtime = libraryTarget["runtime"] as JsonObject;
                if (runtime == null) continue;
                foreach (KeyValuePair<string, JsonNode?> asset in runtime)
                {
                    if (!assets.Contains(asset.Key)) assets.Add(asset.Key);
                }
            }

            AssertTrue(assets.Count > 0, "Helm deps.json must list an Armada.Server runtime asset.");
            foreach (string asset in assets)
            {
                string relativeAsset = asset.Replace('/', Path.DirectorySeparatorChar);
                string from = Path.Combine(appDirectory, Path.GetFileName(relativeAsset));
                AssertTrue(File.Exists(from), "The Armada.Server runtime asset must be copied with Helm.");
                string destination = Path.Combine(probeDirectory, libraryPath.Replace('/', Path.DirectorySeparatorChar), relativeAsset);
                string parentDirectory = Path.GetDirectoryName(destination) ?? "";
                AssertTrue(parentDirectory.Length > 0, "The probe path must have a directory.");
                Directory.CreateDirectory(parentDirectory);
                File.Move(from, destination);
                AssertTrue(File.Exists(destination), "The host probe must contain the Armada.Server runtime asset.");
            }

            string aside = Path.Combine(probeDirectory, "aside");
            foreach (string name in _ServerAssemblyNames)
            {
                string from = Path.Combine(appDirectory, name);
                if (!File.Exists(from)) continue;
                Directory.CreateDirectory(aside);
                File.Move(from, Path.Combine(aside, name));
            }

            AssertTrue(!File.Exists(Path.Combine(appDirectory, "Armada.Server.dll")), "Armada.Server.dll stays out of the CLI directory.");
            AssertTrue(!File.Exists(Path.Combine(appDirectory, "Armada.Server")), "The server apphost stays out of the CLI directory.");
        }

        private static void MakeExecutable(string path)
        {
            // Every case is skipped on Windows; the guard also tells the platform analyzer so.
            if (OperatingSystem.IsWindows()) return;
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        private static string CopyHelmAppHost(string appDirectory)
        {
            // dotnet exec makes the running executable the real host, and that host is itself named dotnet.
            // The apphost is not named dotnet, so resolution continues to the working directory and then PATH.
            string source = FindHelmAppHost();
            if (source.Length == 0) return "";
            string destination = Path.Combine(appDirectory, "Armada.Helm");
            File.Copy(source, destination, true);
            MakeExecutable(destination);
            return destination;
        }

        private static string FindHelmAppHost()
        {
            string location = typeof(HelmChildProcessSuite).Assembly.Location;
            string directory = Path.GetDirectoryName(location) ?? "";
            if (directory.Length == 0) return "";
            string beside = Path.Combine(directory, "Armada.Helm");
            if (File.Exists(beside)) return beside;

            DirectoryInfo tfmDirectory = new DirectoryInfo(directory);
            DirectoryInfo? configurationDirectory = tfmDirectory.Parent;
            DirectoryInfo? binDirectory = configurationDirectory == null ? null : configurationDirectory.Parent;
            DirectoryInfo? projectDirectory = binDirectory == null ? null : binDirectory.Parent;
            DirectoryInfo? srcDirectory = projectDirectory == null ? null : projectDirectory.Parent;
            if (srcDirectory == null || configurationDirectory == null) return "";

            string hostDirectory = Path.Combine(srcDirectory.FullName, "Armada.Helm", "bin", configurationDirectory.Name, tfmDirectory.Name);
            string direct = Path.Combine(hostDirectory, "Armada.Helm");
            if (File.Exists(direct)) return direct;
            if (!Directory.Exists(hostDirectory)) return "";
            foreach (string subDirectory in Directory.GetDirectories(hostDirectory))
            {
                string candidate = Path.Combine(subDirectory, "Armada.Helm");
                if (File.Exists(candidate)) return candidate;
            }

            return "";
        }

        private static async Task<HelmCapture> RunServerStartAsync(PreparedHelm helm, string work, string data, string home, string pathBin, bool useAppHost)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = useAppHost ? helm.AppHost : helm.DotnetHost,
                WorkingDirectory = work,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            if (!useAppHost)
            {
                startInfo.ArgumentList.Add("exec");
                startInfo.ArgumentList.Add("--additionalprobingpath");
                startInfo.ArgumentList.Add(helm.ProbeDirectory);
                startInfo.ArgumentList.Add(helm.HelmAssembly);
            }

            startInfo.ArgumentList.Add("server");
            startInfo.ArgumentList.Add("start");
            startInfo.Environment["ARMADA_DATA_DIR"] = data;
            startInfo.Environment["ARMADA_TUI_CREDENTIAL_STORE"] = "file";
            startInfo.Environment["ARMADA_TUI_PREFERENCES"] = Path.Combine(data, "tui.json");
            startInfo.Environment["ARMADA_TUI_CREDENTIALS"] = Path.Combine(data, "tui-credentials.json");
            startInfo.Environment["HOME"] = home;
            startInfo.Environment["PATH"] = pathBin + ":/usr/bin:/bin";
            startInfo.Environment.Remove("ARMADA_SERVER_URL");
            startInfo.Environment.Remove("ARMADA_URL");
            startInfo.Environment.Remove("ARMADA_TOKEN");

            HelmCapture capture = new HelmCapture();
            using (Process process = new Process { StartInfo = startInfo })
            {
                process.Start();
                Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                Task<string> errorTask = process.StandardError.ReadToEndAsync();
                Task exitTask = process.WaitForExitAsync();
                Task finished = Task.WhenAll(outputTask, errorTask, exitTask);
                Task winner = await Task.WhenAny(finished, Task.Delay(_Deadline)).ConfigureAwait(false);
                if (!Object.ReferenceEquals(winner, finished))
                {
                    try { process.Kill(entireProcessTree: true); } catch (Exception) { }
                    try { await finished.ConfigureAwait(false); } catch (Exception) { }
                    capture.Returned = false;
                    capture.Output = Safe(outputTask) + Safe(errorTask);
                    return capture;
                }

                await finished.ConfigureAwait(false);
                capture.Returned = true;
                capture.ExitCode = process.ExitCode;
                capture.Output = outputTask.Result + errorTask.Result;
                return capture;
            }
        }

        private static void AddProbingPath(string runtimeConfigPath, string probeDirectory)
        {
            JsonNode? parsed = JsonNode.Parse(File.ReadAllText(runtimeConfigPath));
            JsonObject root = parsed as JsonObject ?? new JsonObject();
            JsonObject runtimeOptions;
            if (root["runtimeOptions"] is JsonObject existing) runtimeOptions = existing;
            else
            {
                runtimeOptions = new JsonObject();
                root["runtimeOptions"] = runtimeOptions;
            }

            JsonArray paths;
            if (runtimeOptions["additionalProbingPaths"] is JsonArray existingPaths) paths = existingPaths;
            else
            {
                paths = new JsonArray();
                runtimeOptions["additionalProbingPaths"] = paths;
            }

            paths.Add(JsonValue.Create(probeDirectory));
            File.WriteAllText(runtimeConfigPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }

        private static void CopyTree(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (string file in Directory.GetFiles(source))
            {
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
            }

            foreach (string directory in Directory.GetDirectories(source))
            {
                string name = Path.GetFileName(directory);
                if (String.Equals(name, "refs", StringComparison.OrdinalIgnoreCase)) continue;
                CopyTree(directory, Path.Combine(destination, name));
            }
        }

        private static string ResolveDotnetHost()
        {
            string? hostPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
            if (!String.IsNullOrEmpty(hostPath) && File.Exists(hostPath)) return hostPath!;
            string? root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
            if (!String.IsNullOrEmpty(root))
            {
                string candidate = Path.Combine(root, "dotnet");
                if (File.Exists(candidate)) return candidate;
            }

            DirectoryInfo? dir = new DirectoryInfo(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory());
            for (int i = 0; i < 5 && dir != null; i++)
            {
                string candidate = Path.Combine(dir.FullName, "dotnet");
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }

            string? process = Environment.ProcessPath;
            if (!String.IsNullOrEmpty(process)
                && Path.GetFileName(process).StartsWith("dotnet", StringComparison.OrdinalIgnoreCase)
                && File.Exists(process))
            {
                return process;
            }

            return "dotnet";
        }

        private static string FindPerl()
        {
            foreach (string candidate in new string[] { "/usr/bin/perl", "/usr/local/bin/perl" })
            {
                if (File.Exists(candidate)) return candidate;
            }

            return "";
        }

        private static void KillPidFile(string pidFile)
        {
            if (!File.Exists(pidFile)) return;
            string pid = File.ReadAllText(pidFile).Trim();
            if (pid.Length == 0) return;
            foreach (char character in pid)
            {
                if (character < '0' || character > '9') return;
            }

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "/bin/sh",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add("kill -KILL " + pid + " 2>/dev/null; for child in $(ps -o pid= -ppid " + pid + " 2>/dev/null); do kill -KILL \"$child\" 2>/dev/null; done; true");
            using (Process process = Process.Start(startInfo)!)
            {
                process.StandardOutput.ReadToEnd();
                process.StandardError.ReadToEnd();
                process.WaitForExit();
            }
        }

        private static bool PidIsAlive(string pidFile)
        {
            if (!File.Exists(pidFile)) return false;
            string text = File.ReadAllText(pidFile).Trim();
            int pid;
            if (!Int32.TryParse(text, out pid)) return false;
            try
            {
                using (Process process = Process.GetProcessById(pid))
                {
                    return !process.HasExited;
                }
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private static string Safe(Task<string> task)
        {
            if (task.IsCompletedSuccessfully) return task.Result;
            return "";
        }

        private static string Trim(string text)
        {
            if (text.Length <= 1500) return text;
            return text.Substring(0, 1500);
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            bool skip = OperatingSystem.IsWindows();
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken cancellationToken) => body(),
                tags: new List<string> { tag, TestTags.Process },
                skip: skip,
                skipReason: skip ? "Requires a POSIX shell. The Linux gate runs this case." : "");
        }

        #endregion

        #region Nested-Types

        private sealed class PreparedHelm
        {
            public string DotnetHost { get; set; } = "";
            public string HelmAssembly { get; set; } = "";
            public string ProbeDirectory { get; set; } = "";
            public string AppHost { get; set; } = "";
        }

        private sealed class HelmCapture
        {
            public bool Returned { get; set; }
            public int ExitCode { get; set; }
            public string Output { get; set; } = "";
        }

        #endregion
    }
}

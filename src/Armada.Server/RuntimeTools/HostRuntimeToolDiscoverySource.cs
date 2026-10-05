namespace Armada.Server.RuntimeTools
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Runtimes.Mcp;
    using SyslogLogging;

    /// <summary>
    /// Production <see cref="IRuntimeToolDiscoverySource"/>: reads the host user's runtime config files, runs the
    /// runtime CLIs on this host, reads installed-package built-in inventories, and connects to configured MCP servers.
    /// </summary>
    public class HostRuntimeToolDiscoverySource : IRuntimeToolDiscoverySource
    {
        #region Private-Members

        private readonly LoggingModule _Logging;
        private readonly McpServerToolProbe _Probe;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="logging">Logging module.</param>
        public HostRuntimeToolDiscoverySource(LoggingModule logging)
        {
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Probe = new McpServerToolProbe(new HttpClient());
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public string GetUserProfileDirectory()
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        /// <inheritdoc />
        public async Task<string?> ReadConfigFileAsync(string path, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }

            return await File.ReadAllTextAsync(path, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<RuntimeCommandResult> RunCodexAsync(List<string> arguments, string? workingDirectory, TimeSpan timeout, CancellationToken token = default)
        {
            if (arguments == null) throw new ArgumentNullException(nameof(arguments));

            string command = ResolveCodexCommand();
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = command,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            if (!String.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory))
            {
                startInfo.WorkingDirectory = workingDirectory;
            }

            using Process process = new Process
            {
                StartInfo = startInfo
            };

            if (!process.Start())
            {
                throw new InvalidOperationException("Failed to start process: " + command);
            }

            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();

            using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeoutCts.CancelAfter(timeout);

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(true);
                    }
                }
                catch
                {
                }

                throw new TimeoutException(command + " timed out after " + timeout.TotalSeconds.ToString("0") + " seconds.");
            }

            return new RuntimeCommandResult
            {
                ExitCode = process.ExitCode,
                Stdout = (await stdoutTask.ConfigureAwait(false)).Trim(),
                Stderr = (await stderrTask.ConfigureAwait(false)).Trim()
            };
        }

        /// <inheritdoc />
        public RuntimeBuiltInToolInventory? ReadBuiltInToolInventory(AgentRuntimeEnum runtime)
        {
            switch (runtime)
            {
                case AgentRuntimeEnum.ClaudeCode:
                    return ClaudeBuiltInToolInventoryReader.Read();
                case AgentRuntimeEnum.Gemini:
                    return GeminiBuiltInToolInventoryReader.Read();
                default:
                    return null;
            }
        }

        /// <inheritdoc />
        public async Task<MuxProbeResult> ProbeMuxAsync(Captain captain, IHostCommandExecutor executor, string? workingDirectory, CancellationToken token = default)
        {
            if (captain == null) throw new ArgumentNullException(nameof(captain));
            if (executor == null) throw new ArgumentNullException(nameof(executor));

            MuxCliService muxCli = new MuxCliService(_Logging, executor);
            return await muxCli.ProbeAsync(captain, workingDirectory, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<McpRemoteTool>> ListServerToolsAsync(RuntimeMcpServerDefinition server, CancellationToken token = default)
        {
            return await _Probe.ListToolsAsync(server, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static string ResolveCodexCommand()
        {
            if (!OperatingSystem.IsWindows())
            {
                return "codex";
            }

            string candidate = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "npm",
                "codex.cmd");

            return File.Exists(candidate) ? candidate : "codex.cmd";
        }

        #endregion
    }
}

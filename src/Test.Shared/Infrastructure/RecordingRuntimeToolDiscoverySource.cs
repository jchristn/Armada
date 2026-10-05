namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Runtimes.Mcp;
    using Armada.Server.RuntimeTools;

    /// <summary>
    /// Test <see cref="IRuntimeToolDiscoverySource"/> that never touches the host: the profile directory is a fixed
    /// non-existent path, config files come only from <see cref="ConfigFiles"/>, the Codex CLI reports no servers, no
    /// built-in inventory exists, the Mux probe reports failure, and MCP servers answer only from <see cref="ServerTools"/>.
    /// Every call is recorded so tests can prove the server used this source.
    /// </summary>
    public sealed class RecordingRuntimeToolDiscoverySource : IRuntimeToolDiscoverySource
    {
        #region Public-Members

        /// <summary>
        /// Fake profile directory returned by <see cref="GetUserProfileDirectory"/>.
        /// </summary>
        public string ProfileDirectory { get; set; } = Path.Combine(Path.GetTempPath(), "armada-test-no-profile");

        /// <summary>
        /// Config file contents by absolute path; any other path reads as absent.
        /// </summary>
        public ConcurrentDictionary<string, string> ConfigFiles { get; } = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// Tools each MCP server (by configured name) lists; a server not present fails as unreachable.
        /// </summary>
        public ConcurrentDictionary<string, List<McpRemoteTool>> ServerTools { get; } = new ConcurrentDictionary<string, List<McpRemoteTool>>(StringComparer.Ordinal);

        /// <summary>
        /// Paths passed to <see cref="ReadConfigFileAsync"/>.
        /// </summary>
        public ConcurrentQueue<string> ConfigReads { get; } = new ConcurrentQueue<string>();

        /// <summary>
        /// Runtimes passed to <see cref="ReadBuiltInToolInventory"/>.
        /// </summary>
        public ConcurrentQueue<AgentRuntimeEnum> InventoryReads { get; } = new ConcurrentQueue<AgentRuntimeEnum>();

        /// <summary>
        /// Server names passed to <see cref="ListServerToolsAsync"/>.
        /// </summary>
        public ConcurrentQueue<string> ServerProbes { get; } = new ConcurrentQueue<string>();

        /// <summary>
        /// Server URLs passed to <see cref="ListServerToolsAsync"/> (empty string for a server without a URL).
        /// </summary>
        public ConcurrentQueue<string> ServerProbeUrls { get; } = new ConcurrentQueue<string>();

        /// <summary>
        /// Number of Codex CLI runs.
        /// </summary>
        public int CodexRuns
        {
            get { return _CodexRuns; }
        }

        /// <summary>
        /// Number of Mux probes.
        /// </summary>
        public int MuxProbes
        {
            get { return _MuxProbes; }
        }

        #endregion

        #region Private-Members

        private int _CodexRuns = 0;
        private int _MuxProbes = 0;

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public string GetUserProfileDirectory()
        {
            return ProfileDirectory;
        }

        /// <inheritdoc />
        public Task<string?> ReadConfigFileAsync(string path, CancellationToken token = default)
        {
            ConfigReads.Enqueue(path);
            string? contents = ConfigFiles.TryGetValue(path, out string? value) ? value : null;
            return Task.FromResult(contents);
        }

        /// <inheritdoc />
        public Task<RuntimeCommandResult> RunCodexAsync(List<string> arguments, string? workingDirectory, TimeSpan timeout, CancellationToken token = default)
        {
            Interlocked.Increment(ref _CodexRuns);
            return Task.FromResult(new RuntimeCommandResult { ExitCode = 0, Stdout = "[]" });
        }

        /// <inheritdoc />
        public RuntimeBuiltInToolInventory? ReadBuiltInToolInventory(AgentRuntimeEnum runtime)
        {
            InventoryReads.Enqueue(runtime);
            return null;
        }

        /// <inheritdoc />
        public Task<MuxProbeResult> ProbeMuxAsync(Captain captain, IHostCommandExecutor executor, string? workingDirectory, CancellationToken token = default)
        {
            Interlocked.Increment(ref _MuxProbes);
            return Task.FromResult(new MuxProbeResult { Success = false, ErrorCode = "test_fake" });
        }

        /// <inheritdoc />
        public Task<List<McpRemoteTool>> ListServerToolsAsync(RuntimeMcpServerDefinition server, CancellationToken token = default)
        {
            ServerProbes.Enqueue(server.Name);
            ServerProbeUrls.Enqueue(server.Url ?? String.Empty);
            if (ServerTools.TryGetValue(server.Name, out List<McpRemoteTool>? tools))
            {
                return Task.FromResult(new List<McpRemoteTool>(tools));
            }

            throw new McpClientException("Fake MCP server " + server.Name + " is not reachable.", 503, null);
        }

        #endregion
    }
}

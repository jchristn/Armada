namespace Armada.Server.RuntimeTools
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Runtimes.Mcp;

    /// <summary>
    /// Every host touch point the captain runtime tool catalog uses: the host user's profile directory, runtime config
    /// files (Claude Code, Gemini CLI, Cursor, Mux), runtime CLIs (codex, mux), installed-package built-in tool
    /// inventories, and the configured MCP servers themselves. The production implementation is
    /// <see cref="HostRuntimeToolDiscoverySource"/>; tests substitute a fake so they never read or launch anything that
    /// belongs to the host user.
    /// </summary>
    public interface IRuntimeToolDiscoverySource
    {
        /// <summary>
        /// The host user's profile directory, under which runtime config files live.
        /// </summary>
        /// <returns>Absolute directory path.</returns>
        string GetUserProfileDirectory();

        /// <summary>
        /// Read a runtime config file.
        /// </summary>
        /// <param name="path">Absolute file path.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The file contents, or null when the file does not exist.</returns>
        Task<string?> ReadConfigFileAsync(string path, CancellationToken token = default);

        /// <summary>
        /// Run the Codex CLI with the given arguments and capture its output.
        /// </summary>
        /// <param name="arguments">Arguments (for example "mcp", "list", "--json").</param>
        /// <param name="workingDirectory">Working directory, or null.</param>
        /// <param name="timeout">Time allowed before the process is killed.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Exit code and output.</returns>
        /// <exception cref="TimeoutException">Thrown when the process does not exit within the timeout.</exception>
        Task<RuntimeCommandResult> RunCodexAsync(List<string> arguments, string? workingDirectory, TimeSpan timeout, CancellationToken token = default);

        /// <summary>
        /// Read the best-effort, display-only built-in tool inventory for a runtime.
        /// </summary>
        /// <param name="runtime">Runtime.</param>
        /// <returns>The inventory, or null when none is available.</returns>
        RuntimeBuiltInToolInventory? ReadBuiltInToolInventory(AgentRuntimeEnum runtime);

        /// <summary>
        /// Run the Mux model-endpoint probe for a captain through the given executor.
        /// </summary>
        /// <param name="captain">Mux captain.</param>
        /// <param name="executor">Executor that targets the host where mux runs.</param>
        /// <param name="workingDirectory">Working directory, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The probe result.</returns>
        Task<MuxProbeResult> ProbeMuxAsync(Captain captain, IHostCommandExecutor executor, string? workingDirectory, CancellationToken token = default);

        /// <summary>
        /// Connect to a configured MCP server and list its tools (all pages).
        /// </summary>
        /// <param name="server">Server definition.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Tools the server advertised.</returns>
        /// <exception cref="McpClientException">Thrown on an HTTP or JSON-RPC failure (see its typed codes).</exception>
        Task<List<McpRemoteTool>> ListServerToolsAsync(RuntimeMcpServerDefinition server, CancellationToken token = default);
    }
}

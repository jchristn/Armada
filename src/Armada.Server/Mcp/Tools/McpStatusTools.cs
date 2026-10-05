namespace Armada.Server.Mcp.Tools
{
    using System;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// Registers MCP tools for status and server lifecycle operations.
    /// </summary>
    public static class McpStatusTools
    {
        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        /// <summary>
        /// Registers status and server lifecycle MCP tools with the server.
        /// </summary>
        /// <param name="register">Delegate to register each tool.</param>
        /// <param name="admiral">Admiral service for status retrieval.</param>
        /// <param name="onStop">Optional callback invoked when the server stop tool is triggered.</param>
        /// <param name="stopServerUnavailableMessage">When <paramref name="onStop"/> is null and this is set, stop_server is
        /// registered anyway and answers a typed Unavailable error with this message.</param>
        public static void Register(RegisterToolDelegate register, IAdmiralService admiral, Action? onStop, string? stopServerUnavailableMessage = null)
        {
            register(
                "status",
                "Get aggregate status of active work in Armada: captain counts by state, mission counts by status, active voyages with progress, and recent signals. A global administrator sees every tenant; any other caller sees only its own tenant.",
                new { type = "object", properties = new { } },
                async (args) =>
                {
                    ArmadaStatus status = await admiral.GetStatusAsync(McpToolHelpers.ResolveCallerContext()).ConfigureAwait(false);
                    return (object)status;
                });

            if (onStop != null)
            {
                register(
                    "stop_server",
                    "Initiate a graceful shutdown of the Admiral server",
                    new { type = "object", properties = new { } },
                    (args) =>
                    {
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(500).ConfigureAwait(false);
                            onStop();
                        });
                        return Task.FromResult((object)new { Status = "shutting_down" });
                    });
            }
            else if (!String.IsNullOrEmpty(stopServerUnavailableMessage))
            {
                register(
                    "stop_server",
                    "Initiate a graceful shutdown of the Admiral server",
                    new { type = "object", properties = new { } },
                    (args) => Task.FromResult((object)McpToolError.Unavailable(stopServerUnavailableMessage)));
            }
        }
    }
}

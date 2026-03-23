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
        public static void Register(RegisterToolDelegate register, IAdmiralService admiral, Action? onStop)
        {
            register(
                "armada_status",
                "Get aggregate status of all active work in Armada",
                new { type = "object", properties = new { } },
                async (args) =>
                {
                    ArmadaStatus status = await admiral.GetStatusAsync().ConfigureAwait(false);
                    return (object)status;
                });

            if (onStop != null)
            {
                register(
                    "armada_stop_server",
                    "Initiate a graceful shutdown of the Admiral server",
                    new { type = "object", properties = new { } },
                    async (args) =>
                    {
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(500).ConfigureAwait(false);
                            onStop();
                        });
                        return (object)new { Status = "shutting_down" };
                    });
            }
        }
    }
}

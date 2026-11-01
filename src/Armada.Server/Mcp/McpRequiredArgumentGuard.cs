namespace Armada.Server.Mcp
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Voltaic.Core;
    using Voltaic.Mcp;

    /// <summary>
    /// Answers a <c>tools/call</c> that omits a declared required argument of an opted-in tool with the JSON-RPC
    /// invalid-params protocol error (<c>-32602</c>) instead of Voltaic's default <c>isError</c> tool result.
    /// Every other call, including calls to tools that did not opt in, runs through Voltaic's original
    /// <c>tools/call</c> dispatcher unchanged (schema validation, rate limits, tool context, result shaping).
    /// </summary>
    /// <remarks>
    /// Voltaic 2.4.0 validates input schemas before the tool handler runs and exposes no policy hook for the error
    /// form, but <see cref="McpHttpServer.RegisterMethod(string, Func{RpcParameters, CancellationToken, Task{object}})"/>
    /// replaces a protocol method. The original dispatcher is captured from the server's method table first, so the
    /// override only adds the pre-check. When the method table cannot be read (a future dependency changes its shape)
    /// nothing is replaced and <see cref="Install"/> returns false; the regression suite then fails visibly.
    /// </remarks>
    public static class McpRequiredArgumentGuard
    {
        #region Public-Members

        /// <summary>The MCP method this guard wraps.</summary>
        public const string ToolsCallMethod = "tools/call";

        #endregion

        #region Private-Members

        private static readonly JsonSerializerOptions _Options = new JsonSerializerOptions { PropertyNameCaseInsensitive = false };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Install the guard on <paramref name="server"/> for the tools in <paramref name="requiredByTool"/>.
        /// </summary>
        /// <param name="server">MCP HTTP server; call before it starts accepting requests.</param>
        /// <param name="requiredByTool">Tool name to the argument names that must be present.</param>
        /// <returns>True when the guard was installed; false when Voltaic's original dispatcher could not be captured.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="server"/> or <paramref name="requiredByTool"/> is null.</exception>
        public static bool Install(McpHttpServer server, IReadOnlyDictionary<string, IReadOnlyList<string>> requiredByTool)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));
            if (requiredByTool == null) throw new ArgumentNullException(nameof(requiredByTool));

            Func<RpcParameters, CancellationToken, Task<object>>? original = CaptureOriginal(server);
            if (original == null) return false;

            Dictionary<string, IReadOnlyList<string>> required = new Dictionary<string, IReadOnlyList<string>>(requiredByTool, StringComparer.Ordinal);
            server.RegisterMethod(ToolsCallMethod, (RpcParameters? parameters, CancellationToken token) =>
            {
                string? missing = FindMissing(parameters, required, out string toolName);
                if (missing != null)
                {
                    throw McpProtocolException.InvalidParams(
                        "Tool '" + toolName + "' arguments is missing required property '" + missing + "'.",
                        new McpMissingArgumentData { Tool = toolName, Argument = missing });
                }

                return original(parameters!, token);
            });
            return true;
        }

        /// <summary>
        /// Return the first declared required argument that <paramref name="parameters"/> omits for an opted-in tool,
        /// or null when the call should run through the original dispatcher.
        /// </summary>
        /// <param name="parameters">The raw <c>tools/call</c> params.</param>
        /// <param name="requiredByTool">Tool name to required argument names.</param>
        /// <param name="toolName">The called tool's name, or empty when it could not be read.</param>
        /// <returns>The missing argument name, or null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="requiredByTool"/> is null.</exception>
        public static string? FindMissing(RpcParameters? parameters, IReadOnlyDictionary<string, IReadOnlyList<string>> requiredByTool, out string toolName)
        {
            if (requiredByTool == null) throw new ArgumentNullException(nameof(requiredByTool));
            toolName = String.Empty;
            if (parameters == null || !parameters.HasValue || String.IsNullOrWhiteSpace(parameters.RawJson)) return null;

            McpToolCallEnvelope? envelope;
            try
            {
                envelope = JsonSerializer.Deserialize<McpToolCallEnvelope>(parameters.RawJson, _Options);
            }
            catch (JsonException)
            {
                // A non-object arguments value (or malformed params) is left to Voltaic's own validation.
                return null;
            }

            if (envelope == null || String.IsNullOrEmpty(envelope.Name)) return null;
            toolName = envelope.Name;
            if (!requiredByTool.TryGetValue(envelope.Name, out IReadOnlyList<string>? names) || names == null) return null;

            foreach (string name in names)
            {
                if (envelope.Arguments == null || !envelope.Arguments.ContainsKey(name)) return name;
            }

            return null;
        }

        #endregion

        #region Private-Methods

        private static Func<RpcParameters, CancellationToken, Task<object>>? CaptureOriginal(McpHttpServer server)
        {
            FieldInfo? field = typeof(McpHttpServer).GetField("_Methods", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) return null;
            if (field.GetValue(server) is not IDictionary methods) return null;
            if (!methods.Contains(ToolsCallMethod)) return null;
            return methods[ToolsCallMethod] as Func<RpcParameters, CancellationToken, Task<object>>;
        }

        #endregion
    }
}

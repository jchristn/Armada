namespace Armada.Runtimes
{
    using System;

    /// <summary>
    /// A tool-activity event from the in-process (ApiEndpoint) runtime, delivered on its typed
    /// <see cref="ApiAgentRuntime.OnToolEvent"/> channel (never in-band on stdout, so model text cannot spoof it).
    /// </summary>
    public class ApiRuntimeToolEvent
    {
        #region Public-Members

        /// <summary>
        /// Phase of the call.
        /// </summary>
        public ApiRuntimeToolPhaseEnum Phase { get; set; } = ApiRuntimeToolPhaseEnum.Started;

        /// <summary>
        /// Tool call identifier assigned by the model, or null.
        /// </summary>
        public string? Id { get; set; } = null;

        /// <summary>
        /// Tool name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Arguments as JSON (truncated), set on <see cref="ApiRuntimeToolPhaseEnum.Started"/>.
        /// </summary>
        public string? Arguments { get; set; } = null;

        /// <summary>
        /// Whether the call succeeded, set on <see cref="ApiRuntimeToolPhaseEnum.Completed"/>. For an MCP tool this is
        /// the negation of the server's <c>isError</c> flag.
        /// </summary>
        public bool? Ok { get; set; } = null;

        /// <summary>
        /// Elapsed milliseconds, set on <see cref="ApiRuntimeToolPhaseEnum.Completed"/>.
        /// </summary>
        public double? ElapsedMs { get; set; } = null;

        /// <summary>
        /// Result text (truncated), set on <see cref="ApiRuntimeToolPhaseEnum.Completed"/>.
        /// </summary>
        public string? Result { get; set; } = null;

        /// <summary>
        /// True when the call was refused for lack of permission (the CLI tool permission policy), or null.
        /// </summary>
        public bool? PermissionDenied { get; set; } = null;

        #endregion
    }
}

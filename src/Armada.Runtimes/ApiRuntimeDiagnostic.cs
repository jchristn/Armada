namespace Armada.Runtimes
{
    using System;

    /// <summary>
    /// A diagnostic from the in-process (ApiEndpoint) runtime, delivered on its typed
    /// <see cref="ApiAgentRuntime.OnDiagnostic"/> channel and in the readable output log, but never on the stdout
    /// (reply text) channel.
    /// </summary>
    public class ApiRuntimeDiagnostic
    {
        #region Public-Members

        /// <summary>
        /// Kind of diagnostic.
        /// </summary>
        public ApiRuntimeDiagnosticKindEnum Kind { get; set; } = ApiRuntimeDiagnosticKindEnum.Warning;

        /// <summary>
        /// Human-readable message (also written to the output log).
        /// </summary>
        public string Message { get; set; } = String.Empty;

        #endregion
    }
}

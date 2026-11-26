namespace Armada.Runtimes
{
    using System;
    using Armada.Core.Models;

    /// <summary>
    /// Decision from <see cref="ApiTerminalPolicy"/> for a response that requested no tool calls.
    /// </summary>
    public sealed class ApiTerminalDecision
    {
        /// <summary>
        /// What the turn loop does next.
        /// </summary>
        public ApiTerminalActionEnum Action { get; set; } = ApiTerminalActionEnum.Complete;

        /// <summary>
        /// Visible text of the response (complete think blocks removed); the run's final message when it ends here.
        /// </summary>
        public string FinalText { get; set; } = String.Empty;

        /// <summary>
        /// User turn appended before the next turn (Continue and ForceSubmit), or null.
        /// </summary>
        public string? FollowUp { get; set; } = null;

        /// <summary>
        /// Warning diagnostic for Continue and ForceSubmit, or null.
        /// </summary>
        public string? Warning { get; set; } = null;

        /// <summary>
        /// Error raised for Fail, or null.
        /// </summary>
        public RuntimeProviderError? Failure { get; set; } = null;

        /// <summary>
        /// Error diagnostic emitted after the error for Fail, or null.
        /// </summary>
        public string? ErrorDiagnostic { get; set; } = null;
    }
}

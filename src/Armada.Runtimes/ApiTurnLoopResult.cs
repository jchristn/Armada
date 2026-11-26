namespace Armada.Runtimes
{
    using System;

    /// <summary>
    /// Outcome of <see cref="ApiTurnLoop.RunAsync"/>.
    /// </summary>
    public sealed class ApiTurnLoopResult
    {
        /// <summary>
        /// 0 when the run finished, 1 when it failed (the error was already raised through the callbacks).
        /// </summary>
        public int ExitCode { get; set; } = 0;

        /// <summary>
        /// Text written as the run's final message.
        /// </summary>
        public string FinalText { get; set; } = String.Empty;

        /// <summary>
        /// Model turns sent.
        /// </summary>
        public int Turns { get; set; } = 0;
    }
}

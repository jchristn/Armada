namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Concurrent;
    using System.Threading.Tasks;

    /// <summary>
    /// Script for <see cref="StubCaptainRuntime"/>: what the stub captain does on a thread-scoped turn (Ask Armada) and on
    /// a mission (by default it writes one file in the dock and commits it, so the mission produces work and lands).
    /// Shared by every runtime instance the server creates, and records what happened for assertions.
    /// </summary>
    public sealed class StubCaptainBehavior
    {
        #region Public-Members

        /// <summary>
        /// Thread-scoped turn script returning the reply text. Default replies "Done." without calling tools.
        /// </summary>
        public Func<StubCaptainTurn, Task<string>> OnTurn { get; set; } = turn => Task.FromResult("Done.");

        /// <summary>
        /// When false, a mission exits with code 1 without committing (a failed mission). Default true.
        /// </summary>
        public bool MissionsSucceed { get; set; } = true;

        /// <summary>
        /// Prompts of every thread-scoped turn, in order.
        /// </summary>
        public ConcurrentQueue<string> TurnPrompts { get; } = new ConcurrentQueue<string>();

        /// <summary>
        /// Working directories of every mission launch, in order.
        /// </summary>
        public ConcurrentQueue<string> MissionDirectories { get; } = new ConcurrentQueue<string>();

        /// <summary>
        /// Errors raised while running a turn or a mission (empty when everything ran).
        /// </summary>
        public ConcurrentQueue<string> Errors { get; } = new ConcurrentQueue<string>();

        #endregion
    }
}

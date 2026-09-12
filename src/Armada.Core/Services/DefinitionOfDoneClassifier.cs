namespace Armada.Core.Services
{
    using Armada.Core.Enums;

    /// <summary>
    /// Pure classifier for a single Definition-of-Done phase (build or test). Side-effect free so it
    /// unit-tests without launching a process.
    /// </summary>
    public static class DefinitionOfDoneClassifier
    {
        #region Public-Methods

        /// <summary>
        /// Classify the outcome of one phase. A phase that never started is Infra; one that was killed for
        /// exceeding its budget is Timeout; a non-zero exit is Compile for the build phase and TestFail for
        /// the test phase; a zero exit is Pass.
        /// </summary>
        /// <param name="started">Whether the command process started at all.</param>
        /// <param name="timedOut">Whether the command was killed for exceeding its timeout.</param>
        /// <param name="exitCode">Process exit code (ignored when not started or timed out).</param>
        /// <param name="isBuildPhase">True for the build phase, false for the test phase.</param>
        /// <returns>The classified outcome for the phase.</returns>
        public static DefinitionOfDoneOutcomeEnum ClassifyPhase(bool started, bool timedOut, int exitCode, bool isBuildPhase)
        {
            if (!started) return DefinitionOfDoneOutcomeEnum.Infra;
            if (timedOut) return DefinitionOfDoneOutcomeEnum.Timeout;
            if (exitCode != 0) return isBuildPhase ? DefinitionOfDoneOutcomeEnum.Compile : DefinitionOfDoneOutcomeEnum.TestFail;
            return DefinitionOfDoneOutcomeEnum.Pass;
        }

        #endregion
    }
}

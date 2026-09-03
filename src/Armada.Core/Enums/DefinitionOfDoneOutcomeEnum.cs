namespace Armada.Core.Enums
{
    /// <summary>
    /// Outcome of the in-dock Definition-of-Done gate: the build + unit-test run performed inside a
    /// mission's own checkout before acceptance.
    /// </summary>
    public enum DefinitionOfDoneOutcomeEnum
    {
        /// <summary>
        /// Build and unit tests both succeeded (or a phase was not configured). Acceptance may proceed.
        /// </summary>
        Pass,

        /// <summary>
        /// The build command exited non-zero: the change does not compile.
        /// </summary>
        Compile,

        /// <summary>
        /// The build succeeded but the unit-test command exited non-zero.
        /// </summary>
        TestFail,

        /// <summary>
        /// A phase exceeded its time budget and was killed.
        /// </summary>
        Timeout,

        /// <summary>
        /// A phase could not be executed at all (the command failed to start, e.g. a missing toolchain or
        /// a vanished worktree). This is an infrastructure fault, not a verdict on the change.
        /// </summary>
        Infra
    }
}

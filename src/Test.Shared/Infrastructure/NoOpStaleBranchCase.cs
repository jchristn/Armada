namespace Test.Shared.Infrastructure
{
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// One completion scenario for the no-op suite: a dock on a branch that already carries earlier work, with optional
    /// changes after the dock start commit.
    /// </summary>
    internal sealed class NoOpStaleBranchCase
    {
        /// <summary>Whether the dock start commit is recorded. Default true.</summary>
        public bool RecordStart { get; set; } = true;

        /// <summary>Whether the worktree is deleted before completion, so Git cannot answer. Default false.</summary>
        public bool RemoveWorktree { get; set; }

        /// <summary>Whether a commit is made after the dock start commit. Default false.</summary>
        public bool CommitAfterStart { get; set; }

        /// <summary>Whether a tracked file is edited after the dock start commit. Default false.</summary>
        public bool EditAfterStart { get; set; }

        /// <summary>Whether an untracked file is added after the dock start commit. Default false.</summary>
        public bool UntrackedAfterStart { get; set; }

        /// <summary>
        /// Whether the captain runs qwen3.8 and the Worker persona's escalation ladder goes on to an idle gpt-6.1 captain
        /// (RS-5.6). Default false.
        /// </summary>
        public bool EscalationLadder { get; set; }

        /// <summary>Whether completion captures the diff against the default branch. Default true.</summary>
        public bool CaptureBranchDiff { get; set; } = true;

        /// <summary>Diff stored on the mission before completion, or null.</summary>
        public string? SeededDiff { get; set; }

        /// <summary>The captain's handoff output. Default is the completion line followed by a short note.</summary>
        public string AgentOutput { get; set; } = NoOpCompletionDetector.CompletionMarker + "\nDone.";

        /// <summary>Run time in milliseconds. Default 1000, which is fast enough for the no-op check.</summary>
        public long RuntimeMs { get; set; } = 1000;

        /// <summary>Mission mode. Default Implementation.</summary>
        public MissionModeEnum Mode { get; set; } = MissionModeEnum.Implementation;
    }
}

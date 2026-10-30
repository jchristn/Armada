namespace Armada.Core.BlackBox
{
    /// <summary>
    /// Stable names of black box objects (docs/BLACKBOX_SCHEMA.md lists each with its content type).
    /// </summary>
    public static class BlackBoxObjectNames
    {
        /// <summary>The record (application/json).</summary>
        public const string Record = "record.json";

        /// <summary>The FIX THIS brief (text/markdown).</summary>
        public const string Brief = "brief.md";

        /// <summary>Tail of the captain's output (text/plain).</summary>
        public const string AgentOutput = "evidence/agent-output.txt";

        /// <summary>Tail of the mission log (text/plain).</summary>
        public const string MissionLog = "evidence/mission.log";

        /// <summary>The mission's diff (text/x-diff).</summary>
        public const string Diff = "evidence/diff.patch";

        /// <summary>Diff stat (text/plain).</summary>
        public const string DiffStat = "evidence/diffstat.txt";

        /// <summary>Merge-queue entry test output (text/plain).</summary>
        public const string MergeEntry = "evidence/merge-entry.txt";

        /// <summary>git status of the worktree or checkout (text/plain).</summary>
        public const string GitStatus = "evidence/git-status.txt";

        /// <summary>Target commits since the mission started (text/plain).</summary>
        public const string TargetCommits = "evidence/target-commits.txt";

        /// <summary>Relevant events (application/json).</summary>
        public const string Events = "evidence/events.json";

        /// <summary>
        /// Handoff and landing trail (record.handoff as JSON): ordered steps, automatic recoveries, dock transitions, target movement.
        /// </summary>
        public const string HandoffTimeline = "evidence/handoff-timeline.json";

        /// <summary>Download manifest, only inside a .zip download (application/json).</summary>
        public const string Manifest = "manifest.json";

        /// <summary>Prefix of check output objects: evidence/check-{n}.txt (text/plain).</summary>
        public const string CheckPrefix = "evidence/check-";

        /// <summary>Prefix of verification output objects: evidence/verification-{n}.txt (text/plain).</summary>
        public const string VerificationPrefix = "evidence/verification-";

        /// <summary>Content type of JSON objects.</summary>
        public const string JsonContentType = "application/json";

        /// <summary>Content type of Markdown objects.</summary>
        public const string MarkdownContentType = "text/markdown";

        /// <summary>Content type of text objects.</summary>
        public const string TextContentType = "text/plain";

        /// <summary>Content type of diffs.</summary>
        public const string DiffContentType = "text/x-diff";
    }
}

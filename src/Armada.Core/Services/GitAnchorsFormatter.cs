namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;

    /// <summary>
    /// Renders the resolved git anchors for a mission into a "Starting point" brief section, so a captain
    /// reads its start commit, target branch, and working branch from the instructions instead of burning
    /// opening turns deriving them. Pure string construction so it unit tests without a repository; the
    /// caller resolves the values from the git layer once at brief-assembly time.
    /// </summary>
    public static class GitAnchorsFormatter
    {
        #region Public-Methods

        /// <summary>
        /// Render the anchors section. Returns an empty string when there is nothing worth stating (no
        /// start commit and no branches), so the caller can append unconditionally.
        /// </summary>
        /// <param name="startCommit">The commit the working branch starts from (short or full hash), or null.</param>
        /// <param name="targetBranch">The branch the change targets (e.g. the default branch), or null.</param>
        /// <param name="workingBranch">The mission's working branch name, or null.</param>
        /// <param name="recentPathCommits">Optional recent commits per named path (path -> summary lines).</param>
        /// <returns>A markdown section, or empty string when there is nothing to state.</returns>
        public static string Render(
            string? startCommit,
            string? targetBranch,
            string? workingBranch,
            IReadOnlyList<string>? recentPathCommits = null,
            IReadOnlyList<string>? subjectTermsPresent = null)
        {
            bool hasCommit = !String.IsNullOrWhiteSpace(startCommit);
            bool hasTarget = !String.IsNullOrWhiteSpace(targetBranch);
            bool hasWorking = !String.IsNullOrWhiteSpace(workingBranch);
            bool hasRecent = recentPathCommits != null && recentPathCommits.Count > 0;
            bool hasTerms = subjectTermsPresent != null && subjectTermsPresent.Count > 0;

            if (!hasCommit && !hasTarget && !hasWorking && !hasRecent && !hasTerms) return String.Empty;

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("## Starting Point");
            builder.AppendLine();
            builder.AppendLine("These anchors are resolved for you; do not spend turns rediscovering them.");
            builder.AppendLine();
            if (hasWorking) builder.AppendLine("- Working branch: `" + workingBranch!.Trim() + "`");
            if (hasCommit) builder.AppendLine("- Start commit: `" + startCommit!.Trim() + "`");
            if (hasTarget) builder.AppendLine("- Target branch: `" + targetBranch!.Trim() + "`");
            if (hasRecent)
            {
                builder.AppendLine("- Recent commits on relevant paths:");
                foreach (string entry in recentPathCommits!)
                {
                    if (String.IsNullOrWhiteSpace(entry)) continue;
                    builder.AppendLine("  - " + entry.Trim());
                }
            }
            if (hasTerms)
            {
                builder.AppendLine("- Subject terms already present in the tree: " +
                    String.Join(", ", subjectTermsPresent!.Where(t => !String.IsNullOrWhiteSpace(t)).Select(t => "`" + t.Trim() + "`")));
            }
            builder.AppendLine();
            return builder.ToString();
        }

        #endregion
    }
}

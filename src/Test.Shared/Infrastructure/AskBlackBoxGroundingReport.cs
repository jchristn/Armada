namespace Test.Shared.Infrastructure
{
    using System.Collections.Generic;

    /// <summary>
    /// Result of <see cref="AskBlackBoxGrounding.Check"/>.
    /// </summary>
    public sealed class AskBlackBoxGroundingReport
    {
        /// <summary>
        /// Answer headings that are missing.
        /// </summary>
        public List<string> MissingHeadings { get; } = new List<string>();

        /// <summary>
        /// Whether the opinion is labeled as opinion.
        /// </summary>
        public bool OpinionLabeled { get; set; } = false;

        /// <summary>
        /// Whether the changes are split between what the user can change and what Armada should change.
        /// </summary>
        public bool ChangesSplit { get; set; } = false;

        /// <summary>
        /// Files named in the answer that the entry does not hold.
        /// </summary>
        public List<string> UnknownFiles { get; } = new List<string>();

        /// <summary>
        /// Commits named in the answer that the entry does not hold.
        /// </summary>
        public List<string> UnknownCommits { get; } = new List<string>();

        /// <summary>
        /// Missions named in the answer that the entry does not hold.
        /// </summary>
        public List<string> UnknownMissions { get; } = new List<string>();

        /// <summary>
        /// Whether the answer has the required shape.
        /// </summary>
        public bool Shaped => MissingHeadings.Count == 0 && OpinionLabeled && ChangesSplit;

        /// <summary>
        /// Whether every file, commit and mission the answer names is in the entry.
        /// </summary>
        public bool Grounded => UnknownFiles.Count == 0 && UnknownCommits.Count == 0 && UnknownMissions.Count == 0;

        /// <summary>
        /// One-line description for assertion messages.
        /// </summary>
        /// <returns>Description.</returns>
        public override string ToString()
        {
            return "missing headings [" + string.Join(", ", MissingHeadings) + "], opinion labeled " + OpinionLabeled + ", changes split " + ChangesSplit
                + ", unknown files [" + string.Join(", ", UnknownFiles) + "], unknown commits [" + string.Join(", ", UnknownCommits) + "], unknown missions [" + string.Join(", ", UnknownMissions) + "]";
        }
    }
}

namespace Armada.Core.BlackBox.Enrichment
{
    using System;

    /// <summary>
    /// Insertions and deletions of one file in a unified diff.
    /// </summary>
    public class BlackBoxDiffFileStat
    {
        /// <summary>
        /// Repository-relative path.
        /// </summary>
        public string Path { get; set; } = String.Empty;

        /// <summary>
        /// Added lines.
        /// </summary>
        public int Insertions { get; set; } = 0;

        /// <summary>
        /// Removed lines.
        /// </summary>
        public int Deletions { get; set; } = 0;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxDiffFileStat()
        {
        }
    }
}

namespace Armada.Tui.Screens.Build
{
    using System;
    using Armada.Core.Models;

    /// <summary>
    /// One row of the vessel history commit list: a commit (with its local day label on the first commit of each day)
    /// or a status row at the end (loading, end of history, error). Immutable.
    /// </summary>
    public class VesselHistoryRow
    {
        #region Public-Members

        /// <summary>
        /// Row kind.
        /// </summary>
        public VesselHistoryRowKindEnum Kind { get; }

        /// <summary>
        /// The commit, or null for a status row.
        /// </summary>
        public VesselCommit? Commit { get; }

        /// <summary>
        /// Stable row id (the SHA, or a fixed id for a status row).
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// Local day label shown on the first commit of a day (for example <c>Tue 2026-10-06</c>), or empty.
        /// </summary>
        public string DayLabel { get; }

        /// <summary>
        /// Text of a status row (already translated), or empty.
        /// </summary>
        public string Text { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// A commit row.
        /// </summary>
        /// <param name="commit">Commit.</param>
        /// <param name="dayLabel">Day label, or empty.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="commit"/> is null.</exception>
        public VesselHistoryRow(VesselCommit commit, string dayLabel)
        {
            Commit = commit ?? throw new ArgumentNullException(nameof(commit));
            Kind = VesselHistoryRowKindEnum.Commit;
            Id = commit.Sha;
            DayLabel = dayLabel ?? "";
            Text = "";
        }

        /// <summary>
        /// A status row.
        /// </summary>
        /// <param name="kind">Kind (not <see cref="VesselHistoryRowKindEnum.Commit"/>).</param>
        /// <param name="text">Text (already translated).</param>
        /// <exception cref="ArgumentException">Thrown for <see cref="VesselHistoryRowKindEnum.Commit"/>.</exception>
        public VesselHistoryRow(VesselHistoryRowKindEnum kind, string text)
        {
            if (kind == VesselHistoryRowKindEnum.Commit) throw new ArgumentException("A commit row needs a commit.", nameof(kind));
            Kind = kind;
            Commit = null;
            Id = "__" + kind.ToString().ToLowerInvariant();
            DayLabel = "";
            Text = text ?? "";
        }

        #endregion
    }
}

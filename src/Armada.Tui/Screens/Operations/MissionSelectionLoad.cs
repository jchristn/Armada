namespace Armada.Tui.Screens.Operations
{
    using Armada.Core.Models;

    /// <summary>
    /// One <see cref="MissionSelectionPanel"/> load: the summary plus the data of the table being shown.
    /// </summary>
    public sealed class MissionSelectionLoad
    {
        /// <summary>
        /// Selection summary, or null.
        /// </summary>
        public CaptainSelectionSummary? Summary { get; set; } = null;

        /// <summary>
        /// Decision detail for the comparison view, or null.
        /// </summary>
        public CaptainSelectionDecisionDetail? Detail { get; set; } = null;

        /// <summary>
        /// History page, or null.
        /// </summary>
        public EnumerationResult<CaptainSelectionDecisionView>? History { get; set; } = null;

        /// <summary>
        /// Preview, or null.
        /// </summary>
        public CaptainSelectionPreview? Preview { get; set; } = null;
    }
}

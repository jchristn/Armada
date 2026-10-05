namespace Armada.Tui.Screens.Build
{
    /// <summary>
    /// One line of the fleet recommendations view: a fleet header, a note under it, or one of its vessels.
    /// </summary>
    public class ImportFleetRow
    {
        #region Public-Members

        /// <summary>
        /// The fleet draft.
        /// </summary>
        public ImportFleetDraft Draft { get; }

        /// <summary>
        /// Kind: <c>fleet</c>, <c>note</c>, or <c>vessel</c>.
        /// </summary>
        public string Kind { get; }

        /// <summary>
        /// Vessel id for vessel rows, or null.
        /// </summary>
        public string? VesselId { get; }

        /// <summary>
        /// Text for note rows.
        /// </summary>
        public string Text { get; }

        /// <summary>
        /// True for rows the cursor can stop on (fleets and vessels).
        /// </summary>
        public bool Selectable
        {
            get { return Kind != "note"; }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="draft">Draft.</param>
        /// <param name="kind">Kind.</param>
        /// <param name="vesselId">Vessel id, or null.</param>
        /// <param name="text">Note text.</param>
        public ImportFleetRow(ImportFleetDraft draft, string kind, string? vesselId = null, string text = "")
        {
            Draft = draft;
            Kind = kind ?? "fleet";
            VesselId = vesselId;
            Text = text ?? "";
        }

        #endregion
    }
}

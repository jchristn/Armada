namespace Armada.Server.Ask
{
    /// <summary>
    /// The vessel a tool call targets, read from its arguments so the proposal summary can name the vessel.
    /// Other argument properties are ignored.
    /// </summary>
    public class VesselReferenceArgs
    {
        #region Public-Members

        /// <summary>
        /// Vessel identifier (vsl_ prefix), or null when the call does not name one.
        /// </summary>
        public string? VesselId { get; set; } = null;

        #endregion
    }
}

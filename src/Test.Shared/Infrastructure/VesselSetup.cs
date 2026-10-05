namespace Test.Shared.Infrastructure
{
    using Armada.Core.Models;

    /// <summary>
    /// A fleet and a vessel created on a live server for an end-to-end test, with the vessel's local bare origin.
    /// </summary>
    public sealed class VesselSetup
    {
        #region Public-Members

        /// <summary>
        /// Fleet.
        /// </summary>
        public Fleet Fleet { get; set; } = new Fleet();

        /// <summary>
        /// Vessel.
        /// </summary>
        public Vessel Vessel { get; set; } = new Vessel();

        /// <summary>
        /// Path of the vessel's bare origin repository.
        /// </summary>
        public string BarePath { get; set; } = "";

        #endregion
    }
}

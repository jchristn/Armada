namespace Armada.Core.Models
{
    /// <summary>
    /// Body of POST /api/v1/blackbox/{id}/export-fixture and the arguments of export_blackbox_fixture.
    /// </summary>
    public class BlackBoxFixtureExportRequest
    {
        #region Public-Members

        /// <summary>
        /// Fixture name (file name without .json), or null for the mission id.
        /// </summary>
        public string? Name { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxFixtureExportRequest()
        {
        }

        #endregion
    }
}

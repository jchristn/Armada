namespace Armada.Core.Models
{
    /// <summary>
    /// Vessel import error codes (the <c>Data.Code</c> of a REST error and the MCP <c>Code</c>) for an invalid fleet
    /// categorization request. Reported in place of <see cref="VesselImportCodes.InvalidRequest"/>.
    /// </summary>
    public static class VesselImportCategorizationCodes
    {
        /// <summary>
        /// Error: categorization was enabled without a categorization captain (HTTP 400).
        /// </summary>
        public const string CategorizationCaptainRequired = "CategorizationCaptainRequired";

        /// <summary>
        /// Error: the categorization captain does not exist in the caller's tenant (HTTP 400).
        /// </summary>
        public const string CategorizationCaptainNotFound = "CategorizationCaptainNotFound";
    }
}

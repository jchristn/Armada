namespace Armada.Core.Models
{
    /// <summary>
    /// Enumeration query for fleet action definitions.
    /// </summary>
    public class FleetActionEnumerateRequest : EnumerationQuery
    {
        #region Public-Members

        /// <summary>
        /// Include inactive (soft-deleted built-in) actions. Default false.
        /// </summary>
        public bool IncludeInactive { get; set; } = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public FleetActionEnumerateRequest()
        {
        }

        #endregion
    }
}

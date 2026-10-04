namespace Armada.Core.Services.Health.Json
{
    /// <summary>
    /// One package in npm outdated --json output.
    /// </summary>
    public class NpmOutdatedEntry
    {
        #region Public-Members

        /// <summary>
        /// Installed version, or null when not installed.
        /// </summary>
        public string? Current { get; set; } = null;

        /// <summary>
        /// Highest version satisfying the declared range.
        /// </summary>
        public string? Wanted { get; set; } = null;

        /// <summary>
        /// Latest published version.
        /// </summary>
        public string? Latest { get; set; } = null;

        /// <summary>
        /// Install location.
        /// </summary>
        public string? Location { get; set; } = null;

        /// <summary>
        /// Package that depends on this one.
        /// </summary>
        public string? Dependent { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public NpmOutdatedEntry()
        {
        }

        #endregion
    }
}

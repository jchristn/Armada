namespace Armada.Core.Services.Health.Json
{
    using System.Collections.Generic;

    /// <summary>
    /// One package reference in dotnet list package output.
    /// </summary>
    public class DotnetListPackage
    {
        #region Public-Members

        /// <summary>
        /// Package identifier.
        /// </summary>
        public string? Id { get; set; } = null;

        /// <summary>
        /// Requested version or range.
        /// </summary>
        public string? RequestedVersion { get; set; } = null;

        /// <summary>
        /// Resolved version.
        /// </summary>
        public string? ResolvedVersion { get; set; } = null;

        /// <summary>
        /// Latest available version (outdated mode).
        /// </summary>
        public string? LatestVersion { get; set; } = null;

        /// <summary>
        /// Known vulnerabilities (vulnerable mode).
        /// </summary>
        public List<DotnetListVulnerability>? Vulnerabilities { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public DotnetListPackage()
        {
        }

        #endregion
    }
}

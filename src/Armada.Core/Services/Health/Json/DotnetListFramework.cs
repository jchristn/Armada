namespace Armada.Core.Services.Health.Json
{
    using System.Collections.Generic;

    /// <summary>
    /// One target framework of a project in dotnet list package output.
    /// </summary>
    public class DotnetListFramework
    {
        #region Public-Members

        /// <summary>
        /// Target framework moniker.
        /// </summary>
        public string? Framework { get; set; } = null;

        /// <summary>
        /// Directly referenced packages.
        /// </summary>
        public List<DotnetListPackage>? TopLevelPackages { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public DotnetListFramework()
        {
        }

        #endregion
    }
}

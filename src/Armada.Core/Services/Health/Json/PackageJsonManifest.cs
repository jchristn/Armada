namespace Armada.Core.Services.Health.Json
{
    using System.Collections.Generic;

    /// <summary>
    /// The parts of a package.json file the health criteria read.
    /// </summary>
    public class PackageJsonManifest
    {
        #region Public-Members

        /// <summary>
        /// Package name.
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Runtime dependencies.
        /// </summary>
        public Dictionary<string, string>? Dependencies { get; set; } = null;

        /// <summary>
        /// Development dependencies.
        /// </summary>
        public Dictionary<string, string>? DevDependencies { get; set; } = null;

        /// <summary>
        /// npm scripts.
        /// </summary>
        public Dictionary<string, string>? Scripts { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public PackageJsonManifest()
        {
        }

        #endregion
    }
}

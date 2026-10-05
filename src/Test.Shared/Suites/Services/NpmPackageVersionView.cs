namespace Test.Shared.Suites.Services
{
    using System.Collections.Generic;

    /// <summary>
    /// The version fields of a package.json or package-lock.json file.
    /// </summary>
    public class NpmPackageVersionView
    {
        #region Public-Members

        /// <summary>
        /// Top-level version.
        /// </summary>
        public string? Version { get; set; } = null;

        /// <summary>
        /// package-lock.json "packages" map (the root package is the "" key), or null.
        /// </summary>
        public Dictionary<string, NpmLockPackageView>? Packages { get; set; } = null;

        #endregion
    }
}

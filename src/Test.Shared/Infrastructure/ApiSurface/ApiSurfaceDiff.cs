namespace Test.Shared.Infrastructure.ApiSurface
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Result of comparing a live API surface to the frozen baseline.
    /// </summary>
    public sealed class ApiSurfaceDiff
    {
        #region Public-Members

        /// <summary>
        /// Removals and incompatible changes (fail the contract test).
        /// </summary>
        public List<string> Breaking { get; set; } = new List<string>();

        /// <summary>
        /// Additions (allowed; refresh the surface file to freeze them).
        /// </summary>
        public List<string> Additions { get; set; } = new List<string>();

        /// <summary>
        /// Compatible changes worth a look (type names, defaults, looser auth).
        /// </summary>
        public List<string> Notes { get; set; } = new List<string>();

        #endregion
    }
}

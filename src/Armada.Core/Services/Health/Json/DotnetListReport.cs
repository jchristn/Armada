namespace Armada.Core.Services.Health.Json
{
    using System.Collections.Generic;

    /// <summary>
    /// Root of the JSON written by dotnet list package --format json (schema version 1).
    /// </summary>
    public class DotnetListReport
    {
        #region Public-Members

        /// <summary>
        /// Schema version.
        /// </summary>
        public int? Version { get; set; } = null;

        /// <summary>
        /// Parameters the command ran with.
        /// </summary>
        public string? Parameters { get; set; } = null;

        /// <summary>
        /// Problems reported instead of, or in addition to, results.
        /// </summary>
        public List<DotnetListProblem>? Problems { get; set; } = null;

        /// <summary>
        /// Per-project results.
        /// </summary>
        public List<DotnetListProject>? Projects { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public DotnetListReport()
        {
        }

        #endregion
    }
}

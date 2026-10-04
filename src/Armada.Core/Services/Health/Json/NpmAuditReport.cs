namespace Armada.Core.Services.Health.Json
{
    using System.Collections.Generic;

    /// <summary>
    /// Root of the JSON written by npm audit --json (audit report version 2).
    /// </summary>
    public class NpmAuditReport
    {
        #region Public-Members

        /// <summary>
        /// Audit report version.
        /// </summary>
        public int? AuditReportVersion { get; set; } = null;

        /// <summary>
        /// Vulnerable packages keyed by package name.
        /// </summary>
        public Dictionary<string, NpmAuditVulnerability>? Vulnerabilities { get; set; } = null;

        /// <summary>
        /// Error, when the audit could not run.
        /// </summary>
        public NpmError? Error { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public NpmAuditReport()
        {
        }

        #endregion
    }
}

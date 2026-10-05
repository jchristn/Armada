namespace Test.Shared.Suites.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// Backend code lists and enum member names the dashboard tests check their labels against. Stored as
    /// src/Armada.Dashboard/src/test/fixtures/backendCodes.json and verified against the C# types by
    /// <see cref="DashboardCodeListSuite"/>. Every list is sorted ordinally.
    /// </summary>
    public class DashboardCodeFixture
    {
        #region Public-Members

        /// <summary>
        /// Values of the constants in Armada.Core.Services.FleetActionReasonCodes.
        /// </summary>
        public List<string> FleetActionReasonCodes { get; set; } = new List<string>();

        /// <summary>
        /// Values of the constants in Armada.Core.Models.VesselImportCodes.
        /// </summary>
        public List<string> VesselImportCodes { get; set; } = new List<string>();

        /// <summary>
        /// Values of the constants in Armada.Core.Models.VesselImportCategorizationCodes.
        /// </summary>
        public List<string> VesselImportCategorizationCodes { get; set; } = new List<string>();

        /// <summary>
        /// Values of the constants in Armada.Core.Services.Health.VesselHealthDetailCodes.
        /// </summary>
        public List<string> VesselHealthDetailCodes { get; set; } = new List<string>();

        /// <summary>
        /// Members of FleetActionRunStatusEnum.
        /// </summary>
        public List<string> FleetActionRunStatusEnum { get; set; } = new List<string>();

        /// <summary>
        /// Members of FleetActionTargetStatusEnum.
        /// </summary>
        public List<string> FleetActionTargetStatusEnum { get; set; } = new List<string>();

        /// <summary>
        /// Members of VesselImportCandidateStatusEnum.
        /// </summary>
        public List<string> VesselImportCandidateStatusEnum { get; set; } = new List<string>();

        /// <summary>
        /// Members of VesselImportOutcomeEnum.
        /// </summary>
        public List<string> VesselImportOutcomeEnum { get; set; } = new List<string>();

        /// <summary>
        /// Members of VesselImportBatchStatusEnum.
        /// </summary>
        public List<string> VesselImportBatchStatusEnum { get; set; } = new List<string>();

        /// <summary>
        /// Members of VesselHealthCriterionEnum.
        /// </summary>
        public List<string> VesselHealthCriterionEnum { get; set; } = new List<string>();

        /// <summary>
        /// Members of VesselHealthStatusEnum.
        /// </summary>
        public List<string> VesselHealthStatusEnum { get; set; } = new List<string>();

        /// <summary>
        /// Members of VulnerabilitySeverityEnum.
        /// </summary>
        public List<string> VulnerabilitySeverityEnum { get; set; } = new List<string>();

        /// <summary>
        /// Members of DependencyDriftEnum.
        /// </summary>
        public List<string> DependencyDriftEnum { get; set; } = new List<string>();

        #endregion
    }
}

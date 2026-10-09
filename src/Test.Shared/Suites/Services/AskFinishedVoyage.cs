namespace Test.Shared.Suites.Services
{
    using System;
    using Armada.Core.Models;

    /// <summary>
    /// A tracked voyage created finished by <see cref="AskWorkReportSuite"/>: its tracked work row and its only mission.
    /// </summary>
    public sealed class AskFinishedVoyage
    {
        /// <summary>
        /// Tracked work row of the voyage.
        /// </summary>
        public AskTrackedWork Work { get; set; } = new AskTrackedWork();

        /// <summary>
        /// Identifier of the voyage's only mission.
        /// </summary>
        public string MissionId { get; set; } = String.Empty;
    }
}

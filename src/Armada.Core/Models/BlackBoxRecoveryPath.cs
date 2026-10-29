namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// A ranked recovery path with exact commands and a machine-readable action.
    /// </summary>
    public class BlackBoxRecoveryPath
    {
        /// <summary>
        /// Path id within the entry (for example rp1).
        /// </summary>
        public string Id { get; set; } = "";

        /// <summary>
        /// Rank, 1 is best.
        /// </summary>
        public int Rank { get; set; } = 0;

        /// <summary>
        /// Title.
        /// </summary>
        public string Title { get; set; } = "";

        /// <summary>
        /// Preconditions.
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxPreconditions, Reason = "Capped by BlackBoxRecordBounder")]
        public List<string> Preconditions
        {
            get => _Preconditions;
            set => _Preconditions = value ?? new List<string>();
        }

        /// <summary>
        /// Steps.
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxRecoverySteps, Reason = "Capped by BlackBoxRecordBounder")]
        public List<BlackBoxRecoveryStep> Steps
        {
            get => _Steps;
            set => _Steps = value ?? new List<BlackBoxRecoveryStep>();
        }

        /// <summary>
        /// Primary action, or null.
        /// </summary>
        public BlackBoxAction? Action { get; set; } = null;

        /// <summary>
        /// Risk.
        /// </summary>
        public BlackBoxSeverityEnum Risk { get; set; } = BlackBoxSeverityEnum.Low;

        /// <summary>
        /// Whether any step can lose data (requires acknowledgeDataLoss).
        /// </summary>
        public bool DataLossRisk { get; set; } = false;

        /// <summary>
        /// Expected outcome.
        /// </summary>
        public string? ExpectedOutcome { get; set; } = null;

        /// <summary>
        /// Rule that produced it.
        /// </summary>
        public string? RuleId { get; set; } = null;

        private List<string> _Preconditions = new List<string>();
        private List<BlackBoxRecoveryStep> _Steps = new List<BlackBoxRecoveryStep>();

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxRecoveryPath()
        {
        }
    }
}

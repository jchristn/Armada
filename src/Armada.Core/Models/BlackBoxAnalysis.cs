namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Analysis section: advisor findings. Every claim carries a rule id.
    /// </summary>
    public class BlackBoxAnalysis
    {
        /// <summary>
        /// Plain-language summary (3 to 6 sentences), or null.
        /// </summary>
        public string? Summary { get; set; } = null;

        /// <summary>
        /// Whether Summary was written by a model (labeled model-written).
        /// </summary>
        public bool SummaryIsModelWritten { get; set; } = false;

        /// <summary>
        /// Why findings.
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxFindings, Reason = "Capped by BlackBoxRecordBounder")]
        public List<BlackBoxFinding> Why
        {
            get => _Why;
            set => _Why = value ?? new List<BlackBoxFinding>();
        }

        /// <summary>
        /// Configuration recommendations.
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxRecommendations, Reason = "Capped by BlackBoxRecordBounder")]
        public List<BlackBoxConfigRecommendation> ConfigRecommendations
        {
            get => _ConfigRecommendations;
            set => _ConfigRecommendations = value ?? new List<BlackBoxConfigRecommendation>();
        }

        /// <summary>
        /// Armada improvement hypotheses.
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxImprovements, Reason = "Capped by BlackBoxRecordBounder")]
        public List<BlackBoxImprovement> ArmadaImprovements
        {
            get => _ArmadaImprovements;
            set => _ArmadaImprovements = value ?? new List<BlackBoxImprovement>();
        }

        /// <summary>
        /// Ranked recovery paths.
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxRecoveryPaths, Reason = "Capped by BlackBoxRecordBounder")]
        public List<BlackBoxRecoveryPath> RecoveryPaths
        {
            get => _RecoveryPaths;
            set => _RecoveryPaths = value ?? new List<BlackBoxRecoveryPath>();
        }

        /// <summary>
        /// Triage verdict, or null.
        /// </summary>
        public BlackBoxTriageVerdict? Triage { get; set; } = null;

        private List<BlackBoxFinding> _Why = new List<BlackBoxFinding>();
        private List<BlackBoxConfigRecommendation> _ConfigRecommendations = new List<BlackBoxConfigRecommendation>();
        private List<BlackBoxImprovement> _ArmadaImprovements = new List<BlackBoxImprovement>();
        private List<BlackBoxRecoveryPath> _RecoveryPaths = new List<BlackBoxRecoveryPath>();

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxAnalysis()
        {
        }
    }
}

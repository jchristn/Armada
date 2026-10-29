namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// One confirmed black box recovery run and what happened to the mission afterwards (blackbox_recovery_outcomes).
    /// </summary>
    public class BlackBoxRecoveryOutcome
    {
        #region Public-Members

        /// <summary>
        /// Id (bro_ prefix).
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null or empty.</exception>
        public string Id
        {
            get => _Id;
            set => _Id = String.IsNullOrEmpty(value) ? throw new ArgumentNullException(nameof(Id)) : value;
        }

        /// <summary>
        /// Tenant.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Who ran it.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Black box entry.
        /// </summary>
        public string EntryId { get; set; } = "";

        /// <summary>
        /// Mission, or null for a voyage entry.
        /// </summary>
        public string? MissionId { get; set; } = null;

        /// <summary>
        /// Voyage, or null.
        /// </summary>
        public string? VoyageId { get; set; } = null;

        /// <summary>
        /// Vessel, or null.
        /// </summary>
        public string? VesselId { get; set; } = null;

        /// <summary>
        /// Rule that offered the path.
        /// </summary>
        public string? RuleId { get; set; } = null;

        /// <summary>
        /// Recovery path that ran.
        /// </summary>
        public string PathId { get; set; } = "";

        /// <summary>
        /// Runbook the path started instead of its tool steps, or null.
        /// </summary>
        public string? RunbookId { get; set; } = null;

        /// <summary>
        /// True when every step of the run succeeded.
        /// </summary>
        public bool RunSucceeded { get; set; } = false;

        /// <summary>
        /// What happened next.
        /// </summary>
        public BlackBoxRecoveryOutcomeEnum Outcome { get; set; } = BlackBoxRecoveryOutcomeEnum.Pending;

        /// <summary>
        /// The mission status that settled the outcome, or null.
        /// </summary>
        public string? OutcomeStatus { get; set; } = null;

        /// <summary>
        /// When the run finished.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When the outcome was settled, or null.
        /// </summary>
        public DateTime? ResolvedUtc { get; set; } = null;

        #endregion

        #region Private-Members

        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.BlackBoxRecoveryOutcomeIdPrefix, 24);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxRecoveryOutcome()
        {
        }

        #endregion
    }
}

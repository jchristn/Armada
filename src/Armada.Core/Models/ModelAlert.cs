namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// One scorecard threshold crossed for a (model profile, revision group, persona, vessel or all vessels, rule)
    /// (docs/MODEL_HETEROGENEITY.md, Alerts). There is one row per condition: a later evaluation updates it, resolves it
    /// when the condition no longer holds and reopens it when the condition returns. Alerts are advisory; nothing changes
    /// captains, personas, templates or routing until an operator applies a suggested action.
    /// </summary>
    public class ModelAlert
    {
        #region Public-Members

        /// <summary>
        /// Most records kept in <see cref="Actions"/> (100). Applying an action drops the oldest record past it.
        /// </summary>
        public const int MaxActions = 100;

        /// <summary>
        /// Unique identifier (mal_ prefix).
        /// </summary>
        public string Id
        {
            get => _Id;
            set => _Id = String.IsNullOrEmpty(value) ? throw new ArgumentNullException(nameof(Id)) : value;
        }

        /// <summary>
        /// Tenant of the profile, or null for a tenantless installation.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Model profile identifier (mdl_ prefix).
        /// </summary>
        public string ProfileId { get; set; } = "";

        /// <summary>
        /// The profile's model as served, for display (for example qwen3-coder:30b).
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Revision identifiers of the scorecard group, comma separated (revisions marked compatible pool into one group),
        /// or "unmatched" for attempts that match no recorded revision.
        /// </summary>
        public string RevisionGroup { get; set; } = "";

        /// <summary>
        /// Persona name.
        /// </summary>
        public string Persona { get; set; } = "";

        /// <summary>
        /// Vessel identifier, or null when the alert is about every vessel.
        /// </summary>
        public string? VesselId { get; set; } = null;

        /// <summary>
        /// Vessel name for display, or null.
        /// </summary>
        public string? VesselName { get; set; } = null;

        /// <summary>
        /// Rule whose threshold was crossed.
        /// </summary>
        public ModelAlertRuleEnum Rule { get; set; } = ModelAlertRuleEnum.FirstPassSuccess;

        /// <summary>
        /// Open while the condition holds; Resolved once a later evaluation finds it no longer holds.
        /// </summary>
        public ModelAlertStatusEnum Status { get; set; } = ModelAlertStatusEnum.Open;

        /// <summary>
        /// One-line summary with counts, for example "qwen3-coder:30b as Worker on Armada: first pass 4 of 11".
        /// </summary>
        public string Title { get; set; } = "";

        /// <summary>
        /// The threshold, the interval and what the alert suggests.
        /// </summary>
        public string Detail { get; set; } = "";

        /// <summary>
        /// The metric count (successes, escalations in a row, repairs, accepted results), or null.
        /// </summary>
        public int? Count { get; set; } = null;

        /// <summary>
        /// The metric denominator (missions, attempts, accepted results), or null.
        /// </summary>
        public int? Of { get; set; } = null;

        /// <summary>
        /// The metric value: the rate for rate rules, the streak for EscalationsInARow, tokens per accepted result for
        /// CostPerAcceptedResult.
        /// </summary>
        public double? Value { get; set; } = null;

        /// <summary>
        /// The threshold that was crossed (a rate, a streak length, or the token limit computed from the median).
        /// </summary>
        public double? Threshold { get; set; } = null;

        /// <summary>
        /// When the condition was first raised.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When the condition was last raised or reopened.
        /// </summary>
        public DateTime LastRaisedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When an evaluation last looked at the condition.
        /// </summary>
        public DateTime LastEvaluatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When the condition was resolved, or null while open.
        /// </summary>
        public DateTime? ResolvedUtc { get; set; } = null;

        /// <summary>
        /// The alert is hidden from the inbox until this time (Ignore), or null.
        /// </summary>
        public DateTime? IgnoredUntilUtc { get; set; } = null;

        /// <summary>
        /// Suggested actions applied to this alert, oldest first (the audit record; the newest <see cref="MaxActions"/>).
        /// </summary>
        [CollectionBound(MaxActions, Reason = "Applying an action drops the oldest record past the limit")]
        public List<ModelAlertActionRecord> Actions
        {
            get => _Actions;
            set => _Actions = value ?? new List<ModelAlertActionRecord>();
        }

        /// <summary>
        /// Suggested actions and whether each can be applied now (computed on read, not stored).
        /// </summary>
        [CollectionBound(5, Reason = "One per ModelAlertActionEnum value (MoveDown, RemoveFromPersona, CompactTemplate, Reevaluate, Ignore)")]
        public List<ModelAlertSuggestedAction> SuggestedActions
        {
            get => _SuggestedActions;
            set => _SuggestedActions = value ?? new List<ModelAlertSuggestedAction>();
        }

        /// <summary>
        /// Last time the row changed.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.ModelAlertIdPrefix, 24);
        private List<ModelAlertActionRecord> _Actions = new List<ModelAlertActionRecord>();
        private List<ModelAlertSuggestedAction> _SuggestedActions = new List<ModelAlertSuggestedAction>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ModelAlert()
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether the alert is hidden by Ignore at the given time.
        /// </summary>
        /// <param name="nowUtc">Time.</param>
        /// <returns>True while ignored.</returns>
        public bool IsIgnored(DateTime nowUtc)
        {
            return IgnoredUntilUtc.HasValue && IgnoredUntilUtc.Value > nowUtc;
        }

        #endregion
    }
}

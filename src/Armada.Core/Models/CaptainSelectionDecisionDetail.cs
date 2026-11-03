namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;
    using Armada.Core.Settings;

    /// <summary>Authorized decision details with separately paged comparison and execution evidence.</summary>
    public sealed class CaptainSelectionDecisionDetail
    {
        /// <summary>Immutable authorized decision summary.</summary>
        public CaptainSelectionDecisionView Decision { get; set; } = new CaptainSelectionDecisionView();
        /// <summary>Effective non-secret policy values recorded for this decision.</summary>
        public UsageSelectionSettings Policy { get; set; } = new UsageSelectionSettings();
        /// <summary>A page of scoped candidates; hidden peers do not contribute to totals.</summary>
        public EnumerationResult<CaptainSelectionCandidateView> Candidates { get; set; } = new EnumerationResult<CaptainSelectionCandidateView>();
        /// <summary>A bounded page of the decision attempt execution evidence.</summary>
        public EnumerationResult<CaptainSelectionExecutionEvent> ExecutionEvents { get; set; } = new EnumerationResult<CaptainSelectionExecutionEvent>();
    }
}

namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;
    using Armada.Core.Services;
    using Armada.Core.Settings;

    /// <summary>Authorized decision details with separately paged comparison and execution evidence.</summary>
    public sealed class CaptainSelectionDecisionDetail
    {
        /// <summary>Immutable recovery authorization applied to this decision, if any. Later revocations do not rewrite it.</summary>
        public UsageSelectionRecovery? Recovery { get; set; }
        /// <summary>Immutable authorized decision summary.</summary>
        public CaptainSelectionDecisionView Decision { get; set; } = new CaptainSelectionDecisionView();
        /// <summary>Effective non-secret policy values recorded for this decision.</summary>
        public UsageSelectionSettings Policy { get; set; } = new UsageSelectionSettings();
        /// <summary>A page of scoped candidates; hidden peers do not contribute to totals.</summary>
        public EnumerationResult<CaptainSelectionCandidateView> Candidates { get; set; } = new EnumerationResult<CaptainSelectionCandidateView>();
        /// <summary>
        /// Visible evaluated candidates the decision counted by primary reason instead of storing individually, largest first.
        /// Empty when every visible candidate is in <see cref="Candidates"/>.
        /// </summary>
        [CollectionBound(64)]
        public List<CaptainSelectionOmittedReason> OmittedCandidates { get; set; } = new List<CaptainSelectionOmittedReason>();
        /// <summary>A bounded page of the decision attempt execution evidence.</summary>
        public EnumerationResult<CaptainSelectionExecutionEvent> ExecutionEvents { get; set; } = new EnumerationResult<CaptainSelectionExecutionEvent>();
    }
}

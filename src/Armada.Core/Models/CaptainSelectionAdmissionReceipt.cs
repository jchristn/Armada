namespace Armada.Core.Models
{
    /// <summary>Atomic observed-admission outcome; only Claimed authorizes a new launch.</summary>
    public sealed class CaptainSelectionAdmissionReceipt
    {
        /// <summary>True only for a newly committed slot, mission assignment and decision.</summary>
        public bool Claimed { get; set; }
        /// <summary>A prior matching attempt already committed. Reconcile it; never launch it again.</summary>
        public bool AlreadyCommitted { get; set; }
        /// <summary>Durable decision identity, when a matching admission exists.</summary>
        public string? DecisionId { get; set; }
    }
}

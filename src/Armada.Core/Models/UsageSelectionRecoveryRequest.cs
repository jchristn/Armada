namespace Armada.Core.Models
{
    /// <summary>Compare-and-set recovery authorization; the server supplies scope, actor and timestamp.</summary>
    public sealed class UsageSelectionRecoveryRequest
    {
        /// <summary>Last observed revision; zero when no authorization exists.</summary>
        public int ExpectedRevision { get; set; }
        /// <summary>Explicit permission for eligible preferred-captain fallback.</summary>
        public bool AllowFallback { get; set; }
        /// <summary>Explicit permission to relax only the configured soft reserve.</summary>
        public bool ReserveException { get; set; }
        /// <summary>Required explanation, 1..512 characters; secrets and control characters are rejected.</summary>
        public string Reason { get; set; } = System.String.Empty;
    }
}

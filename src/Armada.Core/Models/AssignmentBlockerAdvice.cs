namespace Armada.Core.Models
{
    using Armada.Core.Enums;

    /// <summary>A suggested next step computed from the limit that actually blocked admission.</summary>
    public sealed class AssignmentBlockerAdvice
    {
        /// <summary>Human-readable explanation; always present even without configuration privileges.</summary>
        public string Text { get; set; } = "";
        /// <summary>Optional navigation action; omitted for callers without configuration privileges.</summary>
        public AssignmentBlockerActionEnum? Action { get; set; }
        /// <summary>Entity category, such as vessel, captain, endpoint or settings.</summary>
        public string? TargetType { get; set; }
        /// <summary>Entity identifier, where applicable.</summary>
        public string? TargetId { get; set; }
        /// <summary>Setting that controls the gate.</summary>
        public string? Setting { get; set; }
        /// <summary>Current configured limit.</summary>
        public int? CurrentValue { get; set; }
        /// <summary>Smallest finite increase that admits another mission; absent at a hard ceiling.</summary>
        public int? SuggestedValue { get; set; }
    }
}

namespace Armada.Core.Models
{
    using Armada.Core.Enums;

    /// <summary>
    /// A single actionable item in the operator's "needs you" inbox -- something awaiting a human
    /// decision or intervention (a review to approve, a failed landing, a stalled captain, etc.).
    /// </summary>
    public class InboxItem
    {
        /// <summary>
        /// A short machine-readable kind (e.g. "review", "landing_failed", "failed", "stalled_captain").
        /// </summary>
        public string Kind { get; set; } = string.Empty;

        /// <summary>
        /// Severity of the item.
        /// </summary>
        public InboxSeverityEnum Severity { get; set; } = InboxSeverityEnum.Info;

        /// <summary>
        /// Human-readable title.
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Additional detail.
        /// </summary>
        public string Detail { get; set; } = string.Empty;

        /// <summary>
        /// The referenced entity type (e.g. "mission", "captain").
        /// </summary>
        public string? EntityType { get; set; } = null;

        /// <summary>
        /// Display name of the referenced entity (mission title, captain name, deployment environment, merge target
        /// branch), or null. Use this instead of parsing <see cref="Title"/>.
        /// </summary>
        public string? EntityName { get; set; } = null;

        /// <summary>
        /// The referenced entity id.
        /// </summary>
        public string? EntityId { get; set; } = null;

        /// <summary>
        /// A relative dashboard path that takes the operator to the item.
        /// </summary>
        public string Href { get; set; } = string.Empty;
    }
}

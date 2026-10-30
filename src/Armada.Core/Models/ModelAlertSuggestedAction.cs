namespace Armada.Core.Models
{
    using Armada.Core.Enums;

    /// <summary>
    /// A suggested action of a model alert and whether it can be applied now. Nothing is applied until an operator asks.
    /// </summary>
    public class ModelAlertSuggestedAction
    {
        /// <summary>
        /// Action.
        /// </summary>
        public ModelAlertActionEnum Action { get; set; } = ModelAlertActionEnum.Ignore;

        /// <summary>
        /// Short label, for example "Move down in Worker priorities".
        /// </summary>
        public string Label { get; set; } = "";

        /// <summary>
        /// What applying it would change.
        /// </summary>
        public string Description { get; set; } = "";

        /// <summary>
        /// Whether it can be applied now.
        /// </summary>
        public bool Available { get; set; } = true;

        /// <summary>
        /// Why it cannot be applied, or null when available.
        /// </summary>
        public string? UnavailableReason { get; set; } = null;

        /// <summary>
        /// Whether the alert's rule recommends this action first (the compact template for protocol repairs).
        /// </summary>
        public bool Recommended { get; set; } = false;
    }
}

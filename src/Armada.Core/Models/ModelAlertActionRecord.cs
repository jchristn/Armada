namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// One suggested action an operator applied to a model alert (the audit record kept on the alert).
    /// </summary>
    public class ModelAlertActionRecord
    {
        /// <summary>
        /// Action applied.
        /// </summary>
        public ModelAlertActionEnum Action { get; set; } = ModelAlertActionEnum.Ignore;

        /// <summary>
        /// When it was applied.
        /// </summary>
        public DateTime AppliedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// User who applied it, or null.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Whether anything changed (false when the action found nothing to change).
        /// </summary>
        public bool Changed { get; set; } = false;

        /// <summary>
        /// What the action did, for example the persona entries moved or the Evaluate run started.
        /// </summary>
        public string Result { get; set; } = "";

        /// <summary>
        /// Identifier of what the action created or changed (persona name, model probe run id), or null.
        /// </summary>
        public string? TargetId { get; set; } = null;
    }
}

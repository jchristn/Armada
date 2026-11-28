namespace Armada.Core.Models
{
    using Armada.Core.Enums;

    /// <summary>
    /// One action in a <see cref="RecoveryDecision"/>, with why it was chosen.
    /// </summary>
    public class RecoveryStep
    {
        /// <summary>The action.</summary>
        public RecoveryActionEnum Action { get; set; } = RecoveryActionEnum.Fail;

        /// <summary>Why this action was chosen, in one line, or null when the site's own wording applies.</summary>
        public string? Reason { get; set; } = null;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public RecoveryStep()
        {
        }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="action">The action.</param>
        /// <param name="reason">Why it was chosen, or null.</param>
        public RecoveryStep(RecoveryActionEnum action, string? reason = null)
        {
            Action = action;
            Reason = reason;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Reason == null ? Action.ToString() : Action + " (" + Reason + ")";
        }
    }
}

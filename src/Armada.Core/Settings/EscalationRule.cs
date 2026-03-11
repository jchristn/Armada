namespace Armada.Core.Settings
{
    using System.Text.Json.Serialization;
    using Armada.Core.Enums;

    /// <summary>
    /// A configurable escalation rule that fires when a condition is met.
    /// </summary>
    public class EscalationRule
    {
        #region Public-Members

        /// <summary>
        /// Whether this rule is enabled.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Condition that triggers this rule.
        /// </summary>
        public EscalationTriggerEnum Trigger { get; set; } = EscalationTriggerEnum.CaptainStalled;

        /// <summary>
        /// Threshold in minutes for time-based triggers (CaptainStalled, MissionOverdue).
        /// Ignored for event-based triggers (MissionFailed, RecoveryExhausted, PoolExhausted).
        /// </summary>
        public int ThresholdMinutes { get; set; } = 15;

        /// <summary>
        /// Action to take when the rule fires.
        /// </summary>
        public EscalationActionEnum Action { get; set; } = EscalationActionEnum.Log;

        /// <summary>
        /// Webhook URL for Webhook action. Required when Action is Webhook.
        /// </summary>
        public string? WebhookUrl { get; set; } = null;

        /// <summary>
        /// Minimum interval in minutes between repeated firings of this rule for the same entity.
        /// Prevents notification spam. Default: 30 minutes.
        /// </summary>
        public int CooldownMinutes { get; set; } = 30;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with defaults.
        /// </summary>
        public EscalationRule()
        {
        }

        /// <summary>
        /// Instantiate with trigger and action.
        /// </summary>
        /// <param name="trigger">Trigger condition.</param>
        /// <param name="action">Action to take.</param>
        /// <param name="thresholdMinutes">Threshold in minutes for time-based triggers.</param>
        public EscalationRule(EscalationTriggerEnum trigger, EscalationActionEnum action, int thresholdMinutes = 15)
        {
            Trigger = trigger;
            Action = action;
            ThresholdMinutes = thresholdMinutes;
        }

        #endregion
    }
}

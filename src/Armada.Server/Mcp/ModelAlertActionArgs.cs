namespace Armada.Server.Mcp
{
    /// <summary>
    /// Arguments of apply_model_alert_action and ignore_model_alert.
    /// </summary>
    public class ModelAlertActionArgs
    {
        /// <summary>
        /// Model alert ID (mal_ prefix).
        /// </summary>
        public string? Id { get; set; } = null;

        /// <summary>
        /// Action: MoveDown, RemoveFromPersona, CompactTemplate, Reevaluate or Ignore (apply_model_alert_action only).
        /// </summary>
        public string? Action { get; set; } = null;

        /// <summary>
        /// For Ignore: days to hide the alert (1-365, default 7).
        /// </summary>
        public int? Days { get; set; } = null;
    }
}

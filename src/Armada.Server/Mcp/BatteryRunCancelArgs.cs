namespace Armada.Server.Mcp
{
    /// <summary>
    /// Arguments of cancel_battery_run.
    /// </summary>
    public class BatteryRunCancelArgs
    {
        /// <summary>
        /// Run (bat_ prefix).
        /// </summary>
        public string? Id { get; set; } = null;

        /// <summary>
        /// Reason recorded on the run, or null.
        /// </summary>
        public string? Reason { get; set; } = null;
    }
}

namespace Armada.Core.Models
{
    /// <summary>
    /// Body of POST /api/v1/battery/runs/{id}/cancel (cancel_battery_run). The body is optional.
    /// </summary>
    public class BatteryRunCancelRequest
    {
        /// <summary>
        /// Reason recorded on the run (at most 500 characters), or null for "Cancelled by request.".
        /// </summary>
        public string? Reason { get; set; } = null;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BatteryRunCancelRequest()
        {
        }
    }
}

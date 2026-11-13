namespace Armada.Core.Models
{
    /// <summary>
    /// Body of POST /api/v1/model-alerts/{id}/ignore.
    /// </summary>
    public class ModelAlertIgnoreRequest
    {
        /// <summary>
        /// Days to hide the alert, 1 to 365, default 7.
        /// </summary>
        public int? Days { get; set; } = null;
    }
}

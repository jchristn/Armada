namespace Armada.Core.Models
{
    using Armada.Core.Enums;

    /// <summary>
    /// Body of POST /api/v1/model-alerts/{id}/apply: the suggested action to apply.
    /// </summary>
    public class ModelAlertApplyRequest
    {
        /// <summary>
        /// Action to apply. Required.
        /// </summary>
        public ModelAlertActionEnum? Action { get; set; } = null;

        /// <summary>
        /// For Ignore: days to hide the alert, 1 to 365, default 7.
        /// </summary>
        public int? Days { get; set; } = null;
    }
}

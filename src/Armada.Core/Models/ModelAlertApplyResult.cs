namespace Armada.Core.Models
{
    /// <summary>
    /// Result of applying a suggested action: the record kept on the alert and the alert after the change.
    /// </summary>
    public class ModelAlertApplyResult
    {
        /// <summary>
        /// The audit record of what was done.
        /// </summary>
        public ModelAlertActionRecord Record { get; set; } = new ModelAlertActionRecord();

        /// <summary>
        /// The alert after the action, with fresh suggested actions.
        /// </summary>
        public ModelAlert Alert { get; set; } = new ModelAlert();
    }
}

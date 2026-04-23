namespace Armada.Core.Models
{
    using Armada.Core.Enums;

    /// <summary>
    /// Identifies a playbook selected for a voyage or mission along with the delivery mode.
    /// </summary>
    public class SelectedPlaybook
    {
        /// <summary>
        /// Playbook identifier.
        /// </summary>
        public string PlaybookId { get; set; } = "";

        /// <summary>
        /// Delivery mode to use for this selection.
        /// </summary>
        public PlaybookDeliveryModeEnum DeliveryMode { get; set; } = PlaybookDeliveryModeEnum.InlineFullContent;
    }
}

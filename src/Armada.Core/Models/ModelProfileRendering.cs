namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// Explicit rendering overrides for a model profile, set by an administrator. Every value is null unless set; a null
    /// value means no override. Manual values always win over measured advice.
    /// </summary>
    public class ModelProfileRendering
    {
        /// <summary>
        /// Prompt variant (template) to render for this model, for example Compact, or null.
        /// </summary>
        public string? Template { get; set; } = null;

        /// <summary>
        /// Whether to repeat the output contract at the end of the prompt, or null.
        /// </summary>
        public bool? RepeatContractAtEnd { get; set; } = null;

        /// <summary>
        /// Maximum number of files a mission for this model should touch, 1 or more, or null.
        /// </summary>
        public int? MaxFilesPerMission
        {
            get => _MaxFilesPerMission;
            set => _MaxFilesPerMission = value.HasValue && value.Value < 1 ? throw new ArgumentOutOfRangeException(nameof(MaxFilesPerMission), "MaxFilesPerMission must be 1 or more.") : value;
        }

        private int? _MaxFilesPerMission = null;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ModelProfileRendering()
        {
        }
    }
}

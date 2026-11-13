namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// One follow-up offered on the Ask black box card. A follow-up with a ToolName becomes a proposal the user approves
    /// (POST /api/v1/ask/threads/{id}/actions with Propose true); one with a Prompt is sent to the captain as a message.
    /// </summary>
    public class AskBlackBoxFollowUp
    {
        #region Public-Members

        /// <summary>
        /// Stable name: fix, apply, armada-fix or compare.
        /// </summary>
        public string Name
        {
            get => _Name;
            set => _Name = value ?? String.Empty;
        }

        /// <summary>
        /// Button label (an English key clients translate).
        /// </summary>
        public string Label
        {
            get => _Label;
            set => _Label = value ?? String.Empty;
        }

        /// <summary>
        /// What happens when it is chosen, or null.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// MCP tool proposed, or null for a message follow-up.
        /// </summary>
        public string? ToolName { get; set; } = null;

        /// <summary>
        /// The tool's arguments as a JSON object text, or null.
        /// </summary>
        public string? ArgumentsText { get; set; } = null;

        /// <summary>
        /// Message sent to the captain, or null for a tool follow-up.
        /// </summary>
        public string? Prompt { get; set; } = null;

        #endregion

        #region Private-Members

        private string _Name = String.Empty;
        private string _Label = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AskBlackBoxFollowUp()
        {
        }

        #endregion
    }
}

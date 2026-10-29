namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// A machine-readable action: an MCP tool name and its arguments.
    /// </summary>
    public class BlackBoxAction
    {
        /// <summary>
        /// MCP tool name (for example retry_landing).
        /// </summary>
        public string Tool { get; set; } = "";

        /// <summary>
        /// Tool arguments as text.
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxActionArguments, Reason = "Capped by BlackBoxRecordBounder")]
        public Dictionary<string, string> Arguments
        {
            get => _Arguments;
            set => _Arguments = value ?? new Dictionary<string, string>();
        }

        private Dictionary<string, string> _Arguments = new Dictionary<string, string>();

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxAction()
        {
        }
    }
}

namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// One step of a recovery path.
    /// </summary>
    public class BlackBoxRecoveryStep
    {
        /// <summary>
        /// Order, starting at 1.
        /// </summary>
        public int Order { get; set; } = 0;

        /// <summary>
        /// Description.
        /// </summary>
        public string Description { get; set; } = "";

        /// <summary>
        /// MCP command text.
        /// </summary>
        public string? McpCommand { get; set; } = null;

        /// <summary>
        /// REST command text.
        /// </summary>
        public string? RestCommand { get; set; } = null;

        /// <summary>
        /// CLI command text.
        /// </summary>
        public string? CliCommand { get; set; } = null;

        /// <summary>
        /// Machine-readable action, or null for a manual step.
        /// </summary>
        public BlackBoxAction? Action { get; set; } = null;

        /// <summary>
        /// Whether the step can lose data.
        /// </summary>
        public bool DataLossRisk { get; set; } = false;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxRecoveryStep()
        {
        }
    }
}

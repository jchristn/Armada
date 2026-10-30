namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Run a recovery path recorded on an entry. Dry run first; the real run needs the confirm token from the dry run.
    /// </summary>
    public class BlackBoxRecoveryRunRequest
    {
        /// <summary>
        /// When true (default), return what would happen and a confirm token.
        /// </summary>
        public bool DryRun { get; set; } = true;

        /// <summary>
        /// Token from the dry run (10-minute expiry; binds the entry, the path and the mission version).
        /// </summary>
        public string? ConfirmToken { get; set; } = null;

        /// <summary>
        /// Required for a path marked dataLossRisk.
        /// </summary>
        public bool AcknowledgeDataLoss { get; set; } = false;

        /// <summary>
        /// Author kind of the note the run adds to the entry: Agent (default, an MCP client or captain) or Operator (a
        /// person in the dashboard, TUI or CLI).
        /// </summary>
        public BlackBoxNoteAuthorKindEnum? AuthorKind { get; set; } = null;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxRecoveryRunRequest()
        {
        }
    }
}

namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Add a note to a black box entry.
    /// </summary>
    public class BlackBoxNoteAddRequest
    {
        /// <summary>
        /// Markdown body (1 to 8000 characters).
        /// </summary>
        public string Body { get; set; } = "";

        /// <summary>
        /// Author kind; defaults to Operator for REST and Agent for MCP.
        /// </summary>
        public BlackBoxNoteAuthorKindEnum? AuthorKind { get; set; } = null;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxNoteAddRequest()
        {
        }
    }
}

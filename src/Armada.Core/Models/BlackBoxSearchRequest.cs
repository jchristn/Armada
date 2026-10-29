namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Full-text search over reason, summary and notes, with the enumerate filters and paging (POST /api/v1/blackbox/search, MCP search_blackbox).
    /// </summary>
    public class BlackBoxSearchRequest : BlackBoxEnumerateRequest
    {
        /// <summary>
        /// Text to find (1 to 200 characters, case-insensitive substring).
        /// </summary>
        public string Query { get; set; } = "";

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxSearchRequest()
        {
        }
    }
}

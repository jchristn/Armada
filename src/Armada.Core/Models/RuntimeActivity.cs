namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// The latest thing a running captain did, read from its runtime's structured output (Claude Code stream-json,
    /// Codex exec --json): a tool call it started, text it wrote, or reasoning. Shown live as the captain's current
    /// activity in Harbor's Running now list and on the mission.
    /// </summary>
    public class RuntimeActivity
    {
        #region Public-Members

        /// <summary>
        /// What kind of activity this is.
        /// </summary>
        public RuntimeActivityKindEnum Kind { get; set; } = RuntimeActivityKindEnum.Text;

        /// <summary>
        /// Tool name as the runtime reported it (for example "Bash" or "mcp__armada__armada_status"), for a tool call;
        /// otherwise null.
        /// </summary>
        public string? ToolName { get; set; } = null;

        /// <summary>
        /// The description the runtime gave a tool call (Claude Code's Bash description), or null.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// The short input of a tool call (the command, the file path, the search pattern) or the first line of text or
        /// reasoning, truncated; null when there is none.
        /// </summary>
        public string? Detail { get; set; } = null;

        /// <summary>
        /// One line describing the activity, for example "Running tests: dotnet test src/App.sln" or
        /// "Calling tool armada_status".
        /// </summary>
        public string Summary { get; set; } = String.Empty;

        /// <summary>
        /// When the activity was observed, UTC.
        /// </summary>
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Public-Methods

        /// <summary>
        /// A copy.
        /// </summary>
        /// <returns>The copy.</returns>
        public RuntimeActivity Clone()
        {
            return new RuntimeActivity
            {
                Kind = Kind,
                ToolName = ToolName,
                Description = Description,
                Detail = Detail,
                Summary = Summary,
                TimestampUtc = TimestampUtc
            };
        }

        #endregion
    }
}

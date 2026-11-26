namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// The Armada MCP tools each mission persona is shown up front. A captain's prompt names a handful of Armada tools
    /// (handoff artifacts, memory, the mission's own status, and for the Recorder the voyage record); sending the schemas
    /// of the whole catalog (175 tools) on every model turn costs tens of thousands of input tokens and makes small models
    /// pick the wrong tool. The scope changes exposure only: every tool the caller's token permits stays callable, and
    /// the rest of the catalog is found through tool discovery. The Admiral passes the scope to the runtime in
    /// <see cref="EnvironmentVariable"/>.
    /// </summary>
    public static class PersonaToolScope
    {
        #region Public-Members

        /// <summary>
        /// Environment variable the Admiral sets on a mission launch to the comma-separated Armada MCP tools the persona
        /// is shown up front. The API endpoint runtime reads it; a Harbor receives it in the launch environment.
        /// </summary>
        public const string EnvironmentVariable = "ARMADA_MCP_TOOL_SCOPE";

        /// <summary>
        /// Estimated schema tokens of the API endpoint runtime's built-in coding tools (read, write, edit, search, run
        /// process, task plan), sent with every model turn. A Runtimes test keeps it within a quarter of the measured value.
        /// </summary>
        public const int BuiltInToolSchemaTokens = 3000;

        /// <summary>
        /// Estimated schema tokens of one scoped Armada MCP tool (measured at 3.2 characters per token: Judge 5 tools
        /// about 1,045 tokens, Recorder 9 tools about 2,235).
        /// </summary>
        public const int ArmadaToolSchemaTokens = 200;

        /// <summary>
        /// Tools every persona is shown: the stage's handoff artifact, the memory search, the mission's own status, and the
        /// vessel model-context update the instructions ask for.
        /// </summary>
        public static readonly IReadOnlyList<string> CommonTools = new List<string>
        {
            "get_handoff_artifact",
            "search_memory",
            "mission_status",
            "update_vessel_context"
        };

        #endregion

        #region Private-Members

        private static readonly IReadOnlyList<string> _ArchitectTools = new List<string> { "voyage_status" };

        private static readonly IReadOnlyList<string> _JudgeTools = new List<string> { "get_mission_log" };

        private static readonly IReadOnlyList<string> _RecorderTools = new List<string>
        {
            "voyage_status",
            "get_mission_log",
            "create_memory",
            "update_memory",
            "delete_memory"
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// The Armada MCP tools a persona is shown up front: <see cref="CommonTools"/>, the persona's own, and its submit tool
        /// (<see cref="StageSubmissionTools.ToolForPersona"/>). A custom persona gets the common tools; an empty persona is a
        /// Worker for the submit tool.
        /// </summary>
        /// <param name="persona">Mission persona, or null.</param>
        /// <returns>Distinct tool names in a stable order.</returns>
        public static List<string> ForPersona(string? persona)
        {
            List<string> tools = new List<string>(CommonTools);
            if (PersonaCatalog.Matches(persona, PersonaCatalog.Architect)) tools.AddRange(_ArchitectTools);
            else if (PersonaCatalog.Matches(persona, PersonaCatalog.Judge)) tools.AddRange(_JudgeTools);
            else if (PersonaCatalog.Matches(persona, PersonaCatalog.Recorder)) tools.AddRange(_RecorderTools);
            // RS-6.2: the persona's submit tool (armada_submit_plan, armada_submit_verdict or armada_submit_result).
            string? submitTool = StageSubmissionTools.ToolForPersona(persona);
            if (submitTool != null) tools.Add(submitTool);
            return tools.Distinct(StringComparer.Ordinal).ToList();
        }

        /// <summary>
        /// Estimated tool schema tokens a mission launch sends with every model turn, for the instructions budget: the
        /// built-in coding tools when the captain has tools, plus the persona's scoped Armada tools and the discovery tool
        /// when it has the Armada MCP tools. CLI runtimes load their own tool schemas and count as zero.
        /// </summary>
        /// <param name="persona">Mission persona, or null.</param>
        /// <param name="builtInTools">Whether the launch sends the built-in coding tools.</param>
        /// <param name="mcpTools">Whether the launch sends the scoped Armada MCP tools.</param>
        /// <returns>Estimated tokens.</returns>
        public static long EstimateSchemaTokens(string? persona, bool builtInTools, bool mcpTools)
        {
            long tokens = builtInTools ? BuiltInToolSchemaTokens : 0;
            if (builtInTools && mcpTools) tokens += (long)(ForPersona(persona).Count + 1) * ArmadaToolSchemaTokens;
            return tokens;
        }

        /// <summary>
        /// Format a scope for <see cref="EnvironmentVariable"/>.
        /// </summary>
        /// <param name="tools">Tool names.</param>
        /// <returns>Comma-separated names.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="tools"/> is null.</exception>
        public static string Format(IEnumerable<string> tools)
        {
            if (tools == null) throw new ArgumentNullException(nameof(tools));
            return String.Join(",", tools.Where(t => !String.IsNullOrWhiteSpace(t)).Select(t => t.Trim()));
        }

        /// <summary>
        /// Parse the value of <see cref="EnvironmentVariable"/>. Null or blank means no scope (the runtime keeps its own
        /// exposure rules).
        /// </summary>
        /// <param name="value">Comma-separated tool names, or null.</param>
        /// <returns>Distinct tool names, or null when there is no scope.</returns>
        public static HashSet<string>? Parse(string? value)
        {
            if (String.IsNullOrWhiteSpace(value)) return null;
            HashSet<string> tools = new HashSet<string>(
                value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                StringComparer.Ordinal);
            return tools.Count == 0 ? null : tools;
        }

        #endregion
    }
}

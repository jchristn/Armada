namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;

    /// <summary>
    /// Parses user-supplied runtime names (CLI options, settings values) into <see cref="AgentRuntimeEnum"/>. Accepts
    /// the enum names and a fixed alias table, case-insensitively; anything else is an error rather than a silent
    /// fallback to a default runtime.
    /// </summary>
    public static class AgentRuntimeParser
    {
        #region Private-Members

        private static readonly Dictionary<string, AgentRuntimeEnum> _Aliases = BuildAliases();

        #endregion

        #region Public-Members

        /// <summary>
        /// Accepted spellings, for help text and error messages.
        /// </summary>
        public static string AcceptedValues
        {
            get => "claude, codex, gemini, cursor, mux, opencode, api (ApiEndpoint), custom";
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Parse a runtime name.
        /// </summary>
        /// <param name="value">Runtime name, for example "claude", "ClaudeCode", "opencode", or "api-endpoint".</param>
        /// <param name="runtime">Parsed runtime when successful.</param>
        /// <returns>True when <paramref name="value"/> names a known runtime.</returns>
        public static bool TryParse(string? value, out AgentRuntimeEnum runtime)
        {
            runtime = AgentRuntimeEnum.ClaudeCode;
            if (String.IsNullOrWhiteSpace(value)) return false;
            return _Aliases.TryGetValue(value.Trim(), out runtime);
        }

        /// <summary>
        /// Error text for a value that <see cref="TryParse(string?, out AgentRuntimeEnum)"/> rejected.
        /// </summary>
        /// <param name="value">Rejected value.</param>
        /// <returns>Error text naming the accepted values.</returns>
        public static string DescribeInvalid(string? value)
        {
            return "Unknown runtime '" + (value ?? String.Empty) + "'. Use one of: " + AcceptedValues + ".";
        }

        #endregion

        #region Private-Methods

        private static Dictionary<string, AgentRuntimeEnum> BuildAliases()
        {
            Dictionary<string, AgentRuntimeEnum> aliases = new Dictionary<string, AgentRuntimeEnum>(StringComparer.OrdinalIgnoreCase);
            foreach (AgentRuntimeEnum runtime in Enum.GetValues<AgentRuntimeEnum>())
            {
                aliases[runtime.ToString()] = runtime;
            }

            aliases["claude"] = AgentRuntimeEnum.ClaudeCode;
            aliases["claude-code"] = AgentRuntimeEnum.ClaudeCode;
            aliases["open-code"] = AgentRuntimeEnum.OpenCode;
            aliases["api"] = AgentRuntimeEnum.ApiEndpoint;
            aliases["api-endpoint"] = AgentRuntimeEnum.ApiEndpoint;
            return aliases;
        }

        #endregion
    }
}

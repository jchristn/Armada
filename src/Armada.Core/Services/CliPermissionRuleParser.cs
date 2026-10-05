namespace Armada.Core.Services
{
    using System;
    using System.Text.RegularExpressions;
    using Armada.Core.Models;

    /// <summary>
    /// Parses CLI permission rules written in Claude Code permission rule syntax: a tool name (<c>Bash</c>,
    /// <c>WebFetch</c>, <c>Edit</c>, <c>mcp__server__tool</c>, <c>mcp__server</c>, or <c>mcp__server__*</c>) optionally
    /// followed by a specifier in parentheses (<c>Bash(git status:*)</c>, <c>Bash(npm run *)</c>,
    /// <c>WebFetch(domain:example.com)</c>, <c>Edit(//repo/src/**)</c>). <c>Tool(*)</c> is the same as the bare name.
    /// </summary>
    public static class CliPermissionRuleParser
    {
        #region Public-Members

        /// <summary>
        /// Maximum rule length.
        /// </summary>
        public const int MaxLength = 1000;

        #endregion

        #region Private-Members

        private static readonly Regex _ToolName = new Regex("^[A-Za-z][A-Za-z0-9_.-]*(?:__\\*)?$", RegexOptions.Compiled);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Parse a rule.
        /// </summary>
        /// <param name="rule">Rule text.</param>
        /// <returns>The parsed rule.</returns>
        /// <exception cref="ArgumentException">Thrown when the rule is empty, too long, or malformed.</exception>
        public static CliPermissionRulePattern Parse(string? rule)
        {
            if (String.IsNullOrWhiteSpace(rule)) throw new ArgumentException("A rule is required.", nameof(rule));
            string text = rule!.Trim();
            if (text.Length > MaxLength) throw new ArgumentException("A rule may not exceed " + MaxLength + " characters.", nameof(rule));
            foreach (char c in text)
            {
                if (c == '\n' || c == '\r') throw new ArgumentException("A rule must be a single line.", nameof(rule));
            }

            string toolName = text;
            string? specifier = null;
            int open = text.IndexOf('(');
            if (open >= 0)
            {
                if (text[text.Length - 1] != ')') throw new ArgumentException("A rule with a specifier must end with ')': " + text, nameof(rule));
                toolName = text.Substring(0, open).Trim();
                specifier = text.Substring(open + 1, text.Length - open - 2).Trim();
                if (specifier.Length == 0) throw new ArgumentException("The specifier in parentheses is empty: " + text, nameof(rule));
                if (specifier == "*") specifier = null;
            }

            if (!_ToolName.IsMatch(toolName)) throw new ArgumentException("Invalid tool name in rule: " + text, nameof(rule));
            bool serverWildcard = toolName.EndsWith("__*", StringComparison.Ordinal);
            if (serverWildcard && !toolName.StartsWith("mcp__", StringComparison.Ordinal))
                throw new ArgumentException("A '*' tool name wildcard is only valid as mcp__server__*: " + text, nameof(rule));
            if (serverWildcard && specifier != null)
                throw new ArgumentException("An mcp__server__* rule cannot have a specifier: " + text, nameof(rule));

            CliPermissionRulePattern pattern = new CliPermissionRulePattern();
            pattern.ToolName = toolName;
            pattern.Specifier = specifier;
            pattern.Raw = specifier == null ? toolName : toolName + "(" + specifier + ")";
            return pattern;
        }

        /// <summary>
        /// Try to parse a rule.
        /// </summary>
        /// <param name="rule">Rule text.</param>
        /// <param name="pattern">The parsed rule, or null.</param>
        /// <returns>True when the rule is valid.</returns>
        public static bool TryParse(string? rule, out CliPermissionRulePattern? pattern)
        {
            try
            {
                pattern = Parse(rule);
                return true;
            }
            catch (ArgumentException)
            {
                pattern = null;
                return false;
            }
        }

        #endregion
    }
}

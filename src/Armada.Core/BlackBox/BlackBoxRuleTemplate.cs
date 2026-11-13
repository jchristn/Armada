namespace Armada.Core.BlackBox
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Text.RegularExpressions;
    using Armada.Core.Models;

    /// <summary>
    /// Renders black box rule text. A template holds <c>{name}</c> placeholders, each filled from the record through
    /// <see cref="BlackBoxRulePlaceholders"/>, or <c>{name|fallback}</c> to use the fallback text when the record lacks
    /// the value. A placeholder with no value and no fallback makes the whole template unrenderable (null), so a rule
    /// never emits broken text. Pure and thread-safe.
    /// </summary>
    public static class BlackBoxRuleTemplate
    {
        #region Private-Members

        private static readonly Regex _Placeholder = new Regex(@"\{([A-Za-z][A-Za-z0-9:.]*)(?:\|([^{}]*))?\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Render a template against a record.
        /// </summary>
        /// <param name="template">Template, or null.</param>
        /// <param name="record">Record.</param>
        /// <param name="missing">Receives the names of placeholders that had no value and no fallback (may be null).</param>
        /// <returns>The text; null when the template is null or a required placeholder is missing.</returns>
        /// <exception cref="ArgumentNullException">Thrown when record is null.</exception>
        public static string? Render(string? template, BlackBoxRecord record, ICollection<string>? missing = null)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (template == null) return null;
            bool ok = true;
            string result = _Placeholder.Replace(template, match =>
            {
                string name = match.Groups[1].Value;
                string? value = BlackBoxRulePlaceholders.Resolve(record, name);
                if (!String.IsNullOrEmpty(value)) return value!;
                if (match.Groups[2].Success) return match.Groups[2].Value;
                ok = false;
                missing?.Add(name);
                return String.Empty;
            });
            return ok ? result : null;
        }

        /// <summary>
        /// Names of every placeholder in a template, in order (duplicates kept).
        /// </summary>
        /// <param name="template">Template, or null.</param>
        /// <returns>Placeholder names.</returns>
        public static List<string> Placeholders(string? template)
        {
            List<string> names = new List<string>();
            if (String.IsNullOrEmpty(template)) return names;
            foreach (Match m in _Placeholder.Matches(template)) names.Add(m.Groups[1].Value);
            return names;
        }

        /// <summary>
        /// Render every value of an argument template map; null when any value cannot be rendered.
        /// </summary>
        /// <param name="arguments">Argument templates.</param>
        /// <param name="record">Record.</param>
        /// <param name="missing">Receives missing placeholder names (may be null).</param>
        /// <returns>The rendered arguments, or null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static Dictionary<string, string>? RenderArguments(Dictionary<string, string> arguments, BlackBoxRecord record, ICollection<string>? missing = null)
        {
            if (arguments == null) throw new ArgumentNullException(nameof(arguments));
            if (record == null) throw new ArgumentNullException(nameof(record));
            Dictionary<string, string> rendered = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> pair in arguments)
            {
                string? value = Render(pair.Value, record, missing);
                if (value == null) return null;
                rendered[pair.Key] = value;
            }
            return rendered;
        }

        /// <summary>
        /// Format an action as an MCP call string, for example <c>retry_landing {"missionId": "msn_x"}</c>.
        /// </summary>
        /// <param name="action">Action.</param>
        /// <returns>The call string.</returns>
        /// <exception cref="ArgumentNullException">Thrown when action is null.</exception>
        public static string McpCommand(BlackBoxAction action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            StringBuilder sb = new StringBuilder(action.Tool).Append(" {");
            bool first = true;
            foreach (KeyValuePair<string, string> pair in action.Arguments)
            {
                if (!first) sb.Append(", ");
                first = false;
                sb.Append('"').Append(pair.Key).Append("\": ").Append(JsonLiteral(pair.Value));
            }
            return sb.Append('}').ToString();
        }

        /// <summary>
        /// The JSON literal an action argument becomes when the action runs: true, false and whole numbers become JSON
        /// booleans and numbers; everything else is a JSON string.
        /// </summary>
        /// <param name="value">Argument value.</param>
        /// <returns>The JSON literal text.</returns>
        public static string JsonLiteral(string? value)
        {
            if (value == null) return "null";
            if (value == "true" || value == "false") return value;
            if (value.Length > 0 && value.Length < 10 && Regex.IsMatch(value, "^-?[0-9]+$")) return value;
            return System.Text.Json.JsonSerializer.Serialize(value);
        }

        #endregion
    }
}

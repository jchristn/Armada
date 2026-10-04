namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using Armada.Core.Models;

    /// <summary>
    /// Renders fleet action command and prompt templates against a fixed variable set. Rendering is a single
    /// regular-expression pass with no code execution: a substituted value is never scanned again, so a value that
    /// itself contains "{{" is emitted verbatim. Variable names are matched case-insensitively and may carry
    /// surrounding whitespace inside the braces. Any other {{name}} is an error that names the variable.
    /// Thread-safe: the class holds no mutable state.
    /// </summary>
    public static class FleetActionTemplateRenderer
    {
        #region Public-Members

        /// <summary>
        /// {{vessel.name}}: the vessel's display name.
        /// </summary>
        public static readonly string VesselNameVariable = "vessel.name";

        /// <summary>
        /// {{vessel.id}}: the vessel identifier (vsl_ prefix).
        /// </summary>
        public static readonly string VesselIdVariable = "vessel.id";

        /// <summary>
        /// {{vessel.defaultBranch}}: the vessel's default branch.
        /// </summary>
        public static readonly string DefaultBranchVariable = "vessel.defaultBranch";

        /// <summary>
        /// {{vessel.workingDirectory}}: the vessel's working directory, or empty.
        /// </summary>
        public static readonly string WorkingDirectoryVariable = "vessel.workingDirectory";

        /// <summary>
        /// {{vessel.buildCommand}}: the vessel's definition-of-done build command, or empty. A Command target whose
        /// template references it while it is empty is skipped with reason NoBuildCommand.
        /// </summary>
        public static readonly string BuildCommandVariable = "vessel.buildCommand";

        /// <summary>
        /// {{health.summary}}: a plain-text list of the vessel's non-passing health findings and outdated or
        /// vulnerable dependencies.
        /// </summary>
        public static readonly string HealthSummaryVariable = "health.summary";

        /// <summary>
        /// Every supported variable name, in documentation order.
        /// </summary>
        public static IReadOnlyList<string> SupportedVariables
        {
            get
            {
                return new List<string>
                {
                    VesselNameVariable,
                    VesselIdVariable,
                    DefaultBranchVariable,
                    WorkingDirectoryVariable,
                    BuildCommandVariable,
                    HealthSummaryVariable
                };
            }
        }

        #endregion

        #region Private-Members

        private static readonly Regex _VariablePattern = new Regex(@"\{\{\s*([^{}]*?)\s*\}\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        #endregion

        #region Public-Methods

        /// <summary>
        /// List the distinct variables a template references, normalized to their canonical spelling for known
        /// variables and trimmed for unknown ones. Returns an empty list for a null or empty template.
        /// </summary>
        /// <param name="template">Template text.</param>
        /// <returns>Distinct referenced variable names.</returns>
        public static List<string> GetReferencedVariables(string? template)
        {
            List<string> names = new List<string>();
            if (String.IsNullOrEmpty(template)) return names;

            foreach (Match match in _VariablePattern.Matches(template))
            {
                string raw = match.Groups[1].Value.Trim();
                string name = Canonicalize(raw) ?? raw;
                if (!names.Contains(name, StringComparer.Ordinal)) names.Add(name);
            }

            return names;
        }

        /// <summary>
        /// Whether a template references the given variable.
        /// </summary>
        /// <param name="template">Template text.</param>
        /// <param name="variableName">Canonical variable name, for example <see cref="HealthSummaryVariable"/>.</param>
        /// <returns>True when referenced.</returns>
        public static bool References(string? template, string variableName)
        {
            if (String.IsNullOrEmpty(variableName)) return false;
            return GetReferencedVariables(template).Contains(variableName, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Validate that a template references only supported variables. A null or empty template is valid.
        /// </summary>
        /// <param name="template">Template text.</param>
        /// <exception cref="FleetActionTemplateException">Thrown for the first unknown variable.</exception>
        public static void Validate(string? template)
        {
            if (String.IsNullOrEmpty(template)) return;
            foreach (Match match in _VariablePattern.Matches(template))
            {
                string raw = match.Groups[1].Value.Trim();
                if (Canonicalize(raw) == null) throw new FleetActionTemplateException(raw);
            }
        }

        /// <summary>
        /// Render a template. Values are substituted in a single pass and never re-expanded.
        /// </summary>
        /// <param name="template">Template text; null renders as empty.</param>
        /// <param name="context">Values to substitute.</param>
        /// <returns>Rendered text.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is null.</exception>
        /// <exception cref="FleetActionTemplateException">Thrown for an unknown variable.</exception>
        public static string Render(string? template, FleetActionTemplateContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (String.IsNullOrEmpty(template)) return String.Empty;

            Validate(template);
            return _VariablePattern.Replace(template, match =>
            {
                string name = Canonicalize(match.Groups[1].Value.Trim())!;
                return Resolve(name, context);
            });
        }

        #endregion

        #region Private-Methods

        private static string? Canonicalize(string raw)
        {
            foreach (string known in SupportedVariables)
            {
                if (String.Equals(known, raw, StringComparison.OrdinalIgnoreCase)) return known;
            }

            return null;
        }

        private static string Resolve(string name, FleetActionTemplateContext context)
        {
            if (name == VesselNameVariable) return context.VesselName;
            if (name == VesselIdVariable) return context.VesselId;
            if (name == DefaultBranchVariable) return context.DefaultBranch;
            if (name == WorkingDirectoryVariable) return context.WorkingDirectory;
            if (name == BuildCommandVariable) return context.BuildCommand;
            if (name == HealthSummaryVariable) return context.HealthSummary;
            throw new FleetActionTemplateException(name);
        }

        #endregion
    }
}

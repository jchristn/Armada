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
        /// {{vessel.buildCommand}}: the vessel's definition-of-done build command, else the repository's build script or
        /// the catalog default for its detected ecosystem, or empty. A target whose template references it while it is
        /// empty is skipped with reason NoBuildCommand.
        /// </summary>
        public static readonly string BuildCommandVariable = "vessel.buildCommand";

        /// <summary>
        /// {{vessel.ecosystems}}: detected ecosystem names, comma-separated, or empty when none were detected.
        /// </summary>
        public static readonly string EcosystemsVariable = "vessel.ecosystems";

        /// <summary>
        /// {{vessel.testCommand}}: the resolved Test command (vessel override, repo script, then ecosystem default), or empty.
        /// </summary>
        public static readonly string TestCommandVariable = "vessel.testCommand";

        /// <summary>
        /// {{vessel.lintCommand}}: the resolved Lint command (vessel override, repo script, then ecosystem default), or empty.
        /// </summary>
        public static readonly string LintCommandVariable = "vessel.lintCommand";

        /// <summary>
        /// {{vessel.auditCommand}}: the resolved Audit command (vessel override, repo script, ecosystem default, then osv-scanner).
        /// </summary>
        public static readonly string AuditCommandVariable = "vessel.auditCommand";

        /// <summary>
        /// The vessel's resolved outdated-dependency report command (catalog default for the detected ecosystem).
        /// </summary>
        public static readonly string OutdatedCommandVariable = "vessel.outdatedCommand";

        /// <summary>
        /// The vessel's resolved license report command (catalog default for the detected ecosystem).
        /// </summary>
        public static readonly string LicensesCommandVariable = "vessel.licensesCommand";

        /// <summary>
        /// The run-time variables that hold a resolved ecosystem command. A target whose template references one that
        /// resolves to nothing is skipped (NoBuildCommand for the build command, NoResolvedCommand otherwise).
        /// </summary>
        public static IReadOnlyList<string> ResolvedCommandVariables
        {
            get
            {
                return new List<string>
                {
                    BuildCommandVariable,
                    TestCommandVariable,
                    LintCommandVariable,
                    AuditCommandVariable,
                    OutdatedCommandVariable,
                    LicensesCommandVariable
                };
            }
        }

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
                    EcosystemsVariable,
                    TestCommandVariable,
                    LintCommandVariable,
                    AuditCommandVariable,
                    OutdatedCommandVariable,
                    LicensesCommandVariable,
                    "scan.summary",
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
        /// <param name="inputNames">Declared runtime input names.</param>
        public static void Validate(string? template, IEnumerable<string>? inputNames = null)
        {
            if (String.IsNullOrEmpty(template)) return;
            foreach (Match match in _VariablePattern.Matches(template))
            {
                string raw = match.Groups[1].Value.Trim();
                if (Canonicalize(raw) == null && !(raw.StartsWith("input.", StringComparison.OrdinalIgnoreCase) && (inputNames ?? Enumerable.Empty<string>()).Contains(raw.Substring(6), StringComparer.OrdinalIgnoreCase))) throw new FleetActionTemplateException(raw);
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

            Validate(template, context.InputValues.Keys);
            return _VariablePattern.Replace(template, match =>
            {
                string raw = match.Groups[1].Value.Trim();
                if (raw.StartsWith("input.", StringComparison.OrdinalIgnoreCase)) return context.InputValues.First(p => String.Equals(p.Key, raw.Substring(6), StringComparison.OrdinalIgnoreCase)).Value;
                string name = Canonicalize(raw)!;
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
            if (name == "scan.summary") return context.ScanSummary;
            if (name == VesselNameVariable) return context.VesselName;
            if (name == VesselIdVariable) return context.VesselId;
            if (name == DefaultBranchVariable) return context.DefaultBranch;
            if (name == WorkingDirectoryVariable) return context.WorkingDirectory;
            if (name == BuildCommandVariable) return context.BuildCommand;
            if (name == EcosystemsVariable) return context.Ecosystems;
            if (name == TestCommandVariable) return context.TestCommand;
            if (name == LintCommandVariable) return context.LintCommand;
            if (name == AuditCommandVariable) return context.AuditCommand;
            if (name == OutdatedCommandVariable) return context.OutdatedCommand;
            if (name == LicensesCommandVariable) return context.LicensesCommand;
            if (name == HealthSummaryVariable) return context.HealthSummary;
            throw new FleetActionTemplateException(name);
        }

        #endregion
    }
}

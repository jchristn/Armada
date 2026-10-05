namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;

    /// <summary>
    /// The one label every surface uses for a deployment approval: the environment name first and the deployment
    /// title second ("Deploy to production: Release 2.3 hotfix"). The dashboard (<c>deploymentApprovalLabel.ts</c>)
    /// mirrors these templates and fallbacks; the templates are keys in the shared i18n catalog. When the title is
    /// missing the label names the environment alone ("Deploy to production"); when the environment name is missing it
    /// leads with the title ("Deploy: Release 2.3 hotfix"), or the deployment id when the title is missing too.
    /// </summary>
    public static class DeploymentApprovalLabel
    {
        #region Public-Members

        /// <summary>
        /// Template when both the environment name and the title are known.
        /// </summary>
        public const string EnvironmentAndTitleTemplate = "Deploy to {{environment}}: {{title}}";

        /// <summary>
        /// Template when only the environment name is known.
        /// </summary>
        public const string EnvironmentOnlyTemplate = "Deploy to {{environment}}";

        /// <summary>
        /// Template when only the title (or, failing that, the id) is known.
        /// </summary>
        public const string TitleOnlyTemplate = "Deploy: {{title}}";

        /// <summary>
        /// Text when nothing identifies the deployment.
        /// </summary>
        public const string UnnamedText = "Deployment";

        /// <summary>
        /// Placeholder name for the environment name.
        /// </summary>
        public const string EnvironmentPlaceholder = "environment";

        /// <summary>
        /// Placeholder name for the deployment title.
        /// </summary>
        public const string TitlePlaceholder = "title";

        #endregion

        #region Public-Methods

        /// <summary>
        /// English label (server-side text such as the inbox item title and MCP output).
        /// </summary>
        /// <param name="environmentName">Environment name, or null.</param>
        /// <param name="deploymentTitle">Deployment title, or null.</param>
        /// <param name="deploymentId">Deployment id, used when both the environment name and the title are missing.</param>
        /// <returns>Label.</returns>
        public static string Format(string? environmentName, string? deploymentTitle, string? deploymentId)
        {
            return Format(environmentName, deploymentTitle, deploymentId, Interpolate);
        }

        /// <summary>
        /// Label through a localizer: <paramref name="localize"/> receives one of the templates (or
        /// <see cref="UnnamedText"/>) and its placeholder values, and returns the rendered text.
        /// </summary>
        /// <param name="environmentName">Environment name, or null.</param>
        /// <param name="deploymentTitle">Deployment title, or null.</param>
        /// <param name="deploymentId">Deployment id, used when both the environment name and the title are missing.</param>
        /// <param name="localize">Template renderer (for example the TUI's <c>Loc.T</c>).</param>
        /// <returns>Label.</returns>
        public static string Format(string? environmentName, string? deploymentTitle, string? deploymentId, Func<string, IDictionary<string, object?>, string> localize)
        {
            if (localize == null) throw new ArgumentNullException(nameof(localize));
            string? environment = Clean(environmentName);
            string? title = Clean(deploymentTitle);
            Dictionary<string, object?> args = new Dictionary<string, object?>(StringComparer.Ordinal);
            if (environment != null) args[EnvironmentPlaceholder] = environment;

            if (environment != null && title != null)
            {
                args[TitlePlaceholder] = title;
                return localize(EnvironmentAndTitleTemplate, args);
            }

            if (environment != null) return localize(EnvironmentOnlyTemplate, args);

            string? fallback = title ?? Clean(deploymentId);
            if (fallback != null)
            {
                args[TitlePlaceholder] = fallback;
                return localize(TitleOnlyTemplate, args);
            }

            return localize(UnnamedText, args);
        }

        #endregion

        #region Private-Methods

        private static string? Clean(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static string Interpolate(string template, IDictionary<string, object?> args)
        {
            string result = template;
            foreach (KeyValuePair<string, object?> kvp in args)
                result = result.Replace("{{" + kvp.Key + "}}", kvp.Value == null ? "" : Convert.ToString(kvp.Value, CultureInfo.InvariantCulture));
            return result;
        }

        #endregion
    }
}

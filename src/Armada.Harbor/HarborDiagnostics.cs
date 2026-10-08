namespace Armada.Harbor
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;
    using System.Runtime.InteropServices;
    using System.Text;
    using Armada.Core.Hosting;

    /// <summary>
    /// Builds the plain-text diagnostics report (versions, paths, link state, recent activity) that Help > Copy
    /// Diagnostics puts on the clipboard for a bug report. Credentials are reported only as configured or not.
    /// </summary>
    public static class HarborDiagnostics
    {
        #region Public-Methods

        /// <summary>
        /// Harbor's version: the informational version without the source revision suffix.
        /// </summary>
        /// <returns>Version text.</returns>
        public static string Version()
        {
            Assembly assembly = typeof(HarborDiagnostics).Assembly;
            string? informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!String.IsNullOrWhiteSpace(informational))
            {
                int plus = informational!.IndexOf('+');
                return plus > 0 ? informational.Substring(0, plus) : informational;
            }

            return assembly.GetName().Version?.ToString() ?? "unknown";
        }

        /// <summary>
        /// Build the report.
        /// </summary>
        /// <param name="settings">Harbor settings.</param>
        /// <param name="linkState">Current link state.</param>
        /// <param name="mcpUrl">MCP URL from the last handshake, or null.</param>
        /// <param name="admiral">Local Admiral paths, or null when not resolved yet.</param>
        /// <param name="recentActivity">Most recent activity log lines, oldest first.</param>
        /// <returns>Report text.</returns>
        public static string Build(HarborAppSettings settings, HarborLinkStateEnum linkState, string? mcpUrl, LocalAdmiralInfo? admiral, IReadOnlyList<string> recentActivity)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Armada Harbor " + Version());
            sb.AppendLine("OS:               " + RuntimeInformation.OSDescription + " (" + RuntimeInformation.OSArchitecture + ")");
            sb.AppendLine(".NET:             " + RuntimeInformation.FrameworkDescription);
            sb.AppendLine("Generated:        " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + "Z");
            sb.AppendLine();
            sb.AppendLine("Harbor:           " + settings.Name + " (" + settings.HarborId + ")");
            sb.AppendLine("Harbor settings:  " + HarborAppSettings.DefaultPath());
            sb.AppendLine("Link URL:         " + settings.ServerLinkUrl);
            sb.AppendLine("Dashboard URL:    " + settings.DashboardUrl);
            sb.AppendLine("Credential:       " + (String.IsNullOrWhiteSpace(settings.AccessKey) ? "none" : "configured"));
            sb.AppendLine("Tenant:           " + (String.IsNullOrWhiteSpace(settings.TenantId) ? "none" : settings.TenantId));
            sb.AppendLine("Max jobs:         " + settings.MaxConcurrentJobs);
            sb.AppendLine("Link state:       " + linkState);
            sb.AppendLine("MCP URL:          " + (String.IsNullOrEmpty(mcpUrl) ? "-" : mcpUrl));
            sb.AppendLine();

            if (admiral == null)
            {
                sb.AppendLine("Admiral paths:    not resolved");
            }
            else
            {
                sb.AppendLine("Admiral local:    " + (admiral.IsLocal ? "yes" : "no (" + admiral.Reason + ")"));
                sb.AppendLine("Data directory:   " + admiral.DataDirectory + (Directory.Exists(admiral.DataDirectory) ? "" : " (missing)"));
                sb.AppendLine("Settings file:    " + admiral.SettingsFile + (File.Exists(admiral.SettingsFile) ? "" : " (missing)"));
                sb.AppendLine("Log directory:    " + admiral.LogDirectory + (Directory.Exists(admiral.LogDirectory) ? "" : " (missing)"));
                sb.AppendLine("Admiral log:      " + (admiral.FindLatestAdmiralLog() ?? "-"));
            }

            sb.AppendLine();
            sb.AppendLine("Recent activity:");
            if (recentActivity == null || recentActivity.Count == 0) sb.AppendLine("  (none)");
            else foreach (string line in recentActivity) sb.AppendLine("  " + line);

            return sb.ToString();
        }

        #endregion
    }
}

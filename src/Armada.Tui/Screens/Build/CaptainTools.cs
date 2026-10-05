namespace Armada.Tui.Screens.Build
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Modals;
    using Armada.Tui.Screens.Operations;
    using Armada.Tui.Services;

    /// <summary>
    /// The dashboard's Available Tools viewer (CaptainToolViewer): the summary, the Accessible/Unavailable,
    /// Verified/Inferred, runtime, source, reachable, endpoint, and listed-tool badges, the point-in-time note, and the
    /// Configured MCP Servers, Runtime Sources, Runtime Internal Tools, and MCP Tools tables.
    /// </summary>
    public static class CaptainTools
    {
        #region Public-Methods

        /// <summary>
        /// Load a captain's tool access and show it (the viewer opens at once with a loading line).
        /// </summary>
        /// <param name="screen">Owning screen.</param>
        /// <param name="captainId">Captain id.</param>
        /// <param name="captainName">Captain name.</param>
        /// <returns>The viewer.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="screen"/> is null.</exception>
        public static ViewerModal Show(OpsScreen screen, string captainId, string captainName)
        {
            if (screen == null) throw new ArgumentNullException(nameof(screen));
            CaptainToolAccessResult? data = null;
            string? error = null;
            bool loading = true;
            OpsDocumentView view = new OpsDocumentView();
            view.Builder = doc => Build(doc, captainName, loading, error, data);
            ViewerModal modal = new ViewerModal(screen.Tr("Available Tools"), view, screen.Context.Loc, screen.Context.Theme.Current);
            modal.WidthRatio = 0.92;
            modal.HeightRatio = 0.9;
            modal.CopyRequested += (s, e) => screen.Context.Clipboard.Copy(view.PlainText, "Tools");
            screen.Context.Modals.Show(modal);
            screen.Call((c, t) => c.GetCaptainToolsAsync(captainId, t), r =>
            {
                loading = false;
                data = r;
                view.Invalidate();
            }, null, ex =>
            {
                loading = false;
                error = screen.Tr("Failed to load captain tools.");
                view.Invalidate();
            });
            return modal;
        }

        /// <summary>
        /// Build the viewer document.
        /// </summary>
        /// <param name="doc">Document.</param>
        /// <param name="captainName">Captain name.</param>
        /// <param name="loading">True while loading.</param>
        /// <param name="error">Error, or null.</param>
        /// <param name="data">Result, or null.</param>
        /// <returns>The document.</returns>
        public static OpsDocument Build(OpsDocument doc, string captainName, bool loading, string? error, CaptainToolAccessResult? data)
        {
            doc.Text(captainName, doc.Theme.Muted);
            if (loading)
            {
                doc.Note("Loading captain tool access...");
                return doc;
            }

            if (error != null || data == null)
            {
                doc.Text(error ?? "", doc.Theme.Muted);
                return doc;
            }

            List<CaptainToolServerSummary> servers = data.Servers ?? new List<CaptainToolServerSummary>();
            List<CaptainToolSummary> tools = data.Tools ?? new List<CaptainToolSummary>();
            List<CaptainToolServerSummary> mcpServers = servers.Where(s => s.SourceKind == CaptainToolSourceKindEnum.McpServer).ToList();
            List<CaptainToolServerSummary> runtimeSources = servers.Where(s => s.SourceKind != CaptainToolSourceKindEnum.McpServer).ToList();
            List<CaptainToolSummary> internalTools = tools.Where(t => t.SourceKind == CaptainToolSourceKindEnum.RuntimeBuiltIn).ToList();
            List<CaptainToolSummary> mcpTools = tools.Where(t => t.SourceKind == CaptainToolSourceKindEnum.McpServer).ToList();
            int runtimeReported = runtimeSources.Sum(s => Math.Max(0, s.ToolCount));

            doc.Blank();
            doc.Text(data.Summary, doc.Theme.Muted);
            List<string> badges = new List<string>();
            badges.Add("[" + doc.Loc.T(data.ToolsAccessible ? "Accessible" : "Unavailable") + "]");
            badges.Add("[" + doc.Loc.T(data.AvailabilityVerified ? "Verified" : "Inferred") + "]");
            badges.Add("[" + data.Runtime + "]");
            if (data.ConfiguredServerCount > 0) badges.Add("[" + doc.Loc.T("{{count}} sources", LocalizationArgs.Of("count", data.ConfiguredServerCount)) + "]");
            if (data.ReachableServerCount > 0) badges.Add("[" + doc.Loc.T("{{count}} reachable", LocalizationArgs.Of("count", data.ReachableServerCount)) + "]");
            if (!String.IsNullOrEmpty(data.EndpointName)) badges.Add("[" + data.EndpointName + "]");
            if (data.EffectiveToolCount.HasValue) badges.Add("[" + doc.Loc.T("{{count}} listed tools", LocalizationArgs.Of("count", data.EffectiveToolCount.Value)) + "]");
            doc.Text(String.Join(" ", badges), data.ToolsAccessible ? doc.Theme.Success : doc.Theme.Error);
            if (data.ConfiguredServerCount > data.ReachableServerCount)
                doc.Note("This is a point-in-time snapshot. Configured MCP servers that did not respond may simply be offline right now.");

            Servers(doc, "Configured MCP Servers", mcpServers, "No external MCP servers are configured for this captain runtime.");
            if (runtimeSources.Count > 0) Servers(doc, "Runtime Sources", runtimeSources, "No runtime-managed sources were reported for this captain runtime.");
            Tools(doc, "Runtime Internal Tools", internalTools, runtimeReported > 0
                ? doc.Loc.T("This runtime reports {{count}} internal tool(s), but it does not expose individual tool names for every internal source.", LocalizationArgs.Of("count", runtimeReported))
                : doc.Loc.T("No named runtime-internal tools are currently exposed for this captain runtime."));
            Tools(doc, "MCP Tools", mcpTools, mcpServers.Count > 0
                ? doc.Loc.T("No named MCP tools were returned from the captain runtime's reachable MCP servers.")
                : doc.Loc.T("No MCP tool inventory is available for this captain runtime."));
            return doc;
        }

        #endregion

        #region Private-Methods

        private static void Servers(OpsDocument doc, string title, List<CaptainToolServerSummary> servers, string empty)
        {
            doc.Section(title);
            if (servers.Count == 0)
            {
                doc.Note(empty);
                return;
            }

            doc.Table(
                new List<string> { "Source", "Transport", "Endpoint / Target", "Status", "Tools", "Notes" },
                servers.Select(s => (IList<string>)new List<string>
                {
                    s.Name,
                    String.IsNullOrEmpty(s.Transport) ? "-" : s.Transport,
                    Endpoint(s),
                    s.Status,
                    s.ToolCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Notes(doc, s),
                }),
                50);
        }

        private static void Tools(OpsDocument doc, string title, List<CaptainToolSummary> tools, string empty)
        {
            doc.Section(title);
            if (tools.Count == 0)
            {
                doc.Text(empty, doc.Theme.Muted);
                return;
            }

            doc.Table(
                new List<string> { "Tool", "Server / Source", "Description" },
                tools.Select(t => (IList<string>)new List<string>
                {
                    t.Name,
                    String.IsNullOrEmpty(t.RegistrationSource) ? doc.Loc.T("Internal") : t.RegistrationSource,
                    t.Description ?? "",
                }),
                60);
        }

        private static string Endpoint(CaptainToolServerSummary s)
        {
            if (!String.IsNullOrEmpty(s.Url)) return s.Url!;
            if (!String.IsNullOrEmpty(s.Command)) return s.Command!;
            if (!String.IsNullOrEmpty(s.Target)) return s.Target;
            return "-";
        }

        private static string Notes(OpsDocument doc, CaptainToolServerSummary s)
        {
            List<string> notes = new List<string>();
            if (!String.IsNullOrEmpty(s.WorkingDirectory)) notes.Add(doc.Loc.T("WD: {{path}}", LocalizationArgs.Of("path", s.WorkingDirectory)));
            if (s.StartupTimeoutSeconds > 0 || s.ToolTimeoutSeconds > 0)
                notes.Add(doc.Loc.T("{{startup}}s startup / {{tool}}s tool", LocalizationArgs.Of("startup", s.StartupTimeoutSeconds, "tool", s.ToolTimeoutSeconds)));
            if (s.HeaderCount > 0) notes.Add(doc.Loc.T("{{count}} header(s)", LocalizationArgs.Of("count", s.HeaderCount)));
            if (s.EnvironmentVariableCount > 0) notes.Add(doc.Loc.T("{{count}} env var(s)", LocalizationArgs.Of("count", s.EnvironmentVariableCount)));
            if (s.EnabledToolFilterCount > 0) notes.Add(doc.Loc.T("{{count}} allow filter(s)", LocalizationArgs.Of("count", s.EnabledToolFilterCount)));
            if (s.DisabledToolFilterCount > 0) notes.Add(doc.Loc.T("{{count}} deny filter(s)", LocalizationArgs.Of("count", s.DisabledToolFilterCount)));
            if (!String.IsNullOrEmpty(s.ErrorMessage)) notes.Add(s.ErrorMessage!);
            return notes.Count > 0 ? String.Join(" | ", notes) : "-";
        }

        #endregion
    }
}

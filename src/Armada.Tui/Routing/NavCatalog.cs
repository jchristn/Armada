namespace Armada.Tui.Routing
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// The sidebar navigation, mirroring the dashboard's <c>navConfig.tsx</c> exactly (Dashboard, Ask Armada, then the
    /// grouped sections with the same labels and matchers). Thread-safe (immutable).
    /// </summary>
    public static class NavCatalog
    {
        #region Public-Members

        /// <summary>
        /// The Dashboard item.
        /// </summary>
        public static NavItem Dashboard { get; } = new NavItem("/", "Dashboard", "Overview of captains, missions, and voyages", "#", "h");

        /// <summary>
        /// The Ask Armada item (top level, never nested).
        /// </summary>
        public static NavItem AskArmada { get; } = new NavItem("/ask", "Ask Armada", "Ask about fleet state in plain language and drive work from the conversation", "?", "a");

        /// <summary>
        /// The grouped sections. Never null.
        /// </summary>
        public static IReadOnlyList<NavSection> Sections { get; } = BuildSections();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Every destination in sidebar order (Dashboard, Ask Armada, then section items).
        /// </summary>
        /// <returns>Items.</returns>
        public static IReadOnlyList<NavItem> AllItems()
        {
            List<NavItem> items = new List<NavItem> { Dashboard, AskArmada };
            foreach (NavSection section in Sections) items.AddRange(section.Items);
            return items;
        }

        /// <summary>
        /// The item that owns a path: an exact item match first, then the item whose section matches and whose target
        /// prefixes the path, then the section's first item.
        /// </summary>
        /// <param name="path">Path without the query.</param>
        /// <returns>The item, or null.</returns>
        public static NavItem? ItemForPath(string? path)
        {
            if (String.IsNullOrEmpty(path)) return null;
            if (path == "/") return Dashboard;
            if (path!.StartsWith("/ask", StringComparison.OrdinalIgnoreCase)) return AskArmada;
            foreach (NavSection section in Sections)
            {
                NavItem? exact = section.Items.FirstOrDefault(i => path.Equals(i.To, StringComparison.OrdinalIgnoreCase) || path.StartsWith(i.To + "/", StringComparison.OrdinalIgnoreCase));
                if (exact != null) return exact;
            }

            foreach (NavSection section in Sections)
            {
                if (section.Matches(path)) return section.Items.FirstOrDefault(i => SectionOwns(i, path)) ?? section.Items.FirstOrDefault();
            }

            return null;
        }

        #endregion

        #region Private-Methods

        private static bool SectionOwns(NavItem item, string path)
        {
            if (item.To == "/missions") return path.StartsWith("/voyages") || path.StartsWith("/merge-queue");
            if (item.To == "/vessels") return path.StartsWith("/fleets") || path.StartsWith("/workspace");
            if (item.To == "/captains") return path.StartsWith("/docks");
            if (item.To == "/dispatch") return path.StartsWith("/backlog") || path.StartsWith("/objectives");
            if (item.To == "/server") return path.StartsWith("/doctor") || path.StartsWith("/settings") || path.StartsWith("/admin");
            return false;
        }

        private static IReadOnlyList<NavSection> BuildSections()
        {
            List<NavSection> sections = new List<NavSection>();
            sections.Add(new NavSection("operations", "OPERATIONS",
                new[] { "/inbox", "/dispatch", "/fleet-actions", "/planning", "/backlog", "/objectives", "/voyages", "/missions", "/merge-queue" },
                new[]
                {
                    new NavItem("/inbox", "Needs You", "Reviews, failures, and stalls awaiting your attention", "!", "i"),
                    new NavItem("/planning", "Planning", "Plan with a captain, preserve the transcript, and dispatch directly from the session", "P"),
                    new NavItem("/dispatch", "Dispatch", "Send work to vessels; capture and refine backlog on the Backlog tab", ">"),
                    new NavItem("/fleet-actions", "Fleet Actions", "Run a command or mission across many vessels and watch each run", "F", "f"),
                    new NavItem("/missions", "Missions", "Work units, plus Voyages and the full Merge Queue as tabs", "M", "m"),
                }));
            sections.Add(new NavSection("delivery", "DELIVERY",
                new[] { "/delivery", "/checks", "/environments", "/deployments", "/releases", "/incidents", "/runbooks" },
                new[]
                {
                    new NavItem("/delivery", "Delivery", "Deployments, Environments, Releases, Incidents, Checks, and Runbooks as tabs", "D", "d"),
                }));
            sections.Add(new NavSection("fleet", "BUILD",
                new[] { "/fleets", "/vessels", "/workspace", "/captains", "/docks" },
                new[]
                {
                    new NavItem("/vessels", "Vessels", "Repositories grouped by fleet, plus the vessel workspace, on one surface", "V", "v"),
                    new NavItem("/captains", "Captains", "AI coding agents that execute missions", "C", "c"),
                }));
            sections.Add(new NavSection("configuration", "CONFIGURATION",
                new[] { "/configuration", "/workflow-profiles", "/project-profiles", "/skills", "/personas", "/pipelines", "/prompt-templates", "/playbooks" },
                new[]
                {
                    new NavItem("/configuration", "Configuration", "Workflow Profiles, Project Profiles, Skills, Personas, Pipelines, Prompts, and Playbooks as tabs", "=", "o"),
                }));
            sections.Add(new NavSection("activity", "ACTIVITY",
                new[] { "/activity", "/history", "/requests", "/events", "/signals", "/jobs" },
                new[]
                {
                    new NavItem("/activity", "Activity", "One log across requests, events, signals, and history; filter by source type", "~", "y"),
                    new NavItem("/jobs", "Jobs", "Background jobs and their status", "J", "j"),
                }));
            sections.Add(new NavSection("system", "SYSTEM",
                new[] { "/server", "/doctor", "/settings", "/admin", "/api-explorer" },
                new[]
                {
                    new NavItem("/api-explorer", "API Explorer", "Browse the live OpenAPI document, execute requests, and inspect responses", "A", "x"),
                    new NavItem("/server", "Settings", "Server settings, diagnostics, and tenant/user/credential administration", "S", "s"),
                }));
            return sections.AsReadOnly();
        }

        #endregion
    }
}

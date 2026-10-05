namespace Armada.Tui.Modals
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using Armada.Tui.Input;
    using Armada.Tui.Routing;
    using Armada.Tui.Services;
    using Armada.Tui.Text;
    using Armada.Tui.Theming;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;

    /// <summary>
    /// The command palette (<c>Ctrl+K</c>, W1.9): fuzzy search over every route and hub tab (same labels and sections
    /// as the sidebar), every visible command (global and current-screen, with key bindings shown), and entity jump
    /// (typing an id such as <c>msn_...</c> or <c>vsl_...</c> offers "Open"). Enter runs, Tab fills the query with the
    /// highlighted title, Esc closes. Closes with the chosen <see cref="PaletteEntry"/>. Not thread-safe.
    /// </summary>
    public class CommandPaletteModal : ArmadaDialog
    {
        #region Public-Members

        /// <summary>
        /// Candidate list.
        /// </summary>
        public FilterList<PaletteEntry> List { get; }

        /// <summary>
        /// Entity id prefixes and the route each opens.
        /// </summary>
        public static IReadOnlyDictionary<string, string> EntityRoutes { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["msn_"] = "/missions/", ["vyg_"] = "/voyages/", ["vsl_"] = "/vessels/", ["cpt_"] = "/captains/",
            ["flt_"] = "/fleets/", ["dck_"] = "/docks/", ["sig_"] = "/signals/", ["evt_"] = "/events/",
            ["mrg_"] = "/merge-queue/", ["far_"] = "/fleet-actions/runs/", ["ath_"] = "/ask/", ["obj_"] = "/backlog/",
            ["dpl_"] = "/deployments/", ["env_"] = "/environments/", ["rel_"] = "/releases/", ["inc_"] = "/incidents/",
            ["chk_"] = "/checks/", ["rbk_"] = "/runbooks/", ["rbd_"] = "/runbooks/", ["req_"] = "/requests/",
            ["wfp_"] = "/workflow-profiles/", ["ppf_"] = "/project-profiles/", ["pbk_"] = "/playbooks/",
            ["skl_"] = "/skills/", ["psn_"] = "/planning/"
        };

        #endregion

        #region Private-Members

        private static readonly Regex _Id = new Regex(@"^([a-z]{3}_)[A-Za-z0-9_\-]+$", RegexOptions.Compiled);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="commands">Command registry.</param>
        /// <param name="navigate">Navigation callback.</param>
        /// <param name="localizer">Localizer.</param>
        /// <param name="theme">Palette.</param>
        /// <param name="routeVisible">Filters routes (role gating), or null for all.</param>
        public CommandPaletteModal(CommandService commands, Action<string> navigate, ITextLocalizer localizer, ArmadaTheme theme, Func<string, bool>? routeVisible = null)
            : base("Command palette", localizer, theme)
        {
            if (commands == null) throw new ArgumentNullException(nameof(commands));
            if (navigate == null) throw new ArgumentNullException(nameof(navigate));
            List<SelectOption<PaletteEntry>> options = new List<SelectOption<PaletteEntry>>();
            foreach (SelectOption<PaletteEntry> o in BuildRoutes(navigate, routeVisible)) options.Add(o);
            foreach (ArmadaCommand c in commands.All().Where(c => c.Visible && c.InPalette && c.Menu != CommandMenuEnum.Go))
            {
                ArmadaCommand captured = c;
                PaletteEntry entry = new PaletteEntry("command", T(c.Title), c.KeyLabel, () => commands.Run(captured, Armada.Tui.Services.TuiTelemetry.SourcePalette));
                SelectOption<PaletteEntry> option = new SelectOption<PaletteEntry>(entry, entry.Title, String.IsNullOrEmpty(c.KeyLabel) ? T(c.Menu.ToString()) : c.KeyLabel);
                option.Enabled = c.Enabled;
                options.Add(option);
            }

            List = new FilterList<PaletteEntry>(options);
            List.Dynamic = query => EntityJump(query, navigate);
            FooterHint = " Enter " + T("Run") + "  Tab " + T("Fill") + "  Esc " + T("Close") + " ";
            MinContentWidth = 50;
            MaxContentWidth = 90;
            MinContentHeight = 10;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (HandleDismiss(key, null)) return true;
            if (key.Code == KeyCode.Enter)
            {
                SelectOption<PaletteEntry>? current = List.Current;
                if (current != null && current.Enabled) RequestClose(current.Value);
                return true;
            }

            if (key.Code == KeyCode.Tab)
            {
                SelectOption<PaletteEntry>? current = List.Current;
                if (current != null) List.Query = current.Label;
                return true;
            }

            return List.HandleKey(key, 12);
        }

        /// <inheritdoc />
        public override bool HandlePaste(string text)
        {
            List.Paste(text.Trim());
            return true;
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override int MeasureContentWidth(int availableWidth)
        {
            return Math.Min(availableWidth, 80);
        }

        /// <inheritdoc />
        protected override int MeasureContentHeight(int contentWidth)
        {
            return 16;
        }

        /// <inheritdoc />
        protected override void RenderContent(ISurface content)
        {
            int width = content.Size.Width;
            SurfaceText.Draw(content, 0, 0, "> " + List.Query + "_", On(Theme.Accent), width);
            string count = List.Visible.Count + "";
            SurfaceText.Draw(content, Math.Max(0, width - count.Length), 0, count, Dim(), width);
            if (List.Visible.Count == 0)
            {
                SurfaceText.Draw(content, 0, 2, T("No matches."), Dim(), width);
                return;
            }

            List.Render(content, 2, content.Size.Height - 2, Theme, Body());
        }

        #endregion

        #region Private-Methods

        private IEnumerable<SelectOption<PaletteEntry>> BuildRoutes(Action<string> navigate, Func<string, bool>? visible)
        {
            List<SelectOption<PaletteEntry>> list = new List<SelectOption<PaletteEntry>>();
            foreach (NavItem item in NavCatalog.AllItems())
            {
                string to = item.To;
                NavSection? section = NavCatalog.Sections.FirstOrDefault(s => s.Items.Contains(item));
                string detail = (item.GoKey != null ? "g " + item.GoKey + "  " : "") + (section != null ? T(section.Label) : "");
                PaletteEntry entry = new PaletteEntry("route", T(item.Label), detail, () => navigate(to));
                list.Add(new SelectOption<PaletteEntry>(entry, entry.Title, detail));
            }

            foreach (RouteDefinition route in RouteTable.All.Where(r => r.Hub != null && r.DefaultTab == null))
            {
                foreach (HubTab tab in route.Hub!.Tabs)
                {
                    string target = route.Pattern + "?" + route.Hub.QueryParam + "=" + tab.Key;
                    if (visible != null && !visible(target)) continue;
                    if (tab.Key == route.Hub.DefaultTab && T(tab.Label) == T(route.Title)) continue;
                    string title = T(route.Title) + ": " + T(tab.Label);
                    PaletteEntry entry = new PaletteEntry("route", title, route.Pattern, () => navigate(target));
                    list.Add(new SelectOption<PaletteEntry>(entry, title, route.Pattern));
                }
            }

            foreach (RouteDefinition route in RouteTable.All.Where(r => r.IsExtension && !ReferenceEquals(r, RouteTable.NotFound)))
            {
                string target = route.Pattern;
                PaletteEntry entry = new PaletteEntry("route", T(route.Title), target, () => navigate(target));
                list.Add(new SelectOption<PaletteEntry>(entry, entry.Title, target));
            }

            return list;
        }

        private IEnumerable<SelectOption<PaletteEntry>> EntityJump(string query, Action<string> navigate)
        {
            string q = (query ?? "").Trim();
            Match m = _Id.Match(q);
            if (!m.Success || !EntityRoutes.TryGetValue(m.Groups[1].Value, out string? prefix)) return Enumerable.Empty<SelectOption<PaletteEntry>>();
            string target = prefix + q;
            PaletteEntry entry = new PaletteEntry("entity", T("Open") + " " + q, target, () => navigate(target));
            return new List<SelectOption<PaletteEntry>> { new SelectOption<PaletteEntry>(entry, entry.Title, target) };
        }

        #endregion
    }
}

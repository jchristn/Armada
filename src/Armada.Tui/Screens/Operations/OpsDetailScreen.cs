namespace Armada.Tui.Screens.Operations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Tui.Input;
    using Armada.Tui.Routing;
    using Armada.Tui.Text;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Widgets;
    using Button = Armada.Tui.Widgets.Button;
    using ButtonRow = Armada.Tui.Widgets.ButtonRow;
    using TabStrip = Armada.Tui.Widgets.TabStrip;

    /// <summary>
    /// The standard OPERATIONS detail page: a heading with a status, a subtitle line, header action buttons, and
    /// tabbed panels (fields, Markdown, tables, logs). <c>[</c>/<c>]</c> and <c>Alt+1..9</c> switch panels, <c>.</c>
    /// opens the full action menu (header and menu actions, like the dashboard's More menu), single keys run actions,
    /// and every action is a palette and Actions-menu command. Derived screens add actions and tabs in their
    /// constructor and call <see cref="Load"/>. Not thread-safe.
    /// </summary>
    public abstract class OpsDetailScreen : OpsScreen
    {
        #region Public-Members

        /// <summary>
        /// Header action buttons.
        /// </summary>
        public ButtonRow ActionBar { get; } = new ButtonRow();

        /// <summary>
        /// Panel tabs.
        /// </summary>
        public TabStrip Tabs { get; } = new TabStrip();

        /// <summary>
        /// Actions: header buttons (Toolbar true) and menu-only actions (Toolbar false), in menu order.
        /// </summary>
        public List<OpsScreenAction> Actions { get; } = new List<OpsScreenAction>();

        /// <summary>
        /// Heading text (record title), or null while loading.
        /// </summary>
        public string? Heading { get; protected set; } = null;

        /// <summary>
        /// Status shown beside the heading, or null.
        /// </summary>
        public string? Status { get; protected set; } = null;

        /// <summary>
        /// Subtitle (already translated or raw), or null.
        /// </summary>
        public string? SubtitleText { get; protected set; } = null;

        /// <summary>
        /// Load error (translated), or null.
        /// </summary>
        public string? LoadError { get; protected set; } = null;

        /// <summary>
        /// True once the record loaded.
        /// </summary>
        public bool Loaded { get; protected set; } = false;

        /// <summary>
        /// Current panel widget, or null.
        /// </summary>
        public IWidget? CurrentPanel
        {
            get { return Tabs.SelectedIndex >= 0 && Tabs.SelectedIndex < _Panels.Count ? _Panels[Tabs.SelectedIndex] : null; }
        }

        /// <inheritdoc />
        public override bool CanFocus { get; set; } = true;

        /// <inheritdoc />
        public override IReadOnlyList<KeyValuePair<string, string>> Hints
        {
            get
            {
                List<KeyValuePair<string, string>> hints = new List<KeyValuePair<string, string>>();
                hints.Add(new KeyValuePair<string, string>(".", "Actions"));
                if (_Panels.Count > 1) hints.Add(new KeyValuePair<string, string>("[ ]", "Panels"));
                foreach (OpsScreenAction a in Actions.Where(a => a.Key != null && a.Available).Take(4))
                    hints.Add(new KeyValuePair<string, string>(KeyLabelOf(a.Key!), a.Label));
                return hints;
            }
        }

        #endregion

        #region Private-Members

        private readonly List<IWidget> _Panels = new List<IWidget>();
        private bool _ActionsBuilt = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Services.</param>
        /// <param name="screenName">Screen name.</param>
        /// <param name="title">English title.</param>
        protected OpsDetailScreen(RouteMatch route, TuiContext context, string screenName, string title)
            : base(route, context, screenName, title)
        {
            AddChild(ActionBar);
            AddChild(Tabs);
            Tabs.SelectedChanged += (s, e) => ShowPanel();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Load (or reload) the record. Called by F5 and auto-refresh.
        /// </summary>
        public abstract void Load();

        /// <summary>
        /// Add a panel tab.
        /// </summary>
        /// <param name="key">Key.</param>
        /// <param name="label">English label.</param>
        /// <param name="panel">Widget.</param>
        /// <returns>The widget.</returns>
        public TWidget AddPanel<TWidget>(string key, string label, TWidget panel) where TWidget : IWidget
        {
            if (panel is ArmadaWidget aw)
            {
                aw.Localizer = Localizer;
                aw.ApplyTheme(Theme);
            }

            Tabs.Add(key, label);
            _Panels.Add(panel);
            if (_Panels.Count == 1) ShowPanel();
            return panel;
        }

        /// <summary>
        /// Select a panel by key.
        /// </summary>
        /// <param name="key">Key.</param>
        public void SelectPanel(string key)
        {
            int idx = Tabs.Keys.IndexOf(key);
            if (idx < 0) return;
            Tabs.SelectKey(key);
            ShowPanel();
        }

        /// <summary>
        /// Open the full action menu.
        /// </summary>
        public void ShowActionMenu()
        {
            List<ActionMenuItem> items = new List<ActionMenuItem>();
            foreach (OpsScreenAction a in Actions.Where(a => a.Available))
            {
                OpsScreenAction action = a;
                ActionMenuItem item = new ActionMenuItem((a.Danger ? "! " : "") + Tr(a.DynamicLabel != null ? a.DynamicLabel() : a.Label), () => action.Run(), a.Key != null ? KeyLabelOf(a.Key) : "");
                item.Destructive = a.Danger;
                items.Add(item);
            }

            if (items.Count > 0) ShowMenu(Heading ?? Title, items);
        }

        /// <summary>
        /// Run an action by id when available.
        /// </summary>
        /// <param name="id">Id.</param>
        /// <returns>True when it ran.</returns>
        public bool RunAction(string id)
        {
            OpsScreenAction? action = Actions.FirstOrDefault(a => a.Id == id);
            if (action == null || !action.Available) return false;
            action.Run();
            return true;
        }

        /// <inheritdoc />
        public override Action? RefreshAction()
        {
            return Load;
        }

        /// <inheritdoc />
        public override IEnumerable<ArmadaCommand> Commands()
        {
            List<ArmadaCommand> list = new List<ArmadaCommand>();
            ArmadaCommand menu = new ArmadaCommand(ScreenKey + ".menu", "More actions", CommandMenuEnum.Actions, ShowActionMenu, ".");
            menu.Group = Title;
            menu.Dispatch = false;
            list.Add(menu);
            foreach (OpsScreenAction a in Actions)
            {
                OpsScreenAction action = a;
                ArmadaCommand c = a.Key != null
                    ? new ArmadaCommand(ScreenKey + "." + a.Id, a.Label, CommandMenuEnum.Actions, () => { if (action.Available) action.Run(); }, a.Key)
                    : new ArmadaCommand(ScreenKey + "." + a.Id, a.Label, CommandMenuEnum.Actions, () => { if (action.Available) action.Run(); });
                c.Group = Title;
                c.Dispatch = false;
                c.IsEnabled = () => action.Available;
                list.Add(c);
            }

            return list;
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            IWidget? leaf = Scope.FocusedLeaf();
            bool typing = leaf is TextInput || leaf is OpsTextArea || (leaf is ScrollTextView scroll && scroll.Searching);
            if (!typing)
            {
                if (key.Code == KeyCode.Character && key.Modifiers == KeyModifiers.None && key.Rune == '.')
                {
                    ShowActionMenu();
                    return true;
                }

                foreach (OpsScreenAction a in Actions)
                {
                    if (a.Key != null && MatchesKey(a.Key, key) && a.Available)
                    {
                        a.Run();
                        return true;
                    }
                }

                if (!(CurrentPanel is DiffViewer) && Tabs.HandleGlobalKey(key)) return true;
            }

            if (Scope.HandleKey(key)) return true;
            if (key.Code == KeyCode.Escape && CurrentPanel != null && !ReferenceEquals(Scope.Focused, CurrentPanel))
            {
                Scope.Focus(CurrentPanel);
                return true;
            }

            return false;
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            SurfaceText.FillRect(surface, new Rect(0, 0, width, height), Theme.Text);
            if (width < 10 || height < 4) return;
            BuildActionBar();
            // Each focus region (action bar, panel tabs, panel) gets a box line above and below it (see RegionStack).
            RegionStack stack = new RegionStack(width, height);
            int y = stack.Content(1);
            string heading = Heading ?? (LoadError != null ? Tr(Title) : Tr("Loading..."));
            int x = SurfaceText.Draw(surface, 0, y, heading, Theme.Accent.WithAttribute(CellAttributes.Bold, true), width - 20);
            if (!String.IsNullOrEmpty(Status))
            {
                string badge = "  " + StatusBadge.Label(Status) + " ";
                x += SurfaceText.Draw(surface, x, y, badge, StatusBadge.Style(Status, Theme), width - x);
            }

            string refresh = Context.Refresh.StatusText;
            string right = refresh.Length > 0 ? "[" + Tr(refresh) + "]" : "";
            if (right.Length > 0 && x + TextCells.Width(right) + 2 < width) SurfaceText.Draw(surface, width - TextCells.Width(right), y, right, Theme.Muted, width);
            y = stack.Content(1);
            if (!String.IsNullOrEmpty(SubtitleText)) SurfaceText.Draw(surface, 0, y, SubtitleText, Theme.Muted, width);
            if (LoadError != null)
            {
                y = stack.Content(1);
                SurfaceText.Draw(surface, 0, y, "! " + LoadError + "  (F5 " + Tr("Retry") + ")", Theme.Error, width);
            }

            foreach (Button b in ActionBar.Buttons)
            {
                OpsScreenAction? a = Actions.FirstOrDefault(x2 => ReferenceEquals(x2.Tag, b));
                if (a != null)
                {
                    b.Visible = a.Available;
                    b.Label = a.DynamicLabel != null ? a.DynamicLabel() : a.Label;
                }
            }

            if (ActionBar.Buttons.Any(b => b.Visible))
            {
                string more = ". " + Tr("More");
                Rect bar = stack.Place(ActionBar, 1);
                Scope.RenderChild(surface, ActionBar, new Rect(0, bar.Y, Math.Max(1, width - TextCells.Width(more) - 2), 1));
                Scope.Place(ActionBar, bar);
                SurfaceText.Draw(surface, Math.Max(0, width - TextCells.Width(more)), bar.Y, more, Theme.Muted, width);
            }

            if (_Panels.Count > 1)
            {
                Scope.RenderChild(surface, Tabs, stack.Place(Tabs, 1));
            }

            IWidget? panel = CurrentPanel;
            if (panel != null)
            {
                Rect area = stack.Fill(panel);
                if (!area.IsEmpty) Scope.RenderChild(surface, panel, area);
            }
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Human label of a key.
        /// </summary>
        /// <param name="key">Key text.</param>
        /// <returns>Label.</returns>
        protected static string KeyLabelOf(string key)
        {
            try { return KeyStroke.Parse(key).ToLabel(); }
            catch (FormatException) { return key; }
        }

        /// <summary>
        /// Add an action (header button when <paramref name="button"/> is true).
        /// </summary>
        /// <param name="id">Id.</param>
        /// <param name="label">English label.</param>
        /// <param name="run">Handler.</param>
        /// <param name="key">Key, or null.</param>
        /// <param name="when">Availability, or null.</param>
        /// <param name="button">Show as a header button.</param>
        /// <param name="danger">Destructive.</param>
        /// <returns>The action.</returns>
        protected OpsScreenAction Action(string id, string label, Action run, string? key = null, Func<bool>? when = null, bool button = false, bool danger = false)
        {
            OpsScreenAction a = new OpsScreenAction(id, label, run, key, when);
            a.Toolbar = button;
            a.Danger = danger;
            Actions.Add(a);
            return a;
        }

        #endregion

        #region Private-Methods

        private static bool MatchesKey(string keyText, KeyEvent key)
        {
            try { return KeyStroke.Parse(keyText).Matches(key); }
            catch (FormatException) { return false; }
        }

        private void BuildActionBar()
        {
            if (_ActionsBuilt) return;
            _ActionsBuilt = true;
            foreach (OpsScreenAction a in Actions.Where(a => a.Toolbar))
            {
                OpsScreenAction action = a;
                Button b = new Button(a.Label, () => { if (action.Available) action.Run(); });
                b.Hint = a.Key != null ? KeyLabelOf(a.Key) : null;
                a.Tag = b;
                ActionBar.Add(b);
            }
        }

        private void ShowPanel()
        {
            foreach (IWidget p in _Panels)
            {
                if (Scope.Children.Contains(p)) Scope.Remove(p);
            }

            IWidget? panel = CurrentPanel;
            if (panel == null) return;
            AddChild(panel);
            Scope.Focus(panel);
        }

        #endregion
    }
}

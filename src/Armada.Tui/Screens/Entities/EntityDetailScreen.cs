namespace Armada.Tui.Screens.Entities
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Client.Socket;
    using Armada.Tui.Input;
    using Armada.Tui.Routing;
    using Armada.Tui.Text;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Widgets;

    /// <summary>
    /// Base for the delivery and configuration detail screens (the dashboard's detail pages): a header with the record
    /// name and status badges, an <see cref="ActionBar"/> with the page actions, and named panels shown one at a time
    /// under a tab strip (<c>[</c>/<c>]</c> or <c>Alt+1..9</c>). Every action is also a screen command, so it appears
    /// in the Actions menu, the palette, and the help overlay, and may have a key. Loads the record off the UI loop,
    /// shows loading and error states, and reloads on <see cref="LiveEvents"/> for this record. Built lazily on first
    /// use. Use on the UI loop thread.
    /// </summary>
    /// <typeparam name="T">Record type.</typeparam>
    public abstract class EntityDetailScreen<T> : ScreenBase where T : class
    {
        #region Public-Members

        /// <summary>
        /// The loaded record, or null.
        /// </summary>
        public T? Entity { get; protected set; } = null;

        /// <summary>
        /// Record id from the route (<c>:id</c> or <c>:name</c>).
        /// </summary>
        public string EntityId { get; protected set; }

        /// <summary>
        /// Header actions.
        /// </summary>
        public ActionBar Actions { get; } = new ActionBar();

        /// <summary>
        /// Panel tabs.
        /// </summary>
        public TabStrip PanelTabs { get; } = new TabStrip();

        /// <summary>
        /// Load error (translated), or null.
        /// </summary>
        public string? LoadError { get; private set; } = null;

        /// <summary>
        /// True while loading.
        /// </summary>
        public bool Loading { get; private set; } = false;

        /// <summary>
        /// Number of completed loads (tests).
        /// </summary>
        public int LoadCount { get; private set; } = 0;

        /// <summary>
        /// Preference and command scope key: the screen's type name, so tabs of one hub keep separate table
        /// preferences and command ids.
        /// </summary>
        public override string ScreenKey
        {
            get { return GetType().Name; }
        }

        /// <summary>
        /// English singular entity name.
        /// </summary>
        public abstract string EntityLabel { get; }

        /// <summary>
        /// True when the screen creates a new record instead of showing one (for example <c>/releases/new</c>).
        /// </summary>
        public virtual bool IsCreateMode
        {
            get { return false; }
        }

        /// <summary>
        /// Key of the visible panel.
        /// </summary>
        public string? ActivePanel
        {
            get { return PanelTabs.SelectedKey; }
        }

        /// <inheritdoc />
        public override bool CanFocus { get; set; } = true;

        /// <inheritdoc />
        public override IReadOnlyList<KeyValuePair<string, string>> Hints
        {
            get
            {
                List<KeyValuePair<string, string>> hints = new List<KeyValuePair<string, string>>();
                if (_Panels.Count > 1) hints.Add(new KeyValuePair<string, string>("[ ]", "Panels"));
                hints.Add(new KeyValuePair<string, string>("F10", "Actions"));
                hints.Add(new KeyValuePair<string, string>("j", "JSON"));
                return hints;
            }
        }

        #endregion

        #region Private-Members

        private readonly List<DetailPanel> _Panels = new List<DetailPanel>();
        private readonly List<DetailAction> _ActionDefs = new List<DetailAction>();
        private readonly List<IDisposable> _Subscriptions = new List<IDisposable>();
        private List<ArmadaCommand>? _Commands = null;
        private bool _Built = false;
        private int _Generation = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Context.</param>
        protected EntityDetailScreen(RouteMatch route, TuiContext context)
            : base(route, context)
        {
            EntityId = route.Param("id") ?? route.Param("name") ?? "";
            PanelTabs.CanFocus = false;
            PanelTabs.SelectedChanged += (s, e) => ShowPanel(e.NewValue);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Reload the record and related data.
        /// </summary>
        public void Reload()
        {
            EnsureBuilt();
            if (IsCreateMode) return;
            int generation = ++_Generation;
            Loading = true;
            Task.Run(async () =>
            {
                try
                {
                    T? entity = await FetchAsync(CancellationToken.None).ConfigureAwait(false);
                    if (entity != null) await FetchRelatedAsync(entity, CancellationToken.None).ConfigureAwait(false);
                    Context.Dispatcher.Post(() =>
                    {
                        if (generation != _Generation) return;
                        Loading = false;
                        LoadCount++;
                        if (entity == null)
                        {
                            LoadError = T(EntityLabel) + ": " + T("not found");
                            return;
                        }

                        LoadError = null;
                        Entity = entity;
                        Populate(entity);
                    });
                }
                catch (ArmadaApiException ex)
                {
                    Context.Dispatcher.Post(() =>
                    {
                        if (generation != _Generation) return;
                        Loading = false;
                        LoadCount++;
                        LoadError = ex.StatusCode == 404 ? T(EntityLabel) + ": " + T("not found") : ex.Message;
                    });
                }
                catch (Exception ex)
                {
                    Context.Dispatcher.Post(() =>
                    {
                        if (generation != _Generation) return;
                        Loading = false;
                        LoadCount++;
                        LoadError = ex.Message;
                    });
                }
            });
        }

        /// <summary>
        /// Show a panel by key.
        /// </summary>
        /// <param name="key">Panel key.</param>
        /// <returns>True when shown.</returns>
        public bool ShowPanel(string? key)
        {
            DetailPanel? panel = _Panels.FirstOrDefault(p => p.Key == key);
            if (panel == null) return false;
            foreach (DetailPanel p in _Panels)
            {
                if (p.Content is ArmadaWidget aw) aw.Visible = ReferenceEquals(p, panel);
            }

            if (PanelTabs.SelectedKey != key) PanelTabs.SelectKey(key);
            FocusPanel(panel.Content);
            return true;
        }

        /// <summary>
        /// Run an action by id (tests and palette).
        /// </summary>
        /// <param name="id">Action id (without the screen prefix).</param>
        /// <returns>True when it ran.</returns>
        public bool RunAction(string id)
        {
            DetailAction? action = _ActionDefs.FirstOrDefault(a => a.Id == id);
            if (action == null || !action.IsVisible()) return false;
            action.Handler();
            return true;
        }

        /// <summary>
        /// Labels of the actions currently offered.
        /// </summary>
        /// <returns>English labels.</returns>
        public List<string> VisibleActions()
        {
            EnsureBuilt();
            return _ActionDefs.Where(a => a.IsVisible()).Select(a => a.Label).ToList();
        }

        /// <inheritdoc />
        public override IEnumerable<ArmadaCommand> Commands()
        {
            EnsureBuilt();
            if (_Commands != null) return _Commands;
            List<ArmadaCommand> list = new List<ArmadaCommand>();
            foreach (DetailAction action in _ActionDefs)
            {
                DetailAction a = action;
                ArmadaCommand c = a.Gesture != null
                    ? new ArmadaCommand(ScreenKey + "." + a.Id, a.Label, CommandMenuEnum.Actions, a.Handler, a.Gesture)
                    : new ArmadaCommand(ScreenKey + "." + a.Id, a.Label, CommandMenuEnum.Actions, a.Handler);
                c.IsVisible = a.IsVisible;
                c.Group = Title;
                list.Add(c);
            }

            for (int i = 0; i < _Panels.Count; i++)
            {
                DetailPanel panel = _Panels[i];
                ArmadaCommand c = new ArmadaCommand(ScreenKey + ".panel." + panel.Key, "Show panel: " + panel.Label, CommandMenuEnum.Actions, () => ShowPanel(panel.Key));
                c.Group = Title;
                c.Dispatch = false;
                list.Add(c);
            }

            _Commands = list;
            return list;
        }

        /// <inheritdoc />
        public override Action? RefreshAction()
        {
            return IsCreateMode ? null : Reload;
        }

        /// <inheritdoc />
        public override void OnActivated()
        {
            EnsureBuilt();
            foreach (string evt in LiveEvents)
            {
                _Subscriptions.Add(Context.Events.Subscribe(evt, m =>
                {
                    EntityChangedEvent? data = null;
                    try { data = m.GetData<EntityChangedEvent>(); } catch (Exception) { data = null; }
                    if (data == null || String.IsNullOrEmpty(data.Id) || String.Equals(data.Id, EntityId, StringComparison.Ordinal)) Reload();
                }));
            }

            if (IsCreateMode) OnCreateMode();
            else Reload();
        }

        /// <inheritdoc />
        public override void OnDeactivated()
        {
            foreach (IDisposable sub in _Subscriptions) sub.Dispose();
            _Subscriptions.Clear();
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            EnsureBuilt();
            if ((key.Code == KeyCode.PageDown || key.Code == KeyCode.PageUp) && (key.Modifiers & KeyModifiers.Ctrl) != 0 && _Panels.Count > 1)
            {
                int index = Math.Max(0, _Panels.FindIndex(p => p.Key == PanelTabs.SelectedKey));
                int next = key.Code == KeyCode.PageDown ? (index + 1) % _Panels.Count : (index - 1 + _Panels.Count) % _Panels.Count;
                return ShowPanel(_Panels[next].Key);
            }

            if (Scope.HandleKey(key)) return true;
            if (key.Code == KeyCode.Character && (key.Modifiers & KeyModifiers.Ctrl) != 0 && Char.ToLowerInvariant((char)key.Rune) == 's')
            {
                DetailPanel? active = _Panels.FirstOrDefault(p => p.Key == PanelTabs.SelectedKey);
                FormView? form = active != null ? FindForm(active.Content) : null;
                if (form != null)
                {
                    form.RequestSave();
                    return true;
                }
            }

            return PanelTabs.HandleGlobalKey(key);
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            EnsureBuilt();
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            SurfaceText.FillRect(surface, new Rect(0, 0, width, height), Theme.Text);
            if (width < 10 || height < 4) return;
            foreach (Button b in Actions.Buttons)
            {
                DetailAction? def = _ActionDefs.FirstOrDefault(a => ReferenceEquals(a.Button, b));
                if (def != null) b.Visible = Entity != null || IsCreateMode ? def.IsVisible() : false;
            }

            int y = 0;
            string title = Entity != null ? HeaderTitle(Entity) : IsCreateMode ? T(CreateTitle) : EntityId;
            int x = SurfaceText.Draw(surface, 0, y, title, Theme.Accent, width);
            if (Entity != null)
            {
                foreach (string status in HeaderStatuses(Entity))
                {
                    if (String.IsNullOrEmpty(status) || x + 3 >= width) continue;
                    x += 2;
                    x += SurfaceText.Draw(surface, x, y, StatusBadge.Marker(status) + " " + T(status), StatusBadge.Style(status, Theme), width - x);
                }
            }

            y++;
            int actionLines = Math.Min(4, Actions.LinesFor(width));
            if (actionLines > 0)
            {
                Scope.RenderChild(surface, Actions, new Rect(0, y, width, actionLines));
                y += actionLines;
            }

            y++;
            if (LoadError != null && Entity == null)
            {
                SurfaceText.Draw(surface, 0, y, "! " + LoadError + "  (F5 " + T("Retry") + ")", Theme.Error, width);
                return;
            }

            if (Entity == null && !IsCreateMode)
            {
                SurfaceText.Draw(surface, 0, y, T("Loading..."), Theme.Muted, width);
                return;
            }

            if (_Panels.Count > 1)
            {
                PanelTabs.Render(new SurfaceView(surface, new Rect(0, y, width, 1)));
                y += 2;
            }

            DetailPanel? active = _Panels.FirstOrDefault(p => p.Key == PanelTabs.SelectedKey) ?? _Panels.FirstOrDefault();
            if (active != null && height - y > 0) Scope.RenderChild(surface, active.Content, new Rect(0, y, width, height - y));
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// English title in create mode.
        /// </summary>
        protected virtual string CreateTitle
        {
            get { return "New " + EntityLabel; }
        }

        /// <summary>
        /// Load the record. Runs off the UI loop.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Record or null.</returns>
        protected abstract Task<T?> FetchAsync(CancellationToken token);

        /// <summary>
        /// Load related data (runbook executions, linked checks, reference lists) into fields. Runs off the UI loop
        /// after <see cref="FetchAsync"/>; failures should be swallowed so the record still shows.
        /// </summary>
        /// <param name="entity">Record.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        protected virtual Task FetchRelatedAsync(T entity, CancellationToken token)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Define panels once with <see cref="AddPanel"/>.
        /// </summary>
        protected abstract void BuildPanels();

        /// <summary>
        /// Define actions once with <see cref="AddAction"/>.
        /// </summary>
        protected abstract void BuildActions();

        /// <summary>
        /// Fill the panels from a freshly loaded record (UI loop).
        /// </summary>
        /// <param name="entity">Record.</param>
        protected abstract void Populate(T entity);

        /// <summary>
        /// Header title for a record.
        /// </summary>
        /// <param name="entity">Record.</param>
        /// <returns>Title.</returns>
        protected abstract string HeaderTitle(T entity);

        /// <summary>
        /// Status badges shown after the title.
        /// </summary>
        /// <param name="entity">Record.</param>
        /// <returns>Statuses.</returns>
        protected virtual IEnumerable<string> HeaderStatuses(T entity)
        {
            return Array.Empty<string>();
        }

        /// <summary>
        /// Socket event types that reload this record when they name it (or name no record).
        /// </summary>
        protected virtual IEnumerable<string> LiveEvents
        {
            get { return Array.Empty<string>(); }
        }

        /// <summary>
        /// Called on activation in create mode.
        /// </summary>
        protected virtual void OnCreateMode()
        {
        }

        /// <summary>
        /// Add a panel.
        /// </summary>
        /// <param name="key">Key.</param>
        /// <param name="label">English label.</param>
        /// <param name="content">Content widget.</param>
        /// <returns>The content.</returns>
        protected TWidget AddPanel<TWidget>(string key, string label, TWidget content) where TWidget : IWidget
        {
            DetailPanel panel = new DetailPanel(key, label, content);
            _Panels.Add(panel);
            PanelTabs.Add(key, label);
            AddChild(content);
            if (content is ArmadaWidget aw) aw.Visible = _Panels.Count == 1;
            if (_Panels.Count == 1)
            {
                PanelTabs.SelectKey(key);
                FocusPanel(content);
            }

            return content;
        }

        /// <summary>
        /// Add an action (button, command, Actions menu entry).
        /// </summary>
        /// <param name="id">Action id (unique per screen).</param>
        /// <param name="label">English label.</param>
        /// <param name="handler">Handler.</param>
        /// <param name="visible">Visibility (evaluated on every frame), or null for always.</param>
        /// <param name="gesture">Key, or null.</param>
        protected void AddAction(string id, string label, Action handler, Func<bool>? visible = null, string? gesture = null)
        {
            DetailAction action = new DetailAction(id, label, handler, visible, gesture);
            action.Button = Actions.Add(label, handler, gesture != null ? KeyLabel(gesture) : null);
            _ActionDefs.Add(action);
        }

        /// <summary>
        /// Add the standard View JSON action (<c>j</c>).
        /// </summary>
        protected void AddJsonAction()
        {
            AddAction("json", "View JSON", () => { if (Entity != null) EntityUi.ShowJson(Context, HeaderTitle(Entity), Entity); }, () => Entity != null, "j");
        }

        /// <summary>
        /// Build (once).
        /// </summary>
        protected void EnsureBuilt()
        {
            if (_Built) return;
            _Built = true;
            Actions.Localizer = Localizer;
            PanelTabs.Localizer = Localizer;
            PanelTabs.ApplyTheme(Theme);
            AddChild(Actions);
            BuildActions();
            BuildPanels();
        }

        #endregion

        #region Private-Methods

        private void FocusPanel(IWidget content)
        {
            if (IsEditor(content) && Actions.LinesFor(200) > 0) Scope.Focus(Actions);
            else Scope.Focus(content);
        }

        private static FormView? FindForm(IWidget widget)
        {
            if (widget is FormView form) return form;
            if (widget is ContainerWidget container)
            {
                foreach (IWidget child in container.Scope.Children)
                {
                    FormView? found = FindForm(child);
                    if (found != null) return found;
                }
            }

            return null;
        }

        private static bool IsEditor(IWidget widget)
        {
            if (widget is FormView || widget is TextInput || widget is TextAreaField) return true;
            if (widget is ContainerWidget container) return container.Scope.Children.Any(IsEditor);
            return false;
        }

        private static string KeyLabel(string gesture)
        {
            try
            {
                return new KeyGesture(gesture).ToLabel();
            }
            catch (FormatException)
            {
                return gesture;
            }
        }

        #endregion
    }
}

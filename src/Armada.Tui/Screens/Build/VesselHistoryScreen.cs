namespace Armada.Tui.Screens.Build
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using Armada.Client.Models;
    using Armada.Core.Models;
    using Armada.Tui.Input;
    using Armada.Tui.Modals;
    using Armada.Tui.Routing;
    using Armada.Tui.Screens.Kit;
    using Armada.Tui.Screens.Operations;
    using Armada.Tui.Services;
    using Armada.Tui.Text;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Widgets;

    /// <summary>
    /// A vessel's commit history (<c>/vessels/:id/history</c>), the dashboard's VesselHistory page: a contribution
    /// heatmap of the branch's commits per local day over a year (<c>[</c>/<c>]</c> move a year back or forward, down to
    /// the first commit; arrows pick a day and Enter jumps the list to it) and the commits newest first, labelled by
    /// local day, loading the next page as the cursor nears the end (Enter shows a commit with its files, <c>y</c>
    /// copies the SHA). <c>t</c> jumps to a date, <c>l</c> returns to the latest commits, <c>b</c> picks the branch
    /// (default the vessel's default branch). Days are bucketed in the local UTC offset. Not thread-safe.
    /// </summary>
    public class VesselHistoryScreen : OpsScreen
    {
        #region Public-Members

        /// <summary>
        /// Commits requested per page.
        /// </summary>
        public const int PageLimit = 50;

        /// <summary>
        /// The next page loads when the cursor is this many rows (or fewer) from the last commit.
        /// </summary>
        public const int PrefetchRows = 5;

        /// <summary>
        /// Vessel id from the route.
        /// </summary>
        public string VesselId { get; }

        /// <summary>
        /// The vessel once loaded.
        /// </summary>
        public Vessel? Vessel { get; private set; } = null;

        /// <summary>
        /// Branch shown (empty until the vessel loads).
        /// </summary>
        public string Branch { get; private set; } = "";

        /// <summary>
        /// Local UTC offset in minutes the days are bucketed in (from the local time zone at the context clock's now).
        /// </summary>
        public int UtcOffsetMinutes { get; }

        /// <summary>
        /// Today in the local offset.
        /// </summary>
        public DateTime Today { get; }

        /// <summary>
        /// Last day of the heatmap range.
        /// </summary>
        public DateTime RangeTo { get; private set; }

        /// <summary>
        /// First day of the heatmap range (364 days before <see cref="RangeTo"/>).
        /// </summary>
        public DateTime RangeFrom
        {
            get { return RangeTo.AddDays(-364); }
        }

        /// <summary>
        /// Activity for the range once loaded, or null.
        /// </summary>
        public VesselCommitActivity? Activity { get; private set; } = null;

        /// <summary>
        /// Activity error (translated or from the server), or null.
        /// </summary>
        public string? ActivityError { get; private set; } = null;

        /// <summary>
        /// The day the list was jumped to (commits on or before it), or null for the latest commits.
        /// </summary>
        public DateTime? JumpDate { get; private set; } = null;

        /// <summary>
        /// Commits loaded so far, newest first, without duplicates.
        /// </summary>
        public List<VesselCommit> Commits { get; } = new List<VesselCommit>();

        /// <summary>
        /// Cursor for the next (older) page, or null.
        /// </summary>
        public string? NextCursor { get; private set; } = null;

        /// <summary>
        /// True while a page is loading.
        /// </summary>
        public bool CommitsLoading { get; private set; } = false;

        /// <summary>
        /// True once the last page arrived.
        /// </summary>
        public bool ReachedEnd { get; private set; } = false;

        /// <summary>
        /// Commit list error, or null.
        /// </summary>
        public string? CommitsError { get; private set; } = null;

        /// <summary>
        /// Load error for the vessel (translated), or null.
        /// </summary>
        public string? LoadError { get; private set; } = null;

        /// <summary>
        /// The heatmap.
        /// </summary>
        public CommitHeatmap Heatmap { get; } = new CommitHeatmap();

        /// <summary>
        /// The commit list.
        /// </summary>
        public ArmadaGrid<VesselHistoryRow> CommitGrid { get; }

        /// <summary>
        /// Screen actions in menu order.
        /// </summary>
        public List<OpsScreenAction> Actions { get; } = new List<OpsScreenAction>();

        /// <summary>
        /// The open commit detail, or null.
        /// </summary>
        public ViewerModal? DetailModal { get; private set; } = null;

        /// <inheritdoc />
        public override bool CanFocus { get; set; } = true;

        /// <inheritdoc />
        public override IReadOnlyList<KeyValuePair<string, string>> Hints
        {
            get
            {
                // Most useful first: the status bar drops the tail at narrow widths, and ". Actions" lists everything.
                List<KeyValuePair<string, string>> hints = new List<KeyValuePair<string, string>>();
                bool list = ReferenceEquals(Scope.Focused, CommitGrid);
                if (list)
                {
                    if (CurrentCommit() != null)
                    {
                        hints.Add(new KeyValuePair<string, string>("Enter", "Details"));
                        hints.Add(new KeyValuePair<string, string>("y", "Copy SHA"));
                    }
                }
                else
                {
                    hints.Add(new KeyValuePair<string, string>("Arrows", "Day"));
                    hints.Add(new KeyValuePair<string, string>("Enter", "Show day"));
                }

                hints.Add(new KeyValuePair<string, string>(".", "Actions"));
                hints.Add(new KeyValuePair<string, string>("t", "Jump to date"));
                if (JumpDate.HasValue || RangeTo != Today) hints.Add(new KeyValuePair<string, string>("l", "Latest"));
                hints.Add(new KeyValuePair<string, string>("b", "Branch"));
                hints.Add(new KeyValuePair<string, string>("[ ]", "Year"));
                hints.Add(new KeyValuePair<string, string>("Tab", list ? "Heatmap" : "Commits"));
                return hints;
            }
        }

        #endregion

        #region Private-Members

        private int _ListGeneration = 0;
        private int _ActivityGeneration = 0;
        private string? _LastCursor = null;
        private readonly HashSet<string> _Seen = new HashSet<string>(StringComparer.Ordinal);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Services.</param>
        public VesselHistoryScreen(RouteMatch route, TuiContext context)
            : base(route, context, "VesselHistoryScreen", "Vessel History")
        {
            VesselId = route.Param("id") ?? "";
            DateTime now = DateTime.SpecifyKind(Context.Clock.UtcNow, DateTimeKind.Utc);
            UtcOffsetMinutes = Math.Clamp((int)Math.Round(TimeZoneInfo.Local.GetUtcOffset(now).TotalMinutes), -840, 840);
            Today = DateTime.SpecifyKind(now.AddMinutes(UtcOffsetMinutes).Date, DateTimeKind.Unspecified);
            RangeTo = Today;

            CommitGrid = new ArmadaGrid<VesselHistoryRow>(r => r.Id);
            CommitGrid.MultiSelect = false;
            CommitGrid.ShowPagingBar = false;
            CommitGrid.PageSize = 1000000;
            CommitGrid.ModalHost = context.Modals;
            CommitGrid.EmptyText = "Loading...";
            CommitGrid.AddColumn(new GridColumn<VesselHistoryRow>("day", "Day", r => r.DayLabel) { Width = 14 });
            CommitGrid.AddColumn(new GridColumn<VesselHistoryRow>("time", "Time", r => r.Commit != null ? LocalTime(r.Commit.CommittedUtc).ToString("HH:mm", CultureInfo.InvariantCulture) : "") { Width = 5 });
            CommitGrid.AddColumn(new GridColumn<VesselHistoryRow>("when", "When", r => r.Commit != null ? Context.Loc.FormatRelative(r.Commit.CommittedUtc, Context.Clock.UtcNow) : "") { Width = 8 });
            CommitGrid.AddColumn(new GridColumn<VesselHistoryRow>("sha", "SHA", r => r.Commit != null ? r.Commit.ShortSha : "") { Width = 8, Identifier = false });
            GridColumn<VesselHistoryRow> subject = new GridColumn<VesselHistoryRow>("subject", "Subject", SubjectText) { Weight = 5, Primary = true };
            subject.Style = (r, t) => r.Kind == VesselHistoryRowKindEnum.Error ? t.Error : r.Kind == VesselHistoryRowKindEnum.Commit ? (CellStyle?)null : t.Muted;
            CommitGrid.AddColumn(subject);
            CommitGrid.AddColumn(new GridColumn<VesselHistoryRow>("author", "Author", r => r.Commit != null ? r.Commit.AuthorName : "") { Weight = 2 });
            GridColumn<VesselHistoryRow> changes = new GridColumn<VesselHistoryRow>("changes", "+/-", r => r.Commit != null ? "+" + r.Commit.AddedLines.ToString(CultureInfo.InvariantCulture) + " -" + r.Commit.DeletedLines.ToString(CultureInfo.InvariantCulture) : "") { Width = 12, Align = CellAlignment.Right };
            changes.Style = (r, t) => t.Info;
            CommitGrid.AddColumn(changes);
            CommitGrid.AddColumn(new GridColumn<VesselHistoryRow>("files", "Files", r => r.Commit != null ? r.Commit.FilesChanged.ToString(CultureInfo.InvariantCulture) : "") { Width = 5, Align = CellAlignment.Right });
            CommitGrid.Activated += (s, r) => { if (r.Commit != null) ShowCommit(r.Commit); };
            CommitGrid.CursorChanged += (s, r) => MaybeLoadMore();

            Heatmap.DayActivated += (s, day) => JumpToDate(day);
            Heatmap.SetData(RangeFrom, RangeTo, null);
            Heatmap.Summary = Tr("Loading...");

            Action("details", "Commit Details", () => { VesselCommit? c = CurrentCommit(); if (c != null) ShowCommit(c); }, "enter", () => ReferenceEquals(Scope.Focused, CommitGrid) && CurrentCommit() != null);
            Action("copy-sha", "Copy SHA", () => Copy(CurrentCommit()?.Sha, "SHA"), "y", () => ReferenceEquals(Scope.Focused, CommitGrid) && CurrentCommit() != null);
            Action("jump", "Jump to Date...", () => PromptDate(), "t", () => Vessel != null);
            Action("latest", "Latest Commits", ShowLatest, "l", () => Vessel != null && (JumpDate.HasValue || RangeTo != Today));
            Action("branch", "Branch...", PickBranch, "b", () => Vessel != null);
            Action("previous-year", "Previous Year", PreviousYear, "[", CanGoBack);
            Action("next-year", "Next Year", NextYear, "]", () => Vessel != null && RangeTo < Today);
            Action("vessel", "Open Vessel", () => Context.Navigate("/vessels/" + Uri.EscapeDataString(VesselId)), null);

            AddChild(Heatmap);
            AddChild(CommitGrid);
            Scope.Focus(Heatmap);
            Load();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The <c>before</c> instant (ISO 8601, UTC) that lists the commits on or before a local day: the start of the
        /// next day in the offset.
        /// </summary>
        /// <param name="localDay">Local day.</param>
        /// <param name="utcOffsetMinutes">UTC offset in minutes.</param>
        /// <returns>Instant text such as <c>2026-10-06T07:00:00Z</c>.</returns>
        public static string BeforeFor(DateTime localDay, int utcOffsetMinutes)
        {
            DateTimeOffset next = new DateTimeOffset(DateTime.SpecifyKind(localDay.Date.AddDays(1), DateTimeKind.Unspecified), TimeSpan.FromMinutes(utcOffsetMinutes));
            return next.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Parse a <c>yyyy-MM-dd</c> date.
        /// </summary>
        /// <param name="text">Text.</param>
        /// <param name="date">The date.</param>
        /// <returns>True when valid.</returns>
        public static bool TryParseDate(string? text, out DateTime date)
        {
            return DateTime.TryParseExact((text ?? "").Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
        }

        /// <summary>
        /// Load the vessel, then the activity and the first page of commits.
        /// </summary>
        public void Load()
        {
            bool initial = Vessel == null;
            Call((c, t) => c.GetVesselAsync(VesselId, t), v =>
            {
                if (v == null)
                {
                    LoadError = Tr("Vessel not found.");
                    return;
                }

                Vessel = v;
                LoadError = null;
                if (initial || Branch.Length == 0) Branch = String.IsNullOrEmpty(v.DefaultBranch) ? "main" : v.DefaultBranch;
                LoadActivity();
                ResetCommits();
            }, null, ex =>
            {
                if (initial) LoadError = String.IsNullOrEmpty(ex.Message) ? Tr("Failed to load vessel.") : ex.Message;
                CommitGrid.EmptyText = LoadError ?? Tr("Failed to load vessel.");
                Heatmap.Summary = "";
            });
        }

        /// <summary>
        /// Show the commits on or before a local day: the heatmap selects it (moving the range when it is outside) and
        /// the list restarts there.
        /// </summary>
        /// <param name="day">Local day.</param>
        public void JumpToDate(DateTime day)
        {
            if (Vessel == null) return;
            DateTime d = day.Date > Today ? Today : day.Date;
            if (d < RangeFrom || d > RangeTo)
            {
                RangeTo = d;
                LoadActivity();
            }

            Heatmap.Select(d);
            JumpDate = d;
            ResetCommits();
        }

        /// <summary>
        /// Back to the latest commits and the last year.
        /// </summary>
        public void ShowLatest()
        {
            if (Vessel == null) return;
            if (RangeTo != Today)
            {
                RangeTo = Today;
                LoadActivity();
            }

            Heatmap.Select(Today);
            JumpDate = null;
            ResetCommits();
        }

        /// <summary>
        /// Show another branch (the activity and the list restart; the jump date is kept).
        /// </summary>
        /// <param name="branch">Branch name.</param>
        public void SelectBranch(string branch)
        {
            if (Vessel == null || String.IsNullOrEmpty(branch)) return;
            Branch = branch;
            LoadActivity();
            ResetCommits();
        }

        /// <summary>
        /// Move the heatmap a year back (not past the first commit).
        /// </summary>
        public void PreviousYear()
        {
            if (!CanGoBack()) return;
            DateTime selected = Heatmap.Selected;
            RangeTo = RangeTo.AddYears(-1);
            LoadActivity();
            Heatmap.Select(selected.AddYears(-1));
        }

        /// <summary>
        /// Move the heatmap a year forward (not past today).
        /// </summary>
        public void NextYear()
        {
            if (Vessel == null || RangeTo >= Today) return;
            DateTime selected = Heatmap.Selected;
            DateTime to = RangeTo.AddYears(1);
            RangeTo = to > Today ? Today : to;
            LoadActivity();
            Heatmap.Select(selected.AddYears(1));
        }

        /// <summary>
        /// True when the range can move a year back: the branch has commits before the range.
        /// </summary>
        /// <returns>True when available.</returns>
        public bool CanGoBack()
        {
            if (Vessel == null || Activity == null || !Activity.FirstCommitUtc.HasValue) return false;
            return LocalTime(Activity.FirstCommitUtc.Value).Date < RangeFrom;
        }

        /// <summary>
        /// The commit under the list cursor, or null.
        /// </summary>
        /// <returns>Commit or null.</returns>
        public VesselCommit? CurrentCommit()
        {
            return CommitGrid.Current?.Commit;
        }

        /// <summary>
        /// Ask for a date (<c>yyyy-MM-dd</c>) and jump to it.
        /// </summary>
        /// <returns>The prompt, or null when the vessel has not loaded.</returns>
        public FormModal? PromptDate()
        {
            if (Vessel == null) return null;
            FormView form = new FormView();
            InputField field = form.AddField("Date", new InputField(), "Show the commits on or before this day (yyyy-MM-dd).");
            field.Value = (JumpDate ?? Heatmap.Selected).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            field.Validator = v => TryParseDate(v, out DateTime parsed) ? null : "Enter a date as yyyy-MM-dd.";
            FormModal modal = new FormModal("Jump to Date", form, Context, "Jump");
            DateTime chosen = DateTime.MinValue;
            modal.Submit = () => TryParseDate(field.Value, out chosen) ? null : Tr("Enter a date as yyyy-MM-dd.");
            field.Submitted += (s, e) => modal.RunSubmit();
            Context.Modals.Show(modal, result =>
            {
                if (result is bool ok && ok) JumpToDate(chosen);
            });
            return modal;
        }

        /// <summary>
        /// Pick the branch from the vessel's branches.
        /// </summary>
        public void PickBranch()
        {
            if (Vessel == null) return;
            Call((c, t) => c.GetVesselBranchesAsync(VesselId, t), result =>
            {
                List<SelectOption<string>> options = new List<SelectOption<string>>();
                foreach (BranchInfo b in result?.Branches ?? new List<BranchInfo>())
                {
                    if (String.IsNullOrEmpty(b.Name)) continue;
                    options.Add(new SelectOption<string>(b.Name, b.Name, b.IsDefault ? Tr("default") : (b.Name == Branch ? Tr("current") : "")));
                }

                if (options.Count == 0)
                {
                    ShowMessage(!String.IsNullOrEmpty(result?.Error) ? result!.Error! : Tr("No branches found."));
                    return;
                }

                PickerModal<string> picker = new PickerModal<string>("Branch", options, Context.Loc, Context.Theme.Current);
                Context.Modals.Show(picker, chosen =>
                {
                    if (chosen is SelectOption<string> option && option.Value != Branch) SelectBranch(option.Value);
                });
            }, "Failed to load branches.");
        }

        /// <summary>
        /// Show a commit: message, author and committer with both dates, parents, and the files.
        /// </summary>
        /// <param name="commit">Commit.</param>
        /// <returns>The viewer.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="commit"/> is null.</exception>
        public ViewerModal ShowCommit(VesselCommit commit)
        {
            if (commit == null) throw new ArgumentNullException(nameof(commit));
            OpsDocumentView view = new OpsDocumentView();
            view.Builder = doc => BuildCommit(doc, commit);
            ViewerModal modal = new ViewerModal(Tr("Commit {{sha}}", LocalizationArgs.Of("sha", commit.ShortSha)), view, Context.Loc, Context.Theme.Current);
            modal.WidthRatio = 0.92;
            modal.HeightRatio = 0.9;
            modal.FooterHint = " y " + Tr("Copy SHA") + "  / " + Tr("Search") + "  Esc " + Tr("Close") + " ";
            modal.CopyRequested += (s, e) => Copy(commit.Sha, "SHA");
            DetailModal = modal;
            Context.Modals.Show(modal, r => { if (ReferenceEquals(DetailModal, modal)) DetailModal = null; });
            return modal;
        }

        /// <summary>
        /// Open the action menu.
        /// </summary>
        public void ShowActionMenu()
        {
            List<ActionMenuItem> items = new List<ActionMenuItem>();
            foreach (OpsScreenAction a in Actions.Where(a => a.Available))
            {
                OpsScreenAction action = a;
                items.Add(new ActionMenuItem(Tr(a.Label), () => action.Run(), a.Key != null ? KeyLabelOf(a.Key) : ""));
            }

            if (items.Count > 0) ShowMenu(Vessel != null ? Vessel.Name : Title, items);
        }

        /// <summary>
        /// Run an action by id when available.
        /// </summary>
        /// <param name="id">Action id.</param>
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
            return Refresh;
        }

        /// <inheritdoc />
        public override int DefaultRefreshSeconds()
        {
            // A refresh restarts the list from the top, so it never runs on a timer.
            return 0;
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

            return Scope.HandleKey(key);
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            SurfaceText.FillRect(surface, new Rect(0, 0, width, height), Theme.Text);
            if (width < 10 || height < 4) return;
            // Each focus region (heatmap, commit list) gets a box line above and below it (see RegionStack).
            RegionStack stack = new RegionStack(width, height);
            int y = stack.Content(1);
            int x = SurfaceText.Draw(surface, 0, y, Tr("Vessel History"), Theme.Accent.WithAttribute(CellAttributes.Bold, true), width);
            if (Vessel != null) x += SurfaceText.Draw(surface, x, y, "  " + Vessel.Name, Theme.Text, width - x);
            string right = Branch.Length > 0 ? Tr("Branch") + ": " + Branch + " (b)" : "";
            if (right.Length > 0 && x + TextCells.Width(right) + 2 < width) SurfaceText.Draw(surface, width - TextCells.Width(right), y, right, Theme.Muted, width);
            if (LoadError != null)
            {
                y = stack.Content(1);
                SurfaceText.Draw(surface, 0, y, "! " + LoadError + "  (F5 " + Tr("Retry") + ")", Theme.Error, width);
            }

            // The heatmap gives up rows before the list does: the list keeps at least a header and three rows.
            int heatRows = Math.Clamp(stack.Remaining - 9, 3, CommitHeatmap.PreferredHeight);
            Scope.RenderChild(surface, Heatmap, stack.Place(Heatmap, heatRows));
            if (stack.Remaining >= 7)
            {
                y = stack.Content(1);
                SurfaceText.Draw(surface, 0, y, ListCaption(), Theme.Muted, width);
            }

            Rect grid = stack.Fill(CommitGrid);
            if (!grid.IsEmpty) Scope.RenderChild(surface, CommitGrid, grid);
        }

        #endregion

        #region Private-Methods

        private void Action(string id, string label, Action run, string? key, Func<bool>? when = null)
        {
            OpsScreenAction a = new OpsScreenAction(id, label, run, key, when);
            a.Toolbar = false;
            Actions.Add(a);
        }

        private static string KeyLabelOf(string key)
        {
            try { return KeyStroke.Parse(key).ToLabel(); }
            catch (FormatException) { return key; }
        }

        private static bool MatchesKey(string keyText, KeyEvent key)
        {
            try { return KeyStroke.Parse(keyText).Matches(key); }
            catch (FormatException) { return false; }
        }

        private void Refresh()
        {
            if (Vessel == null)
            {
                Load();
                return;
            }

            LoadActivity();
            ResetCommits();
        }

        private DateTime LocalTime(DateTime utc)
        {
            return DateTime.SpecifyKind(utc, DateTimeKind.Utc).AddMinutes(UtcOffsetMinutes);
        }

        private string ListCaption()
        {
            string caption = Tr("Commits on {{branch}}", LocalizationArgs.Of("branch", Branch.Length > 0 ? Branch : "-"));
            if (JumpDate.HasValue)
                caption += ", " + Tr("on or before {{date}}", LocalizationArgs.Of("date", JumpDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))) + "  (l " + Tr("Latest") + ")";
            else
                caption += ", " + Tr("newest first");
            if (Commits.Count > 0) caption += "  |  " + Tr("{count, plural, one {# commit loaded} other {# commits loaded}}", LocalizationArgs.Of("count", Commits.Count));
            return caption;
        }

        private string SubjectText(VesselHistoryRow row)
        {
            if (row.Commit == null) return row.Text;
            return (row.Commit.IsMerge ? "[" + Tr("merge") + "] " : "") + row.Commit.Subject;
        }

        private void LoadActivity()
        {
            int generation = ++_ActivityGeneration;
            DateTime from = RangeFrom;
            DateTime to = RangeTo;
            Activity = null;
            ActivityError = null;
            Heatmap.SetData(from, to, null);
            Heatmap.Summary = Tr("Loading activity...");
            Heatmap.SummaryStyle = Theme.Muted;
            VesselCommitActivityQuery query = new VesselCommitActivityQuery();
            query.Branch = Branch.Length > 0 ? Branch : null;
            query.From = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            query.To = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            query.UtcOffsetMinutes = UtcOffsetMinutes;
            Call((c, t) => c.GetVesselCommitActivityAsync(VesselId, query, t), activity =>
            {
                if (generation != _ActivityGeneration) return;
                if (activity == null)
                {
                    ActivityError = Tr("Failed to load activity.");
                    Heatmap.Summary = ActivityError;
                    Heatmap.SummaryStyle = Theme.Error;
                    return;
                }

                Activity = activity;
                Dictionary<DateTime, int> counts = new Dictionary<DateTime, int>();
                foreach (VesselCommitActivityDay day in activity.Days ?? new List<VesselCommitActivityDay>())
                {
                    if (TryParseDate(day.Date, out DateTime d)) counts[d] = day.Count;
                }

                Heatmap.SetData(from, to, counts);
                if (!String.IsNullOrEmpty(activity.Error))
                {
                    ActivityError = activity.Error;
                    Heatmap.Summary = "! " + activity.Error;
                    Heatmap.SummaryStyle = Theme.Error;
                    return;
                }

                Heatmap.SummaryStyle = null;
                Heatmap.Summary = to == Today
                    ? Tr("{count, plural, one {# commit in the last year} other {# commits in the last year}}", LocalizationArgs.Of("count", activity.TotalCommits))
                    : Tr("{count, plural, one {# commit} other {# commits}}", LocalizationArgs.Of("count", activity.TotalCommits)) + " " + Tr("from {{from}} to {{to}}", LocalizationArgs.Of("from", query.From, "to", query.To));
            }, null, ex =>
            {
                if (generation != _ActivityGeneration) return;
                ActivityError = String.IsNullOrEmpty(ex.Message) ? Tr("Failed to load activity.") : ex.Message;
                Heatmap.Summary = "! " + ActivityError;
                Heatmap.SummaryStyle = Theme.Error;
            });
        }

        private void ResetCommits()
        {
            _ListGeneration++;
            Commits.Clear();
            _Seen.Clear();
            NextCursor = null;
            _LastCursor = null;
            ReachedEnd = false;
            CommitsError = null;
            CommitsLoading = false;
            RebuildRows();
            CommitGrid.MoveCursor(0);
            LoadPage(null);
        }

        private void LoadPage(string? cursor)
        {
            int generation = _ListGeneration;
            CommitsLoading = true;
            _LastCursor = cursor;
            VesselCommitQuery query = new VesselCommitQuery();
            if (cursor != null)
            {
                query.Cursor = cursor;
            }
            else
            {
                query.Branch = Branch.Length > 0 ? Branch : null;
                query.Before = JumpDate.HasValue ? BeforeFor(JumpDate.Value, UtcOffsetMinutes) : null;
            }

            query.Limit = PageLimit;
            RebuildRows();
            Call((c, t) => c.GetVesselCommitsAsync(VesselId, query, t), page =>
            {
                if (generation != _ListGeneration) return;
                CommitsLoading = false;
                if (page == null)
                {
                    CommitsError = Tr("Failed to load commits.");
                    RebuildRows();
                    return;
                }

                if (!String.IsNullOrEmpty(page.Error))
                {
                    CommitsError = page.Error;
                    NextCursor = null;
                    RebuildRows();
                    return;
                }

                foreach (VesselCommit commit in page.Commits ?? new List<VesselCommit>())
                {
                    if (commit != null && !String.IsNullOrEmpty(commit.Sha) && _Seen.Add(commit.Sha)) Commits.Add(commit);
                }

                // A cursor that comes back unchanged would load the same page forever.
                NextCursor = String.IsNullOrEmpty(page.NextCursor) || page.NextCursor == cursor ? null : page.NextCursor;
                ReachedEnd = NextCursor == null;
                RebuildRows();
                MaybeLoadMore();
            }, null, ex =>
            {
                if (generation != _ListGeneration) return;
                CommitsLoading = false;
                CommitsError = String.IsNullOrEmpty(ex.Message) ? Tr("Failed to load commits.") : ex.Message;
                RebuildRows();
            });
        }

        private void MaybeLoadMore()
        {
            if (CommitsLoading || NextCursor == null || CommitsError != null) return;
            if (CommitGrid.CursorIndex >= Commits.Count - PrefetchRows) LoadPage(NextCursor);
        }

        private void RebuildRows()
        {
            List<VesselHistoryRow> rows = new List<VesselHistoryRow>();
            DateTime? lastDay = null;
            foreach (VesselCommit commit in Commits)
            {
                DateTime day = LocalTime(commit.CommittedUtc).Date;
                string label = lastDay.HasValue && lastDay.Value == day ? "" : day.ToString("ddd yyyy-MM-dd", CultureInfo.InvariantCulture);
                lastDay = day;
                rows.Add(new VesselHistoryRow(commit, label));
            }

            if (Commits.Count > 0)
            {
                if (CommitsLoading) rows.Add(new VesselHistoryRow(VesselHistoryRowKindEnum.Loading, Tr("Loading older commits...")));
                else if (CommitsError != null) rows.Add(new VesselHistoryRow(VesselHistoryRowKindEnum.Error, "! " + CommitsError + "  (F5 " + Tr("Retry") + ")"));
                else if (ReachedEnd) rows.Add(new VesselHistoryRow(VesselHistoryRowKindEnum.End, "-- " + Tr("End of history") + " --"));
            }

            if (CommitsLoading) CommitGrid.EmptyText = Tr("Loading commits...");
            else if (CommitsError != null) CommitGrid.EmptyText = "! " + CommitsError;
            else if (JumpDate.HasValue) CommitGrid.EmptyText = Tr("No commits on or before {{date}}.", LocalizationArgs.Of("date", JumpDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
            else CommitGrid.EmptyText = Tr("No commits on this branch.");
            CommitGrid.SetLocalRows(rows);
        }

        private OpsDocument BuildCommit(OpsDocument doc, VesselCommit c)
        {
            DateTime now = Context.Clock.UtcNow;
            doc.Text(c.Subject, doc.Theme.Text.WithAttribute(CellAttributes.Bold, true));
            if (!String.IsNullOrEmpty(c.Body))
            {
                doc.Blank();
                foreach (string line in c.Body.Split('\n')) doc.Text(line.TrimEnd('\r'));
            }

            doc.Section("Commit");
            doc.Field("SHA", c.Sha);
            doc.Field("Author", Person(c.AuthorName, c.AuthorEmail));
            doc.Field("Authored", When(c.AuthoredUtc, now));
            doc.Field("Committer", Person(c.CommitterName, c.CommitterEmail));
            doc.Field("Committed", When(c.CommittedUtc, now));
            doc.Field("Parents", c.ParentShas != null && c.ParentShas.Count > 0 ? String.Join(", ", c.ParentShas) : Tr("none (root commit)"));
            if (c.IsMerge) doc.Note("Merge commit: the file changes are against the first parent.");
            doc.Section("Files", " (" + c.FilesChanged.ToString(CultureInfo.InvariantCulture) + ")");
            doc.Text("+" + c.AddedLines.ToString(CultureInfo.InvariantCulture) + " -" + c.DeletedLines.ToString(CultureInfo.InvariantCulture), doc.Theme.Info);
            List<GitChangedFile> files = c.Files ?? new List<GitChangedFile>();
            if (files.Count == 0)
            {
                doc.Note("No file changes.");
            }
            else
            {
                List<IList<string>> rows = new List<IList<string>>();
                foreach (GitChangedFile f in files)
                {
                    string path = !String.IsNullOrEmpty(f.OldPath) && f.OldPath != f.Path ? f.OldPath + " -> " + f.Path : f.Path;
                    rows.Add(new List<string>
                    {
                        Tr(f.Kind.ToString()),
                        path,
                        f.IsBinary ? Tr("binary") : "+" + (f.AddedLines ?? 0).ToString(CultureInfo.InvariantCulture),
                        f.IsBinary ? "" : "-" + (f.DeletedLines ?? 0).ToString(CultureInfo.InvariantCulture),
                    });
                }

                doc.Table(new List<string> { Tr("Kind"), Tr("Path"), "+", "-" }, rows, 80);
            }

            if (c.FilesTruncated)
                doc.Text(Tr("Showing the first {{shown}} of {{total}} changed files.", LocalizationArgs.Of("shown", files.Count, "total", c.FilesChanged)), doc.Theme.Warning);
            return doc;
        }

        private string When(DateTime utc, DateTime now)
        {
            return LocalTime(utc).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " (" + Context.Loc.FormatRelative(utc, now) + ")";
        }

        private static string Person(string name, string email)
        {
            if (String.IsNullOrEmpty(email)) return String.IsNullOrEmpty(name) ? "-" : name;
            return (String.IsNullOrEmpty(name) ? "" : name + " ") + "<" + email + ">";
        }

        #endregion
    }
}

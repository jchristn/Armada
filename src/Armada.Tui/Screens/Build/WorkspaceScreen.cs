namespace Armada.Tui.Screens.Build
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Client.Models;
    using Armada.Core.Models;
    using Armada.Tui.Input;
    using Armada.Tui.Modals;
    using Armada.Tui.Routing;
    using Armada.Tui.Screens.Operations;
    using Armada.Tui.Services;
    using Armada.Tui.Text;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Widgets;
    using Button = Armada.Tui.Widgets.Button;

    /// <summary>
    /// Workspace (W4.6, <c>/workspace/:vesselId</c>), the dashboard's Workspace page for one vessel: the status line
    /// (branch, clean or dirty, active missions, ahead and behind), the compact readiness line, the file tree (folders
    /// load on demand; New File, New Folder, Rename, Delete, Metadata; <c>Space</c> marks files for Plan, Dispatch,
    /// and context curation), the editor with tabs for open files (unsaved changes marked <c>*</c>, <c>Ctrl+S</c>
    /// saves with the content hash so a concurrent change is refused, <c>Ctrl+E</c> opens <c>$EDITOR</c>; binary,
    /// too-large, and read-only files show a highlighted preview), Run Check, Plan and Dispatch with the selection
    /// handed off in the route query, the Vessel Context dialog with Append Selection, Review Diff, the terminal,
    /// file search, and Switch vessel. Expanded folders and recent files are remembered per vessel. Not thread-safe.
    /// </summary>
    public class WorkspaceScreen : OpsScreen
    {
        #region Public-Members

        /// <summary>
        /// Vessel id from the route.
        /// </summary>
        public string VesselId { get; }

        /// <summary>
        /// Every vessel (Switch vessel and the current vessel's record).
        /// </summary>
        public List<Vessel> Vessels { get; private set; } = new List<Vessel>();

        /// <summary>
        /// The current vessel once loaded.
        /// </summary>
        public Vessel? CurrentVessel
        {
            get { return Vessels.FirstOrDefault(v => v.Id == VesselId); }
        }

        /// <summary>
        /// Workspace status.
        /// </summary>
        public WorkspaceStatusResult? Status { get; private set; } = null;

        /// <summary>
        /// Readiness.
        /// </summary>
        public VesselReadinessResult? Readiness { get; private set; } = null;

        /// <summary>
        /// The file tree.
        /// </summary>
        public WorkspaceTreeView Tree { get; } = new WorkspaceTreeView();

        /// <summary>
        /// The editor for editable files.
        /// </summary>
        public WorkspaceEditor Editor { get; } = new WorkspaceEditor();

        /// <summary>
        /// The preview for binary, too-large, and read-only files.
        /// </summary>
        public WorkspaceCodeView Preview { get; } = new WorkspaceCodeView();

        /// <summary>
        /// Open tabs (file paths) in order.
        /// </summary>
        public List<string> OpenTabs { get; } = new List<string>();

        /// <summary>
        /// The active tab, or null.
        /// </summary>
        public string? ActivePath { get; private set; } = null;

        /// <summary>
        /// Loaded files by path.
        /// </summary>
        public Dictionary<string, WorkspaceFileResponse> Files { get; } = new Dictionary<string, WorkspaceFileResponse>(StringComparer.Ordinal);

        /// <summary>
        /// Editor drafts by path.
        /// </summary>
        public Dictionary<string, string> Drafts { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// Recently opened files, newest first.
        /// </summary>
        public List<string> RecentFiles { get; private set; } = new List<string>();

        /// <summary>
        /// The current error, or null.
        /// </summary>
        public string? Error { get; private set; } = null;

        /// <summary>
        /// Terminal output kept for the life of the screen.
        /// </summary>
        public List<WorkspaceTerminalLine> TerminalLines { get; } = new List<WorkspaceTerminalLine>();

        /// <summary>
        /// Terminal command history.
        /// </summary>
        public List<string> TerminalHistory { get; } = new List<string>();

        /// <inheritdoc />
        public override bool CanFocus { get; set; } = true;

        /// <inheritdoc />
        public override IReadOnlyList<KeyValuePair<string, string>> Hints
        {
            get
            {
                List<KeyValuePair<string, string>> hints = new List<KeyValuePair<string, string>>();
                hints.Add(new KeyValuePair<string, string>("Enter", "Open"));
                hints.Add(new KeyValuePair<string, string>("Space", "Select"));
                hints.Add(new KeyValuePair<string, string>("D", "Dispatch"));
                hints.Add(new KeyValuePair<string, string>("Tab", "Tree/Editor"));
                hints.Add(new KeyValuePair<string, string>("Ctrl+S", "Save"));
                return hints;
            }
        }

        #endregion

        #region Private-Members

        private string? _EditorPath = null;
        private bool _Switching = false;
        private bool _Loading = false;
        private bool _LoadingReadiness = false;
        private readonly List<OpsScreenAction> _Actions = new List<OpsScreenAction>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Services.</param>
        public WorkspaceScreen(RouteMatch route, TuiContext context)
            : base(route, context, "WorkspaceScreen", "Workspace")
        {
            VesselId = route.Param("vesselId") ?? "";
            Tree.ToggleDirectory = ToggleDirectory;
            Tree.OpenFile = path => OpenFile(path);
            Tree.Localizer = Localizer;
            Editor.ExternalEditor = (text, done) => EditExternally(text, done, Extension(ActivePath));
            Editor.Changed += (s, e) =>
            {
                if (!_Switching && _EditorPath != null) Drafts[_EditorPath] = Editor.Text;
            };
            Editor.Visible = false;
            Preview.Visible = false;
            AddChild(Tree);
            AddChild(Editor);
            AddChild(Preview);
            Scope.Focus(Tree);

            Act("save", "Save", Save, "ctrl+s", () => ActiveFile != null && ActiveFile.IsEditable && IsDirty(ActivePath!));
            Act("refresh", "Refresh", () => RefreshWorkspace(null), null);
            Act("run-check", "Run Check", RunCheck, "k", () => CurrentVessel != null);
            Act("plan", "Plan", Plan, "P", () => CurrentVessel != null && ActionablePaths().Count > 0);
            Act("dispatch", "Dispatch", Dispatch, "D", () => CurrentVessel != null && ActionablePaths().Count > 0);
            Act("context", "Context", OpenContext, "C", () => CurrentVessel != null);
            Act("diff", "Review Diff", ReviewDiff, "d", () => CurrentVessel != null);
            Act("terminal", "Terminal", OpenTerminal, "t", () => CurrentVessel != null);
            Act("search", "Search files", SearchFiles, "F", () => CurrentVessel != null);
            Act("switch", "Switch vessel", SwitchVessel, "V", () => Vessels.Count > 0);
            Act("readiness", "Readiness", () => OpsReadiness.ShowDetails(this, Readiness, _LoadingReadiness), "r");
            Act("new-file", "New File", NewFile, "n");
            Act("new-folder", "New Folder", NewFolder, "N");
            Act("rename", "Rename", () => RenamePath(TargetPath()), "R", () => TargetPath() != null);
            Act("delete", "Delete", () => DeletePath(TargetPath()), "del", () => TargetPath() != null);
            Act("metadata", "Metadata", ShowMetadata, "i", () => Tree.Current != null);
            Act("close-tab", "Close Tab", () => { if (ActivePath != null) CloseTab(ActivePath); }, "ctrl+w", () => ActivePath != null);
            Act("next-tab", "Next Tab", () => CycleTab(1), "ctrl+pagedown", () => OpenTabs.Count > 1);
            Act("previous-tab", "Previous Tab", () => CycleTab(-1), "ctrl+pageup", () => OpenTabs.Count > 1);
            Act("vessel", "Open Vessel", () => Context.Navigate("/vessels/" + Uri.EscapeDataString(VesselId)), "v");

            RestorePrefs();
            RememberVessel();
            Call((c, t) => c.ListVesselsAsync(new ArmadaPageQuery(1, 9999), t), r => Vessels = r?.Objects ?? new List<Vessel>(), null, ex => Error = String.IsNullOrEmpty(ex.Message) ? Tr("Failed to load vessels.") : ex.Message);
            LoadReadiness();
            RefreshWorkspace(Tree.Expanded.Where(p => p.Length > 0).ToList());
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The active file, or null.
        /// </summary>
        public WorkspaceFileResponse? ActiveFile
        {
            get { return ActivePath != null && Files.TryGetValue(ActivePath, out WorkspaceFileResponse? f) ? f : null; }
        }

        /// <summary>
        /// True when a file's draft differs from its saved content.
        /// </summary>
        /// <param name="path">Path.</param>
        /// <returns>True when dirty.</returns>
        public bool IsDirty(string path)
        {
            if (!Files.TryGetValue(path, out WorkspaceFileResponse? file)) return false;
            return Drafts.TryGetValue(path, out string? draft) && draft != file.Content;
        }

        /// <summary>
        /// The marked paths, or the active file when nothing is marked (the dashboard's <c>actionablePaths</c>).
        /// </summary>
        /// <returns>Paths.</returns>
        public List<string> ActionablePaths()
        {
            List<string> selected = Tree.Selected.Select(WorkspacePaths.Normalize).Where(p => p.Length > 0).ToList();
            if (selected.Count > 0) return selected;
            return ActivePath != null ? new List<string> { WorkspacePaths.Normalize(ActivePath) } : new List<string>();
        }

        /// <summary>
        /// Reload status and the tree, restoring the given expanded folders (or the current ones).
        /// </summary>
        /// <param name="restore">Folders to restore, or null for the current ones.</param>
        public void RefreshWorkspace(List<string>? restore)
        {
            if (String.IsNullOrEmpty(VesselId)) return;
            List<string> paths = (restore ?? Tree.Expanded.Where(p => p.Length > 0).ToList())
                .Select(WorkspacePaths.Normalize).Where(p => p.Length > 0).Distinct()
                .OrderBy(p => p.Split('/').Length).ThenBy(p => p, StringComparer.Ordinal).ToList();
            _Loading = true;
            string vesselId = VesselId;
            Call(async (c, t) =>
            {
                WorkspaceLoad load = new WorkspaceLoad();
                load.Status = await c.GetWorkspaceStatusAsync(vesselId, t).ConfigureAwait(false);
                WorkspaceTreeResult? root = await c.GetWorkspaceTreeAsync(vesselId, null, t).ConfigureAwait(false);
                load.Entries[""] = Normalize(root?.Entries);
                HashSet<string> expanded = new HashSet<string>(StringComparer.Ordinal) { "" };
                foreach (string path in paths)
                {
                    string parent = WorkspacePaths.Parent(path);
                    if (parent.Length > 0 && !expanded.Contains(parent)) continue;
                    try
                    {
                        WorkspaceTreeResult? tree = await c.GetWorkspaceTreeAsync(vesselId, path, t).ConfigureAwait(false);
                        load.Entries[path] = Normalize(tree?.Entries);
                        expanded.Add(path);
                    }
                    catch (ArmadaApiException)
                    {
                        // Skip folders that can no longer be restored.
                    }
                }

                load.Expanded = expanded.ToList();
                return load;
            }, load =>
            {
                _Loading = false;
                Status = load.Status;
                Tree.Entries.Clear();
                foreach (KeyValuePair<string, List<WorkspaceTreeEntry>> kv in load.Entries) Tree.Entries[kv.Key] = kv.Value;
                Tree.Expanded.Clear();
                foreach (string p in load.Expanded) Tree.Expanded.Add(p);
                Tree.Loading.Clear();
                Tree.EmptyText = "This folder is empty.";
                Error = null;
                SavePrefs();
            }, null, ex =>
            {
                _Loading = false;
                Tree.EmptyText = Tr("Failed to load Workspace.");
                Error = String.IsNullOrEmpty(ex.Message) ? Tr("Failed to load Workspace.") : ex.Message;
            });
        }

        /// <summary>
        /// Open a file in a tab (loading it once).
        /// </summary>
        /// <param name="path">Path.</param>
        /// <param name="focusEditor">Move focus to the editor.</param>
        public void OpenFile(string path, bool focusEditor = true)
        {
            string p = WorkspacePaths.Normalize(path);
            if (Files.ContainsKey(p))
            {
                Activate(p, focusEditor);
                return;
            }

            Call((c, t) => c.GetWorkspaceFileAsync(VesselId, p, t), file =>
            {
                if (file == null) return;
                Files[p] = file;
                if (!Drafts.ContainsKey(p)) Drafts[p] = file.Content ?? "";
                Error = null;
                Activate(p, focusEditor);
            }, null, ex => Error = String.IsNullOrEmpty(ex.Message) ? Tr("Failed to open file.") : ex.Message);
        }

        /// <summary>
        /// Save the active file.
        /// </summary>
        public void Save()
        {
            WorkspaceFileResponse? file = ActiveFile;
            string? path = ActivePath;
            if (file == null || path == null || !file.IsEditable) return;
            if (_EditorPath == path) Drafts[path] = Editor.Text;
            string content = Drafts.TryGetValue(path, out string? d) ? d : file.Content;
            WorkspaceSaveRequest request = new WorkspaceSaveRequest();
            request.Path = path;
            request.Content = content;
            request.ExpectedHash = String.IsNullOrEmpty(file.ContentHash) ? null : file.ContentHash;
            Call((c, t) => c.SaveWorkspaceFileAsync(VesselId, request, t), result =>
            {
                if (result == null) return;
                file.Content = content;
                file.ContentHash = result.ContentHash;
                file.SizeBytes = result.SizeBytes;
                file.LastWriteUtc = result.LastWriteUtc;
                file.IsEditable = true;
                file.IsBinary = false;
                file.IsLarge = false;
                file.PreviewTruncated = false;
                Drafts[path] = content;
                Error = null;
                Toast(NotificationSeverityEnum.Success, Tr("Saved {{path}}", LocalizationArgs.Of("path", path)));
                RefreshWorkspace(null);
            }, null, ex =>
            {
                string message = String.IsNullOrEmpty(ex.Message) ? Tr("Save failed.") : ex.Message;
                Error = message;
                Toast(NotificationSeverityEnum.Warning, message);
            });
        }

        /// <inheritdoc />
        public override Action? RefreshAction()
        {
            return () =>
            {
                RefreshWorkspace(null);
                LoadReadiness();
            };
        }

        /// <inheritdoc />
        public override int DefaultRefreshSeconds()
        {
            return 0;
        }

        /// <inheritdoc />
        public override IEnumerable<ArmadaCommand> Commands()
        {
            List<ArmadaCommand> list = new List<ArmadaCommand>();
            foreach (OpsScreenAction a in _Actions)
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
        public override FocusHints ResolveHints(FocusHints? inner, bool textEntry)
        {
            if (ReferenceEquals(Scope.Focused, Editor))
            {
                // The editor takes typed text: Space and D type there, so the first hint is the way back to the tree.
                return FocusHints.Typing("Esc", "Back to the file tree (then Space select, D dispatch)").Add("Ctrl+S", "Save").Add("Tab", "Next pane");
            }

            if (ReferenceEquals(Scope.Focused, Preview))
            {
                if (inner != null) return inner;
                return new FocusHints().Add("Esc", "Back to the file tree").Add("Up/Down", "Scroll").Add("Tab", "Next pane");
            }

            if (ReferenceEquals(Scope.Focused, Tree))
            {
                return FocusHints.Of(Hints);
            }

            return base.ResolveHints(inner, textEntry);
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            bool inTree = ReferenceEquals(Scope.Focused, Tree);
            bool ctrl = (key.Modifiers & KeyModifiers.Ctrl) != 0;
            foreach (OpsScreenAction a in _Actions)
            {
                if (a.Key == null) continue;
                bool chord = a.Key.StartsWith("ctrl+", StringComparison.Ordinal);
                if (!chord && !inTree) continue;
                if (!chord && ctrl) continue;
                if (Matches(a.Key, key) && a.Available)
                {
                    a.Run();
                    return true;
                }
            }

            if (key.Code == KeyCode.Tab)
            {
                Scope.Move((key.Modifiers & KeyModifiers.Shift) == 0);
                return true;
            }

            if (Scope.HandleKey(key)) return true;
            if (key.Code == KeyCode.Escape && !inTree)
            {
                Scope.Focus(Tree);
                return true;
            }

            return false;
        }

        /// <inheritdoc />
        public override bool HandlePaste(string text)
        {
            if (ReferenceEquals(Scope.Focused, Editor)) return Editor.HandlePaste(text);
            return false;
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            SurfaceText.FillRect(surface, new Rect(0, 0, width, height), Theme.Text);
            if (width < 20 || height < 8) return;
            Vessel? vessel = CurrentVessel;
            int y = 0;
            string title = vessel?.Name ?? (Vessels.Count == 0 ? Tr("Loading...") : VesselId);
            int x = SurfaceText.Draw(surface, 0, y, title, Theme.Accent.WithAttribute(CellAttributes.Bold, true), width);
            SurfaceText.Draw(surface, x + 2, y, Tr("Workspace") + "  (" + Tr("Workspace") + " > " + title + ")", Theme.Muted, Math.Max(0, width - x - 2));
            y++;
            WorkspaceStatusResult? s = Status;
            string status = "[" + (String.IsNullOrEmpty(s?.BranchName) ? Tr("No branch") : s!.BranchName) + "]  [" + Tr(s != null && s.IsDirty ? "Dirty working tree" : "Clean working tree") + "]  ["
                + (s?.ActiveMissionCount ?? 0) + " " + Tr("active mission(s)") + "]  [" + (s?.CommitsAhead ?? 0) + " " + Tr("ahead") + " / " + (s?.CommitsBehind ?? 0) + " " + Tr("behind") + "]";
            SurfaceText.Draw(surface, 0, y++, status, s != null && s.IsDirty ? Theme.Warning : Theme.Info, width);
            string keys = "Ctrl+S " + Tr("Save") + "  k " + Tr("Run Check") + "  P " + Tr("Plan") + "  D " + Tr("Dispatch") + "  C " + Tr("Context") + "  d " + Tr("Review Diff") + "  t " + Tr("Terminal") + "  F " + Tr("Search files") + "  V " + Tr("Switch vessel") + "  F5 " + Tr("Refresh");
            SurfaceText.Draw(surface, 0, y++, keys, Theme.Muted, width);
            string readiness = Tr("Readiness") + ": [" + Tr(OpsReadiness.Label(Readiness)) + "]";
            if (_LoadingReadiness) readiness += "  " + Tr("Checking readiness...");
            else if (Readiness != null)
                readiness += "  " + Tr(Readiness.HasWorkingDirectory ? "Working directory available" : "Working directory unavailable") + "  |  " + Tr(Readiness.HasRepositoryContext ? "Repository context available" : "Repository context unavailable") + "  (r)";
            SurfaceText.Draw(surface, 0, y++, readiness, OpsReadiness.Style(Readiness, Theme), width);
            if (s != null && !_Loading && !s.HasWorkingDirectory)
                SurfaceText.Draw(surface, 0, y++, "! " + (String.IsNullOrEmpty(s.Error) ? Tr("This vessel does not have a usable working directory.") : s.Error), Theme.Warning, width);
            if (Error != null) SurfaceText.Draw(surface, 0, y++, "! " + Error, Theme.Error, width);
            if (_Loading && Status == null) SurfaceText.Draw(surface, 0, y++, Tr("Loading Workspace..."), Theme.Muted, width);
            // The tree, the editor, and the preview are focus regions: each sits in a box (see RegionFrames) whose
            // edges are the blank row above the panes and the column between them.
            y++;
            int bodyTop = y;
            int bodyHeight = Math.Max(1, height - bodyTop);
            int treeWidth = Math.Clamp(width * 32 / 100, 22, 56);
            int treeTop = RenderTreePane(surface, new Rect(0, bodyTop, treeWidth, bodyHeight));
            int editorTop = RenderEditorPane(surface, new Rect(treeWidth + 1, bodyTop, Math.Max(1, width - treeWidth - 1), bodyHeight));
            // Above the boxes the column between the panes is a plain divider.
            int boxesTop = Math.Min(treeTop, editorTop) - 1;
            for (int r = bodyTop; r < Math.Min(boxesTop, height); r++) SurfaceText.Draw(surface, treeWidth, r, "|", Theme.Border, 1);
        }

        #endregion

        #region Private-Methods

        private static bool Matches(string keyText, KeyEvent key)
        {
            try { return KeyStroke.Parse(keyText).Matches(key); }
            catch (FormatException) { return false; }
        }

        private static List<WorkspaceTreeEntry> Normalize(List<WorkspaceTreeEntry>? entries)
        {
            List<WorkspaceTreeEntry> list = entries ?? new List<WorkspaceTreeEntry>();
            foreach (WorkspaceTreeEntry e in list) e.RelativePath = WorkspacePaths.Normalize(e.RelativePath);
            return list;
        }

        private static string Extension(string? path)
        {
            string name = WorkspacePaths.Name(path);
            int dot = name.LastIndexOf('.');
            return dot > 0 ? name.Substring(dot) : ".txt";
        }

        private void Act(string id, string label, Action run, string? key, Func<bool>? when = null)
        {
            _Actions.Add(new OpsScreenAction(id, label, run, key, when));
        }

        private int RenderTreePane(ISurface surface, Rect rect)
        {
            int y = rect.Y;
            string head = Tr("Files") + "  n " + Tr("New File") + "  N " + Tr("New Folder");
            SurfaceText.Draw(surface, rect.X, y++, head, ReferenceEquals(Scope.Focused, Tree) ? Theme.Accent : Theme.Muted, rect.Width);
            y++;
            List<string> selection = Tree.Selected.ToList();
            int footer = selection.Count > 0 ? 3 : 0;
            List<WorkspaceActiveMission> overlap = Overlapping();
            if (overlap.Count > 0) footer += footer > 0 ? 1 : 2;
            int treeTop = y;
            int treeHeight = Math.Max(1, rect.Y + rect.Height - y - footer);
            Scope.RenderChild(surface, Tree, new Rect(rect.X, y, rect.Width, treeHeight));
            y += treeHeight + 1;
            if (selection.Count > 0 && y < rect.Y + rect.Height)
            {
                SurfaceText.Draw(surface, rect.X, y++, Tr("Selection") + " (" + selection.Count + ")", Theme.Accent, rect.Width);
                if (y < rect.Y + rect.Height) SurfaceText.Draw(surface, rect.X, y++, String.Join(", ", selection), Theme.Text, rect.Width);
            }

            if (overlap.Count > 0 && y < rect.Y + rect.Height)
            {
                SurfaceText.Draw(surface, rect.X, y, "! " + Tr("Active mission overlap") + ": " + String.Join(", ", overlap.Select(m => m.Title + " (" + m.Status + ")")), Theme.Warning, rect.Width);
            }

            return treeTop;
        }

        private int RenderEditorPane(ISurface surface, Rect rect)
        {
            int y = rect.Y;
            int bottom = rect.Y + rect.Height;
            if (OpenTabs.Count > 0)
            {
                int x = rect.X;
                foreach (string path in OpenTabs)
                {
                    string label = WorkspacePaths.Name(path) + (IsDirty(path) ? "*" : "");
                    string text = path == ActivePath ? "[" + label + "]" : " " + label + " ";
                    if (x >= rect.X + rect.Width) break;
                    x += SurfaceText.Draw(surface, x, y, text, path == ActivePath ? Theme.Accent : Theme.Muted, rect.X + rect.Width - x) + 1;
                }

                y++;
            }

            WorkspaceFileResponse? file = ActiveFile;
            Editor.Visible = file != null && file.IsEditable;
            Preview.Visible = file != null && !file.IsEditable;
            if (file == null || ActivePath == null)
            {
                if (y < bottom) SurfaceText.Draw(surface, rect.X, y++, Tr("Open a file to begin"), Theme.Accent, rect.Width);
                if (y < bottom) SurfaceText.Draw(surface, rect.X, y, Tr("Browse the vessel tree, then select a file to start editing, planning, or dispatching scoped work."), Theme.Muted, rect.Width);
                return bottom + 1;
            }

            string info = file.Path + (IsDirty(ActivePath) ? "   * " + Tr("Unsaved changes") : "") + (file.IsEditable ? "   " + Editor.Position() : "") + "   Ctrl+W " + Tr("Close Tab") + "  R " + Tr("Rename") + "  Del " + Tr("Delete");
            if (y < bottom) SurfaceText.Draw(surface, rect.X, y++, info, Theme.Muted, rect.Width);
            if (!file.IsEditable && y < bottom)
            {
                string warning = file.IsBinary
                    ? Tr("This file is binary and cannot be edited in Workspace.")
                    : file.IsLarge
                        ? Tr("This file is too large for editing in Workspace. A preview is shown below.")
                        : Tr("This file is read-only in Workspace.");
                SurfaceText.Draw(surface, rect.X, y++, "! " + warning, Theme.Warning, rect.Width);
            }

            y++;
            Rect body = new Rect(rect.X, y, rect.Width, Math.Max(1, bottom - y));
            if (file.IsEditable) Scope.RenderChild(surface, Editor, body);
            else Scope.RenderChild(surface, Preview, body);
            return y;
        }

        private List<WorkspaceActiveMission> Overlapping()
        {
            List<string> paths = ActionablePaths();
            if (Status?.ActiveMissions == null || Status.ActiveMissions.Count == 0 || paths.Count == 0) return new List<WorkspaceActiveMission>();
            return Status.ActiveMissions.Where(m => (m.ScopedFiles ?? new List<string>()).Any(f => paths.Contains(WorkspacePaths.Normalize(f)))).ToList();
        }

        private void Activate(string path, bool focusEditor)
        {
            if (!OpenTabs.Contains(path)) OpenTabs.Add(path);
            ActivePath = path;
            Tree.ActivePath = path;
            RecentFiles.Remove(path);
            RecentFiles.Insert(0, path);
            while (RecentFiles.Count > 12) RecentFiles.RemoveAt(RecentFiles.Count - 1);
            ShowActive();
            if (focusEditor)
            {
                WorkspaceFileResponse? file = ActiveFile;
                if (file != null) Scope.Focus(file.IsEditable ? (IWidget)Editor : Preview);
            }

            SavePrefs();
            RememberVessel();
        }

        private void ShowActive()
        {
            WorkspaceFileResponse? file = ActiveFile;
            _Switching = true;
            try
            {
                if (file == null || ActivePath == null)
                {
                    _EditorPath = null;
                    Editor.Text = "";
                    Preview.Content = "";
                    return;
                }

                if (file.IsEditable)
                {
                    _EditorPath = ActivePath;
                    Editor.Text = Drafts.TryGetValue(ActivePath, out string? d) ? d : file.Content;
                    Editor.Visible = true;
                    Preview.Visible = false;
                }
                else
                {
                    _EditorPath = null;
                    Preview.Content = file.Content ?? "";
                    Preview.Language = String.IsNullOrEmpty(file.Language) ? WorkspacePaths.InferLanguage(file.Path) : file.Language;
                    Editor.Visible = false;
                    Preview.Visible = true;
                }
            }
            finally
            {
                _Switching = false;
            }
        }

        private void CloseTab(string path)
        {
            int idx = OpenTabs.IndexOf(path);
            if (idx < 0) return;
            OpenTabs.RemoveAt(idx);
            if (ActivePath == path)
            {
                ActivePath = OpenTabs.Count > 0 ? OpenTabs[OpenTabs.Count - 1] : null;
                Tree.ActivePath = ActivePath;
                ShowActive();
                if (ActivePath == null) Scope.Focus(Tree);
            }
        }

        private void CycleTab(int direction)
        {
            if (OpenTabs.Count < 2 || ActivePath == null) return;
            int idx = OpenTabs.IndexOf(ActivePath);
            int next = (idx + direction + OpenTabs.Count) % OpenTabs.Count;
            Activate(OpenTabs[next], ReferenceEquals(Scope.Focused, Editor) || ReferenceEquals(Scope.Focused, Preview));
        }

        private string? TargetPath()
        {
            if (ReferenceEquals(Scope.Focused, Tree) && Tree.Current != null) return Tree.Current.RelativePath;
            return ActivePath ?? Tree.Selected.FirstOrDefault();
        }

        private void ToggleDirectory(string path)
        {
            string p = WorkspacePaths.Normalize(path);
            if (Tree.Expanded.Contains(p) && p.Length > 0)
            {
                foreach (string e in Tree.Expanded.Where(x => WorkspacePaths.InScope(x, p)).ToList()) Tree.Expanded.Remove(e);
                Error = null;
                SavePrefs();
                return;
            }

            if (Tree.Entries.ContainsKey(p))
            {
                Tree.Expanded.Add(p);
                Error = null;
                SavePrefs();
                return;
            }

            if (Tree.Loading.Contains(p)) return;
            Tree.Loading.Add(p);
            Call((c, t) => c.GetWorkspaceTreeAsync(VesselId, p.Length > 0 ? p : null, t), tree =>
            {
                Tree.Loading.Remove(p);
                Tree.Entries[p] = Normalize(tree?.Entries);
                Tree.Expanded.Add(p);
                Error = null;
                SavePrefs();
            }, null, ex =>
            {
                Tree.Loading.Remove(p);
                string message = String.IsNullOrEmpty(ex.Message) ? Tr("Failed to load directory.") : ex.Message;
                if (message.Contains("Workspace path not found", StringComparison.Ordinal))
                {
                    Tree.Expanded.Remove(p);
                    Tree.Entries.Remove(p);
                }
                else
                {
                    Error = message;
                }
            });
        }

        private void LoadReadiness()
        {
            if (String.IsNullOrEmpty(VesselId)) return;
            _LoadingReadiness = true;
            Call((c, t) => c.GetVesselReadinessAsync(VesselId, null, t), r =>
            {
                _LoadingReadiness = false;
                Readiness = r;
            }, null, ex =>
            {
                _LoadingReadiness = false;
                Readiness = null;
            });
        }

        private void RestorePrefs()
        {
            if (Context.Prefs.Current.Workspaces.TryGetValue(VesselId, out WorkspacePreferences? prefs) && prefs != null)
            {
                foreach (string p in prefs.ExpandedPaths ?? new List<string>()) Tree.Expanded.Add(WorkspacePaths.Normalize(p));
                RecentFiles = (prefs.RecentFiles ?? new List<string>()).ToList();
            }
        }

        private void SavePrefs()
        {
            if (String.IsNullOrEmpty(VesselId)) return;
            WorkspacePreferences prefs = new WorkspacePreferences();
            prefs.ExpandedPaths = Tree.Expanded.Where(p => p.Length > 0).OrderBy(p => p, StringComparer.Ordinal).ToList();
            prefs.RecentFiles = RecentFiles.ToList();
            Context.Prefs.Current.Workspaces[VesselId] = prefs;
            Context.Prefs.Save();
        }

        private void RememberVessel()
        {
            if (String.IsNullOrEmpty(VesselId)) return;
            List<string> recent = Context.Prefs.Current.WorkspaceRecentVessels ?? new List<string>();
            recent.Remove(VesselId);
            recent.Insert(0, VesselId);
            while (recent.Count > 8) recent.RemoveAt(recent.Count - 1);
            Context.Prefs.Current.WorkspaceRecentVessels = recent;
            Context.Prefs.Save();
        }

        private void Prompt(string title, string label, string initial, Action<string> done)
        {
            OpsFormDialog dialog = NewForm(title, "OK");
            InputField input = new InputField();
            input.Value = initial ?? "";
            input.Validator = v => String.IsNullOrWhiteSpace(v) ? "A path is required." : null;
            dialog.AddField(label, input);
            dialog.Submit = d =>
            {
                string value = input.Value.Trim();
                Post(() => done(value));
                return true;
            };
            Context.Modals.Show(dialog);
        }

        private void NewFile()
        {
            string parent = ActivePath != null ? WorkspacePaths.Parent(ActivePath) : "";
            Prompt("New File", "New file path", parent.Length > 0 ? parent + "/new-file.txt" : "new-file.txt", value =>
            {
                string p = WorkspacePaths.Normalize(value);
                if (p.Length == 0) return;
                WorkspaceFileResponse draft = new WorkspaceFileResponse();
                draft.VesselId = VesselId;
                draft.Path = p;
                draft.Name = WorkspacePaths.Name(p);
                draft.Content = "";
                draft.ContentHash = "";
                draft.IsEditable = true;
                draft.LastWriteUtc = Context.Clock.UtcNow;
                draft.Language = WorkspacePaths.InferLanguage(p);
                Files[p] = draft;
                Drafts[p] = "";
                if (!Tree.Selected.Contains(p)) Tree.Selected.Add(p);
                string folder = WorkspacePaths.Parent(p);
                Tree.Expanded.Add(folder);
                Activate(p, true);
                RefreshWorkspace(null);
            });
        }

        private void NewFolder()
        {
            string parent = ActivePath != null ? WorkspacePaths.Parent(ActivePath) : "";
            Prompt("New Folder", "New folder path", parent.Length > 0 ? parent + "/new-folder" : "new-folder", value =>
            {
                string p = WorkspacePaths.Normalize(value);
                if (p.Length == 0) return;
                WorkspaceCreateDirectoryRequest request = new WorkspaceCreateDirectoryRequest();
                request.Path = p;
                Call((c, t) => c.CreateWorkspaceDirectoryAsync(VesselId, request, t), r =>
                {
                    Error = null;
                    Toast(NotificationSeverityEnum.Success, Tr("Created folder {{path}}", LocalizationArgs.Of("path", p)));
                    RefreshWorkspace(null);
                }, null, ex => Error = String.IsNullOrEmpty(ex.Message) ? Tr("Failed to create folder.") : ex.Message);
            });
        }

        private void RenamePath(string? target)
        {
            if (target == null) return;
            string source = WorkspacePaths.Normalize(target);
            Prompt("Rename", "Rename or move path", source, value =>
            {
                string next = WorkspacePaths.Normalize(value);
                if (next.Length == 0 || next == source) return;
                WorkspaceRenameRequest request = new WorkspaceRenameRequest();
                request.Path = source;
                request.NewPath = next;
                Call((c, t) => c.RenameWorkspaceEntryAsync(VesselId, request, t), result =>
                {
                    string moved = WorkspacePaths.Normalize(String.IsNullOrEmpty(result?.NewPath) ? next : result!.NewPath);
                    List<string> expanded = Tree.Expanded.Select(p => WorkspacePaths.Remap(p, source, moved)).Where(p => p.Length > 0).ToList();
                    RemapAll(source, moved);
                    Error = null;
                    Toast(NotificationSeverityEnum.Success, Tr("Renamed {{path}}", LocalizationArgs.Of("path", result?.Path ?? source)));
                    RefreshWorkspace(expanded);
                }, null, ex => Error = String.IsNullOrEmpty(ex.Message) ? Tr("Rename failed.") : ex.Message);
            });
        }

        private void RemapAll(string source, string target)
        {
            for (int i = 0; i < OpenTabs.Count; i++) OpenTabs[i] = WorkspacePaths.Remap(OpenTabs[i], source, target);
            for (int i = 0; i < Tree.Selected.Count; i++) Tree.Selected[i] = WorkspacePaths.Remap(Tree.Selected[i], source, target);
            RecentFiles = RecentFiles.Select(p => WorkspacePaths.Remap(p, source, target)).ToList();
            foreach (string key in Files.Keys.ToList())
            {
                string moved = WorkspacePaths.Remap(key, source, target);
                if (moved == key) continue;
                WorkspaceFileResponse f = Files[key];
                Files.Remove(key);
                f.Path = moved;
                f.Name = WorkspacePaths.Name(moved);
                Files[moved] = f;
            }

            foreach (string key in Drafts.Keys.ToList())
            {
                string moved = WorkspacePaths.Remap(key, source, target);
                if (moved == key) continue;
                string d = Drafts[key];
                Drafts.Remove(key);
                Drafts[moved] = d;
            }

            if (ActivePath != null) ActivePath = WorkspacePaths.Remap(ActivePath, source, target);
            if (_EditorPath != null) _EditorPath = WorkspacePaths.Remap(_EditorPath, source, target);
            Tree.ActivePath = ActivePath;
        }

        private void DeletePath(string? target)
        {
            if (target == null) return;
            string p = WorkspacePaths.Normalize(target);
            Confirm("Delete", Tr("Delete {{path}}?", LocalizationArgs.Of("path", p)), () =>
            {
                Call((c, t) => c.DeleteWorkspaceEntryAsync(VesselId, p, t), r =>
                {
                    List<string> expanded = Tree.Expanded.Where(x => x.Length > 0 && !WorkspacePaths.InScope(x, p)).ToList();
                    OpenTabs.RemoveAll(x => WorkspacePaths.InScope(x, p));
                    Tree.Selected.RemoveAll(x => WorkspacePaths.InScope(x, p));
                    RecentFiles.RemoveAll(x => WorkspacePaths.InScope(x, p));
                    foreach (string key in Files.Keys.Where(k => WorkspacePaths.InScope(k, p)).ToList()) Files.Remove(key);
                    foreach (string key in Drafts.Keys.Where(k => WorkspacePaths.InScope(k, p)).ToList()) Drafts.Remove(key);
                    if (ActivePath != null && WorkspacePaths.InScope(ActivePath, p))
                    {
                        ActivePath = OpenTabs.Count > 0 ? OpenTabs[OpenTabs.Count - 1] : null;
                        Tree.ActivePath = ActivePath;
                        ShowActive();
                    }

                    Error = null;
                    Toast(NotificationSeverityEnum.Warning, Tr("Deleted {{path}}", LocalizationArgs.Of("path", p)));
                    RefreshWorkspace(expanded);
                }, null, ex => Error = String.IsNullOrEmpty(ex.Message) ? Tr("Delete failed.") : ex.Message);
            }, "Delete");
        }

        private void ShowMetadata()
        {
            WorkspaceTreeEntry? e = Tree.Current;
            if (e == null) return;
            OpsDocumentView view = new OpsDocumentView();
            view.Builder = doc =>
            {
                doc.Field("Name", e.Name);
                doc.Field("Path", e.RelativePath);
                doc.Field("Type", Tr(e.IsDirectory ? "Folder" : "File"));
                doc.Field("Editable", Tr(e.IsEditable ? "Yes" : "No"));
                doc.Field("Size", e.SizeBytes.HasValue ? e.SizeBytes.Value + " bytes" : Tr("Directory"));
                doc.Field("Modified", Context.Loc.FormatDateTime(e.LastWriteUtc));
                if (!e.IsDirectory && Files.TryGetValue(e.RelativePath, out WorkspaceFileResponse? f)) doc.Field("Language", f.Language);
                return doc;
            };
            ViewerModal modal = new ViewerModal(Tr("Entry Metadata"), view, Context.Loc, Context.Theme.Current);
            modal.CopyRequested += (s, a) => Context.Clipboard.Copy(view.PlainText, "Metadata");
            Context.Modals.Show(modal);
        }

        private void RunCheck()
        {
            Vessel? vessel = CurrentVessel;
            if (vessel == null) return;
            List<string> paths = ActionablePaths();
            Dictionary<string, string> q = new Dictionary<string, string>(StringComparer.Ordinal);
            q["tab"] = "checks";
            q["prefill"] = "1";
            q["vesselId"] = vessel.Id;
            string branch = !String.IsNullOrEmpty(Status?.BranchName) ? Status!.BranchName! : vessel.DefaultBranch ?? "";
            if (branch.Length > 0) q["branchName"] = branch;
            q["label"] = paths.Count > 0 ? vessel.Name + ": " + paths[0] : vessel.Name;
            Context.Navigate("/delivery" + RouteMatch.BuildQuery(q));
        }

        private void Plan()
        {
            Vessel? vessel = CurrentVessel;
            List<string> paths = ActionablePaths();
            if (vessel == null || paths.Count == 0) return;
            WorkspaceDraft draft = WorkspacePaths.PlanningDraft(vessel, paths);
            Context.Navigate(OpsHandoff.Planning(OpsHandoff.FromWorkspace, null, null, vessel.Id, vessel.FleetId, null, draft.Title, draft.Prompt));
        }

        private void Dispatch()
        {
            Vessel? vessel = CurrentVessel;
            List<string> paths = ActionablePaths();
            if (vessel == null || paths.Count == 0) return;
            WorkspaceDraft draft = WorkspacePaths.DispatchDraft(vessel, paths);
            Context.Navigate(OpsHandoff.Dispatch(OpsHandoff.FromWorkspace, vessel.Id, null, draft.Prompt, draft.Title, null, null));
        }

        private void OpenContext()
        {
            Vessel? vessel = CurrentVessel;
            if (vessel == null) return;
            OpsFormDialog dialog = NewForm("Vessel Context", "Save Context");
            dialog.WidthRatio = 0.9;
            dialog.Intro = "Edit the vessel context fields directly, or append the current Workspace selection into any field.";
            OpsTextArea project = ContextArea(vessel.ProjectContext);
            OpsTextArea style = ContextArea(vessel.StyleGuide);
            OpsTextArea model = ContextArea(vessel.ModelContext);
            bool canAppend = ActionablePaths().Count > 0;
            dialog.AddField("Project Context", project, null, 5);
            dialog.AddField("", AppendButton(project, canAppend));
            dialog.AddField("Style Guide", style, null, 5);
            dialog.AddField(" ", AppendButton(style, canAppend));
            dialog.AddField("Model Context", model, null, 5);
            dialog.AddField("  ", AppendButton(model, canAppend));
            dialog.Submit = d =>
            {
                VesselUpsertRequest body = VesselUpsertRequest.From(vessel);
                body.ProjectContext = project.Text;
                body.StyleGuide = style.Text;
                body.ModelContext = model.Text;
                Call((c, t) => c.UpdateVesselAsync(vessel.Id, body, t), updated =>
                {
                    d.Complete();
                    if (updated != null)
                    {
                        int idx = Vessels.FindIndex(v => v.Id == updated.Id);
                        if (idx >= 0) Vessels[idx] = updated;
                    }

                    Error = null;
                    Toast(NotificationSeverityEnum.Success, Tr("Saved vessel context."));
                }, null, ex => d.Fail(String.IsNullOrEmpty(ex.Message) ? Tr("Failed to save vessel context.") : ex.Message));
                return false;
            };
            Context.Modals.Show(dialog);
        }

        private OpsTextArea ContextArea(string? text)
        {
            OpsTextArea area = new OpsTextArea();
            area.Text = text ?? "";
            area.ExternalEditor = (t, done) => EditExternally(t, done);
            return area;
        }

        private Button AppendButton(OpsTextArea target, bool enabled)
        {
            Button button = new Button("Append Selection", () => AppendSelection(target));
            button.Enabled = enabled;
            return button;
        }

        private void AppendSelection(OpsTextArea target)
        {
            Vessel? vessel = CurrentVessel;
            List<string> paths = ActionablePaths().Take(6).ToList();
            if (vessel == null || paths.Count == 0) return;
            Dictionary<string, string> cached = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string p in paths)
            {
                if (Files.TryGetValue(p, out WorkspaceFileResponse? f) && !f.IsBinary && !f.IsLarge)
                    cached[p] = Drafts.TryGetValue(p, out string? d) ? d : f.Content;
            }

            string vesselId = vessel.Id;
            Call(async (c, t) =>
            {
                List<KeyValuePair<string, string>> files = new List<KeyValuePair<string, string>>();
                foreach (string p in paths)
                {
                    if (cached.TryGetValue(p, out string? text))
                    {
                        files.Add(new KeyValuePair<string, string>(p, text));
                        continue;
                    }

                    WorkspaceFileResponse? response = await c.GetWorkspaceFileAsync(vesselId, p, t).ConfigureAwait(false);
                    if (response == null || response.IsBinary || response.IsLarge) continue;
                    files.Add(new KeyValuePair<string, string>(p, response.Content ?? ""));
                }

                return files.Where(f => !String.IsNullOrEmpty(f.Value)).ToList();
            }, files =>
            {
                if (files.Count == 0)
                {
                    Toast(NotificationSeverityEnum.Warning, Tr("No text files were available for context curation."));
                    return;
                }

                string snippet = WorkspacePaths.ContextSnippet(files);
                string existing = target.Text.Trim();
                target.Text = existing.Length > 0 ? existing + "\n\n" + snippet : snippet;
            }, null, ex => Error = String.IsNullOrEmpty(ex.Message) ? Tr("Failed to build context from selection.") : ex.Message);
        }

        private void ReviewDiff()
        {
            Call((c, t) => c.GetWorkspaceDiffAsync(VesselId, null, t), result =>
            {
                if (result == null) return;
                if (!String.IsNullOrEmpty(result.Error))
                {
                    ShowMessage(result.Error!);
                    return;
                }

                if (String.IsNullOrWhiteSpace(result.Diff))
                {
                    Toast(NotificationSeverityEnum.Info, Tr("No tracked changes against HEAD."));
                    return;
                }

                ShowDiff(Tr("Review Diff"), result.Diff);
            }, null, ex => ShowMessage(String.IsNullOrEmpty(ex.Message) ? Tr("Failed to load diff.") : ex.Message));
        }

        private void OpenTerminal()
        {
            WorkspaceTerminalDialog dialog = new WorkspaceTerminalDialog(this, VesselId, TerminalLines, TerminalHistory);
            Context.Modals.Show(dialog);
        }

        private void SearchFiles()
        {
            Prompt("Search files", "Text to find", "", query =>
            {
                if (String.IsNullOrWhiteSpace(query)) return;
                Call((c, t) => c.SearchWorkspaceAsync(VesselId, query, 200, t), result =>
                {
                    List<OpsLinkItem> items = new List<OpsLinkItem>();
                    foreach (WorkspaceSearchMatch m in result?.Matches ?? new List<WorkspaceSearchMatch>())
                    {
                        string path = WorkspacePaths.Normalize(m.Path);
                        items.Add(new OpsLinkItem(path + ":" + m.LineNumber + "  " + (m.Preview ?? "").Trim(), () =>
                        {
                            DropTopModal();
                            OpenFile(path);
                        }));
                    }

                    if (items.Count == 0) items.Add(new OpsLinkItem(Tr("No matches."), null));
                    OpsLinkList list = new OpsLinkList();
                    list.Items = items;
                    list.OnFocusChanged(true);
                    string title = Tr("Search files") + ": " + query + " (" + (result?.TotalMatches ?? 0) + (result != null && result.Truncated ? "+" : "") + ")";
                    ViewerModal modal = new ViewerModal(title, list, Context.Loc, Context.Theme.Current);
                    modal.WidthRatio = 0.9;
                    Context.Modals.Show(modal);
                }, null, ex => ShowMessage(ex.Message));
            });
        }

        private void DropTopModal()
        {
            if (Context.App.Modals.Top is ArmadaDialog dialog) dialog.RequestClose(null);
            DropClosedModals();
        }

        private void SwitchVessel()
        {
            SelectField<string> picker = NewSelect("Switch vessel", Vessels.OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase).Select(v => new SelectOption<string>(v.Id, v.Name, v.WorkingDirectory ?? "")).ToList());
            picker.SetValue(VesselId);
            picker.ValueChanged += (s, e) =>
            {
                if (!String.IsNullOrEmpty(picker.Value) && picker.Value != VesselId) Context.Navigate("/workspace/" + Uri.EscapeDataString(picker.Value!));
            };
            picker.Open();
        }

        #endregion
    }
}

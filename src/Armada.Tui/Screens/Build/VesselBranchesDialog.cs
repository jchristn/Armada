namespace Armada.Tui.Screens.Build
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Models;
    using Armada.Tui.Modals;
    using Armada.Tui.Screens.Operations;
    using Armada.Tui.Services;
    using Armada.Tui.Text;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;

    /// <summary>
    /// The dashboard's Manage Branches modal for a vessel: each branch with its default and current tags, commits
    /// ahead and behind, and last commit; Push (<c>p</c>) and copy the name (<c>y</c>) on the selected branch; Refresh
    /// (<c>r</c>); and Merge a branch (source, target, push after merge; <c>m</c> or the Merge button). Not thread-safe.
    /// </summary>
    public class VesselBranchesDialog : ArmadaDialog
    {
        #region Public-Members

        /// <summary>
        /// Branch table.
        /// </summary>
        public ArmadaGrid<BranchInfo> Grid { get; } = new ArmadaGrid<BranchInfo>(b => b.Name);

        /// <summary>
        /// Merge source.
        /// </summary>
        public SelectField<string> Source { get; } = new SelectField<string>();

        /// <summary>
        /// Merge target.
        /// </summary>
        public SelectField<string> Target { get; } = new SelectField<string>();

        /// <summary>
        /// Push after merge (on by default, as on the dashboard).
        /// </summary>
        public OpsCheckField PushAfterMerge { get; } = new OpsCheckField("Push after merge", true);

        /// <summary>
        /// Merge button.
        /// </summary>
        public Button MergeButton { get; }

        /// <summary>
        /// Branches from the last load.
        /// </summary>
        public List<BranchInfo> Branches { get; private set; } = new List<BranchInfo>();

        /// <summary>
        /// Error from the last load, or null.
        /// </summary>
        public string? Error { get; private set; } = null;

        /// <summary>
        /// True while loading.
        /// </summary>
        public bool Loading { get; private set; } = false;

        /// <summary>
        /// Current busy key (<c>push:NAME</c> or <c>merge</c>), or empty.
        /// </summary>
        public string Busy { get; private set; } = "";

        #endregion

        #region Private-Members

        private string? _PreselectSource = null;
        private string? _PreselectTarget = null;

        private readonly OpsScreen _Screen;
        private readonly string _VesselId;
        private readonly FocusScope _Scope = new FocusScope();
        private int _ScreenWidth = 100;
        private int _ScreenHeight = 30;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate and load.
        /// </summary>
        /// <param name="screen">Owning screen (API calls and toasts).</param>
        /// <param name="vesselId">Vessel id.</param>
        /// <param name="vesselName">Vessel name.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public VesselBranchesDialog(OpsScreen screen, string vesselId, string vesselName)
            : base(TitleOf(screen, vesselName), screen?.Context.Loc, screen?.Context.Theme.Current)
        {
            _Screen = screen ?? throw new ArgumentNullException(nameof(screen));
            _VesselId = vesselId ?? throw new ArgumentNullException(nameof(vesselId));
            Grid.MultiSelect = false;
            Grid.ShowPagingBar = false;
            Grid.PageSize = 10000;
            Grid.ModalHost = screen.Context.Modals;
            Grid.EmptyText = "No branches found.";
            Grid.AddColumn(new GridColumn<BranchInfo>("branch", "Branch", b => b.Name + (b.IsDefault ? "  [" + T("default") + "]" : "") + (b.IsCurrent ? "  [" + T("current") + "]" : "")) { Weight = 3 });
            Grid.AddColumn(new GridColumn<BranchInfo>("aheadBehind", "Ahead / Behind", b => "+" + b.Ahead + " / -" + b.Behind) { Width = 15 });
            Grid.AddColumn(new GridColumn<BranchInfo>("commit", "Last Commit", b => (String.IsNullOrEmpty(b.CommitSubject) ? "-" : b.CommitSubject!) + (String.IsNullOrEmpty(b.CommitHash) ? "" : "  " + b.CommitHash) + (b.CommitDate.HasValue ? " -- " + screen.Context.Loc.FormatDateTime(b.CommitDate.Value) : "")) { Weight = 5 });
            Grid.Activated += (s, b) => Push(b.Name);
            Source.ModalHost = screen.Context.Modals;
            Source.PickerTitle = "Source";
            Source.Placeholder = "Select...";
            Target.ModalHost = screen.Context.Modals;
            Target.PickerTitle = "Target";
            Target.Placeholder = "Select...";
            MergeButton = new Button("Merge", Merge);
            foreach (ArmadaWidget w in new ArmadaWidget[] { Grid, Source, Target, PushAfterMerge, MergeButton })
            {
                w.Localizer = Localizer;
                w.ApplyTheme(Theme);
            }

            _Scope.Wrap = true;
            _Scope.Add(Grid);
            _Scope.Add(Source);
            _Scope.Add(Target);
            _Scope.Add(PushAfterMerge);
            _Scope.Add(MergeButton);
            _Scope.Focus(Grid);
            _Scope.SetActive(true);
            FooterHint = " p " + T("Push") + "  y " + T("Copy branch name") + "  r " + T("Refresh") + "  m " + T("Merge") + "  Tab " + T("Merge a branch") + "  Esc " + T("Close") + " ";
            MinContentWidth = 60;
            MaxContentWidth = 160;
            Load();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Preselect the merge source and target (for example a mission's branch into the vessel's default branch); they
        /// apply when the branch list loads.
        /// </summary>
        /// <param name="source">Source branch.</param>
        /// <param name="target">Target branch, or null.</param>
        public void PreselectMerge(string? source, string? target)
        {
            _PreselectSource = String.IsNullOrEmpty(source) ? null : source;
            _PreselectTarget = String.IsNullOrEmpty(target) ? null : target;
            if (Branches.Count > 0)
            {
                if (_PreselectSource != null) Source.SetValue(_PreselectSource);
                if (_PreselectTarget != null) Target.SetValue(_PreselectTarget);
            }
        }

        /// <summary>
        /// Reload the branches.
        /// </summary>
        public void Load()
        {
            Loading = true;
            Error = null;
            _Screen.Call((c, t) => c.GetVesselBranchesAsync(_VesselId, t), r =>
            {
                Loading = false;
                Branches = r?.Branches ?? new List<BranchInfo>();
                if (!String.IsNullOrEmpty(r?.Error)) Error = r!.Error;
                Grid.SetLocalRows(Branches);
                List<SelectOption<string>> options = Branches.Select(b => new SelectOption<string>(b.Name, b.Name)).ToList();
                string? source = Source.Value ?? _PreselectSource;
                string? target = Target.Value ?? _PreselectTarget;
                Source.Options = options;
                Target.Options = options;
                Source.SetValue(source);
                Target.SetValue(target);
            }, null, ex =>
            {
                Loading = false;
                Error = ex.Message;
            });
        }

        /// <summary>
        /// Push a branch.
        /// </summary>
        /// <param name="branch">Branch name.</param>
        public void Push(string branch)
        {
            if (String.IsNullOrEmpty(branch) || Busy.Length > 0) return;
            Busy = "push:" + branch;
            _Screen.Call((c, t) => c.PushVesselBranchAsync(_VesselId, branch, t), r =>
            {
                Busy = "";
                _Screen.Toast(NotificationSeverityEnum.Success, _Screen.Tr("Pushed {{branch}}", LocalizationArgs.Of("branch", branch)));
                Load();
            }, null, ex =>
            {
                Busy = "";
                _Screen.Toast(NotificationSeverityEnum.Error, _Screen.Tr("Push failed: {{message}}", LocalizationArgs.Of("message", ex.Message)));
            });
        }

        /// <summary>
        /// Merge the chosen source into the chosen target (warns when either is missing or they are the same).
        /// </summary>
        public void Merge()
        {
            if (Busy.Length > 0) return;
            string source = Source.Value ?? "";
            string target = Target.Value ?? "";
            if (source.Length == 0 || target.Length == 0)
            {
                _Screen.Toast(NotificationSeverityEnum.Warning, _Screen.Tr("Select a source and a target branch."));
                return;
            }

            if (source == target)
            {
                _Screen.Toast(NotificationSeverityEnum.Warning, _Screen.Tr("Source and target must differ."));
                return;
            }

            bool push = PushAfterMerge.Checked;
            Busy = "merge";
            _Screen.Call((c, t) => c.MergeVesselBranchAsync(_VesselId, source, target, push, t), r =>
            {
                Busy = "";
                _Screen.Toast(NotificationSeverityEnum.Success, _Screen.Tr("Merged {{source}} into {{target}}", LocalizationArgs.Of("source", source, "target", target)));
                Load();
            }, null, ex =>
            {
                Busy = "";
                _Screen.Toast(NotificationSeverityEnum.Error, _Screen.Tr("Merge failed: {{message}}", LocalizationArgs.Of("message", ex.Message)));
            });
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (HandleDismiss(key, null)) return true;
            if (key.Code == KeyCode.Tab)
            {
                _Scope.Move((key.Modifiers & KeyModifiers.Shift) == 0);
                return true;
            }

            bool plain = key.Code == KeyCode.Character && key.Modifiers == KeyModifiers.None;
            if (plain && ReferenceEquals(_Scope.Focused, Grid))
            {
                BranchInfo? current = Grid.Current;
                switch (key.Rune)
                {
                    case 'p':
                        if (current != null) Push(current.Name);
                        return true;
                    case 'y':
                        if (current != null) _Screen.Copy(current.Name, "Branch");
                        return true;
                    case 'r':
                        Load();
                        return true;
                    case 'm':
                        Merge();
                        return true;
                }
            }

            _Scope.HandleKey(key);
            return true;
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            _ScreenWidth = surface.Size.Width;
            _ScreenHeight = surface.Size.Height;
            MergeButton.Label = Busy == "merge" ? T("Merging...") : T("Merge");
            base.Render(surface);
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override int MeasureContentWidth(int availableWidth)
        {
            return Math.Min(availableWidth, Math.Max(60, (int)(_ScreenWidth * 0.85)));
        }

        /// <inheritdoc />
        protected override int MeasureContentHeight(int contentWidth)
        {
            return Math.Max(10, (int)(_ScreenHeight * 0.8) - 4);
        }

        /// <inheritdoc />
        protected override void RenderContent(ISurface content)
        {
            int width = content.Size.Width;
            int height = content.Size.Height;
            int y = 0;
            if (Error != null) SurfaceText.Draw(content, 0, y++, "! " + Error, On(Theme.Error), width);
            if (Busy.StartsWith("push:", StringComparison.Ordinal)) SurfaceText.Draw(content, 0, y++, T("Pushing...") + " " + Busy.Substring(5), Dim(), width);
            int mergeRows = 4;
            int gridHeight = Math.Max(1, height - y - mergeRows);
            if (Loading && Branches.Count == 0) SurfaceText.Draw(content, 0, y, T("Loading branches..."), Dim(), width);
            else _Scope.RenderChild(content, Grid, new Rect(0, y, width, gridHeight));
            y += gridHeight + 1;
            if (y >= height) return;
            SurfaceText.Draw(content, 0, y++, T("Merge a branch"), On(Theme.Accent), width);
            if (y >= height) return;
            int x = 0;
            int selectWidth = Math.Max(14, (width - 40) / 2);
            x += SurfaceText.Draw(content, x, y, T("Source") + " ", Dim(), width);
            _Scope.RenderChild(content, Source, new Rect(x, y, Math.Min(selectWidth, Math.Max(1, width - x)), 1));
            x += selectWidth + 1;
            if (x < width) x += SurfaceText.Draw(content, x, y, " " + T("into") + " ", Dim(), width - x);
            if (x < width)
            {
                _Scope.RenderChild(content, Target, new Rect(x, y, Math.Min(selectWidth, Math.Max(1, width - x)), 1));
                x += selectWidth + 1;
            }

            y++;
            if (y >= height) return;
            _Scope.RenderChild(content, PushAfterMerge, new Rect(0, y, Math.Max(1, Math.Min(30, width)), 1));
            if (width > 34) _Scope.RenderChild(content, MergeButton, new Rect(32, y, Math.Max(1, Math.Min(16, width - 32)), 1));
        }

        #endregion

        #region Private-Methods

        private static string TitleOf(OpsScreen? screen, string vesselName)
        {
            string label = screen != null ? screen.Tr("Manage Branches") : "Manage Branches";
            return label + " -- " + (vesselName ?? "");
        }

        #endregion
    }
}

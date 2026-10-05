namespace Armada.Tui.Screens.Operations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Models;
    using Armada.Tui.Text;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;

    /// <summary>
    /// The Start Session panel of Planning (the dashboard's PlanningStartCard plus the compact Vessel Readiness
    /// panel): title, captain (only idle captains on a supported runtime can start), fleet filter, vessel, pipeline
    /// or inherit, playbooks, and the initial message carried by a pre-fill; the reservation notice, the
    /// supported-runtimes note or the unsupported reason, and the long-start progress text. <c>Ctrl+S</c> starts.
    /// Not thread-safe.
    /// </summary>
    public class PlanningStartPanel : ContainerWidget
    {
        #region Public-Members

        /// <summary>
        /// Form.
        /// </summary>
        public FormView Form { get; } = new FormView();

        #endregion

        #region Private-Members

        private readonly PlanningScreen _Owner;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="owner">Planning screen.</param>
        public PlanningStartPanel(PlanningScreen owner)
        {
            _Owner = owner ?? throw new ArgumentNullException(nameof(owner));
            Form.SaveButton.Label = "Start Planning Session";
            Form.SaveButton.Hint = "Ctrl+S";
            Form.DiscardButton.Visible = false;
            Form.SaveRequested += (s, e) => _Owner.StartSession();
            Form.AddField("Title", owner.StartTitle);
            Form.AddField("Captain", owner.StartCaptain);
            Form.AddField("Fleet", owner.StartFleet);
            Form.AddField("Vessel", owner.StartVessel);
            Form.AddField("Pipeline", owner.StartPipeline, "Manage pipelines: Configuration, Pipelines");
            Form.AddField("Playbooks", owner.StartPlaybooks, "Enter to add, remove, or change delivery modes");
            Form.AddField("Initial message", owner.StartPrompt, "Sent as the first message once the session starts (Ctrl+E opens $EDITOR).", 3);
            AddChild(Form);
            Scope.Focus(Form);
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            return Scope.HandleKey(key);
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            SurfaceText.FillRect(surface, new Rect(0, 0, width, height), Theme.Text);
            if (width < 10 || height < 3) return;
            OpsDocument doc = new OpsDocument(Theme, Localizer);
            doc.Add(StyledText.From(T("Start Session"), Theme.Accent.WithAttribute(CellAttributes.Bold, true)));
            doc.Note("Reserve a captain on a vessel, then use the transcript as the source of truth for a later dispatch.");
            doc.Note("Planning sessions reserve the selected captain and dock for the duration of the session. The captain can inspect and modify the repository while you plan.", Theme.Info);
            string? banner = _Owner.PrefillBanner();
            if (banner != null) doc.Text(banner + (_Owner.ObjectiveId != null ? "  [Alt+B " + T("Open Backlog Item") + "]" : ""), Theme.Info);
            if (_Owner.LoadingCatalog) doc.Note("Loading planning catalog...");
            Captain? captain = _Owner.SelectedStartCaptain();
            if (captain != null && !captain.SupportsPlanningSessions)
                doc.Text(String.IsNullOrEmpty(captain.PlanningSessionSupportReason) ? T("This captain runtime is not currently supported for planning sessions.") : captain.PlanningSessionSupportReason, Theme.Warning);
            if (captain != null && captain.SupportsPlanningSessions)
                doc.Note("Planning runs this captain's CLI through transcript-backed turn relaunches.");
            if (banner != null && _Owner.StartPrompt.Text.Trim().Length > 0)
                doc.Note("Workspace prefilled an initial planning brief. Start the session and review the draft message in the transcript composer before sending it to the captain.", Theme.Info);
            if (_Owner.Creating)
            {
                doc.Note("Starting planning session...", Theme.Warning);
                doc.Note("Armada is reserving the captain, provisioning the dock, and preparing the worktree. First-time repository setup can take a few minutes.");
                doc.Note("You can stay on this page while Armada finishes setup. The session will appear below as soon as provisioning completes.");
            }

            if (_Owner.Reference.Loaded.Contains("vessels") && _Owner.Reference.Vessels.Count == 0)
                doc.Note("Create a vessel first so Armada has a repository context for planning.", Theme.Warning);
            if (!String.IsNullOrEmpty(_Owner.StartVessel.Value))
            {
                OpsDocument r = new OpsDocument(Theme, Localizer);
                OpsReadiness.Build(r, "Vessel Readiness", _Owner.StartReadiness, _Owner.LoadingReadiness, "Select a vessel to inspect readiness.", true);
                doc.Lines.AddRange(r.Lines.Take(3));
                if (r.Lines.Count > 3) doc.Text("... Alt+R " + T("Vessel Readiness"), Theme.Muted);
            }

            int maxInfo = Math.Max(1, height / 2);
            int y = OpsDraw.Lines(surface, 0, 0, doc.Lines, width, maxInfo, Theme.Text) + 1;
            Form.SaveButton.Label = _Owner.Creating ? "Starting Planning Session..." : "Start Planning Session";
            Form.SaveButton.Enabled = _Owner.CanStartSession();
            Form.MarkClean();
            if (y < height) Scope.RenderChild(surface, Form, new Rect(0, y, width, height - y));
        }

        #endregion
    }
}

namespace Armada.Tui.Screens.Operations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Client.Models;
    using Armada.Client.Socket;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Ask;
    using Armada.Tui.Input;
    using Armada.Tui.Routing;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;
    using TUIKit.Input;
    using TUIKit.Widgets;

    /// <summary>
    /// Planning (W3.4, <c>/planning</c> and <c>/planning/:id</c>), the dashboard's Planning page as four panels: Start
    /// Session (form, pre-fill from a backlog item, incident, Setup Wizard, Workspace, or a captain; supported
    /// runtimes; long-start notice and timeout message; Vessel Readiness), Recent Sessions (title, captain, vessel,
    /// pipeline, status, updated; End Session, Delete, Delete All with a typed confirmation), Current Session (header,
    /// streaming transcript with tool calls, thinking, and metrics; composer; Stop; End Session; Clear conversation;
    /// use a reply for dispatch or open it in Dispatch), and Dispatch From Session (voyage title, mission description;
    /// Summarize Draft, Open In Dispatch, Dispatch). Live through <c>planning-session.*</c> and <c>captain.changed</c>;
    /// the waiting text rotates every 4 s while the captain responds. Not thread-safe.
    /// </summary>
    public class PlanningScreen : OpsDetailScreen
    {
        #region Public-Members

        /// <summary>
        /// Session id from the route, or null on <c>/planning</c>.
        /// </summary>
        public string? SessionId { get; }

        /// <summary>
        /// Recent sessions, newest update first.
        /// </summary>
        public List<PlanningSession> Sessions { get; private set; } = new List<PlanningSession>();

        /// <summary>
        /// The open session, or null.
        /// </summary>
        public PlanningSessionDetail? Detail { get; private set; } = null;

        /// <summary>
        /// Messages of the open session.
        /// </summary>
        public List<PlanningSessionMessage> Messages
        {
            get { return Detail?.Messages ?? new List<PlanningSessionMessage>(); }
        }

        /// <summary>
        /// Tool chips by assistant message id (from <c>planning-session.tool</c>).
        /// </summary>
        public Dictionary<string, List<AskToolChip>> Tools { get; } = new Dictionary<string, List<AskToolChip>>(StringComparer.Ordinal);

        /// <summary>
        /// Thinking text by assistant message id (from <c>planning-session.thinking</c>).
        /// </summary>
        public Dictionary<string, string> Thinking { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// Reply selected for dispatch.
        /// </summary>
        public string SelectedMessageId { get; private set; } = "";

        /// <summary>
        /// Stream responses (default on).
        /// </summary>
        public bool Streaming { get; set; } = true;

        /// <summary>
        /// Ask the captain to show thinking (default off).
        /// </summary>
        public bool ShowThinking { get; set; } = false;

        /// <summary>
        /// The rotating waiting phrase.
        /// </summary>
        public string ThinkingPhrase { get; private set; } = "Thinking...";

        /// <summary>
        /// Composer.
        /// </summary>
        public PlanningComposer Composer { get; } = new PlanningComposer();

        /// <summary>
        /// Start: title.
        /// </summary>
        public InputField StartTitle { get; } = new InputField();

        /// <summary>
        /// Start: captain.
        /// </summary>
        public SelectField<string> StartCaptain { get; }

        /// <summary>
        /// Start: fleet filter.
        /// </summary>
        public SelectField<string> StartFleet { get; }

        /// <summary>
        /// Start: vessel.
        /// </summary>
        public SelectField<string> StartVessel { get; }

        /// <summary>
        /// Start: pipeline.
        /// </summary>
        public SelectField<string> StartPipeline { get; }

        /// <summary>
        /// Start: playbooks.
        /// </summary>
        public PlaybookSelectionField StartPlaybooks { get; } = new PlaybookSelectionField();

        /// <summary>
        /// Start: initial message (pre-fill).
        /// </summary>
        public OpsTextArea StartPrompt { get; } = new OpsTextArea();

        /// <summary>
        /// Dispatch draft title.
        /// </summary>
        public InputField DispatchTitle { get; } = new InputField();

        /// <summary>
        /// Dispatch draft description.
        /// </summary>
        public OpsTextArea DispatchDescription { get; } = new OpsTextArea();

        /// <summary>
        /// Sessions table.
        /// </summary>
        public ArmadaGrid<PlanningSession> SessionsGrid { get; }

        /// <summary>
        /// Start panel.
        /// </summary>
        public PlanningStartPanel StartPanel { get; }

        /// <summary>
        /// Current Session panel.
        /// </summary>
        public PlanningTranscriptPanel TranscriptPanel { get; }

        /// <summary>
        /// Dispatch From Session panel.
        /// </summary>
        public PlanningDispatchPanel DispatchPanel { get; }

        /// <summary>
        /// Handoff source, or null.
        /// </summary>
        public string? From { get; }

        /// <summary>
        /// Backlog item carried by a handoff, or null.
        /// </summary>
        public string? ObjectiveId { get; private set; } = null;

        /// <summary>
        /// True until sessions, captains, and vessels have loaded.
        /// </summary>
        public bool LoadingCatalog
        {
            get { return !_SessionsLoaded || !Reference.Loaded.Contains("captains") || !Reference.Loaded.Contains("vessels"); }
        }

        /// <summary>
        /// True while the open session loads.
        /// </summary>
        public bool LoadingDetail { get; private set; } = false;

        /// <summary>
        /// True while a session starts.
        /// </summary>
        public bool Creating { get; private set; } = false;

        /// <summary>
        /// True while a message is sending.
        /// </summary>
        public bool Sending { get; private set; } = false;

        /// <summary>
        /// True while summarizing.
        /// </summary>
        public bool Summarizing { get; private set; } = false;

        /// <summary>
        /// True while dispatching from the session.
        /// </summary>
        public bool DispatchingDraft { get; private set; } = false;

        /// <summary>
        /// Readiness of the start vessel, or null.
        /// </summary>
        public VesselReadinessResult? StartReadiness { get; private set; } = null;

        /// <summary>
        /// True while readiness loads.
        /// </summary>
        public bool LoadingReadiness { get; private set; } = false;

        /// <summary>
        /// True while the captain responds or a message is sending.
        /// </summary>
        public bool Busy
        {
            get { return Sending || Detail?.Session?.Status == PlanningSessionStatusEnum.Responding; }
        }

        /// <summary>
        /// True when the composer accepts input (session Active).
        /// </summary>
        public bool ComposerEnabled
        {
            get { return Detail?.Session?.Status == PlanningSessionStatusEnum.Active; }
        }

        /// <summary>
        /// Captain name of the open session.
        /// </summary>
        public string CaptainName
        {
            get { return Detail?.Captain?.Name ?? Detail?.Session?.CaptainId ?? T("Captain"); }
        }

        /// <summary>
        /// Captain runtime of the open session.
        /// </summary>
        public AgentRuntimeEnum? CaptainRuntime
        {
            get { return Detail?.Captain?.Runtime; }
        }

        #endregion

        #region Private-Members

        private bool _SessionsLoaded = false;
        private bool _Deleting = false;
        private string? _EndingSessionId = null;
        private PlanningDispatchSeed? _Seed = null;
        private string? _PendingCaptain = null;
        private string? _PendingFleet = null;
        private string? _PendingVessel = null;
        private string? _PendingPipeline = null;
        private int _ReadinessGeneration = 0;
        private PlanningSessionStatusEnum? _LastStatus = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Services.</param>
        public PlanningScreen(RouteMatch route, TuiContext context)
            : base(route, context, "PlanningScreen", "Planning")
        {
            SessionId = route.Param("id");
            Heading = Tr("Planning");
            SubtitleText = Tr("Chat with a captain against a specific vessel, preserve the transcript, and dispatch directly from the planning output.");

            StartTitle.Placeholder = "Optional planning session title";
            StartCaptain = NewSelect("Captain", new List<SelectOption<string>>(), "Select a captain...");
            StartCaptain.Required = true;
            StartFleet = NewSelect("Fleet", new List<SelectOption<string>> { new SelectOption<string>("", Tr("Any fleet")) }, null);
            StartFleet.SetValue("");
            StartVessel = NewSelect("Vessel", new List<SelectOption<string>>(), "Select a vessel...");
            StartVessel.Required = true;
            StartPipeline = NewSelect("Pipeline", new List<SelectOption<string>> { new SelectOption<string>("", Tr("Inherit later during dispatch")) }, null);
            StartPipeline.SetValue("");
            StartPlaybooks.ModalHost = context.Modals;
            StartPrompt.ExternalEditor = (text, done) => EditExternally(text, done);
            Composer.ExternalEditor = (text, done) => EditExternally(text, done);
            Composer.Submitted += (s, e) => Send();
            DispatchTitle.Placeholder = "Optional override for the resulting voyage title";
            DispatchDescription.Placeholder = "Select a planning response to seed the dispatch description.";
            DispatchDescription.ExternalEditor = (text, done) => EditExternally(text, done);

            From = OpsHandoff.Get(route, "from");
            if (SessionId == null && (From != null || OpsHandoff.Get(route, "captainId") != null))
            {
                string? title = OpsHandoff.Get(route, "title");
                if (title != null) StartTitle.Value = title;
                _PendingCaptain = OpsHandoff.Get(route, "captainId");
                _PendingFleet = OpsHandoff.Get(route, "fleetId");
                _PendingVessel = OpsHandoff.Get(route, "vesselId");
                _PendingPipeline = OpsHandoff.Get(route, "pipelineId");
                string? prompt = OpsHandoff.Get(route, "initialPrompt");
                if (prompt != null) StartPrompt.Text = prompt;
                ObjectiveId = OpsHandoff.Get(route, "objectiveId");
            }
            else if (SessionId != null)
            {
                string? prompt = OpsHandoff.Get(route, "initialPrompt");
                if (prompt != null) Composer.Text = prompt;
            }

            StartCaptain.ValueChanged += (s, e) => _PendingCaptain = null;
            StartFleet.ValueChanged += (s, e) =>
            {
                _PendingFleet = null;
                FleetChanged();
            };
            StartVessel.ValueChanged += (s, e) =>
            {
                _PendingVessel = null;
                LoadReadiness();
            };
            StartPipeline.ValueChanged += (s, e) => _PendingPipeline = null;

            SessionsGrid = new ArmadaGrid<PlanningSession>(p => p.Id);
            SessionsGrid.MultiSelect = false;
            SessionsGrid.Dispatcher = context.Dispatcher;
            SessionsGrid.ModalHost = context.Modals;
            SessionsGrid.EmptyText = "No planning sessions yet.";
            SessionsGrid.AddColumn(Col("title", "Title", p => (p.Id == SessionId ? "* " : "") + p.Title + "  " + p.Id, 4));
            SessionsGrid.AddColumn(Col("captain", "Captain", p => Reference.CaptainName(p.CaptainId), 2));
            SessionsGrid.AddColumn(Col("vessel", "Vessel", p => Reference.VesselName(p.VesselId), 2));
            SessionsGrid.AddColumn(Col("pipeline", "Pipeline", p => PipelineName(p.PipelineId), 2));
            GridColumn<PlanningSession> status = Col("status", "Status", p => StatusBadge.Label(p.Status), 1);
            status.Width = 14;
            status.Style = (p, t) => StatusBadge.Style(p.Status, t);
            SessionsGrid.AddColumn(status);
            GridColumn<PlanningSession> updated = Col("updated", "Updated", p => Context.Loc.FormatRelative(p.LastUpdateUtc, Context.Clock.UtcNow), 1);
            updated.Width = 16;
            SessionsGrid.AddColumn(updated);
            SessionsGrid.Activated += (s, p) => Context.Navigate("/planning/" + Uri.EscapeDataString(p.Id));
            SessionsGrid.MenuRequested += (s, p) => ShowSessionMenu(p);

            StartPanel = new PlanningStartPanel(this);
            TranscriptPanel = new PlanningTranscriptPanel(this);
            DispatchPanel = new PlanningDispatchPanel(this);
            AddPanel("start", "Start Session", StartPanel);
            AddPanel("sessions", "Recent Sessions", SessionsGrid);
            AddPanel("session", "Current Session", TranscriptPanel);
            AddPanel("dispatch", "Dispatch From Session", DispatchPanel);
            if (SessionId != null) SelectPanel("session");

            Action("new", "New Session", () => Context.Navigate("/planning"), "n", () => SessionId != null, true);
            Action("stop", "Stop", StopTurn, "ctrl+c", () => Detail?.Session?.Status == PlanningSessionStatusEnum.Responding, true);
            OpsScreenAction end = Action("end", "End Session", () => { if (Detail?.Session != null) RequestEndSession(Detail.Session); }, "E", CanEndSession, true);
            end.DynamicLabel = () => _EndingSessionId == SessionId || Detail?.Session?.Status == PlanningSessionStatusEnum.Stopping ? Tr("Ending...") : Tr("End Session");
            Action("dispatch", "Dispatch", DispatchFromSession, "D", CanDispatch, true);
            Action("summarize", "Summarize Draft", Summarize, "u", CanSummarize);
            Action("open-dispatch", "Open In Dispatch", OpenDraftInDispatch, "O", CanOpenInDispatch);
            Action("send", "Send", Send, null, CanSend);
            OpsScreenAction stream = Action("stream", "Stream responses", () => { if (!Busy) Streaming = !Streaming; }, "alt+s");
            stream.DynamicLabel = () => (Streaming ? "[x] " : "[ ] ") + Tr("Stream responses");
            OpsScreenAction thinking = Action("thinking", "Show thinking", () => { if (!Busy) ShowThinking = !ShowThinking; }, "alt+t");
            thinking.DynamicLabel = () => (ShowThinking ? "[x] " : "[ ] ") + Tr("Show thinking");
            Action("readiness", "Vessel Readiness", () => OpsReadiness.ShowDetails(this, StartReadiness, LoadingReadiness), "alt+r", () => !String.IsNullOrEmpty(StartVessel.Value));
            Action("open-backlog", "Open Backlog Item", () => Context.Navigate("/backlog/" + Uri.EscapeDataString(ObjectiveId!)), "alt+b", () => ObjectiveId != null);
            Action("json", "View JSON", () => ShowJson(Tr("Planning Session") + ": " + (Detail?.Session?.Title ?? ""), Detail), "j", () => Detail != null);
            Action("clear", "Clear conversation", ConfirmDeleteCurrent, null, () => Detail?.Session != null && !Busy, false, true);
            Action("delete", "Delete Session", ConfirmDeleteCurrent, "del", () => Detail?.Session != null, false, true);
            Action("delete-all", "Delete All", ConfirmDeleteAll, "X", () => Sessions.Count > 0, false, true);
            Action("manage-pipelines", "Manage pipelines", () => Context.Navigate("/configuration?tab=pipelines"));

            Reference.Changed += (s, name) => ReferenceArrived(name);
            Subscribe("planning-session.", OnPlanningEvent);
            Subscribe("captain.changed", OnCaptainChanged);
            Timer rotate = new Timer(_ => Post(RotateThinking), null, 4000, 4000);
            Track(rotate);
            Load();
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override void Load()
        {
            Reference.Ensure(true, "captains", "fleets", "vessels", "pipelines", "playbooks");
            Call((c, t) => c.ListPlanningSessionsAsync(t), list =>
            {
                Sessions = (list ?? new List<PlanningSession>()).OrderByDescending(s => s.LastUpdateUtc).ToList();
                _SessionsLoaded = true;
                LoadError = null;
                SyncGrid();
            }, null, ex =>
            {
                _SessionsLoaded = true;
                LoadError = String.IsNullOrEmpty(ex.Message) ? Tr("Failed to load planning data.") : ex.Message;
            });
            if (SessionId != null) LoadDetail();
        }

        /// <inheritdoc />
        public override int DefaultRefreshSeconds()
        {
            return 0;
        }

        /// <summary>
        /// The prefill banner for <c>/planning</c>, or null.
        /// </summary>
        /// <returns>Translated text or null.</returns>
        public string? PrefillBanner()
        {
            if (SessionId != null) return null;
            switch (From)
            {
                case OpsHandoff.FromObjective: return Tr("Prefilled from a backlog item. Review the vessel and prompt, then start a planning session to turn that scoped work into an execution plan.");
                case OpsHandoff.FromIncident: return Tr("Prefilled from an incident. Review the vessel and prompt, then start a planning session for the hotfix or recovery path.");
                case OpsHandoff.FromSetupWizard: return Tr("Prefilled from the setup wizard. Review the vessel and prompt, then start a planning session to turn onboarding follow-up into an execution plan.");
                case OpsHandoff.FromWorkspace: return Tr("Prefilled from Workspace. Review the vessel and prompt, then start a planning session.");
                default: return null;
            }
        }

        /// <summary>
        /// The captain chosen in the start form, or null.
        /// </summary>
        /// <returns>Captain or null.</returns>
        public Captain? SelectedStartCaptain()
        {
            string? id = StartCaptain.Value;
            return String.IsNullOrEmpty(id) ? null : Reference.Captains.FirstOrDefault(c => c.Id == id);
        }

        /// <summary>
        /// True when the start form can start (idle supported captain, a vessel, not already starting).
        /// </summary>
        /// <returns>True when enabled.</returns>
        public bool CanStartSession()
        {
            return PlanningLogic.CanStartPlanning(SelectedStartCaptain()) && !String.IsNullOrEmpty(StartVessel.Value) && !Creating;
        }

        /// <summary>
        /// Header display values of the open session.
        /// </summary>
        /// <returns>View.</returns>
        public PlanningSessionDetailView CurrentDetail()
        {
            PlanningSessionDetailView view = new PlanningSessionDetailView();
            PlanningSession? s = Detail?.Session;
            if (s == null) return view;
            view.HasSession = true;
            view.Title = s.Title;
            view.CaptainName = Detail!.Captain?.Name ?? s.CaptainId;
            view.Runtime = Detail.Captain != null ? Detail.Captain.Runtime.ToString() : "-";
            view.VesselName = Detail.Vessel?.Name ?? s.VesselId;
            view.Branch = String.IsNullOrEmpty(s.BranchName) ? "-" : s.BranchName!;
            view.Pipeline = PipelineName(s.PipelineId);
            view.PlaybookCount = s.SelectedPlaybooks?.Count ?? 0;
            view.Updated = Context.Loc.FormatRelative(s.LastUpdateUtc, Context.Clock.UtcNow);
            view.MessageCount = Messages.Count;
            view.FailureReason = s.FailureReason;
            return view;
        }

        /// <summary>
        /// The reply selected for dispatch, or null.
        /// </summary>
        /// <returns>Message or null.</returns>
        public PlanningSessionMessage? SelectedMessage()
        {
            return Messages.FirstOrDefault(m => m.Id == SelectedMessageId);
        }

        /// <summary>
        /// True when a message can be sent.
        /// </summary>
        /// <returns>True when enabled.</returns>
        public bool CanSend()
        {
            return ComposerEnabled && Composer.Text.Trim().Length > 0 && !Sending;
        }

        /// <summary>
        /// True when the open session can be ended.
        /// </summary>
        /// <returns>True when enabled.</returns>
        public bool CanEndSession()
        {
            PlanningSession? s = Detail?.Session;
            return s != null && (s.Status == PlanningSessionStatusEnum.Active || s.Status == PlanningSessionStatusEnum.Responding) && _EndingSessionId == null;
        }

        /// <summary>
        /// True when Summarize Draft is available.
        /// </summary>
        /// <returns>True when enabled.</returns>
        public bool CanSummarize()
        {
            PlanningSessionMessage? m = SelectedMessage();
            return Detail?.Session != null && m != null && !String.IsNullOrWhiteSpace(m.Content) && !Summarizing;
        }

        /// <summary>
        /// True when Open In Dispatch is available.
        /// </summary>
        /// <returns>True when enabled.</returns>
        public bool CanOpenInDispatch()
        {
            return Detail?.Session != null && DispatchDescription.Text.Trim().Length > 0;
        }

        /// <summary>
        /// True when Dispatch is available.
        /// </summary>
        /// <returns>True when enabled.</returns>
        public bool CanDispatch()
        {
            PlanningSessionMessage? m = SelectedMessage();
            return Detail?.Session != null && m != null && !String.IsNullOrWhiteSpace(m.Content) && DispatchDescription.Text.Trim().Length > 0 && !DispatchingDraft;
        }

        /// <summary>
        /// Start a planning session (the dashboard's handleCreateSession).
        /// </summary>
        public void StartSession()
        {
            if (!StartPanel.Form.ValidateAll()) return;
            if (!CanStartSession()) return;
            PlanningSessionCreateRequest req = new PlanningSessionCreateRequest();
            req.Title = StartTitle.Value.Trim().Length > 0 ? StartTitle.Value.Trim() : null;
            req.CaptainId = StartCaptain.Value!;
            req.VesselId = StartVessel.Value!;
            req.FleetId = String.IsNullOrEmpty(StartFleet.Value) ? null : StartFleet.Value;
            req.PipelineId = String.IsNullOrEmpty(StartPipeline.Value) ? null : StartPipeline.Value;
            req.SelectedPlaybooks = StartPlaybooks.Value.ToList();
            req.ObjectiveId = ObjectiveId;
            string prompt = StartPrompt.Text.Trim();
            Creating = true;
            Call((c, t) => c.CreatePlanningSessionAsync(req, t), result =>
            {
                Creating = false;
                PlanningSession? session = result?.Session;
                if (session == null) return;
                Sessions = PlanningLogic.UpsertSession(Sessions, session);
                Toast(NotificationSeverityEnum.Success, Tr("Planning session started."));
                Dictionary<string, string> q = new Dictionary<string, string>(StringComparer.Ordinal);
                if (prompt.Length > 0)
                {
                    q["from"] = OpsHandoff.FromWorkspace;
                    q["initialPrompt"] = prompt;
                }

                Context.Navigate("/planning/" + Uri.EscapeDataString(session.Id) + RouteMatch.BuildQuery(q));
            }, null, ex =>
            {
                Creating = false;
                string message = String.IsNullOrEmpty(ex.Message) ? Tr("Failed to start planning session.") : ex.Message;
                if (ex is ArmadaApiException api && api.IsTimeout)
                {
                    Load();
                    ShowMessage(Tr("Starting the planning session is taking longer than expected. Armada is still provisioning the dock and worktree. If setup completes, the session will appear in the list on the left."));
                }
                else
                {
                    ShowMessage(message);
                }
            });
        }

        /// <summary>
        /// Send the composer text (the dashboard's handleSendMessage).
        /// </summary>
        public void Send()
        {
            PlanningSession? session = Detail?.Session;
            if (session == null || !CanSend()) return;
            PlanningSessionMessageRequest req = new PlanningSessionMessageRequest();
            req.Content = Composer.Text.Trim();
            req.ShowThinking = ShowThinking;
            req.Stream = Streaming;
            Sending = true;
            Call((c, t) => c.SendPlanningSessionMessageAsync(session.Id, req, t), result =>
            {
                Sending = false;
                Composer.Text = "";
                if (result?.Session != null)
                {
                    ApplyDetail(result);
                    TranscriptPanel.Transcript.ToEnd();
                }
            }, null, ex =>
            {
                Sending = false;
                ShowMessage(String.IsNullOrEmpty(ex.Message) ? Tr("Failed to send message.") : ex.Message);
            });
        }

        /// <summary>
        /// Stop the current turn.
        /// </summary>
        public void StopTurn()
        {
            PlanningSession? session = Detail?.Session;
            if (session == null) return;
            Call((c, t) => c.StopPlanningTurnAsync(session.Id, t), result =>
            {
                if (result?.Session != null) ApplyDetail(result);
            }, null, ex => ShowMessage(String.IsNullOrEmpty(ex.Message) ? Tr("Failed to stop the current turn.") : ex.Message));
        }

        /// <summary>
        /// Use a reply for the dispatch draft.
        /// </summary>
        /// <param name="messageId">Message id.</param>
        public void SelectForDispatch(string messageId)
        {
            if (String.IsNullOrEmpty(messageId)) return;
            SelectedMessageId = messageId;
            SyncSeed();
        }

        /// <summary>
        /// Summarize the selected reply into a draft.
        /// </summary>
        public void Summarize()
        {
            PlanningSession? session = Detail?.Session;
            if (session == null || !CanSummarize()) return;
            PlanningSessionSummaryRequest req = new PlanningSessionSummaryRequest();
            req.MessageId = SelectedMessageId;
            req.Title = DispatchTitle.Value.Trim().Length > 0 ? DispatchTitle.Value.Trim() : null;
            Summarizing = true;
            Call((c, t) => c.SummarizePlanningSessionAsync(session.Id, req, t), result =>
            {
                Summarizing = false;
                if (result == null) return;
                DispatchTitle.Value = result.Title;
                DispatchDescription.Text = result.Description;
                _Seed = new PlanningDispatchSeed(result.SessionId + ":" + result.MessageId, result.Title, result.Description, "summary");
                Toast(NotificationSeverityEnum.Success, Tr("Dispatch draft summarized from planning output."));
            }, null, ex =>
            {
                Summarizing = false;
                ShowMessage(String.IsNullOrEmpty(ex.Message) ? Tr("Failed to summarize planning output.") : ex.Message);
            });
        }

        /// <summary>
        /// Open the draft in Dispatch (releases the session first, best effort).
        /// </summary>
        public void OpenDraftInDispatch()
        {
            PlanningSession? session = Detail?.Session;
            if (session == null || DispatchDescription.Text.Trim().Length == 0) return;
            string title = DispatchTitle.Value.Trim();
            string target = OpsHandoff.Dispatch(OpsHandoff.FromPlanning, session.VesselId, PipelineNameOrNull(session.PipelineId), DispatchDescription.Text.Trim(), title.Length > 0 ? title : null, null, session.SelectedPlaybooks);
            ReleaseThen(session, target);
        }

        /// <summary>
        /// Open one reply in Dispatch with its full text as the prompt.
        /// </summary>
        /// <param name="messageId">Message id.</param>
        public void OpenMessageInDispatch(string messageId)
        {
            PlanningSession? session = Detail?.Session;
            PlanningSessionMessage? message = Messages.FirstOrDefault(m => m.Id == messageId);
            string prompt = message?.Content?.Trim() ?? "";
            if (session == null || prompt.Length == 0) return;
            string title = DispatchTitle.Value.Trim().Length > 0 ? DispatchTitle.Value.Trim() : session.Title;
            string target = OpsHandoff.Dispatch(OpsHandoff.FromPlanning, session.VesselId, PipelineNameOrNull(session.PipelineId), prompt, String.IsNullOrEmpty(title) ? null : title, null, session.SelectedPlaybooks);
            ReleaseThen(session, target);
        }

        /// <summary>
        /// Dispatch from the session (the dashboard's handleDispatch).
        /// </summary>
        public void DispatchFromSession()
        {
            PlanningSession? session = Detail?.Session;
            if (session == null || !CanDispatch()) return;
            PlanningSessionDispatchRequest req = new PlanningSessionDispatchRequest();
            req.MessageId = String.IsNullOrEmpty(SelectedMessageId) ? null : SelectedMessageId;
            req.Title = DispatchTitle.Value.Trim().Length > 0 ? DispatchTitle.Value.Trim() : null;
            req.Description = DispatchDescription.Text.Trim();
            DispatchingDraft = true;
            Call((c, t) => c.DispatchPlanningSessionAsync(session.Id, req, t), voyage =>
            {
                DispatchingDraft = false;
                Toast(NotificationSeverityEnum.Success, Tr("Dispatch created from planning session."));
                if (voyage != null) Context.Navigate("/voyages/" + Uri.EscapeDataString(voyage.Id));
            }, null, ex =>
            {
                DispatchingDraft = false;
                ShowMessage(String.IsNullOrEmpty(ex.Message) ? Tr("Failed to dispatch from planning session.") : ex.Message);
            });
        }

        /// <summary>
        /// Ask to end a session.
        /// </summary>
        /// <param name="session">Session.</param>
        public void RequestEndSession(PlanningSession session)
        {
            if (session == null) return;
            Confirm("End Planning Session", Tr("End this planning session and release the reserved captain and dock? The transcript will be kept until you delete it or it expires under server retention settings."), () =>
            {
                string target = session.Id;
                _EndingSessionId = target;
                Call((c, t) => c.StopPlanningSessionAsync(target, t), result =>
                {
                    _EndingSessionId = null;
                    if (result?.Session != null)
                    {
                        if (Detail?.Session?.Id == target) ApplyDetail(result);
                        else UpsertSession(result.Session);
                    }

                    Toast(NotificationSeverityEnum.Warning, Tr("Planning session is ending."));
                }, null, ex =>
                {
                    _EndingSessionId = null;
                    ShowMessage(String.IsNullOrEmpty(ex.Message) ? Tr("Failed to end planning session.") : ex.Message);
                });
            }, "End Session");
        }

        /// <summary>
        /// Delete a session from the Recent Sessions menu (no confirmation, like the dashboard's row menu).
        /// </summary>
        /// <param name="session">Session.</param>
        public void DeleteSession(PlanningSession session)
        {
            if (session == null) return;
            string id = session.Id;
            _Deleting = true;
            Run((c, t) => c.DeletePlanningSessionAsync(id, t), () =>
            {
                _Deleting = false;
                Sessions = Sessions.Where(s => s.Id != id).ToList();
                SyncGrid();
                if (SessionId == id)
                {
                    Detail = null;
                    Context.Navigate("/planning");
                }

                Toast(NotificationSeverityEnum.Warning, Tr("Planning session deleted."));
            }, null, ex =>
            {
                _Deleting = false;
                ShowMessage(String.IsNullOrEmpty(ex.Message) ? Tr("Failed to delete planning session.") : ex.Message);
            });
        }

        /// <summary>
        /// Show tool call details of a message.
        /// </summary>
        /// <param name="messageId">Message id.</param>
        public void ShowTools(string messageId)
        {
            if (!Tools.TryGetValue(messageId, out List<AskToolChip>? tools) || tools.Count == 0)
            {
                ShowText(Tr("Tools"), Tr("No details available."));
                return;
            }

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (AskToolChip chip in tools)
            {
                sb.Append(chip.Name).Append("  ").Append(chip.Status).Append(chip.ElapsedMs.HasValue ? "  " + PlanningLogic.Ms(chip.ElapsedMs) : "").Append('\n');
                sb.Append(Tr("Arguments")).Append(":\n").Append(Armada.Tui.Approvals.ApprovalActions.Pretty(chip.Arguments)).Append('\n');
                if (chip.Result != null) sb.Append(Tr("Result")).Append(":\n").Append(Armada.Tui.Approvals.ApprovalActions.Pretty(chip.Result)).Append('\n');
                sb.Append('\n');
            }

            ShowText(Tr("Tools"), sb.ToString());
        }

        /// <inheritdoc />
        public override IEnumerable<ArmadaCommand> Commands()
        {
            List<ArmadaCommand> list = base.Commands().ToList();
            ArmadaCommand rowMenu = new ArmadaCommand(ScreenKey + ".session-menu", "Selected session actions", CommandMenuEnum.Actions, () => { PlanningSession? p = SessionsGrid.Current; if (p != null) ShowSessionMenu(p); });
            rowMenu.Group = Title;
            rowMenu.Dispatch = false;
            list.Add(rowMenu);
            return list;
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            bool chord = (key.Modifiers & (KeyModifiers.Ctrl | KeyModifiers.Alt)) != 0;
            if ((key.Modifiers & KeyModifiers.Alt) != 0 && key.Code == KeyCode.Character && key.Rune >= '1' && key.Rune <= '9' && Tabs.HandleGlobalKey(key)) return true;
            if (chord)
            {
                foreach (OpsScreenAction a in Actions)
                {
                    if (a.Key != null && (a.Key.StartsWith("ctrl+", StringComparison.Ordinal) || a.Key.StartsWith("alt+", StringComparison.Ordinal)) && Matches(a.Key, key))
                    {
                        if (a.Available) a.Run();
                        return true;
                    }
                }
            }

            if (ReferenceEquals(CurrentPanel, SessionsGrid) && key.Modifiers == KeyModifiers.None)
            {
                PlanningSession? row = SessionsGrid.Current;
                if (row != null && key.Code == KeyCode.Character && key.Rune == '.')
                {
                    ShowSessionMenu(row);
                    return true;
                }

                if (row != null && key.Code == KeyCode.Character && key.Rune == 'E')
                {
                    if (row.Status == PlanningSessionStatusEnum.Active || row.Status == PlanningSessionStatusEnum.Responding) RequestEndSession(row);
                    return true;
                }

                if (row != null && key.Code == KeyCode.Delete)
                {
                    DeleteSession(row);
                    return true;
                }
            }

            return base.HandleKey(key);
        }

        #endregion

        #region Private-Methods

        private static bool Matches(string keyText, KeyEvent key)
        {
            try { return KeyStroke.Parse(keyText).Matches(key); }
            catch (FormatException) { return false; }
        }

        private static GridColumn<PlanningSession> Col(string key, string title, Func<PlanningSession, string> value, int weight)
        {
            GridColumn<PlanningSession> c = new GridColumn<PlanningSession>(key, title, value);
            c.Weight = weight;
            return c;
        }

        private string PipelineName(string? id)
        {
            if (String.IsNullOrEmpty(id)) return "-";
            return Reference.Pipelines.FirstOrDefault(p => p.Id == id)?.Name ?? id!;
        }

        private string? PipelineNameOrNull(string? id)
        {
            if (String.IsNullOrEmpty(id)) return null;
            return Reference.Pipelines.FirstOrDefault(p => p.Id == id)?.Name;
        }

        private void ShowSessionMenu(PlanningSession session)
        {
            List<ActionMenuItem> items = new List<ActionMenuItem>();
            items.Add(new ActionMenuItem(Tr("Open"), () => Context.Navigate("/planning/" + Uri.EscapeDataString(session.Id)), "Enter"));
            bool canEnd = session.Status == PlanningSessionStatusEnum.Active || session.Status == PlanningSessionStatusEnum.Responding;
            bool ending = _EndingSessionId == session.Id || session.Status == PlanningSessionStatusEnum.Stopping;
            if (canEnd || ending)
            {
                ActionMenuItem end = new ActionMenuItem(ending ? Tr("Ending...") : Tr("End Session"), () => { if (!ending) RequestEndSession(session); }, "E");
                end.Enabled = !ending;
                items.Add(end);
            }

            ActionMenuItem delete = new ActionMenuItem("! " + Tr("Delete"), () => DeleteSession(session), "Del");
            delete.Destructive = true;
            items.Add(delete);
            items.Add(new ActionMenuItem(Tr("View JSON"), () => ShowJson(Tr("Planning Session") + ": " + session.Title, session), ""));
            ShowMenu(session.Title, items);
        }

        private void ConfirmDeleteCurrent()
        {
            PlanningSession? session = Detail?.Session;
            if (session == null) return;
            Confirm("Delete Planning Session", Tr("Delete this planning session and its transcript?"), () =>
            {
                string id = session.Id;
                _Deleting = true;
                Run((c, t) => c.DeletePlanningSessionAsync(id, t), () =>
                {
                    _Deleting = false;
                    Sessions = Sessions.Where(s => s.Id != id).ToList();
                    Detail = null;
                    SelectedMessageId = "";
                    DispatchTitle.Value = "";
                    DispatchDescription.Text = "";
                    _Seed = null;
                    Toast(NotificationSeverityEnum.Warning, Tr("Planning session deleted."));
                    Context.Navigate("/planning");
                }, null, ex =>
                {
                    _Deleting = false;
                    ShowMessage(String.IsNullOrEmpty(ex.Message) ? Tr("Failed to delete planning session.") : ex.Message);
                });
            }, "Delete Session").Destructive = true;
        }

        private void ConfirmDeleteAll()
        {
            List<PlanningSession> targets = Sessions.ToList();
            if (targets.Count == 0) return;
            Confirm("Delete All Planning Sessions", Tr("Delete all {{count}} planning session(s) and their transcripts? This cannot be undone.", LocalizationArgs.Of("count", targets.Count)), () =>
            {
                _Deleting = true;
                Call(async (c, t) =>
                {
                    int failures = 0;
                    foreach (PlanningSession s in targets)
                    {
                        try { await c.DeletePlanningSessionAsync(s.Id, t).ConfigureAwait(false); }
                        catch (ArmadaApiException) { failures++; }
                    }

                    return failures;
                }, failures =>
                {
                    _Deleting = false;
                    Detail = null;
                    Toast(NotificationSeverityEnum.Warning, failures > 0
                        ? Tr("{{count}} planning session(s) could not be deleted (they may still be running).", LocalizationArgs.Of("count", failures))
                        : Tr("All planning sessions deleted."));
                    if (SessionId != null) Context.Navigate("/planning");
                    else Load();
                }, "Failed to delete planning sessions.");
            }, "Delete All", "delete");
        }

        private void ReleaseThen(PlanningSession session, string target)
        {
            string id = session.Id;
            Call((c, t) => c.StopPlanningSessionAsync(id, t), result => Context.Navigate(target), null, ex => Context.Navigate(target));
        }

        private void LoadDetail()
        {
            if (SessionId == null) return;
            string id = SessionId;
            LoadingDetail = Detail == null;
            Call((c, t) => c.GetPlanningSessionAsync(id, t), result =>
            {
                LoadingDetail = false;
                if (result?.Session == null)
                {
                    Detail = null;
                    return;
                }

                ApplyDetail(result);
            }, null, ex =>
            {
                LoadingDetail = false;
                Detail = null;
                LoadError = String.IsNullOrEmpty(ex.Message) ? Tr("Failed to load planning session.") : ex.Message;
            });
        }

        private void ApplyDetail(PlanningSessionDetail detail)
        {
            if (detail.Session == null) return;
            if (Detail != null && Detail.Session != null && Detail.Session.Id == detail.Session.Id)
            {
                if (detail.Captain == null) detail.Captain = Detail.Captain;
                if (detail.Vessel == null) detail.Vessel = Detail.Vessel;
            }

            Detail = detail;
            Status = detail.Session.Status.ToString();
            UpsertSession(detail.Session);
            SyncSeed();
        }

        private void UpsertSession(PlanningSession session)
        {
            Sessions = PlanningLogic.UpsertSession(Sessions, session);
            SyncGrid();
        }

        private void SyncGrid()
        {
            SessionsGrid.SetLocalRows(Sessions);
        }

        private void SyncSeed()
        {
            if (Detail?.Session == null) return;
            PlanningSessionMessage? latest = PlanningLogic.LatestAssistant(Messages);
            if (latest != null && (String.IsNullOrEmpty(SelectedMessageId) || !Messages.Any(m => m.Id == SelectedMessageId))) SelectedMessageId = latest.Id;
            PlanningDispatchSeed? seed = PlanningLogic.ResolveSeed(Detail.Session.Id, Detail.Session.Title, SelectedMessage(), DispatchTitle.Value, DispatchDescription.Text, _Seed);
            if (seed == null) return;
            _Seed = seed;
            if (seed.Title != DispatchTitle.Value) DispatchTitle.Value = seed.Title;
            if (seed.Description != DispatchDescription.Text) DispatchDescription.Text = seed.Description;
        }

        private void RotateThinking()
        {
            PlanningSessionStatusEnum? status = Detail?.Session?.Status;
            if (status == PlanningSessionStatusEnum.Responding)
            {
                ThinkingPhrase = AskPhrases.RandomThinking(ThinkingPhrase);
            }

            _LastStatus = status;
        }

        private void OnPlanningEvent(ArmadaSocketMessage message)
        {
            PlanningSessionEvent? e = message.GetData<PlanningSessionEvent>();
            if (e == null) return;
            switch (message.Type)
            {
                case ArmadaEventTypes.PlanningSessionChanged:
                    if (e.Session == null) return;
                    UpsertSession(e.Session);
                    if (Detail?.Session != null && Detail.Session.Id == e.Session.Id)
                    {
                        bool wasResponding = Detail.Session.Status == PlanningSessionStatusEnum.Responding;
                        Detail.Session = e.Session;
                        Status = e.Session.Status.ToString();
                        if (!wasResponding && e.Session.Status == PlanningSessionStatusEnum.Responding) ThinkingPhrase = AskPhrases.RandomThinking(ThinkingPhrase);
                    }

                    return;
                case ArmadaEventTypes.PlanningSessionMessageCreated:
                case ArmadaEventTypes.PlanningSessionMessageUpdated:
                    if (e.Message == null || String.IsNullOrEmpty(e.SessionId) || Detail?.Session == null || Detail.Session.Id != e.SessionId) return;
                    Detail.Messages = PlanningLogic.UpsertMessage(Detail.Messages, e.Message);
                    SyncSeed();
                    return;
                case ArmadaEventTypes.PlanningSessionTool:
                    if (e.SessionId != SessionId || String.IsNullOrEmpty(e.MessageId) || String.IsNullOrEmpty(e.Id)) return;
                    Tools[e.MessageId!] = PlanningLogic.ApplyTool(Tools.TryGetValue(e.MessageId!, out List<AskToolChip>? existing) ? existing : null, e);
                    return;
                case ArmadaEventTypes.PlanningSessionThinking:
                    if (e.SessionId != SessionId || String.IsNullOrEmpty(e.MessageId) || String.IsNullOrEmpty(e.Delta)) return;
                    Thinking[e.MessageId!] = (Thinking.TryGetValue(e.MessageId!, out string? prior) ? prior : "") + e.Delta;
                    return;
                case ArmadaEventTypes.PlanningSessionSummaryCreated:
                    if (String.IsNullOrEmpty(e.SessionId) || e.SessionId != SessionId || e.Draft == null) return;
                    if (!String.IsNullOrEmpty(e.MessageId)) SelectedMessageId = e.MessageId!;
                    DispatchTitle.Value = e.Draft.Title ?? "";
                    DispatchDescription.Text = e.Draft.Description ?? "";
                    _Seed = new PlanningDispatchSeed(e.SessionId + ":" + (e.MessageId ?? "latest"), e.Draft.Title ?? "", e.Draft.Description ?? "", "summary");
                    return;
                case ArmadaEventTypes.PlanningSessionDispatchCreated:
                    if (String.IsNullOrEmpty(e.SessionId) || e.SessionId != SessionId || String.IsNullOrEmpty(e.VoyageId)) return;
                    Toast(NotificationSeverityEnum.Success, Tr("Dispatch created from this planning session."));
                    return;
                case ArmadaEventTypes.PlanningSessionDeleted:
                    if (String.IsNullOrEmpty(e.SessionId)) return;
                    Sessions = Sessions.Where(s => s.Id != e.SessionId).ToList();
                    SyncGrid();
                    if (e.SessionId == SessionId)
                    {
                        Detail = null;
                        SelectedMessageId = "";
                        _Seed = null;
                        if (!_Deleting) Toast(NotificationSeverityEnum.Warning, Tr("Planning session deleted."));
                        Context.Navigate("/planning");
                    }

                    return;
                default:
                    return;
            }
        }

        private void OnCaptainChanged(ArmadaSocketMessage message)
        {
            EntityChangedEvent? e = message.GetData<EntityChangedEvent>();
            CaptainStateEnum? typed = e?.CaptainState;
            if (e == null || String.IsNullOrEmpty(e.Id) || String.IsNullOrEmpty(e.State) || typed == null) return;
            CaptainStateEnum state = typed.Value;
            foreach (Captain c in Reference.Captains.Where(c => c.Id == e.Id))
            {
                c.State = state;
                if (!String.IsNullOrEmpty(e.Name)) c.Name = e.Name!;
            }

            if (Detail?.Captain != null && Detail.Captain.Id == e.Id)
            {
                Detail.Captain.State = state;
                if (!String.IsNullOrEmpty(e.Name)) Detail.Captain.Name = e.Name!;
            }

            ResetCaptainOptions();
        }

        private void ReferenceArrived(string name)
        {
            switch (name)
            {
                case "captains":
                    ResetCaptainOptions();
                    break;
                case "fleets":
                    List<SelectOption<string>> fleets = new List<SelectOption<string>> { new SelectOption<string>("", Tr("Any fleet")) };
                    fleets.AddRange(Reference.Fleets.Select(f => new SelectOption<string>(f.Id, f.Name)));
                    Reset(StartFleet, fleets, _PendingFleet);
                    FleetChanged();
                    break;
                case "vessels":
                    FleetChanged();
                    break;
                case "pipelines":
                    List<SelectOption<string>> pipelines = new List<SelectOption<string>> { new SelectOption<string>("", Tr("Inherit later during dispatch")) };
                    pipelines.AddRange(Reference.Pipelines.Select(p => new SelectOption<string>(p.Id, p.Name)));
                    Reset(StartPipeline, pipelines, _PendingPipeline);
                    break;
                case "playbooks":
                    StartPlaybooks.Available = Reference.Playbooks;
                    StartPlaybooks.Loaded = true;
                    break;
            }
        }

        private void ResetCaptainOptions()
        {
            List<SelectOption<string>> options = new List<SelectOption<string>>();
            foreach (Captain c in Reference.Captains)
            {
                SelectOption<string> o = new SelectOption<string>(c.Id, c.Name + " (" + c.Runtime + ") - " + c.State + (c.SupportsPlanningSessions ? "" : " - planning unsupported"));
                o.Enabled = PlanningLogic.CanStartPlanning(c);
                options.Add(o);
            }

            Reset(StartCaptain, options, _PendingCaptain);
        }

        private void FleetChanged()
        {
            string fleet = StartFleet.Value ?? "";
            List<Vessel> available = fleet.Length > 0 ? Reference.Vessels.Where(v => v.FleetId == fleet).ToList() : Reference.Vessels;
            string? before = StartVessel.Value;
            List<SelectOption<string>> options = available.OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase).Select(v => new SelectOption<string>(v.Id, v.Name, v.Id)).ToList();
            Reset(StartVessel, options, _PendingVessel);
            if (StartVessel.Value != before) LoadReadiness();
        }

        private static void Reset(SelectField<string> select, List<SelectOption<string>> options, string? preferred)
        {
            string? keep = preferred ?? select.Value;
            select.Options = options;
            select.SetValue(keep);
            if (select.Selected == null && options.Count > 0 && options[0].Value == "") select.SetValue("");
        }

        private void LoadReadiness()
        {
            string? vesselId = StartVessel.Value;
            int generation = ++_ReadinessGeneration;
            if (String.IsNullOrEmpty(vesselId))
            {
                StartReadiness = null;
                LoadingReadiness = false;
                return;
            }

            LoadingReadiness = true;
            Call((c, t) => c.GetVesselReadinessAsync(vesselId!, null, t), r =>
            {
                if (generation != _ReadinessGeneration) return;
                StartReadiness = r;
                LoadingReadiness = false;
            }, null, ex =>
            {
                if (generation != _ReadinessGeneration) return;
                StartReadiness = null;
                LoadingReadiness = false;
            });
        }

        #endregion
    }
}

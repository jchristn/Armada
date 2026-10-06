namespace Armada.Tui.Screens.Operations
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using Armada.Client.Socket;
    using Armada.Core;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Routing;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;
    using TUIKit.Input;

    /// <summary>
    /// Backlog item (W3.6, <c>/backlog/:id</c>, <c>/objectives/:id</c>, and <c>/backlog/new</c>), the dashboard's
    /// ObjectiveDetail page: header actions (View JSON, History, Refresh GitHub, Start Planning, Open In Dispatch,
    /// Draft Release, Duplicate, Delete), the full edit form (every field, long text in <c>$EDITOR</c> with
    /// <c>Ctrl+E</c>, Save and Back with dirty tracking), an overview with the info cards, readiness notes, and the
    /// GitHub source, the linked records (fleets, vessels, planning and refinement sessions, voyages, missions, checks,
    /// releases, deployments, incidents; <c>Enter</c> opens), and backlog refinement (start a session, the session list,
    /// the transcript with message selection, Send, Stop, Delete, Summarize, Apply To Backlog Item, and the summary
    /// draft). Live on <c>objective.changed</c>, every <c>objective-refinement-session.*</c> event, and
    /// <c>captain.changed</c>. Not thread-safe.
    /// </summary>
    public class BacklogItemScreen : OpsDetailScreen
    {
        #region Public-Members

        /// <summary>
        /// True for <c>/backlog/new</c>.
        /// </summary>
        public bool CreateMode { get; }

        /// <summary>
        /// The loaded item, or null.
        /// </summary>
        public Objective? Item { get; private set; } = null;

        /// <summary>
        /// The edit form.
        /// </summary>
        public FormView Form { get; } = new FormView();

        /// <summary>
        /// Title field.
        /// </summary>
        public InputField TitleField { get; } = new InputField();

        /// <summary>
        /// Primary vessel field.
        /// </summary>
        public SelectField<string> VesselField { get; }

        /// <summary>
        /// Description field.
        /// </summary>
        public OpsTextArea DescriptionField { get; } = new OpsTextArea();

        /// <summary>
        /// Links grid.
        /// </summary>
        public ArmadaGrid<BacklogLink> Links { get; } = new ArmadaGrid<BacklogLink>(l => l.Key);

        /// <summary>
        /// Refinement sessions grid.
        /// </summary>
        public ArmadaGrid<ObjectiveRefinementSession> Sessions { get; } = new ArmadaGrid<ObjectiveRefinementSession>(s => s.Id);

        /// <summary>
        /// Refinement start form.
        /// </summary>
        public FormView StartForm { get; } = new FormView();

        /// <summary>
        /// Refinement captain field.
        /// </summary>
        public SelectField<string> RefinementCaptain { get; }

        /// <summary>
        /// Selected refinement session detail, or null.
        /// </summary>
        public ObjectiveRefinementSessionDetail? Detail { get; private set; } = null;

        /// <summary>
        /// Transcript.
        /// </summary>
        public RefinementTranscriptView Transcript { get; } = new RefinementTranscriptView();

        /// <summary>
        /// Refinement composer.
        /// </summary>
        public OpsTextArea Composer { get; } = new OpsTextArea();

        /// <summary>
        /// Latest summary draft, or null.
        /// </summary>
        public ObjectiveRefinementSummaryResponse? SummaryDraft { get; private set; } = null;

        /// <summary>
        /// True for tenant and global admins.
        /// </summary>
        public bool CanManage
        {
            get { return IsTenantAdmin; }
        }

        #endregion

        #region Private-Members

        private readonly string _Id;
        private readonly SelectField<string> _Status;
        private readonly InputField _Owner = new InputField();
        private readonly OpsTextArea _Tags = new OpsTextArea();
        private readonly OpsTextArea _RefinementSummary = new OpsTextArea();
        private readonly OpsTextArea _Acceptance = new OpsTextArea();
        private readonly OpsTextArea _NonGoals = new OpsTextArea();
        private readonly OpsTextArea _Rollout = new OpsTextArea();
        private readonly OpsTextArea _Evidence = new OpsTextArea();
        private readonly SelectField<string> _Kind;
        private readonly InputField _Category = new InputField();
        private readonly SelectField<string> _Priority;
        private readonly SelectField<string> _BacklogState;
        private readonly SelectField<string> _Effort;
        private readonly InputField _Rank = new InputField();
        private readonly InputField _TargetVersion = new InputField();
        private readonly DateField _Due = new DateField();
        private readonly SelectField<string> _Parent;
        private readonly SelectField<string> _Pipeline;
        private readonly MultiSelectField<string> _BlockedBy = new MultiSelectField<string>();
        private readonly OpsTextArea _Playbooks = new OpsTextArea();
        private readonly SelectField<string> _RefinementVessel;
        private readonly SelectField<string> _RefinementFleet;
        private readonly InputField _RefinementTitle = new InputField();
        private readonly OpsTextArea _RefinementPrompt = new OpsTextArea();
        private readonly OpsDocumentView _Overview = new OpsDocumentView();
        private readonly OpsDocumentView _TranscriptHeader = new OpsDocumentView();
        private readonly OpsDocumentView _SummaryView = new OpsDocumentView();
        private readonly TextBlock _CaptainHelper = new TextBlock("", t => t.Muted);
        private readonly TextBlock _TranscriptHelper = new TextBlock("", t => t.Muted);
        private readonly TextBlock _FailureLine = new TextBlock("", t => t.Error);
        private readonly ButtonRow _TranscriptButtons = new ButtonRow();
        private readonly BacklogStackPanel _TranscriptPanel = new BacklogStackPanel();
        private List<Objective> _Available = new List<Objective>();
        private List<string> _VesselIds = new List<string>();
        private List<string> _FleetIds = new List<string>();
        private List<ObjectiveRefinementSession> _SessionList = new List<ObjectiveRefinementSession>();
        private string _SelectedSessionId = "";
        private bool _Busy = false;
        private bool _SessionsRequested = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Services.</param>
        public BacklogItemScreen(RouteMatch route, TuiContext context)
            : base(route, context, "BacklogItemScreen", "Backlog Item")
        {
            _Id = route.Param("id") ?? "";
            CreateMode = _Id == "new";
            VesselField = NewSelect("Vessel", new List<SelectOption<string>> { new SelectOption<string>("", Tr("No vessel selected")) });
            _Status = NewSelect("Status", Values(BacklogLogic.Statuses));
            _Kind = NewSelect("Kind", Values(BacklogLogic.Kinds));
            _Priority = NewSelect("Priority", Values(BacklogLogic.Priorities));
            _BacklogState = NewSelect("Backlog State", Values(BacklogLogic.BacklogStates));
            _Effort = NewSelect("Effort", Values(BacklogLogic.Efforts));
            _Parent = NewSelect("Parent Objective", new List<SelectOption<string>> { new SelectOption<string>("", Tr("No parent objective")) });
            _Pipeline = NewSelect("Suggested Pipeline", new List<SelectOption<string>> { new SelectOption<string>("", Tr("None")) });
            _BlockedBy.ModalHost = context.Modals;
            _BlockedBy.PickerTitle = "Blocked By Objectives";
            _BlockedBy.Placeholder = "None";
            RefinementCaptain = NewSelect("Captain", new List<SelectOption<string>> { new SelectOption<string>("", Tr("Select a captain")) });
            _RefinementVessel = NewSelect("Vessel Context", new List<SelectOption<string>> { new SelectOption<string>("", Tr("No vessel context")) });
            _RefinementFleet = NewSelect("Fleet Context", new List<SelectOption<string>> { new SelectOption<string>("", Tr("No fleet context")) });

            BuildForm();
            Heading = CreateMode ? Tr("Create Backlog Item") : null;
            AddPanel("item", "Backlog Item", Form);
            if (!CreateMode)
            {
                _Overview.Builder = BuildOverview;
                AddPanel("overview", "Overview", _Overview);
                BuildLinks();
                AddPanel("links", "Links", Links);
                AddPanel("refinement", "Refinement", BuildStartPanel());
                AddPanel("transcript", "Transcript", BuildTranscriptPanel());
                _SummaryView.Builder = BuildSummary;
                AddPanel("summary", "Summary Draft", _SummaryView);
            }

            BuildActions();
            Reference.Changed += (s, name) => ReferenceArrived(name);
            Reference.Ensure("vessels", "fleets", "captains", "pipelines");
            Subscribe("objective.changed", OnObjectiveChanged);
            Subscribe("captain.changed", OnCaptainChanged);
            Subscribe("objective-refinement-session.", OnRefinementEvent);
            LoadAvailable();
            if (!String.IsNullOrEmpty(OpsHandoff.Get(route, "refinementSessionId"))) SelectPanel("transcript");
            Load();
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override int DefaultRefreshSeconds()
        {
            return 0;
        }

        /// <inheritdoc />
        public override void Load()
        {
            if (CreateMode)
            {
                Loaded = true;
                string? vessel = OpsHandoff.Get(Route, "vesselId");
                if (vessel != null && _VesselIds.Count == 0)
                {
                    _VesselIds = BacklogLogic.ReplacePrimary(_VesselIds, vessel);
                    SyncPrimaryVessel();
                    Form.MarkClean();
                }

                return;
            }

            Call((c, t) => c.GetBacklogItemAsync(_Id, t), item =>
            {
                if (item == null)
                {
                    LoadError = Tr("Failed to load backlog item.");
                    return;
                }

                LoadError = null;
                bool first = Item == null;
                if (first || !Form.IsDirty) Hydrate(item);
                else
                {
                    Item = item;
                    _Overview.Invalidate();
                    RebuildLinks();
                }

                Loaded = true;
                if (first || !_SessionsRequested)
                {
                    _SessionsRequested = true;
                    LoadSessions(OpsHandoff.Get(Route, "refinementSessionId"));
                }
            }, null, ex => LoadError = String.IsNullOrEmpty(ex.Message) ? Tr("Failed to load backlog item.") : ex.Message);
        }

        /// <summary>
        /// Save the form (create or update), as the dashboard's Save Changes / Create Backlog Item.
        /// </summary>
        public void Save()
        {
            if (!CanManage)
            {
                ShowMessage(Tr("You can view backlog items, but only tenant administrators can create or change them."));
                return;
            }

            if (_Busy) return;
            if (TitleField.Value.Trim().Length == 0)
            {
                ShowMessage(Tr("Backlog item title is required."));
                return;
            }

            if (!Form.ValidateAll()) return;
            ObjectiveUpsertRequest payload = BuildPayload();
            _Busy = true;
            if (CreateMode)
            {
                Call((c, t) => c.CreateBacklogItemAsync(payload, t), created =>
                {
                    _Busy = false;
                    if (created == null) return;
                    Form.MarkClean();
                    Toast(NotificationSeverityEnum.Success, Tr("Backlog item \"{{title}}\" created.", LocalizationArgs.Of("title", created.Title)));
                    Context.Navigate("/backlog/" + Uri.EscapeDataString(created.Id));
                }, null, ex => { _Busy = false; ShowMessage(String.IsNullOrEmpty(ex.Message) ? Tr("Save failed.") : ex.Message); });
                return;
            }

            Call((c, t) => c.UpdateBacklogItemAsync(_Id, payload, t), updated =>
            {
                _Busy = false;
                if (updated == null) return;
                Hydrate(updated);
                Toast(NotificationSeverityEnum.Success, Tr("Backlog item \"{{title}}\" saved.", LocalizationArgs.Of("title", updated.Title)));
            }, null, ex => { _Busy = false; ShowMessage(String.IsNullOrEmpty(ex.Message) ? Tr("Save failed.") : ex.Message); });
        }

        /// <summary>
        /// Return to the backlog (asks first when there are unsaved changes).
        /// </summary>
        public void Back()
        {
            if (Form.IsDirty && CanManage)
            {
                Confirm("Discard changes", Tr("Discard unsaved changes to this backlog item?"), () => Context.Navigate("/backlog"), "Discard");
                return;
            }

            Context.Navigate("/backlog");
        }

        /// <summary>
        /// The update payload built from the form (the dashboard's <c>buildPayload</c>).
        /// </summary>
        /// <returns>Request.</returns>
        public ObjectiveUpsertRequest BuildPayload()
        {
            ObjectiveUpsertRequest r = new ObjectiveUpsertRequest();
            r.Title = Null(TitleField.Value);
            r.Description = Null(DescriptionField.Text);
            r.Status = Enum.TryParse(_Status.Value, out ObjectiveStatusEnum st) ? st : ObjectiveStatusEnum.Draft;
            r.Kind = Enum.TryParse(_Kind.Value, out ObjectiveKindEnum k) ? k : ObjectiveKindEnum.Feature;
            r.Category = Null(_Category.Value);
            r.Priority = Enum.TryParse(_Priority.Value, out ObjectivePriorityEnum p) ? p : ObjectivePriorityEnum.P2;
            r.Rank = Int32.TryParse(_Rank.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int rank) ? rank : (int?)null;
            r.BacklogState = Enum.TryParse(_BacklogState.Value, out ObjectiveBacklogStateEnum bs) ? bs : ObjectiveBacklogStateEnum.Inbox;
            r.Effort = Enum.TryParse(_Effort.Value, out ObjectiveEffortEnum e) ? e : ObjectiveEffortEnum.M;
            r.Owner = Null(_Owner.Value);
            r.TargetVersion = Null(_TargetVersion.Value);
            r.DueUtc = _Due.ValueUtc;
            r.ParentObjectiveId = Null(_Parent.Value);
            r.BlockedByObjectiveIds = _BlockedBy.Values.ToList();
            r.RefinementSummary = Null(_RefinementSummary.Text);
            r.SuggestedPipelineId = Null(_Pipeline.Value);
            r.SuggestedPlaybooks = BacklogLogic.ParsePlaybooks(_Playbooks.Text);
            r.Tags = BacklogLogic.ParseTags(_Tags.Text);
            r.AcceptanceCriteria = BacklogLogic.SplitList(_Acceptance.Text);
            r.NonGoals = BacklogLogic.SplitList(_NonGoals.Text);
            r.RolloutConstraints = BacklogLogic.SplitList(_Rollout.Text);
            r.EvidenceLinks = BacklogLogic.SplitList(_Evidence.Text);
            r.FleetIds = _FleetIds.ToList();
            r.VesselIds = _VesselIds.ToList();
            Objective? o = Item;
            r.PlanningSessionIds = (o?.PlanningSessionIds ?? new List<string>()).ToList();
            r.RefinementSessionIds = (o?.RefinementSessionIds ?? new List<string>()).ToList();
            r.VoyageIds = (o?.VoyageIds ?? new List<string>()).ToList();
            r.MissionIds = (o?.MissionIds ?? new List<string>()).ToList();
            r.CheckRunIds = (o?.CheckRunIds ?? new List<string>()).ToList();
            r.ReleaseIds = (o?.ReleaseIds ?? new List<string>()).ToList();
            r.DeploymentIds = (o?.DeploymentIds ?? new List<string>()).ToList();
            r.IncidentIds = (o?.IncidentIds ?? new List<string>()).ToList();
            return r;
        }

        /// <summary>
        /// Start a refinement session from the start form.
        /// </summary>
        public void StartRefinement()
        {
            Objective? o = Item;
            string captainId = RefinementCaptain.Value ?? "";
            if (o == null || captainId.Length == 0 || _Busy) return;
            ObjectiveRefinementSessionCreateRequest req = new ObjectiveRefinementSessionCreateRequest();
            req.CaptainId = captainId;
            req.FleetId = Null(_RefinementFleet.Value);
            req.VesselId = Null(_RefinementVessel.Value);
            req.Title = Null(_RefinementTitle.Value);
            req.InitialMessage = Null(_RefinementPrompt.Text);
            _Busy = true;
            StartForm.SaveButton.Label = "Starting...";
            Call((c, t) => c.CreateBacklogRefinementSessionAsync(o.Id, req, t), detail =>
            {
                _Busy = false;
                StartForm.SaveButton.Label = "Start Refinement";
                if (detail == null) return;
                UpsertSession(detail.Session);
                ShowDetail(detail);
                _RefinementPrompt.Text = "";
                _RefinementTitle.Value = "";
                SummaryDraft = null;
                _SummaryView.Invalidate();
                Toast(NotificationSeverityEnum.Success, Tr("Refinement session started with {{captain}}.", LocalizationArgs.Of("captain", detail.Captain?.Name ?? detail.Session.CaptainId)));
                SelectPanel("transcript");
                LoadSessions(detail.Session.Id);
            }, null, ex =>
            {
                _Busy = false;
                StartForm.SaveButton.Label = "Start Refinement";
                ShowMessage(String.IsNullOrEmpty(ex.Message) ? Tr("Failed to start refinement session.") : ex.Message);
            });
        }

        /// <summary>
        /// Send the composer text to the selected session.
        /// </summary>
        public void SendRefinement()
        {
            ObjectiveRefinementSessionDetail? d = Detail;
            string text = Composer.Text.Trim();
            if (d == null || text.Length == 0 || !CanManage || _Busy) return;
            ObjectiveRefinementMessageRequest req = new ObjectiveRefinementMessageRequest();
            req.Content = text;
            _Busy = true;
            Call((c, t) => c.SendObjectiveRefinementMessageAsync(d.Session.Id, req, t), detail =>
            {
                _Busy = false;
                Composer.Text = "";
                if (detail == null) return;
                ObjectiveRefinementSessionDetail merged = MergeWithLive(detail);
                ShowDetail(merged);
                UpsertSession(merged.Session);
            }, null, ex => { _Busy = false; ShowMessage(String.IsNullOrEmpty(ex.Message) ? Tr("Failed to send refinement message.") : ex.Message); });
        }

        /// <summary>
        /// Summarize the selected transcript message.
        /// </summary>
        public void Summarize()
        {
            ObjectiveRefinementSessionDetail? d = Detail;
            if (d == null || !CanManage || Transcript.SelectedId.Length == 0) return;
            ObjectiveRefinementSummaryRequest req = new ObjectiveRefinementSummaryRequest();
            req.MessageId = Transcript.SelectedId;
            Call((c, t) => c.SummarizeObjectiveRefinementSessionAsync(d.Session.Id, req, t), summary =>
            {
                if (summary == null) return;
                SetSummary(summary);
                Toast(NotificationSeverityEnum.Success, Tr("Refinement summary generated."));
            }, null, ex => ShowMessage(String.IsNullOrEmpty(ex.Message) ? Tr("Failed to summarize refinement session.") : ex.Message));
        }

        /// <summary>
        /// Apply the selected message's summary back to the backlog item.
        /// </summary>
        public void Apply()
        {
            ObjectiveRefinementSessionDetail? d = Detail;
            Objective? o = Item;
            if (d == null || o == null || !CanManage || Transcript.SelectedId.Length == 0) return;
            ObjectiveRefinementApplyRequest req = new ObjectiveRefinementApplyRequest();
            req.MessageId = Transcript.SelectedId;
            req.MarkMessageSelected = true;
            req.PromoteBacklogState = true;
            string sessionId = d.Session.Id;
            Call((c, t) => c.ApplyObjectiveRefinementSummaryAsync(sessionId, req, t), result =>
            {
                if (result == null) return;
                if (result.Objective != null) Hydrate(result.Objective);
                if (result.Summary != null) SetSummary(result.Summary);
                Toast(NotificationSeverityEnum.Success, Tr("Applied refinement summary back to the backlog item."));
                LoadSessions(sessionId);
            }, null, ex => ShowMessage(String.IsNullOrEmpty(ex.Message) ? Tr("Failed to apply refinement summary.") : ex.Message));
        }

        /// <summary>
        /// Stop the selected session.
        /// </summary>
        public void StopSession()
        {
            ObjectiveRefinementSessionDetail? d = Detail;
            if (d == null) return;
            Call((c, t) => c.StopObjectiveRefinementSessionAsync(d.Session.Id, t), detail =>
            {
                if (detail != null)
                {
                    ObjectiveRefinementSessionDetail merged = MergeWithLive(detail);
                    ShowDetail(merged);
                    UpsertSession(merged.Session);
                }

                Toast(NotificationSeverityEnum.Warning, Tr("Refinement session is stopping."));
            }, null, ex => ShowMessage(String.IsNullOrEmpty(ex.Message) ? Tr("Failed to stop refinement session.") : ex.Message));
        }

        /// <summary>
        /// Delete the selected session.
        /// </summary>
        public void DeleteSession()
        {
            ObjectiveRefinementSessionDetail? d = Detail;
            if (d == null) return;
            string sessionId = d.Session.Id;
            Run((c, t) => c.DeleteObjectiveRefinementSessionAsync(sessionId, t), () =>
            {
                _SessionList = _SessionList.Where(s => s.Id != sessionId).ToList();
                _SelectedSessionId = "";
                ShowDetail(null);
                SummaryDraft = null;
                _SummaryView.Invalidate();
                Toast(NotificationSeverityEnum.Warning, Tr("Refinement session deleted."));
                LoadSessions(null);
            }, null, ex => ShowMessage(String.IsNullOrEmpty(ex.Message) ? Tr("Failed to delete refinement session.") : ex.Message));
        }

        /// <summary>
        /// Select a refinement session and load its transcript.
        /// </summary>
        /// <param name="sessionId">Session id.</param>
        public void SelectSession(string sessionId)
        {
            if (String.IsNullOrEmpty(sessionId)) return;
            _SelectedSessionId = sessionId;
            Call((c, t) => c.GetObjectiveRefinementSessionAsync(sessionId, t), detail =>
            {
                if (_SelectedSessionId != sessionId) return;
                ShowDetail(detail != null ? MergeWithLive(detail) : null);
            }, null, ex => { if (_SelectedSessionId == sessionId) ShowDetail(null); });
        }

        #endregion

        #region Private-Methods

        private static string? Null(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value!.Trim();
        }

        private static List<SelectOption<string>> Values(string[] values)
        {
            return values.Select(v => new SelectOption<string>(v, v)).ToList();
        }

        private string PrimaryVesselId()
        {
            return _VesselIds.FirstOrDefault() ?? Item?.VesselIds?.FirstOrDefault() ?? "";
        }

        private string PrimaryFleetId()
        {
            return _FleetIds.FirstOrDefault() ?? Item?.FleetIds?.FirstOrDefault() ?? "";
        }

        private int? GitHubNumber()
        {
            return Item?.SourceNumber;
        }

        private void BuildForm()
        {
            Form.Localizer = Localizer;
            Form.SaveButton.Label = CreateMode ? "Create Backlog Item" : "Save Changes";
            Form.DiscardButton.Label = "Back";
            Form.ShowButtons = true;
            Form.SaveRequested += (s, e) => Save();
            Form.DiscardRequested += (s, e) => Back();
            TitleField.Placeholder = "Add feature to improve login";
            TitleField.Validator = v => String.IsNullOrWhiteSpace(v) ? "Backlog item title is required." : null;
            _Rank.Validator = v => String.IsNullOrWhiteSpace(v) || Int32.TryParse(v.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int r) ? null : "Enter a number.";
            _Tags.Placeholder = "key:value (one per line)";
            _Playbooks.Placeholder = "playbook-id:InlineFullContent";
            foreach (OpsTextArea area in new OpsTextArea[] { DescriptionField, _Tags, _RefinementSummary, _Acceptance, _NonGoals, _Rollout, _Evidence, _Playbooks, _RefinementPrompt, Composer })
            {
                area.ExternalEditor = (text, done) => EditExternally(text, done);
            }

            VesselField.ValueChanged += (s, e) =>
            {
                string next = e.NewValue ?? "";
                _VesselIds = BacklogLogic.ReplacePrimary(_VesselIds, next);
                if (next.Length == 0) _FleetIds = new List<string>();
                else
                {
                    Vessel? v = Reference.Vessels.FirstOrDefault(x => x.Id == next);
                    _FleetIds = !String.IsNullOrEmpty(v?.FleetId) ? BacklogLogic.ReplacePrimary(_FleetIds, v!.FleetId) : new List<string>();
                }
            };

            Form.AddSection("Backlog Item");
            Form.AddField("Title", TitleField);
            Form.AddField("Vessel", VesselField);
            Form.AddField("Status", _Status);
            Form.AddField("Owner", _Owner);
            Form.AddField("Description", DescriptionField, null, 5);
            Form.AddField("Tags", _Tags, "One tag per line as key:value (or just a key).", 3);
            Form.AddSection("Scope");
            Form.AddField("Refinement Summary", _RefinementSummary, null, 4);
            Form.AddField("Acceptance Criteria", _Acceptance, null, 5);
            Form.AddField("Non-Goals", _NonGoals, null, 5);
            Form.AddField("Rollout Constraints", _Rollout, null, 4);
            Form.AddField("Evidence Links", _Evidence, null, 4);
            Form.AddSection("Workflow Metadata");
            Form.AddField("Kind", _Kind);
            Form.AddField("Category", _Category);
            Form.AddField("Priority", _Priority);
            Form.AddField("Backlog State", _BacklogState);
            Form.AddField("Effort", _Effort);
            Form.AddField("Rank", _Rank);
            Form.AddField("Target Version", _TargetVersion);
            Form.AddField("Due UTC", _Due);
            Form.AddField("Parent Objective", _Parent);
            Form.AddField("Suggested Pipeline", _Pipeline);
            Form.AddField("Blocked By Objectives", _BlockedBy);
            Form.AddField("Suggested Playbooks", _Playbooks, null, 3);
            _Status.SetValue("Draft");
            _Kind.SetValue("Feature");
            _Priority.SetValue("P2");
            _BacklogState.SetValue("Inbox");
            _Effort.SetValue("M");
            VesselField.SetValue("");
            _Parent.SetValue("");
            _Pipeline.SetValue("");
            Form.MarkClean();
        }

        private void BuildLinks()
        {
            Links.MultiSelect = false;
            Links.Dispatcher = Context.Dispatcher;
            Links.ModalHost = Context.Modals;
            Links.EmptyText = "None";
            Links.AddColumn(new GridColumn<BacklogLink>("kind", "Kind", l => Tr(l.Kind)) { Width = 22 });
            Links.AddColumn(new GridColumn<BacklogLink>("name", "Name", l => l.Name) { Weight = 3 });
            Links.AddColumn(new GridColumn<BacklogLink>("id", "ID", l => l.Id) { Weight = 2 });
            Links.Activated += (s, l) => Context.Navigate(l.Route);
        }

        private BacklogStackPanel BuildStartPanel()
        {
            BacklogStackPanel panel = new BacklogStackPanel();
            StartForm.Localizer = Localizer;
            StartForm.SaveButton.Label = "Start Refinement";
            StartForm.SaveButton.Hint = "Ctrl+S";
            StartForm.DiscardButton.Visible = false;
            StartForm.ShowButtons = CanManage;
            StartForm.SaveRequested += (s, e) => StartRefinement();
            _RefinementTitle.Placeholder = "Optional refinement session title";
            _RefinementPrompt.Placeholder = "Optional kickoff prompt for the selected captain";
            StartForm.AddField("Captain", RefinementCaptain);
            StartForm.AddField("Vessel Context", _RefinementVessel);
            StartForm.AddField("Fleet Context", _RefinementFleet);
            StartForm.AddField("Session Title", _RefinementTitle);
            StartForm.AddField("Initial Refinement Prompt", _RefinementPrompt, null, 3);
            RefinementCaptain.ValueChanged += (s, e) => SyncCaptainHelper();
            Sessions.MultiSelect = false;
            Sessions.Dispatcher = Context.Dispatcher;
            Sessions.ModalHost = Context.Modals;
            Sessions.EmptyText = "No refinement sessions yet.";
            Sessions.AddColumn(new GridColumn<ObjectiveRefinementSession>("title", "Title", x => (x.Id == _SelectedSessionId ? "> " : "") + x.Title) { Weight = 3 });
            Sessions.AddColumn(new GridColumn<ObjectiveRefinementSession>("status", "Status", x => StatusBadge.Label(x.Status)) { Width = 14, Style = (x, t) => StatusBadge.Style(x.Status, t) });
            Sessions.AddColumn(new GridColumn<ObjectiveRefinementSession>("captain", "Captain", x => Reference.CaptainName(x.CaptainId)) { Weight = 2 });
            Sessions.AddColumn(new GridColumn<ObjectiveRefinementSession>("updated", "Updated", x => Context.Loc.FormatRelative(x.LastUpdateUtc, Context.Clock.UtcNow)) { Width = 16 });
            Sessions.Activated += (s, x) =>
            {
                SelectSession(x.Id);
                SelectPanel("transcript");
            };
            panel.AddRow(new TextBlock("Choose the captain explicitly here. Refinement is a backlog workflow, separate from repository-aware planning and dispatch.", t => t.Muted), () => 2);
            panel.AddRow(StartForm, () => CanManage ? 10 : 8);
            panel.AddRow(_CaptainHelper, () => _CaptainHelper.Text.Length > 0 ? 2 : 0);
            panel.AddRow(new TextBlock("Refinement Sessions", t => t.Accent), () => 1);
            panel.AddRow(Sessions, () => 0);
            return panel;
        }

        private BacklogStackPanel BuildTranscriptPanel()
        {
            _TranscriptHeader.CanFocus = false;
            _TranscriptHeader.Builder = BuildTranscriptHeader;
            Transcript.FormatAge = utc => Context.Loc.FormatRelative(utc, Context.Clock.UtcNow);
            Transcript.SelectionChanged += (s, id) => SyncTranscriptHelper();
            Composer.Placeholder = "Ask the selected captain to sharpen scope, acceptance criteria, non-goals, or rollout constraints.";
            Button send = new Button("Send", SendRefinement);
            send.Hint = "Ctrl+S";
            _TranscriptButtons.Add(send);
            _TranscriptButtons.Add(new Button("Summarize", Summarize));
            _TranscriptButtons.Add(new Button("Apply To Backlog Item", Apply));
            _TranscriptButtons.Add(new Button("Stop Session", StopSession));
            _TranscriptButtons.Add(new Button("Delete Session", DeleteSession));
            _TranscriptPanel.KeyHook = key =>
            {
                if (key.Code == KeyCode.Character && (key.Modifiers & KeyModifiers.Ctrl) != 0 && Char.ToLowerInvariant((char)key.Rune) == 's')
                {
                    SendRefinement();
                    return true;
                }

                return false;
            };
            _TranscriptPanel.AddRow(_TranscriptHeader, () => Detail == null ? 3 : 4);
            _TranscriptPanel.AddRow(Transcript, () => Detail == null ? 1 : 0);
            _TranscriptPanel.AddRow(new TextBlock("Send Refinement Message", t => t.Accent), () => Detail != null && CanManage ? 1 : 0);
            _TranscriptPanel.AddRow(Composer, () => Detail != null && CanManage ? 3 : 0);
            _TranscriptPanel.AddRow(_TranscriptButtons, () => Detail != null ? 1 : 0);
            _TranscriptPanel.AddRow(_TranscriptHelper, () => Detail != null && _TranscriptHelper.Text.Length > 0 ? 1 : 0);
            _TranscriptPanel.AddRow(_FailureLine, () => Detail != null && _FailureLine.Text.Length > 0 ? 2 : 0);
            ShowDetail(null);
            return _TranscriptPanel;
        }

        private void BuildActions()
        {
            bool loaded() => !CreateMode && Item != null;
            Action("save", "Save Changes", Save, null, () => CanManage);
            Action("back", "Back", Back, "b");
            Action("json", "View JSON", () => ShowJson(TitleField.Value, Item), "j", loaded, true);
            Action("history", "History", () => Context.Navigate("/history?objectiveId=" + Uri.EscapeDataString(Item!.Id)), "H", loaded, true);
            Action("refresh-github", "Refresh GitHub", RefreshGitHub, "F", () => loaded() && Item!.SourceProvider == Objective.GitHubSourceProvider && PrimaryVesselId().Length > 0 && GitHubNumber().HasValue, true);
            Action("planning", "Start Planning", StartPlanning, "P", () => loaded() && PrimaryVesselId().Length > 0, true);
            Action("dispatch", "Open In Dispatch", OpenInDispatch, "D", () => loaded() && PrimaryVesselId().Length > 0, true);
            Action("release", "Draft Release", DraftRelease, "R", () => loaded() && PrimaryVesselId().Length > 0, true);
            Action("duplicate", "Duplicate", Duplicate, "u", () => loaded() && CanManage, true);
            Action("delete", "Delete", Delete, "del", () => loaded() && CanManage, true, true);
            Action("start-refinement", "Start Refinement", StartRefinement, null, () => loaded() && CanManage && !String.IsNullOrEmpty(RefinementCaptain.Value));
            Action("send-refinement", "Send", SendRefinement, null, () => Detail != null && CanManage && Composer.Text.Trim().Length > 0);
            Action("summarize", "Summarize", Summarize, "m", () => Detail != null && CanManage && Transcript.SelectedId.Length > 0);
            Action("apply", "Apply To Backlog Item", Apply, "A", () => Detail != null && CanManage && Transcript.SelectedId.Length > 0);
            Action("stop-session", "Stop Session", StopSession, "x", () => Detail != null);
            Action("delete-session", "Delete Session", DeleteSession, "X", () => Detail != null, false, true);
        }

        private void Hydrate(Objective next)
        {
            Item = next;
            Heading = next.Title;
            Status = next.Status.ToString();
            SubtitleText = next.Kind + "  " + next.Priority + "  " + next.Effort + "  " + next.BacklogState + "   " + next.Id
                + (CanManage ? "" : "   " + Tr("You can view backlog items, but only tenant administrators can create or change them."));
            TitleField.Value = next.Title ?? "";
            DescriptionField.Text = next.Description ?? "";
            _Status.SetValue(next.Status.ToString());
            _Kind.SetValue(next.Kind.ToString());
            _Category.Value = next.Category ?? "";
            _Priority.SetValue(next.Priority.ToString());
            _Rank.Value = next.Rank.ToString(CultureInfo.InvariantCulture);
            _BacklogState.SetValue(next.BacklogState.ToString());
            _Effort.SetValue(next.Effort.ToString());
            _Owner.Value = next.Owner ?? "";
            _TargetVersion.Value = next.TargetVersion ?? "";
            _Due.SetDate(next.DueUtc.HasValue ? DateTime.SpecifyKind(next.DueUtc.Value, DateTimeKind.Utc).ToLocalTime() : (DateTime?)null);
            _RefinementSummary.Text = next.RefinementSummary ?? "";
            _Tags.Text = BacklogLogic.JoinList(next.Tags);
            _Acceptance.Text = BacklogLogic.JoinList(next.AcceptanceCriteria);
            _NonGoals.Text = BacklogLogic.JoinList(next.NonGoals);
            _Rollout.Text = BacklogLogic.JoinList(next.RolloutConstraints);
            _Evidence.Text = BacklogLogic.JoinList(next.EvidenceLinks);
            _Playbooks.Text = BacklogLogic.JoinPlaybooks(next.SuggestedPlaybooks);
            _VesselIds = (next.VesselIds ?? new List<string>()).ToList();
            _FleetIds = (next.FleetIds ?? new List<string>()).ToList();
            RebuildObjectiveOptions();
            _Parent.SetValue(next.ParentObjectiveId ?? "");
            _BlockedBy.SetValues(next.BlockedByObjectiveIds ?? new List<string>(), false);
            RebuildPipelineOptions();
            _Pipeline.SetValue(next.SuggestedPipelineId ?? "");
            SyncPrimaryVessel();
            if (String.IsNullOrEmpty(_RefinementVessel.Value)) _RefinementVessel.SetValue(next.VesselIds?.FirstOrDefault() ?? "");
            if (String.IsNullOrEmpty(_RefinementFleet.Value)) _RefinementFleet.SetValue(next.FleetIds?.FirstOrDefault() ?? "");
            Form.MarkClean();
            _Overview.Invalidate();
            RebuildLinks();
        }

        private void SyncPrimaryVessel()
        {
            List<string> keep = _VesselIds.ToList();
            List<string> keepFleets = _FleetIds.ToList();
            RebuildVesselOptions();
            string primary = keep.FirstOrDefault() ?? "";
            if (primary.Length > 0 && !VesselField.Options.Any(o => o.Value == primary)) VesselField.Options.Add(new SelectOption<string>(primary, primary));
            VesselField.SetValue(primary);
            _VesselIds = keep;
            _FleetIds = keepFleets;
            if (CreateMode && _FleetIds.Count == 0 && primary.Length > 0)
            {
                Vessel? v = Reference.Vessels.FirstOrDefault(x => x.Id == primary);
                if (!String.IsNullOrEmpty(v?.FleetId)) _FleetIds = new List<string> { v!.FleetId! };
            }
        }

        private void RebuildVesselOptions()
        {
            List<SelectOption<string>> options = new List<SelectOption<string>> { new SelectOption<string>("", Tr("No vessel selected")) };
            options.AddRange(Reference.Vessels.Select(v => new SelectOption<string>(v.Id, String.IsNullOrEmpty(v.FleetId) ? v.Name : v.Name + " (" + Reference.FleetName(v.FleetId) + ")")));
            VesselField.Options = options;
        }

        private void RebuildPipelineOptions()
        {
            string? current = _Pipeline.Value;
            _Pipeline.Options = Reference.PipelineOptions("None");
            if (current != null) _Pipeline.SetValue(current);
        }

        private void RebuildObjectiveOptions()
        {
            string self = Item?.Id ?? "";
            List<Objective> selectable = _Available.Where(o => o.Id != self).OrderBy(o => o.Rank).ThenBy(o => o.Title, StringComparer.Ordinal).ToList();
            string? parent = _Parent.Value ?? Item?.ParentObjectiveId;
            List<SelectOption<string>> parents = new List<SelectOption<string>> { new SelectOption<string>("", Tr("No parent objective")) };
            if (!String.IsNullOrEmpty(parent) && !selectable.Any(o => o.Id == parent))
                parents.Add(new SelectOption<string>(parent!, Tr("Unavailable backlog item ({{id}})", LocalizationArgs.Of("id", parent))));
            parents.AddRange(selectable.Select(o => new SelectOption<string>(o.Id, ObjectiveLabel(o))));
            _Parent.Options = parents;
            if (parent != null) _Parent.SetValue(parent);
            List<string> blocked = _BlockedBy.Values.Count > 0 ? _BlockedBy.Values.ToList() : (Item?.BlockedByObjectiveIds ?? new List<string>()).ToList();
            List<SelectOption<string>> blockers = blocked.Where(b => !selectable.Any(o => o.Id == b))
                .Select(b => new SelectOption<string>(b, Tr("Unavailable backlog item ({{id}})", LocalizationArgs.Of("id", b)))).ToList();
            blockers.AddRange(selectable.Select(o => new SelectOption<string>(o.Id, ObjectiveLabel(o))));
            _BlockedBy.Options = blockers;
            _BlockedBy.SetValues(blocked, false);
        }

        private string ObjectiveLabel(Objective o)
        {
            string vessel = o.VesselIds?.FirstOrDefault() ?? "";
            return vessel.Length > 0 ? o.Title + " - " + Reference.VesselName(vessel) + " (" + o.Id + ")" : o.Title + " (" + o.Id + ")";
        }

        private void ReferenceArrived(string name)
        {
            bool dirty = Form.IsDirty;
            if (name == "vessels" || name == "fleets")
            {
                SyncPrimaryVessel();
                string rv = _RefinementVessel.Value ?? "";
                _RefinementVessel.Options = Reference.VesselOptions("No vessel context");
                _RefinementVessel.SetValue(rv);
                string rf = _RefinementFleet.Value ?? "";
                _RefinementFleet.Options = Reference.FleetOptions("No fleet context");
                _RefinementFleet.SetValue(rf);
                RebuildLinks();
            }
            else if (name == "pipelines")
            {
                RebuildPipelineOptions();
            }
            else if (name == "captains")
            {
                RebuildCaptainOptions();
            }

            if (!dirty) Form.MarkClean();
            _Overview.Invalidate();
            _SummaryView.Invalidate();
        }

        private void RebuildCaptainOptions()
        {
            string current = RefinementCaptain.Value ?? "";
            List<SelectOption<string>> options = new List<SelectOption<string>> { new SelectOption<string>("", Tr("Select a captain")) };
            options.AddRange(Reference.Captains.Select(c => new SelectOption<string>(c.Id, c.Name + " (" + c.State + ")")));
            RefinementCaptain.Options = options;
            RefinementCaptain.SetValue(current);
            SyncCaptainHelper();
        }

        private void SyncCaptainHelper()
        {
            Captain? c = Reference.Captains.FirstOrDefault(x => x.Id == RefinementCaptain.Value);
            _CaptainHelper.Translate = false;
            _CaptainHelper.Text = c == null ? "" : Tr("Selected captain {{captain}} is currently {{state}}. Armada will fail fast if that captain cannot accept a refinement session.", LocalizationArgs.Of("captain", c.Name, "state", c.State.ToString()));
        }

        private void LoadAvailable()
        {
            ObjectiveQuery q = new ObjectiveQuery();
            q.PageNumber = 1;
            q.PageSize = 9999;
            Call((c, t) => c.ListBacklogAsync(q, t), result =>
            {
                bool dirty = Form.IsDirty;
                _Available = result?.Objects ?? new List<Objective>();
                RebuildObjectiveOptions();
                if (!dirty) Form.MarkClean();
            }, null, ex => { });
        }

        private void RebuildLinks()
        {
            Objective? o = Item;
            List<BacklogLink> rows = new List<BacklogLink>();
            if (o != null)
            {
                foreach (string id in _FleetIds) rows.Add(new BacklogLink("Linked Fleets", id, Reference.FleetName(id), "/fleets/" + Uri.EscapeDataString(id)));
                foreach (string id in _VesselIds) rows.Add(new BacklogLink("Linked Vessels", id, Reference.VesselName(id), "/vessels/" + Uri.EscapeDataString(id)));
                Add(rows, "Planning Sessions", o.PlanningSessionIds, id => "/planning/" + Uri.EscapeDataString(id));
                Add(rows, "Refinement Sessions", o.RefinementSessionIds, id => "/backlog/" + Uri.EscapeDataString(o.Id) + "?refinementSessionId=" + Uri.EscapeDataString(id));
                Add(rows, "Voyages", o.VoyageIds, id => "/voyages/" + Uri.EscapeDataString(id));
                Add(rows, "Missions", o.MissionIds, id => "/missions/" + Uri.EscapeDataString(id));
                Add(rows, "Checks", o.CheckRunIds, id => "/checks/" + Uri.EscapeDataString(id));
                Add(rows, "Releases", o.ReleaseIds, id => "/releases/" + Uri.EscapeDataString(id));
                Add(rows, "Deployments", o.DeploymentIds, id => "/deployments/" + Uri.EscapeDataString(id));
                Add(rows, "Incidents", o.IncidentIds, id => "/incidents/" + Uri.EscapeDataString(id));
            }

            Links.SetLocalRows(rows);
        }

        private static void Add(List<BacklogLink> rows, string kind, List<string>? ids, Func<string, string> route)
        {
            foreach (string id in ids ?? new List<string>()) rows.Add(new BacklogLink(kind, id, id, route(id)));
        }

        private OpsDocument BuildOverview(OpsDocument doc)
        {
            Objective? o = Item;
            if (o == null) return doc;
            DateTime now = Context.Clock.UtcNow;
            if (!CanManage) doc.Note("You can view backlog items, but only tenant administrators can create or change them.", Theme.Warning);
            doc.Section("Overview");
            doc.Field("Rank", o.Rank.ToString(CultureInfo.InvariantCulture));
            doc.Field("Owner", String.IsNullOrEmpty(o.Owner) ? Tr("Unassigned") : o.Owner);
            doc.Field("Refinement Sessions", (o.RefinementSessionIds?.Count ?? 0).ToString(CultureInfo.InvariantCulture));
            doc.Field("Linked Releases", (o.ReleaseIds?.Count ?? 0).ToString(CultureInfo.InvariantCulture));
            doc.Field("Linked Incidents", (o.IncidentIds?.Count ?? 0).ToString(CultureInfo.InvariantCulture));
            doc.Time("Last Updated", o.LastUpdateUtc, now);
            doc.Blank();
            doc.Note("Refinement is lighter than planning: it uses a selected captain to sharpen the backlog item, but it does not imply repository mutation, dock provisioning, or dispatch by itself.", Theme.Info);
            string primary = PrimaryVesselId();
            if (primary.Length > 0)
                doc.Add(TUIKit.StyledText.From(Tr("Primary vessel {{vessel}} is linked, so planning, dispatch, and release drafting can start from this backlog item.", LocalizationArgs.Of("vessel", Reference.VesselName(primary))), Theme.Success));
            else
                doc.Note("This backlog item can be refined now, but it still needs a vessel before repository-aware planning or dispatch can start.", Theme.Warning);
            if (o.SourceProvider == Objective.GitHubSourceProvider)
            {
                doc.Section("GitHub Source", "  " + (String.IsNullOrEmpty(o.SourceType) ? Tr("Unknown source") : o.SourceType));
                doc.Field("Provider", o.SourceProvider);
                doc.Field("Source", o.SourceId);
                doc.Field("Last Source Update", o.SourceUpdatedUtc.HasValue ? Context.Loc.FormatDateTime(o.SourceUpdatedUtc.Value) : "-");
                doc.Field("Source Link", o.SourceUrl, Theme.Link);
            }

            return doc;
        }

        private OpsDocument BuildTranscriptHeader(OpsDocument doc)
        {
            ObjectiveRefinementSessionDetail? d = Detail;
            if (d == null)
            {
                doc.Note("No active refinement transcript selected.", Theme.Accent);
                doc.Note("Start a session with a selected captain or choose an existing transcript from the left.");
                return doc;
            }

            string captain = d.Captain?.Name ?? d.Session.CaptainId;
            string clause = d.Vessel != null ? Tr("with optional vessel context {{vessel}}", LocalizationArgs.Of("vessel", d.Vessel.Name)) : Tr("without vessel context");
            doc.Add(TUIKit.StyledText.From(d.Session.Title + "   " + StatusBadge.Label(d.Session.Status), Theme.Accent));
            doc.Add(TUIKit.StyledText.From(Tr("Captain {{captain}} {{vesselClause}}", LocalizationArgs.Of("captain", captain, "vesselClause", clause)), Theme.Muted));
            doc.Add(TUIKit.StyledText.From(Tr("Selected Captain") + ": " + captain + "   " + Tr("Captain State") + ": " + (d.Captain?.State.ToString() ?? "-")
                + "   " + Tr("Vessel Context") + ": " + (d.Vessel?.Name ?? Tr("None")) + "   " + Tr("Updated") + ": " + Context.Loc.FormatRelative(d.Session.LastUpdateUtc, Context.Clock.UtcNow), Theme.Muted));
            doc.Add(TUIKit.StyledText.From("Up/Down " + Tr("select message") + "   Ctrl+S " + Tr("Send") + "   m " + Tr("Summarize") + "   A " + Tr("Apply To Backlog Item") + "   x " + Tr("Stop Session") + "   X " + Tr("Delete Session"), Theme.Muted));
            return doc;
        }

        private OpsDocument BuildSummary(OpsDocument doc)
        {
            ObjectiveRefinementSummaryResponse? s = SummaryDraft;
            doc.Section("Refinement Summary Draft");
            if (s == null)
            {
                doc.Note("Generate a summary from a selected assistant message, then apply it back into the backlog item.");
                return doc;
            }

            doc.Field("Summary", s.Summary);
            Pipeline? p = Reference.Pipelines.FirstOrDefault(x => x.Id == s.SuggestedPipelineId);
            doc.Field("Suggested Pipeline", p?.Name ?? (String.IsNullOrEmpty(s.SuggestedPipelineId) ? "-" : s.SuggestedPipelineId));
            doc.Field("Method", s.Method);
            List(doc, "Acceptance Criteria", s.AcceptanceCriteria);
            List(doc, "Non-Goals", s.NonGoals);
            List(doc, "Rollout Constraints", s.RolloutConstraints);
            return doc;
        }

        private static void List(OpsDocument doc, string title, List<string>? items)
        {
            doc.Section(title);
            foreach (string item in items ?? new List<string>()) doc.Text("- " + item);
        }

        private void LoadSessions(string? preferred)
        {
            Objective? o = Item;
            if (o == null) return;
            Call((c, t) => c.ListBacklogRefinementSessionsAsync(o.Id, t), list =>
            {
                _SessionList = (list ?? new List<ObjectiveRefinementSession>()).ToList();
                Sessions.SetLocalRows(_SessionList);
                string selected = !String.IsNullOrEmpty(preferred) ? preferred!
                    : _SessionList.Any(s => s.Id == _SelectedSessionId) ? _SelectedSessionId
                    : _SessionList.FirstOrDefault()?.Id ?? "";
                _SelectedSessionId = selected;
                if (selected.Length > 0) SelectSession(selected);
                else ShowDetail(null);
            }, null, ex => { });
        }

        private void UpsertSession(ObjectiveRefinementSession session)
        {
            if (session == null) return;
            int idx = _SessionList.FindIndex(s => s.Id == session.Id);
            if (idx >= 0) _SessionList[idx] = session;
            else _SessionList.Insert(0, session);
            _SessionList = _SessionList.OrderByDescending(s => s.LastUpdateUtc).ToList();
            Sessions.SetLocalRows(_SessionList);
        }

        /// <summary>
        /// Fold a detail from a request response (send, stop, or a transcript reload) into what live events already
        /// delivered for the same session. The response can be built before a fast captain reply arrives over the
        /// WebSocket, so replacing the transcript with it would drop that reply; for each message and for the session,
        /// the copy with the later <c>LastUpdateUtc</c> wins.
        /// </summary>
        private ObjectiveRefinementSessionDetail MergeWithLive(ObjectiveRefinementSessionDetail incoming)
        {
            ObjectiveRefinementSessionDetail? current = Detail;
            if (current == null || current.Session.Id != incoming.Session.Id) return incoming;
            Dictionary<string, ObjectiveRefinementMessage> byId = new Dictionary<string, ObjectiveRefinementMessage>(StringComparer.Ordinal);
            foreach (ObjectiveRefinementMessage m in incoming.Messages) byId[m.Id] = m;
            foreach (ObjectiveRefinementMessage m in current.Messages)
            {
                if (!byId.TryGetValue(m.Id, out ObjectiveRefinementMessage? other) || m.LastUpdateUtc > other.LastUpdateUtc) byId[m.Id] = m;
            }

            incoming.Messages = byId.Values.OrderBy(m => m.Sequence).ToList();
            if (current.Session.LastUpdateUtc > incoming.Session.LastUpdateUtc) incoming.Session = current.Session;
            return incoming;
        }

        private void ShowDetail(ObjectiveRefinementSessionDetail? detail)
        {
            Detail = detail;
            if (detail != null) _SelectedSessionId = detail.Session.Id;
            Transcript.SetMessages(detail?.Messages);
            Transcript.Visible = detail != null;
            Composer.Visible = detail != null && CanManage;
            _TranscriptButtons.Visible = detail != null;
            if (detail != null)
            {
                bool exists = Transcript.Messages.Any(m => m.Id == Transcript.SelectedId);
                ObjectiveRefinementMessage? latest = Transcript.Messages.LastOrDefault(m => String.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase) && m.Content.Trim().Length > 0);
                if (!exists) Transcript.Select(latest?.Id ?? "");
                _FailureLine.Translate = false;
                _FailureLine.Text = detail.Session.FailureReason ?? "";
            }
            else
            {
                Transcript.Select("");
                _FailureLine.Text = "";
            }

            SyncTranscriptHelper();
            _TranscriptHeader.Invalidate();
        }

        private void SyncTranscriptHelper()
        {
            _TranscriptHelper.Text = Transcript.SelectedId.Length == 0 ? "Select a transcript message before summarizing or applying refinement back to the backlog item." : "";
        }

        private void SetSummary(ObjectiveRefinementSummaryResponse summary)
        {
            SummaryDraft = summary;
            if (!String.IsNullOrEmpty(summary.MessageId)) Transcript.Select(summary.MessageId);
            _SummaryView.Invalidate();
        }

        private void OnObjectiveChanged(ArmadaSocketMessage message)
        {
            Objective? payload = message.GetData<Objective>();
            if (payload == null || Item == null || payload.Id != Item.Id) return;
            if (!Form.IsDirty) Hydrate(payload);
            else
            {
                Item = payload;
                _Overview.Invalidate();
                RebuildLinks();
            }
        }

        private void OnCaptainChanged(ArmadaSocketMessage message)
        {
            EntityChangedEvent? payload = message.GetData<EntityChangedEvent>();
            if (payload == null || String.IsNullOrEmpty(payload.Id) || String.IsNullOrEmpty(payload.State)) return;
            CaptainStateEnum? typed = payload.CaptainState;
            if (typed == null) return;
            CaptainStateEnum state = typed.Value;
            foreach (Captain c in Reference.Captains.Where(c => c.Id == payload.Id))
            {
                c.State = state;
                if (!String.IsNullOrEmpty(payload.Name)) c.Name = payload.Name!;
            }

            if (Detail?.Captain != null && Detail.Captain.Id == payload.Id)
            {
                Detail.Captain.State = state;
                if (!String.IsNullOrEmpty(payload.Name)) Detail.Captain.Name = payload.Name!;
                _TranscriptHeader.Invalidate();
            }

            RebuildCaptainOptions();
        }

        private void OnRefinementEvent(ArmadaSocketMessage message)
        {
            Objective? o = Item;
            if (o == null) return;
            switch (message.Type)
            {
                case ArmadaEventTypes.RefinementSessionChanged:
                {
                    RefinementSessionEvent? e = message.GetData<RefinementSessionEvent>();
                    if (e?.Session == null || e.Session.ObjectiveId != o.Id) return;
                    UpsertSession(e.Session);
                    if (Detail != null && Detail.Session.Id == e.Session.Id)
                    {
                        Detail.Session = e.Session;
                        _FailureLine.Text = e.Session.FailureReason ?? "";
                        _TranscriptHeader.Invalidate();
                    }

                    return;
                }

                case ArmadaEventTypes.RefinementSessionMessageCreated:
                case ArmadaEventTypes.RefinementSessionMessageUpdated:
                {
                    RefinementSessionEvent? e = message.GetData<RefinementSessionEvent>();
                    if (e == null || String.IsNullOrEmpty(e.SessionId) || e.ObjectiveId != o.Id || e.Message == null) return;
                    if (Detail == null || Detail.Session.Id != e.SessionId) return;
                    int idx = Detail.Messages.FindIndex(m => m.Id == e.Message.Id);
                    if (idx >= 0) Detail.Messages[idx] = e.Message;
                    else Detail.Messages.Add(e.Message);
                    Detail.Messages = Detail.Messages.OrderBy(m => m.Sequence).ToList();
                    Transcript.SetMessages(Detail.Messages);
                    if (Transcript.SelectedId.Length == 0) ShowDetail(Detail);
                    return;
                }

                case ArmadaEventTypes.RefinementSessionSummaryCreated:
                {
                    ObjectiveRefinementSummaryResponse? s = message.GetData<ObjectiveRefinementSummaryResponse>();
                    if (s == null || s.SessionId != _SelectedSessionId) return;
                    SetSummary(s);
                    return;
                }

                case ArmadaEventTypes.RefinementSessionApplied:
                {
                    ObjectiveRefinementApplyResponse? a = message.GetData<ObjectiveRefinementApplyResponse>();
                    if (a?.Objective == null || a.Objective.Id != o.Id) return;
                    Hydrate(a.Objective);
                    if (a.Summary != null) SetSummary(a.Summary);
                    return;
                }

                case ArmadaEventTypes.RefinementSessionDeleted:
                {
                    RefinementSessionEvent? e = message.GetData<RefinementSessionEvent>();
                    if (e == null || String.IsNullOrEmpty(e.SessionId) || e.ObjectiveId != o.Id) return;
                    _SessionList = _SessionList.Where(s => s.Id != e.SessionId).ToList();
                    Sessions.SetLocalRows(_SessionList);
                    if (_SelectedSessionId == e.SessionId)
                    {
                        _SelectedSessionId = "";
                        ShowDetail(null);
                        SummaryDraft = null;
                        _SummaryView.Invalidate();
                    }

                    return;
                }
            }
        }

        private void RefreshGitHub()
        {
            Objective? o = Item;
            int? number = GitHubNumber();
            string vessel = PrimaryVesselId();
            if (o == null || !number.HasValue || vessel.Length == 0) return;
            GitHubObjectiveImportRequest req = new GitHubObjectiveImportRequest();
            req.ObjectiveId = o.Id;
            req.VesselId = vessel;
            req.SourceType = EnumNames.ParseOrNull<GitHubObjectiveSourceTypeEnum>(o.SourceType) ?? GitHubObjectiveSourceTypeEnum.Issue;
            req.Number = number.Value;
            Call((c, t) => c.ImportObjectiveFromGitHubAsync(req, t), refreshed =>
            {
                if (refreshed == null) return;
                Hydrate(refreshed);
                Toast(NotificationSeverityEnum.Success, Tr("Backlog item \"{{title}}\" refreshed from GitHub.", LocalizationArgs.Of("title", refreshed.Title)));
            }, null, ex => ShowMessage(String.IsNullOrEmpty(ex.Message) ? Tr("GitHub refresh failed.") : ex.Message));
        }

        private void StartPlanning()
        {
            Objective? o = Item;
            if (o == null) return;
            string fleet = PrimaryFleetId();
            Context.Navigate(OpsHandoff.Planning(OpsHandoff.FromObjective, null, o.Id, PrimaryVesselId(), fleet.Length > 0 ? fleet : null, o.SuggestedPipelineId, o.Title + " Planning", BacklogLogic.PlanningPrompt(o)));
        }

        private void OpenInDispatch()
        {
            Objective? o = Item;
            if (o == null) return;
            string? pipelineName = Reference.Pipelines.FirstOrDefault(p => p.Id == o.SuggestedPipelineId)?.Name;
            Context.Navigate(OpsHandoff.Dispatch(OpsHandoff.FromObjective, PrimaryVesselId(), pipelineName, BacklogLogic.DispatchPrompt(o), o.Title, o.Id, o.SuggestedPlaybooks));
        }

        private void DraftRelease()
        {
            Objective? o = Item;
            if (o == null) return;
            Dictionary<string, string> q = new Dictionary<string, string>(StringComparer.Ordinal);
            q["prefill"] = "1";
            q["objectiveIds"] = o.Id;
            q["vesselId"] = PrimaryVesselId();
            q["title"] = o.Title + " Release";
            if (!String.IsNullOrEmpty(o.Description)) q["summary"] = o.Description!;
            q["notes"] = BacklogLogic.ReleaseNotes(o);
            q["status"] = "Draft";
            Context.Navigate("/releases/new" + RouteMatch.BuildQuery(q));
        }

        private void Duplicate()
        {
            Objective? o = Item;
            if (o == null || !CanManage) return;
            ObjectiveUpsertRequest payload = BacklogLogic.DuplicatePayload(o);
            Call((c, t) => c.CreateBacklogItemAsync(payload, t), created =>
            {
                if (created == null) return;
                Toast(NotificationSeverityEnum.Success, Tr("Backlog item \"{{title}}\" duplicated.", LocalizationArgs.Of("title", created.Title)));
                Context.Navigate("/backlog/" + Uri.EscapeDataString(created.Id));
            }, null, ex => ShowMessage(String.IsNullOrEmpty(ex.Message) ? Tr("Duplicate failed.") : ex.Message));
        }

        private void Delete()
        {
            Objective? o = Item;
            if (o == null || !CanManage) return;
            Confirm("Delete Backlog Item", Tr("Delete \"{{title}}\"? This removes the backlog item and its objective snapshot history only.", LocalizationArgs.Of("title", o.Title)), () =>
            {
                Run((c, t) => c.DeleteBacklogItemAsync(o.Id, t), () =>
                {
                    Toast(NotificationSeverityEnum.Warning, Tr("Backlog item \"{{title}}\" deleted.", LocalizationArgs.Of("title", o.Title)));
                    Form.MarkClean();
                    Context.Navigate("/backlog");
                }, null, ex => ShowMessage(String.IsNullOrEmpty(ex.Message) ? Tr("Delete failed.") : ex.Message));
            }, "Delete");
        }

        #endregion
    }
}

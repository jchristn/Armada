namespace Armada.Tui.Screens.Operations
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using Armada.Client.Models;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Input;
    using Armada.Tui.Routing;
    using Armada.Tui.Services;
    using Armada.Tui.Text;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Widgets;

    /// <summary>
    /// Dispatch (W3.5, the Dispatch tab of <c>/dispatch</c>), the dashboard's Dispatch page: vessel (required),
    /// pipeline or inherit, priority (default 100), voyage title, description (<c>Ctrl+E</c> opens <c>$EDITOR</c>),
    /// playbooks with delivery modes, captain assignments per pipeline step (preferred captain and fallback tier,
    /// seeded from each persona's default captain; a single "All steps" row when the pipeline is inherited), the
    /// compact Vessel Readiness panel (<c>Alt+R</c> for the full panel), and the pre-fill banner for handoffs from
    /// Planning, Workspace, an incident, or a backlog item (<see cref="OpsHandoff"/>). <c>Ctrl+S</c> dispatches the
    /// voyage, toasts the dashboard's message, and opens the voyage 1.5 s later. Not thread-safe.
    /// </summary>
    public class DispatchScreen : OpsScreen
    {
        #region Public-Members

        /// <summary>
        /// The current form (rebuilt when the pipeline's steps change).
        /// </summary>
        public FormView Form { get; private set; } = new FormView();

        /// <summary>
        /// Vessel.
        /// </summary>
        public SelectField<string> Vessel { get; }

        /// <summary>
        /// Pipeline (value: pipeline name; "" inherits).
        /// </summary>
        public SelectField<string> Pipeline { get; }

        /// <summary>
        /// Priority.
        /// </summary>
        public InputField Priority { get; } = new InputField();

        /// <summary>
        /// Voyage title override.
        /// </summary>
        public InputField VoyageTitle { get; } = new InputField();

        /// <summary>
        /// Description (the prompt).
        /// </summary>
        public OpsTextArea Description { get; } = new OpsTextArea();

        /// <summary>
        /// Playbooks.
        /// </summary>
        public PlaybookSelectionField Playbooks { get; } = new PlaybookSelectionField();

        /// <summary>
        /// Captain picker per step persona ("*" for all steps).
        /// </summary>
        public Dictionary<string, SelectField<string>> StepCaptains { get; } = new Dictionary<string, SelectField<string>>(StringComparer.Ordinal);

        /// <summary>
        /// Fallback tier picker per step persona.
        /// </summary>
        public Dictionary<string, SelectField<string>> StepTiers { get; } = new Dictionary<string, SelectField<string>>(StringComparer.Ordinal);

        /// <summary>
        /// Backlog item carried from a handoff, or null.
        /// </summary>
        public string? ObjectiveId { get; private set; } = null;

        /// <summary>
        /// Handoff source (planning, workspace, incident, objective), or null.
        /// </summary>
        public string? From { get; }

        /// <summary>
        /// Result line (translated), or null.
        /// </summary>
        public string? Result { get; private set; } = null;

        /// <summary>
        /// True when <see cref="Result"/> reports success.
        /// </summary>
        public bool ResultOk { get; private set; } = false;

        /// <summary>
        /// True while dispatching.
        /// </summary>
        public bool Dispatching { get; private set; } = false;

        /// <summary>
        /// Readiness of the selected vessel, or null.
        /// </summary>
        public VesselReadinessResult? Readiness { get; private set; } = null;

        /// <summary>
        /// True while readiness loads.
        /// </summary>
        public bool LoadingReadiness { get; private set; } = false;

        /// <summary>
        /// Voyage created by the last dispatch, or null.
        /// </summary>
        public string? LastVoyageId { get; private set; } = null;

        /// <inheritdoc />
        public override bool CanFocus { get; set; } = true;

        /// <inheritdoc />
        public override IReadOnlyList<KeyValuePair<string, string>> TypingHints
        {
            get { return Hints; }
        }

        /// <inheritdoc />
        public override IReadOnlyList<KeyValuePair<string, string>> Hints
        {
            get
            {
                List<KeyValuePair<string, string>> hints = new List<KeyValuePair<string, string>>();
                hints.Add(new KeyValuePair<string, string>("Ctrl+S", "Dispatch"));
                hints.Add(new KeyValuePair<string, string>("Ctrl+E", "Editor"));
                if (!String.IsNullOrEmpty(Vessel.Value)) hints.Add(new KeyValuePair<string, string>("Alt+R", "Vessel Readiness"));
                if (ObjectiveId != null) hints.Add(new KeyValuePair<string, string>("Alt+B", "Open Backlog Item"));
                return hints;
            }
        }

        #endregion

        #region Private-Members

        private readonly Dictionary<string, string?> _AssignedCaptain = new Dictionary<string, string?>(StringComparer.Ordinal);
        private readonly Dictionary<string, string?> _AssignedTier = new Dictionary<string, string?>(StringComparer.Ordinal);
        private readonly HashSet<string> _TouchedCaptain = new HashSet<string>(StringComparer.Ordinal);
        private string? _PendingVessel = null;
        private string? _PendingPipeline = null;
        private int _ReadinessGeneration = 0;
        private Timer? _NavigateTimer = null;
        private List<string> _Steps = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Services.</param>
        public DispatchScreen(RouteMatch route, TuiContext context)
            : base(route, context, "DispatchScreen", "Dispatch")
        {
            Vessel = NewSelect("Vessel", new List<SelectOption<string>>(), "Select a vessel...");
            Pipeline = NewSelect("Pipeline", PipelineOptions(), null);
            Pipeline.SetValue("");
            Priority.Value = "100";
            Priority.Validator = v => Int32.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int p) && p >= 0 && p <= 1000 ? null : "Enter a number from 0 to 1000.";
            VoyageTitle.Placeholder = "Optional override for the voyage title";
            Description.Placeholder = PromptPlaceholder();
            Description.ExternalEditor = (text, done) => EditExternally(text, done);
            Description.Validator = v => String.IsNullOrWhiteSpace(v) ? "Describe what you need done." : null;
            Playbooks.ModalHost = context.Modals;

            From = OpsHandoff.Get(route, "from");
            if (From != null)
            {
                _PendingVessel = OpsHandoff.Get(route, "vesselId");
                _PendingPipeline = OpsHandoff.Get(route, "pipelineName");
                string? prompt = OpsHandoff.Get(route, "prompt");
                if (prompt != null) Description.Text = prompt;
                string? title = OpsHandoff.Get(route, "voyageTitle");
                if (title != null) VoyageTitle.Value = title;
                ObjectiveId = OpsHandoff.Get(route, "objectiveId");
                List<SelectedPlaybook> playbooks = OpsHandoff.Playbooks(route);
                if (playbooks.Count > 0) Playbooks.SetValue(playbooks);
            }

            Vessel.ValueChanged += (s, e) =>
            {
                _PendingVessel = null;
                LoadReadiness();
            };
            Pipeline.ValueChanged += (s, e) =>
            {
                _PendingPipeline = null;
                RebuildForm();
            };
            Reference.Changed += (s, name) => ReferenceArrived(name);
            Reference.Ensure("vessels", "pipelines", "captains", "personas", "playbooks");
            RebuildForm();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The distinct personas of the selected pipeline in stage order, or "*" when inherited (the dashboard's
        /// effectivePersonas).
        /// </summary>
        /// <returns>Step personas.</returns>
        public List<string> EffectivePersonas()
        {
            Pipeline? p = SelectedPipeline();
            List<string> steps = p == null ? new List<string>() : p.Stages.OrderBy(s => s.Order).Select(s => s.PersonaName).Distinct(StringComparer.Ordinal).ToList();
            return steps.Count > 0 ? steps : new List<string> { "*" };
        }

        /// <summary>
        /// Dispatch the voyage (the dashboard's handleDispatch).
        /// </summary>
        public void Dispatch()
        {
            if (Dispatching) return;
            string prompt = Description.Text.Trim();
            if (prompt.Length == 0)
            {
                Description.ValidateField();
                return;
            }

            string vesselId = Vessel.Value ?? "";
            if (vesselId.Length == 0)
            {
                Result = Tr("Please select a vessel.");
                ResultOk = false;
                return;
            }

            if (!Form.ValidateAll()) return;
            Pipeline? pipeline = SelectedPipeline();
            bool multiStage = pipeline != null && pipeline.Stages.Count > 1;
            int priority = Int32.TryParse(Priority.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int pr) ? pr : 100;
            List<CaptainAssignmentOverride> assignments = new List<CaptainAssignmentOverride>();
            foreach (string persona in _Steps)
            {
                string? captain = StepCaptains.TryGetValue(persona, out SelectField<string>? c) ? c.Value : null;
                string? tier = StepTiers.TryGetValue(persona, out SelectField<string>? t) ? t.Value : null;
                if (String.IsNullOrEmpty(captain) && String.IsNullOrEmpty(tier)) continue;
                CaptainAssignmentOverride o = new CaptainAssignmentOverride();
                o.Persona = persona;
                o.CaptainId = String.IsNullOrEmpty(captain) ? null : captain;
                o.FallbackTier = Enum.TryParse(tier, out CaptainTierEnum parsed) ? parsed : (CaptainTierEnum?)null;
                assignments.Add(o);
            }

            VoyageCreateRequest req = new VoyageCreateRequest();
            req.Title = VoyageTitle.Value.Trim().Length > 0 ? VoyageTitle.Value.Trim() : Truncate(prompt, 80);
            req.VesselId = vesselId;
            DispatchRequest mission = new DispatchRequest();
            mission.VesselId = vesselId;
            mission.Title = Truncate(prompt, 80);
            mission.Description = prompt;
            mission.Priority = priority;
            req.Missions = new List<DispatchRequest> { mission };
            if (!String.IsNullOrEmpty(ObjectiveId)) req.ObjectiveId = ObjectiveId;
            if (!String.IsNullOrEmpty(Pipeline.Value)) req.Pipeline = Pipeline.Value;
            if (Playbooks.Value.Count > 0) req.SelectedPlaybooks = Playbooks.Value.ToList();
            if (assignments.Count > 0) req.CaptainAssignments = assignments;
            string missionCount = multiStage
                ? Tr("{{count}} pipeline stages", LocalizationArgs.Of("count", pipeline!.Stages.Count))
                : Tr("{{count}} mission(s)", LocalizationArgs.Of("count", 1));

            Dispatching = true;
            Result = null;
            Playbooks.Disabled = true;
            Call((c, t) => c.CreateVoyageAsync(req, t), voyage =>
            {
                Dispatching = false;
                Playbooks.Disabled = false;
                string message = Tr("Dispatched voyage with {{missionCount}}", LocalizationArgs.Of("missionCount", missionCount));
                Result = message;
                ResultOk = true;
                Toast(NotificationSeverityEnum.Success, message);
                VoyageTitle.Value = "";
                Description.Text = "";
                LastVoyageId = voyage?.Id;
                if (voyage != null && !String.IsNullOrEmpty(voyage.Id))
                {
                    string target = "/voyages/" + Uri.EscapeDataString(voyage.Id);
                    _NavigateTimer?.Dispose();
                    _NavigateTimer = new Timer(_ => Post(() => Context.Navigate(target)), null, 1500, Timeout.Infinite);
                    Track(_NavigateTimer);
                }
            }, null, ex =>
            {
                Dispatching = false;
                Playbooks.Disabled = false;
                Result = Tr("Failed: {{message}}", LocalizationArgs.Of("message", String.IsNullOrEmpty(ex.Message) ? Tr("Unknown error") : ex.Message));
                ResultOk = false;
            });
        }

        /// <inheritdoc />
        public override Action? RefreshAction()
        {
            return () =>
            {
                Reference.Ensure(true, "vessels", "pipelines", "captains", "personas", "playbooks");
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
            list.Add(Cmd("dispatch", "Dispatch", Dispatch, "ctrl+s"));
            ArmadaCommand readiness = Cmd("readiness", "Vessel Readiness", () => OpsReadiness.ShowDetails(this, Readiness, LoadingReadiness), "alt+r");
            readiness.IsEnabled = () => !String.IsNullOrEmpty(Vessel.Value);
            list.Add(readiness);
            ArmadaCommand backlog = Cmd("open-backlog", "Open Backlog Item", OpenBacklogItem, "alt+b");
            backlog.IsEnabled = () => ObjectiveId != null;
            list.Add(backlog);
            list.Add(Cmd("edit-description", "Edit description in $EDITOR", () => Description.EditExternally(), "ctrl+e"));
            list.Add(Cmd("manage-pipelines", "Manage pipelines", () => Context.Navigate("/configuration?tab=pipelines"), null));
            list.Add(Cmd("manage-personas", "Manage persona defaults", () => Context.Navigate("/configuration?tab=personas"), null));
            list.Add(Cmd("manage-playbooks", "Manage playbooks", () => Context.Navigate("/configuration?tab=playbooks"), null));
            return list;
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (key.Code == KeyCode.Character && (key.Modifiers & KeyModifiers.Alt) != 0)
            {
                char c = Char.ToLowerInvariant((char)key.Rune);
                if (c == 'r' && !String.IsNullOrEmpty(Vessel.Value))
                {
                    OpsReadiness.ShowDetails(this, Readiness, LoadingReadiness);
                    return true;
                }

                if (c == 'b' && ObjectiveId != null)
                {
                    OpenBacklogItem();
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
            if (width < 20 || height < 6) return;
            int y = 0;
            int x = SurfaceText.Draw(surface, 0, y, Tr("Dispatch"), Theme.Accent.WithAttribute(CellAttributes.Bold, true), width);
            SurfaceText.Draw(surface, x + 2, y, Tr("Describe the work you want Armada to dispatch through the selected vessel and pipeline."), Theme.Muted, width - x - 2);
            y++;
            string? banner = Banner();
            if (banner != null)
            {
                string text = banner + (ObjectiveId != null ? "  [Alt+B " + Tr("Open Backlog Item") + "]" : "");
                foreach (string line in TextCells.Wrap(text, width))
                {
                    if (y >= height - 4) break;
                    SurfaceText.Draw(surface, 0, y++, line, Theme.Info, width);
                }
            }

            if (Result != null && y < height - 4)
            {
                SurfaceText.Draw(surface, 0, y++, (ResultOk ? "+ " : "! ") + Result, ResultOk ? Theme.Success : Theme.Error, width);
            }

            if (!String.IsNullOrEmpty(Vessel.Value))
            {
                OpsDocument doc = new OpsDocument(Theme, Localizer);
                OpsReadiness.Build(doc, "Vessel Readiness", Readiness, LoadingReadiness, "Select a vessel to inspect readiness.", true);
                List<StyledText> lines = doc.Lines.Take(4).ToList();
                y += OpsDraw.Lines(surface, 0, y, lines, width, Math.Max(0, Math.Min(4, height - y - 4)), Theme.Text);
                if (doc.Lines.Count > 4 && y < height - 4) SurfaceText.Draw(surface, 0, y++, "... Alt+R " + Tr("Vessel Readiness"), Theme.Muted, width);
            }

            Form.SaveButton.Label = Dispatching ? "Dispatching..." : "Dispatch";
            Form.SaveButton.Enabled = !Dispatching;
            Form.MarkClean();
            if (y < height) Scope.RenderChild(surface, Form, new Rect(0, y, width, height - y));
        }

        #endregion

        #region Private-Methods

        private ArmadaCommand Cmd(string id, string title, Action handler, string? key)
        {
            ArmadaCommand c = key != null
                ? new ArmadaCommand(ScreenKey + "." + id, title, CommandMenuEnum.Actions, handler, key)
                : new ArmadaCommand(ScreenKey + "." + id, title, CommandMenuEnum.Actions, handler);
            c.Group = Title;
            c.Dispatch = false;
            return c;
        }

        private string PromptPlaceholder()
        {
            return Tr("Describe what you need done.\n\nArmada will dispatch this request as a voyage on the selected vessel.").Replace("\n\n", " ").Replace('\n', ' ');
        }

        private string? Banner()
        {
            switch (From)
            {
                case OpsHandoff.FromObjective: return Tr("Prefilled from a backlog item. Review the scoped draft below and dispatch when ready.");
                case OpsHandoff.FromPlanning: return Tr("Prefilled from a planning session. Review the draft below and dispatch when ready.");
                case OpsHandoff.FromIncident: return Tr("Prefilled from an incident. Review the hotfix draft below and dispatch when ready.");
                case OpsHandoff.FromWorkspace: return Tr("Prefilled from Workspace selection. Review the scoped draft below and dispatch when ready.");
                default: return null;
            }
        }

        private void OpenBacklogItem()
        {
            if (ObjectiveId != null) Context.Navigate("/backlog/" + Uri.EscapeDataString(ObjectiveId));
        }

        private Pipeline? SelectedPipeline()
        {
            string name = Pipeline.Value ?? "";
            if (name.Length == 0) return null;
            return Reference.Pipelines.FirstOrDefault(p => p.Name == name);
        }

        private List<SelectOption<string>> PipelineOptions()
        {
            List<SelectOption<string>> options = new List<SelectOption<string>> { new SelectOption<string>("", Tr("Inherit (vessel, then fleet, then WorkerOnly)")) };
            options.AddRange(Reference.Pipelines.Select(p => new SelectOption<string>(p.Name, p.Name + " (" + String.Join(" -> ", p.Stages.OrderBy(s => s.Order).Select(s => s.PersonaName)) + ")")));
            return options;
        }

        private List<SelectOption<string>> CaptainOptions()
        {
            List<SelectOption<string>> options = new List<SelectOption<string>> { new SelectOption<string>("", Tr("Auto (default routing)")) };
            foreach (Captain c in Reference.Captains)
            {
                string label = c.Name + (c.Tier.HasValue ? " - " + Tr(c.Tier.Value.ToString()) : "") + " (" + c.Runtime + ")";
                options.Add(new SelectOption<string>(c.Id, label));
            }

            return options;
        }

        private List<SelectOption<string>> TierOptions()
        {
            return new List<SelectOption<string>>
            {
                new SelectOption<string>("", Tr("Auto")),
                new SelectOption<string>("Economy", Tr("Economy")),
                new SelectOption<string>("Standard", Tr("Standard")),
                new SelectOption<string>("Premium", Tr("Premium")),
            };
        }

        private static void Reset(SelectField<string> select, List<SelectOption<string>> options, string? preferred)
        {
            string? keep = preferred ?? select.Value;
            select.Options = options;
            select.SetValue(keep ?? "");
            if (select.Selected == null && options.Count > 0 && options[0].Value == "") select.SetValue("");
        }

        private void ReferenceArrived(string name)
        {
            if (name == "vessels")
            {
                string? before = Vessel.Value;
                Reset(Vessel, Reference.VesselOptions(), _PendingVessel);
                if (Vessel.Value != before) LoadReadiness();
            }
            else if (name == "pipelines")
            {
                Reset(Pipeline, PipelineOptions(), _PendingPipeline);
                RebuildForm();
            }
            else if (name == "captains")
            {
                RebuildForm();
            }
            else if (name == "personas")
            {
                RebuildForm();
            }
            else if (name == "playbooks")
            {
                Playbooks.Available = Reference.Playbooks;
                Playbooks.Loaded = true;
            }
        }

        private void RebuildForm()
        {
            foreach (KeyValuePair<string, SelectField<string>> kvp in StepCaptains) _AssignedCaptain[kvp.Key] = kvp.Value.Value;
            foreach (KeyValuePair<string, SelectField<string>> kvp in StepTiers) _AssignedTier[kvp.Key] = kvp.Value.Value;
            List<string> steps = EffectivePersonas();
            IWidget? focused = Form.Scope.Focused;
            bool active = Scope.IsActive;
            FormView form = new FormView();
            form.Localizer = Localizer;
            form.ApplyTheme(Theme);
            form.SaveButton.Label = "Dispatch";
            form.SaveButton.Hint = "Ctrl+S";
            form.DiscardButton.Visible = false;
            form.SaveRequested += (s, e) => Dispatch();
            form.AddField("Vessel", Vessel);
            form.AddField("Pipeline", Pipeline, "Manage pipelines: Configuration, Pipelines");
            form.AddField("Priority", Priority, "Higher priority missions are assigned first (default 100)");
            form.AddField("Voyage Title", VoyageTitle);
            form.AddField("Description", Description, null, 10);
            form.AddField("Playbooks", Playbooks, "Enter to add, remove, or change delivery modes");
            form.AddSection("Captain Assignments");
            StepCaptains.Clear();
            StepTiers.Clear();
            bool specific = !(steps.Count == 1 && steps[0] == "*");
            for (int i = 0; i < steps.Count; i++)
            {
                string persona = steps[i];
                string label = persona == "*" ? Tr("All steps") : persona;
                SelectField<string> captain = NewSelect(Tr("Preferred captain for {{persona}}", LocalizationArgs.Of("persona", persona)), CaptainOptions(), null);
                string? captainValue = _TouchedCaptain.Contains(persona) && _AssignedCaptain.TryGetValue(persona, out string? cv) ? cv : Reference.Personas.FirstOrDefault(p => p.Name == persona)?.DefaultCaptainId;
                captain.SetValue(captainValue ?? "");
                if (captain.Selected == null) captain.SetValue("");
                SelectField<string> tier = NewSelect(Tr("Fallback tier for {{persona}}", LocalizationArgs.Of("persona", persona)), TierOptions(), null);
                tier.SetValue(_AssignedTier.TryGetValue(persona, out string? tv) ? tv ?? "" : "");
                if (tier.Selected == null) tier.SetValue("");
                string touched = persona;
                captain.ValueChanged += (s2, e2) => _TouchedCaptain.Add(touched);
                StepCaptains[persona] = captain;
                StepTiers[persona] = tier;
                string? hint = i == 0
                    ? (specific
                        ? "Pick a preferred captain per pipeline step. When it is busy, Armada falls back to an idle captain at or above the fallback tier. Defaults come from each persona."
                        : "No specific pipeline selected. This captain applies to every step of the mission; leave blank to let Armada auto-assign an idle captain.")
                    : null;
                form.AddField(label + ": " + Tr("Preferred Captain"), captain, hint);
                form.AddField(label + ": " + Tr("Fallback Tier"), tier);
            }

            _Steps = steps;
            if (Scope.Children.Contains(Form)) Scope.Remove(Form);
            Form = form;
            AddChild(Form);
            if (focused != null) Form.Scope.Focus(focused);
            Scope.Focus(Form);
            if (active) Form.OnFocusChanged(true);
        }

        private void LoadReadiness()
        {
            string? vesselId = Vessel.Value;
            int generation = ++_ReadinessGeneration;
            if (String.IsNullOrEmpty(vesselId))
            {
                Readiness = null;
                LoadingReadiness = false;
                return;
            }

            LoadingReadiness = true;
            Call((c, t) => c.GetVesselReadinessAsync(vesselId!, null, t), r =>
            {
                if (generation != _ReadinessGeneration) return;
                Readiness = r;
                LoadingReadiness = false;
            }, null, ex =>
            {
                if (generation != _ReadinessGeneration) return;
                Readiness = null;
                LoadingReadiness = false;
            });
        }

        private static string Truncate(string text, int length)
        {
            return text.Length <= length ? text : text.Substring(0, length);
        }

        #endregion
    }
}

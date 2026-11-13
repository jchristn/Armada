namespace Armada.Tui.Screens.Operations
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using Armada.Client.Models;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// The dashboard's run flow: the vessel picker (unless vessels are given), then the RunActionModal (a saved action
    /// or an ad hoc definition, the clean-tree override, concurrency, and a rendered preview for the first vessel),
    /// then the inputs step when the saved action declares runtime inputs (string, semver, enum, and package values,
    /// validated with the server's rules), then the review step ("Run on N vessels"), and finally the run detail page
    /// (or a caller callback). A follow-up run (<see cref="StartFollowUp"/>) skips the vessel picker: its targets are
    /// an earlier run's vessels with the chosen target statuses (Findings and Failed by default), and the actions
    /// linked to that run's action (a fix action whose ScanSourceActionKey names the scan) are listed first. A failed
    /// start returns to the configure step with the server's message. Use on the UI loop.
    /// </summary>
    public class FleetActionRunFlow
    {
        #region Public-Members

        /// <summary>
        /// Target vessel ids.
        /// </summary>
        public List<string> VesselIds { get; private set; } = new List<string>();

        /// <summary>
        /// Saved actions offered.
        /// </summary>
        public List<FleetAction> Actions { get; private set; } = new List<FleetAction>();

        /// <summary>
        /// Source: Saved action or Ad hoc.
        /// </summary>
        public SelectField<string> Mode { get; }

        /// <summary>
        /// Saved action.
        /// </summary>
        public SelectField<string> Action { get; }

        /// <summary>
        /// Clean-tree check (saved Command actions; an override when it differs from the action).
        /// </summary>
        public OpsCheckField SavedClean { get; } = new OpsCheckField("Skip vessels with uncommitted changes (clean-tree check)", true);

        /// <summary>
        /// Ad hoc name.
        /// </summary>
        public InputField Name { get; } = new InputField();

        /// <summary>
        /// Ad hoc kind.
        /// </summary>
        public SelectField<string> Kind { get; }

        /// <summary>
        /// Ad hoc command text.
        /// </summary>
        public OpsTextArea Command { get; } = new OpsTextArea();

        /// <summary>
        /// Ad hoc prompt template.
        /// </summary>
        public OpsTextArea Prompt { get; } = new OpsTextArea();

        /// <summary>
        /// Ad hoc pipeline.
        /// </summary>
        public SelectField<string> Pipeline { get; }

        /// <summary>
        /// Ad hoc timeout.
        /// </summary>
        public InputField Timeout { get; } = new InputField();

        /// <summary>
        /// Ad hoc clean-tree check.
        /// </summary>
        public OpsCheckField AdHocClean { get; } = new OpsCheckField("Skip vessels with uncommitted changes (clean-tree check)", true);

        /// <summary>
        /// Concurrency.
        /// </summary>
        public InputField Concurrency { get; } = new InputField();

        /// <summary>
        /// Follow-up target statuses (follow-up runs only).
        /// </summary>
        public MultiSelectField<FleetActionTargetStatusEnum> FromStatuses { get; } = new MultiSelectField<FleetActionTargetStatusEnum>();

        /// <summary>
        /// The earlier run a follow-up run selects its targets from, or null for a normal run.
        /// </summary>
        public FleetActionRun? FromRun { get; }

        /// <summary>
        /// True for a follow-up run (targets come from <see cref="FromRun"/>).
        /// </summary>
        public bool IsFollowUp
        {
            get { return FromRun != null; }
        }

        /// <summary>
        /// Runtime input values for the selected saved action, by input name.
        /// </summary>
        public Dictionary<string, string> InputValues { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Preview vessel (the first target), or null.
        /// </summary>
        public Vessel? PreviewVessel { get; private set; } = null;

        /// <summary>
        /// The dialog currently shown, or null.
        /// </summary>
        public OpsFormDialog? Dialog { get; private set; } = null;

        #endregion

        #region Private-Members

        private readonly OpsScreen _Screen;
        private readonly Action<FleetActionRunStartResult>? _OnStarted;
        private readonly string? _InitialActionId;
        private readonly TextBlock _Definition = new TextBlock();
        private readonly TextBlock _Preview = new TextBlock();
        private readonly Button _Variables;
        private bool? _CleanOverride = null;
        private bool _ActionsLoading = true;
        private string? _ActionsError = null;
        private string? _ServerError = null;
        private string? _InputsActionId = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="screen">Owning screen.</param>
        /// <param name="initialActionId">Saved action to preselect, or null.</param>
        /// <param name="initialDefinition">Ad hoc definition to start from (re-running an ad hoc run), or null.</param>
        /// <param name="onStarted">Runs after the server accepted the run; null navigates to the run.</param>
        /// <param name="fromRun">Earlier run for a follow-up run, or null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="screen"/> is null.</exception>
        public FleetActionRunFlow(OpsScreen screen, string? initialActionId, FleetActionUpsertRequest? initialDefinition, Action<FleetActionRunStartResult>? onStarted, FleetActionRun? fromRun = null)
        {
            _Screen = screen ?? throw new ArgumentNullException(nameof(screen));
            _InitialActionId = initialActionId;
            _OnStarted = onStarted;
            FromRun = fromRun;
            FromStatuses.Options = FleetActionLabels.TargetStatuses.Select(st => new SelectOption<FleetActionTargetStatusEnum>(st, screen.Tr(FleetActionLabels.TargetStatusLabel(st)))).ToList();
            FromStatuses.SetValues(FleetActionLabels.DefaultFollowUpStatuses, false);
            FromStatuses.ModalHost = screen.Context.Modals;
            FromStatuses.PickerTitle = "Target statuses";
            FromStatuses.Placeholder = "None";
            Mode = screen.NewSelect("Action source", new List<SelectOption<string>>
            {
                new SelectOption<string>("saved", screen.Tr("Saved action")),
                new SelectOption<string>("adhoc", screen.Tr("Ad hoc")),
            });
            Mode.SetValue(initialDefinition != null ? "adhoc" : "saved");
            Action = screen.NewSelect("Action", new List<SelectOption<string>>(), "Loading...");
            Action.Required = true;
            Kind = screen.NewSelect("Kind", new List<SelectOption<string>>
            {
                new SelectOption<string>("Command", screen.Tr("Command"), screen.Tr(FleetActionLabels.KindDescription(FleetActionKindEnum.Command))),
                new SelectOption<string>("Mission", screen.Tr("Mission"), screen.Tr(FleetActionLabels.KindDescription(FleetActionKindEnum.Mission))),
            });
            Pipeline = screen.NewSelect("Pipeline", screen.Reference.PipelineOptions("Vessel default"));
            Pipeline.SetValue("");
            Name.MaxLength = 200;
            Name.Placeholder = "e.g. Show git status";
            Name.Validator = v => v.Trim().Length == 0 ? "Name is required." : v.Trim().Length > 200 ? "Name must be 200 characters or fewer." : null;
            Command.Placeholder = "git status -sb";
            Command.ExternalEditor = (text, done) => screen.EditExternally(text, done, ".sh");
            Command.Validator = v => v.Trim().Length == 0 ? "Command text is required." : null;
            Prompt.ExternalEditor = (text, done) => screen.EditExternally(text, done);
            Prompt.Validator = v => v.Trim().Length == 0 ? "Prompt template is required." : null;
            Timeout.Value = "300";
            Timeout.Validator = v => Strict(v, 5, 7200) ? null : "Timeout must be a whole number of seconds from 5 to 7200.";
            Concurrency.Value = "4";
            Concurrency.Validator = v => Strict(v, 1, 32) ? null : "Concurrency must be a whole number from 1 to 32.";
            _Definition.Translate = false;
            _Preview.Translate = false;
            _Variables = new Button("Template variables", () =>
            {
                OpsTextArea target = Kind.Value == "Mission" ? Prompt : Command;
                FleetActionForm.ShowVariables(screen, token => { target.Editor.InsertText(token); Changed(); });
            });

            if (initialDefinition != null)
            {
                Name.Value = initialDefinition.Name ?? "";
                Kind.SetValue((initialDefinition.Kind ?? FleetActionKindEnum.Command).ToString());
                Command.Text = initialDefinition.CommandText ?? "";
                Prompt.Text = initialDefinition.PromptTemplate ?? "";
                Pipeline.SetValue(initialDefinition.PipelineId ?? "");
                Timeout.Value = (initialDefinition.TimeoutSeconds ?? 300).ToString(CultureInfo.InvariantCulture);
                AdHocClean.Checked = initialDefinition.RequiresCleanWorkingTree ?? (initialDefinition.Kind != FleetActionKindEnum.Mission);
            }
            else
            {
                Kind.SetValue("Command");
            }

            Mode.ValueChanged += (s, e) => { _CleanOverride = null; Changed(); };
            Action.ValueChanged += (s, e) => { _CleanOverride = null; FollowAction(); Changed(); };
            Kind.ValueChanged += (s, e) => { AdHocClean.Checked = e.NewValue != "Mission"; Changed(); };
            SavedClean.Changed += (s, e) => _CleanOverride = SavedClean.Checked;
            Command.Changed += (s, e) => Changed();
            Prompt.Changed += (s, e) => Changed();
            Concurrency.ValueChanged += (s, e) => Changed();
            FromStatuses.ValueChanged += (s, e) => Changed();
            screen.Reference.Changed += (s, list) =>
            {
                if (list != "pipelines") return;
                string? current = Pipeline.Value;
                Pipeline.Options = screen.Reference.PipelineOptions("Vessel default");
                Pipeline.SetValue(current ?? "");
            };
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Start the flow: open the vessel picker, or go straight to the run dialog when vessels are given.
        /// </summary>
        /// <param name="screen">Owning screen.</param>
        /// <param name="vesselIds">Target vessels, or null to pick them first.</param>
        /// <param name="initialActionId">Saved action to preselect, or null.</param>
        /// <param name="initialDefinition">Ad hoc definition, or null.</param>
        /// <param name="onStarted">Callback, or null to open the run.</param>
        /// <returns>The flow.</returns>
        public static FleetActionRunFlow Start(OpsScreen screen, List<string>? vesselIds, string? initialActionId, FleetActionUpsertRequest? initialDefinition, Action<FleetActionRunStartResult>? onStarted = null)
        {
            FleetActionRunFlow flow = new FleetActionRunFlow(screen, initialActionId, initialDefinition, onStarted);
            if (vesselIds != null && vesselIds.Count > 0) flow.OpenRun(vesselIds);
            else flow.OpenPicker();
            return flow;
        }

        /// <summary>
        /// Start a follow-up run of <paramref name="run"/>: choose the action (linked fix actions first) and the target
        /// statuses, then review and run. The new run's targets are the earlier run's vessels with those statuses.
        /// </summary>
        /// <param name="screen">Owning screen.</param>
        /// <param name="run">Earlier run.</param>
        /// <param name="onStarted">Callback, or null to open the new run.</param>
        /// <returns>The flow.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="screen"/> or <paramref name="run"/> is null.</exception>
        public static FleetActionRunFlow StartFollowUp(OpsScreen screen, FleetActionRun run, Action<FleetActionRunStartResult>? onStarted = null)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));
            FleetActionRunFlow flow = new FleetActionRunFlow(screen, null, null, onStarted, run);
            flow.VesselIds = new List<string>();
            flow.LoadActions();
            flow.ShowConfigure();
            return flow;
        }

        /// <summary>
        /// True when <paramref name="action"/> is linked to the follow-up source run's action (its ScanSourceActionKey
        /// names that action's built-in key or id).
        /// </summary>
        /// <param name="action">Action.</param>
        /// <returns>True when suggested.</returns>
        public bool IsSuggested(FleetAction action)
        {
            if (FromRun == null || action == null || String.IsNullOrEmpty(action.ScanSourceActionKey) || String.IsNullOrEmpty(FromRun.ActionId)) return false;
            if (String.Equals(action.ScanSourceActionKey, FromRun.ActionId, StringComparison.Ordinal)) return true;
            FleetAction? source = Actions.FirstOrDefault(a => a.Id == FromRun.ActionId);
            return source != null && !String.IsNullOrEmpty(source.BuiltInKey) && String.Equals(action.ScanSourceActionKey, source.BuiltInKey, StringComparison.Ordinal);
        }

        /// <summary>
        /// Runtime inputs the selected saved action declares (empty for ad hoc runs).
        /// </summary>
        /// <returns>Inputs.</returns>
        public List<FleetActionInput> SelectedInputs()
        {
            if (Mode.Value != "saved") return new List<FleetActionInput>();
            return SelectedAction()?.Inputs.ToList() ?? new List<FleetActionInput>();
        }

        /// <summary>
        /// Open the vessel picker.
        /// </summary>
        /// <returns>The picker.</returns>
        public VesselPickerDialog OpenPicker()
        {
            VesselPickerDialog picker = new VesselPickerDialog("Choose vessels to run on", _Screen.Context.Modals, _Screen.Context.Loc, _Screen.Context.Theme.Current);
            _Screen.Call(async (c, t) =>
            {
                ArmadaPageQuery q = new ArmadaPageQuery();
                q.PageSize = 1000;
                EnumerationResult<Vessel>? v = await c.ListVesselsAsync(q, t).ConfigureAwait(false);
                EnumerationResult<Fleet>? f = await c.ListFleetsAsync(q, t).ConfigureAwait(false);
                return new KeyValuePair<List<Vessel>, List<Fleet>>(v?.Objects ?? new List<Vessel>(), f?.Objects ?? new List<Fleet>());
            }, data => picker.SetData(data.Key, data.Value), null, ex =>
            {
                picker.Loading = false;
                picker.Error = String.IsNullOrEmpty(ex.Message) ? _Screen.Tr("Failed to load vessels.") : ex.Message;
            });
            _Screen.Context.Modals.Show(picker, result =>
            {
                if (result is List<string> ids && ids.Count > 0) OpenRun(ids);
            });
            return picker;
        }

        /// <summary>
        /// Load the saved actions and the preview vessel, then show the configure step.
        /// </summary>
        /// <param name="vesselIds">Target vessels.</param>
        public void OpenRun(List<string> vesselIds)
        {
            VesselIds = vesselIds.ToList();
            LoadActions();
            _Screen.Call((c, t) => c.GetVesselAsync(VesselIds[0], t), v => { PreviewVessel = v; Changed(); }, null, ex => { PreviewVessel = null; Changed(); });
            _Screen.Reference.Ensure("pipelines");
            ShowConfigure();
        }

        /// <summary>
        /// The selected saved action, or null.
        /// </summary>
        /// <returns>Action or null.</returns>
        public FleetAction? SelectedAction()
        {
            return Actions.FirstOrDefault(a => a.Id == Action.Value);
        }

        /// <summary>
        /// The kind that will run.
        /// </summary>
        /// <returns>Kind.</returns>
        public FleetActionKindEnum EffectiveKind()
        {
            if (Mode.Value == "saved") return SelectedAction()?.Kind ?? FleetActionKindEnum.Command;
            return Kind.Value == "Mission" ? FleetActionKindEnum.Mission : FleetActionKindEnum.Command;
        }

        /// <summary>
        /// The command or prompt that will run.
        /// </summary>
        /// <returns>Text.</returns>
        public string BodyText()
        {
            if (Mode.Value == "saved")
            {
                FleetAction? a = SelectedAction();
                if (a == null) return "";
                return (a.Kind == FleetActionKindEnum.Command ? a.CommandText : a.PromptTemplate) ?? "";
            }

            return Kind.Value == "Mission" ? Prompt.Text : Command.Text;
        }

        /// <summary>
        /// True when the clean-tree check applies.
        /// </summary>
        /// <returns>True when on.</returns>
        public bool EffectiveClean()
        {
            if (Mode.Value == "saved") return _CleanOverride ?? SelectedAction()?.RequiresCleanWorkingTree ?? true;
            return AdHocClean.Checked;
        }

        #endregion

        #region Private-Methods

        private static bool Strict(string value, int min, int max)
        {
            string v = (value ?? "").Trim();
            if (v.Length == 0 || !v.All(Char.IsDigit)) return false;
            return Int32.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n >= min && n <= max;
        }

        private void LoadActions()
        {
            _ActionsLoading = true;
            _Screen.Call((c, t) => c.EnumerateFleetActionsAsync(new FleetActionEnumerateQuery { PageNumber = 1, PageSize = 500 }, t), result =>
            {
                List<FleetAction> all = result?.Objects ?? new List<FleetAction>();
                Actions = all;
                if (IsFollowUp) Actions = all.Where(a => IsSuggested(a)).Concat(all.Where(a => !IsSuggested(a))).ToList();
                _ActionsLoading = false;
                Action.Options = Actions.Select(a => new SelectOption<string>(a.Id, ActionOptionLabel(a))).ToList();
                Action.Placeholder = Actions.Count == 0 ? "No saved actions" : "Select...";
                string? pick = _InitialActionId != null && Actions.Any(a => a.Id == _InitialActionId) ? _InitialActionId : Actions.FirstOrDefault()?.Id;
                if (pick != null) Action.Choose(Action.Options.First(o => o.Value == pick));
                FollowAction();
                Changed();
            }, null, ex =>
            {
                _ActionsLoading = false;
                _ActionsError = String.IsNullOrEmpty(ex.Message) ? _Screen.Tr("Failed to load fleet actions.") : ex.Message;
                Changed();
            });
        }

        private string ActionOptionLabel(FleetAction a)
        {
            string label = a.Name + " (" + _Screen.Tr(a.Kind.ToString()) + ")";
            if (FleetActionLabels.IsReportOnly(a.Kind, a.MissionMode)) label += "  [" + _Screen.Tr("Report-only") + "]";
            if (IsSuggested(a)) label += "  [" + _Screen.Tr("Suggested") + "]";
            return label;
        }

        private void FollowAction()
        {
            FleetAction? a = SelectedAction();
            if (Mode.Value == "saved" && a != null)
            {
                Concurrency.Value = Math.Min(32, Math.Max(1, a.DefaultConcurrency > 0 ? a.DefaultConcurrency : 4)).ToString(CultureInfo.InvariantCulture);
                SavedClean.Checked = a.RequiresCleanWorkingTree;
                _CleanOverride = null;
            }
        }

        private void ShowConfigure()
        {
            OpsFormDialog dialog = _Screen.NewForm(IsFollowUp ? "Follow-up run" : "Run fleet action", "Review and run");
            dialog.WidthRatio = 0.85;
            foreach (ArmadaWidget w in new ArmadaWidget[] { Mode, Action, FromStatuses, SavedClean, Name, Kind, Command, Prompt, Pipeline, _Variables, Timeout, AdHocClean, Concurrency, _Definition, _Preview })
                w.OnFocusChanged(false);
            if (!IsFollowUp) dialog.AddField("Action source", Mode);
            dialog.AddField("Action", Action, IsFollowUp ? "Actions linked to this run's action (a fix for its scan) are listed first." : null);
            if (IsFollowUp) dialog.AddField("Target statuses", FromStatuses, "The new run targets this run's vessels whose target status is one of these.");
            dialog.AddField("Definition", _Definition, null, 4);
            dialog.AddField("", SavedClean);
            dialog.AddField("Name", Name);
            dialog.AddField("Kind", Kind);
            dialog.AddField("Command text", Command, null, 4);
            dialog.AddField("Prompt template", Prompt, null, 6);
            dialog.AddField("Pipeline", Pipeline);
            dialog.AddField("Template variables", _Variables);
            dialog.AddField("Timeout (seconds)", Timeout);
            dialog.AddField("", AdHocClean);
            dialog.AddField("Concurrency", Concurrency);
            dialog.AddField("Preview", _Preview, null, 6);
            dialog.Error = _ServerError;
            dialog.Validate = Validate;
            dialog.Submit = d => true;
            Dialog = dialog;
            Changed();
            _Screen.Context.Modals.Show(dialog, result =>
            {
                if (result is bool ok && ok) Next();
            });
        }

        private void Next()
        {
            if (SelectedInputs().Count > 0) ShowInputs();
            else ShowConfirm();
        }

        private void ShowInputs()
        {
            FleetAction? action = SelectedAction();
            List<FleetActionInput> inputs = SelectedInputs();
            if (action == null || inputs.Count == 0)
            {
                ShowConfirm();
                return;
            }

            if (_InputsActionId != action.Id)
            {
                InputValues.Clear();
                foreach (FleetActionInput input in inputs) InputValues[input.Name] = input.Default ?? "";
                _InputsActionId = action.Id;
            }

            OpsFormDialog dialog = _Screen.NewForm("Action inputs", "Continue");
            dialog.Form.DiscardButton.Label = "Back";
            dialog.WidthRatio = 0.8;
            dialog.Notes.Add(action.Name);
            dialog.Notes.Add(_Screen.Tr("Values are substituted for {{input.name}} in the action's command or prompt."));
            Dictionary<string, Func<string>> readers = new Dictionary<string, Func<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (FleetActionInput input in inputs)
            {
                FleetActionInput def = input;
                string label = def.Name + (def.Required ? " *" : "");
                string hint = (String.IsNullOrEmpty(def.Description) ? "" : def.Description + " ") + "(" + _Screen.Tr(TypeLabel(def.Type)) + ")";
                string current = InputValues.TryGetValue(def.Name, out string? v) ? v : "";
                if (def.Type == "enum" && def.AllowedValues.Count > 0)
                {
                    List<SelectOption<string>> options = new List<SelectOption<string>>();
                    if (!def.Required) options.Add(new SelectOption<string>("", _Screen.Tr("(none)")));
                    options.AddRange(def.AllowedValues.Select(a => new SelectOption<string>(a, a)));
                    SelectField<string> select = _Screen.NewSelect(def.Name, options);
                    select.Required = def.Required;
                    select.SetValue(options.Any(o => o.Value == current) ? current : options[0].Value);
                    dialog.AddField(label, select, hint);
                    readers[def.Name] = () => select.Value ?? "";
                }
                else
                {
                    InputField field = new InputField();
                    field.MaxLength = 4000;
                    field.Value = current;
                    field.Placeholder = def.Type == "semver" ? "1.2.3" : def.Type == "package" ? "Newtonsoft.Json" : def.Required ? "" : "Optional";
                    field.Validator = text => FleetActionLabels.InputError(def, text.Trim());
                    dialog.AddField(label, field, hint);
                    readers[def.Name] = () => field.Value.Trim();
                }
            }

            dialog.Validate = () =>
            {
                foreach (FleetActionInput input in inputs)
                {
                    string? error = FleetActionLabels.InputError(input, readers[input.Name]());
                    if (error != null) return input.Name + ": " + _Screen.Tr(error);
                }

                return null;
            };
            bool back = false;
            dialog.Form.DiscardRequested += (s, e) => back = true;
            dialog.Submit = d =>
            {
                foreach (FleetActionInput input in inputs) InputValues[input.Name] = readers[input.Name]();
                return true;
            };
            Dialog = dialog;
            _Screen.Context.Modals.Show(dialog, result =>
            {
                if (result is bool ok && ok) ShowConfirm();
                else if (back) ShowConfigure();
            });
        }

        private static string TypeLabel(string type)
        {
            switch (type)
            {
                case "semver": return "semantic version";
                case "enum": return "one of the allowed values";
                case "package": return "package identifier";
                default: return "text";
            }
        }

        private string? Validate()
        {
            if (IsFollowUp)
            {
                if (SelectedAction() == null) return _Screen.Tr("Choose an action.");
                if (FromStatuses.Values.Count == 0) return _Screen.Tr("Choose at least one target status.");
                return null;
            }

            if (VesselIds.Count < 1) return _Screen.Tr("Select at least one vessel.");
            if (VesselIds.Count > FleetActionLabels.MaxRunVessels) return _Screen.Tr("A run can target at most 500 vessels.");
            if (Mode.Value == "saved")
            {
                if (SelectedAction() == null) return _Screen.Tr("Choose an action.");
                return null;
            }

            List<string> unknown = FleetActionLabels.FindUnknownVariables(BodyText());
            if (unknown.Count > 0) return _Screen.Tr("Unknown template variable: {{names}}", LocalizationArgs.Of("names", String.Join(", ", unknown)));
            return null;
        }

        private void Changed()
        {
            bool saved = Mode.Value == "saved";
            bool command = Kind.Value != "Mission";
            FleetAction? action = SelectedAction();
            Mode.Visible = !IsFollowUp;
            FromStatuses.Visible = IsFollowUp;
            Action.Visible = saved;
            _Definition.Visible = saved && action != null;
            SavedClean.Visible = saved && action != null && action.Kind == FleetActionKindEnum.Command;
            Name.Visible = !saved;
            Kind.Visible = !saved;
            Command.Visible = !saved && command;
            Prompt.Visible = !saved && !command;
            Pipeline.Visible = !saved && !command;
            _Variables.Visible = !saved;
            Timeout.Visible = !saved && command;
            AdHocClean.Visible = !saved && command;
            if (saved && action != null) SavedClean.Checked = EffectiveClean();

            List<string> definition = new List<string>();
            if (action != null)
            {
                string kind = _Screen.Tr(action.Kind.ToString());
                if (action.Kind == FleetActionKindEnum.Mission) kind += " / " + _Screen.Tr(FleetActionLabels.MissionModeLabel(action.MissionMode));
                definition.Add(kind + (String.IsNullOrEmpty(action.Description) ? "" : "  " + action.Description));
                if (action.Inputs.Count > 0) definition.Add(_Screen.Tr("Inputs") + ": " + String.Join(", ", action.Inputs.Select(i => i.Name + (i.Required ? " *" : ""))) + "  " + _Screen.Tr("(asked for in the next step)"));
                definition.AddRange(BodyText().Replace("\r\n", "\n").Split('\n'));
            }

            _Definition.Text = String.Join("\n", definition);
            SetRowHeight(_Definition, Math.Clamp(definition.Count, 1, 6));

            List<string> preview = new List<string>();
            int count = VesselIds.Count;
            if (IsFollowUp && FromRun != null)
            {
                string statuses = String.Join(", ", FromStatuses.Values.Select(st => _Screen.Tr(FleetActionLabels.TargetStatusLabel(st))));
                preview.Add(_Screen.Tr("Targets: the vessels of run \"{{name}}\" whose target status is {{statuses}}.", LocalizationArgs.Of("name", FromRun.ActionName, "statuses", statuses.Length > 0 ? statuses : "-")));
                preview.Add(_Screen.Tr("The server resolves the vessels when the run starts; vessels picked here are not used."));
                _Preview.Text = String.Join("\n", preview);
                SetRowHeight(_Preview, 2);
                if (Dialog != null)
                {
                    Dialog.Notes.Clear();
                    Dialog.Notes.Add(_Screen.Tr("Follow-up of run {{id}}", LocalizationArgs.Of("id", FromRun.Id)));
                    if (_ActionsLoading) Dialog.Notes.Add(_Screen.Tr("Loading..."));
                    if (_ActionsError != null) Dialog.Notes.Add("! " + _ActionsError);
                }

                return;
            }

            string head = PreviewVessel != null ? _Screen.Tr("Preview for {{name}}", LocalizationArgs.Of("name", PreviewVessel.Name)) : _Screen.Tr("Preview");
            if (count > 1) head += " " + _Screen.Tr("{count, plural, one {(and # more vessel)} other {(and # more vessels)}}", LocalizationArgs.Of("count", count - 1));
            preview.Add(head);
            string body = BodyText();
            if (body.Length == 0) preview.Add(_Screen.Tr("Choose or define an action to see the rendered text."));
            else if (PreviewVessel == null) preview.Add(_Screen.Tr("Loading preview..."));
            else
            {
                string rendered = FleetActionLabels.RenderPreview(body, PreviewVessel, _Screen.Tr("[health summary is rendered on the server for each vessel]"), out bool health);
                preview.AddRange(rendered.Replace("\r\n", "\n").Split('\n'));
                if (health) preview.Add(_Screen.Tr("{{health.summary}} is rendered on the server from each vessel's latest health evaluation."));
            }

            preview.Add(EffectiveKind() == FleetActionKindEnum.Command
                ? _Screen.Tr("How many vessels run the command at the same time (1-32). The Admiral-wide limit also applies.")
                : _Screen.Tr("How many voyages from this run may be active at once (1-32)."));
            _Preview.Text = String.Join("\n", preview);
            SetRowHeight(_Preview, Math.Clamp(preview.Count, 2, 12));

            if (Dialog != null)
            {
                Dialog.Notes.Clear();
                Dialog.Notes.Add(_Screen.Tr("{count, plural, one {# vessel selected} other {# vessels selected}}", LocalizationArgs.Of("count", count)));
                if (_ActionsLoading && saved) Dialog.Notes.Add(_Screen.Tr("Loading..."));
                if (_ActionsError != null && saved) Dialog.Notes.Add("! " + _ActionsError);
            }
        }

        private void SetRowHeight(ArmadaWidget widget, int height)
        {
            if (Dialog == null) return;
            FormRow? row = Dialog.Form.Rows.FirstOrDefault(r => ReferenceEquals(r.Field, widget));
            if (row != null) row.Height = height;
        }

        private void ShowConfirm()
        {
            int count = VesselIds.Count;
            int concurrency = Int32.TryParse(Concurrency.Value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int c) ? c : 1;
            FleetActionKindEnum kind = EffectiveKind();
            string submit = IsFollowUp ? "Start follow-up run" : _Screen.Tr("{count, plural, one {Run on # vessel} other {Run on # vessels}}", LocalizationArgs.Of("count", count));
            OpsFormDialog dialog = _Screen.NewForm(IsFollowUp ? "Follow-up run" : "Run fleet action", submit);
            dialog.Form.DiscardButton.Label = "Back";
            dialog.WidthRatio = 0.8;
            string title = Mode.Value == "saved" ? SelectedAction()?.Name ?? "" : Name.Value.Trim();
            dialog.Notes.Add(title);
            dialog.Notes.Add("");
            FleetAction? chosen = Mode.Value == "saved" ? SelectedAction() : null;
            List<FleetActionInput> inputs = SelectedInputs();
            if (IsFollowUp && FromRun != null)
            {
                string statuses = String.Join(", ", FromStatuses.Values.Select(st => _Screen.Tr(FleetActionLabels.TargetStatusLabel(st))));
                dialog.Notes.Add(_Screen.Tr("Targets: the vessels of run \"{{name}}\" whose target status is {{statuses}}.", LocalizationArgs.Of("name", FromRun.ActionName, "statuses", statuses)));
                dialog.Notes.Add(kind == FleetActionKindEnum.Command
                    ? "! " + _Screen.Tr("This runs the command below in the working directory of each of those vessels, on the Admiral host or each vessel's preferred Harbor.")
                    : _Screen.Tr("This dispatches one voyage per vessel."));
                if (kind == FleetActionKindEnum.Mission) dialog.Notes.Add(_Screen.Tr("{count, plural, one {At most # voyage from this run is active at a time.} other {At most # voyages from this run are active at a time.}}", LocalizationArgs.Of("count", concurrency)));
                else dialog.Notes.Add(EffectiveClean()
                    ? _Screen.Tr("Vessels with uncommitted changes are skipped.")
                    : _Screen.Tr("The clean-tree check is off: the command also runs in vessels with uncommitted changes."));
            }
            else if (kind == FleetActionKindEnum.Command)
            {
                dialog.Notes.Add("! " + _Screen.Tr("{count, plural, one {This runs the command below in the working directory of # vessel, on the Admiral host or the vessel's preferred Harbor.} other {This runs the command below in the working directory of each of # vessels, on the Admiral host or each vessel's preferred Harbor.}}", LocalizationArgs.Of("count", count)));
                dialog.Notes.Add(EffectiveClean()
                    ? _Screen.Tr("Vessels with uncommitted changes are skipped.")
                    : _Screen.Tr("The clean-tree check is off: the command also runs in vessels with uncommitted changes."));
            }
            else
            {
                dialog.Notes.Add(_Screen.Tr("{count, plural, one {This dispatches # voyage, one per vessel.} other {This dispatches # voyages, one per vessel.}}", LocalizationArgs.Of("count", count)));
                dialog.Notes.Add(_Screen.Tr("{count, plural, one {At most # voyage from this run is active at a time.} other {At most # voyages from this run are active at a time.}}", LocalizationArgs.Of("count", concurrency)));
            }

            if (chosen != null && FleetActionLabels.IsReportOnly(chosen.Kind, chosen.MissionMode))
                dialog.Notes.Add(_Screen.Tr("Report-only: missions run read-only and never land; each captain's report is stored on its run target (View report on the run page)."));
            dialog.Notes.Add("");
            foreach (string line in BodyText().Replace("\r\n", "\n").Split('\n')) dialog.Notes.Add("  " + line);
            if (inputs.Count > 0)
            {
                dialog.Notes.Add("");
                foreach (FleetActionInput input in inputs)
                {
                    string value = InputValues.TryGetValue(input.Name, out string? v) ? v : "";
                    dialog.Notes.Add("  " + input.Name + " = " + (value.Length > 0 ? value : _Screen.Tr("(empty)")));
                }
            }

            dialog.Notes.Add("");
            dialog.Notes.Add(IsFollowUp
                ? _Screen.Tr("Concurrency") + ": " + _Screen.Context.Loc.FormatNumber(concurrency)
                : _Screen.Tr("Vessels") + ": " + _Screen.Context.Loc.FormatNumber(count) + "   " + _Screen.Tr("Concurrency") + ": " + _Screen.Context.Loc.FormatNumber(concurrency));
            bool started = false;
            bool back = false;
            dialog.Form.DiscardRequested += (s, e) => back = true;
            dialog.Submit = d =>
            {
                FleetActionRunRequest request = new FleetActionRunRequest();
                request.VesselIds = IsFollowUp ? new List<string>() : VesselIds.ToList();
                request.Concurrency = concurrency;
                if (IsFollowUp && FromRun != null)
                {
                    request.FromRunId = FromRun.Id;
                    request.FromTargetStatuses = FromStatuses.Values.ToList();
                }

                if (inputs.Count > 0)
                {
                    Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (FleetActionInput input in inputs)
                    {
                        values[input.Name] = InputValues.TryGetValue(input.Name, out string? v) ? v : "";
                    }

                    request.InputValues = values;
                }

                FleetAction? action = SelectedAction();
                bool saved = Mode.Value == "saved" && action != null;
                if (saved)
                {
                    if (action!.Kind == FleetActionKindEnum.Command && _CleanOverride.HasValue && _CleanOverride.Value != action.RequiresCleanWorkingTree)
                    {
                        FleetActionRunOverrides overrides = new FleetActionRunOverrides();
                        overrides.RequiresCleanWorkingTree = _CleanOverride.Value;
                        request.Overrides = overrides;
                    }
                }
                else
                {
                    bool isCommand = Kind.Value != "Mission";
                    FleetActionUpsertRequest def = new FleetActionUpsertRequest();
                    def.Name = Name.Value.Trim();
                    def.Kind = isCommand ? FleetActionKindEnum.Command : FleetActionKindEnum.Mission;
                    def.CommandText = isCommand ? Command.Text : null;
                    def.PromptTemplate = isCommand ? null : Prompt.Text;
                    def.PipelineId = !isCommand && !String.IsNullOrEmpty(Pipeline.Value) ? Pipeline.Value : null;
                    def.TimeoutSeconds = isCommand ? Int32.Parse(Timeout.Value.Trim(), CultureInfo.InvariantCulture) : (int?)null;
                    def.RequiresCleanWorkingTree = isCommand && AdHocClean.Checked;
                    request.Definition = def;
                }

                string? actionId = saved ? action!.Id : null;
                _Screen.Call((cl, t) => actionId != null ? cl.RunFleetActionAsync(actionId, request, t) : cl.RunAdHocFleetActionAsync(request, t), result =>
                {
                    started = true;
                    _ServerError = null;
                    d.Complete();
                    int targets = result?.TargetCount ?? count;
                    _Screen.Toast(NotificationSeverityEnum.Success, _Screen.Tr("{count, plural, one {Fleet action started on # vessel.} other {Fleet action started on # vessels.}}", LocalizationArgs.Of("count", targets)));
                    if (result == null) return;
                    if (_OnStarted != null) _OnStarted(result);
                    else _Screen.Context.Navigate("/fleet-actions/runs/" + Uri.EscapeDataString(result.RunId));
                }, null, ex =>
                {
                    _ServerError = String.IsNullOrEmpty(ex.Message) ? _Screen.Tr("Failed to start the run.") : ex.Message;
                    d.Complete(false);
                });
                return false;
            };
            Dialog = dialog;
            _Screen.Context.Modals.Show(dialog, result =>
            {
                if (started) return;
                if (back || (result is bool b && !b))
                {
                    if (inputs.Count > 0) ShowInputs();
                    else ShowConfigure();
                }
            });
        }

        #endregion
    }
}

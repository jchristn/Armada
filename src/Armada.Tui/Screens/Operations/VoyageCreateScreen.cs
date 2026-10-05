namespace Armada.Tui.Screens.Operations
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
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
    /// Create Voyage (W3.9, <c>/voyages/create</c>), the dashboard's VoyageCreate page: title, description, vessel,
    /// pipeline (or inherit), Auto-Push, Auto-Create PRs, Auto-Merge PRs, playbooks with a delivery mode, and the
    /// missions list (title, priority, description; <c>Ctrl+N</c> adds, <c>Ctrl+D</c> removes the focused one).
    /// <c>Ctrl+S</c> creates the voyage and opens it; <c>Esc</c> returns to Voyages. Not thread-safe.
    /// </summary>
    public class VoyageCreateScreen : OpsScreen
    {
        #region Public-Members

        /// <summary>
        /// The form (rebuilt when missions are added or removed).
        /// </summary>
        public FormView Form { get; private set; } = new FormView();

        /// <summary>
        /// Title.
        /// </summary>
        public InputField VoyageTitle { get; } = new InputField();

        /// <summary>
        /// Description.
        /// </summary>
        public OpsTextArea VoyageDescription { get; } = new OpsTextArea();

        /// <summary>
        /// Vessel.
        /// </summary>
        public SelectField<string> Vessel { get; }

        /// <summary>
        /// Pipeline (value: name; empty inherits).
        /// </summary>
        public SelectField<string> Pipeline { get; }

        /// <summary>
        /// Auto-Push.
        /// </summary>
        public OpsCheckField AutoPush { get; } = new OpsCheckField("");

        /// <summary>
        /// Auto-Create PRs.
        /// </summary>
        public OpsCheckField AutoCreatePrs { get; } = new OpsCheckField("");

        /// <summary>
        /// Auto-Merge PRs.
        /// </summary>
        public OpsCheckField AutoMergePrs { get; } = new OpsCheckField("");

        /// <summary>
        /// Playbooks.
        /// </summary>
        public MultiSelectField<string> Playbooks { get; } = new MultiSelectField<string>();

        /// <summary>
        /// Delivery mode for the selected playbooks.
        /// </summary>
        public SelectField<string> DeliveryMode { get; }

        /// <summary>
        /// Mission titles.
        /// </summary>
        public List<InputField> MissionTitles { get; } = new List<InputField>();

        /// <summary>
        /// Mission priorities.
        /// </summary>
        public List<InputField> MissionPriorities { get; } = new List<InputField>();

        /// <summary>
        /// Mission descriptions.
        /// </summary>
        public List<OpsTextArea> MissionDescriptions { get; } = new List<OpsTextArea>();

        /// <summary>
        /// True while creating.
        /// </summary>
        public bool Submitting { get; private set; } = false;

        /// <inheritdoc />
        public override bool CanFocus { get; set; } = true;

        /// <inheritdoc />
        public override IReadOnlyList<KeyValuePair<string, string>> TypingHints
        {
            get
            {
                return new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("Ctrl+S", "Create Voyage"),
                    new KeyValuePair<string, string>("Ctrl+N", "Add Mission"),
                    new KeyValuePair<string, string>("Ctrl+D", "Remove"),
                    new KeyValuePair<string, string>("Ctrl+E", "Editor")
                };
            }
        }

        /// <inheritdoc />
        public override IReadOnlyList<KeyValuePair<string, string>> Hints
        {
            get
            {
                return new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("Ctrl+S", "Create Voyage"),
                    new KeyValuePair<string, string>("Ctrl+N", "Add Mission"),
                    new KeyValuePair<string, string>("Ctrl+D", "Remove"),
                    new KeyValuePair<string, string>("Ctrl+E", "Editor"),
                    new KeyValuePair<string, string>("Esc", "Cancel"),
                };
            }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Services.</param>
        public VoyageCreateScreen(RouteMatch route, TuiContext context)
            : base(route, context, "VoyageCreateScreen", "Create Voyage")
        {
            VoyageTitle.Placeholder = "Name for this batch of missions";
            VoyageDescription.Placeholder = "Optional description for the voyage...";
            VoyageDescription.ExternalEditor = (text, done) => EditExternally(text, done);
            Vessel = NewSelect("Vessel", new List<SelectOption<string>>(), "Select a vessel...");
            Pipeline = NewSelect("Pipeline", new List<SelectOption<string>> { new SelectOption<string>("", Tr("Inherit (vessel, then fleet, then WorkerOnly)")) });
            Pipeline.SetValue("");
            Playbooks.ModalHost = context.Modals;
            Playbooks.PickerTitle = "Playbooks";
            Playbooks.Placeholder = "None";
            DeliveryMode = NewSelect("Delivery mode", new List<SelectOption<string>>
            {
                new SelectOption<string>("InlineFullContent", Tr("Inline Full Content"), Tr("Inject the complete markdown into the mission instructions.")),
                new SelectOption<string>("InstructionWithReference", Tr("Instruction With Reference"), Tr("Tell the model to read the materialized playbook path outside the worktree.")),
                new SelectOption<string>("AttachIntoWorktree", Tr("Attach Into Worktree"), Tr("Materialize the playbook in `.armada/playbooks/` and instruct the model to read it there.")),
            });
            DeliveryMode.SetValue("InlineFullContent");
            Reference.Changed += (s, name) => ApplyReference(name);
            Reference.Ensure("vessels", "pipelines", "playbooks");
            AddMissionFields();
            Rebuild();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Add a mission row.
        /// </summary>
        public void AddMission()
        {
            AddMissionFields();
            Rebuild();
            Form.Scope.Focus(MissionTitles[MissionTitles.Count - 1]);
        }

        /// <summary>
        /// Remove a mission row (the dashboard keeps at least one).
        /// </summary>
        /// <param name="index">Index.</param>
        public void RemoveMission(int index)
        {
            if (MissionTitles.Count <= 1 || index < 0 || index >= MissionTitles.Count) return;
            MissionTitles.RemoveAt(index);
            MissionPriorities.RemoveAt(index);
            MissionDescriptions.RemoveAt(index);
            Rebuild();
        }

        /// <summary>
        /// Validate and create the voyage.
        /// </summary>
        public void Submit()
        {
            if (Submitting) return;
            if (VoyageTitle.Value.Trim().Length == 0)
            {
                ShowMessage(Tr("Voyage title is required."));
                return;
            }

            if (String.IsNullOrEmpty(Vessel.Value))
            {
                ShowMessage(Tr("Please select a vessel."));
                return;
            }

            List<DispatchRequest> missions = new List<DispatchRequest>();
            for (int i = 0; i < MissionTitles.Count; i++)
            {
                string t = MissionTitles[i].Value.Trim();
                if (t.Length == 0) continue;
                DispatchRequest m = new DispatchRequest();
                m.Title = t;
                string d = MissionDescriptions[i].Text.Trim();
                m.Description = d.Length > 0 ? d : t;
                m.VesselId = Vessel.Value;
                m.Priority = Int32.TryParse(MissionPriorities[i].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int p) && p != 0 ? p : 100;
                missions.Add(m);
            }

            if (missions.Count == 0)
            {
                ShowMessage(Tr("At least one mission with a title is required."));
                return;
            }

            VoyageCreateRequest req = new VoyageCreateRequest();
            req.Title = VoyageTitle.Value.Trim();
            string desc = VoyageDescription.Text.Trim();
            req.Description = desc.Length > 0 ? desc : null;
            req.VesselId = Vessel.Value;
            if (!String.IsNullOrEmpty(Pipeline.Value)) req.Pipeline = Pipeline.Value;
            req.Missions = missions;
            PlaybookDeliveryModeEnum mode = Enum.TryParse(DeliveryMode.Value, out PlaybookDeliveryModeEnum dm) ? dm : PlaybookDeliveryModeEnum.InlineFullContent;
            if (Playbooks.Values.Count > 0)
            {
                req.SelectedPlaybooks = Playbooks.Values.Select(id => new SelectedPlaybook { PlaybookId = id, DeliveryMode = mode }).ToList();
            }

            string title = req.Title;
            Submitting = true;
            Call((c, t) => c.CreateVoyageAsync(req, t), v =>
            {
                Submitting = false;
                Toast(NotificationSeverityEnum.Success, Tr("Voyage \"{{title}}\" created.", LocalizationArgs.Of("title", title)));
                if (v != null && !String.IsNullOrEmpty(v.Id)) Context.Navigate("/voyages/" + Uri.EscapeDataString(v.Id));
            }, null, ex =>
            {
                Submitting = false;
                ShowMessage(String.IsNullOrEmpty(ex.Message) ? Tr("Failed to create voyage.") : ex.Message);
            });
        }

        /// <inheritdoc />
        public override IEnumerable<ArmadaCommand> Commands()
        {
            List<ArmadaCommand> list = new List<ArmadaCommand>();
            ArmadaCommand create = new ArmadaCommand(ScreenKey + ".create", "Create Voyage", CommandMenuEnum.Actions, Submit, "ctrl+s");
            create.Dispatch = false;
            list.Add(create);
            ArmadaCommand add = new ArmadaCommand(ScreenKey + ".add-mission", "Add Mission", CommandMenuEnum.Actions, AddMission, "ctrl+n");
            add.Dispatch = false;
            list.Add(add);
            ArmadaCommand cancel = new ArmadaCommand(ScreenKey + ".cancel", "Cancel", CommandMenuEnum.Actions, () => Context.Navigate("/missions?tab=voyages"));
            cancel.Dispatch = false;
            list.Add(cancel);
            return list;
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            bool ctrl = (key.Modifiers & KeyModifiers.Ctrl) != 0;
            if (ctrl && key.Code == KeyCode.Character)
            {
                char c = Char.ToLowerInvariant((char)key.Rune);
                if (c == 'n')
                {
                    AddMission();
                    return true;
                }

                if (c == 'd')
                {
                    int idx = FocusedMission();
                    if (idx >= 0) RemoveMission(idx);
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
            SurfaceText.Draw(surface, 0, 0, Tr("Voyages") + " / " + Tr("Create Voyage"), Theme.Muted, width);
            SurfaceText.Draw(surface, 0, 1, Tr("Create Voyage"), Theme.Accent.WithAttribute(CellAttributes.Bold, true), width);
            string hint = Submitting ? Tr("Creating...") : "Ctrl+N " + Tr("Add Mission") + "   Ctrl+D " + Tr("Remove");
            SurfaceText.Draw(surface, Math.Max(0, width - TextCells.Width(hint)), 1, hint, Theme.Muted, width);
            Scope.RenderChild(surface, Form, new Rect(0, 3, width, Math.Max(1, height - 3)));
        }

        #endregion

        #region Private-Methods

        private void ApplyReference(string name)
        {
            if (name == "vessels")
            {
                string? v = Vessel.Value;
                Vessel.Options = Reference.Vessels.Select(x => new SelectOption<string>(x.Id, x.Name + " (" + x.Id + ")")).ToList();
                if (v != null) Vessel.SetValue(v);
            }
            else if (name == "pipelines")
            {
                string? p = Pipeline.Value;
                List<SelectOption<string>> options = new List<SelectOption<string>> { new SelectOption<string>("", Tr("Inherit (vessel, then fleet, then WorkerOnly)")) };
                options.AddRange(Reference.Pipelines.Select(x => new SelectOption<string>(x.Name, x.Name + " (" + String.Join(" -> ", (x.Stages ?? new List<PipelineStage>()).Select(s => s.PersonaName)) + ")")));
                Pipeline.Options = options;
                Pipeline.SetValue(p ?? "");
            }
            else if (name == "playbooks")
            {
                Playbooks.Options = Reference.Playbooks.Where(p => p.Active).Select(p => new SelectOption<string>(p.Id, p.FileName, p.Description ?? "")).ToList();
            }
        }

        private void AddMissionFields()
        {
            InputField title = new InputField();
            title.Placeholder = "What needs to be done?";
            InputField priority = new InputField();
            priority.Value = "100";
            priority.Validator = v => Int32.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int p) && p >= 0 && p <= 1000 ? null : "Enter a number from 0 to 1000.";
            OpsTextArea description = new OpsTextArea();
            description.Placeholder = "Detailed instructions for the AI captain...";
            description.ExternalEditor = (text, done) => EditExternally(text, done);
            MissionTitles.Add(title);
            MissionPriorities.Add(priority);
            MissionDescriptions.Add(description);
        }

        private int FocusedMission()
        {
            IWidget? focused = Form.Scope.Focused;
            for (int i = 0; i < MissionTitles.Count; i++)
            {
                if (ReferenceEquals(focused, MissionTitles[i]) || ReferenceEquals(focused, MissionPriorities[i]) || ReferenceEquals(focused, MissionDescriptions[i])) return i;
            }

            return -1;
        }

        private void Rebuild()
        {
            IWidget? previous = Form.Scope.Focused;
            if (Scope.Children.Contains(Form)) Scope.Remove(Form);
            FormView form = new FormView();
            form.SaveButton.Label = "Create Voyage";
            form.SaveButton.Hint = "Ctrl+S";
            form.DiscardButton.Label = "Cancel";
            form.SaveRequested += (s, e) => Submit();
            form.DiscardRequested += (s, e) => Context.Navigate("/missions?tab=voyages");
            form.AddSection("Voyage Details");
            form.AddField("Title", VoyageTitle);
            form.AddField("Description", VoyageDescription, null, 3);
            form.AddField("Vessel", Vessel);
            form.AddField("Pipeline", Pipeline);
            form.AddField("Auto-Push", AutoPush);
            form.AddField("Auto-Create PRs", AutoCreatePrs);
            form.AddField("Auto-Merge PRs", AutoMergePrs);
            form.AddSection("Playbooks");
            form.AddField("Playbooks", Playbooks, Reference.Loaded.Contains("playbooks") && Playbooks.Options.Count == 0 ? "No active playbooks found." : null);
            form.AddField("Delivery mode", DeliveryMode);
            form.AddSection(Tr("Missions") + " (" + MissionTitles.Count + ")");
            for (int i = 0; i < MissionTitles.Count; i++)
            {
                string n = Tr("Mission {{index}}", LocalizationArgs.Of("index", i + 1));
                form.AddField(n + " " + Tr("Title"), MissionTitles[i]);
                form.AddField(n + " " + Tr("Priority"), MissionPriorities[i]);
                form.AddField(n + " " + Tr("Description"), MissionDescriptions[i], null, 2);
            }

            Form = AddChild(form);
            Scope.Focus(Form);
            if (previous != null) Form.Scope.Focus(previous);
        }

        #endregion
    }
}

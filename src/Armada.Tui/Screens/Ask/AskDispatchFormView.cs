namespace Armada.Tui.Screens.Ask
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json.Nodes;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Client.Models;
    using Armada.Core.Models;
    using Armada.Tui.Ask;
    using Armada.Tui.Text;
    using Armada.Tui.Widgets;
    using TUIKit;

    /// <summary>
    /// The inline <c>/dispatch</c> form (the dashboard's <c>AskDispatchForm</c>): vessel (preselected when there is only
    /// one), optional pipeline ("Vessel default"), optional voyage title ("Defaults to the first mission title"), and one
    /// to 25 missions with a title and an optional description (<c>Ctrl+N</c> adds a mission, <c>Ctrl+D</c> removes the
    /// focused one). Submitting runs the MCP <c>dispatch</c> tool; "Submitting this form is the confirmation; the voyage
    /// starts right away." Not thread-safe.
    /// </summary>
    public class AskDispatchFormView : AskQuickActionForm
    {
        #region Public-Members

        /// <summary>
        /// Vessel picker.
        /// </summary>
        public SelectField<string> Vessel { get; } = new SelectField<string>();

        /// <summary>
        /// Pipeline picker (empty value is the vessel default).
        /// </summary>
        public SelectField<string> Pipeline { get; } = new SelectField<string>();

        /// <summary>
        /// Voyage title.
        /// </summary>
        public InputField VoyageTitle { get; } = new InputField();

        /// <summary>
        /// Mission title fields.
        /// </summary>
        public List<InputField> MissionTitles { get; } = new List<InputField>();

        /// <summary>
        /// Mission description fields.
        /// </summary>
        public List<InputField> MissionDescriptions { get; } = new List<InputField>();

        /// <inheritdoc />
        public override int PreferredHeight
        {
            get { return 8 + MissionTitles.Count * 2 + (Errors.Count > 0 ? 1 : 0) + (LoadError != null ? 1 : 0); }
        }

        #endregion

        #region Private-Members

        private readonly Button _AddMission;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate and load vessels and pipelines.
        /// </summary>
        /// <param name="context">Services.</param>
        /// <param name="ask">Ask session.</param>
        /// <param name="action">Quick action.</param>
        /// <param name="load">Load choices from the server (tests may set them directly).</param>
        public AskDispatchFormView(TuiContext context, AskController ask, AskQuickAction action, bool load = true)
            : base(context, ask, action, "Dispatch")
        {
            Vessel.PickerTitle = "Vessel";
            Vessel.Placeholder = "Loading...";
            Vessel.ModalHost = context.Modals;
            Pipeline.PickerTitle = "Pipeline (optional)";
            Pipeline.Placeholder = "Vessel default";
            Pipeline.ModalHost = context.Modals;
            Pipeline.Options = new List<SelectOption<string>> { new SelectOption<string>("", "Vessel default") };
            VoyageTitle.Placeholder = "Defaults to the first mission title";
            VoyageTitle.MaxLength = 200;
            _AddMission = new Button("+ Add mission", AddMission);
            AddMissionFields("", "");
            Rebuild();
            if (load) LoadChoices();
            else Loading = false;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Set the vessel and pipeline choices (sorted by name; a single vessel is preselected).
        /// </summary>
        /// <param name="vessels">Vessels.</param>
        /// <param name="pipelines">Pipelines.</param>
        public void SetChoices(IEnumerable<Vessel> vessels, IEnumerable<Pipeline> pipelines)
        {
            List<Vessel> list = vessels.OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase).ToList();
            Vessel.Options = list.Select(v => new SelectOption<string>(v.Id, v.Name, v.Id)).ToList();
            Vessel.Placeholder = "Choose a vessel";
            Pipeline.Options = new List<SelectOption<string>> { new SelectOption<string>("", "Vessel default") }
                .Concat(pipelines.Select(p => new SelectOption<string>(p.Id, p.Name, p.Id))).ToList();
            if (list.Count == 1) Vessel.SetValue(list[0].Id);
            Loading = false;
        }

        /// <summary>
        /// The current draft.
        /// </summary>
        /// <returns>Draft.</returns>
        public AskDispatchDraft Draft()
        {
            AskDispatchDraft draft = new AskDispatchDraft();
            draft.VesselId = Vessel.Value ?? "";
            draft.PipelineId = Pipeline.Value ?? "";
            draft.Title = VoyageTitle.Value;
            draft.Missions = MissionTitles.Select((t, i) => new AskDispatchMissionDraft(t.Value, MissionDescriptions[i].Value)).ToList();
            return draft;
        }

        /// <summary>
        /// Add an empty mission (up to 25).
        /// </summary>
        public void AddMission()
        {
            if (MissionTitles.Count >= AskQuickActions.MaxMissions || Ask.ActionBusy) return;
            AddMissionFields("", "");
            Rebuild();
            Scope.Focus(MissionTitles[MissionTitles.Count - 1]);
        }

        /// <summary>
        /// Remove a mission (at least one stays).
        /// </summary>
        /// <param name="index">Index.</param>
        public void RemoveMission(int index)
        {
            if (MissionTitles.Count <= 1 || index < 0 || index >= MissionTitles.Count || Ask.ActionBusy) return;
            MissionTitles.RemoveAt(index);
            MissionDescriptions.RemoveAt(index);
            Rebuild();
            Scope.Focus(MissionTitles[Math.Min(index, MissionTitles.Count - 1)]);
        }

        /// <inheritdoc />
        public override bool HandleKey(TUIKit.Input.KeyEvent key)
        {
            bool ctrl = (key.Modifiers & TUIKit.Input.KeyModifiers.Ctrl) != 0;
            if (ctrl && key.Code == TUIKit.Input.KeyCode.Character)
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

            return base.HandleKey(key);
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            if (width < 20 || height < 3) return;
            SurfaceText.FillRect(surface, new Rect(0, 0, width, height), Theme.Text);
            DrawHeader(surface, "Dispatch a voyage", width);
            int y = 1;
            if (LoadError != null) SurfaceText.Draw(surface, 1, y++, "! " + LoadError, Theme.Error, width - 1);
            int half = Math.Max(20, (width - 2) / 2);
            Label(surface, 1, y, "Vessel", 12);
            SubmitButton.Label = Ask.ActionBusy ? "Dispatching..." : "Dispatch";
            SubmitButton.Enabled = !Ask.ActionBusy && !Loading;
            Scope.RenderChild(surface, Vessel, new Rect(13, y, Math.Max(8, half - 14), 1));
            Label(surface, half + 1, y, "Pipeline (optional)", 20);
            Scope.RenderChild(surface, Pipeline, new Rect(half + 22, y, Math.Max(8, width - half - 23), 1));
            y++;
            if (DrawError(surface, "vessel", 13, y, width)) y++;
            Label(surface, 1, y, "Voyage title (optional)", 24);
            Scope.RenderChild(surface, VoyageTitle, new Rect(26, y++, Math.Max(8, width - 27), 1));
            Label(surface, 1, y++, "Missions", width - 1);
            for (int i = 0; i < MissionTitles.Count && y < height - 2; i++)
            {
                string n = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + ".";
                SurfaceText.Draw(surface, 1, y, n, Theme.Muted, 4);
                Scope.RenderChild(surface, MissionTitles[i], new Rect(5, y++, Math.Max(8, width - 6), 1));
                Scope.RenderChild(surface, MissionDescriptions[i], new Rect(5, y++, Math.Max(8, width - 6), 1));
            }

            if (DrawError(surface, "missions", 5, y, width)) y++;
            Scope.RenderChild(surface, _AddMission, new Rect(5, y, Math.Min(24, width - 6), 1));
            string hint = "Ctrl+N " + T("Add mission") + (MissionTitles.Count > 1 ? "  Ctrl+D " + T("Remove mission") : "");
            SurfaceText.Draw(surface, 30, y++, hint, Theme.Muted, width - 31);
            if (y < height)
            {
                string note = T("Submitting this form is the confirmation; the voyage starts right away.");
                int bw = 32;
                SurfaceText.Draw(surface, 1, y, note, Theme.Muted, Math.Max(1, width - bw - 2));
                Scope.RenderChild(surface, Buttons, new Rect(Math.Max(1, width - bw), y, bw, 1));
            }
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override Dictionary<string, string> Validate()
        {
            return AskQuickActions.ValidateDispatch(Draft());
        }

        /// <inheritdoc />
        protected override JsonObject BuildArguments()
        {
            return AskQuickActions.BuildDispatchArguments(Draft());
        }

        #endregion

        #region Private-Methods

        private void AddMissionFields(string title, string description)
        {
            InputField t = new InputField();
            t.Placeholder = "Mission title";
            t.MaxLength = 200;
            t.Value = title;
            InputField d = new InputField();
            d.Placeholder = "What should the captain do? (optional)";
            d.Value = description;
            t.Submitted += (s, e) => Scope.Move(true);
            d.Submitted += (s, e) => Scope.Move(true);
            MissionTitles.Add(t);
            MissionDescriptions.Add(d);
        }

        private int FocusedMission()
        {
            object? focused = Scope.Focused;
            for (int i = 0; i < MissionTitles.Count; i++)
            {
                if (ReferenceEquals(focused, MissionTitles[i]) || ReferenceEquals(focused, MissionDescriptions[i])) return i;
            }

            return -1;
        }

        private void Rebuild()
        {
            TUIKit.Widgets.IWidget? previous = Scope.Focused;
            bool active = Scope.IsActive;
            if (active) Scope.SetActive(false);
            Scope.Clear();
            AddChild(Vessel);
            AddChild(Pipeline);
            AddChild(VoyageTitle);
            for (int i = 0; i < MissionTitles.Count; i++)
            {
                AddChild(MissionTitles[i]);
                AddChild(MissionDescriptions[i]);
            }

            AddChild(_AddMission);
            AddChild(Buttons);
            VoyageTitle.Submitted -= MoveNext;
            VoyageTitle.Submitted += MoveNext;
            if (previous != null) Scope.Focus(previous);
            if (active) Scope.SetActive(true);
        }

        private void MoveNext(object? sender, EventArgs e)
        {
            Scope.Move(true);
        }

        private void LoadChoices()
        {
            ArmadaClient client = Context.Client;
            _ = Task.Run(async () =>
            {
                try
                {
                    ArmadaPageQuery vq = new ArmadaPageQuery();
                    vq.PageSize = 9999;
                    EnumerationResult<Vessel>? vessels = await client.ListVesselsAsync(vq).ConfigureAwait(false);
                    List<Pipeline> pipelines;
                    try
                    {
                        ArmadaPageQuery pq = new ArmadaPageQuery();
                        pq.PageSize = 500;
                        pipelines = (await client.ListPipelinesAsync(pq).ConfigureAwait(false))?.Objects ?? new List<Pipeline>();
                    }
                    catch (ArmadaApiException)
                    {
                        pipelines = new List<Pipeline>();
                    }

                    Context.Dispatcher.Post(() => SetChoices(vessels?.Objects ?? new List<Vessel>(), pipelines));
                }
                catch (ArmadaApiException ex)
                {
                    Context.Dispatcher.Post(() =>
                    {
                        Loading = false;
                        LoadError = String.IsNullOrEmpty(ex.Message) ? T("Failed to load vessels.") : ex.Message;
                    });
                }
            });
        }

        #endregion
    }
}

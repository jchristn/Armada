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
    /// The inline <c>/fleet-action</c> form (the dashboard's <c>AskFleetActionForm</c>): pick a saved fleet action and
    /// the vessels to run it on (filter, Select visible / Clear visible, "N selected"). Submitting runs the MCP
    /// <c>run_fleet_action</c> tool; "Submitting this form is the confirmation; the run starts right away." Not
    /// thread-safe.
    /// </summary>
    public class AskFleetActionFormView : AskQuickActionForm
    {
        #region Public-Members

        /// <summary>
        /// Action picker.
        /// </summary>
        public SelectField<string> ActionPicker { get; } = new SelectField<string>();

        /// <summary>
        /// Vessel checklist.
        /// </summary>
        public AskVesselChecklist Vessels { get; } = new AskVesselChecklist();

        /// <inheritdoc />
        public override int PreferredHeight
        {
            get { return 6 + Math.Min(6, Math.Max(1, Vessels.Vessels.Count)) + (Errors.Count > 0 ? 1 : 0) + (LoadError != null ? 1 : 0); }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate and load actions and vessels.
        /// </summary>
        /// <param name="context">Services.</param>
        /// <param name="ask">Ask session.</param>
        /// <param name="action">Quick action.</param>
        /// <param name="load">Load choices from the server (tests may set them directly).</param>
        public AskFleetActionFormView(TuiContext context, AskController ask, AskQuickAction action, bool load = true)
            : base(context, ask, action, "Run action")
        {
            ActionPicker.PickerTitle = "Action";
            ActionPicker.Placeholder = "Loading...";
            ActionPicker.ModalHost = context.Modals;
            AddChild(ActionPicker);
            AddChild(Vessels);
            AddChild(Buttons);
            if (load) LoadChoices();
            else Loading = false;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Set the action and vessel choices.
        /// </summary>
        /// <param name="actions">Fleet actions.</param>
        /// <param name="vessels">Vessels.</param>
        public void SetChoices(IEnumerable<FleetAction> actions, IEnumerable<Vessel> vessels)
        {
            ActionPicker.Options = actions.Select(a => new SelectOption<string>(a.Id, a.Name, a.Id)).ToList();
            ActionPicker.Placeholder = "Choose an action";
            Vessels.SetVessels(vessels.Select(v => new KeyValuePair<string, string>(v.Id, v.Name)));
            Loading = false;
        }

        /// <summary>
        /// The current draft.
        /// </summary>
        /// <returns>Draft.</returns>
        public AskFleetActionDraft Draft()
        {
            AskFleetActionDraft draft = new AskFleetActionDraft();
            draft.ActionId = ActionPicker.Value ?? "";
            draft.VesselIds = Vessels.Selected.ToList();
            return draft;
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            if (width < 20 || height < 3) return;
            SurfaceText.FillRect(surface, new Rect(0, 0, width, height), Theme.Text);
            DrawHeader(surface, "Run a fleet action", width);
            int y = 1;
            if (LoadError != null) SurfaceText.Draw(surface, 1, y++, "! " + LoadError, Theme.Error, width - 1);
            SubmitButton.Label = Ask.ActionBusy ? "Starting..." : "Run action";
            SubmitButton.Enabled = !Ask.ActionBusy && !Loading;
            Label(surface, 1, y, "Action", 10);
            Scope.RenderChild(surface, ActionPicker, new Rect(11, y++, Math.Max(10, width - 12), 1));
            if (DrawError(surface, "action", 11, y, width)) y++;
            string count = T("Vessels") + " (" + Context.Loc.T("{{count}} selected", Services.LocalizationArgs.Of("count", Vessels.Selected.Count)) + ")";
            SurfaceText.Draw(surface, 1, y++, count, Theme.Muted, width - 1);
            int listRows = Math.Max(2, Math.Min(7, height - y - 2 - (Errors.ContainsKey("vessels") ? 1 : 0)));
            Scope.RenderChild(surface, Vessels, new Rect(1, y, width - 2, listRows));
            y += listRows;
            if (DrawError(surface, "vessels", 1, y, width)) y++;
            if (y < height)
            {
                int bw = 30;
                SurfaceText.Draw(surface, 1, y, T("Submitting this form is the confirmation; the run starts right away."), Theme.Muted, Math.Max(1, width - bw - 2));
                Scope.RenderChild(surface, Buttons, new Rect(Math.Max(1, width - bw), y, bw, 1));
            }
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override Dictionary<string, string> Validate()
        {
            return AskQuickActions.ValidateFleetAction(Draft());
        }

        /// <inheritdoc />
        protected override JsonObject BuildArguments()
        {
            return AskQuickActions.BuildFleetActionArguments(Draft());
        }

        #endregion

        #region Private-Methods

        private void LoadChoices()
        {
            ArmadaClient client = Context.Client;
            _ = Task.Run(async () =>
            {
                try
                {
                    FleetActionEnumerateQuery aq = new FleetActionEnumerateQuery();
                    aq.PageSize = 500;
                    EnumerationResult<FleetAction>? actions = await client.EnumerateFleetActionsAsync(aq).ConfigureAwait(false);
                    ArmadaPageQuery vq = new ArmadaPageQuery();
                    vq.PageSize = 9999;
                    EnumerationResult<Vessel>? vessels = await client.ListVesselsAsync(vq).ConfigureAwait(false);
                    Context.Dispatcher.Post(() => SetChoices(actions?.Objects ?? new List<FleetAction>(), vessels?.Objects ?? new List<Vessel>()));
                }
                catch (ArmadaApiException ex)
                {
                    Context.Dispatcher.Post(() =>
                    {
                        Loading = false;
                        LoadError = String.IsNullOrEmpty(ex.Message) ? T("Failed to load fleet actions.") : ex.Message;
                    });
                }
            });
        }

        #endregion
    }
}

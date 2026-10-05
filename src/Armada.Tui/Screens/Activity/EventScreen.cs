namespace Armada.Tui.Screens.Activity
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Client.Models;
    using Armada.Core.Models;
    using Armada.Tui.Input;
    using Armada.Tui.Routing;
    using Armada.Tui.Screens.Kit;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// Event detail (dashboard <c>EventDetail.tsx</c>, route <c>/events/:id</c>): id, type, message, entity, captain,
    /// mission, vessel, voyage, tenant, created, and the payload (pretty-printed when it is JSON), with View JSON,
    /// Delete (confirmed; returns to Events), and links that open the related records. Not thread-safe.
    /// </summary>
    public class EventScreen : StackScreen
    {
        #region Public-Members

        /// <summary>
        /// Event id from the route.
        /// </summary>
        public string EventId { get; }

        /// <summary>
        /// Loaded event, or null.
        /// </summary>
        public ArmadaEvent? Event { get; private set; } = null;

        /// <summary>
        /// Header.
        /// </summary>
        public ScreenHeader Header { get; }

        /// <summary>
        /// Fields.
        /// </summary>
        public RecordDetailView Details { get; } = new RecordDetailView();

        /// <summary>
        /// Payload viewer.
        /// </summary>
        public JsonOrTextViewer Payload { get; } = new JsonOrTextViewer();

        /// <inheritdoc />
        public override IReadOnlyList<KeyValuePair<string, string>> Hints
        {
            get
            {
                return new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("Enter", "Open link"),
                    new KeyValuePair<string, string>("y", "Copy"),
                    new KeyValuePair<string, string>("j", "JSON"),
                    new KeyValuePair<string, string>("Del", "Delete"),
                };
            }
        }

        #endregion

        #region Private-Members

        private readonly TextBlock _PayloadTitle = new TextBlock("Payload", t => t.Accent);
        private Dictionary<string, string> _CaptainNames = new Dictionary<string, string>(StringComparer.Ordinal);
        private Dictionary<string, string> _VesselNames = new Dictionary<string, string>(StringComparer.Ordinal);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Context.</param>
        public EventScreen(RouteMatch route, TuiContext context)
            : base(route, context)
        {
            EventId = route.Parameters.TryGetValue("id", out string? id) ? id : "";
            Header = new ScreenHeader("Event Details", "");
            Header.Status = context.Loc.T("Events") + " / " + EventId;
            Header.AddButton("Back to Events", () => Context.Navigate("/activity?source=events"));
            Header.AddButton("View JSON", ShowJson, "j");
            Header.AddButton("Delete", Delete, "Del");
            AddFixed(Header, w => Header.HeightFor(w));
            AddFixed(Details, w => Event == null ? 1 : Math.Min(Details.HeightFor(w), 18));
            AddFixed(_PayloadTitle, w => Event != null && !String.IsNullOrEmpty(Event.Payload) ? 1 : 0);
            AddFill(Payload);
            Details.CopyRequested += (s, value) => Context.Clipboard.Copy(value, "Value");
            Details.LinkActivated += (s, link) => Context.Navigate(link);
            Payload.Visible = false;
            Details.Add("Loading...", "");
            Scope.Focus(Details);
            Load();
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override Action? RefreshAction()
        {
            return Load;
        }

        /// <inheritdoc />
        public override IEnumerable<ArmadaCommand> Commands()
        {
            ArmadaCommand json = new ArmadaCommand(ScreenKey + ".json", "View JSON", CommandMenuEnum.Actions, ShowJson, "j");
            json.IsEnabled = () => Event != null;
            ArmadaCommand delete = new ArmadaCommand(ScreenKey + ".delete", "Delete", CommandMenuEnum.Actions, Delete, "Delete");
            delete.IsEnabled = () => Event != null;
            ArmadaCommand back = new ArmadaCommand(ScreenKey + ".back", "Back to Events", CommandMenuEnum.Actions, () => Context.Navigate("/activity?source=events"));
            return new List<ArmadaCommand> { json, delete, back };
        }

        /// <summary>
        /// Load (or reload) the event and the name lookups.
        /// </summary>
        public void Load()
        {
            ScreenOps.Run(Context, () => Context.Client.GetEventAsync(EventId), e =>
            {
                Event = e;
                Banner = e == null ? Context.Loc.T("Failed to load event.") : "";
                Rebuild();
            }, "Failed to load event.", ex => { Banner = Context.Loc.T("Failed to load event."); });
            ScreenOps.Quiet(Context, () => Context.Client.ListCaptainsAsync(new ArmadaPageQuery(1, 1000)), r =>
            {
                _CaptainNames = (r?.Objects ?? new List<Captain>()).ToDictionary(c => c.Id, c => c.Name, StringComparer.Ordinal);
                Rebuild();
            });
            ScreenOps.Quiet(Context, () => Context.Client.ListVesselsAsync(new ArmadaPageQuery(1, 1000)), r =>
            {
                _VesselNames = (r?.Objects ?? new List<Vessel>()).ToDictionary(v => v.Id, v => v.Name, StringComparer.Ordinal);
                Rebuild();
            });
        }

        /// <summary>
        /// Pretty-print a payload when it parses as JSON; otherwise return it unchanged.
        /// </summary>
        /// <param name="payload">Payload.</param>
        /// <returns>Display text.</returns>
        public static string FormatPayload(string? payload)
        {
            if (String.IsNullOrEmpty(payload)) return "";
            try
            {
                using (System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(payload))
                {
                    return System.Text.Json.JsonSerializer.Serialize(doc.RootElement, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                }
            }
            catch (System.Text.Json.JsonException)
            {
                return payload;
            }
        }

        #endregion

        #region Private-Methods

        private void Rebuild()
        {
            ArmadaEvent? e = Event;
            if (e == null) return;
            int cursor = Details.Cursor;
            Details.Clear();
            Details.AddRow("ID", e.Id);
            Details.AddRow("Event Type", e.EventType);
            Details.AddRow("Message", String.IsNullOrEmpty(e.Message) ? "-" : e.Message);
            if (!String.IsNullOrEmpty(e.EntityType)) Details.AddRow("Entity Type", e.EntityType);
            if (!String.IsNullOrEmpty(e.EntityId)) Details.AddRow("Entity ID", e.EntityId, ScreenOps.EntityRoute(e.EntityType, e.EntityId));
            if (!String.IsNullOrEmpty(e.CaptainId)) Details.AddRow("Captain", Name(_CaptainNames, e.CaptainId), "/captains/" + e.CaptainId);
            if (!String.IsNullOrEmpty(e.MissionId)) Details.AddRow("Mission", e.MissionId, "/missions/" + e.MissionId);
            if (!String.IsNullOrEmpty(e.VesselId)) Details.AddRow("Vessel", Name(_VesselNames, e.VesselId), "/vessels/" + e.VesselId);
            if (!String.IsNullOrEmpty(e.VoyageId)) Details.AddRow("Voyage", e.VoyageId, "/voyages/" + e.VoyageId);
            Details.AddRow("Tenant ID", String.IsNullOrEmpty(e.TenantId) ? "-" : e.TenantId);
            Details.AddRow("Created", ScreenOps.Both(Context, e.CreatedUtc));
            for (int i = 0; i < cursor; i++) Details.HandleKey(new TUIKit.Input.KeyEvent(TUIKit.Input.KeyCode.Down, 0, TUIKit.Input.KeyModifiers.None));
            Payload.Text = FormatPayload(e.Payload);
            Payload.Visible = !String.IsNullOrEmpty(e.Payload);
        }

        private static string Name(Dictionary<string, string> names, string? id)
        {
            if (String.IsNullOrEmpty(id)) return "-";
            return names.TryGetValue(id, out string? name) && !String.IsNullOrEmpty(name) ? name : id;
        }

        private void ShowJson()
        {
            if (Event == null) return;
            ScreenOps.ShowJson(Context, Context.Loc.T("Event: {{id}}", LocalizationArgs.Of("id", Event.Id)), Event);
        }

        private void Delete()
        {
            if (Event == null) return;
            string id = Event.Id;
            Context.Confirm("Delete", Context.Loc.T("Delete {{entity}} {{name}}?", LocalizationArgs.Of("entity", Context.Loc.T("Event").ToLowerInvariant(), "name", id)), () =>
            {
                ScreenOps.Run(Context, () => Context.Client.DeleteEventsBatchAsync(new List<string> { id }), r =>
                {
                    ScreenOps.Toast(Context, NotificationSeverityEnum.Warning, "Event {{id}} deleted.", LocalizationArgs.Of("id", id));
                    Context.Navigate("/activity?source=events");
                }, "Delete failed.");
            }, "Delete");
        }

        #endregion
    }
}

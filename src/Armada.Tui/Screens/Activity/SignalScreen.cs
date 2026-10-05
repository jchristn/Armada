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
    /// Signal detail (dashboard <c>SignalDetail.tsx</c>, route <c>/signals/:id</c>): id, type, read, from, to,
    /// mission, tenant, created, and the payload (pretty-printed when it is JSON, "(empty)" otherwise), with Mark
    /// Read (unread only), View JSON, Delete (confirmed; returns to Signals), and links to captains and the mission.
    /// Not thread-safe.
    /// </summary>
    public class SignalScreen : StackScreen
    {
        #region Public-Members

        /// <summary>
        /// Signal id from the route.
        /// </summary>
        public string SignalId { get; }

        /// <summary>
        /// Loaded signal, or null.
        /// </summary>
        public Signal? Signal { get; private set; } = null;

        /// <summary>
        /// Related mission id, or null.
        /// </summary>
        public string? MissionId { get; private set; } = null;

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
                    new KeyValuePair<string, string>("r", "Mark Read"),
                    new KeyValuePair<string, string>("j", "JSON"),
                    new KeyValuePair<string, string>("Del", "Delete"),
                };
            }
        }

        #endregion

        #region Private-Members

        private readonly TextBlock _PayloadTitle = new TextBlock("Payload", t => t.Accent);
        private readonly Button _MarkRead;
        private List<Captain> _Captains = new List<Captain>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Context.</param>
        public SignalScreen(RouteMatch route, TuiContext context)
            : base(route, context)
        {
            SignalId = route.Parameters.TryGetValue("id", out string? id) ? id : "";
            Header = new ScreenHeader("Signal Details", "");
            Header.Status = context.Loc.T("Signals") + " / " + SignalId;
            Header.AddButton("Back to Signals", () => Context.Navigate("/activity?source=signals"));
            _MarkRead = Header.AddButton("Mark Read", MarkRead, "r");
            _MarkRead.Visible = false;
            Header.AddButton("View JSON", ShowJson, "j");
            Header.AddButton("Delete", Delete, "Del");
            AddFixed(Header, w => Header.HeightFor(w));
            AddFixed(Details, w => Signal == null ? 1 : Math.Min(Details.HeightFor(w), 18));
            AddFixed(_PayloadTitle, w => Signal != null ? 1 : 0);
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
            ArmadaCommand read = new ArmadaCommand(ScreenKey + ".mark-read", "Mark Read", CommandMenuEnum.Actions, MarkRead, "r");
            read.IsEnabled = () => Signal != null && !Signal.Read;
            ArmadaCommand json = new ArmadaCommand(ScreenKey + ".json", "View JSON", CommandMenuEnum.Actions, ShowJson, "j");
            json.IsEnabled = () => Signal != null;
            ArmadaCommand delete = new ArmadaCommand(ScreenKey + ".delete", "Delete", CommandMenuEnum.Actions, Delete, "Delete");
            delete.IsEnabled = () => Signal != null;
            ArmadaCommand back = new ArmadaCommand(ScreenKey + ".back", "Back to Signals", CommandMenuEnum.Actions, () => Context.Navigate("/activity?source=signals"));
            return new List<ArmadaCommand> { read, json, delete, back };
        }

        /// <summary>
        /// Load (or reload) the signal and captain names.
        /// </summary>
        public void Load()
        {
            ScreenOps.Run(Context, () => Context.Client.GetSignalAsync(SignalId), s =>
            {
                Signal = s;
                Banner = s == null ? Context.Loc.T("Failed to load signal.") : "";
                Rebuild();
            }, "Failed to load signal.", ex => { Banner = Context.Loc.T("Failed to load signal."); });
            ScreenOps.Quiet(Context, () => Context.Client.GetEntityAsync("signals", SignalId), raw =>
            {
                MissionId = raw?.As<SignalMissionLink>()?.MissionId;
                Rebuild();
            });
            ScreenOps.Quiet(Context, () => Context.Client.ListCaptainsAsync(new ArmadaPageQuery(1, 1000)), r =>
            {
                _Captains = r?.Objects ?? new List<Captain>();
                Rebuild();
            });
        }

        /// <summary>
        /// Mark the signal read, then reload it.
        /// </summary>
        public void MarkRead()
        {
            if (Signal == null || Signal.Read) return;
            ScreenOps.Run(Context, async () =>
            {
                await Context.Client.MarkSignalReadAsync(SignalId).ConfigureAwait(false);
                return await Context.Client.GetSignalAsync(SignalId).ConfigureAwait(false);
            }, s =>
            {
                if (s != null) Signal = s;
                Rebuild();
                ScreenOps.Toast(Context, NotificationSeverityEnum.Success, "Signal marked as read.");
            }, "Failed to mark signal as read.");
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override void BeforeRender(int width, int height)
        {
            _MarkRead.Visible = Signal != null && !Signal.Read;
        }

        #endregion

        #region Private-Methods

        private string CaptainName(string? id)
        {
            if (String.IsNullOrEmpty(id)) return Context.Loc.T("Admiral");
            Captain? c = _Captains.FirstOrDefault(x => x.Id == id);
            return c != null && !String.IsNullOrEmpty(c.Name) ? c.Name : id;
        }

        private void Rebuild()
        {
            Signal? s = Signal;
            if (s == null) return;
            int cursor = Details.Cursor;
            Details.Clear();
            Details.AddRow("ID", s.Id);
            Details.AddRow("Type", s.Type.ToString());
            Details.AddRow("Read", Context.Loc.T(s.Read ? "Yes" : "No"));
            Details.AddRow("From", CaptainName(s.FromCaptainId), String.IsNullOrEmpty(s.FromCaptainId) ? null : "/captains/" + s.FromCaptainId);
            Details.AddRow("To", CaptainName(s.ToCaptainId), String.IsNullOrEmpty(s.ToCaptainId) ? null : "/captains/" + s.ToCaptainId);
            if (!String.IsNullOrEmpty(MissionId)) Details.AddRow("Mission", MissionId, "/missions/" + MissionId);
            Details.AddRow("Tenant ID", String.IsNullOrEmpty(s.TenantId) ? "-" : s.TenantId);
            Details.AddRow("Created", ScreenOps.Both(Context, s.CreatedUtc));
            for (int i = 0; i < cursor; i++) Details.HandleKey(new TUIKit.Input.KeyEvent(TUIKit.Input.KeyCode.Down, 0, TUIKit.Input.KeyModifiers.None));
            Payload.Text = String.IsNullOrEmpty(s.Payload) ? Context.Loc.T("(empty)") : EventScreen.FormatPayload(s.Payload);
            Payload.Visible = true;
        }

        private void ShowJson()
        {
            if (Signal == null) return;
            ScreenOps.ShowJson(Context, Context.Loc.T("Signal: {{id}}", LocalizationArgs.Of("id", Signal.Id)), Signal);
        }

        private void Delete()
        {
            if (Signal == null) return;
            string id = Signal.Id;
            Context.Confirm("Delete", Context.Loc.T("Delete {{entity}} {{name}}?", LocalizationArgs.Of("entity", Context.Loc.T("Signal").ToLowerInvariant(), "name", id)), () =>
            {
                ScreenOps.Run(Context, () => Context.Client.DeleteSignalsBatchAsync(new List<string> { id }), r =>
                {
                    ScreenOps.Toast(Context, NotificationSeverityEnum.Warning, "Signal {{id}} deleted.", LocalizationArgs.Of("id", id));
                    Context.Navigate("/activity?source=signals");
                }, "Delete failed.");
            }, "Delete");
        }

        #endregion
    }
}

namespace Armada.Tui.Screens.Activity
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Models;
    using Armada.Tui.Services;
    using Armada.Tui.Text;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;

    /// <summary>
    /// The request detail drawer body (dashboard Request Detail modal): method and route, Replay / Delete / Copy /
    /// Close buttons, and a scrolling view with the summary (entry id, principal, auth method, status, duration,
    /// captured), path and query parameters, request and response headers, and the request and response bodies with
    /// their size or the truncation note. Keys: <c>r</c> replay, <c>Del</c> delete, <c>y</c> copy a block. Not
    /// thread-safe.
    /// </summary>
    public class RequestHistoryDetailPane : ContainerWidget
    {
        #region Public-Members

        /// <summary>
        /// Record shown, or null while loading.
        /// </summary>
        public RequestHistoryRecord? Record { get; private set; } = null;

        /// <summary>
        /// The section view.
        /// </summary>
        public RequestHistorySectionView View { get; } = new RequestHistorySectionView();

        /// <summary>
        /// Raised for Replay.
        /// </summary>
        public event EventHandler? ReplayRequested;

        /// <summary>
        /// Raised for Delete.
        /// </summary>
        public event EventHandler? DeleteRequested;

        /// <summary>
        /// Raised for Copy.
        /// </summary>
        public event EventHandler? CopyRequested;

        /// <summary>
        /// Raised for Close.
        /// </summary>
        public event EventHandler? CloseRequested;

        #endregion

        #region Private-Members

        private readonly ButtonRow _Buttons = new ButtonRow();
        private readonly Button _Replay;
        private readonly Button _Delete;
        private readonly Button _Copy;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public RequestHistoryDetailPane()
        {
            _Replay = _Buttons.Add(new Button("Replay", () => ReplayRequested?.Invoke(this, EventArgs.Empty)) { Hint = "r" });
            _Delete = _Buttons.Add(new Button("Delete", () => DeleteRequested?.Invoke(this, EventArgs.Empty)) { Hint = "Del" });
            _Copy = _Buttons.Add(new Button("Copy", () => CopyRequested?.Invoke(this, EventArgs.Empty)) { Hint = "y" });
            _Buttons.Add(new Button("Close", () => CloseRequested?.Invoke(this, EventArgs.Empty)) { Hint = "Esc" });
            AddChild(View);
            AddChild(_Buttons);
            Scope.Focus(View);
            ShowLoading();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Show the loading state.
        /// </summary>
        public void ShowLoading()
        {
            Record = null;
            View.Clear();
            View.AddText(T("Loading request detail..."), true);
            _Replay.Visible = false;
            _Delete.Visible = false;
            _Copy.Visible = false;
        }

        /// <summary>
        /// Show a record.
        /// </summary>
        /// <param name="record">Record.</param>
        /// <param name="localizer">Localizer (dates).</param>
        public void Show(RequestHistoryRecord record, ITextLocalizer localizer)
        {
            Record = record ?? throw new ArgumentNullException(nameof(record));
            _Replay.Visible = true;
            _Delete.Visible = true;
            _Copy.Visible = true;
            RequestHistoryEntry e = record.Entry;
            RequestHistoryDetail? d = record.Detail;
            View.Clear();
            View.AddPair(T("Entry ID"), e.Id);
            View.AddPair(T("Principal"), String.IsNullOrEmpty(e.PrincipalDisplay) ? T("Anonymous") : e.PrincipalDisplay);
            View.AddPair(T("Auth Method"), String.IsNullOrEmpty(e.AuthMethod) ? "-" : e.AuthMethod);
            View.AddPair(T("Status"), e.StatusCode.ToString(System.Globalization.CultureInfo.InvariantCulture) + (e.IsSuccess ? "" : "  (" + T("Failure") + ")"));
            View.AddPair(T("Duration"), RequestHistoryFormat.Ms(e.DurationMs));
            View.AddPair(T("Captured"), localizer.FormatDateTime(e.CreatedUtc));
            foreach (RequestHistoryBlock block in Blocks())
            {
                if (block.Kind == RequestHistoryBlockEnum.RequestBody || block.Kind == RequestHistoryBlockEnum.ResponseBody) continue;
                View.AddHeading(T(block.Title));
                View.AddCode(block.Text, "json");
            }

            View.AddHeading(T("Request Body"), d != null && d.RequestBodyTruncated ? T("Stored body was truncated") : RequestHistoryFormat.Bytes(e.RequestSizeBytes));
            AddBody(d?.RequestBodyText);
            View.AddHeading(T("Response Body"), d != null && d.ResponseBodyTruncated ? T("Stored body was truncated") : RequestHistoryFormat.Bytes(e.ResponseSizeBytes));
            AddBody(d?.ResponseBodyText);
        }

        /// <summary>
        /// The copyable blocks of the current record, in display order.
        /// </summary>
        /// <returns>Blocks.</returns>
        public List<RequestHistoryBlock> Blocks()
        {
            List<RequestHistoryBlock> blocks = new List<RequestHistoryBlock>();
            if (Record == null) return blocks;
            RequestHistoryDetail? d = Record.Detail;
            blocks.Add(new RequestHistoryBlock(RequestHistoryBlockEnum.PathParameters, "Path Parameters", RequestHistoryFormat.PairsJson(RequestHistoryFormat.ParseDictionary(d?.PathParamsJson))));
            blocks.Add(new RequestHistoryBlock(RequestHistoryBlockEnum.QueryParameters, "Query Parameters", RequestHistoryFormat.PairsJson(RequestHistoryFormat.ParseDictionary(d?.QueryParamsJson))));
            blocks.Add(new RequestHistoryBlock(RequestHistoryBlockEnum.RequestHeaders, "Request Headers", RequestHistoryFormat.PairsJson(RequestHistoryFormat.ParseDictionary(d?.RequestHeadersJson))));
            blocks.Add(new RequestHistoryBlock(RequestHistoryBlockEnum.ResponseHeaders, "Response Headers", RequestHistoryFormat.PairsJson(RequestHistoryFormat.ParseDictionary(d?.ResponseHeadersJson))));
            blocks.Add(new RequestHistoryBlock(RequestHistoryBlockEnum.RequestBody, "Request Body", String.IsNullOrEmpty(d?.RequestBodyText) ? "(empty)" : d!.RequestBodyText!));
            blocks.Add(new RequestHistoryBlock(RequestHistoryBlockEnum.ResponseBody, "Response Body", String.IsNullOrEmpty(d?.ResponseBodyText) ? "(empty)" : d!.ResponseBodyText!));
            return blocks;
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (Scope.HandleKey(key)) return true;
            if (key.Code == KeyCode.Delete && Record != null)
            {
                DeleteRequested?.Invoke(this, EventArgs.Empty);
                return true;
            }

            if (key.Code == KeyCode.Character && key.Modifiers == KeyModifiers.None && Record != null)
            {
                if (key.Rune == 'r')
                {
                    ReplayRequested?.Invoke(this, EventArgs.Empty);
                    return true;
                }

                if (key.Rune == 'y')
                {
                    CopyRequested?.Invoke(this, EventArgs.Empty);
                    return true;
                }
            }

            return false;
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            SurfaceText.FillRect(surface, new Rect(0, 0, width, height), Theme.Text);
            if (height < 3) return;
            string title = Record != null ? Record.Entry.Method + " " + Record.Entry.Route : T("Request Detail");
            SurfaceText.Draw(surface, 0, 0, title, Theme.Muted, width);
            Scope.RenderChild(surface, _Buttons, new Rect(0, 1, width, 1));
            Scope.RenderChild(surface, View, new Rect(0, 3, width, Math.Max(1, height - 3)));
        }

        #endregion

        #region Private-Methods

        private void AddBody(string? body)
        {
            if (String.IsNullOrEmpty(body))
            {
                View.AddText("(empty)", true);
                return;
            }

            if (RequestHistoryFormat.IsJson(body)) View.AddCode(RequestHistoryFormat.PrettyJson(body!), "json");
            else View.AddCode(body!);
        }

        #endregion
    }
}

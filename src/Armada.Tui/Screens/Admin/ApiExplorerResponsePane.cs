namespace Armada.Tui.Screens.Admin
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using Armada.Tui.Screens.Activity;
    using Armada.Tui.Text;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;

    /// <summary>
    /// The API Explorer's Response card: status, content type, duration, and size, the Preview / Body / Headers /
    /// Code views, and the curl / fetch / C# snippet tabs. Before a response arrives, the Code view shows the snippets
    /// for the request as currently built. Not thread-safe.
    /// </summary>
    public class ApiExplorerResponsePane : ContainerWidget
    {
        #region Public-Members

        /// <summary>
        /// Last response, or null.
        /// </summary>
        public ApiExplorerResponse? Response { get; private set; } = null;

        /// <summary>
        /// View tabs (preview, body, headers, code).
        /// </summary>
        public TabStrip Tabs { get; } = new TabStrip();

        /// <summary>
        /// Snippet tabs (curl, fetch, csharp).
        /// </summary>
        public TabStrip CodeTabs { get; } = new TabStrip();

        /// <summary>
        /// Content view.
        /// </summary>
        public RequestHistorySectionView View { get; } = new RequestHistorySectionView();

        /// <summary>
        /// Supplies the request as currently built (for the Code view before sending), or null.
        /// </summary>
        public Func<ApiExplorerRequestPreview?>? CurrentRequest { get; set; } = null;

        #endregion

        #region Private-Members

        private string _LastCodeSignature = "";
        private string _LastView = "preview|curl";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ApiExplorerResponsePane()
        {
            Tabs.Add("preview", "Preview");
            Tabs.Add("body", "Body");
            Tabs.Add("headers", "Headers");
            Tabs.Add("code", "Code");
            Tabs.SelectKey("preview");
            foreach (string tab in ApiExplorerSpec.CodeTabs) CodeTabs.Add(tab, tab);
            CodeTabs.SelectKey("curl");
            Tabs.SelectedChanged += (s, e) => Rebuild();
            CodeTabs.SelectedChanged += (s, e) => Rebuild();
            AddChild(Tabs);
            AddChild(CodeTabs);
            AddChild(View);
            Rebuild();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Show a response (resets to the Preview view and the curl snippet, like the dashboard).
        /// </summary>
        /// <param name="response">Response, or null to clear.</param>
        public void SetResponse(ApiExplorerResponse? response)
        {
            Response = response;
            Tabs.SelectKey("preview");
            CodeTabs.SelectKey("curl");
            Rebuild();
        }

        /// <summary>
        /// Switch the view (preview, body, headers, code) and optionally the snippet (curl, fetch, csharp).
        /// </summary>
        /// <param name="view">View key.</param>
        /// <param name="code">Snippet key, or null to keep.</param>
        public void SelectView(string view, string? code = null)
        {
            Tabs.SelectKey(view);
            if (code != null) CodeTabs.SelectKey(code);
            Rebuild();
        }

        /// <summary>
        /// The text of the current view (what Copy copies).
        /// </summary>
        /// <returns>Text.</returns>
        public string CurrentText()
        {
            string tab = Tabs.SelectedKey ?? "preview";
            if (tab == "code")
            {
                ApiExplorerRequestPreview? request = Response?.Request ?? CurrentRequest?.Invoke();
                if (request == null) return "";
                return ApiExplorerSpec.Snippets(request)[CodeTabs.SelectedKey ?? "curl"];
            }

            if (Response == null) return "";
            if (tab == "headers")
            {
                Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, string> h in Response.Headers) map[h.Key] = h.Value;
                return System.Text.Json.JsonSerializer.Serialize(map, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            }

            return ApiExplorerSpec.Prettify(Response.Body, Response.ContentType);
        }

        /// <summary>
        /// Status line ("200 OK  application/json  12.34 ms  1.2 KB"), or empty.
        /// </summary>
        /// <returns>Text.</returns>
        public string MetaText()
        {
            if (Response == null) return "";
            return Response.Status.ToString(CultureInfo.InvariantCulture) + " " + Response.StatusText + (Response.Ok ? "" : " (!)") + "   "
                + (String.IsNullOrEmpty(Response.ContentType) ? "n/a" : Response.ContentType) + "   "
                + RequestHistoryFormat.Ms(Response.DurationMs) + "   " + RequestHistoryFormat.Bytes(Response.SizeBytes);
        }

        /// <summary>
        /// Rebuild the content view.
        /// </summary>
        public void Rebuild()
        {
            View.Clear();
            string tab = Tabs.SelectedKey ?? "preview";
            _LastView = tab + "|" + (CodeTabs.SelectedKey ?? "curl");
            if (tab == "code")
            {
                ApiExplorerRequestPreview? request = Response?.Request ?? CurrentRequest?.Invoke();
                if (request == null)
                {
                    View.AddText(T("Send a request to inspect the live response here."), true);
                    return;
                }

                string kind = CodeTabs.SelectedKey ?? "curl";
                string code = ApiExplorerSpec.Snippets(request)[kind];
                _LastCodeSignature = code;
                View.AddCode(code, kind == "csharp" ? "csharp" : "");
                return;
            }

            if (Response == null)
            {
                View.AddText(T("Send a request to inspect the live response here."), true);
                return;
            }

            if (tab == "headers")
            {
                foreach (KeyValuePair<string, string> h in Response.Headers) View.AddPair(h.Key, h.Value);
                return;
            }

            string text = ApiExplorerSpec.Prettify(Response.Body, Response.ContentType);
            bool json = Response.ContentType.Contains("application/json", StringComparison.Ordinal) && RequestHistoryFormat.IsJson(Response.Body);
            View.AddCode(String.IsNullOrEmpty(text) ? "(empty)" : text, json ? "json" : "");
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            return Scope.HandleKey(key);
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            SurfaceText.FillRect(surface, new Rect(0, 0, width, height), Theme.Text);
            if (height < 3 || width < 8) return;
            if (!String.Equals(_LastView, (Tabs.SelectedKey ?? "preview") + "|" + (CodeTabs.SelectedKey ?? "curl"), StringComparison.Ordinal)) Rebuild();
            if (Tabs.SelectedKey == "code" && Response == null)
            {
                ApiExplorerRequestPreview? request = CurrentRequest?.Invoke();
                string signature = request != null ? ApiExplorerSpec.Snippets(request)[CodeTabs.SelectedKey ?? "curl"] : "";
                if (!String.Equals(signature, _LastCodeSignature, StringComparison.Ordinal)) Rebuild();
            }

            int x = SurfaceText.Draw(surface, 0, 0, T("Response"), Theme.Accent.WithAttribute(CellAttributes.Bold, true), width);
            string meta = MetaText();
            if (meta.Length > 0) SurfaceText.Draw(surface, x + 2, 0, meta, Response != null && Response.Ok ? Theme.Success : Theme.Error, Math.Max(0, width - x - 2));
            Scope.RenderChild(surface, Tabs, new Rect(0, 1, width, 1));
            int top = 2;
            CodeTabs.Visible = Tabs.SelectedKey == "code";
            if (CodeTabs.Visible)
            {
                Scope.RenderChild(surface, CodeTabs, new Rect(2, 2, Math.Max(1, width - 2), 1));
                top = 3;
            }

            Scope.RenderChild(surface, View, new Rect(0, top, width, Math.Max(1, height - top)));
        }

        #endregion
    }
}

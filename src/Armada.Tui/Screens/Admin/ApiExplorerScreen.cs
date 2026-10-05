namespace Armada.Tui.Screens.Admin
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Net.Http;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Client.Models;
    using Armada.Core.Models;
    using Armada.Tui.Input;
    using Armada.Tui.Routing;
    using Armada.Tui.Screens.Activity;
    using Armada.Tui.Screens.Kit;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;
    using TUIKit.Input;

    /// <summary>
    /// API Explorer (dashboard <c>ApiExplorer.tsx</c>; routes <c>/api-explorer</c> and
    /// <c>/api-explorer/:operationId</c>): loads the live OpenAPI document, filters operations by category and text,
    /// builds a request (path, query, and header parameters with their initial values, and a JSON body seeded from the
    /// schema, editable in <c>$EDITOR</c> with <c>Ctrl+E</c>), previews it as a URL and as curl, fetch, and C#
    /// snippets, sends it with the session's credentials (<c>F9</c>) and aborts it (<c>F8</c>), and shows the response
    /// (status, content type, duration, size; Preview, Body, Headers, Code), which can be copied (<c>y</c>) or saved
    /// to a file. <c>?replay=&lt;requestId&gt;</c> prefills a captured request from API Requests. Not thread-safe.
    /// </summary>
    public class ApiExplorerScreen : StackScreen
    {
        #region Public-Members

        /// <summary>
        /// Header.
        /// </summary>
        public ScreenHeader Header { get; }

        /// <summary>
        /// Operation picker.
        /// </summary>
        public FilterStrip Picker { get; } = new FilterStrip();

        /// <summary>
        /// Category picker.
        /// </summary>
        public SelectField<string> CategoryField { get; } = new SelectField<string>();

        /// <summary>
        /// Text filter.
        /// </summary>
        public InputField FilterField { get; } = new InputField();

        /// <summary>
        /// Operation picker.
        /// </summary>
        public SelectField<string> OperationField { get; } = new SelectField<string>();

        /// <summary>
        /// Builder and response.
        /// </summary>
        public ApiExplorerWorkspace Workspace { get; }

        /// <summary>
        /// Response pane.
        /// </summary>
        public ApiExplorerResponsePane ResponsePane { get; } = new ApiExplorerResponsePane();

        /// <summary>
        /// Parsed document, or null while loading.
        /// </summary>
        public ApiExplorerDocument? Document { get; private set; } = null;

        /// <summary>
        /// All operations.
        /// </summary>
        public List<ApiExplorerOperation> Operations { get; private set; } = new List<ApiExplorerOperation>();

        /// <summary>
        /// Selected operation, or null.
        /// </summary>
        public ApiExplorerOperation? Selected { get; private set; } = null;

        /// <summary>
        /// Path parameter fields by name.
        /// </summary>
        public Dictionary<string, InputField> PathFields { get; } = new Dictionary<string, InputField>(StringComparer.Ordinal);

        /// <summary>
        /// Query parameter fields by name.
        /// </summary>
        public Dictionary<string, InputField> QueryFields { get; } = new Dictionary<string, InputField>(StringComparer.Ordinal);

        /// <summary>
        /// Header fields by name.
        /// </summary>
        public Dictionary<string, InputField> HeaderFields { get; } = new Dictionary<string, InputField>(StringComparer.Ordinal);

        /// <summary>
        /// Body editor, or null when the operation has no body.
        /// </summary>
        public MultilineField? BodyField { get; private set; } = null;

        /// <summary>
        /// True while a request is in flight.
        /// </summary>
        public bool Sending
        {
            get { return _SendCts != null; }
        }

        /// <summary>
        /// True while the document loads.
        /// </summary>
        public bool Loading { get; private set; } = true;

        /// <inheritdoc />
        public override IReadOnlyList<KeyValuePair<string, string>> TypingHints
        {
            get
            {
                return new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("F9", "Send Request"),
                    new KeyValuePair<string, string>("F8", "Abort"),
                    new KeyValuePair<string, string>("Ctrl+E", "Edit body")
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
                    new KeyValuePair<string, string>("F9", "Send Request"),
                    new KeyValuePair<string, string>("F8", "Abort"),
                    new KeyValuePair<string, string>("y", "Copy"),
                    new KeyValuePair<string, string>("Ctrl+E", "Edit body"),
                };
            }
        }

        #endregion

        #region Private-Members

        private static readonly HashSet<string> _HiddenHeaderParams = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "x-token", "authorization", "content-type" };
        private static readonly HashSet<string> _UnsendableHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "host", "content-length", "connection", "keep-alive", "transfer-encoding", "accept-encoding", "upgrade", "expect", "te", "trailer", "cookie", "content-type",
        };

        private readonly Button _Send;
        private readonly Button _Abort;
        private readonly Button _Copy;
        private readonly Button _Save;
        private readonly TextBlock _PreviewBlock = new TextBlock("", t => t.Code);
        private CancellationTokenSource? _SendCts = null;
        private RequestHistoryReplay? _PendingReplay = null;
        private string? _RouteOperationId = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Context.</param>
        public ApiExplorerScreen(RouteMatch route, TuiContext context)
            : base(route, context)
        {
            Header = new ScreenHeader("API Explorer", "Browse the live OpenAPI document, execute authenticated requests, inspect responses, and replay captured traffic.");
            Header.AddButton("OpenAPI JSON", () => OpenServerUrl("/openapi.json"));
            Header.AddButton("Swagger", () => OpenServerUrl("/swagger"));
            _Send = Header.AddButton("Send Request", Send, "F9");
            _Abort = Header.AddButton("Abort", Abort, "F8");
            _Copy = Header.AddButton("Copy", CopyResponse, "y");
            _Save = Header.AddButton("Save Response", SaveResponse);
            _Abort.Visible = false;
            AddFixed(Header, w => Header.HeightFor(w));

            CategoryField.ModalHost = context.Modals;
            CategoryField.PickerTitle = "Category";
            OperationField.ModalHost = context.Modals;
            OperationField.PickerTitle = "Operation";
            FilterField.Placeholder = "Filter by path or summary";
            Picker.Add("Category", CategoryField, 16);
            Picker.Add("Filter", FilterField, 24);
            Picker.Add("Operation", OperationField, 60);
            CategoryField.ValueChanged += (s, e) => UpdateOperationOptions();
            FilterField.ValueChanged += (s, e) => UpdateOperationOptions();
            OperationField.ValueChanged += (s, e) =>
            {
                if (!String.IsNullOrEmpty(e.NewValue) && (Selected == null || Selected.Id != e.NewValue)) SelectOperation(e.NewValue!, null);
            };
            AddFixed(Picker, w => Picker.HeightFor(w));

            ResponsePane.CurrentRequest = BuildPreview;
            Workspace = new ApiExplorerWorkspace(new TextBlock("Loading OpenAPI document...", t => t.Muted), ResponsePane);
            AddFill(Workspace);

            if (route.Parameters.TryGetValue("operationId", out string? opId) && !String.IsNullOrEmpty(opId)) _RouteOperationId = Uri.UnescapeDataString(opId);
            Banner = context.Loc.T("Loading OpenAPI document...");
            BannerStyle = t => t.Muted;
            Scope.Focus(Picker);
            LoadSpec();
            if (route.Query.TryGetValue("replay", out string? replayId) && !String.IsNullOrEmpty(replayId)) LoadReplay(replayId);
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override IEnumerable<ArmadaCommand> Commands()
        {
            List<ArmadaCommand> commands = new List<ArmadaCommand>();
            commands.Add(Cmd(ScreenKey + ".send", "Send Request", Send, () => Selected != null && !Sending && !Loading, "f9"));
            commands.Add(Cmd(ScreenKey + ".abort", "Abort", Abort, () => Sending, "f8"));
            commands.Add(Cmd(ScreenKey + ".copy", "Copy", CopyResponse, () => ResponsePane.CurrentText().Length > 0, "y"));
            commands.Add(Cmd(ScreenKey + ".save", "Save Response", SaveResponse, () => ResponsePane.Response != null));
            commands.Add(Cmd(ScreenKey + ".openapi", "OpenAPI JSON", () => OpenServerUrl("/openapi.json"), null));
            commands.Add(Cmd(ScreenKey + ".swagger", "Swagger", () => OpenServerUrl("/swagger"), null));
            commands.Add(Cmd(ScreenKey + ".filter", "Filter", () => Scope.Focus(Picker), null, "/"));
            return commands;
        }

        /// <inheritdoc />
        public override Action? RefreshAction()
        {
            return null;
        }

        /// <summary>
        /// Select an operation and reset the builder to its initial values (or to a pending replay).
        /// </summary>
        /// <param name="operationId">Operation id.</param>
        /// <param name="replay">Replay values, or null.</param>
        public void SelectOperation(string operationId, RequestHistoryReplay? replay)
        {
            ApiExplorerOperation? op = Operations.FirstOrDefault(o => o.Id == operationId);
            if (op == null) return;
            Selected = op;
            OperationField.SetValue(op.Id);
            BuildBuilder(op, replay);
            ResponsePane.SetResponse(null);
        }

        /// <summary>
        /// The request as currently built (the dashboard's <c>requestPreview</c>), or null without an operation.
        /// </summary>
        /// <returns>Preview.</returns>
        public ApiExplorerRequestPreview? BuildPreview()
        {
            ApiExplorerOperation? op = Selected;
            if (op == null) return null;
            string path = op.Path;
            foreach (KeyValuePair<string, InputField> p in PathFields)
            {
                string value = p.Value.Value;
                path = path.Replace("{" + p.Key + "}", Uri.EscapeDataString(value.Length > 0 ? value : "{" + p.Key + "}"));
            }

            List<string> query = new List<string>();
            foreach (KeyValuePair<string, InputField> q in QueryFields)
            {
                if (q.Value.Value.Length > 0) query.Add(Uri.EscapeDataString(q.Key) + "=" + Uri.EscapeDataString(q.Value.Value));
            }

            string pathAndQuery = path + (query.Count > 0 ? "?" + String.Join("&", query) : "");
            ApiExplorerRequestPreview preview = new ApiExplorerRequestPreview();
            preview.Method = op.Method;
            preview.PathAndQuery = pathAndQuery;
            preview.Url = Context.Client.BaseUrl.TrimEnd('/') + pathAndQuery;
            KeyValuePair<string, string>? auth = AuthHeader();
            if (auth.HasValue) preview.Headers.Add(auth.Value);
            foreach (KeyValuePair<string, InputField> h in HeaderFields)
            {
                if (h.Value.Value.Length > 0 && !String.Equals(h.Key, "x-token", StringComparison.OrdinalIgnoreCase)) preview.Headers.Add(new KeyValuePair<string, string>(h.Key, h.Value.Value));
            }

            string body = BodyField != null ? BodyField.Value.Trim() : "";
            preview.ContentType = String.IsNullOrEmpty(op.BodyContentType) ? "application/json" : op.BodyContentType;
            if (op.HasBody && body.Length > 0) preview.Headers.Add(new KeyValuePair<string, string>("Content-Type", preview.ContentType));
            preview.Body = body;
            return preview;
        }

        /// <summary>
        /// Send the built request.
        /// </summary>
        public void Send()
        {
            ApiExplorerRequestPreview? preview = BuildPreview();
            if (preview == null || Sending || Loading) return;
            CancellationTokenSource cts = new CancellationTokenSource();
            _SendCts = cts;
            _Abort.Visible = true;
            _Send.Label = "Running...";
            _Send.Enabled = false;
            Banner = "";
            KeyValuePair<string, string>? auth = AuthHeader();
            Dictionary<string, string> extra = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> h in preview.Headers)
            {
                if (auth.HasValue && String.Equals(h.Key, auth.Value.Key, StringComparison.OrdinalIgnoreCase)) continue;
                if (_UnsendableHeaders.Contains(h.Key)) continue;
                extra[h.Key] = h.Value;
            }

            ArmadaClient client = Context.Client;
            Task.Run(async () =>
            {
                ApiExplorerResponse? result = null;
                string? error = null;
                bool aborted = false;
                Stopwatch sw = Stopwatch.StartNew();
                try
                {
                    HttpContent? content = preview.Body.Length > 0 ? new StringContent(preview.Body, Encoding.UTF8, preview.ContentType) : null;
                    using (HttpResponseMessage response = await client.SendRawAsync(new HttpMethod(preview.Method.ToUpperInvariant()), preview.PathAndQuery, content, extra, null, cts.Token).ConfigureAwait(false))
                    {
                        result = await ReadResponseAsync(response, preview, cts.Token).ConfigureAwait(false);
                        result.DurationMs = sw.Elapsed.TotalMilliseconds;
                    }
                }
                catch (OperationCanceledException)
                {
                    aborted = true;
                }
                catch (ArmadaApiException ex)
                {
                    if (cts.IsCancellationRequested) aborted = true;
                    else error = ex.Message;
                }
                catch (HttpRequestException ex)
                {
                    error = ex.Message;
                }

                Context.Dispatcher.Post(() =>
                {
                    _SendCts = null;
                    _Abort.Visible = false;
                    _Send.Label = "Send Request";
                    _Send.Enabled = true;
                    if (aborted) return;
                    if (result != null)
                    {
                        ResponsePane.SetResponse(result);
                        ScreenOps.Toast(Context, NotificationSeverityEnum.Success, "Request completed with status {{status}}.", LocalizationArgs.Of("status", result.Status));
                    }
                    else
                    {
                        ShowError(error ?? Context.Loc.T("Request failed."));
                    }
                });
            });
        }

        /// <summary>
        /// Abort the request in flight.
        /// </summary>
        public void Abort()
        {
            _SendCts?.Cancel();
        }

        /// <inheritdoc />
        public override void OnDeactivated()
        {
            _SendCts?.Cancel();
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override void BeforeRender(int width, int height)
        {
            _Copy.Enabled = ResponsePane.CurrentText().Length > 0;
            _Save.Enabled = ResponsePane.Response != null;
            _Send.Enabled = Selected != null && !Sending && !Loading;
            ApiExplorerRequestPreview? preview = BuildPreview();
            _PreviewBlock.Text = preview != null ? preview.Method.ToUpperInvariant() + " " + preview.Url : "";
        }

        #endregion

        #region Private-Methods

        private static ArmadaCommand Cmd(string id, string title, Action handler, Func<bool>? enabled, params string[] gestures)
        {
            ArmadaCommand command = new ArmadaCommand(id, title, CommandMenuEnum.Actions, handler, gestures);
            command.IsEnabled = enabled;
            return command;
        }

        private void LoadSpec()
        {
            Loading = true;
            ScreenOps.Quiet(Context, async () =>
            {
                try
                {
                    ArmadaRawJson? raw = await Context.Client.GetOpenApiDocumentAsync().ConfigureAwait(false);
                    ApiExplorerDocument doc = ApiExplorerSpec.Parse(raw?.Json ?? "{}");
                    return new KeyValuePair<ApiExplorerDocument?, string?>(doc, null);
                }
                catch (ArmadaApiException ex)
                {
                    return new KeyValuePair<ApiExplorerDocument?, string?>(null, Context.Loc.T("Failed to load OpenAPI document ({{status}})", LocalizationArgs.Of("status", ex.StatusCode)) + ": " + ex.Message);
                }
                catch (System.Text.Json.JsonException)
                {
                    return new KeyValuePair<ApiExplorerDocument?, string?>(null, Context.Loc.T("Failed to load OpenAPI document."));
                }
            }, result =>
            {
                Loading = false;
                if (result.Key == null)
                {
                    ShowError(result.Value ?? Context.Loc.T("Failed to load OpenAPI document."));
                    Workspace.SetBuilder(new TextBlock("No operations are available in the current OpenAPI document.", t => t.Muted));
                    return;
                }

                Banner = "";
                Document = result.Key;
                Operations = ApiExplorerSpec.Operations(result.Key);
                List<SelectOption<string>> categories = ApiExplorerSpec.Tags(Operations).Select(t => new SelectOption<string>(t, t == "All" ? Context.Loc.T("All") : t)).ToList();
                CategoryField.Options = categories;
                CategoryField.SetValue("All");
                UpdateOperationOptions();
                if (Operations.Count == 0)
                {
                    Workspace.SetBuilder(new TextBlock("No operations are available in the current OpenAPI document.", t => t.Muted));
                    return;
                }

                if (_PendingReplay != null)
                {
                    ApplyReplay();
                    return;
                }

                string? initial = _RouteOperationId != null && Operations.Any(o => o.Id == _RouteOperationId) ? _RouteOperationId : null;
                List<ApiExplorerOperation> filtered = Filtered();
                SelectOperation(initial ?? (filtered.Count > 0 ? filtered[0].Id : Operations[0].Id), null);
            });
        }

        private void LoadReplay(string requestId)
        {
            ScreenOps.Quiet(Context, async () =>
            {
                try
                {
                    RequestHistoryRecord? record = await Context.Client.GetRequestHistoryEntryAsync(requestId).ConfigureAwait(false);
                    return record;
                }
                catch (ArmadaApiException)
                {
                    return null;
                }
            }, record =>
            {
                if (record == null)
                {
                    ShowError(Context.Loc.T("Failed to prepare replay request."));
                    return;
                }

                _PendingReplay = RequestHistoryReplay.From(record);
                if (!Loading && Operations.Count > 0) ApplyReplay();
            });
        }

        private void ApplyReplay()
        {
            RequestHistoryReplay? replay = _PendingReplay;
            _PendingReplay = null;
            if (replay == null) return;
            ApiExplorerOperation? op = ApiExplorerSpec.FindForReplay(Operations, replay.Method, replay.Route, replay.RouteTemplate, replay.PathValues, out Dictionary<string, string> pathValues);
            if (op == null)
            {
                ShowError(Context.Loc.T("No matching OpenAPI operation was found for the replay request."));
                if (Selected == null && Operations.Count > 0) SelectOperation(Operations[0].Id, null);
                return;
            }

            RequestHistoryReplay resolved = new RequestHistoryReplay();
            resolved.Method = replay.Method;
            resolved.Route = replay.Route;
            resolved.RouteTemplate = replay.RouteTemplate;
            resolved.PathValues = pathValues.ToList();
            resolved.QueryValues = replay.QueryValues;
            resolved.HeaderValues = replay.HeaderValues.Where(h => !String.Equals(h.Key, "x-token", StringComparison.OrdinalIgnoreCase) && !String.Equals(h.Key, "authorization", StringComparison.OrdinalIgnoreCase)).ToList();
            resolved.BodyValue = replay.BodyValue;
            CategoryField.SetValue("All");
            FilterField.Value = "";
            UpdateOperationOptions();
            SelectOperation(op.Id, resolved);
            Banner = Context.Loc.T("Replaying") + " " + replay.Method + " " + replay.Route;
            BannerStyle = t => t.Info;
        }

        private List<ApiExplorerOperation> Filtered()
        {
            return ApiExplorerSpec.Filter(Operations, CategoryField.Value ?? "All", FilterField.Value);
        }

        private void UpdateOperationOptions()
        {
            List<ApiExplorerOperation> filtered = Filtered();
            List<SelectOption<string>> options = filtered.Select(o => new SelectOption<string>(o.Id, o.Method.ToUpperInvariant() + " " + o.Path + (o.Summary.Length > 0 ? " -- " + o.Summary : ""))).ToList();
            if (options.Count == 0) options.Add(new SelectOption<string>("", Context.Loc.T("No operations match the current filter")));
            if (Selected != null && !filtered.Contains(Selected))
            {
                options.Insert(0, new SelectOption<string>(Selected.Id, Selected.Method.ToUpperInvariant() + " " + Selected.Path + (Selected.Summary.Length > 0 ? " -- " + Selected.Summary : "")));
            }

            OperationField.Options = options;
            if (Selected != null) OperationField.SetValue(Selected.Id);
            else if (filtered.Count > 0 && !Loading && Operations.Count > 0 && _PendingReplay == null) SelectOperation(filtered[0].Id, null);
        }

        private void BuildBuilder(ApiExplorerOperation op, RequestHistoryReplay? replay)
        {
            PathFields.Clear();
            QueryFields.Clear();
            HeaderFields.Clear();
            BodyField = null;
            FormView form = new FormView();
            form.ShowButtons = false;
            form.AddSection(op.Summary.Length > 0 ? op.Summary : "Request Builder");
            form.AddField("Method", new TextBlock(op.Method.ToUpperInvariant() + " " + op.Path, t => t.Accent) { Translate = false });
            form.AddField("Category", new TextBlock(op.Tag) { Translate = false });
            string about = op.Description.Length > 0 ? op.Description : op.Path;
            form.AddField("Description", new TextBlock(about, t => t.Muted) { Translate = false });

            Dictionary<string, string> pathReplay = ToMap(replay?.PathValues);
            Dictionary<string, string> queryReplay = ToMap(replay?.QueryValues);
            Dictionary<string, string> headerReplay = ToMap(replay?.HeaderValues);
            AddParameterSection(form, "Path Parameters", op.Parameters.Where(p => p.In == "path").ToList(), PathFields, replay != null ? pathReplay : null);
            AddParameterSection(form, "Query Parameters", op.Parameters.Where(p => p.In == "query").ToList(), QueryFields, replay != null ? queryReplay : null);
            List<ApiExplorerParameterSource> headerParams = op.Parameters.Where(p => p.In == "header" && !_HiddenHeaderParams.Contains(p.Name)).ToList();
            AddParameterSection(form, "Headers", headerParams, HeaderFields, replay != null ? headerReplay : null);
            if (replay != null)
            {
                foreach (KeyValuePair<string, string> extra in headerReplay.Where(h => !HeaderFields.ContainsKey(h.Key)))
                {
                    InputField field = form.AddField(extra.Key, new InputField());
                    field.Value = extra.Value;
                    HeaderFields[extra.Key] = field;
                }

                foreach (KeyValuePair<string, string> extra in queryReplay.Where(q => !QueryFields.ContainsKey(q.Key)))
                {
                    QueryFields[extra.Key] = new InputField { Value = extra.Value };
                }
            }

            if (op.HasBody)
            {
                form.AddSection("Request Body");
                MultilineField body = new MultilineField();
                body.ExternalEditor = text => Context.External.EditTextAsync(text, ".json");
                body.Dispatcher = Context.Dispatcher;
                body.Value = replay != null ? replay.BodyValue : ApiExplorerSpec.ExampleBody(op, Document, Context.Clock.UtcNow);
                form.AddField(String.IsNullOrEmpty(op.BodyContentType) ? "application/json" : op.BodyContentType, body, "Ctrl+E opens the body in $EDITOR.", 10);
                BodyField = body;
            }

            form.AddSection("Request Preview");
            form.AddField("URL", _PreviewBlock, null, 2);
            _PreviewBlock.Translate = false;
            form.MarkClean();
            Workspace.SetBuilder(form);
        }

        private void AddParameterSection(FormView form, string title, List<ApiExplorerParameterSource> parameters, Dictionary<string, InputField> fields, Dictionary<string, string>? replayValues)
        {
            form.AddSection(Context.Loc.T(title) + " (" + parameters.Count + ")");
            if (parameters.Count == 0)
            {
                form.AddField("", new TextBlock("No parameters", t => t.Muted));
                return;
            }

            foreach (ApiExplorerParameterSource p in parameters)
            {
                InputField field = new InputField();
                field.Placeholder = String.IsNullOrEmpty(p.Description) ? p.Name : p.Description!;
                if (replayValues != null) field.Value = replayValues.TryGetValue(p.Name, out string? v) ? v : "";
                else field.Value = ApiExplorerSpec.InitialValue(p, Document);
                form.AddField(p.Name + (p.Required ? " *" : ""), field);
                fields[p.Name] = field;
            }
        }

        private static Dictionary<string, string> ToMap(IEnumerable<KeyValuePair<string, string>>? pairs)
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> kvp in pairs ?? Enumerable.Empty<KeyValuePair<string, string>>()) map[kvp.Key] = kvp.Value;
            return map;
        }

        private KeyValuePair<string, string>? AuthHeader()
        {
            ArmadaClientOptions options = Context.Client.Options;
            if (!String.IsNullOrEmpty(options.Token)) return new KeyValuePair<string, string>("X-Token", options.Token!);
            if (!String.IsNullOrEmpty(options.BearerToken)) return new KeyValuePair<string, string>("Authorization", "Bearer " + options.BearerToken);
            if (!String.IsNullOrEmpty(options.ApiKey)) return new KeyValuePair<string, string>("X-Api-Key", options.ApiKey!);
            return null;
        }

        private static async Task<ApiExplorerResponse> ReadResponseAsync(HttpResponseMessage response, ApiExplorerRequestPreview request, CancellationToken token)
        {
            ApiExplorerResponse result = new ApiExplorerResponse();
            result.Request = request;
            result.Ok = response.IsSuccessStatusCode;
            result.Status = (int)response.StatusCode;
            result.StatusText = response.ReasonPhrase ?? "";
            foreach (KeyValuePair<string, IEnumerable<string>> h in response.Headers) result.Headers.Add(new KeyValuePair<string, string>(h.Key.ToLowerInvariant(), String.Join(", ", h.Value)));
            if (response.Content != null)
            {
                foreach (KeyValuePair<string, IEnumerable<string>> h in response.Content.Headers) result.Headers.Add(new KeyValuePair<string, string>(h.Key.ToLowerInvariant(), String.Join(", ", h.Value)));
            }

            result.ContentType = response.Content?.Headers.ContentType?.ToString() ?? "";
            if (response.Content == null) return result;
            if (result.ContentType.Contains("application/json", StringComparison.Ordinal) || result.ContentType.StartsWith("text/", StringComparison.Ordinal))
            {
                result.Body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                result.SizeBytes = Encoding.UTF8.GetByteCount(result.Body);
            }
            else
            {
                byte[] bytes = await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);
                result.SizeBytes = bytes.LongLength;
                string type = String.IsNullOrEmpty(result.ContentType) ? "application/octet-stream" : result.ContentType;
                result.Body = bytes.Length == 0 ? "" : "Binary response (" + type + ", " + bytes.Length + " bytes)";
            }

            return result;
        }

        private void ShowError(string message)
        {
            Banner = message;
            BannerStyle = t => t.Error;
        }

        private void CopyResponse()
        {
            string text = ResponsePane.CurrentText();
            if (text.Length > 0) Context.Clipboard.Copy(text, "Response");
        }

        private void SaveResponse()
        {
            ApiExplorerResponse? response = ResponsePane.Response;
            if (response == null) return;
            string ext = response.ContentType.Contains("json", StringComparison.Ordinal) ? ".json" : ".txt";
            string body = response.Body;
            PathPrompt.AskSave(Context, "Save Response", "armada-response-" + response.Status + ext, path =>
            {
                try
                {
                    string full = Context.External.SaveText(path, body);
                    ScreenOps.Toast(Context, NotificationSeverityEnum.Success, "Saved to {{path}}.", LocalizationArgs.Of("path", full));
                }
                catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
                {
                    ScreenOps.Toast(Context, NotificationSeverityEnum.Error, "Failed to save file: {{message}}", LocalizationArgs.Of("message", ex.Message));
                }
            });
        }

        private void OpenServerUrl(string path)
        {
            string url = Context.Client.BaseUrl.TrimEnd('/') + path;
            if (!Context.External.OpenUrl(url)) Context.Clipboard.Copy(url, "URL");
        }

        #endregion
    }
}

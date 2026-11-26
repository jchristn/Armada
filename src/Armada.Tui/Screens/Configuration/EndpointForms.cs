namespace Armada.Tui.Screens.Configuration
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Modals;
    using Armada.Tui.Screens.Entities;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// Model endpoint dialogs (dashboard <c>Endpoints.tsx</c>): the create/edit form (name, kind, provider, region,
    /// project, access key ID, base URL, model or deployment, API version, the write-only credential where blank keeps
    /// the stored secret, dimensionality, timeout, scope, enabled; provider-specific requirements validated on save),
    /// the Validate Now result, and the health detail (status, uptime, history span, consecutive OK and failures,
    /// last error, the bucketed history, and timestamps). Use on the UI loop.
    /// </summary>
    public static class EndpointForms
    {
        #region Public-Methods

        /// <summary>
        /// Reason a provider and kind combination is unsupported (mirrors the server guard), or null.
        /// </summary>
        /// <param name="provider">Provider.</param>
        /// <param name="kind">Kind.</param>
        /// <returns>English reason or null.</returns>
        public static string? UnsupportedReason(ModelProviderEnum provider, ModelEndpointKindEnum kind)
        {
            if (kind == ModelEndpointKindEnum.Embedding && provider == ModelProviderEnum.Anthropic) return "Anthropic does not provide an embeddings API. Choose Inference or a different provider.";
            if (kind == ModelEndpointKindEnum.Inference && provider == ModelProviderEnum.VoyageAI) return "Voyage AI provides embeddings only. Choose Embedding or a different provider.";
            return null;
        }

        /// <summary>
        /// What the secret is and how it is sent, for a provider and authentication type.
        /// </summary>
        /// <param name="provider">Provider.</param>
        /// <param name="authType">Authentication type.</param>
        /// <returns>English hint.</returns>
        public static string SecretHint(ModelProviderEnum provider, ModelEndpointAuthTypeEnum authType)
        {
            switch (authType)
            {
                case ModelEndpointAuthTypeEnum.None: return "No credential is sent.";
                case ModelEndpointAuthTypeEnum.Bearer: return "Bearer token, sent as Authorization: Bearer <token>. Blank keeps the stored secret.";
                case ModelEndpointAuthTypeEnum.Basic: return "Password, sent with the username as Authorization: Basic. Blank keeps the stored secret.";
                case ModelEndpointAuthTypeEnum.Header: return "Header value, sent in the header named above. Blank keeps the stored secret.";
                case ModelEndpointAuthTypeEnum.QueryParameter: return "Value of the query parameter named above, appended to every request URL. Blank keeps the stored secret.";
            }

            switch (provider)
            {
                case ModelProviderEnum.Anthropic: return "API key, sent as x-api-key. Blank keeps the stored secret.";
                case ModelProviderEnum.Gemini: return "API key, sent as x-goog-api-key. Blank keeps the stored secret.";
                case ModelProviderEnum.AzureOpenAI: return "API key, sent as api-key. Blank keeps the stored secret.";
                case ModelProviderEnum.VertexAI: return "Service account JSON, exchanged for an OAuth token. Blank keeps the stored secret.";
                case ModelProviderEnum.Bedrock: return "AWS secret access key, used to sign requests. Blank keeps the stored secret.";
                default: return "API key or token, sent as Authorization: Bearer <key>. Blank keeps the stored secret.";
            }
        }

        /// <summary>
        /// Parse the extra headers text: one <c>Name: value</c> per line; a name with no value keeps the stored value.
        /// </summary>
        /// <param name="text">Text.</param>
        /// <param name="error">English error, or null.</param>
        /// <returns>Headers, or null when <paramref name="error"/> is set.</returns>
        public static List<ModelEndpointHeader>? ParseHeaders(string? text, out string? error)
        {
            error = null;
            List<ModelEndpointHeader> headers = new List<ModelEndpointHeader>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in (text ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                int colon = line.IndexOf(':');
                string name = (colon < 0 ? line : line.Substring(0, colon)).Trim();
                string value = colon < 0 ? "" : line.Substring(colon + 1).Trim();
                if (name.Length == 0 || name.Contains(' '))
                {
                    error = "Each extra header line must be Name: value.";
                    return null;
                }

                if (!seen.Add(name))
                {
                    error = "Header \"" + name + "\" is listed more than once.";
                    return null;
                }

                headers.Add(new ModelEndpointHeader(name, value.Length == 0 ? null : value));
            }

            return headers;
        }

        /// <summary>
        /// Open the create/edit form.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="existing">Endpoint to edit, or null.</param>
        /// <param name="onSaved">Runs on the UI loop with the saved endpoint.</param>
        /// <returns>The dialog.</returns>
        public static FormDialog Open(TuiContext context, ModelEndpoint? existing, Action<ModelEndpoint> onSaved)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            EntityForm form = new EntityForm(context);
            InputField name = form.Text("Name", existing != null ? existing.Name : "New Endpoint", "", true);
            SelectField<string> kind = form.Enum("Kind", existing != null ? existing.Kind : ModelEndpointKindEnum.Inference);
            SelectField<string> provider = form.Enum("Provider", existing != null ? existing.Provider : ModelProviderEnum.OpenAI);
            InputField region = form.Text("Region", existing?.Region, "us-east-1 / us-central1", false, "Required for Vertex AI and Bedrock.");
            InputField project = form.Text("Project", existing?.Project, "my-gcp-project", false, "Required for Vertex AI.");
            InputField accessKeyId = form.Text("Access Key ID", existing?.AccessKeyId, "AKIA...", false, "Required for Bedrock.");
            InputField baseUrl = form.Text("Base URL", existing?.BaseUrl, "https://api.openai.com", false, "OllamaCloud uses https://ollama.com:443 (fixed); XAI defaults to https://api.x.ai. Optional override for Vertex AI and Bedrock; the resource endpoint for Azure OpenAI.");
            InputField model = form.Text("Model", existing?.Model, "gpt-4o-mini", false, "Deployment name for Azure OpenAI; Bedrock model id for Bedrock.");
            InputField apiVersion = form.Text("API Version", existing?.ApiVersion, "2024-10-21 (default)", false, "Azure OpenAI only.");
            SelectField<string> authType = form.Enum("Authentication", existing != null ? existing.AuthType : ModelEndpointAuthTypeEnum.ProviderDefault,
                "How requests authenticate. Provider default uses the provider's own scheme; the others send the secret below as a bearer token, basic authentication, a custom header, or a query parameter. Vertex AI and Bedrock support only the provider default.");
            InputField authUsername = form.Text("Username", existing?.AuthUsername, "user", false, "Basic authentication user name; the password is the secret.");
            InputField authHeaderName = form.Text("Header Name", existing?.AuthHeaderName, "X-API-Key", false, "Header that carries the secret.");
            InputField authQueryParameter = form.Text("Query Parameter", existing?.AuthQueryParameter, "key", false, "Query string parameter that carries the secret.");
            bool hasKey = existing != null && existing.HasApiKey;
            InputField apiKey = form.Text("Secret", "", hasKey ? context.Loc.T("(unchanged - leave blank to keep stored key)") : context.Loc.T("Optional"), false,
                SecretHint(existing != null ? existing.Provider : ModelProviderEnum.OpenAI, existing != null ? existing.AuthType : ModelEndpointAuthTypeEnum.ProviderDefault));
            apiKey.Masked = true;
            TextAreaField headers = form.Area("Extra Headers", HeaderLines(existing), 3, false,
                "Sent with every request, one per line as Name: value (for example CF-Access-Client-Id: abc). A stored header shows only its name; leave its value blank to keep it. Values are shown while you type.", ".txt");
            FormRow secretRow = form.View.Rows.First(r => ReferenceEquals(r.Field, apiKey));
            void SyncAuth()
            {
                ModelProviderEnum pr = EntityForm.EnumValue(provider, ModelProviderEnum.OpenAI);
                ModelEndpointAuthTypeEnum at = EntityForm.EnumValue(authType, ModelEndpointAuthTypeEnum.ProviderDefault);
                baseUrl.Visible = pr != ModelProviderEnum.OllamaCloud;
                authUsername.Visible = at == ModelEndpointAuthTypeEnum.Basic;
                authHeaderName.Visible = at == ModelEndpointAuthTypeEnum.Header;
                authQueryParameter.Visible = at == ModelEndpointAuthTypeEnum.QueryParameter;
                apiKey.Visible = at != ModelEndpointAuthTypeEnum.None;
                secretRow.Hint = SecretHint(pr, at);
            }

            authType.ValueChanged += (s, e) => SyncAuth();
            provider.ValueChanged += (s, e) => SyncAuth();
            SyncAuth();
            InputField dimensionality = form.Number("Dimensionality", existing?.Dimensionality ?? 0, 0, 100000);
            InputField timeout = form.Number("Timeout (ms)", existing?.TimeoutMs ?? 120000, 1000, 600000);
            InferenceOptions inference = existing?.InferenceOptions ?? new InferenceOptions();
            CheckField toolsEnabled = form.Check("Enable Tool Calls", inference.ToolsEnabled && inference.AllowedTools?.Count != 0);
            CheckField readOnlyTools = form.Check("Read-only Built-in Tools", inference.ReadOnlyTools);
            CheckField mcpToolsEnabled = form.Check("Enable Scoped MCP Tools", inference.McpToolsEnabled);
            CheckField requireHarbor = form.Check("Require Harbor Execution", inference.RequireHarbor);
            TextAreaField allowedTools = form.Area("Allowed Tools", inference.AllowedTools == null ? "" : String.Join("\n", inference.AllowedTools), 3, false, "Exact tool names, one per line. Blank permits all. Disable tool calls to permit none. Read-only excludes shell, file changes and MCP tools.", ".txt");
            InputField maxIterations = form.Number("Maximum Model Turns", inference.MaxIterations, 1, 1000);
            InputField maxTokens = form.Text("Maximum Output Tokens", inference.MaxTokens?.ToString(CultureInfo.InvariantCulture), "Provider default");
            InputField temperature = form.Text("Temperature", inference.Temperature?.ToString(CultureInfo.InvariantCulture), "Provider default (0–2)");
            InputField topP = form.Text("Top P", inference.TopP?.ToString(CultureInfo.InvariantCulture), "Provider default (0–1)");
            InputField contextLength = form.Text("Context Window (Ollama num_ctx)", inference.ContextLength?.ToString(CultureInfo.InvariantCulture), "Model default (512–2097152)");
            InputField maxConcurrent = form.Text("Max Concurrent Missions", inference.MaxConcurrentMissions?.ToString(CultureInfo.InvariantCulture), "Unlimited (1–64, per base URL)");
            CheckField streaming = form.Check("Stream Responses", inference.Streaming);
            InputField firstResponse = form.Text("First-response Timeout (ms)", inference.FirstResponseTimeoutMs?.ToString(CultureInfo.InvariantCulture), "Request timeout (1000–1800000)");
            InputField toolWindow = form.Text("Tool Output Window (%)", inference.ToolOutputWindowPercent?.ToString(CultureInfo.InvariantCulture), "12 (1–50, share of the context window)");
            SelectField<string> scope = form.Scope(existing != null ? existing.Scope : ScopeRules.ResolveCreateScope(context.Session, null));
            CheckField enabled = form.Check("Enabled", existing?.Enabled ?? true);
            form.MarkClean();

            ModelEndpoint? saved = null;
            return EntityUi.ShowForm(context, existing != null ? "Edit Endpoint" : "Create Endpoint", form, existing != null ? "Save Changes" : "Create Endpoint", async ct =>
            {
                ModelEndpointKindEnum k = EntityForm.EnumValue(kind, ModelEndpointKindEnum.Inference);
                ModelProviderEnum p = EntityForm.EnumValue(provider, ModelProviderEnum.OpenAI);
                string? reason = UnsupportedReason(p, k);
                if (reason != null) return reason;
                bool vertex = p == ModelProviderEnum.VertexAI;
                bool bedrock = p == ModelProviderEnum.Bedrock;
                bool azure = p == ModelProviderEnum.AzureOpenAI;
                if ((vertex || bedrock) && String.IsNullOrWhiteSpace(region.Value)) return "Region is required for Vertex AI and Bedrock.";
                if (vertex && String.IsNullOrWhiteSpace(project.Value)) return "Project is required for Vertex AI.";
                if (bedrock && String.IsNullOrWhiteSpace(accessKeyId.Value)) return "Access Key ID is required for Bedrock.";
                if (!vertex && !bedrock && p != ModelProviderEnum.OllamaCloud && p != ModelProviderEnum.XAI && String.IsNullOrWhiteSpace(baseUrl.Value)) return "Base URL is required.";
                if (azure && String.IsNullOrWhiteSpace(model.Value)) return "Deployment name is required for Azure OpenAI.";
                ModelEndpointAuthTypeEnum at = EntityForm.EnumValue(authType, ModelEndpointAuthTypeEnum.ProviderDefault);
                if ((vertex || bedrock) && at != ModelEndpointAuthTypeEnum.ProviderDefault) return "Vertex AI and Bedrock support only the provider default authentication.";
                if (p == ModelProviderEnum.OllamaCloud || p == ModelProviderEnum.XAI)
                {
                    if (at != ModelEndpointAuthTypeEnum.ProviderDefault && at != ModelEndpointAuthTypeEnum.Bearer) return "This provider requires bearer authentication.";
                    if (String.IsNullOrWhiteSpace(apiKey.Value) && existing?.HasApiKey != true) return "API key is required.";
                }
                if (at == ModelEndpointAuthTypeEnum.Basic && String.IsNullOrWhiteSpace(authUsername.Value)) return "Basic authentication needs a username.";
                if (at == ModelEndpointAuthTypeEnum.Header && String.IsNullOrWhiteSpace(authHeaderName.Value)) return "Header authentication needs a header name.";
                if (at == ModelEndpointAuthTypeEnum.QueryParameter && String.IsNullOrWhiteSpace(authQueryParameter.Value)) return "Query parameter authentication needs a parameter name.";
                List<ModelEndpointHeader>? extra = ParseHeaders(headers.Value, out string? headerError);
                if (extra == null) return headerError;

                ModelEndpoint payload = new ModelEndpoint();
                try
                {
                    payload.InferenceOptions = new InferenceOptions {
                        ToolsEnabled = toolsEnabled.Value, ReadOnlyTools = readOnlyTools.Value, McpToolsEnabled = mcpToolsEnabled.Value, RequireHarbor = requireHarbor.Value,
                        AllowedTools = String.IsNullOrWhiteSpace(allowedTools.Value) ? null : allowedTools.Value.Split(new[] { '\n', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(n => n.Trim()).ToList(),
                        MaxIterations = EntityForm.IntValue(maxIterations) ?? 100,
                        MaxTokens = String.IsNullOrWhiteSpace(maxTokens.Value) ? null : Int32.Parse(maxTokens.Value, CultureInfo.InvariantCulture),
                        Temperature = String.IsNullOrWhiteSpace(temperature.Value) ? null : Double.Parse(temperature.Value, CultureInfo.InvariantCulture),
                        TopP = String.IsNullOrWhiteSpace(topP.Value) ? null : Double.Parse(topP.Value, CultureInfo.InvariantCulture),
                        ContextLength = String.IsNullOrWhiteSpace(contextLength.Value) ? null : Int32.Parse(contextLength.Value, CultureInfo.InvariantCulture),
                        MaxConcurrentMissions = String.IsNullOrWhiteSpace(maxConcurrent.Value) ? null : Int32.Parse(maxConcurrent.Value, CultureInfo.InvariantCulture),
                        Streaming = streaming.Value,
                        FirstResponseTimeoutMs = String.IsNullOrWhiteSpace(firstResponse.Value) ? null : Int32.Parse(firstResponse.Value, CultureInfo.InvariantCulture),
                        ToolOutputWindowPercent = String.IsNullOrWhiteSpace(toolWindow.Value) ? null : Int32.Parse(toolWindow.Value, CultureInfo.InvariantCulture)
                    };
                    payload.InferenceOptions.Validate();
                }
                catch (Exception ex) when (ex is ArgumentException || ex is FormatException || ex is OverflowException) { return ex.Message; }
                payload.Name = name.Value.Trim();
                payload.Kind = k;
                payload.Provider = p;
                payload.BaseUrl = p == ModelProviderEnum.OllamaCloud ? "https://ollama.com:443" : baseUrl.Value.Trim();
                payload.Model = EntityUi.Blank(model.Value);
                payload.Region = EntityUi.Blank(region.Value);
                payload.Project = EntityUi.Blank(project.Value);
                payload.ApiVersion = EntityUi.Blank(apiVersion.Value);
                payload.AccessKeyId = EntityUi.Blank(accessKeyId.Value);
                payload.Dimensionality = EntityForm.IntValue(dimensionality) ?? 0;
                payload.TimeoutMs = EntityForm.IntValue(timeout) ?? 120000;
                payload.Enabled = enabled.Value;
                payload.Scope = ScopeRules.ResolveCreateScope(context.Session, EntityForm.EnumValue(scope, ScopeEnum.TenantWide));
                payload.AuthType = at;
                payload.AuthUsername = at == ModelEndpointAuthTypeEnum.Basic ? EntityUi.Blank(authUsername.Value) : null;
                payload.AuthHeaderName = at == ModelEndpointAuthTypeEnum.Header ? EntityUi.Blank(authHeaderName.Value) : null;
                payload.AuthQueryParameter = at == ModelEndpointAuthTypeEnum.QueryParameter ? EntityUi.Blank(authQueryParameter.Value) : null;
                payload.Headers = extra;
                string? key = at == ModelEndpointAuthTypeEnum.None || String.IsNullOrEmpty(apiKey.Value) ? null : apiKey.Value;
                saved = existing != null
                    ? await context.Client.UpdateModelEndpointAsync(existing.Id, payload, key, ct).ConfigureAwait(false)
                    : await context.Client.CreateModelEndpointAsync(payload, key, ct).ConfigureAwait(false);
                return null;
            }, () =>
            {
                if (saved == null) return;
                string text = existing != null
                    ? EntityUi.T(context, "Endpoint \"{{name}}\" saved.", "name", saved.Name)
                    : EntityUi.T(context, "Endpoint \"{{name}}\" created.", "name", saved.Name);
                EntityUi.Toast(context, NotificationSeverityEnum.Success, text);
                onSaved?.Invoke(saved);
            });
        }

        /// <summary>
        /// Run Validate Now and show the result.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="endpoint">Endpoint.</param>
        /// <param name="onDone">Runs on the UI loop after the result is shown (refresh).</param>
        public static void Validate(TuiContext context, ModelEndpoint endpoint, Action? onDone)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            EntityUi.Toast(context, NotificationSeverityEnum.Info, context.Loc.T("Validating..."));
            EntityUi.Run<ModelEndpointProbeResult?>(context, ct => context.Client.ValidateModelEndpointAsync(endpoint.Id, ct), result =>
            {
                if (result == null) return;
                ShowProbe(context, endpoint, result);
                if (result.Success) EntityUi.Toast(context, NotificationSeverityEnum.Success, EntityUi.T(context, "Endpoint \"{{name}}\" validated.", "name", endpoint.Name));
                else EntityUi.Toast(context, NotificationSeverityEnum.Error, EntityUi.T(context, "Endpoint \"{{name}}\" validation failed.", "name", endpoint.Name));
                onDone?.Invoke();
            }, "Validation failed.");
        }

        /// <summary>
        /// Show a probe result.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="endpoint">Endpoint.</param>
        /// <param name="result">Result.</param>
        /// <returns>The modal.</returns>
        public static ViewerModal ShowProbe(TuiContext context, ModelEndpoint endpoint, ModelEndpointProbeResult result)
        {
            LinkDetailView view = new LinkDetailView();
            view.Row("Endpoint", endpoint.Name + "  " + endpoint.Id);
            view.Row("Result", EntityUi.Badge(context, result.Success ? "Healthy" : "Unhealthy"), t => result.Success ? t.Success : t.Error);
            view.Row("Latency", result.LatencyMs.ToString(CultureInfo.InvariantCulture) + " ms");
            if (result.StatusCode.HasValue) view.Row("Status Code", result.StatusCode.Value.ToString(CultureInfo.InvariantCulture));
            if (result.EmbeddingDimensions.HasValue) view.Row("Dimensions", result.EmbeddingDimensions.Value.ToString(CultureInfo.InvariantCulture));
            if (!String.IsNullOrEmpty(result.SampleText)) view.Row("Sample", result.SampleText, t => t.Code);
            if (!String.IsNullOrEmpty(result.Error)) view.Row("Error", result.Error, t => t.Error);
            if (!String.IsNullOrEmpty(result.Note)) view.Row("Note", result.Note);
            ViewerModal modal = new ViewerModal("Validation Result", view, context.Loc, context.Theme.Current);
            modal.HeightRatio = 0.5;
            modal.CopyRequested += (s, e) => context.Clipboard.Copy(Armada.Client.ArmadaJson.Serialize(result), "JSON");
            context.Modals.Show(modal);
            return modal;
        }

        /// <summary>
        /// Show the health detail. <c>v</c> runs Validate Now when <paramref name="canValidate"/> is true.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="endpoint">Endpoint.</param>
        /// <param name="canValidate">Whether the user may validate it.</param>
        /// <param name="onValidated">Runs after a validation from the dialog.</param>
        /// <returns>The modal.</returns>
        public static ViewerModal ShowHealth(TuiContext context, ModelEndpoint endpoint, bool canValidate, Action? onValidated)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            DateTime now = context.Clock.UtcNow;
            EndpointHealthView view = new EndpointHealthView();
            LinkDetailView s = view.Summary;
            s.Row("Base URL", EntityUi.Dash(endpoint.BaseUrl), t => t.Code);
            s.Row("Authentication", AuthSummary(endpoint));
            if (!String.IsNullOrEmpty(endpoint.Model)) s.Row("Model", endpoint.Model);
            if (endpoint.LastLatencyMs.HasValue) s.Row("Latency", endpoint.LastLatencyMs.Value.ToString(CultureInfo.InvariantCulture) + " ms");
            s.Row("Status", EntityUi.Badge(context, endpoint.HealthStatus.ToString()), t => Widgets.StatusBadge.Style(endpoint.HealthStatus, t));
            s.Row("Uptime", endpoint.HealthHistory.Count > 0 ? endpoint.UptimePercentage.ToString("0.00", CultureInfo.InvariantCulture) + "%" : "-");
            s.Row("History Span", EndpointHealthHistogram.Span(endpoint.FirstHealthCheckUtc, now));
            s.Row("Consecutive OK", endpoint.ConsecutiveSuccesses.ToString(CultureInfo.InvariantCulture), t => t.Success);
            s.Row("Consecutive Fail", endpoint.ConsecutiveFailures.ToString(CultureInfo.InvariantCulture), t => t.Error);
            if (!String.IsNullOrEmpty(endpoint.LastHealthError)) s.Row("Last Error", endpoint.LastHealthError, t => t.Error);
            string strip = EndpointHealthHistogram.Strip(endpoint.HealthHistory, now, 60);
            s.Row("Health History", strip.Length == 0 ? context.Loc.T("No data") : strip + "   (+ ok, x fail, ~ mixed)");
            s.Row("First check", EntityUi.Date(context, endpoint.FirstHealthCheckUtc));
            s.Row("Last check", EntityUi.Date(context, endpoint.LastHealthCheckUtc));
            s.Row("Last healthy", EntityUi.Date(context, endpoint.LastHealthyUtc));
            s.Row("Last unhealthy", EntityUi.Date(context, endpoint.LastUnhealthyUtc));
            s.Row("Note", context.Loc.T("Health checks are deduplicated by base URL: endpoints sharing a base URL are probed once per sweep."));
            List<HealthBucket> buckets = EndpointHealthHistogram.Buckets(endpoint.HealthHistory, now, 60);
            view.Chart.Title = "Health History";
            view.Chart.Series.Add(new ChartSeries("Successful probes", buckets.Select(b => (double)b.Success)));
            foreach (HealthBucket b in buckets) view.Chart.Labels.Add(b.TimeUtc.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture));

            ViewerModal modal = new ViewerModal(context.Loc.T("Health") + ": " + endpoint.Name, view, context.Loc, context.Theme.Current);
            if (canValidate)
            {
                modal.FooterHint = " v " + context.Loc.T("Validate Now") + "  y " + context.Loc.T("Copy") + "  Esc " + context.Loc.T("Close") + " ";
                view.ValidateRequested = () =>
                {
                    modal.RequestClose(null);
                    context.App.Modals.RemoveClosed();
                    Validate(context, endpoint, onValidated);
                };
            }

            modal.CopyRequested += (s2, e) => context.Clipboard.Copy(Armada.Client.ArmadaJson.Serialize(endpoint), "JSON");
            context.Modals.Show(modal);
            return modal;
        }

        #endregion

        #region Private-Methods

        private static string AuthSummary(ModelEndpoint endpoint)
        {
            string summary = endpoint.AuthType.ToString();
            if (endpoint.AuthType == ModelEndpointAuthTypeEnum.Basic && !String.IsNullOrEmpty(endpoint.AuthUsername)) summary += " (" + endpoint.AuthUsername + ")";
            if (endpoint.AuthType == ModelEndpointAuthTypeEnum.Header && !String.IsNullOrEmpty(endpoint.AuthHeaderName)) summary += " (" + endpoint.AuthHeaderName + ")";
            if (endpoint.AuthType == ModelEndpointAuthTypeEnum.QueryParameter && !String.IsNullOrEmpty(endpoint.AuthQueryParameter)) summary += " (?" + endpoint.AuthQueryParameter + "=)";
            if (endpoint.AuthType != ModelEndpointAuthTypeEnum.None) summary += endpoint.HasApiKey ? ", secret stored" : ", no secret";
            if (endpoint.Headers.Count > 0) summary += ", headers: " + String.Join(", ", endpoint.Headers.Select(h => h.Name));
            return summary;
        }

        private static string HeaderLines(ModelEndpoint? existing)
        {
            if (existing == null || existing.Headers.Count == 0) return "";
            return String.Join("\n", existing.Headers.Select(h => h.Name + ":"));
        }

        #endregion
    }
}

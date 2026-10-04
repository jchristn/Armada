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
            InputField baseUrl = form.Text("Base URL", existing?.BaseUrl, "https://api.openai.com", false, "Optional override for Vertex AI and Bedrock; the resource endpoint for Azure OpenAI.");
            InputField model = form.Text("Model", existing?.Model, "gpt-4o-mini", false, "Deployment name for Azure OpenAI; Bedrock model id for Bedrock.");
            InputField apiVersion = form.Text("API Version", existing?.ApiVersion, "2024-10-21 (default)", false, "Azure OpenAI only.");
            bool hasKey = existing != null && existing.HasApiKey;
            InputField apiKey = form.Text("API Key", "", hasKey ? context.Loc.T("(unchanged - leave blank to keep stored key)") : context.Loc.T("Optional"), false,
                "Service Account JSON for Vertex AI; AWS Secret Access Key for Bedrock. Blank keeps the stored secret.");
            apiKey.Masked = true;
            InputField dimensionality = form.Number("Dimensionality", existing?.Dimensionality ?? 0, 0, 100000);
            InputField timeout = form.Number("Timeout (ms)", existing?.TimeoutMs ?? 120000, 1000, 600000);
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
                if (!vertex && !bedrock && String.IsNullOrWhiteSpace(baseUrl.Value)) return "Base URL is required.";
                if (azure && String.IsNullOrWhiteSpace(model.Value)) return "Deployment name is required for Azure OpenAI.";

                ModelEndpoint payload = new ModelEndpoint();
                payload.Name = name.Value.Trim();
                payload.Kind = k;
                payload.Provider = p;
                payload.BaseUrl = baseUrl.Value.Trim();
                payload.Model = EntityUi.Blank(model.Value);
                payload.Region = EntityUi.Blank(region.Value);
                payload.Project = EntityUi.Blank(project.Value);
                payload.ApiVersion = EntityUi.Blank(apiVersion.Value);
                payload.AccessKeyId = EntityUi.Blank(accessKeyId.Value);
                payload.Dimensionality = EntityForm.IntValue(dimensionality) ?? 0;
                payload.TimeoutMs = EntityForm.IntValue(timeout) ?? 120000;
                payload.Enabled = enabled.Value;
                payload.Scope = ScopeRules.ResolveCreateScope(context.Session, EntityForm.EnumValue(scope, ScopeEnum.TenantWide));
                string? key = String.IsNullOrEmpty(apiKey.Value) ? null : apiKey.Value;
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
            if (!String.IsNullOrEmpty(endpoint.Model)) s.Row("Model", endpoint.Model);
            if (endpoint.LastLatencyMs.HasValue) s.Row("Latency", endpoint.LastLatencyMs.Value.ToString(CultureInfo.InvariantCulture) + " ms");
            s.Row("Status", EntityUi.Badge(context, endpoint.HealthStatus.ToString()), t => Widgets.StatusBadge.Style(endpoint.HealthStatus.ToString(), t));
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
    }
}

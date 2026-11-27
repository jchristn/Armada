namespace Armada.Client
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client.Http;
    using Armada.Core.Models;

    /// <summary>
    /// Model alert calls (<c>/api/v1/model-alerts</c>): list and read scorecard alerts, evaluate now, read and replace the
    /// thresholds, apply a suggested action and ignore an alert. Alerts are advisory; nothing changes until an action is
    /// applied.
    /// </summary>
    public sealed class ModelAlertsClient
    {
        #region Private-Members

        private readonly ArmadaTransport _Transport;

        #endregion

        #region Constructors-and-Factories

        internal ModelAlertsClient(ArmadaTransport transport)
        {
            _Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// GET /api/v1/model-alerts: one page of the alerts visible to the caller, most recently raised first.
        /// </summary>
        /// <param name="query">Filters and paging, or null for the first page.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<ModelAlert>?> ListAsync(ModelAlertQuery? query = null, CancellationToken token = default)
        {
            string querystring = String.Empty;
            if (query != null)
            {
                querystring = ArmadaQueryString.FromPairs(new List<KeyValuePair<string, string?>>
                {
                    new KeyValuePair<string, string?>("tenantId", query.TenantId),
                    new KeyValuePair<string, string?>("status", query.Status?.ToString()),
                    new KeyValuePair<string, string?>("profileId", query.ProfileId),
                    new KeyValuePair<string, string?>("persona", query.Persona),
                    new KeyValuePair<string, string?>("includeIgnored", query.IncludeIgnored ? "true" : "false"),
                    new KeyValuePair<string, string?>("pageNumber", query.PageNumber.ToString(CultureInfo.InvariantCulture)),
                    new KeyValuePair<string, string?>("pageSize", query.PageSize.ToString(CultureInfo.InvariantCulture))
                });
            }

            return _Transport.GetAsync<EnumerationResult<ModelAlert>>($"/api/v1/model-alerts{querystring}", null, token);
        }

        /// <summary>
        /// POST /api/v1/model-alerts/enumerate: one page of the alerts visible to the caller.
        /// </summary>
        /// <param name="query">Paging and filters, or null for the first page.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<ModelAlert>?> EnumerateAsync(ModelAlertQuery? query = null, CancellationToken token = default)
        {
            return _Transport.PostAsync<EnumerationResult<ModelAlert>>("/api/v1/model-alerts/enumerate", query ?? new ModelAlertQuery(), null, token);
        }

        /// <summary>
        /// GET /api/v1/model-alerts/{id}: one alert with its suggested actions.
        /// </summary>
        /// <param name="id">Alert id (mal_ prefix).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The alert.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<ModelAlert?> GetAsync(string id, CancellationToken token = default)
        {
            return _Transport.GetAsync<ModelAlert>($"/api/v1/model-alerts/{ArmadaQueryString.Escape(id)}", null, token);
        }

        /// <summary>
        /// POST /api/v1/model-alerts/evaluate: run one evaluation pass now (admins).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The evaluation.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<ModelAlertEvaluation?> EvaluateAsync(CancellationToken token = default)
        {
            return _Transport.PostAsync<ModelAlertEvaluation>("/api/v1/model-alerts/evaluate", new object(), null, token);
        }

        /// <summary>
        /// GET /api/v1/model-alerts/thresholds: defaults and per-persona overrides.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The thresholds.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<ModelAlertThresholds?> GetThresholdsAsync(CancellationToken token = default)
        {
            return _Transport.GetAsync<ModelAlertThresholds>("/api/v1/model-alerts/thresholds", null, token);
        }

        /// <summary>
        /// PUT /api/v1/model-alerts/thresholds: replace the thresholds (global admins).
        /// </summary>
        /// <param name="thresholds">Defaults and overrides.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored thresholds.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<ModelAlertThresholds?> UpdateThresholdsAsync(ModelAlertThresholds thresholds, CancellationToken token = default)
        {
            return _Transport.PutAsync<ModelAlertThresholds>("/api/v1/model-alerts/thresholds", thresholds, null, token);
        }

        /// <summary>
        /// POST /api/v1/model-alerts/{id}/apply: apply one suggested action (admins).
        /// </summary>
        /// <param name="id">Alert id (mal_ prefix).</param>
        /// <param name="request">Action and, for Ignore, days.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The record and the alert.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("applyModelAlertAction")]
        public Task<ModelAlertApplyResult?> ApplyActionAsync(string id, ModelAlertApplyRequest request, CancellationToken token = default)
        {
            return _Transport.PostAsync<ModelAlertApplyResult>($"/api/v1/model-alerts/{ArmadaQueryString.Escape(id)}/apply", request, null, token);
        }

        /// <summary>
        /// POST /api/v1/model-alerts/{id}/ignore: hide the alert for a number of days (admins).
        /// </summary>
        /// <param name="id">Alert id (mal_ prefix).</param>
        /// <param name="days">Days (1-365), or null for the default (7).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The record and the alert.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("ignoreModelAlert")]
        public Task<ModelAlertApplyResult?> IgnoreAsync(string id, int? days = null, CancellationToken token = default)
        {
            return _Transport.PostAsync<ModelAlertApplyResult>($"/api/v1/model-alerts/{ArmadaQueryString.Escape(id)}/ignore", new ModelAlertIgnoreRequest { Days = days }, null, token);
        }

        #endregion
    }
}

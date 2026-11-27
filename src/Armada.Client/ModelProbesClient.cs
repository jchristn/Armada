namespace Armada.Client
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client.Http;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Evaluate calls: start a probe-only Quick Evaluate of a captain or a model endpoint and read the runs it records
    /// (<c>/api/v1/model-probe-runs</c>) (dashboard <c>api/client.ts</c> parity).
    /// </summary>
    public sealed class ModelProbesClient
    {
        #region Private-Members

        private readonly ArmadaTransport _Transport;

        #endregion

        #region Constructors-and-Factories

        internal ModelProbesClient(ArmadaTransport transport)
        {
            _Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// POST /api/v1/captains/{id}/evaluate: start an Evaluate of a captain through its own runtime. Returns the queued
        /// run at once. Global admin or tenant admin; Full adds the fixture battery and the evaluator's grading with the defaults.
        /// </summary>
        /// <param name="captainId">Captain id (cpt_ prefix).</param>
        /// <param name="depth">Depth (Quick).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The queued run.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("evaluateCaptain")]
        public Task<ModelProbeRun?> EvaluateCaptainAsync(string captainId, ModelProbeDepthEnum depth = ModelProbeDepthEnum.Quick, CancellationToken token = default)
        {
            EvaluateCaptainRequest body = new EvaluateCaptainRequest { Depth = depth };
            return _Transport.PostAsync<ModelProbeRun>($"/api/v1/captains/{ArmadaQueryString.Escape(captainId)}/evaluate", body, null, token);
        }

        /// <summary>
        /// Start an Evaluate of a captain with every parameter: depth and, for Full, the evaluator captain, packs, personas
        /// and run count. Returns the queued run at once.
        /// </summary>
        /// <param name="captainId">Captain ID (cpt_ prefix).</param>
        /// <param name="request">Depth and Full Evaluate parameters (CaptainId and ModelEndpointId are ignored).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The queued run.</returns>
        /// <exception cref="ArgumentNullException">Thrown when request is null.</exception>
        public Task<ModelProbeRun?> EvaluateCaptainWithRequestAsync(string captainId, EvaluateCaptainRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return _Transport.PostAsync<ModelProbeRun>($"/api/v1/captains/{ArmadaQueryString.Escape(captainId)}/evaluate", request, null, token);
        }

        /// <summary>
        /// Cancel a running Evaluate (Quick or Full). A Full run keeps a partial report marked incomplete and releases its
        /// battery dock and captain; a finished run is returned unchanged.
        /// </summary>
        /// <param name="id">Run ID.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The run after the cancellation.</returns>
        [DashboardExport("cancelModelProbeRun")]
        public Task<ModelProbeRun?> CancelRunAsync(string id, CancellationToken token = default)
        {
            return _Transport.PostAsync<ModelProbeRun>($"/api/v1/model-probe-runs/{ArmadaQueryString.Escape(id)}/cancel", new object(), null, token);
        }

        /// <summary>
        /// POST /api/v1/model-endpoints/{id}/evaluate: start an Evaluate of a model endpoint through the API endpoint
        /// runtime. Returns the queued run at once; depth Full needs an admin.
        /// </summary>
        /// <param name="modelEndpointId">Model endpoint id (mep_ prefix).</param>
        /// <param name="depth">Depth (Quick).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The queued run.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("evaluateModelEndpoint")]
        public Task<ModelProbeRun?> EvaluateModelEndpointAsync(string modelEndpointId, ModelProbeDepthEnum depth = ModelProbeDepthEnum.Quick, CancellationToken token = default)
        {
            EvaluateCaptainRequest body = new EvaluateCaptainRequest { Depth = depth };
            return _Transport.PostAsync<ModelProbeRun>($"/api/v1/model-endpoints/{ArmadaQueryString.Escape(modelEndpointId)}/evaluate", body, null, token);
        }

        /// <summary>
        /// GET /api/v1/model-probe-runs/{id}: one run with its per-probe results.
        /// </summary>
        /// <param name="id">Run id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The run.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getModelProbeRun")]
        public Task<ModelProbeRun?> GetRunAsync(string id, CancellationToken token = default)
        {
            return _Transport.GetAsync<ModelProbeRun>($"/api/v1/model-probe-runs/{ArmadaQueryString.Escape(id)}", null, token);
        }

        /// <summary>
        /// GET /api/v1/model-probe-runs: one page of the runs visible to the caller, newest first, without results.
        /// </summary>
        /// <param name="query">Filters and paging, or null for the first page.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("listModelProbeRuns")]
        public Task<EnumerationResult<ModelProbeRun>?> ListRunsAsync(ModelProbeRunQuery? query = null, CancellationToken token = default)
        {
            return _Transport.GetAsync<EnumerationResult<ModelProbeRun>>($"/api/v1/model-probe-runs{ArmadaQueryString.FromObject(query)}", null, token);
        }

        /// <summary>
        /// POST /api/v1/model-probe-runs/enumerate: one page of the quick model probe runs visible to the caller, newest first, with the total.
        /// </summary>
        /// <param name="query">Paging and filters, or null for the first page.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<ModelProbeRun>?> EnumerateRunsAsync(ModelProbeRunQuery? query = null, CancellationToken token = default)
        {
            return _Transport.PostAsync<EnumerationResult<ModelProbeRun>>("/api/v1/model-probe-runs/enumerate", query ?? new ModelProbeRunQuery(), null, token);
        }

        #endregion
    }
}

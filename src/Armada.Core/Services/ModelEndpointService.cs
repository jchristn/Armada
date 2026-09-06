namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using PolyPrompt.Clients;
    using PolyPrompt.Models;
    using SyslogLogging;

    /// <summary>
    /// Creates, updates, enumerates, validates, and health-checks managed model endpoints (embedding and
    /// inference). Validation issues a real request through PolyPrompt; health checks are deduplicated by base
    /// URL so a URL shared by several endpoints is probed once and the result fanned out to all of them.
    /// </summary>
    public class ModelEndpointService
    {
        #region Private-Members

        private const int _MaxHistoryRecords = 500;

        private readonly string _Header = "[ModelEndpointService] ";
        private readonly DatabaseDriver _Database;
        private readonly LoggingModule _Logging;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="logging">Logging module.</param>
        public ModelEndpointService(DatabaseDriver database, LoggingModule logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Enumerate model endpoints visible to the caller (all endpoints for an admin, otherwise the caller's
        /// tenant), newest first.
        /// </summary>
        /// <param name="auth">Authentication context.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>List of model endpoints.</returns>
        public async Task<List<ModelEndpoint>> EnumerateAsync(AuthContext auth, CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));

            if (auth.IsAdmin || String.IsNullOrEmpty(auth.TenantId))
                return await _Database.ModelEndpoints.EnumerateAsync(token).ConfigureAwait(false);

            // Within a tenant: a tenant admin sees all; a regular user sees tenant-wide endpoints plus their own.
            List<ModelEndpoint> tenantEndpoints = await _Database.ModelEndpoints.EnumerateAsync(auth.TenantId, token).ConfigureAwait(false);
            if (auth.IsTenantAdmin) return tenantEndpoints;

            List<ModelEndpoint> visible = new List<ModelEndpoint>();
            foreach (ModelEndpoint endpoint in tenantEndpoints)
            {
                if (ScopedVisibility.CanView(auth, endpoint.Scope, endpoint.TenantId, endpoint.UserId)) visible.Add(endpoint);
            }

            return visible;
        }

        /// <summary>
        /// Read a single model endpoint within the caller scope, or null when not found or not visible.
        /// </summary>
        /// <param name="auth">Authentication context.</param>
        /// <param name="id">Endpoint identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The endpoint or null.</returns>
        public async Task<ModelEndpoint?> ReadAsync(AuthContext auth, string id, CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            ModelEndpoint? endpoint = await _Database.ModelEndpoints.ReadAsync(id, token).ConfigureAwait(false);
            if (endpoint == null) return null;
            if (!IsVisible(auth, endpoint)) return null;
            return endpoint;
        }

        /// <summary>
        /// Create a model endpoint. The caller's tenant and user own the record. The provider/kind combination
        /// is validated (Anthropic cannot embed, Voyage AI cannot do inference) and a base URL is required.
        /// </summary>
        /// <param name="auth">Authentication context.</param>
        /// <param name="endpoint">Endpoint to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created endpoint (with the API key redacted from serialization).</returns>
        public async Task<ModelEndpoint> CreateAsync(AuthContext auth, ModelEndpoint endpoint, CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));

            ValidateShape(endpoint);

            endpoint.TenantId = auth.TenantId;
            endpoint.UserId = auth.UserId;
            // Regular users may only create user-specific endpoints; admins may choose (default tenant-wide).
            endpoint.Scope = ScopedVisibility.ResolveCreateScope(auth, endpoint.Scope);
            endpoint.CreatedUtc = DateTime.UtcNow;
            endpoint.LastUpdateUtc = DateTime.UtcNow;
            endpoint.HealthStatus = EndpointHealthStatusEnum.Unknown;
            endpoint.LastHealthCheckUtc = null;
            endpoint.LastHealthError = null;
            endpoint.LastLatencyMs = null;

            _Logging.Info(_Header + "creating endpoint " + endpoint.Id + " (" + endpoint.Provider + "/" + endpoint.Kind + ")");
            return await _Database.ModelEndpoints.CreateAsync(endpoint, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Update a model endpoint. Editable fields are copied from the supplied instance onto the stored
        /// record. The API key is preserved unless the caller explicitly supplied a new value.
        /// </summary>
        /// <param name="auth">Authentication context.</param>
        /// <param name="endpoint">Endpoint carrying updated fields (its Id selects the record).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated endpoint.</returns>
        public async Task<ModelEndpoint> UpdateAsync(AuthContext auth, ModelEndpoint endpoint, CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));

            ModelEndpoint? existing = await _Database.ModelEndpoints.ReadAsync(endpoint.Id, token).ConfigureAwait(false);
            if (existing == null) throw new KeyNotFoundException("Model endpoint not found: " + endpoint.Id);
            if (!ScopedVisibility.CanEdit(auth, existing.Scope, existing.TenantId, existing.UserId)) throw new UnauthorizedAccessException("Not permitted to modify endpoint " + endpoint.Id);
            // Preserve ownership/scope on update; only an admin may change the scope of an existing endpoint.
            endpoint.UserId = existing.UserId;
            endpoint.Scope = (auth.IsAdmin || auth.IsTenantAdmin) ? endpoint.Scope : existing.Scope;

            ValidateShape(endpoint);

            existing.Name = endpoint.Name;
            existing.Kind = endpoint.Kind;
            existing.Provider = endpoint.Provider;
            existing.BaseUrl = endpoint.BaseUrl;
            existing.Model = endpoint.Model;
            existing.Dimensionality = endpoint.Dimensionality;
            existing.TimeoutMs = endpoint.TimeoutMs;
            existing.Enabled = endpoint.Enabled;
            if (endpoint.ApiKeySpecified) existing.ApiKey = endpoint.ApiKey;
            existing.LastUpdateUtc = DateTime.UtcNow;

            _Logging.Info(_Header + "updating endpoint " + existing.Id);
            return await _Database.ModelEndpoints.UpdateAsync(existing, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Delete a model endpoint within the caller scope.
        /// </summary>
        /// <param name="auth">Authentication context.</param>
        /// <param name="id">Endpoint identifier.</param>
        /// <param name="token">Cancellation token.</param>
        public async Task DeleteAsync(AuthContext auth, string id, CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            ModelEndpoint? existing = await _Database.ModelEndpoints.ReadAsync(id, token).ConfigureAwait(false);
            if (existing == null) throw new KeyNotFoundException("Model endpoint not found: " + id);
            if (!ScopedVisibility.CanEdit(auth, existing.Scope, existing.TenantId, existing.UserId)) throw new UnauthorizedAccessException("Not permitted to delete endpoint " + id);

            // Guard: do not delete an endpoint a captain still references, which would orphan that captain and
            // cause its launches to fail. The caller must first repoint or remove those captains.
            List<Captain> referencing = await FindCaptainsReferencingAsync(id, token).ConfigureAwait(false);
            if (referencing.Count > 0)
            {
                List<string> names = new List<string>();
                foreach (Captain captain in referencing) names.Add(captain.Name);
                throw new InvalidOperationException(
                    "Cannot delete endpoint " + id + ": it is used by " + referencing.Count + " captain(s): " + String.Join(", ", names) +
                    ". Repoint or delete those captains first.");
            }

            _Logging.Info(_Header + "deleting endpoint " + id);
            await _Database.ModelEndpoints.DeleteAsync(id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Return the captains that reference the given endpoint, so callers can warn before attempting a
        /// delete. Empty when the endpoint is unused.
        /// </summary>
        /// <param name="auth">Authentication context.</param>
        /// <param name="id">Endpoint identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The referencing captains.</returns>
        public async Task<List<Captain>> GetReferencingCaptainsAsync(AuthContext auth, string id, CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            return await FindCaptainsReferencingAsync(id, token).ConfigureAwait(false);
        }

        private async Task<List<Captain>> FindCaptainsReferencingAsync(string id, CancellationToken token)
        {
            List<Captain> all = await _Database.Captains.EnumerateAsync(token).ConfigureAwait(false);
            List<Captain> referencing = new List<Captain>();
            foreach (Captain captain in all)
            {
                if (String.Equals(captain.ModelEndpointId, id, StringComparison.Ordinal)) referencing.Add(captain);
            }

            return referencing;
        }

        /// <summary>
        /// Validate a model endpoint by issuing a real request: an embedding request for embedding endpoints, a
        /// short completion for inference endpoints. Persists the resulting health status on the endpoint.
        /// </summary>
        /// <param name="auth">Authentication context.</param>
        /// <param name="id">Endpoint identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The probe result.</returns>
        public async Task<ModelEndpointProbeResult> ValidateAsync(AuthContext auth, string id, CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            ModelEndpoint? endpoint = await _Database.ModelEndpoints.ReadAsync(id, token).ConfigureAwait(false);
            if (endpoint == null) throw new KeyNotFoundException("Model endpoint not found: " + id);
            if (!IsVisible(auth, endpoint)) throw new UnauthorizedAccessException("Not permitted to validate endpoint " + id);

            ModelEndpointProbeResult result = await ValidateEndpointAsync(endpoint, token).ConfigureAwait(false);
            await PersistProbeAsync(endpoint, result, token).ConfigureAwait(false);
            return result;
        }

        /// <summary>
        /// Run a system-wide health sweep across all enabled endpoints. Endpoints are grouped by normalized
        /// base URL so each distinct URL is probed exactly once; the outcome is written to every endpoint that
        /// shares that URL.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The number of distinct base URLs probed.</returns>
        public async Task<int> CheckHealthAllAsync(CancellationToken token = default)
        {
            List<ModelEndpoint> all = await _Database.ModelEndpoints.EnumerateAsync(token).ConfigureAwait(false);
            List<ModelEndpoint> enabled = all.Where(e => e.Enabled).ToList();

            Dictionary<string, List<ModelEndpoint>> groups = new Dictionary<string, List<ModelEndpoint>>(StringComparer.OrdinalIgnoreCase);
            foreach (ModelEndpoint endpoint in enabled)
            {
                string key = NormalizeBaseUrl(endpoint.BaseUrl);
                if (!groups.ContainsKey(key)) groups[key] = new List<ModelEndpoint>();
                groups[key].Add(endpoint);
            }

            int probed = 0;
            foreach (KeyValuePair<string, List<ModelEndpoint>> group in groups)
            {
                if (token.IsCancellationRequested) break;
                ModelEndpoint representative = group.Value[0];
                ModelEndpointProbeResult result = await HealthCheckEndpointAsync(representative, token).ConfigureAwait(false);
                probed++;

                foreach (ModelEndpoint member in group.Value)
                    await PersistProbeAsync(member, result, token).ConfigureAwait(false);
            }

            _Logging.Debug(_Header + "health sweep probed " + probed + " distinct base URL(s) across " + enabled.Count + " endpoint(s)");
            return probed;
        }

        /// <summary>
        /// Normalize a base URL for health-check deduplication: lower-cased scheme and host, explicit default
        /// port dropped, and any trailing slash removed. Endpoints whose base URLs normalize equal share one
        /// health probe.
        /// </summary>
        /// <param name="baseUrl">Raw base URL.</param>
        /// <returns>Normalized key.</returns>
        public static string NormalizeBaseUrl(string? baseUrl)
        {
            if (String.IsNullOrWhiteSpace(baseUrl)) return String.Empty;
            string trimmed = baseUrl.Trim();
            Uri? uri;
            if (Uri.TryCreate(trimmed, UriKind.Absolute, out uri))
            {
                string scheme = uri.Scheme.ToLowerInvariant();
                string host = uri.Host.ToLowerInvariant();
                string port = uri.IsDefaultPort ? String.Empty : ":" + uri.Port;
                string path = uri.AbsolutePath.TrimEnd('/');
                return scheme + "://" + host + port + path;
            }

            return trimmed.TrimEnd('/').ToLowerInvariant();
        }

        #endregion

        #region Private-Methods

        private static bool IsVisible(AuthContext auth, ModelEndpoint endpoint)
        {
            return ScopedVisibility.CanView(auth, endpoint.Scope, endpoint.TenantId, endpoint.UserId);
        }

        private static void ValidateShape(ModelEndpoint endpoint)
        {
            if (String.IsNullOrWhiteSpace(endpoint.Name)) throw new ArgumentException("Endpoint name is required.");
            if (String.IsNullOrWhiteSpace(endpoint.BaseUrl)) throw new ArgumentException("Endpoint base URL is required.");

            string? reason = ModelEndpointClientFactory.UnsupportedReason(endpoint.Provider, endpoint.Kind);
            if (reason != null) throw new ArgumentException(reason);
        }

        private async Task<ModelEndpointProbeResult> ValidateEndpointAsync(ModelEndpoint endpoint, CancellationToken token)
        {
            ModelEndpointProbeResult result = new ModelEndpointProbeResult();
            result.BaseUrl = endpoint.BaseUrl;
            Stopwatch sw = Stopwatch.StartNew();

            try
            {
                using (CompletionClientBase client = ModelEndpointClientFactory.Create(endpoint, _Logging))
                {
                    if (endpoint.Kind == ModelEndpointKindEnum.Embedding)
                    {
                        EmbeddingResponse resp = await client.EmbedAsync("Armada model endpoint connectivity check.", null, token).ConfigureAwait(false);
                        sw.Stop();
                        result.LatencyMs = sw.ElapsedMilliseconds;
                        result.StatusCode = resp.StatusCode;
                        result.Success = resp.Success;
                        result.Error = resp.Success ? null : (resp.Error ?? "Embedding request failed.");
                        if (resp.Success && resp.Embeddings != null && resp.Embeddings.Count > 0 && resp.Embeddings[0].Embedding != null)
                            result.EmbeddingDimensions = resp.Embeddings[0].Embedding.Length;
                    }
                    else
                    {
                        ChatCompletionOptions options = new ChatCompletionOptions();
                        options.MaxTokens = 16;
                        ChatResponse resp = await client.ChatAsync("Reply with the single word: pong", options, token).ConfigureAwait(false);
                        sw.Stop();
                        result.LatencyMs = sw.ElapsedMilliseconds;
                        result.StatusCode = resp.StatusCode;
                        result.Success = resp.Success;
                        result.Error = resp.Success ? null : (resp.Error ?? "Completion request failed.");
                        if (resp.Success && !String.IsNullOrEmpty(resp.Text))
                            result.SampleText = resp.Text.Length > 200 ? resp.Text.Substring(0, 200) : resp.Text;
                    }
                }
            }
            catch (Exception e)
            {
                sw.Stop();
                result.LatencyMs = sw.ElapsedMilliseconds;
                result.Success = false;
                result.Error = e.Message;
                _Logging.Warn(_Header + "validation of endpoint " + endpoint.Id + " threw: " + e.Message);
            }

            return result;
        }

        private async Task<ModelEndpointProbeResult> HealthCheckEndpointAsync(ModelEndpoint endpoint, CancellationToken token)
        {
            ModelEndpointProbeResult result = new ModelEndpointProbeResult();
            result.BaseUrl = endpoint.BaseUrl;
            Stopwatch sw = Stopwatch.StartNew();

            try
            {
                using (CompletionClientBase client = ModelEndpointClientFactory.Create(endpoint, _Logging))
                {
                    bool reachable = await client.ValidateConnectivityAsync(token).ConfigureAwait(false);
                    sw.Stop();
                    result.LatencyMs = sw.ElapsedMilliseconds;
                    result.Success = reachable;
                    result.Error = reachable ? null : "Provider did not respond successfully to a connectivity probe.";
                }
            }
            catch (Exception e)
            {
                sw.Stop();
                result.LatencyMs = sw.ElapsedMilliseconds;
                result.Success = false;
                result.Error = e.Message;
            }

            return result;
        }

        private async Task PersistProbeAsync(ModelEndpoint endpoint, ModelEndpointProbeResult result, CancellationToken token)
        {
            endpoint.HealthStatus = result.Success ? EndpointHealthStatusEnum.Healthy : EndpointHealthStatusEnum.Unhealthy;
            endpoint.LastHealthCheckUtc = result.TimestampUtc;
            endpoint.LastHealthError = result.Success ? null : result.Error;
            endpoint.LastLatencyMs = result.LatencyMs;

            if (endpoint.HealthHistory == null) endpoint.HealthHistory = new List<ModelEndpointHealthRecord>();
            endpoint.HealthHistory.Add(new ModelEndpointHealthRecord
            {
                TimestampUtc = result.TimestampUtc,
                Success = result.Success
            });
            if (endpoint.HealthHistory.Count > _MaxHistoryRecords)
                endpoint.HealthHistory.RemoveRange(0, endpoint.HealthHistory.Count - _MaxHistoryRecords);

            endpoint.LastUpdateUtc = DateTime.UtcNow;
            await _Database.ModelEndpoints.UpdateAsync(endpoint, token).ConfigureAwait(false);
        }

        #endregion
    }
}

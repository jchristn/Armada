namespace Armada.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// REST API routes for registered Harbor (host runner) management.
    /// </summary>
    public class HarborRoutes
    {
        private readonly HarborService _Harbors;
        private readonly HarborConnectionManager _Connections;
        private readonly Armada.Core.Database.DatabaseDriver? _Database;
        private readonly HarborMetricsService? _Metrics;
        private readonly HarborJobControlService? _Jobs;
        private static readonly JsonSerializerOptions _BodyJsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="harbors">Harbor service.</param>
        /// <param name="connections">Harbor connection manager (for the connectivity probe).</param>
        /// <param name="database">Database driver, for command audit records (optional).</param>
        /// <param name="metrics">Harbor metrics service (optional; without it the metrics route answers 404).</param>
        /// <param name="jobs">Harbor job control (optional; without it the job routes answer 404).</param>
        public HarborRoutes(HarborService harbors, HarborConnectionManager connections, Armada.Core.Database.DatabaseDriver? database = null, HarborMetricsService? metrics = null, HarborJobControlService? jobs = null)
        {
            _Jobs = jobs;
            _Harbors = harbors ?? throw new ArgumentNullException(nameof(harbors));
            _Connections = connections ?? throw new ArgumentNullException(nameof(connections));
            _Database = database;
            _Metrics = metrics;
        }

        /// <summary>
        /// Register routes.
        /// </summary>
        /// <param name="app">Web server.</param>
        /// <param name="authenticate">Authentication delegate.</param>
        /// <param name="authz">Authorization service.</param>
        public void Register(
            Webserver app,
            Func<HttpContextBase, Task<AuthContext>> authenticate,
            IAuthorizationService authz)
        {
            app.Post("/api/v1/harbors/enumerate", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);
                EnumerationQuery query = EnumerationRequestReader.Read(req, _BodyJsonOptions);
                return await _Harbors.EnumeratePageAsync(ctx, query).ConfigureAwait(false);
            },
            api => api
                .WithTag("Harbors")
                .WithSummary("Enumerate Harbors")
                .WithDescription("One page of the registered Harbors visible to the caller, counted and cut in the database, newest first (Order CreatedAscending for oldest first; ties break by id). Paging and filters (CreatedAfter, CreatedBefore) come from the JSON body (EnumerationQuery) or the querystring (pageNumber, pageSize 1 to 1000, default 10).")
                .WithRequestBody(OpenApiJson.BodyFor<EnumerationQuery>("Paging and filters", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<Harbor>>("One page"))
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/harbors", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                Harbor request = JsonSerializer.Deserialize<Harbor>(req.Http.Request.DataAsString, _BodyJsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as Harbor.");
                try
                {
                    Harbor created = await _Harbors.CreateAsync(ctx, request).ConfigureAwait(false);
                    req.Http.Response.StatusCode = 201;
                    return created;
                }
                catch (CollectionLimitExceededException ex)
                {
                    return RouteErrorMapper.LimitExceeded(req, ex);
                }
                catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = ex.Message };
                }
            },
            api => api
                .WithTag("Harbors")
                .WithSummary("Register a Harbor")
                .WithDescription("Pre-registers a Harbor. A Harbor also self-registers on first handshake. Capabilities may hold at most 256 entries (400 with Data.Code CollectionLimitExceeded otherwise).")
                .WithRequestBody(OpenApiJson.BodyFor<Harbor>("Harbor create request", true))
                .WithResponse(201, OpenApiJson.For<Harbor>("Created Harbor"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/harbors/{id}", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                Harbor? harbor = await _Harbors.ReadAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                if (harbor == null)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Harbor not found" };
                }

                return harbor;
            },
            api => api
                .WithTag("Harbors")
                .WithSummary("Get a Harbor")
                .WithDescription("Returns one Harbor by ID.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Harbor ID (hbr_ prefix)"))
                .WithResponse(200, OpenApiJson.For<Harbor>("Harbor"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/harbors/{id}/metrics", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                string? rangeText = req.Query.GetValueOrDefault("range");
                if (!Armada.Core.Metrics.HarborMetricsRanges.TryParse(rangeText, out HarborMetricsRangeEnum range))
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "range must be 1h, 24h, or 7d." };
                }

                HarborMetrics? metrics = _Metrics == null ? null : await _Metrics.GetAsync(ctx, req.Parameters["id"], range).ConfigureAwait(false);
                if (metrics == null)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Harbor not found" };
                }

                return metrics;
            },
            api => api
                .WithTag("Harbors")
                .WithSummary("Get a Harbor's metrics")
                .WithDescription("Returns charts for one Harbor over a window: jobs finished and failed per bucket (missions and other launches apart), slot usage (peak and average concurrent jobs against its capacity), launch speed per runtime (median and p95 time to first output and total runtime), link health (connected, reconnecting, and down stretches, and heartbeat round-trip times), and token usage by runtime and model. range is 1h (1-minute buckets), 24h (30-minute buckets, the default), or 7d (3-hour buckets).")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Harbor ID (hbr_ prefix)"))
                .WithParameter(OpenApiParameterMetadata.Query("range", "Window: 1h, 24h (default), or 7d", false))
                .WithResponse(200, OpenApiJson.For<HarborMetrics>("Harbor metrics"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Put("/api/v1/harbors/{id}", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                Harbor request = JsonSerializer.Deserialize<Harbor>(req.Http.Request.DataAsString, _BodyJsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as Harbor.");
                request.Id = req.Parameters["id"];
                try
                {
                    return await _Harbors.UpdateAsync(ctx, request).ConfigureAwait(false);
                }
                catch (KeyNotFoundException ex)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = ex.Message };
                }
                catch (UnauthorizedAccessException ex)
                {
                    req.Http.Response.StatusCode = 403;
                    return new ApiErrorResponse { Error = ApiResultEnum.Forbidden, Message = ex.Message };
                }
                catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = ex.Message };
                }
            },
            api => api
                .WithTag("Harbors")
                .WithSummary("Update a Harbor")
                .WithDescription("Updates operator-editable fields (name, capacity, enabled). Runtime state is managed by the link.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Harbor ID (hbr_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<Harbor>("Harbor update request", true))
                .WithResponse(200, OpenApiJson.For<Harbor>("Updated Harbor"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithSecurity("ApiKey"));

            app.Delete("/api/v1/harbors/{id}", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                try
                {
                    await _Harbors.DeleteAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                    req.Http.Response.StatusCode = 204;
                    return null;
                }
                catch (KeyNotFoundException ex)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = ex.Message };
                }
                catch (UnauthorizedAccessException ex)
                {
                    req.Http.Response.StatusCode = 403;
                    return new ApiErrorResponse { Error = ApiResultEnum.Forbidden, Message = ex.Message };
                }
            },
            api => api
                .WithTag("Harbors")
                .WithSummary("Delete a Harbor")
                .WithDescription("Deletes one Harbor registration within the caller scope.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Harbor ID (hbr_ prefix)"))
                .WithResponse(204, OpenApiResponseMetadata.NoContent())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/harbors/{id}/jobs", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                EnumerationQuery query = new EnumerationQuery();
                query.ApplyQuerystringOverrides(key => req.Query.GetValueOrDefault(key));
                bool openOnly = String.Equals(req.Query.GetValueOrDefault("open"), "true", StringComparison.OrdinalIgnoreCase);
                EnumerationResult<HarborJobRecord>? page = _Jobs == null ? null
                    : await _Jobs.EnumerateJobsAsync(ctx, req.Parameters["id"], openOnly, query).ConfigureAwait(false);
                if (page == null)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Harbor not found" };
                }

                return page;
            },
            api => api
                .WithTag("Harbors")
                .WithSummary("List a Harbor's jobs")
                .WithDescription("One page of the captain launches the Admiral recorded on a Harbor, newest first. open=true lists only jobs that have not ended; otherwise createdAfter and createdBefore bound the launch time (default: the last 7 days).")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Harbor ID (hbr_ prefix)"))
                .WithParameter(OpenApiParameterMetadata.Query("open", "true to list only jobs that have not ended", false))
                .WithParameter(OpenApiParameterMetadata.Query("pageNumber", "Page number (1-based)", false))
                .WithParameter(OpenApiParameterMetadata.Query("pageSize", "Page size (1 to 1000, default 10)", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<HarborJobRecord>>("Jobs"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/harbors/{id}/jobs/enumerate", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                EnumerationQuery query = EnumerationRequestReader.Read(req, _BodyJsonOptions);
                bool openOnly = String.Equals(req.Query.GetValueOrDefault("open"), "true", StringComparison.OrdinalIgnoreCase);
                EnumerationResult<HarborJobRecord>? page = _Jobs == null ? null
                    : await _Jobs.EnumerateJobsAsync(ctx, req.Parameters["id"], openOnly, query).ConfigureAwait(false);
                if (page == null)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Harbor not found" };
                }

                return page;
            },
            api => api
                .WithTag("Harbors")
                .WithSummary("Enumerate a Harbor's jobs")
                .WithDescription("One page of the captain launches the Admiral recorded on a Harbor, newest first. The JSON body (EnumerationQuery) carries PageNumber, PageSize (1 to 1000, default 10), CreatedAfter, and CreatedBefore (default: the last 7 days); open=true in the querystring lists only jobs that have not ended.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Harbor ID (hbr_ prefix)"))
                .WithParameter(OpenApiParameterMetadata.Query("open", "true to list only jobs that have not ended", false))
                .WithRequestBody(OpenApiJson.BodyFor<EnumerationQuery>("Paging and filters", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<HarborJobRecord>>("One page"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/harbors/{id}/jobs/{jobId}/stop", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);
                if (_Jobs == null)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Harbor job control is not available" };
                }

                HarborJobStopResult result = await _Jobs.StopJobAsync(ctx, req.Parameters["id"], req.Parameters["jobId"]).ConfigureAwait(false);
                switch (result.Status)
                {
                    case HarborJobStopStatusEnum.NotFound:
                        req.Http.Response.StatusCode = 404;
                        return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = result.Message };
                    case HarborJobStopStatusEnum.HarborNotConnected:
                        req.Http.Response.StatusCode = 409;
                        return new ApiErrorResponse { Error = ApiResultEnum.Conflict, Message = result.Message };
                    case HarborJobStopStatusEnum.Requested:
                        req.Http.Response.StatusCode = 202;
                        return result;
                    default:
                        return result;
                }
            },
            api => api
                .WithTag("Harbors")
                .WithSummary("Stop a Harbor job")
                .WithDescription("Asks a connected Harbor to stop one job by ID, for an agent no mission or captain still points at. 202 when the stop was sent (the Harbor reports the exit), 200 when the job had already ended, 409 when the Harbor is not connected.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Harbor ID (hbr_ prefix)"))
                .WithParameter(OpenApiParameterMetadata.Path("jobId", "Job ID as listed by GET /api/v1/harbors/{id}/jobs"))
                .WithResponse(202, OpenApiJson.For<HarborJobStopResult>("Stop sent"))
                .WithResponse(200, OpenApiJson.For<HarborJobStopResult>("Job had already ended"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithResponse(409, OpenApiJson.For<ApiErrorResponse>("Harbor not connected"))
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/harbors/{id}/enable", async (ApiRequest req) =>
            {
                return await SetEnabledAsync(req, authenticate, authz, true).ConfigureAwait(false);
            },
            api => api
                .WithTag("Harbors")
                .WithSummary("Enable a Harbor")
                .WithDescription("Enables a Harbor so the router may dispatch new missions to it.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Harbor ID (hbr_ prefix)"))
                .WithResponse(200, OpenApiJson.For<Harbor>("Updated Harbor"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/harbors/{id}/disable", async (ApiRequest req) =>
            {
                return await SetEnabledAsync(req, authenticate, authz, false).ConfigureAwait(false);
            },
            api => api
                .WithTag("Harbors")
                .WithSummary("Disable a Harbor")
                .WithDescription("Disables a Harbor so it keeps its docks but receives no new missions.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Harbor ID (hbr_ prefix)"))
                .WithResponse(200, OpenApiJson.For<Harbor>("Updated Harbor"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/harbors/{id}/probe", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                Harbor? harbor = await _Harbors.ReadAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                if (harbor == null)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Harbor not found" };
                }

                HarborProbeRequest probe = new HarborProbeRequest();
                if (!string.IsNullOrWhiteSpace(req.Http.Request.DataAsString))
                    probe = JsonSerializer.Deserialize<HarborProbeRequest>(req.Http.Request.DataAsString, _BodyJsonOptions) ?? new HarborProbeRequest();
                try
                {
                    CollectionBounds.Validate(probe);
                }
                catch (CollectionLimitExceededException ex)
                {
                    return RouteErrorMapper.LimitExceeded(req, ex);
                }

                if (!_Connections.IsConnected(harbor.Id))
                {
                    req.Http.Response.StatusCode = 409;
                    return new ApiErrorResponse { Error = ApiResultEnum.Conflict, Message = "Harbor is not connected; no link to probe over." };
                }

                // Normalized once: a body can set Arguments or WorkingDirectory to JSON null, so neither the audit
                // record nor the command reads the raw request fields.
                HostCommandRequest command = probe.ToHostCommandRequest();

                if (_Database != null)
                {
                    await CommandAudit.RecordAsync(_Database, new CommandAuditRecord
                    {
                        Source = "HarborProbe",
                        Command = command.Executable + (command.Arguments.Count > 0 ? " " + String.Join(" ", command.Arguments) : String.Empty),
                        WorkingDirectory = command.WorkingDirectory,
                        Host = "Harbor " + harbor.Id,
                        TenantId = harbor.TenantId ?? ctx.TenantId,
                        UserId = ctx.UserId,
                        EntityType = "Harbor",
                        EntityId = harbor.Id
                    }).ConfigureAwait(false);
                }

                RemoteHostCommandExecutor executor = new RemoteHostCommandExecutor(_Connections, harbor.Id);
                return await executor.RunAsync(command).ConfigureAwait(false);
            },
            api => api
                .WithTag("Harbors")
                .WithSummary("Probe a Harbor")
                .WithDescription("Runs a one-off host command (default: git --version) on a connected Harbor over its link and returns the result. Verifies the end-to-end server-issues-work / Harbor-replies loop.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Harbor ID (hbr_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<HarborProbeRequest>("Optional command to run", false))
                .WithResponse(200, OpenApiJson.For<HostCommandResult>("Command result"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithResponse(409, OpenApiJson.For<ApiErrorResponse>("Conflicts with the current state"))
                .WithSecurity("ApiKey"));
        }

        private async Task<object?> SetEnabledAsync(
            ApiRequest req,
            Func<HttpContextBase, Task<AuthContext>> authenticate,
            IAuthorizationService authz,
            bool enabled)
        {
            AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
            if (ctx == null) return BuildAuthError(req);

            try
            {
                return await _Harbors.SetEnabledAsync(ctx, req.Parameters["id"], enabled).ConfigureAwait(false);
            }
            catch (KeyNotFoundException ex)
            {
                req.Http.Response.StatusCode = 404;
                return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = ex.Message };
            }
            catch (UnauthorizedAccessException ex)
            {
                req.Http.Response.StatusCode = 403;
                return new ApiErrorResponse { Error = ApiResultEnum.Forbidden, Message = ex.Message };
            }
        }

        private static ApiErrorResponse BuildAuthError(ApiRequest req)
        {
            return new ApiErrorResponse
            {
                Error = req.Http.Response.StatusCode == 401 ? ApiResultEnum.NotAuthorized : ApiResultEnum.Forbidden,
                Message = req.Http.Response.StatusCode == 401
                    ? "Authentication required"
                    : "You do not have permission to perform this action"
            };
        }

        private static async Task<AuthContext?> AuthorizeAsync(
            ApiRequest req,
            Func<HttpContextBase, Task<AuthContext>> authenticate,
            IAuthorizationService authz)
        {
            AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
            if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
            {
                req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                return null;
            }

            return ctx;
        }
    }
}

namespace Armada.Server.Routes
{
    using System;
    using System.Globalization;
    using System.Text.Json;
    using System.Threading.Tasks;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;
    using Armada.Core;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// REST routes for model alerts (scorecard thresholds crossed, with suggested actions) under /api/v1/model-alerts.
    /// Every operation enforces the same rules as MCP through <see cref="ModelAlertAccessService"/>.
    /// </summary>
    public class ModelAlertRoutes
    {
        #region Private-Members

        private readonly ModelAlertAccessService _Service;
        private readonly JsonSerializerOptions _JsonOptions;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="service">Caller-scoped model alert service.</param>
        /// <param name="jsonOptions">Serializer options for request bodies.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public ModelAlertRoutes(ModelAlertAccessService service, JsonSerializerOptions jsonOptions)
        {
            _Service = service ?? throw new ArgumentNullException(nameof(service));
            _JsonOptions = jsonOptions ?? throw new ArgumentNullException(nameof(jsonOptions));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register routes with the application.
        /// </summary>
        /// <param name="app">Web server.</param>
        /// <param name="authenticate">Authenticator.</param>
        /// <param name="authz">Route authorization.</param>
        public void Register(Webserver app, Func<HttpContextBase, Task<AuthContext>> authenticate, IAuthorizationService authz)
        {
            app.Get("/api/v1/model-alerts", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                try
                {
                    return await _Service.ListAsync(ctx, ParseQuery(key => req.Query.GetValueOrDefault(key))).ConfigureAwait(false);
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex);
                }
            },
            api => api
                .WithTag("ModelAlerts")
                .WithSummary("List model alerts")
                .WithDescription("One page of model alerts (a scorecard threshold crossed for a model profile, revision group, persona and vessel scope), most recently raised first, each with its counts, applied-action record and suggested actions (with availability and the reason when unavailable). A global admin sees every tenant; everyone else sees their own tenant. Alerts are advisory: nothing changes until a suggested action is applied.")
                .WithParameter(OpenApiParameterMetadata.Query("tenantId", "Tenant filter (global admins only; ignored otherwise)", false))
                .WithParameter(OpenApiParameterMetadata.Query("status", "Open or Resolved (default both)", false))
                .WithParameter(OpenApiParameterMetadata.Query("profileId", "Model profile ID (mdl_ prefix)", false))
                .WithParameter(OpenApiParameterMetadata.Query("persona", "Persona name (case-insensitive)", false))
                .WithParameter(OpenApiParameterMetadata.Query("includeIgnored", "Include alerts hidden by Ignore (default true)", false, OpenApiSchemaMetadata.Boolean()))
                .WithParameter(OpenApiParameterMetadata.Query("pageNumber", "Page number (default 1)", false, OpenApiSchemaMetadata.Integer()))
                .WithParameter(OpenApiParameterMetadata.Query("pageSize", "Page size (1-1000, default 100)", false, OpenApiSchemaMetadata.Integer()))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<ModelAlert>>("One page"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/model-alerts/enumerate", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                try
                {
                    return await _Service.ListAsync(ctx, ReadBody<ModelAlertQuery>(req)).ConfigureAwait(false);
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex);
                }
            },
            api => api
                .WithTag("ModelAlerts")
                .WithSummary("Enumerate model alerts")
                .WithDescription("One page of model alerts visible to the caller, most recently raised first. The JSON body (ModelAlertQuery) carries TenantId (global admins only), Status, ProfileId, Persona, IncludeIgnored (default true), PageNumber, and PageSize (1-1000, default 100).")
                .WithRequestBody(OpenApiJson.BodyFor<ModelAlertQuery>("Paging and filters", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<ModelAlert>>("One page"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/model-alerts/evaluate", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                try
                {
                    return await _Service.EvaluateAsync(ctx).ConfigureAwait(false);
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex);
                }
            },
            api => api
                .WithTag("ModelAlerts")
                .WithSummary("Evaluate model alerts now")
                .WithDescription("Runs one alert evaluation pass now (the periodic pass runs every ModelAlerts.IntervalMinutes, default 60): the scorecard of every (model profile, persona) with production attempts in the last 30 days is checked against the thresholds; crossed conditions raise or keep one alert each, conditions that no longer hold are resolved. Targets lists every rule result, including inactive (Unavailable) rules and rules whose minimum count is not met, with the reason. Reads scorecard data only; never changes captains, personas, templates or routing. Tenant admins evaluate their tenant, global admins every tenant (403 otherwise).")
                .WithResponse(200, OpenApiJson.For<ModelAlertEvaluation>("The evaluation"))
                .WithResponse(403, OpenApiJson.For<ApiErrorResponse>("Not an admin"))
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/model-alerts/thresholds", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                try
                {
                    return _Service.GetThresholds(ctx);
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex);
                }
            },
            api => api
                .WithTag("ModelAlerts")
                .WithSummary("Get model alert thresholds")
                .WithDescription("The alert thresholds: Defaults (every value set; configured values, else the built-in defaults: first pass below 0.5 over at least 8 missions, 3 escalations away in a row, protocol repairs above 0.3 over at least 8 attempts, tokens per accepted result above 2x the persona median across other profiles with at least 5 accepted results on each side) and per-persona Overrides (a null value keeps the default).")
                .WithResponse(200, OpenApiJson.For<ModelAlertThresholds>("The thresholds"))
                .WithSecurity("ApiKey"));

            app.Put("/api/v1/model-alerts/thresholds", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                try
                {
                    return await _Service.UpdateThresholdsAsync(ctx, ReadBody<ModelAlertThresholds>(req)).ConfigureAwait(false);
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex);
                }
            },
            api => api
                .WithTag("ModelAlerts")
                .WithSummary("Update model alert thresholds")
                .WithDescription("Replaces the alert thresholds (Defaults and per-persona Overrides) in the server settings; the next evaluation pass uses them. Rates are 0 to 1, minimum counts 1 to 1000, the cost multiplier 1 to 100; each persona may have one override. Global admins only (403 otherwise).")
                .WithRequestBody(OpenApiJson.BodyFor<ModelAlertThresholds>("Defaults and Overrides"))
                .WithResponse(200, OpenApiJson.For<ModelAlertThresholds>("The stored thresholds"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(403, OpenApiJson.For<ApiErrorResponse>("Not an admin"))
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/model-alerts/{id}", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                try
                {
                    return await _Service.GetAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex);
                }
            },
            api => api
                .WithTag("ModelAlerts")
                .WithSummary("Get a model alert")
                .WithDescription("One model alert with its counts, applied-action record and suggested actions (MoveDown, RemoveFromPersona, CompactTemplate, Reevaluate, Ignore), each with availability and the reason when unavailable. 404 when the alert does not exist or belongs to another tenant.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Model alert ID (mal_ prefix)"))
                .WithResponse(200, OpenApiJson.For<ModelAlert>("The alert"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/model-alerts/{id}/apply", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                try
                {
                    return await _Service.ApplyAsync(ctx, req.Parameters["id"], ReadBody<ModelAlertApplyRequest>(req)).ConfigureAwait(false);
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex);
                }
            },
            api => api
                .WithTag("ModelAlerts")
                .WithSummary("Apply a model alert suggested action")
                .WithDescription("Applies one suggested action, only now and only this one: MoveDown moves the persona captain priority entries that select only this profile's captains to the bottom; RemoveFromPersona removes them and clears the default captain when it is on this profile (both through the persona update); Reevaluate starts a Quick Evaluate of a captain on this profile within the evaluation budgets; Ignore hides the alert for Days (1-365, default 7); CompactTemplate is unavailable (no prompt variant per profile and persona). The result is recorded on the alert. The alert is resolved before the body is validated. 400 for a missing, unknown or unavailable action; 403 unless a tenant admin of the alert's tenant or a global admin; 404 when the alert does not exist or belongs to another tenant.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Model alert ID (mal_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<ModelAlertApplyRequest>("Action and, for Ignore, Days"))
                .WithResponse(200, OpenApiJson.For<ModelAlertApplyResult>("The record and the alert"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(403, OpenApiJson.For<ApiErrorResponse>("Not an admin"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/model-alerts/{id}/ignore", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                try
                {
                    return await _Service.IgnoreAsync(ctx, req.Parameters["id"], ReadBody<ModelAlertIgnoreRequest>(req).Days).ConfigureAwait(false);
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex);
                }
            },
            api => api
                .WithTag("ModelAlerts")
                .WithSummary("Ignore a model alert")
                .WithDescription("Hides the alert from the inbox for Days (1-365, default ModelAlerts.DefaultIgnoreDays, 7); it shows again afterwards if the condition still holds. Recorded on the alert. 400 for days out of range; 403 unless a tenant admin of the alert's tenant or a global admin; 404 when the alert does not exist or belongs to another tenant.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Model alert ID (mal_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<ModelAlertIgnoreRequest>("Days", false))
                .WithResponse(200, OpenApiJson.For<ModelAlertApplyResult>("The record and the alert"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(403, OpenApiJson.For<ApiErrorResponse>("Not an admin"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));
        }

        /// <summary>
        /// Parse list filters from name/value pairs (querystring); shared with MCP.
        /// </summary>
        /// <param name="value">Reads one value by name, or null.</param>
        /// <returns>The query.</returns>
        /// <exception cref="ArgumentNullException">Thrown when value is null.</exception>
        /// <exception cref="ArgumentException">Thrown for an invalid status, flag, page number or size.</exception>
        public static ModelAlertQuery ParseQuery(Func<string, string?> value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            ModelAlertQuery query = new ModelAlertQuery
            {
                TenantId = Blank(value("tenantId")),
                ProfileId = Blank(value("profileId")),
                Persona = Blank(value("persona"))
            };

            string? status = Blank(value("status"));
            if (status != null)
            {
                if (!Enum.TryParse(status, true, out ModelAlertStatusEnum parsedStatus) || !Enum.IsDefined(typeof(ModelAlertStatusEnum), parsedStatus))
                    throw new ArgumentException("Invalid status: " + status + " (Open or Resolved).");
                query.Status = parsedStatus;
            }
            string? includeIgnored = Blank(value("includeIgnored"));
            if (includeIgnored != null)
            {
                if (!Boolean.TryParse(includeIgnored, out bool parsedFlag)) throw new ArgumentException("Invalid includeIgnored: " + includeIgnored);
                query.IncludeIgnored = parsedFlag;
            }
            int? pageNumber = ParseInt(value("pageNumber"), "pageNumber");
            if (pageNumber.HasValue) query.PageNumber = pageNumber.Value;
            int? pageSize = ParseInt(value("pageSize"), "pageSize");
            if (pageSize.HasValue) query.PageSize = pageSize.Value;
            return query;
        }

        #endregion

        #region Private-Methods

        private T ReadBody<T>(ApiRequest req) where T : class, new()
        {
            string body = req.Http.Request.DataAsString ?? String.Empty;
            if (String.IsNullOrWhiteSpace(body)) return new T();
            try
            {
                return JsonSerializer.Deserialize<T>(body, _JsonOptions) ?? new T();
            }
            catch (JsonException ex)
            {
                throw new ArgumentException("Invalid request body: " + ex.Message, ex);
            }
        }

        private static ApiErrorResponse? Deny(ApiRequest req, AuthContext ctx, IAuthorizationService authz)
        {
            if (authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery)) return null;
            req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
            return new ApiErrorResponse
            {
                Error = ctx.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized,
                Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required"
            };
        }

        private static int? ParseInt(string? value, string name)
        {
            string? text = Blank(value);
            if (text == null) return null;
            if (!Int32.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)) throw new ArgumentException("Invalid " + name + ": " + text);
            return parsed;
        }

        private static string? Blank(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        #endregion
    }
}

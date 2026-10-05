namespace Armada.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading.Tasks;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;
    using Armada.Core;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// REST routes for CLI tool permissions: permission requests raised by CLI captains (list, read, decide), rules, and
    /// the captain and Ask thread policy overrides. Every operation enforces the same rules as MCP and WebSocket through
    /// <see cref="CliPermissionService"/> and <see cref="CliPermissionAccess"/>.
    /// </summary>
    public class CliPermissionRoutes
    {
        #region Private-Members

        private readonly CliPermissionService _Service;
        private readonly JsonSerializerOptions _JsonOptions;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="service">CLI permission service.</param>
        /// <param name="jsonOptions">Serializer options for request bodies.</param>
        public CliPermissionRoutes(CliPermissionService service, JsonSerializerOptions jsonOptions)
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
            app.Get("/api/v1/cli-permissions/requests", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                try
                {
                    CliPermissionRequestQuery query = new CliPermissionRequestQuery
                    {
                        MissionId = Blank(req.Query.GetValueOrDefault("missionId")),
                        ThreadId = Blank(req.Query.GetValueOrDefault("threadId")),
                        CaptainId = Blank(req.Query.GetValueOrDefault("captainId")),
                        VesselId = Blank(req.Query.GetValueOrDefault("vesselId"))
                    };
                    string? status = Blank(req.Query.GetValueOrDefault("status"));
                    if (status != null)
                    {
                        if (!EnumNames.TryParse(status, true, out CliPermissionRequestStatusEnum parsed)) throw new ArgumentException("Invalid status: " + status);
                        query.Status = parsed;
                    }

                    string? limit = Blank(req.Query.GetValueOrDefault("limit"));
                    if (limit != null)
                    {
                        if (!Int32.TryParse(limit, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int parsedLimit)) throw new ArgumentException("Invalid limit: " + limit);
                        query.Limit = parsedLimit;
                    }

                    return await _Service.ListAsync(ctx, query).ConfigureAwait(false);
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex);
                }
            },
            api => api
                .WithTag("CliPermissions")
                .WithSummary("List CLI permission requests")
                .WithDescription("Lists the permission prompts CLI captains raised for their own tools (newest first). Admins see their tenant (global admins every tenant); other users see requests from their own missions and Ask conversations. Inputs are redacted. Each request carries canDecide and canRemember for the caller.")
                .WithParameter(OpenApiParameterMetadata.Query("status", "Pending, Allowed, Denied, Expired, or Cancelled", false))
                .WithParameter(OpenApiParameterMetadata.Query("missionId", "Mission filter (msn_ prefix)", false))
                .WithParameter(OpenApiParameterMetadata.Query("threadId", "Ask thread filter (ath_ prefix)", false))
                .WithParameter(OpenApiParameterMetadata.Query("captainId", "Captain filter (cpt_ prefix)", false))
                .WithParameter(OpenApiParameterMetadata.Query("vesselId", "Vessel filter (vsl_ prefix)", false))
                .WithParameter(OpenApiParameterMetadata.Query("limit", "Maximum rows (1-1000, default 100)", false, OpenApiSchemaMetadata.Integer()))
                .WithResponse(200, OpenApiJson.For<List<CliPermissionRequest>>("Requests"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/cli-permissions/requests/{id}", async (ApiRequest req) =>
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
                .WithTag("CliPermissions")
                .WithSummary("Get a CLI permission request")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Request ID (cpr_ prefix)"))
                .WithResponse(200, OpenApiJson.For<CliPermissionRequest>("The request"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/cli-permissions/requests/{id}/decide", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                try
                {
                    CliPermissionDecisionRequest body = ReadBody<CliPermissionDecisionRequest>(req);
                    return await _Service.DecideAsync(ctx, req.Parameters["id"], body).ConfigureAwait(false);
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex, 409);
                }
            },
            api => api
                .WithTag("CliPermissions")
                .WithSummary("Decide a CLI permission request")
                .WithDescription("Allow once, allow and remember (also stores an allow rule; admins only), or deny a pending request; the waiting captain continues with the answer. Deciding requires a global admin or a tenant admin of the request's tenant, or the owner when Permissions.AllowOwnerApproval is on (403 otherwise). 409 when the request is no longer pending.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Request ID (cpr_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<CliPermissionDecisionRequest>("Decision", true))
                .WithResponse(200, OpenApiJson.For<CliPermissionRequest>("The decided request"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithResponse(409, OpenApiJson.For<ApiErrorResponse>("No longer pending"))
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/cli-permissions/rules", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                try
                {
                    CliPermissionRuleQuery query = new CliPermissionRuleQuery
                    {
                        VesselId = Blank(req.Query.GetValueOrDefault("vesselId")),
                        CaptainId = Blank(req.Query.GetValueOrDefault("captainId"))
                    };
                    string? scope = Blank(req.Query.GetValueOrDefault("scope"));
                    if (scope != null)
                    {
                        if (!EnumNames.TryParse(scope, true, out CliPermissionRuleScopeEnum parsed)) throw new ArgumentException("Invalid scope: " + scope);
                        query.Scope = parsed;
                    }

                    return await _Service.ListRulesAsync(ctx, query).ConfigureAwait(false);
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex);
                }
            },
            api => api
                .WithTag("CliPermissions")
                .WithSummary("List CLI permission rules")
                .WithDescription("Lists the rules visible to the caller: the caller's tenant plus rules for every tenant (global admins see all). Rules use Claude Code permission rule syntax; deny rules win over allow rules.")
                .WithParameter(OpenApiParameterMetadata.Query("scope", "Global, Vessel, or Captain", false))
                .WithParameter(OpenApiParameterMetadata.Query("vesselId", "Vessel filter (vsl_ prefix)", false))
                .WithParameter(OpenApiParameterMetadata.Query("captainId", "Captain filter (cpt_ prefix)", false))
                .WithResponse(200, OpenApiJson.For<List<CliPermissionRule>>("Rules"))
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/cli-permissions/rules", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                try
                {
                    CliPermissionRule body = ReadBody<CliPermissionRule>(req);
                    CliPermissionRule created = await _Service.CreateRuleAsync(ctx, body).ConfigureAwait(false);
                    req.Http.Response.StatusCode = 201;
                    return created;
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex);
                }
            },
            api => api
                .WithTag("CliPermissions")
                .WithSummary("Create a CLI permission rule")
                .WithDescription("Creates an allow or deny rule (admins). Pattern, Action, Scope (Global, Vessel, Captain), VesselId or CaptainId, Description; a global admin may set TenantId (omit for every tenant), a tenant admin's rules belong to their tenant. 400 for an invalid pattern.")
                .WithRequestBody(OpenApiJson.BodyFor<CliPermissionRule>("Rule", true))
                .WithResponse(201, OpenApiJson.For<CliPermissionRule>("The created rule"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/cli-permissions/rules/{id}", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                try
                {
                    return await _Service.GetRuleAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex);
                }
            },
            api => api
                .WithTag("CliPermissions")
                .WithSummary("Get a CLI permission rule")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Rule ID (cpl_ prefix)"))
                .WithResponse(200, OpenApiJson.For<CliPermissionRule>("The rule"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Put("/api/v1/cli-permissions/rules/{id}", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                try
                {
                    CliPermissionRule body = ReadBody<CliPermissionRule>(req);
                    return await _Service.UpdateRuleAsync(ctx, req.Parameters["id"], body).ConfigureAwait(false);
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex);
                }
            },
            api => api
                .WithTag("CliPermissions")
                .WithSummary("Update a CLI permission rule")
                .WithDescription("Updates the pattern, action, and description of a rule (admins of the rule's tenant; rules for every tenant need a global admin). Scope and target are fixed; delete and recreate to change them.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Rule ID (cpl_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<CliPermissionRule>("Rule", true))
                .WithResponse(200, OpenApiJson.For<CliPermissionRule>("The updated rule"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Delete("/api/v1/cli-permissions/rules/{id}", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                try
                {
                    await _Service.DeleteRuleAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                    req.Http.Response.StatusCode = 204;
                    return null;
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex);
                }
            },
            api => api
                .WithTag("CliPermissions")
                .WithSummary("Delete a CLI permission rule")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Rule ID (cpl_ prefix)"))
                .WithResponse(204, OpenApiResponseMetadata.Create("Deleted"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Put("/api/v1/captains/{id}/cli-permission-policy", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                try
                {
                    CliPermissionPolicyUpdateRequest body = ReadBody<CliPermissionPolicyUpdateRequest>(req);
                    return await _Service.SetCaptainPolicyAsync(ctx, req.Parameters["id"], body.Policy).ConfigureAwait(false);
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex);
                }
            },
            api => api
                .WithTag("Captains")
                .WithSummary("Set a captain's CLI tool permission policy")
                .WithDescription("Sets (Refuse, ApproveInArmada, Bypass) or clears (null) the captain's CliPermissionPolicy. Requires a global admin or a tenant admin of the captain's tenant. Bypass runs the CLI with its permission-bypass flag. Captain create and update leave the policy unchanged.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Captain ID (cpt_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<CliPermissionPolicyUpdateRequest>("Policy", true))
                .WithResponse(200, OpenApiJson.For<Captain>("The captain"))
                .WithResponse(403, OpenApiJson.For<ApiErrorResponse>("Not an admin of the captain's tenant"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Put("/api/v1/ask/threads/{id}/cli-permission-policy", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                try
                {
                    CliPermissionPolicyUpdateRequest body = ReadBody<CliPermissionPolicyUpdateRequest>(req);
                    return await _Service.SetThreadPolicyAsync(ctx, req.Parameters["id"], body.Policy).ConfigureAwait(false);
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex);
                }
            },
            api => api
                .WithTag("Ask")
                .WithSummary("Set a thread's CLI tool permission policy")
                .WithDescription("Sets (Refuse, ApproveInArmada, Bypass) or clears (null) the thread's CliPermissionPolicy override for its captain's turns. Only the thread owner; Bypass requires the owner to be a global admin or a tenant admin (403). Returns the thread with its resolved CliPermission.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Thread ID (ath_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<CliPermissionPolicyUpdateRequest>("Policy", true))
                .WithResponse(200, OpenApiJson.For<AskThread>("The thread"))
                .WithResponse(403, OpenApiJson.For<ApiErrorResponse>("Bypass requires an admin"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));
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

        private static string? Blank(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        #endregion
    }
}

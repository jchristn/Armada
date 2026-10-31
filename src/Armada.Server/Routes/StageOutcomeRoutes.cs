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
    /// REST routes for stage outcomes (one row per stage attempt with its provenance, disposition and suspected cause)
    /// and the failure-cause rules that classify them. Every operation enforces the same rules as MCP through
    /// <see cref="StageOutcomeAccessService"/>.
    /// </summary>
    public class StageOutcomeRoutes
    {
        #region Private-Members

        private readonly StageOutcomeAccessService _Service;
        private readonly JsonSerializerOptions _JsonOptions;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="service">Caller-scoped stage outcome service.</param>
        /// <param name="jsonOptions">Serializer options for request bodies.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public StageOutcomeRoutes(StageOutcomeAccessService service, JsonSerializerOptions jsonOptions)
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
            app.Get("/api/v1/stage-outcomes", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                try
                {
                    return await _Service.ListOutcomesAsync(ctx, ReadQuery(req)).ConfigureAwait(false);
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex);
                }
            },
            api => api
                .WithTag("StageOutcomes")
                .WithSummary("List stage outcomes")
                .WithDescription("One page of stage outcomes (one per stage attempt), newest first. A global admin sees every tenant (tenantId filters); everyone else sees their own tenant. Filters are optional; enum filters take the names of FailureCauseEnum, FailureCauseSourceEnum and StageDispositionEnum.")
                .WithParameter(OpenApiParameterMetadata.Query("tenantId", "Tenant filter (global admins only; ignored otherwise)", false))
                .WithParameter(OpenApiParameterMetadata.Query("missionId", "Mission filter (msn_ prefix)", false))
                .WithParameter(OpenApiParameterMetadata.Query("voyageId", "Voyage filter (vyg_ prefix)", false))
                .WithParameter(OpenApiParameterMetadata.Query("vesselId", "Vessel filter (vsl_ prefix)", false))
                .WithParameter(OpenApiParameterMetadata.Query("captainId", "Captain filter (cpt_ prefix)", false))
                .WithParameter(OpenApiParameterMetadata.Query("persona", "Persona name (exact, case-insensitive)", false))
                .WithParameter(OpenApiParameterMetadata.Query("model", "Model (exact, case-insensitive)", false))
                .WithParameter(OpenApiParameterMetadata.Query("suspectedCause", "Protocol, InstructionFollowing, ToolUse, Context, Capability, NotTheModel, Unknown, Mixed, or None", false))
                .WithParameter(OpenApiParameterMetadata.Query("causeSource", "None, Rule, Judge, or Manual", false))
                .WithParameter(OpenApiParameterMetadata.Query("disposition", "Completed, Relaunched, OutputRepair, Escalated, Failed, Interrupted, Cancelled, or Pending", false))
                .WithParameter(OpenApiParameterMetadata.Query("isEvaluation", "true for evaluation attempts only, false for production only", false, OpenApiSchemaMetadata.Boolean()))
                .WithParameter(OpenApiParameterMetadata.Query("fromUtc", "Ended at or after (ISO 8601 UTC)", false))
                .WithParameter(OpenApiParameterMetadata.Query("toUtc", "Ended before (ISO 8601 UTC)", false))
                .WithParameter(OpenApiParameterMetadata.Query("pageNumber", "Page number (default 1)", false, OpenApiSchemaMetadata.Integer()))
                .WithParameter(OpenApiParameterMetadata.Query("pageSize", "Page size (1-1000, default 100)", false, OpenApiSchemaMetadata.Integer()))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<StageOutcome>>("One page"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/stage-outcomes/enumerate", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                try
                {
                    return await _Service.ListOutcomesAsync(ctx, ReadBody<StageOutcomeQuery>(req)).ConfigureAwait(false);
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex);
                }
            },
            api => api
                .WithTag("StageOutcomes")
                .WithSummary("Enumerate stage outcomes")
                .WithDescription("One page of stage outcomes visible to the caller, newest first. The JSON body (StageOutcomeQuery) carries the same filters as GET /api/v1/stage-outcomes plus PageNumber and PageSize (1-1000, default 100).")
                .WithRequestBody(OpenApiJson.BodyFor<StageOutcomeQuery>("Paging and filters", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<StageOutcome>>("One page"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/stage-outcomes/reclassify", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                try
                {
                    StageOutcomeQuery filter = ReadBody<StageOutcomeQuery>(req);
                    return await _Service.ReclassifyAsync(ctx, filter).ConfigureAwait(false);
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex);
                }
            },
            api => api
                .WithTag("StageOutcomes")
                .WithSummary("Re-apply the failure-cause rules")
                .WithDescription("Re-applies the enabled failure-cause rules to every stage outcome matching the filter (paging is ignored), so a new or edited rule takes effect on past attempts. Outcomes classified by hand are left alone and counted as skippedManual. Global admins (any tenant, or every tenant with no tenantId) and tenant admins (their own tenant) only; 403 otherwise.")
                .WithRequestBody(OpenApiJson.BodyFor<StageOutcomeQuery>("Filter (every field optional)", false))
                .WithResponse(200, OpenApiJson.For<StageOutcomeReclassifyResult>("What was examined and changed"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(403, OpenApiJson.For<ApiErrorResponse>("Not an admin"))
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/stage-outcomes/{id}", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                try
                {
                    return await _Service.GetOutcomeAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex);
                }
            },
            api => api
                .WithTag("StageOutcomes")
                .WithSummary("Get a stage outcome")
                .WithDescription("404 when the outcome does not exist or belongs to another tenant.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Outcome ID (sto_ prefix)"))
                .WithResponse(200, OpenApiJson.For<StageOutcome>("The outcome"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Put("/api/v1/stage-outcomes/{id}/cause", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                try
                {
                    StageOutcomeClassifyRequest body = ReadBody<StageOutcomeClassifyRequest>(req);
                    return await _Service.ClassifyAsync(ctx, req.Parameters["id"], body).ConfigureAwait(false);
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex);
                }
            },
            api => api
                .WithTag("StageOutcomes")
                .WithSummary("Classify a stage outcome by hand")
                .WithDescription("Sets the suspected cause by hand (causeSource Manual, classifiedByUserId the caller); rules never overwrite a manual cause. A null cause clears the manual cause and re-applies the rules. 400 for cause None; 404 when the outcome does not exist or belongs to another tenant.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Outcome ID (sto_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<StageOutcomeClassifyRequest>("Cause (null to hand back to the rules) and note", true))
                .WithResponse(200, OpenApiJson.For<StageOutcome>("The updated outcome"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/stage-outcomes/{id}/rule", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                try
                {
                    return await _Service.DraftRuleAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex, 409);
                }
            },
            api => api
                .WithTag("StageOutcomes")
                .WithSummary("Draft a failure-cause rule from a stage outcome")
                .WithDescription("Returns an unsaved failure-cause rule prefilled from the outcome: its cause, its tenant, priority 5 (ahead of the built-in rules), and a match on its failure kind, runtime failure kind, stage-check status and reason, disposition and persona. Nothing is stored: edit the draft and POST it to /api/v1/failure-cause-rules. 409 when the outcome has no cause (None).")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Outcome ID (sto_ prefix)"))
                .WithResponse(200, OpenApiJson.For<FailureCauseRule>("The unsaved draft"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithResponse(409, OpenApiJson.For<ApiErrorResponse>("The outcome has no cause"))
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/failure-cause-rules", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                try
                {
                    EnumerationQuery page = new EnumerationQuery();
                    page.ApplyQuerystringOverrides(key => QueryValueReader.Read(req.Query, key));
                    return await _Service.ListRulesAsync(ctx, page).ConfigureAwait(false);
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex);
                }
            },
            api => api
                .WithTag("FailureCauseRules")
                .WithSummary("List failure-cause rules")
                .WithDescription("One page of the rules visible to the caller in evaluation order (priority ascending, then name): the caller's tenant plus the rules for every tenant (tenantId null), built-in rules included; global admins see every rule. The first enabled matching rule assigns a stage outcome's suspected cause.")
                .WithParameter(OpenApiParameterMetadata.Query("pageNumber", "Page number (default 1)", false, OpenApiSchemaMetadata.Integer()))
                .WithParameter(OpenApiParameterMetadata.Query("pageSize", "Page size (1-1000, default 10)", false, OpenApiSchemaMetadata.Integer()))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<FailureCauseRule>>("One page"))
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/failure-cause-rules/enumerate", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                try
                {
                    return await _Service.ListRulesAsync(ctx, ReadBody<EnumerationQuery>(req)).ConfigureAwait(false);
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex);
                }
            },
            api => api
                .WithTag("FailureCauseRules")
                .WithSummary("Enumerate failure-cause rules")
                .WithDescription("One page of the failure-cause rules visible to the caller in evaluation order (priority ascending, then name). The JSON body (EnumerationQuery) carries PageNumber and PageSize (1-1000, default 10).")
                .WithRequestBody(OpenApiJson.BodyFor<EnumerationQuery>("Paging", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<FailureCauseRule>>("One page"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/failure-cause-rules", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                try
                {
                    FailureCauseRule body = ReadBody<FailureCauseRule>(req);
                    FailureCauseRule created = await _Service.CreateRuleAsync(ctx, body).ConfigureAwait(false);
                    req.Http.Response.StatusCode = 201;
                    return created;
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex, 409);
                }
            },
            api => api
                .WithTag("FailureCauseRules")
                .WithSummary("Create a failure-cause rule")
                .WithDescription("Creates a rule from name, description, priority, enabled, match and cause. A global admin may set tenantId (omit for every tenant); anyone else creates a rule for their own tenant. isBuiltIn is always false. 400 when the rule is invalid (no name, a name over 200 characters, no match criterion, cause None, or an invalid textPattern regular expression); 409 when the tenant already has a rule with that name.")
                .WithRequestBody(OpenApiJson.BodyFor<FailureCauseRule>("Rule", true))
                .WithResponse(201, OpenApiJson.For<FailureCauseRule>("The created rule"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(409, OpenApiJson.For<ApiErrorResponse>("Duplicate name"))
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/failure-cause-rules/{id}", async (ApiRequest req) =>
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
                .WithTag("FailureCauseRules")
                .WithSummary("Get a failure-cause rule")
                .WithDescription("404 when the rule does not exist or belongs to another tenant.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Rule ID (fcr_ prefix)"))
                .WithResponse(200, OpenApiJson.For<FailureCauseRule>("The rule"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Put("/api/v1/failure-cause-rules/{id}", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Deny(req, ctx, authz);
                if (denied != null) return denied;
                try
                {
                    FailureCauseRule body = ReadBody<FailureCauseRule>(req);
                    return await _Service.UpdateRuleAsync(ctx, req.Parameters["id"], body).ConfigureAwait(false);
                }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex))
                {
                    return RouteErrorMapper.ToResponse(req, ex, 409);
                }
            },
            api => api
                .WithTag("FailureCauseRules")
                .WithSummary("Update a failure-cause rule")
                .WithDescription("Replaces the name, description, priority, enabled, match and cause of a rule; tenantId and isBuiltIn are fixed. Rules for every tenant and built-in rules need a global admin (403 otherwise). An edited built-in rule is never overwritten when the Admiral seeds the built-ins. 400 when the result is invalid; 409 for a duplicate name.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Rule ID (fcr_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<FailureCauseRule>("Rule", true))
                .WithResponse(200, OpenApiJson.For<FailureCauseRule>("The updated rule"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(403, OpenApiJson.For<ApiErrorResponse>("A rule for every tenant or a built-in rule, and not a global admin"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithResponse(409, OpenApiJson.For<ApiErrorResponse>("Duplicate name"))
                .WithSecurity("ApiKey"));

            app.Delete("/api/v1/failure-cause-rules/{id}", async (ApiRequest req) =>
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
                .WithTag("FailureCauseRules")
                .WithSummary("Delete a failure-cause rule")
                .WithDescription("Rules for every tenant and built-in rules need a global admin (403 otherwise). A deleted built-in rule is stored again the next time the Admiral starts, because seeding restores every missing built-in by name; disable a built-in (enabled false) to keep it out of classification for good.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Rule ID (fcr_ prefix)"))
                .WithResponse(204, OpenApiResponseMetadata.Create("Deleted"))
                .WithResponse(403, OpenApiJson.For<ApiErrorResponse>("A rule for every tenant or a built-in rule, and not a global admin"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));
        }

        /// <summary>
        /// Read a stage outcome query from querystring values (names as in <see cref="StageOutcomeQuery"/>, camelCase).
        /// </summary>
        /// <param name="value">Reads one querystring value by name, or null when absent.</param>
        /// <returns>The query.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="value"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown for a value that does not parse.</exception>
        public static StageOutcomeQuery ParseQuery(Func<string, string?> value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            StageOutcomeQuery query = new StageOutcomeQuery
            {
                TenantId = Blank(value("tenantId")),
                MissionId = Blank(value("missionId")),
                VoyageId = Blank(value("voyageId")),
                VesselId = Blank(value("vesselId")),
                CaptainId = Blank(value("captainId")),
                Persona = Blank(value("persona")),
                Model = Blank(value("model"))
            };

            string? cause = Blank(value("suspectedCause"));
            if (cause != null)
            {
                if (!EnumNames.TryParse(cause, true, out FailureCauseEnum parsed)) throw new ArgumentException("Invalid suspectedCause: " + cause);
                query.SuspectedCause = parsed;
            }

            string? source = Blank(value("causeSource"));
            if (source != null)
            {
                if (!EnumNames.TryParse(source, true, out FailureCauseSourceEnum parsed)) throw new ArgumentException("Invalid causeSource: " + source);
                query.CauseSource = parsed;
            }

            string? disposition = Blank(value("disposition"));
            if (disposition != null)
            {
                if (!EnumNames.TryParse(disposition, true, out StageDispositionEnum parsed)) throw new ArgumentException("Invalid disposition: " + disposition);
                query.Disposition = parsed;
            }

            string? evaluation = Blank(value("isEvaluation"));
            if (evaluation != null)
            {
                if (!Boolean.TryParse(evaluation, out bool parsed)) throw new ArgumentException("Invalid isEvaluation: " + evaluation);
                query.IsEvaluation = parsed;
            }

            query.FromUtc = ParseUtc(value("fromUtc"), "fromUtc");
            query.ToUtc = ParseUtc(value("toUtc"), "toUtc");
            int? pageNumber = ParseInt(value("pageNumber"), "pageNumber");
            if (pageNumber.HasValue) query.PageNumber = pageNumber.Value;
            int? pageSize = ParseInt(value("pageSize"), "pageSize");
            if (pageSize.HasValue) query.PageSize = pageSize.Value;
            return query;
        }

        #endregion

        #region Private-Methods

        private static StageOutcomeQuery ReadQuery(ApiRequest req)
        {
            return ParseQuery(key => QueryValueReader.Read(req.Query, key));
        }

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

        private static DateTime? ParseUtc(string? value, string name)
        {
            string? text = Blank(value);
            if (text == null) return null;
            if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime parsed))
                throw new ArgumentException("Invalid " + name + ": " + text);
            return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
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

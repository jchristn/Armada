namespace Armada.Server.Routes
{
    using System;
    using System.Globalization;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>Native Watson routes over the shared scoped selection access service.</summary>
    public sealed class CaptainSelectionRoutes
    {
        private readonly CaptainSelectionAccessService _Service;
        private readonly JsonSerializerOptions _Options;

        /// <summary>Create selection routes using the server's JSON contract.</summary>
        public CaptainSelectionRoutes(CaptainSelectionAccessService service, JsonSerializerOptions options)
        { _Service = service; _Options = options; }

        /// <summary>Register read-only history and side-effect-free preview routes with OpenAPI and shared authorization.</summary>
        public void Register(Webserver app, Func<HttpContextBase, Task<AuthContext>> authenticate, IAuthorizationService authz)
        {
            RegisterOne(app, authenticate, authz, HttpMethod.GET, "/api/v1/missions/{id}/captain-selection", "summary");
            RegisterOne(app, authenticate, authz, HttpMethod.GET, "/api/v1/missions/{id}/captain-selection/history", "history");
            RegisterOne(app, authenticate, authz, HttpMethod.POST, "/api/v1/missions/{id}/captain-selection/preview", "preview");
            RegisterOne(app, authenticate, authz, HttpMethod.POST, "/api/v1/missions/{id}/captain-selection/history/enumerate", "history");
            RegisterOne(app, authenticate, authz, HttpMethod.GET, "/api/v1/captain-selection-decisions/{id}", "detail");
            RegisterOne(app, authenticate, authz, HttpMethod.GET, "/api/v1/captain-selection-decisions/{id}/candidates", "candidates");
            RegisterOne(app, authenticate, authz, HttpMethod.POST, "/api/v1/captain-selection-decisions/{id}/candidates/enumerate", "candidates");
        }

        private void RegisterOne(Webserver app, Func<HttpContextBase, Task<AuthContext>> authenticate, IAuthorizationService authz, HttpMethod method, string path, string kind)
        {
            app.Routes.PostAuthentication.Parameter.Add(method, path, async context =>
            {
                AuthContext caller = await authenticate(context).ConfigureAwait(false);
                object payload;
                if (!authz.IsAuthorized(caller, context.Request.Method.ToString(), context.Request.Url.RawWithoutQuery))
                {
                    context.Response.StatusCode = caller.IsAuthenticated ? 403 : 401;
                    payload = new ApiErrorResponse { Error = caller.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized, Message = "Selection access denied." };
                }
                else
                {
                    try
                    {
                        string id = context.Request.Url.Parameters["id"] ?? throw new ArgumentException("Identifier is required.");
                        string? tenant = context.Request.RetrieveQueryValue("tenantId");
                        await _Service.ValidateScopeAsync(caller, id, kind == "detail" || kind == "candidates", tenant, context.Token).ConfigureAwait(false);
                        bool enumerationBody = method == HttpMethod.POST && (kind == "history" || kind == "candidates");
                        CaptainSelectionHistoryQuery? filter = enumerationBody
                            ? String.IsNullOrWhiteSpace(context.Request.DataAsString) ? new CaptainSelectionHistoryQuery()
                                : JsonSerializer.Deserialize<CaptainSelectionHistoryQuery>(context.Request.DataAsString, _Options) ?? new CaptainSelectionHistoryQuery()
                            : null;
                        int page = filter?.PageNumber ?? Number(context, "pageNumber", 1), size = filter?.PageSize ?? Number(context, "pageSize", 25);
                        payload = kind switch
                        {
                            "summary" => await _Service.SummaryAsync(caller, id, context.Token).ConfigureAwait(false),
                            "history" => await _Service.HistoryAsync(caller, id, filter ?? new CaptainSelectionHistoryQuery { PageNumber = page, PageSize = size,
                                Trigger = context.Request.RetrieveQueryValue("trigger"), Outcome = context.Request.RetrieveQueryValue("outcome"),
                                FromUtc = Date(context, "from"), ToUtc = Date(context, "to") }, context.Token).ConfigureAwait(false),
                            "preview" => await _Service.PreviewAsync(caller, id, page, size, context.Token).ConfigureAwait(false),
                            "candidates" => await _Service.CandidatesAsync(caller, id, tenant, page, size, context.Token).ConfigureAwait(false),
                            _ => await _Service.DetailAsync(caller, id, tenant, page, size, context.Token).ConfigureAwait(false)
                        };
                    }
                    catch (Exception error) when (RouteErrorMapper.IsMapped(error))
                    {
                        context.Response.StatusCode = RouteErrorMapper.StatusCodeFor(error, 409);
                        payload = new ApiErrorResponse { Error = RouteErrorMapper.ResultFor(context.Response.StatusCode), Message = error.Message };
                    }
                    catch (System.Data.Common.DbException)
                    {
                        context.Response.StatusCode = 503;
                        payload = new ApiStatusErrorResponse(Armada.Core.Enums.ApiStatusErrorCodeEnum.ServiceUnavailable, "Selection evidence is temporarily unavailable.");
                    }
                }
                context.Response.ContentType = "application/json";
                await context.Response.Send(JsonSerializer.Serialize(payload, _Options)).ConfigureAwait(false);
            }, openApiMetadata: Metadata(path, kind, method));
        }

        private static OpenApiRouteMetadata Metadata(string path, string kind, HttpMethod method)
        {
            OpenApiRouteMetadata metadata = OpenApiRouteMetadata.Create("Read captain selection " + kind, new[] { "Captain selection" })
                .WithParameter(OpenApiParameterMetadata.Path("id", "Mission or decision identifier"))
                .WithSecurity("ApiKey");
            if (method == HttpMethod.POST && (kind == "history" || kind == "candidates"))
                metadata.WithRequestBody(OpenApiJson.BodyFor<CaptainSelectionHistoryQuery>("Scoped filters and pagination; tenant scope comes from the authenticated caller and path", false));
            if (kind != "summary")
                foreach (string name in new[] { "pageNumber", "pageSize" }) metadata.WithParameter(OpenApiParameterMetadata.Query(name, "One-based page; pageSize 1..1000, default 25", false));
            if (kind == "detail" || kind == "candidates") metadata.WithParameter(OpenApiParameterMetadata.Query("tenantId", "Explicit tenant for global administrators; defaults to caller tenant", false));
            if (kind == "history")
                foreach (string name in new[] { "trigger", "outcome", "from", "to" }) metadata.WithParameter(OpenApiParameterMetadata.Query(name, "History filter; UTC range [from,to)", false));
            metadata.WithResponse(200, kind switch {
                "summary" => OpenApiJson.For<CaptainSelectionSummary>("Current attempt and latest retained selection"),
                "history" => OpenApiJson.For<EnumerationResult<CaptainSelectionDecisionView>>("Scoped history page"),
                "preview" => OpenApiJson.For<CaptainSelectionPreview>("Current hypothetical comparison; no writes or launch"),
                "candidates" => OpenApiJson.For<EnumerationResult<CaptainSelectionCandidateView>>("Scoped candidate page"),
                _ => OpenApiJson.For<CaptainSelectionDecisionDetail>("Immutable policy, candidates and runtime evidence") });
            foreach (int status in new[] { 400, 401, 403, 404, 409 }) metadata.WithResponse(status, OpenApiJson.For<ApiErrorResponse>("Selection error"));
            metadata.WithResponse(503, OpenApiJson.For<ApiStatusErrorResponse>("Selection evidence unavailable"));
            return metadata;
        }

        private static int Number(HttpContextBase context, string name, int fallback)
        {
            string? text = context.Request.RetrieveQueryValue(name);
            if (text == null) return fallback;
            return Int32.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value) ? value : throw new ArgumentException("Invalid " + name + ".");
        }

        private static DateTime? Date(HttpContextBase context, string name)
        {
            string? text = context.Request.RetrieveQueryValue(name);
            if (text == null) return null;
            return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime value)
                ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : throw new ArgumentException("Invalid " + name + ".");
        }
    }
}

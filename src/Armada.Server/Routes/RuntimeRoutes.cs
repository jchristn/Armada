namespace Armada.Server.Routes
{
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using Armada.Core;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using SyslogLogging;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// REST API routes for runtime-specific helper operations.
    /// </summary>
    public class RuntimeRoutes
    {
        #region Private-Members

        private readonly LoggingModule _Logging;
        private readonly MuxHostResolver _MuxHosts;
        private static readonly JsonSerializerOptions _BodyJsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };
        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="logging">Logging.</param>
        /// <param name="muxHosts">Decides where mux runs (the caller's Harbor, or the Admiral host in standalone mode).</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public RuntimeRoutes(LoggingModule logging, MuxHostResolver muxHosts)
        {
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _MuxHosts = muxHosts ?? throw new ArgumentNullException(nameof(muxHosts));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register routes with the application.
        /// </summary>
        public void Register(
            Webserver app,
            Func<WatsonWebserver.Core.HttpContextBase, Task<AuthContext>> authenticate,
            IAuthorizationService authz)
        {
            app.Get("/api/v1/runtimes/mux/endpoints", async (ApiRequest req) =>
            {
                EnumerationQuery query = new EnumerationQuery();
                query.ApplyQuerystringOverrides(key => req.Query.GetValueOrDefault(key));
                return await EnumerateMuxEndpointsAsync(req, authenticate, authz, query).ConfigureAwait(false);
            },
            api => api
                .WithTag("Runtimes")
                .WithSummary("Enumerate saved Mux endpoints")
                .WithDescription("One page of the endpoints configured in the selected Mux config directory, ordered by name, with secret values redacted. mux reads its whole configuration file, so the Admiral pages its answer: Objects is one page and TotalRecords counts every saved endpoint. Mux runs on the caller's connected Harbor (or another Harbor the tenant may use); only a standalone Admiral with no eligible Harbor runs it locally.")
                .WithParameter(OpenApiParameterMetadata.Query("pageNumber", "One-based page number (default 1)", false, OpenApiSchemaMetadata.Integer()))
                .WithParameter(OpenApiParameterMetadata.Query("pageSize", "Page size, 1 to 1000 (default 10)", false, OpenApiSchemaMetadata.Integer()))
                .WithParameter(OpenApiParameterMetadata.Query("configDirectory", "Optional Mux config directory override (a path on the machine that runs mux)", false))
                .WithParameter(OpenApiParameterMetadata.Query("harborId", "Optional Harbor whose mux to ask; by default the caller's own connected Harbor", false))
                .WithResponse(200, OpenApiJson.For<MuxEndpointPage>("One page of Mux endpoints"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/runtimes/mux/endpoints/enumerate", async (ApiRequest req) =>
            {
                EnumerationQuery query;
                try
                {
                    query = EnumerationRequestReader.Read(req, _BodyJsonOptions);
                }
                catch (JsonException ex)
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "Invalid enumeration query: " + ex.Message };
                }

                return await EnumerateMuxEndpointsAsync(req, authenticate, authz, query).ConfigureAwait(false);
            },
            api => api
                .WithTag("Runtimes")
                .WithSummary("Enumerate saved Mux endpoints (body query)")
                .WithDescription("The POST twin of GET /api/v1/runtimes/mux/endpoints: paging comes from the JSON body (EnumerationQuery: PageNumber, PageSize 1 to 1000, default 10) with querystring values applied on top; configDirectory and harborId are querystring parameters.")
                .WithParameter(OpenApiParameterMetadata.Query("configDirectory", "Optional Mux config directory override (a path on the machine that runs mux)", false))
                .WithParameter(OpenApiParameterMetadata.Query("harborId", "Optional Harbor whose mux to ask; by default the caller's own connected Harbor", false))
                .WithRequestBody(OpenApiJson.BodyFor<EnumerationQuery>("Paging", false))
                .WithResponse(200, OpenApiJson.For<MuxEndpointPage>("One page of Mux endpoints"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/runtimes/mux/endpoints/{name}", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse
                    {
                        Error = ctx.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized,
                        Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required"
                    };
                }

                try
                {
                    string endpointName = RouteParameters.Get(req, "name");
                    string? configDirectory = RouteParameters.Decode(req.Query.GetValueOrDefault("configDirectory"));
                    MuxHost host = await _MuxHosts.ResolveAsync(ctx.TenantId, ctx.UserId, req.Query.GetValueOrDefault("harborId"), null).ConfigureAwait(false);
                    if (host.Executor == null)
                    {
                        req.Http.Response.StatusCode = 503;
                        return (object)new MuxEndpointShowResult { ErrorCode = "harbor_unavailable", ErrorMessage = host.Error ?? "No host can run mux." };
                    }

                    MuxEndpointShowResult result = await new MuxCliService(_Logging, host.Executor).ShowEndpointAsync(endpointName, configDirectory).ConfigureAwait(false);
                    result.HarborId = host.HarborId;
                    if (!result.Success)
                    {
                        req.Http.Response.StatusCode = MapMuxErrorStatusCode(result.ErrorCode);
                    }

                    return (object)result;
                }
                catch (CollectionLimitExceededException ex)
                {
                    return RouteErrorMapper.LimitExceeded(req, ex);
                }
                catch (Exception ex)
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = ex.Message };
                }
            },
            api => api
                .WithTag("Runtimes")
                .WithSummary("Inspect a saved Mux endpoint")
                .WithDescription("Returns one configured Mux endpoint, including tool capability and redacted header names.")
                .WithParameter(OpenApiParameterMetadata.Path("name", "Mux endpoint name"))
                .WithParameter(OpenApiParameterMetadata.Query("configDirectory", "Optional Mux config directory override (a path on the machine that runs mux)", false))
                .WithParameter(OpenApiParameterMetadata.Query("harborId", "Optional Harbor whose mux to ask; by default the caller's own connected Harbor", false))
                .WithResponse(200, OpenApiJson.For<MuxEndpointShowResult>("Mux endpoint details"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithSecurity("ApiKey"));
        }

        #endregion

        #region Private-Methods

        private async Task<object?> EnumerateMuxEndpointsAsync(
            ApiRequest req,
            Func<WatsonWebserver.Core.HttpContextBase, Task<AuthContext>> authenticate,
            IAuthorizationService authz,
            EnumerationQuery query)
        {
            AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
            if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
            {
                req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                return new ApiErrorResponse
                {
                    Error = ctx.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized,
                    Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required"
                };
            }

            try
            {
                string? configDirectory = RouteParameters.Decode(req.Query.GetValueOrDefault("configDirectory"));
                MuxHost host = await _MuxHosts.ResolveAsync(ctx.TenantId, ctx.UserId, req.Query.GetValueOrDefault("harborId"), null).ConfigureAwait(false);
                if (host.Executor == null)
                {
                    req.Http.Response.StatusCode = 503;
                    return new MuxEndpointPage
                    {
                        Success = false,
                        PageNumber = query.PageNumber,
                        PageSize = query.PageSize,
                        ErrorCode = "harbor_unavailable",
                        ErrorMessage = host.Error ?? "No host can run mux."
                    };
                }

                MuxEndpointPage page = await new MuxCliService(_Logging, host.Executor).EnumerateEndpointsAsync(configDirectory, query).ConfigureAwait(false);
                page.HarborId = host.HarborId;
                if (!page.Success) req.Http.Response.StatusCode = MapMuxErrorStatusCode(page.ErrorCode);
                return page;
            }
            catch (CollectionLimitExceededException ex)
            {
                return RouteErrorMapper.LimitExceeded(req, ex);
            }
            catch (Exception ex)
            {
                req.Http.Response.StatusCode = 400;
                return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = ex.Message };
            }
        }

        private static int MapMuxErrorStatusCode(string? errorCode)
        {
            return errorCode?.Trim().ToLowerInvariant() switch
            {
                "endpoint_not_found" => 404,
                "harbor_unavailable" => 503,
                _ => 400
            };
        }

        #endregion
    }
}

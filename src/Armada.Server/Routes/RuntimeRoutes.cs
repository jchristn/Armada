namespace Armada.Server.Routes
{
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

        private readonly MuxCliService _MuxCli;
        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public RuntimeRoutes(LoggingModule logging)
        {
            _MuxCli = new MuxCliService(logging ?? throw new ArgumentNullException(nameof(logging)));
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
                    string? configDirectory = req.Query.GetValueOrDefault("configDirectory");
                    MuxEndpointListResult result = await _MuxCli.ListEndpointsAsync(configDirectory).ConfigureAwait(false);
                    if (!result.Success)
                    {
                        req.Http.Response.StatusCode = MapMuxErrorStatusCode(result.ErrorCode);
                    }

                    return (object)result;
                }
                catch (Exception ex)
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = ex.Message };
                }
            },
            api => api
                .WithTag("Runtimes")
                .WithSummary("List saved Mux endpoints")
                .WithDescription("Returns the endpoints configured in the selected Mux config directory, with secret values redacted.")
                .WithParameter(OpenApiParameterMetadata.Query("configDirectory", "Optional Mux config directory override", false))
                .WithResponse(200, OpenApiJson.For<MuxEndpointListResult>("Mux endpoint list"))
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
                    string endpointName = req.Parameters["name"];
                    string? configDirectory = req.Query.GetValueOrDefault("configDirectory");
                    MuxEndpointShowResult result = await _MuxCli.ShowEndpointAsync(endpointName, configDirectory).ConfigureAwait(false);
                    if (!result.Success)
                    {
                        req.Http.Response.StatusCode = MapMuxErrorStatusCode(result.ErrorCode);
                    }

                    return (object)result;
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
                .WithParameter(OpenApiParameterMetadata.Query("configDirectory", "Optional Mux config directory override", false))
                .WithResponse(200, OpenApiJson.For<MuxEndpointShowResult>("Mux endpoint details"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));
        }

        #endregion

        #region Private-Methods

        private static int MapMuxErrorStatusCode(string? errorCode)
        {
            return errorCode?.Trim().ToLowerInvariant() switch
            {
                "endpoint_not_found" => 404,
                _ => 400
            };
        }

        #endregion
    }
}

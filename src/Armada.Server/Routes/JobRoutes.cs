namespace Armada.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;
    using Armada.Server;
    using Armada.Core.Database;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// REST API routes for background jobs: list, read, and cancel for status polling.
    /// </summary>
    public class JobRoutes
    {
        #region Private-Members

        private readonly DatabaseDriver _database;
        private readonly JobService _jobs;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="jobs">Job service.</param>
        public JobRoutes(DatabaseDriver database, JobService jobs)
        {
            _database = database ?? throw new ArgumentNullException(nameof(database));
            _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register routes with the application.
        /// </summary>
        /// <param name="app">Webserver.</param>
        /// <param name="authenticate">Authentication middleware.</param>
        /// <param name="authz">Authorization service.</param>
        public void Register(
            Webserver app,
            Func<HttpContextBase, Task<AuthContext>> authenticate,
            IAuthorizationService authz)
        {
            app.Get("/api/v1/jobs", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }

                // With pageNumber, pageSize, status, or kind: one filtered page (the dashboard header polls
                // status=Queued,Running). Without any of them: every job in scope, as before.
                if (JobQuery.TryFromQuerystring(key => req.Query.GetValueOrDefault(key), out JobQuery? query, out string? queryError))
                {
                    if (!ctx.IsAdmin) query!.TenantId = ctx.TenantId;
                    if (!ctx.IsAdmin && !ctx.IsTenantAdmin) query!.UserId = ctx.UserId;
                    return (object)await _database.Jobs.EnumeratePageAsync(query!).ConfigureAwait(false);
                }
                if (queryError != null)
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = queryError };
                }

                List<Job> jobs = ctx.IsAdmin
                    ? await _database.Jobs.EnumerateAsync().ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Jobs.EnumerateAsync(ctx.TenantId!).ConfigureAwait(false)
                        : await _database.Jobs.EnumerateAsync(ctx.TenantId!, ctx.UserId!).ConfigureAwait(false);
                return (object)new { Success = true, Objects = jobs, TotalRecords = jobs.Count };
            },
            api => api
                .WithTag("Jobs")
                .WithSummary("List background jobs")
                .WithDescription("Returns background jobs newest first, scoped to the caller. With any of pageNumber, pageSize "
                    + "(1-1000, default 100), status (comma-separated: Queued, Running, Succeeded, Failed, Cancelled), or kind, "
                    + "returns one page as an enumeration result; without them, returns every job in scope.")
                .WithParameter(OpenApiParameterMetadata.Query("pageNumber", "Page number (1-based)", false, OpenApiSchemaMetadata.Integer()))
                .WithParameter(OpenApiParameterMetadata.Query("pageSize", "Page size, 1-1000 (default 100)", false, OpenApiSchemaMetadata.Integer()))
                .WithParameter(OpenApiParameterMetadata.Query("status", "Comma-separated job statuses, e.g. Queued,Running", false))
                .WithParameter(OpenApiParameterMetadata.Query("kind", "Job kind, e.g. Report", false))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/jobs/{id}", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                string id = req.Parameters["id"];
                Job? job = ctx.IsAdmin
                    ? await _database.Jobs.ReadAsync(id).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Jobs.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false)
                        : await _database.Jobs.ReadAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                if (job == null) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Job not found" }; }
                return (object)job;
            },
            api => api
                .WithTag("Jobs")
                .WithSummary("Get a background job")
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/jobs/{id}/cancel", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                string id = req.Parameters["id"];
                Job? job = ctx.IsAdmin
                    ? await _database.Jobs.ReadAsync(id).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Jobs.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false)
                        : await _database.Jobs.ReadAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                if (job == null) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Job not found" }; }

                try
                {
                    Job cancelled = await _jobs.CancelAsync(job).ConfigureAwait(false);
                    return (object)cancelled;
                }
                catch (InvalidOperationException ex)
                {
                    req.Http.Response.StatusCode = 409;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = ex.Message };
                }
            },
            api => api
                .WithTag("Jobs")
                .WithSummary("Cancel a background job")
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));
        }

        #endregion
    }
}

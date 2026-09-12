namespace Armada.Server.Routes
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;
    using Armada.Core.Authorization;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// REST API routes for playbook-backed runbooks and event-backed executions.
    /// </summary>
    public class RunbookRoutes
    {
        private readonly RunbookService _Runbooks;
        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>
        /// Instantiate.
        /// </summary>
        public RunbookRoutes(RunbookService runbooks)
        {
            _Runbooks = runbooks ?? throw new ArgumentNullException(nameof(runbooks));
        }

        /// <summary>
        /// Register routes.
        /// </summary>
        public void Register(
            Webserver app,
            Func<HttpContextBase, Task<AuthContext>> authenticate,
            IAuthorizationService authz)
        {
            app.Get("/api/v1/runbooks", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);
                RunbookQuery query = BuildRunbookQueryFromRequest(req);
                return await _Runbooks.EnumerateAsync(ctx, query).ConfigureAwait(false);
            },
            api => api
                .WithTag("Runbooks")
                .WithSummary("List runbooks")
                .WithDescription("Returns paginated runbooks backed by playbooks.")
                .WithParameter(OpenApiParameterMetadata.Query("pageNumber", "One-based page number", false, OpenApiSchemaMetadata.Integer()))
                .WithParameter(OpenApiParameterMetadata.Query("pageSize", "Page size", false, OpenApiSchemaMetadata.Integer()))
                .WithParameter(OpenApiParameterMetadata.Query("workflowProfileId", "Optional workflow-profile filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("environmentId", "Optional environment filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("defaultCheckType", "Optional bound default check type", false))
                .WithParameter(OpenApiParameterMetadata.Query("active", "Optional active-state filter", false, OpenApiSchemaMetadata.Boolean()))
                .WithParameter(OpenApiParameterMetadata.Query("search", "Optional free-text search", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<Runbook>>("Paginated runbooks"))
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/runbooks/enumerate", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);
                RunbookQuery query = JsonSerializer.Deserialize<RunbookQuery>(req.Http.Request.DataAsString, _JsonOptions) ?? new RunbookQuery();
                ApplyRunbookQuerystringOverrides(req, query);
                return await _Runbooks.EnumerateAsync(ctx, query).ConfigureAwait(false);
            },
            api => api
                .WithTag("Runbooks")
                .WithSummary("Enumerate runbooks")
                .WithDescription("Paginated runbook enumeration using a JSON body and optional querystring overrides.")
                .WithRequestBody(OpenApiJson.BodyFor<RunbookQuery>("Runbook query", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<Runbook>>("Paginated runbooks"))
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/runbooks/{id}", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);
                Runbook? runbook = await _Runbooks.ReadAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                if (runbook == null)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Runbook not found" };
                }

                return runbook;
            },
            api => api
                .WithTag("Runbooks")
                .WithSummary("Get a runbook")
                .WithDescription("Returns one runbook backed by a playbook.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Runbook ID (same as playbook ID)"))
                .WithResponse(200, OpenApiJson.For<Runbook>("Runbook"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/runbooks", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);
                RunbookUpsertRequest request = JsonSerializer.Deserialize<RunbookUpsertRequest>(req.Http.Request.DataAsString, _JsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as RunbookUpsertRequest.");

                try
                {
                    Runbook runbook = await _Runbooks.CreateAsync(ctx, request).ConfigureAwait(false);
                    req.Http.Response.StatusCode = 201;
                    return runbook;
                }
                catch (UnauthorizedAccessException ex)
                {
                    req.Http.Response.StatusCode = 403;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = ex.Message };
                }
                catch (InvalidOperationException ex)
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = ex.Message };
                }
            },
            api => api
                .WithTag("Runbooks")
                .WithSummary("Create a runbook")
                .WithDescription("Creates a playbook-backed runbook with explicit or derived steps and parameters.")
                .WithRequestBody(OpenApiJson.BodyFor<RunbookUpsertRequest>("Runbook create request", true))
                .WithResponse(201, OpenApiJson.For<Runbook>("Created runbook"))
                .WithSecurity("ApiKey"));

            app.Put("/api/v1/runbooks/{id}", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);
                RunbookUpsertRequest request = JsonSerializer.Deserialize<RunbookUpsertRequest>(req.Http.Request.DataAsString, _JsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as RunbookUpsertRequest.");

                try
                {
                    return await _Runbooks.UpdateAsync(ctx, req.Parameters["id"], request).ConfigureAwait(false);
                }
                catch (UnauthorizedAccessException ex)
                {
                    req.Http.Response.StatusCode = 403;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = ex.Message };
                }
                catch (InvalidOperationException ex)
                {
                    req.Http.Response.StatusCode = ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase) ? 404 : 400;
                    return new ApiErrorResponse
                    {
                        Error = req.Http.Response.StatusCode == 404 ? ApiResultEnum.NotFound : ApiResultEnum.BadRequest,
                        Message = ex.Message
                    };
                }
            },
            api => api
                .WithTag("Runbooks")
                .WithSummary("Update a runbook")
                .WithDescription("Updates runbook metadata, markdown overview, steps, and parameters.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Runbook ID"))
                .WithRequestBody(OpenApiJson.BodyFor<RunbookUpsertRequest>("Runbook update request", true))
                .WithResponse(200, OpenApiJson.For<Runbook>("Updated runbook"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Delete("/api/v1/runbooks/{id}", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);
                try
                {
                    await _Runbooks.DeleteAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                    req.Http.Response.StatusCode = 204;
                    return null;
                }
                catch (UnauthorizedAccessException ex)
                {
                    req.Http.Response.StatusCode = 403;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = ex.Message };
                }
                catch (InvalidOperationException ex)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = ex.Message };
                }
            },
            api => api
                .WithTag("Runbooks")
                .WithSummary("Delete a runbook")
                .WithDescription("Deletes one runbook and leaves unrelated playbooks intact.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Runbook ID"))
                .WithResponse(204, OpenApiResponseMetadata.NoContent())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/runbook-executions", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);
                RunbookExecutionQuery query = BuildExecutionQueryFromRequest(req);
                return await _Runbooks.EnumerateExecutionsAsync(ctx, query).ConfigureAwait(false);
            },
            api => api
                .WithTag("Runbooks")
                .WithSummary("List runbook executions")
                .WithDescription("Returns paginated event-backed runbook execution records.")
                .WithParameter(OpenApiParameterMetadata.Query("pageNumber", "One-based page number", false, OpenApiSchemaMetadata.Integer()))
                .WithParameter(OpenApiParameterMetadata.Query("pageSize", "Page size", false, OpenApiSchemaMetadata.Integer()))
                .WithParameter(OpenApiParameterMetadata.Query("runbookId", "Optional runbook filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("deploymentId", "Optional deployment filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("incidentId", "Optional incident filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("status", "Optional status filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("search", "Optional free-text search", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<RunbookExecution>>("Paginated runbook executions"))
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/runbook-executions/enumerate", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);
                RunbookExecutionQuery query = JsonSerializer.Deserialize<RunbookExecutionQuery>(req.Http.Request.DataAsString, _JsonOptions) ?? new RunbookExecutionQuery();
                ApplyExecutionQuerystringOverrides(req, query);
                return await _Runbooks.EnumerateExecutionsAsync(ctx, query).ConfigureAwait(false);
            },
            api => api
                .WithTag("Runbooks")
                .WithSummary("Enumerate runbook executions")
                .WithDescription("Paginated runbook-execution enumeration using a JSON body and optional querystring overrides.")
                .WithRequestBody(OpenApiJson.BodyFor<RunbookExecutionQuery>("Runbook execution query", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<RunbookExecution>>("Paginated runbook executions"))
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/runbook-executions/{id}", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);
                RunbookExecution? execution = await _Runbooks.ReadExecutionAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                if (execution == null)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Runbook execution not found" };
                }

                return execution;
            },
            api => api
                .WithTag("Runbooks")
                .WithSummary("Get a runbook execution")
                .WithDescription("Returns one runbook execution by ID.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Runbook execution ID (rbx_ prefix)"))
                .WithResponse(200, OpenApiJson.For<RunbookExecution>("Runbook execution"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/runbooks/{id}/executions", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);
                RunbookExecutionStartRequest request = JsonSerializer.Deserialize<RunbookExecutionStartRequest>(req.Http.Request.DataAsString, _JsonOptions)
                    ?? new RunbookExecutionStartRequest();

                try
                {
                    RunbookExecution execution = await _Runbooks.StartExecutionAsync(ctx, req.Parameters["id"], request).ConfigureAwait(false);
                    req.Http.Response.StatusCode = 201;
                    return execution;
                }
                catch (InvalidOperationException ex)
                {
                    req.Http.Response.StatusCode = ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase) ? 404 : 400;
                    return new ApiErrorResponse
                    {
                        Error = req.Http.Response.StatusCode == 404 ? ApiResultEnum.NotFound : ApiResultEnum.BadRequest,
                        Message = ex.Message
                    };
                }
            },
            api => api
                .WithTag("Runbooks")
                .WithSummary("Start a runbook execution")
                .WithDescription("Starts a runbook execution with optional parameter overrides and related incident or deployment links.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Runbook ID"))
                .WithRequestBody(OpenApiJson.BodyFor<RunbookExecutionStartRequest>("Runbook execution start request", false))
                .WithResponse(201, OpenApiJson.For<RunbookExecution>("Started runbook execution"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Put("/api/v1/runbook-executions/{id}", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);
                RunbookExecutionUpdateRequest request = JsonSerializer.Deserialize<RunbookExecutionUpdateRequest>(req.Http.Request.DataAsString, _JsonOptions)
                    ?? new RunbookExecutionUpdateRequest();

                try
                {
                    return await _Runbooks.UpdateExecutionAsync(ctx, req.Parameters["id"], request).ConfigureAwait(false);
                }
                catch (InvalidOperationException ex)
                {
                    req.Http.Response.StatusCode = ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase) ? 404 : 400;
                    return new ApiErrorResponse
                    {
                        Error = req.Http.Response.StatusCode == 404 ? ApiResultEnum.NotFound : ApiResultEnum.BadRequest,
                        Message = ex.Message
                    };
                }
            },
            api => api
                .WithTag("Runbooks")
                .WithSummary("Update a runbook execution")
                .WithDescription("Updates step completion, notes, and status for a running or completed runbook execution.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Runbook execution ID (rbx_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<RunbookExecutionUpdateRequest>("Runbook execution update request", true))
                .WithResponse(200, OpenApiJson.For<RunbookExecution>("Updated runbook execution"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Delete("/api/v1/runbook-executions/{id}", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);
                try
                {
                    await _Runbooks.DeleteExecutionAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                    req.Http.Response.StatusCode = 204;
                    return null;
                }
                catch (InvalidOperationException ex)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = ex.Message };
                }
            },
            api => api
                .WithTag("Runbooks")
                .WithSummary("Delete a runbook execution")
                .WithDescription("Deletes one runbook execution snapshot chain.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Runbook execution ID (rbx_ prefix)"))
                .WithResponse(204, OpenApiResponseMetadata.NoContent())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));
        }

        private static RunbookQuery BuildRunbookQueryFromRequest(ApiRequest req)
        {
            RunbookQuery query = new RunbookQuery();
            ApplyRunbookQuerystringOverrides(req, query);
            return query;
        }

        private static RunbookExecutionQuery BuildExecutionQueryFromRequest(ApiRequest req)
        {
            RunbookExecutionQuery query = new RunbookExecutionQuery();
            ApplyExecutionQuerystringOverrides(req, query);
            return query;
        }

        private static void ApplyRunbookQuerystringOverrides(ApiRequest req, RunbookQuery query)
        {
            if (Int32.TryParse(req.Query.GetValueOrDefault("pageNumber"), out int pageNumber))
                query.PageNumber = Math.Max(1, pageNumber);
            if (Int32.TryParse(req.Query.GetValueOrDefault("pageSize"), out int pageSize))
                query.PageSize = Math.Clamp(pageSize, 1, 500);
            if (Boolean.TryParse(req.Query.GetValueOrDefault("active"), out bool active))
                query.Active = active;
            if (Enum.TryParse(req.Query.GetValueOrDefault("defaultCheckType"), true, out CheckRunTypeEnum defaultCheckType))
                query.DefaultCheckType = defaultCheckType;

            query.WorkflowProfileId = NormalizeEmpty(req.Query.GetValueOrDefault("workflowProfileId")) ?? query.WorkflowProfileId;
            query.EnvironmentId = NormalizeEmpty(req.Query.GetValueOrDefault("environmentId")) ?? query.EnvironmentId;
            query.Search = NormalizeEmpty(req.Query.GetValueOrDefault("search")) ?? query.Search;
        }

        private static void ApplyExecutionQuerystringOverrides(ApiRequest req, RunbookExecutionQuery query)
        {
            if (Int32.TryParse(req.Query.GetValueOrDefault("pageNumber"), out int pageNumber))
                query.PageNumber = Math.Max(1, pageNumber);
            if (Int32.TryParse(req.Query.GetValueOrDefault("pageSize"), out int pageSize))
                query.PageSize = Math.Clamp(pageSize, 1, 500);
            if (Enum.TryParse(req.Query.GetValueOrDefault("status"), true, out RunbookExecutionStatusEnum status))
                query.Status = status;

            query.RunbookId = NormalizeEmpty(req.Query.GetValueOrDefault("runbookId")) ?? query.RunbookId;
            query.DeploymentId = NormalizeEmpty(req.Query.GetValueOrDefault("deploymentId")) ?? query.DeploymentId;
            query.IncidentId = NormalizeEmpty(req.Query.GetValueOrDefault("incidentId")) ?? query.IncidentId;
            query.Search = NormalizeEmpty(req.Query.GetValueOrDefault("search")) ?? query.Search;
        }

        private static ApiErrorResponse BuildAuthError(ApiRequest req)
        {
            return new ApiErrorResponse
            {
                Error = ApiResultEnum.BadRequest,
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

        private static string? NormalizeEmpty(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}

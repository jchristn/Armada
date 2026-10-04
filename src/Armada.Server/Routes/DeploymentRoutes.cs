namespace Armada.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;
    using Armada.Core.Authorization;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// REST API routes for first-class deployments.
    /// </summary>
    public class DeploymentRoutes
    {
        private readonly DeploymentService _Deployments;
        private readonly ObjectiveService _Objectives;
        private readonly JsonSerializerOptions _JsonOptions;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public DeploymentRoutes(DeploymentService deployments, ObjectiveService objectives, JsonSerializerOptions jsonOptions)
        {
            _Deployments = deployments ?? throw new ArgumentNullException(nameof(deployments));
            _Objectives = objectives ?? throw new ArgumentNullException(nameof(objectives));
            _JsonOptions = jsonOptions ?? throw new ArgumentNullException(nameof(jsonOptions));
        }

        /// <summary>
        /// Register routes.
        /// </summary>
        public void Register(
            Webserver app,
            Func<HttpContextBase, Task<AuthContext>> authenticate,
            IAuthorizationService authz)
        {
            app.Get("/api/v1/deployments", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                DeploymentQuery query = BuildQueryFromRequest(req);
                return await _Deployments.EnumerateAsync(ctx, query).ConfigureAwait(false);
            },
            api => api
                .WithTag("Deployments")
                .WithSummary("List deployments")
                .WithDescription("Returns paginated first-class deployment records.")
                .WithParameter(OpenApiParameterMetadata.Query("pageNumber", "One-based page number", false, OpenApiSchemaMetadata.Integer()))
                .WithParameter(OpenApiParameterMetadata.Query("pageSize", "Page size", false, OpenApiSchemaMetadata.Integer()))
                .WithParameter(OpenApiParameterMetadata.Query("vesselId", "Optional vessel filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("environmentId", "Optional environment filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("environmentName", "Optional environment-name filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("releaseId", "Optional linked release filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("missionId", "Optional linked mission filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("voyageId", "Optional linked voyage filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("checkRunId", "Optional linked check-run filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("status", "Optional deployment status filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("verificationStatus", "Optional verification-status filter", false))
                .WithParameter(OpenApiParameterMetadata.Query("search", "Optional free-text search", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<Deployment>>("Paginated deployment records"))
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/deployments/enumerate", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                DeploymentQuery query = JsonSerializer.Deserialize<DeploymentQuery>(req.Http.Request.DataAsString, _JsonOptions)
                    ?? new DeploymentQuery();
                ApplyQuerystringOverrides(req, query);
                return await _Deployments.EnumerateAsync(ctx, query).ConfigureAwait(false);
            },
            api => api
                .WithTag("Deployments")
                .WithSummary("Enumerate deployments")
                .WithDescription("Paginated deployment enumeration using a JSON body and optional querystring overrides.")
                .WithRequestBody(OpenApiJson.BodyFor<DeploymentQuery>("Deployment query", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<Deployment>>("Paginated deployment records"))
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/deployments", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                DeploymentUpsertRequest request = JsonSerializer.Deserialize<DeploymentUpsertRequest>(req.Http.Request.DataAsString, _JsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as DeploymentUpsertRequest.");

                try
                {
                    await ValidateObjectivesAsync(ctx, request.ObjectiveIds).ConfigureAwait(false);
                    Deployment deployment = await _Deployments.CreateAsync(ctx, request).ConfigureAwait(false);
                    await LinkObjectivesAsync(ctx, deployment, request.ObjectiveIds).ConfigureAwait(false);
                    req.Http.Response.StatusCode = 201;
                    return deployment;
                }
                catch (InvalidOperationException ex)
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = ex.Message };
                }
            },
            api => api
                .WithTag("Deployments")
                .WithSummary("Create a deployment")
                .WithDescription("Creates a deployment record and executes it immediately when approval is not required.")
                .WithRequestBody(OpenApiJson.BodyFor<DeploymentUpsertRequest>("Deployment create request", true))
                .WithResponse(201, OpenApiJson.For<Deployment>("Created deployment"))
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/deployments/{id}", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                Deployment? deployment = await _Deployments.ReadAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
                if (deployment == null)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Deployment not found" };
                }

                return deployment;
            },
            api => api
                .WithTag("Deployments")
                .WithSummary("Get a deployment")
                .WithDescription("Returns one deployment by ID.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Deployment ID (dpl_ prefix)"))
                .WithResponse(200, OpenApiJson.For<Deployment>("Deployment"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Put("/api/v1/deployments/{id}", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                DeploymentUpsertRequest request = JsonSerializer.Deserialize<DeploymentUpsertRequest>(req.Http.Request.DataAsString, _JsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as DeploymentUpsertRequest.");

                try
                {
                    await ValidateObjectivesAsync(ctx, request.ObjectiveIds).ConfigureAwait(false);
                    Deployment deployment = await _Deployments.UpdateAsync(ctx, req.Parameters["id"], request).ConfigureAwait(false);
                    await LinkObjectivesAsync(ctx, deployment, request.ObjectiveIds).ConfigureAwait(false);
                    return deployment;
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
                .WithTag("Deployments")
                .WithSummary("Update a deployment")
                .WithDescription("Updates deployment metadata such as title, notes, and source reference.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Deployment ID (dpl_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<DeploymentUpsertRequest>("Deployment update request", true))
                .WithResponse(200, OpenApiJson.For<Deployment>("Updated deployment"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/deployments/{id}/approve", async (ApiRequest req) =>
            {
                return await HandleCommentedActionAsync(req, authenticate, authz, (ctx, deploymentId, comment) => _Deployments.ApproveAsync(ctx, deploymentId, comment)).ConfigureAwait(false);
            },
            api => api
                .WithTag("Deployments")
                .WithSummary("Approve a deployment")
                .WithDescription("Approves a pending deployment and begins execution.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Deployment ID (dpl_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<DeploymentApprovalBody>("Optional approval comment", false))
                .WithResponse(200, OpenApiJson.For<Deployment>("Approved deployment"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/deployments/{id}/deny", async (ApiRequest req) =>
            {
                return await HandleCommentedActionAsync(req, authenticate, authz, (ctx, deploymentId, comment) => _Deployments.DenyAsync(ctx, deploymentId, comment)).ConfigureAwait(false);
            },
            api => api
                .WithTag("Deployments")
                .WithSummary("Deny a deployment")
                .WithDescription("Denies a pending deployment without executing it.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Deployment ID (dpl_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<DeploymentApprovalBody>("Optional denial comment", false))
                .WithResponse(200, OpenApiJson.For<Deployment>("Denied deployment"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/deployments/{id}/verify", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                try
                {
                    return await _Deployments.VerifyAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
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
                .WithTag("Deployments")
                .WithSummary("Run post-deploy verification")
                .WithDescription("Re-runs smoke, health, and deployment-verification steps for an existing deployment.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Deployment ID (dpl_ prefix)"))
                .WithResponse(200, OpenApiJson.For<Deployment>("Verified deployment"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/deployments/{id}/rollback", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                try
                {
                    return await _Deployments.RollbackAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
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
                .WithTag("Deployments")
                .WithSummary("Rollback a deployment")
                .WithDescription("Runs the configured rollback flow for an existing deployment.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Deployment ID (dpl_ prefix)"))
                .WithResponse(200, OpenApiJson.For<Deployment>("Rolled-back deployment"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Delete("/api/v1/deployments/{id}", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                try
                {
                    await _Deployments.DeleteAsync(ctx, req.Parameters["id"]).ConfigureAwait(false);
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
                .WithTag("Deployments")
                .WithSummary("Delete a deployment")
                .WithDescription("Deletes one deployment record within the caller scope.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Deployment ID (dpl_ prefix)"))
                .WithResponse(204, OpenApiResponseMetadata.NoContent())
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));
        }

        private async Task<object?> HandleCommentedActionAsync(
            ApiRequest req,
            Func<HttpContextBase, Task<AuthContext>> authenticate,
            IAuthorizationService authz,
            Func<AuthContext, string, string?, Task<Deployment>> handler)
        {
            AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
            if (ctx == null) return BuildAuthError(req);

            DeploymentApprovalBody? body = null;
            if (!String.IsNullOrWhiteSpace(req.Http.Request.DataAsString))
            {
                body = JsonSerializer.Deserialize<DeploymentApprovalBody>(req.Http.Request.DataAsString, _JsonOptions);
            }

            try
            {
                return await handler(ctx, req.Parameters["id"], body?.Comment).ConfigureAwait(false);
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
        }

        private static DeploymentQuery BuildQueryFromRequest(ApiRequest req)
        {
            DeploymentQuery query = new DeploymentQuery();
            ApplyQuerystringOverrides(req, query);
            return query;
        }

        private static void ApplyQuerystringOverrides(ApiRequest req, DeploymentQuery query)
        {
            if (Int32.TryParse(req.Query.GetValueOrDefault("pageNumber"), out int pageNumber))
                query.PageNumber = Math.Max(1, pageNumber);
            if (Int32.TryParse(req.Query.GetValueOrDefault("pageSize"), out int pageSize))
                query.PageSize = Math.Clamp(pageSize, 1, 500);
            if (DateTime.TryParse(req.Query.GetValueOrDefault("fromUtc"), out DateTime fromUtc))
                query.FromUtc = fromUtc.ToUniversalTime();
            if (DateTime.TryParse(req.Query.GetValueOrDefault("toUtc"), out DateTime toUtc))
                query.ToUtc = toUtc.ToUniversalTime();
            if (Enum.TryParse(req.Query.GetValueOrDefault("status"), true, out DeploymentStatusEnum status))
                query.Status = status;
            if (Enum.TryParse(req.Query.GetValueOrDefault("verificationStatus"), true, out DeploymentVerificationStatusEnum verificationStatus))
                query.VerificationStatus = verificationStatus;

            query.VesselId = NormalizeEmpty(req.Query.GetValueOrDefault("vesselId")) ?? query.VesselId;
            query.WorkflowProfileId = NormalizeEmpty(req.Query.GetValueOrDefault("workflowProfileId")) ?? query.WorkflowProfileId;
            query.EnvironmentId = NormalizeEmpty(req.Query.GetValueOrDefault("environmentId")) ?? query.EnvironmentId;
            query.EnvironmentName = NormalizeEmpty(req.Query.GetValueOrDefault("environmentName")) ?? query.EnvironmentName;
            query.ReleaseId = NormalizeEmpty(req.Query.GetValueOrDefault("releaseId")) ?? query.ReleaseId;
            query.MissionId = NormalizeEmpty(req.Query.GetValueOrDefault("missionId")) ?? query.MissionId;
            query.VoyageId = NormalizeEmpty(req.Query.GetValueOrDefault("voyageId")) ?? query.VoyageId;
            query.CheckRunId = NormalizeEmpty(req.Query.GetValueOrDefault("checkRunId")) ?? query.CheckRunId;
            query.Search = NormalizeEmpty(req.Query.GetValueOrDefault("search")) ?? query.Search;
        }

        private async Task ValidateObjectivesAsync(AuthContext auth, IEnumerable<string>? objectiveIds)
        {
            if (objectiveIds == null) return;

            foreach (string objectiveId in objectiveIds)
            {
                string? normalized = NormalizeEmpty(objectiveId);
                if (String.IsNullOrWhiteSpace(normalized))
                    continue;

                Objective? objective = await _Objectives.ReadAsync(auth, normalized).ConfigureAwait(false);
                if (objective == null)
                    throw new InvalidOperationException("Objective not found: " + normalized);
            }
        }

        private async Task LinkObjectivesAsync(AuthContext auth, Deployment deployment, IEnumerable<string>? explicitObjectiveIds)
        {
            HashSet<string> objectiveIds = await ResolveObjectiveIdsAsync(
                auth,
                explicitObjectiveIds,
                deployment.ReleaseId,
                deployment.MissionId,
                deployment.VoyageId).ConfigureAwait(false);

            foreach (string objectiveId in objectiveIds)
            {
                await _Objectives.LinkDeploymentAsync(auth, objectiveId, deployment.Id).ConfigureAwait(false);
            }
        }

        private async Task<HashSet<string>> ResolveObjectiveIdsAsync(
            AuthContext auth,
            IEnumerable<string>? explicitObjectiveIds,
            string? releaseId,
            string? missionId,
            string? voyageId)
        {
            HashSet<string> objectiveIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (explicitObjectiveIds != null)
            {
                foreach (string objectiveId in explicitObjectiveIds)
                {
                    string? normalized = NormalizeEmpty(objectiveId);
                    if (!String.IsNullOrWhiteSpace(normalized))
                        objectiveIds.Add(normalized);
                }
            }

            await AddObjectivesForQueryAsync(auth, objectiveIds, query => query.ReleaseId = NormalizeEmpty(releaseId)).ConfigureAwait(false);
            await AddObjectivesForQueryAsync(auth, objectiveIds, query => query.MissionId = NormalizeEmpty(missionId)).ConfigureAwait(false);
            await AddObjectivesForQueryAsync(auth, objectiveIds, query => query.VoyageId = NormalizeEmpty(voyageId)).ConfigureAwait(false);
            return objectiveIds;
        }

        private async Task AddObjectivesForQueryAsync(
            AuthContext auth,
            HashSet<string> objectiveIds,
            Action<ObjectiveQuery> configure)
        {
            ObjectiveQuery query = new ObjectiveQuery
            {
                PageNumber = 1,
                PageSize = 200
            };
            configure(query);

            if (String.IsNullOrWhiteSpace(query.ReleaseId)
                && String.IsNullOrWhiteSpace(query.MissionId)
                && String.IsNullOrWhiteSpace(query.VoyageId))
                return;

            while (true)
            {
                EnumerationResult<Objective> results = await _Objectives.EnumerateAsync(auth, query).ConfigureAwait(false);
                foreach (Objective objective in results.Objects)
                    objectiveIds.Add(objective.Id);

                if (results.PageNumber >= results.TotalPages || results.Objects.Count == 0)
                    return;

                query.PageNumber++;
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

        private static string? NormalizeEmpty(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private sealed class DeploymentApprovalBody
        {
            public string? Comment { get; set; } = null;
        }
    }
}

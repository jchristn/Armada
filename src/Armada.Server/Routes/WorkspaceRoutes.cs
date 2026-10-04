namespace Armada.Server.Routes
{
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// REST API routes for the first-class vessel Workspace experience.
    /// </summary>
    public class WorkspaceRoutes
    {
        private static readonly Regex _ScopedFilesDirectiveRegex =
            new Regex(@"^\s*(?:Touch|Edit|Modify)\s+only\s+(?<files>.+)$",
                RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);
        private static readonly Regex _ScopedFileTokenRegex =
            new Regex(
                @"(?<path>(?:[A-Za-z0-9_.-]+[\\/])+[A-Za-z0-9_.-]+|[A-Za-z0-9_.-]+\.(?:cs|csproj|sln|md|json|yaml|yml|ts|tsx|js|jsx|css|html|sh|bat))",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly DatabaseDriver _database;
        private readonly IWorkspaceService _workspace;
        private readonly JsonSerializerOptions _jsonOptions;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public WorkspaceRoutes(
            DatabaseDriver database,
            IWorkspaceService workspace,
            JsonSerializerOptions jsonOptions)
        {
            _database = database ?? throw new ArgumentNullException(nameof(database));
            _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
            _jsonOptions = jsonOptions ?? throw new ArgumentNullException(nameof(jsonOptions));
        }

        /// <summary>
        /// Register routes with the application.
        /// </summary>
        public void Register(
            Webserver app,
            Func<WatsonWebserver.Core.HttpContextBase, Task<AuthContext>> authenticate,
            IAuthorizationService authz)
        {
            app.Get("/api/v1/workspace/vessels/{vesselId}/tree", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                Vessel? vessel = await ReadVesselForContextAsync(ctx, req.Parameters["vesselId"]).ConfigureAwait(false);
                if (vessel == null)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Vessel not found" };
                }

                try
                {
                    string? path = req.Query.GetValueOrDefault("path");
                    return await _workspace.GetTreeAsync(vessel, path).ConfigureAwait(false);
                }
                catch (Exception ex) when (TryMapWorkspaceException(req, ex, out ApiErrorResponse error))
                {
                    return error;
                }
            },
            api => api
                .WithTag("Workspace")
                .WithSummary("List one workspace directory")
                .WithDescription("Returns one directory of the vessel working tree with direct child files and folders.")
                .WithParameter(OpenApiParameterMetadata.Path("vesselId", "Vessel ID (vsl_ prefix)"))
                .WithParameter(OpenApiParameterMetadata.Query("path", "Optional repository-relative directory path", false))
                .WithResponse(200, OpenApiJson.For<WorkspaceTreeResult>("Workspace directory listing"))
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/workspace/vessels/{vesselId}/diff", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                Vessel? vessel = await ReadVesselForContextAsync(ctx, req.Parameters["vesselId"]).ConfigureAwait(false);
                if (vessel == null)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Vessel not found" };
                }

                string? path = req.Query.GetValueOrDefault("path");
                try
                {
                    return await _workspace.GetDiffAsync(vessel, path).ConfigureAwait(false);
                }
                catch (Exception ex) when (TryMapWorkspaceException(req, ex, out ApiErrorResponse error))
                {
                    return error;
                }
            },
            api => api
                .WithTag("Workspace")
                .WithSummary("Get a working-tree diff")
                .WithDescription("Returns a unified git diff of the vessel working tree against HEAD, optionally scoped to one path.")
                .WithParameter(OpenApiParameterMetadata.Path("vesselId", "Vessel ID (vsl_ prefix)"))
                .WithParameter(OpenApiParameterMetadata.Query("path", "Optional repository-relative path to scope the diff", false))
                .WithResponse(200, OpenApiJson.For<WorkspaceDiffResult>("Working-tree diff"))
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/workspace/vessels/{vesselId}/file", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                Vessel? vessel = await ReadVesselForContextAsync(ctx, req.Parameters["vesselId"]).ConfigureAwait(false);
                if (vessel == null)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Vessel not found" };
                }

                string? path = req.Query.GetValueOrDefault("path");
                if (String.IsNullOrWhiteSpace(path))
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "path is required" };
                }

                try
                {
                    return await _workspace.GetFileAsync(vessel, path).ConfigureAwait(false);
                }
                catch (Exception ex) when (TryMapWorkspaceException(req, ex, out ApiErrorResponse error))
                {
                    return error;
                }
            },
            api => api
                .WithTag("Workspace")
                .WithSummary("Read one workspace file")
                .WithDescription("Returns file content and metadata from the vessel working tree.")
                .WithParameter(OpenApiParameterMetadata.Path("vesselId", "Vessel ID (vsl_ prefix)"))
                .WithParameter(OpenApiParameterMetadata.Query("path", "Repository-relative file path", true))
                .WithResponse(200, OpenApiJson.For<WorkspaceFileResponse>("Workspace file"))
                .WithSecurity("ApiKey"));

            app.Put<WorkspaceSaveRequest>("/api/v1/workspace/vessels/{vesselId}/file", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                Vessel? vessel = await ReadVesselForContextAsync(ctx, req.Parameters["vesselId"]).ConfigureAwait(false);
                if (vessel == null)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Vessel not found" };
                }

                WorkspaceSaveRequest request = JsonSerializer.Deserialize<WorkspaceSaveRequest>(req.Http.Request.DataAsString, _jsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as WorkspaceSaveRequest.");

                try
                {
                    return await _workspace.SaveFileAsync(vessel, request).ConfigureAwait(false);
                }
                catch (Exception ex) when (TryMapWorkspaceException(req, ex, out ApiErrorResponse error))
                {
                    return error;
                }
            },
            api => api
                .WithTag("Workspace")
                .WithSummary("Save one workspace file")
                .WithDescription("Writes text content into the vessel working tree with optimistic concurrency validation.")
                .WithParameter(OpenApiParameterMetadata.Path("vesselId", "Vessel ID (vsl_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<WorkspaceSaveRequest>("Workspace save request", true))
                .WithResponse(200, OpenApiJson.For<WorkspaceSaveResult>("Save result"))
                .WithSecurity("ApiKey"));

            app.Post<WorkspaceExecRequest>("/api/v1/workspace/vessels/{vesselId}/exec", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);
                if (!ctx.IsAdmin && !ctx.IsTenantAdmin)
                {
                    req.Http.Response.StatusCode = 403;
                    return new ApiErrorResponse { Error = ApiResultEnum.Forbidden, Message = "Only tenant administrators can run workspace commands" };
                }

                Vessel? vessel = await ReadVesselForContextAsync(ctx, req.Parameters["vesselId"]).ConfigureAwait(false);
                if (vessel == null)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Vessel not found" };
                }

                WorkspaceExecRequest execRequest = JsonSerializer.Deserialize<WorkspaceExecRequest>(req.Http.Request.DataAsString, _jsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as WorkspaceExecRequest.");

                await CommandAudit.RecordAsync(_database, new CommandAuditRecord
                {
                    Source = "WorkspaceExec",
                    Command = execRequest.Command ?? String.Empty,
                    WorkingDirectory = vessel.WorkingDirectory,
                    TenantId = vessel.TenantId ?? ctx.TenantId,
                    UserId = ctx.UserId,
                    VesselId = vessel.Id,
                    EntityType = "Vessel",
                    EntityId = vessel.Id
                }).ConfigureAwait(false);

                try
                {
                    return await _workspace.ExecAsync(vessel, execRequest).ConfigureAwait(false);
                }
                catch (Exception ex) when (TryMapWorkspaceException(req, ex, out ApiErrorResponse error))
                {
                    return error;
                }
            },
            api => api
                .WithTag("Workspace")
                .WithSummary("Run a command in the vessel workspace")
                .WithDescription("Executes a shell command in the vessel working tree (the in-browser dock terminal), bounded by a timeout. Tenant administrators only.")
                .WithParameter(OpenApiParameterMetadata.Path("vesselId", "Vessel ID (vsl_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<WorkspaceExecRequest>("Command to run", true))
                .WithResponse(200, OpenApiJson.For<WorkspaceExecResult>("Command result"))
                .WithSecurity("ApiKey"));

            app.Post<WorkspaceCreateDirectoryRequest>("/api/v1/workspace/vessels/{vesselId}/directory", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                Vessel? vessel = await ReadVesselForContextAsync(ctx, req.Parameters["vesselId"]).ConfigureAwait(false);
                if (vessel == null)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Vessel not found" };
                }

                WorkspaceCreateDirectoryRequest request = JsonSerializer.Deserialize<WorkspaceCreateDirectoryRequest>(req.Http.Request.DataAsString, _jsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as WorkspaceCreateDirectoryRequest.");

                try
                {
                    req.Http.Response.StatusCode = 201;
                    return await _workspace.CreateDirectoryAsync(vessel, request).ConfigureAwait(false);
                }
                catch (Exception ex) when (TryMapWorkspaceException(req, ex, out ApiErrorResponse error))
                {
                    return error;
                }
            },
            api => api
                .WithTag("Workspace")
                .WithSummary("Create one workspace directory")
                .WithDescription("Creates a directory inside the vessel working tree.")
                .WithParameter(OpenApiParameterMetadata.Path("vesselId", "Vessel ID (vsl_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<WorkspaceCreateDirectoryRequest>("Workspace directory request", true))
                .WithResponse(201, OpenApiJson.For<WorkspaceOperationResult>("Directory created"))
                .WithSecurity("ApiKey"));

            app.Post<WorkspaceRenameRequest>("/api/v1/workspace/vessels/{vesselId}/rename", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                Vessel? vessel = await ReadVesselForContextAsync(ctx, req.Parameters["vesselId"]).ConfigureAwait(false);
                if (vessel == null)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Vessel not found" };
                }

                WorkspaceRenameRequest request = JsonSerializer.Deserialize<WorkspaceRenameRequest>(req.Http.Request.DataAsString, _jsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as WorkspaceRenameRequest.");

                try
                {
                    return await _workspace.RenameAsync(vessel, request).ConfigureAwait(false);
                }
                catch (Exception ex) when (TryMapWorkspaceException(req, ex, out ApiErrorResponse error))
                {
                    return error;
                }
            },
            api => api
                .WithTag("Workspace")
                .WithSummary("Rename or move one workspace entry")
                .WithDescription("Renames or moves one file or directory inside the vessel working tree.")
                .WithParameter(OpenApiParameterMetadata.Path("vesselId", "Vessel ID (vsl_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<WorkspaceRenameRequest>("Workspace rename request", true))
                .WithResponse(200, OpenApiJson.For<WorkspaceOperationResult>("Rename result"))
                .WithSecurity("ApiKey"));

            app.Delete("/api/v1/workspace/vessels/{vesselId}/entry", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                Vessel? vessel = await ReadVesselForContextAsync(ctx, req.Parameters["vesselId"]).ConfigureAwait(false);
                if (vessel == null)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Vessel not found" };
                }

                string? path = req.Query.GetValueOrDefault("path");
                if (String.IsNullOrWhiteSpace(path))
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "path is required" };
                }

                try
                {
                    return await _workspace.DeleteAsync(vessel, path).ConfigureAwait(false);
                }
                catch (Exception ex) when (TryMapWorkspaceException(req, ex, out ApiErrorResponse error))
                {
                    return error;
                }
            },
            api => api
                .WithTag("Workspace")
                .WithSummary("Delete one workspace entry")
                .WithDescription("Deletes one file or directory inside the vessel working tree.")
                .WithParameter(OpenApiParameterMetadata.Path("vesselId", "Vessel ID (vsl_ prefix)"))
                .WithParameter(OpenApiParameterMetadata.Query("path", "Repository-relative file or directory path", true))
                .WithResponse(200, OpenApiJson.For<WorkspaceOperationResult>("Delete result"))
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/workspace/vessels/{vesselId}/search", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                Vessel? vessel = await ReadVesselForContextAsync(ctx, req.Parameters["vesselId"]).ConfigureAwait(false);
                if (vessel == null)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Vessel not found" };
                }

                string? query = req.Query.GetValueOrDefault("q");
                if (String.IsNullOrWhiteSpace(query))
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "q is required" };
                }

                int maxResults = 200;
                string? rawMaxResults = req.Query.GetValueOrDefault("maxResults");
                if (!String.IsNullOrWhiteSpace(rawMaxResults) && int.TryParse(rawMaxResults, out int parsedMaxResults))
                {
                    maxResults = Math.Clamp(parsedMaxResults, 1, 1000);
                }

                try
                {
                    return await _workspace.SearchAsync(vessel, query, maxResults).ConfigureAwait(false);
                }
                catch (Exception ex) when (TryMapWorkspaceException(req, ex, out ApiErrorResponse error))
                {
                    return error;
                }
            },
            api => api
                .WithTag("Workspace")
                .WithSummary("Search one workspace")
                .WithDescription("Searches text files in the vessel working tree and returns line-level matches.")
                .WithParameter(OpenApiParameterMetadata.Path("vesselId", "Vessel ID (vsl_ prefix)"))
                .WithParameter(OpenApiParameterMetadata.Query("q", "Search text query", true))
                .WithParameter(OpenApiParameterMetadata.Query("maxResults", "Maximum number of matches to return", false))
                .WithResponse(200, OpenApiJson.For<WorkspaceSearchResult>("Workspace search results"))
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/workspace/vessels/{vesselId}/changes", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                Vessel? vessel = await ReadVesselForContextAsync(ctx, req.Parameters["vesselId"]).ConfigureAwait(false);
                if (vessel == null)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Vessel not found" };
                }

                try
                {
                    return await _workspace.GetChangesAsync(vessel).ConfigureAwait(false);
                }
                catch (Exception ex) when (TryMapWorkspaceException(req, ex, out ApiErrorResponse error))
                {
                    return error;
                }
            },
            api => api
                .WithTag("Workspace")
                .WithSummary("Get workspace changes")
                .WithDescription("Returns branch status and changed files from the vessel working tree.")
                .WithParameter(OpenApiParameterMetadata.Path("vesselId", "Vessel ID (vsl_ prefix)"))
                .WithResponse(200, OpenApiJson.For<WorkspaceChangesResult>("Workspace change summary"))
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/workspace/vessels/{vesselId}/status", async (ApiRequest req) =>
            {
                AuthContext? ctx = await AuthorizeAsync(req, authenticate, authz).ConfigureAwait(false);
                if (ctx == null) return BuildAuthError(req);

                Vessel? vessel = await ReadVesselForContextAsync(ctx, req.Parameters["vesselId"]).ConfigureAwait(false);
                if (vessel == null)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Vessel not found" };
                }

                try
                {
                    List<WorkspaceActiveMission> activeMissions = await GetActiveMissionSummariesAsync(ctx, vessel.Id).ConfigureAwait(false);
                    return await _workspace.GetStatusAsync(vessel, activeMissions).ConfigureAwait(false);
                }
                catch (Exception ex) when (TryMapWorkspaceException(req, ex, out ApiErrorResponse error))
                {
                    return error;
                }
            },
            api => api
                .WithTag("Workspace")
                .WithSummary("Get workspace status")
                .WithDescription("Returns high-level workspace status, git branch state, and active mission overlap context.")
                .WithParameter(OpenApiParameterMetadata.Path("vesselId", "Vessel ID (vsl_ prefix)"))
                .WithResponse(200, OpenApiJson.For<WorkspaceStatusResult>("Workspace status"))
                .WithSecurity("ApiKey"));
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
            Func<WatsonWebserver.Core.HttpContextBase, Task<AuthContext>> authenticate,
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

        private async Task<Vessel?> ReadVesselForContextAsync(AuthContext ctx, string vesselId)
        {
            if (ctx.IsAdmin) return await _database.Vessels.ReadAsync(vesselId).ConfigureAwait(false);
            if (ctx.IsTenantAdmin) return await _database.Vessels.ReadAsync(ctx.TenantId!, vesselId).ConfigureAwait(false);
            return await _database.Vessels.ReadAsync(ctx.TenantId!, ctx.UserId!, vesselId).ConfigureAwait(false);
        }

        private async Task<List<WorkspaceActiveMission>> GetActiveMissionSummariesAsync(AuthContext ctx, string vesselId)
        {
            List<Mission> missions = ctx.IsAdmin
                ? await _database.Missions.EnumerateByVesselAsync(vesselId).ConfigureAwait(false)
                : await _database.Missions.EnumerateByVesselAsync(ctx.TenantId!, vesselId).ConfigureAwait(false);

            return missions
                .Where(IsActiveMission)
                .OrderByDescending(mission => mission.LastUpdateUtc)
                .Select(mission => new WorkspaceActiveMission
                {
                    MissionId = mission.Id,
                    Title = mission.Title ?? String.Empty,
                    Status = mission.Status.ToString(),
                    ScopedFiles = ParseMissionScopedFiles(mission.Description ?? String.Empty).ToList()
                })
                .ToList();
        }

        private static bool IsActiveMission(Mission mission)
        {
            return mission.Status == MissionStatusEnum.Pending
                || mission.Status == MissionStatusEnum.Assigned
                || mission.Status == MissionStatusEnum.InProgress
                || mission.Status == MissionStatusEnum.Review
                || mission.Status == MissionStatusEnum.Testing;
        }

        private static HashSet<string> ParseMissionScopedFiles(string description)
        {
            HashSet<string> files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (String.IsNullOrWhiteSpace(description))
                return files;

            foreach (Match directiveMatch in _ScopedFilesDirectiveRegex.Matches(description))
            {
                string fileSegment = directiveMatch.Groups["files"].Value;
                foreach (Match pathMatch in _ScopedFileTokenRegex.Matches(fileSegment))
                {
                    string normalizedPath = NormalizeMissionPath(pathMatch.Groups["path"].Value);
                    if (!String.IsNullOrWhiteSpace(normalizedPath))
                        files.Add(normalizedPath);
                }
            }

            return files;
        }

        private static string NormalizeMissionPath(string path)
        {
            return (path ?? String.Empty).Trim().Replace('\\', '/');
        }

        private static bool TryMapWorkspaceException(ApiRequest req, Exception ex, out ApiErrorResponse error)
        {
            error = new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = ex.Message };
            switch (ex)
            {
                case WorkspaceConflictException:
                    req.Http.Response.StatusCode = 409;
                    error.Error = ApiResultEnum.Conflict;
                    return true;
                case UnauthorizedAccessException:
                    req.Http.Response.StatusCode = 400;
                    return true;
                case FileNotFoundException:
                case DirectoryNotFoundException:
                    req.Http.Response.StatusCode = 404;
                    error.Error = ApiResultEnum.NotFound;
                    return true;
                case InvalidOperationException:
                case ArgumentException:
                    req.Http.Response.StatusCode = 400;
                    return true;
                default:
                    return false;
            }
        }
    }
}

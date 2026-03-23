namespace Armada.Server.Routes
{
    using System.Diagnostics;
    using System.IO;
    using System.Text.Json;
    using SwiftStack;
    using SwiftStack.Rest;
    using SwiftStack.Rest.OpenApi;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// REST API routes for vessel management.
    /// </summary>
    public class VesselRoutes
    {
        private readonly DatabaseDriver _database;
        private readonly Func<string, string, string?, string?, string?, string?, string?, string?, Task> _emitEvent;
        private readonly JsonSerializerOptions _jsonOptions;

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="emitEvent">Event broadcast callback.</param>
        /// <param name="jsonOptions">JSON serializer options.</param>
        public VesselRoutes(
            DatabaseDriver database,
            Func<string, string, string?, string?, string?, string?, string?, string?, Task> emitEvent,
            JsonSerializerOptions jsonOptions)
        {
            _database = database;
            _emitEvent = emitEvent;
            _jsonOptions = jsonOptions;
        }

        /// <summary>
        /// Register routes with the application.
        /// </summary>
        /// <param name="app">SwiftStack application.</param>
        /// <param name="authenticate">Authentication middleware.</param>
        /// <param name="authz">Authorization service.</param>
        public void Register(
            SwiftStackApp app,
            Func<WatsonWebserver.Core.HttpContextBase, Task<AuthContext>> authenticate,
            IAuthorizationService authz)
        {
            // Vessels
            app.Rest.Get("/api/v1/vessels", async (AppRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return (object)new { Error = ctx.IsAuthenticated ? "Forbidden" : "Unauthorized" };
                }
                EnumerationQuery query = new EnumerationQuery();
                query.ApplyQuerystringOverrides(key => req.Query.GetValueOrDefault(key));
                Stopwatch sw = Stopwatch.StartNew();
                EnumerationResult<Vessel> result = ctx.IsAdmin
                    ? await _database.Vessels.EnumerateAsync(query).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Vessels.EnumerateAsync(ctx.TenantId!, query).ConfigureAwait(false)
                        : await _database.Vessels.EnumerateAsync(ctx.TenantId!, ctx.UserId!, query).ConfigureAwait(false);
                result.TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                return result;
            },
            api => api
                .WithTag("Vessels")
                .WithSummary("List all vessels")
                .WithDescription("Returns all registered vessels (git repositories), optionally filtered by fleet.")
                .WithParameter(OpenApiParameterMetadata.Query("fleetId", "Filter by fleet ID", false))
                .WithResponse(200, OpenApiResponseMetadata.Json<EnumerationResult<Vessel>>("Paginated vessel list"))
                .WithSecurity("ApiKey"));

            app.Rest.Post<EnumerationQuery>("/api/v1/vessels/enumerate", async (AppRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return (object)new { Error = ctx.IsAuthenticated ? "Forbidden" : "Unauthorized" };
                }
                EnumerationQuery query = JsonSerializer.Deserialize<EnumerationQuery>(req.Http.Request.DataAsString, _jsonOptions) ?? new EnumerationQuery();
                query.ApplyQuerystringOverrides(key => req.Query.GetValueOrDefault(key));
                Stopwatch sw = Stopwatch.StartNew();
                EnumerationResult<Vessel> result = ctx.IsAdmin
                    ? await _database.Vessels.EnumerateAsync(query).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Vessels.EnumerateAsync(ctx.TenantId!, query).ConfigureAwait(false)
                        : await _database.Vessels.EnumerateAsync(ctx.TenantId!, ctx.UserId!, query).ConfigureAwait(false);
                result.TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                return result;
            },
            api => api
                .WithTag("Vessels")
                .WithSummary("Enumerate vessels")
                .WithDescription("Paginated enumeration of vessels with optional filtering and sorting.")
                .WithRequestBody(OpenApiRequestBodyMetadata.Json<EnumerationQuery>("Enumeration query", false))
                .WithSecurity("ApiKey"));

            app.Rest.Post<Vessel>("/api/v1/vessels", async (AppRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return (object)new { Error = ctx.IsAuthenticated ? "Forbidden" : "Unauthorized" };
                }
                Vessel vessel = JsonSerializer.Deserialize<Vessel>(req.Http.Request.DataAsString, _jsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as Vessel.");
                vessel.TenantId = ctx.TenantId;
                vessel.UserId = ctx.UserId;
                vessel = await _database.Vessels.CreateAsync(vessel).ConfigureAwait(false);
                req.Http.Response.StatusCode = 201;
                return vessel;
            },
            api => api
                .WithTag("Vessels")
                .WithSummary("Create a vessel")
                .WithDescription("Registers a new vessel (git repository) and returns it with an assigned ID.")
                .WithRequestBody(OpenApiRequestBodyMetadata.Json<Vessel>("Vessel data", true))
                .WithResponse(201, OpenApiResponseMetadata.Json<Vessel>("Created vessel"))
                .WithSecurity("ApiKey"));

            app.Rest.Get("/api/v1/vessels/{id}", async (AppRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return (object)new { Error = ctx.IsAuthenticated ? "Forbidden" : "Unauthorized" };
                }
                string id = req.Parameters["id"];
                Vessel? vessel = ctx.IsAdmin
                    ? await _database.Vessels.ReadAsync(id).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Vessels.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false)
                        : await _database.Vessels.ReadAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                if (vessel == null) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Vessel not found" }; }
                return (object)vessel;
            },
            api => api
                .WithTag("Vessels")
                .WithSummary("Get a vessel")
                .WithDescription("Returns a single vessel by ID.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Vessel ID (vsl_ prefix)"))
                .WithResponse(200, OpenApiResponseMetadata.Json<Vessel>("Vessel details"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Rest.Put<Vessel>("/api/v1/vessels/{id}", async (AppRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return (object)new { Error = ctx.IsAuthenticated ? "Forbidden" : "Unauthorized" };
                }
                string id = req.Parameters["id"];
                Vessel? existing = ctx.IsAdmin
                    ? await _database.Vessels.ReadAsync(id).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Vessels.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false)
                        : await _database.Vessels.ReadAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                if (existing == null) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Vessel not found" }; }
                Vessel updated = JsonSerializer.Deserialize<Vessel>(req.Http.Request.DataAsString, _jsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as Vessel.");
                updated.Id = id;
                updated = await _database.Vessels.UpdateAsync(updated).ConfigureAwait(false);
                return (object)updated;
            },
            api => api
                .WithTag("Vessels")
                .WithSummary("Update a vessel")
                .WithDescription("Updates an existing vessel by ID.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Vessel ID (vsl_ prefix)"))
                .WithRequestBody(OpenApiRequestBodyMetadata.Json<Vessel>("Updated vessel data", true))
                .WithResponse(200, OpenApiResponseMetadata.Json<Vessel>("Updated vessel"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Rest.Patch<Vessel>("/api/v1/vessels/{id}/context", async (AppRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return (object)new { Error = ctx.IsAuthenticated ? "Forbidden" : "Unauthorized" };
                }
                string id = req.Parameters["id"];
                Vessel? existing = ctx.IsAdmin
                    ? await _database.Vessels.ReadAsync(id).ConfigureAwait(false)
                    : ctx.IsTenantAdmin
                        ? await _database.Vessels.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false)
                        : await _database.Vessels.ReadAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                if (existing == null) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Vessel not found" }; }
                Vessel patch = JsonSerializer.Deserialize<Vessel>(req.Http.Request.DataAsString, _jsonOptions)
                    ?? throw new InvalidOperationException("Request body could not be deserialized as Vessel.");
                if (patch.ProjectContext != null)
                    existing.ProjectContext = patch.ProjectContext;
                if (patch.StyleGuide != null)
                    existing.StyleGuide = patch.StyleGuide;
                if (patch.ModelContext != null)
                    existing.ModelContext = patch.ModelContext;
                existing = await _database.Vessels.UpdateAsync(existing).ConfigureAwait(false);
                return (object)existing;
            },
            api => api
                .WithTag("Vessels")
                .WithSummary("Update vessel context")
                .WithDescription("Updates only the ProjectContext and StyleGuide fields of a vessel.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Vessel ID (vsl_ prefix)"))
                .WithRequestBody(OpenApiRequestBodyMetadata.Json<Vessel>("Vessel context data (projectContext, styleGuide)", true))
                .WithResponse(200, OpenApiResponseMetadata.Json<Vessel>("Updated vessel"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Rest.Get("/api/v1/vessels/{id}/git-status", async (AppRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return (object)new { Error = ctx.IsAuthenticated ? "Forbidden" : "Unauthorized" };
                }
                string id = req.Parameters["id"];
                Vessel? vessel = ctx.IsAdmin
                    ? await _database.Vessels.ReadAsync(id).ConfigureAwait(false)
                    : await _database.Vessels.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false);
                if (vessel == null) { req.Http.Response.StatusCode = 404; return new ApiErrorResponse { Error = ApiResultEnum.NotFound, Message = "Vessel not found" }; }
                if (String.IsNullOrEmpty(vessel.WorkingDirectory) || !Directory.Exists(vessel.WorkingDirectory))
                    return (object)new { VesselId = id, CommitsAhead = (int?)null, CommitsBehind = (int?)null, Error = "No working directory configured or directory does not exist" };

                try
                {
                    string baseBranch = vessel.DefaultBranch ?? "main";

                    // Fetch latest from remote (silent, best-effort)
                    try { await RunGitCommandAsync(vessel.WorkingDirectory, "fetch", "origin", "--quiet").ConfigureAwait(false); }
                    catch { /* ignore fetch failures -- offline or no remote */ }

                    string aheadStr = await RunGitCommandAsync(vessel.WorkingDirectory, "rev-list", "--count", "origin/" + baseBranch + "..HEAD").ConfigureAwait(false);
                    string behindStr = await RunGitCommandAsync(vessel.WorkingDirectory, "rev-list", "--count", "HEAD..origin/" + baseBranch).ConfigureAwait(false);

                    int.TryParse(aheadStr.Trim(), out int ahead);
                    int.TryParse(behindStr.Trim(), out int behind);

                    return (object)new { VesselId = id, CommitsAhead = ahead, CommitsBehind = behind };
                }
                catch (Exception ex)
                {
                    return (object)new { VesselId = id, CommitsAhead = (int?)null, CommitsBehind = (int?)null, Error = "Git error: " + ex.Message };
                }
            },
            api => api
                .WithTag("Vessels")
                .WithSummary("Get vessel git status")
                .WithDescription("Returns commits ahead/behind the remote default branch for the vessel's working directory.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Vessel ID (vsl_ prefix)"))
                .WithSecurity("ApiKey"));

            app.Rest.Delete("/api/v1/vessels/{id}", async (AppRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return (object)new { Error = ctx.IsAuthenticated ? "Forbidden" : "Unauthorized" };
                }
                string id = req.Parameters["id"];
                if (ctx.IsAdmin)
                    await _database.Vessels.DeleteAsync(id).ConfigureAwait(false);
                else if (ctx.IsTenantAdmin)
                    await _database.Vessels.DeleteAsync(ctx.TenantId!, id).ConfigureAwait(false);
                else
                    await _database.Vessels.DeleteAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                req.Http.Response.StatusCode = 204;
                return null;
            },
            api => api
                .WithTag("Vessels")
                .WithSummary("Delete a vessel")
                .WithDescription("Deletes a vessel by ID.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Vessel ID (vsl_ prefix)"))
                .WithResponse(204, OpenApiResponseMetadata.NoContent())
                .WithSecurity("ApiKey"));

            app.Rest.Post<DeleteMultipleRequest>("/api/v1/vessels/delete/multiple", async (AppRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return (object)new { Error = ctx.IsAuthenticated ? "Forbidden" : "Unauthorized" };
                }
                DeleteMultipleRequest? body = JsonSerializer.Deserialize<DeleteMultipleRequest>(req.Http.Request.DataAsString, _jsonOptions);
                if (body == null || body.Ids == null || body.Ids.Count == 0)
                    return (object)new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "Ids is required and must not be empty" };

                DeleteMultipleResult result = new DeleteMultipleResult();
                foreach (string id in body.Ids)
                {
                    if (String.IsNullOrEmpty(id))
                    {
                        result.Skipped.Add(new DeleteMultipleSkipped(id ?? "", "Empty ID"));
                        continue;
                    }
                    Vessel? existing = ctx.IsAdmin
                        ? await _database.Vessels.ReadAsync(id).ConfigureAwait(false)
                        : ctx.IsTenantAdmin
                            ? await _database.Vessels.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false)
                            : await _database.Vessels.ReadAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                    if (existing == null)
                    {
                        result.Skipped.Add(new DeleteMultipleSkipped(id, "Not found"));
                        continue;
                    }
                    if (ctx.IsAdmin)
                        await _database.Vessels.DeleteAsync(id).ConfigureAwait(false);
                    else if (ctx.IsTenantAdmin)
                        await _database.Vessels.DeleteAsync(ctx.TenantId!, id).ConfigureAwait(false);
                    else
                        await _database.Vessels.DeleteAsync(ctx.TenantId!, ctx.UserId!, id).ConfigureAwait(false);
                    result.Deleted++;
                }

                await _emitEvent("vessel.batch_deleted", "Batch deleted " + result.Deleted + " vessels",
                    "vessel", null, null, null, null, null).ConfigureAwait(false);

                result.ResolveStatus();
                return (object)result;
            },
            api => api
                .WithTag("Vessels")
                .WithSummary("Batch delete multiple vessels")
                .WithDescription("Permanently deletes multiple vessels from the database by ID. Returns a summary of deleted and skipped entries. This cannot be undone.")
                .WithRequestBody(OpenApiRequestBodyMetadata.Json<DeleteMultipleRequest>("List of vessel IDs to delete"))
                .WithResponse(200, OpenApiResponseMetadata.Json<DeleteMultipleResult>("Delete result summary"))
                .WithSecurity("ApiKey"));
        }

        /// <summary>
        /// Run a git command and return stdout.
        /// </summary>
        private static async Task<string> RunGitCommandAsync(string workingDirectory, params string[] args)
        {
            ProcessStartInfo psi = new ProcessStartInfo("git")
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (string arg in args) psi.ArgumentList.Add(arg);

            using (Process process = Process.Start(psi)!)
            {
                string output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
                await process.WaitForExitAsync().ConfigureAwait(false);
                if (process.ExitCode != 0)
                {
                    string error = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
                    throw new InvalidOperationException("git exited with code " + process.ExitCode + ": " + error.Trim());
                }
                return output;
            }
        }
    }
}

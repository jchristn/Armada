namespace Armada.Server.Routes
{
    using System.IO;
    using System.Text.Json;
    using SwiftStack;
    using SwiftStack.Rest;
    using SwiftStack.Rest.OpenApi;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using Armada.Server.Mcp.Tools;

    /// <summary>
    /// REST API routes for backup management.
    /// </summary>
    public class BackupRoutes
    {
        private readonly DatabaseDriver _database;
        private readonly ArmadaSettings _settings;
        private readonly JsonSerializerOptions _jsonOptions;

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Application settings.</param>
        /// <param name="jsonOptions">JSON serializer options.</param>
        public BackupRoutes(
            DatabaseDriver database,
            ArmadaSettings settings,
            JsonSerializerOptions jsonOptions)
        {
            _database = database;
            _settings = settings;
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
            // Backup & Restore
            app.Rest.Get("/api/v1/backup", async (AppRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                object backupResult = await McpToolHelpers.PerformBackupAsync(_database, _settings, null).ConfigureAwait(false);
                string zipPath = (string)backupResult.GetType().GetProperty("Path")!.GetValue(backupResult)!;
                byte[] fileBytes = await File.ReadAllBytesAsync(zipPath).ConfigureAwait(false);
                string filename = Path.GetFileName(zipPath);
                req.Http.Response.ContentType = "application/zip";
                req.Http.Response.Headers.Add("Content-Disposition", "attachment; filename=\"" + filename + "\"");
                await req.Http.Response.Send(fileBytes).ConfigureAwait(false);
                return null;
            },
            api => api
                .WithTag("Backup")
                .WithSummary("Download backup")
                .WithDescription("Creates and streams a ZIP backup of the database and settings.")
                .WithSecurity("ApiKey"));

            app.Rest.Post("/api/v1/restore", async (AppRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.BadRequest : ApiResultEnum.BadRequest, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }
                byte[] body = req.Http.Request.DataAsBytes;
                if (body == null || body.Length == 0)
                    return (object)new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "Request body must contain the ZIP file" };

                string tempZipPath = Path.Combine(Path.GetTempPath(), "armada-upload-" + Guid.NewGuid().ToString("N") + ".zip");
                try
                {
                    await File.WriteAllBytesAsync(tempZipPath, body).ConfigureAwait(false);
                    string? originalFilename = req.Http.Request.Headers.Get("X-Original-Filename");
                    object result = await McpToolHelpers.PerformRestoreAsync(_database, _settings, tempZipPath, originalFilename).ConfigureAwait(false);
                    return result;
                }
                finally
                {
                    if (File.Exists(tempZipPath))
                    {
                        try { File.Delete(tempZipPath); }
                        catch { /* best effort */ }
                    }
                }
            },
            api => api
                .WithTag("Backup")
                .WithSummary("Restore from backup")
                .WithDescription("Accepts a ZIP backup file in the request body and restores the database and settings. Server restart recommended after restore.")
                .WithSecurity("ApiKey"));
        }
    }
}

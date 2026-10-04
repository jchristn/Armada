namespace Armada.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading.Tasks;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;
    using Armada.Core;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using SyslogLogging;

    /// <summary>
    /// REST API routes for bulk vessel import: browse the Admiral filesystem, discover candidates, import the selected
    /// candidates, and read import history. Browse, discover, and import require TenantAdmin because they read the
    /// host filesystem; history reads require authentication. Everything is scoped to the caller's tenant.
    /// </summary>
    public class VesselImportRoutes
    {
        #region Private-Members

        private readonly string _Header = "[VesselImportRoutes] ";
        private readonly IVesselImportService _Import;
        private readonly IFleetCategorizationService? _Categorization;
        private readonly LoggingModule _Logging;
        private static readonly JsonSerializerOptions _BodyJsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="import">Vessel import service.</param>
        /// <param name="categorization">Fleet categorization service, or null when categorization is unavailable.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when import or logging is null.</exception>
        public VesselImportRoutes(IVesselImportService import, IFleetCategorizationService? categorization, LoggingModule logging)
        {
            _Import = import ?? throw new ArgumentNullException(nameof(import));
            _Categorization = categorization;
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
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
            app.Get("/api/v1/vessels/import/browse", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Authorize(req, ctx, authz);
                if (denied != null) return denied;

                string? path = DecodeBrowsePath(req.Query.GetValueOrDefault("path"));
                try
                {
                    VesselBrowseResult result = await _Import.BrowseAsync(path).ConfigureAwait(false);
                    return (object)result;
                }
                catch (VesselImportPathNotAllowedException ex)
                {
                    return Error(req, 403, ApiResultEnum.BadRequest, ex.Message, VesselImportCodes.PathNotAllowed, ex.RejectedPath);
                }
                catch (DirectoryNotFoundException ex)
                {
                    return Error(req, 404, ApiResultEnum.NotFound, ex.Message, VesselImportCodes.DirectoryNotFound, path);
                }
                catch (UnauthorizedAccessException ex)
                {
                    return Error(req, 403, ApiResultEnum.BadRequest, "Directory cannot be read: " + ex.Message, VesselImportCodes.PathNotAllowed, path);
                }
                catch (ArgumentException ex)
                {
                    return Error(req, 400, ApiResultEnum.BadRequest, ex.Message, VesselImportCodes.InvalidRequest, path);
                }
            },
            api => api
                .WithTag("Vessel Import")
                .WithSummary("Browse directories for import")
                .WithDescription("Lists the browsable subdirectories of a directory on the Admiral host, flagging git repositories and worktrees. Without a path, lists the allowed roots (Import.AllowedRoots, or the user profile directory when none are configured). Excluded and dot-prefixed names are skipped. Requires TenantAdmin.")
                .WithParameter(OpenApiParameterMetadata.Query("path", "Directory to list, base64url-encoded (UTF-8). A plain absolute path is also accepted. Omit to list the allowed roots.", false))
                .WithResponse(200, OpenApiJson.For<VesselBrowseResult>("Directory listing"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(403, OpenApiJson.For<ApiErrorResponse>("Not a tenant admin, or the path is outside the allowed roots"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Post<VesselDiscoveryRequest>("/api/v1/vessels/import/discover", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Authorize(req, ctx, authz);
                if (denied != null) return denied;

                VesselDiscoveryRequest? body = DeserializeBody<VesselDiscoveryRequest>(req);
                if (body == null) return Error(req, 400, ApiResultEnum.BadRequest, "Request body is required.", VesselImportCodes.InvalidRequest, null);

                try
                {
                    VesselImportDiscoverResponse result = await _Import.DiscoverAsync(ResolveTenant(ctx), ctx.UserId, body).ConfigureAwait(false);
                    req.Http.Response.StatusCode = result.RunsInBackground ? 202 : 200;
                    return (object)result;
                }
                catch (VesselImportPathNotAllowedException ex)
                {
                    return Error(req, 403, ApiResultEnum.BadRequest, ex.Message, VesselImportCodes.PathNotAllowed, ex.RejectedPath);
                }
                catch (NotSupportedException ex)
                {
                    return Error(req, 400, ApiResultEnum.BadRequest, ex.Message, VesselImportCodes.HarborNotSupported, null);
                }
                catch (ArgumentException ex)
                {
                    return Error(req, 400, ApiResultEnum.BadRequest, ex.Message, VesselImportCodes.InvalidRequest, null);
                }
            },
            api => api
                .WithTag("Vessel Import")
                .WithSummary("Discover vessel import candidates")
                .WithDescription("Scans the given directories and roots on the Admiral host for git repositories and persists the result as an import batch in status Discovered. Creates no vessels. With runInBackground=true the request is validated, the batch is created in status Discovering, and the scan runs as a VesselDiscovery job (202 with jobId); poll the batch until it is Discovered or Failed. Requires TenantAdmin.")
                .WithRequestBody(OpenApiJson.BodyFor<VesselDiscoveryRequest>("Directories and roots to scan", true))
                .WithResponse(200, OpenApiJson.For<VesselImportDiscoverResponse>("Batch and candidates"))
                .WithResponse(202, OpenApiJson.For<VesselImportDiscoverResponse>("Background discovery accepted"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(403, OpenApiJson.For<ApiErrorResponse>("Not a tenant admin, or a path is outside the allowed roots"))
                .WithSecurity("ApiKey"));

            app.Post<VesselImportRequest>("/api/v1/vessels/import", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Authorize(req, ctx, authz);
                if (denied != null) return denied;

                VesselImportRequest? body = DeserializeBody<VesselImportRequest>(req);
                if (body == null) return Error(req, 400, ApiResultEnum.BadRequest, "Request body is required.", VesselImportCodes.InvalidRequest, null);

                try
                {
                    VesselImportResponse result = await _Import.ImportAsync(ResolveTenant(ctx), ctx.UserId, body).ConfigureAwait(false);
                    req.Http.Response.StatusCode = result.RunsInBackground ? 202 : 200;
                    return (object)result;
                }
                catch (KeyNotFoundException ex)
                {
                    return Error(req, 404, ApiResultEnum.NotFound, ex.Message, VesselImportCodes.BatchNotFound, null);
                }
                catch (InvalidOperationException ex)
                {
                    return Error(req, 409, ApiResultEnum.Conflict, ex.Message, VesselImportCodes.BatchBusy, null);
                }
                catch (ArgumentException ex)
                {
                    return Error(req, 400, ApiResultEnum.BadRequest, ex.Message, VesselImportCodes.InvalidRequest, null);
                }
            },
            api => api
                .WithTag("Vessel Import")
                .WithSummary("Import discovered vessels")
                .WithDescription("Creates vessels for the selected candidates of a discovered batch. Runs inline (200) when the selection is at or below Import.InlineBatchLimit, otherwise runs as a background job (202 with jobId). Re-importing a path that already has a vessel records SkippedExisting. With categorization.enabled, a FleetCategorization job starts after the vessels exist: the captain recommends fleets for the selected vessels (created and already existing); the captain must exist in the tenant (400 otherwise). Requires TenantAdmin.")
                .WithRequestBody(OpenApiJson.BodyFor<VesselImportRequest>("Batch, selected paths, and optional defaults", true))
                .WithResponse(200, OpenApiJson.For<VesselImportResponse>("Inline import result"))
                .WithResponse(202, OpenApiJson.For<VesselImportResponse>("Background import accepted"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(403, OpenApiJson.For<ApiErrorResponse>("Not a tenant admin"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithResponse(409, OpenApiJson.For<ApiErrorResponse>("Batch is already being imported"))
                .WithSecurity("ApiKey"));

            app.Post<EnumerationQuery>("/api/v1/vessels/import/batches/enumerate", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Authorize(req, ctx, authz);
                if (denied != null) return denied;

                EnumerationQuery query = DeserializeBody<EnumerationQuery>(req) ?? new EnumerationQuery();
                query.ApplyQuerystringOverrides(key => req.Query.GetValueOrDefault(key));
                System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
                EnumerationResult<VesselImportBatch> result = await _Import.EnumerateBatchesAsync(ResolveTenant(ctx), query).ConfigureAwait(false);
                result.TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                return (object)result;
            },
            api => api
                .WithTag("Vessel Import")
                .WithSummary("Enumerate import batches")
                .WithDescription("Paged import history for the caller's tenant, newest first by default. Honors pageNumber, pageSize, order, createdAfter, createdBefore, and status.")
                .WithRequestBody(OpenApiJson.BodyFor<EnumerationQuery>("Enumeration query", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<VesselImportBatch>>("Page of batches"))
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/vessels/import/batches/{id}", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Authorize(req, ctx, authz);
                if (denied != null) return denied;

                string id = req.Parameters["id"];
                VesselImportBatchDetail? detail = await _Import.ReadBatchAsync(ResolveTenant(ctx), id).ConfigureAwait(false);
                if (detail == null) return Error(req, 404, ApiResultEnum.NotFound, "Import batch not found", VesselImportCodes.BatchNotFound, null);
                return (object)detail;
            },
            api => api
                .WithTag("Vessel Import")
                .WithSummary("Get an import batch")
                .WithDescription("Returns an import batch with all of its items, rebuilt discovery hints, and the fleet recommendations of its latest categorization run. A batch from another tenant returns 404.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Batch ID (vib_ prefix)"))
                .WithResponse(200, OpenApiJson.For<VesselImportBatchDetail>("Batch with items"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/vessels/import/categorization/default-prompt", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Authorize(req, ctx, authz);
                if (denied != null) return denied;
                if (_Categorization == null) return Error(req, 400, ApiResultEnum.BadRequest, "Fleet categorization is not available on this Admiral.", VesselImportCodes.InvalidRequest, null);

                FleetCategorizationDefaultPrompt result = new FleetCategorizationDefaultPrompt();
                result.TemplateName = PromptTemplateService.FleetCategorizationTemplateName;
                result.Prompt = await _Categorization.GetDefaultPromptAsync().ConfigureAwait(false);
                result.TimeoutMinutes = _Categorization.TimeoutMinutes;
                return (object)result;
            },
            api => api
                .WithTag("Vessel Import")
                .WithSummary("Get the default fleet categorization prompt")
                .WithDescription("Returns the import.fleet_categorization prompt template text (editable under Configuration > Prompts) that pre-fills the categorization instructions, plus the run time limit. Requires TenantAdmin.")
                .WithResponse(200, OpenApiJson.For<FleetCategorizationDefaultPrompt>("Default prompt"))
                .WithResponse(403, OpenApiJson.For<ApiErrorResponse>("Not a tenant admin"))
                .WithSecurity("ApiKey"));

            app.Post<VesselImportCategorizationRequest>("/api/v1/vessels/import/batches/{id}/categorize", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Authorize(req, ctx, authz);
                if (denied != null) return denied;
                if (_Categorization == null) return Error(req, 400, ApiResultEnum.BadRequest, "Fleet categorization is not available on this Admiral.", VesselImportCodes.InvalidRequest, null);

                string id = req.Parameters["id"];
                VesselImportCategorizationRequest? body = DeserializeBody<VesselImportCategorizationRequest>(req);
                try
                {
                    VesselImportBatch batch = await _Categorization.CategorizeAsync(ResolveTenant(ctx), id, ctx.UserId, body).ConfigureAwait(false);
                    req.Http.Response.StatusCode = 202;
                    return (object)batch;
                }
                catch (KeyNotFoundException ex)
                {
                    return Error(req, 404, ApiResultEnum.NotFound, ex.Message, VesselImportCodes.BatchNotFound, null);
                }
                catch (InvalidOperationException ex)
                {
                    return Error(req, 409, ApiResultEnum.Conflict, ex.Message, VesselImportCodes.BatchBusy, null);
                }
                catch (ArgumentException ex)
                {
                    return Error(req, 400, ApiResultEnum.BadRequest, ex.Message, VesselImportCodes.InvalidRequest, null);
                }
            },
            api => api
                .WithTag("Vessel Import")
                .WithSummary("Run or retry fleet categorization")
                .WithDescription("Starts a FleetCategorization job for a batch whose import finished: a captain analyzes the batch's selected vessels and recommends fleets. Omitted fields (captainId, prompt, applyAutomatically) reuse the batch's previous run, so an empty body retries it. Returns 202 with the batch (categorizationStatus Pending, categorizationJobId set). Requires TenantAdmin.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Batch ID (vib_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<VesselImportCategorizationRequest>("Optional captain, prompt, and auto-apply overrides", false))
                .WithResponse(202, OpenApiJson.For<VesselImportBatch>("Categorization accepted"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(403, OpenApiJson.For<ApiErrorResponse>("Not a tenant admin"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithResponse(409, OpenApiJson.For<ApiErrorResponse>("Import not finished, or categorization already running"))
                .WithSecurity("ApiKey"));

            app.Post<FleetRecommendationApplyRequest>("/api/v1/vessels/import/batches/{id}/fleet-recommendations/apply", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                ApiErrorResponse? denied = Authorize(req, ctx, authz);
                if (denied != null) return denied;
                if (_Categorization == null) return Error(req, 400, ApiResultEnum.BadRequest, "Fleet categorization is not available on this Admiral.", VesselImportCodes.InvalidRequest, null);

                string id = req.Parameters["id"];
                FleetRecommendationApplyRequest? body = DeserializeBody<FleetRecommendationApplyRequest>(req);
                if (body == null) return Error(req, 400, ApiResultEnum.BadRequest, "Request body is required.", VesselImportCodes.InvalidRequest, null);

                try
                {
                    FleetRecommendationApplyResult result = await _Categorization.ApplyAsync(ResolveTenant(ctx), id, ctx.UserId, body).ConfigureAwait(false);
                    return (object)result;
                }
                catch (KeyNotFoundException ex)
                {
                    return Error(req, 404, ApiResultEnum.NotFound, ex.Message, VesselImportCodes.BatchNotFound, null);
                }
                catch (InvalidOperationException ex)
                {
                    return Error(req, 409, ApiResultEnum.Conflict, ex.Message, VesselImportCodes.BatchBusy, null);
                }
                catch (ArgumentException ex)
                {
                    return Error(req, 400, ApiResultEnum.BadRequest, ex.Message, VesselImportCodes.InvalidRequest, null);
                }
            },
            api => api
                .WithTag("Vessel Import")
                .WithSummary("Apply fleet recommendations")
                .WithDescription("Applies a (possibly edited) list of fleets to a batch: each fleet is reused when the tenant already has a fleet with the same name (case-insensitive) or created otherwise, and each listed vessel is assigned to it. Vessels must belong to the batch and may appear in only one fleet; fleets without vessels are skipped; a fleet named Uncategorized is never created and its vessels keep their current fleet. Marks the batch's categorization Applied. Requires TenantAdmin.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Batch ID (vib_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<FleetRecommendationApplyRequest>("Fleets and their vessels", true))
                .WithResponse(200, OpenApiJson.For<FleetRecommendationApplyResult>("Fleets used and vessel assignments"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithResponse(403, OpenApiJson.For<ApiErrorResponse>("Not a tenant admin"))
                .WithResponse(404, OpenApiResponseMetadata.NotFound())
                .WithResponse(409, OpenApiJson.For<ApiErrorResponse>("Discovery, import, or categorization still running"))
                .WithSecurity("ApiKey"));
        }

        /// <summary>
        /// Decode the browse path query value: base64url (UTF-8) when it decodes to an absolute or ~-relative path,
        /// otherwise the value URL-decoded as plain text. Returns null for an empty value.
        /// </summary>
        /// <param name="raw">Raw query value.</param>
        /// <returns>The decoded path, or null.</returns>
        public static string? DecodeBrowsePath(string? raw)
        {
            if (String.IsNullOrWhiteSpace(raw)) return null;
            string value = raw.Trim();

            if (IsBase64UrlAlphabet(value))
            {
                try
                {
                    string padded = value.Replace('-', '+').Replace('_', '/');
                    switch (padded.Length % 4)
                    {
                        case 2: padded += "=="; break;
                        case 3: padded += "="; break;
                    }

                    byte[] bytes = Convert.FromBase64String(padded);
                    string decoded = new UTF8Encoding(false, true).GetString(bytes);
                    if (decoded.StartsWith("~", StringComparison.Ordinal) || Path.IsPathFullyQualified(decoded)) return decoded;
                }
                catch (FormatException)
                {
                    // Not base64url; fall through to plain handling.
                }
                catch (DecoderFallbackException)
                {
                    // Not UTF-8; fall through to plain handling.
                }
            }

            try
            {
                return Uri.UnescapeDataString(value);
            }
            catch (UriFormatException)
            {
                return value;
            }
        }

        #endregion

        #region Private-Methods

        private ApiErrorResponse? Authorize(ApiRequest req, AuthContext ctx, IAuthorizationService authz)
        {
            if (authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery)) return null;

            req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
            if (ctx.IsAuthenticated)
            {
                _Logging.Warn(_Header + "denied " + req.Http.Request.Method + " " + req.Http.Request.Url.RawWithoutQuery
                    + " for user " + (ctx.UserId ?? "(none)") + " in tenant " + (ctx.TenantId ?? "(none)") + " request " + req.Http.Guid);
            }

            return new ApiErrorResponse
            {
                Error = ApiResultEnum.BadRequest,
                Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required"
            };
        }

        private static ApiErrorResponse Error(ApiRequest req, int statusCode, ApiResultEnum error, string message, string code, string? path)
        {
            req.Http.Response.StatusCode = statusCode;
            return new ApiErrorResponse
            {
                Error = error,
                Message = message,
                Data = new VesselImportErrorDetail(code, path)
            };
        }

        private static T? DeserializeBody<T>(ApiRequest req) where T : class
        {
            string body = req.Http.Request.DataAsString;
            if (String.IsNullOrWhiteSpace(body)) return null;
            try
            {
                return JsonSerializer.Deserialize<T>(body, _BodyJsonOptions);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string ResolveTenant(AuthContext ctx)
        {
            return String.IsNullOrEmpty(ctx.TenantId) ? Constants.DefaultTenantId : ctx.TenantId;
        }

        private static bool IsBase64UrlAlphabet(string value)
        {
            foreach (char c in value)
            {
                bool ok = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_' || c == '=';
                if (!ok) return false;
            }

            return true;
        }

        #endregion
    }
}

namespace Armada.Server.Routes
{
    using System;
    using System.Globalization;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.BlackBox;
    using Armada.Core.BlackBox.Storage;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// REST routes for the mission and voyage black box (tag BlackBox, MISSION_BLACKBOX_PLAN.md sections 7.3 and 8.1).
    /// Every route calls only <see cref="IBlackBoxService"/>, which scopes reads to the caller (an entry the caller cannot
    /// see is 404 BlackBoxNotFound, never 403). Errors are mapped by <see cref="RouteErrorMapper.ToBlackBoxResponse"/>.
    /// </summary>
    public class BlackBoxRoutes
    {
        #region Private-Members

        private const string Tag = "BlackBox";
        private const string EntryIdDescription = "Black box entry ID (bbx_ prefix)";
        private const string AudienceDescription = "Brief audience: agent (default), user or maintainer";
        private const string RedactHostsDescription = "Strip absolute paths, host names and user names (for sharing outside the installation)";

        private readonly IBlackBoxService? _Service;
        private readonly JsonSerializerOptions _JsonOptions;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="service">Black box service, or null when the black box is not wired (every route then answers 503).</param>
        /// <param name="jsonOptions">Serializer options for request bodies.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="jsonOptions"/> is null.</exception>
        public BlackBoxRoutes(IBlackBoxService? service, JsonSerializerOptions jsonOptions)
        {
            _Service = service;
            _JsonOptions = jsonOptions ?? throw new ArgumentNullException(nameof(jsonOptions));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register routes with the application.
        /// </summary>
        /// <param name="app">Web server.</param>
        /// <param name="authenticate">Authenticator.</param>
        /// <param name="authz">Route authorization.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public void Register(Webserver app, Func<HttpContextBase, Task<AuthContext>> authenticate, IAuthorizationService authz)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));
            if (authenticate == null) throw new ArgumentNullException(nameof(authenticate));
            if (authz == null) throw new ArgumentNullException(nameof(authz));

            // Watson matches these routes in registration order, so the literal routes (/blackbox/retention,
            // /blackbox/storage, ...) are registered before /blackbox/{id}.
            RegisterAdminRoutes(app, authenticate, authz);
            RegisterListRoutes(app, authenticate, authz);
            RegisterWriteRoutes(app, authenticate, authz);
            RegisterSubjectRoutes(app, authenticate, authz);
            RegisterEntryRoutes(app, authenticate, authz);
        }

        #endregion

        #region Private-Methods

        private void RegisterSubjectRoutes(Webserver app, Func<HttpContextBase, Task<AuthContext>> authenticate, IAuthorizationService authz)
        {
            app.Get("/api/v1/missions/{id}/blackbox", Handle(authenticate, authz, async (req, ctx, svc) =>
                (object?)await svc.GetMissionEntryAsync(ctx, Param(req, "id"), ReadBool(req, "includeRecord") ?? true).ConfigureAwait(false)),
            api => api
                .WithTag(Tag)
                .WithSummary("Get a mission's current black box entry")
                .WithDescription("The mission's current black box entry with its versioned record (set includeRecord=false for the index row only). 404 BlackBoxNotFound when the mission has no entry, does not exist, or belongs to another tenant (no existence leak). The mission read (GET /api/v1/missions/{id}) carries LastBlackBoxEntryId.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Mission ID (msn_ prefix)"))
                .WithParameter(OpenApiParameterMetadata.Query("includeRecord", "Include the record (default true)", false, OpenApiSchemaMetadata.Boolean()))
                .WithResponse(200, OpenApiJson.For<BlackBoxEntry>("The current entry"))
                .WithResponse(404, OpenApiJson.For<ApiErrorResponse>("BlackBoxNotFound"))
                .WithResponse(409, OpenApiJson.For<ApiErrorResponse>("BlackBoxDeleting"))
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/missions/{id}/blackbox/brief", Handle(authenticate, authz, async (req, ctx, svc) =>
            {
                BlackBoxAudienceEnum audience = ReadAudience(req);
                bool redactHosts = ReadBool(req, "redactHosts") ?? false;
                BlackBoxEntry entry = await svc.GetMissionEntryAsync(ctx, Param(req, "id"), false).ConfigureAwait(false);
                string brief = await svc.GetBriefAsync(ctx, entry.Id, audience, redactHosts).ConfigureAwait(false);
                return await SendTextAsync(req, brief, "text/markdown; charset=utf-8").ConfigureAwait(false);
            }),
            api => BriefMetadata(api, "Get the FIX THIS brief of a mission's current black box entry", "Mission ID (msn_ prefix)"));

            app.Get("/api/v1/missions/{id}/blackbox/entries", Handle(authenticate, authz, async (req, ctx, svc) =>
                (object?)await svc.EnumerateMissionEntriesAsync(ctx, Param(req, "id"), ReadPageQuery(req)).ConfigureAwait(false)),
            api => api
                .WithTag(Tag)
                .WithSummary("List a mission's black box entries")
                .WithDescription("One page of the mission's black box entries (one per failed attempt, current and superseded), newest first. Page size 1 to 1000 (normalized), default 10. An unknown or invisible mission is an empty page.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Mission ID (msn_ prefix)"))
                .WithParameter(OpenApiParameterMetadata.Query("pageNumber", "Page number (default 1)", false, OpenApiSchemaMetadata.Integer()))
                .WithParameter(OpenApiParameterMetadata.Query("pageSize", "Page size (1-1000, default 10)", false, OpenApiSchemaMetadata.Integer()))
                .WithParameter(OpenApiParameterMetadata.Query("order", "CreatedDescending (default) or CreatedAscending", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<BlackBoxEntrySummary>>("One page"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/missions/{id}/blackbox/entries/enumerate", Handle(authenticate, authz, async (req, ctx, svc) =>
                (object?)await svc.EnumerateMissionEntriesAsync(ctx, Param(req, "id"), EnumerationRequestReader.Read(req, _JsonOptions)).ConfigureAwait(false)),
            api => api
                .WithTag(Tag)
                .WithSummary("Enumerate a mission's black box entries")
                .WithDescription("POST twin of GET /api/v1/missions/{id}/blackbox/entries: paging in the body (EnumerationQuery), querystring values override it.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Mission ID (msn_ prefix)"))
                .WithRequestBody(OpenApiJson.BodyFor<EnumerationQuery>("Paging", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<BlackBoxEntrySummary>>("One page"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/voyages/{id}/blackbox", Handle(authenticate, authz, async (req, ctx, svc) =>
                (object?)await svc.GetVoyageEntryAsync(ctx, Param(req, "id"), ReadBool(req, "includeRecord") ?? true).ConfigureAwait(false)),
            api => api
                .WithTag(Tag)
                .WithSummary("Get a voyage's current black box entry")
                .WithDescription("The voyage's current black box entry with its record (includeRecord=false for the index row). 404 BlackBoxNotFound when the voyage has no entry or is not visible. The voyage read carries LastBlackBoxEntryId.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Voyage ID (vyg_ prefix)"))
                .WithParameter(OpenApiParameterMetadata.Query("includeRecord", "Include the record (default true)", false, OpenApiSchemaMetadata.Boolean()))
                .WithResponse(200, OpenApiJson.For<BlackBoxEntry>("The current entry"))
                .WithResponse(404, OpenApiJson.For<ApiErrorResponse>("BlackBoxNotFound"))
                .WithResponse(409, OpenApiJson.For<ApiErrorResponse>("BlackBoxDeleting"))
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/voyages/{id}/blackbox/brief", Handle(authenticate, authz, async (req, ctx, svc) =>
            {
                BlackBoxAudienceEnum audience = ReadAudience(req);
                bool redactHosts = ReadBool(req, "redactHosts") ?? false;
                BlackBoxEntry entry = await svc.GetVoyageEntryAsync(ctx, Param(req, "id"), false).ConfigureAwait(false);
                string brief = await svc.GetBriefAsync(ctx, entry.Id, audience, redactHosts).ConfigureAwait(false);
                return await SendTextAsync(req, brief, "text/markdown; charset=utf-8").ConfigureAwait(false);
            }),
            api => BriefMetadata(api, "Get the FIX THIS brief of a voyage's current black box entry", "Voyage ID (vyg_ prefix)"));
        }

        private void RegisterEntryRoutes(Webserver app, Func<HttpContextBase, Task<AuthContext>> authenticate, IAuthorizationService authz)
        {
            app.Get("/api/v1/blackbox/{id}", Handle(authenticate, authz, async (req, ctx, svc) =>
                (object?)await svc.GetEntryAsync(ctx, Param(req, "id"), ReadBool(req, "includeRecord") ?? true).ConfigureAwait(false)),
            api => api
                .WithTag(Tag)
                .WithSummary("Get a black box entry")
                .WithDescription("One entry with its versioned record (includeRecord=false for the index row only). 404 BlackBoxNotFound when missing or not visible; 409 BlackBoxDeleting while it is being deleted.")
                .WithParameter(OpenApiParameterMetadata.Path("id", EntryIdDescription))
                .WithParameter(OpenApiParameterMetadata.Query("includeRecord", "Include the record (default true)", false, OpenApiSchemaMetadata.Boolean()))
                .WithResponse(200, OpenApiJson.For<BlackBoxEntry>("The entry"))
                .WithResponse(404, OpenApiJson.For<ApiErrorResponse>("BlackBoxNotFound"))
                .WithResponse(409, OpenApiJson.For<ApiErrorResponse>("BlackBoxDeleting"))
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/blackbox/{id}/brief", Handle(authenticate, authz, async (req, ctx, svc) =>
            {
                BlackBoxAudienceEnum audience = ReadAudience(req);
                bool redactHosts = ReadBool(req, "redactHosts") ?? false;
                string brief = await svc.GetBriefAsync(ctx, Param(req, "id"), audience, redactHosts).ConfigureAwait(false);
                return await SendTextAsync(req, brief, "text/markdown; charset=utf-8").ConfigureAwait(false);
            }),
            api => BriefMetadata(api, "Get the FIX THIS brief of a black box entry", EntryIdDescription));

            app.Get("/api/v1/blackbox/{id}/sections/{section}", Handle(authenticate, authz, async (req, ctx, svc) =>
            {
                BlackBoxSectionEnum section = ReadSection(Param(req, "section"));
                return await svc.GetSectionAsync(ctx, Param(req, "id"), section).ConfigureAwait(false);
            }),
            api => api
                .WithTag(Tag)
                .WithSummary("Get one section of a black box record")
                .WithDescription("One record section: Subject, Status, Site, Mechanism, Classification, Workspace, Conflict, Scope, Configuration, Attempts, Evidence, Analysis, Related, Voyage, Redaction or Truncation (case-insensitive). The returned record carries only that section (other sections null). 400 InvalidArgument (field section) for any other name.")
                .WithParameter(OpenApiParameterMetadata.Path("id", EntryIdDescription))
                .WithParameter(OpenApiParameterMetadata.Path("section", "Section name"))
                .WithResponse(200, OpenApiJson.For<BlackBoxSectionResult>("The section"))
                .WithResponse(400, OpenApiJson.For<ApiErrorResponse>("InvalidArgument"))
                .WithResponse(404, OpenApiJson.For<ApiErrorResponse>("BlackBoxNotFound"))
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/blackbox/{id}/objects", Handle(authenticate, authz, async (req, ctx, svc) =>
                (object?)await svc.EnumerateObjectsAsync(ctx, Param(req, "id"), ReadPageQuery(req)).ConfigureAwait(false)),
            api => api
                .WithTag(Tag)
                .WithSummary("List the objects of a black box entry")
                .WithDescription("One page of the entry's objects (record.json, the briefs and evidence) with name, content type, encoding, sizes and SHA-256. Page size 1 to 1000 (normalized), default 10.")
                .WithParameter(OpenApiParameterMetadata.Path("id", EntryIdDescription))
                .WithParameter(OpenApiParameterMetadata.Query("pageNumber", "Page number (default 1)", false, OpenApiSchemaMetadata.Integer()))
                .WithParameter(OpenApiParameterMetadata.Query("pageSize", "Page size (1-1000, default 10)", false, OpenApiSchemaMetadata.Integer()))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<BlackBoxObjectSummary>>("One page"))
                .WithResponse(404, OpenApiJson.For<ApiErrorResponse>("BlackBoxNotFound"))
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/blackbox/{id}/objects/enumerate", Handle(authenticate, authz, async (req, ctx, svc) =>
                (object?)await svc.EnumerateObjectsAsync(ctx, Param(req, "id"), EnumerationRequestReader.Read(req, _JsonOptions)).ConfigureAwait(false)),
            api => api
                .WithTag(Tag)
                .WithSummary("Enumerate the objects of a black box entry")
                .WithDescription("POST twin of GET /api/v1/blackbox/{id}/objects: paging in the body (EnumerationQuery), querystring values override it.")
                .WithParameter(OpenApiParameterMetadata.Path("id", EntryIdDescription))
                .WithRequestBody(OpenApiJson.BodyFor<EnumerationQuery>("Paging", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<BlackBoxObjectSummary>>("One page"))
                .WithResponse(404, OpenApiJson.For<ApiErrorResponse>("BlackBoxNotFound"))
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/blackbox/{id}/objects/{name}", Handle(authenticate, authz, async (req, ctx, svc) =>
            {
                long offset = ReadLong(req, "offset") ?? 0;
                long? length = ReadLong(req, "length");
                BlackBoxObjectReadResult read = await svc.ReadObjectAsync(ctx, Param(req, "id"), Param(req, "name"), offset, length).ConfigureAwait(false);
                req.Http.Response.Headers.Add("X-BlackBox-Total-Bytes", read.TotalBytes.ToString(CultureInfo.InvariantCulture));
                req.Http.Response.Headers.Add("X-BlackBox-Offset", read.Offset.ToString(CultureInfo.InvariantCulture));
                long next = read.Offset + read.Data.LongLength;
                if (next < read.TotalBytes && read.Data.LongLength > 0) req.Http.Response.Headers.Add("X-BlackBox-Next-Offset", next.ToString(CultureInfo.InvariantCulture));
                string etag = String.IsNullOrEmpty(read.Object.Sha256) ? Sha256Hex(read.Data) : read.Object.Sha256!;
                return await SendBytesAsync(req, read.Data, String.IsNullOrEmpty(read.Object.ContentType) ? "application/octet-stream" : read.Object.ContentType, etag, null).ConfigureAwait(false);
            }),
            api => api
                .WithTag(Tag)
                .WithSummary("Read a black box object")
                .WithDescription("The object's bytes with its own Content-Type and ETag (its SHA-256). Encode a name with a folder as one path segment (evidence%2Fgit-status.txt). offset and length page through a large object: at most 1 MB per read (default 1 MB); X-BlackBox-Total-Bytes, X-BlackBox-Offset and, when more remains, X-BlackBox-Next-Offset say where the next read starts. 400 InvalidArgument for a negative offset or a length outside 1 to 1048576; 404 BlackBoxNotFound for an unknown entry or object; 409 BlackBoxObjectCorrupt when the SHA-256 does not match; 410 BlackBoxObjectMissing when the store lost it; 503 BlackBoxStoreUnavailable.")
                .WithParameter(OpenApiParameterMetadata.Path("id", EntryIdDescription))
                .WithParameter(OpenApiParameterMetadata.Path("name", "Object name, URL-encoded (for example record.json or evidence%2Fdiff.patch)"))
                .WithParameter(OpenApiParameterMetadata.Query("offset", "Byte offset (default 0)", false, OpenApiSchemaMetadata.Integer()))
                .WithParameter(OpenApiParameterMetadata.Query("length", "Bytes to read (1-1048576, default 1048576)", false, OpenApiSchemaMetadata.Integer()))
                .WithResponse(200, OpenApiResponseMetadata.Binary("Object bytes (Content-Type from the object)", "application/octet-stream"))
                .WithResponse(400, OpenApiJson.For<ApiErrorResponse>("InvalidArgument"))
                .WithResponse(404, OpenApiJson.For<ApiErrorResponse>("BlackBoxNotFound"))
                .WithResponse(409, OpenApiJson.For<ApiErrorResponse>("BlackBoxObjectCorrupt or BlackBoxDeleting"))
                .WithResponse(410, OpenApiJson.For<ApiErrorResponse>("BlackBoxObjectMissing"))
                .WithResponse(503, OpenApiJson.For<ApiStatusErrorResponse>("BlackBoxStoreUnavailable"))
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/blackbox/{id}/download", Handle(authenticate, authz, async (req, ctx, svc) =>
            {
                string id = Param(req, "id");
                byte[] zip = await svc.DownloadAsync(ctx, id).ConfigureAwait(false);
                return await SendBytesAsync(req, zip, "application/zip", Sha256Hex(zip), SafeFileName(id) + ".zip").ConfigureAwait(false);
            }),
            api => api
                .WithTag(Tag)
                .WithSummary("Download a black box entry")
                .WithDescription("A .zip of every object of the entry plus manifest.json (BlackBoxDownloadManifest: entry, schema version, Armada version, objects, and any objects the store could not read).")
                .WithParameter(OpenApiParameterMetadata.Path("id", EntryIdDescription))
                .WithResponse(200, OpenApiResponseMetadata.Binary("Zip archive", "application/zip"))
                .WithResponse(404, OpenApiJson.For<ApiErrorResponse>("BlackBoxNotFound"))
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/blackbox/{id}/notes", Handle(authenticate, authz, async (req, ctx, svc) =>
                (object?)await svc.EnumerateNotesAsync(ctx, Param(req, "id"), ReadPageQuery(req)).ConfigureAwait(false)),
            api => api
                .WithTag(Tag)
                .WithSummary("List the notes on a black box entry")
                .WithDescription("One page of the entry's notes (operator, agent, Ask Armada and system annotations, including the record of every recovery run), newest first. Page size 1 to 1000 (normalized), default 10.")
                .WithParameter(OpenApiParameterMetadata.Path("id", EntryIdDescription))
                .WithParameter(OpenApiParameterMetadata.Query("pageNumber", "Page number (default 1)", false, OpenApiSchemaMetadata.Integer()))
                .WithParameter(OpenApiParameterMetadata.Query("pageSize", "Page size (1-1000, default 10)", false, OpenApiSchemaMetadata.Integer()))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<BlackBoxNote>>("One page"))
                .WithResponse(404, OpenApiJson.For<ApiErrorResponse>("BlackBoxNotFound"))
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/blackbox/{id}/notes/enumerate", Handle(authenticate, authz, async (req, ctx, svc) =>
                (object?)await svc.EnumerateNotesAsync(ctx, Param(req, "id"), EnumerationRequestReader.Read(req, _JsonOptions)).ConfigureAwait(false)),
            api => api
                .WithTag(Tag)
                .WithSummary("Enumerate the notes on a black box entry")
                .WithDescription("POST twin of GET /api/v1/blackbox/{id}/notes: paging in the body (EnumerationQuery), querystring values override it.")
                .WithParameter(OpenApiParameterMetadata.Path("id", EntryIdDescription))
                .WithRequestBody(OpenApiJson.BodyFor<EnumerationQuery>("Paging", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<BlackBoxNote>>("One page"))
                .WithResponse(404, OpenApiJson.For<ApiErrorResponse>("BlackBoxNotFound"))
                .WithSecurity("ApiKey"));
        }

        private void RegisterListRoutes(Webserver app, Func<HttpContextBase, Task<AuthContext>> authenticate, IAuthorizationService authz)
        {
            app.Get("/api/v1/blackbox", Handle(authenticate, authz, async (req, ctx, svc) =>
                (object?)await svc.EnumerateAsync(ctx, ParseEnumerateQuery(key => req.Query.GetValueOrDefault(key))).ConfigureAwait(false)),
            api => WithEnumerateQueryParameters(api
                    .WithTag(Tag)
                    .WithSummary("List black box entries")
                    .WithDescription("One page of the black box entries visible to the caller (global admins every tenant, tenant admins their tenant, users their own), newest first. Filters are optional; enum filters take the names of BlackBoxPhaseEnum, BlackBoxStepEnum, MissionFailureKindEnum, BlackBoxTriageEnum, BlackBoxResolutionEnum, BlackBoxStateEnum, BlackBoxCaptureModeEnum and BlackBoxSubjectTypeEnum; path matches any file role."))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<BlackBoxEntrySummary>>("One page"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/blackbox/enumerate", Handle(authenticate, authz, async (req, ctx, svc) =>
                (object?)await svc.EnumerateAsync(ctx, ReadPagedBody<BlackBoxEnumerateRequest>(req)).ConfigureAwait(false)),
            api => api
                .WithTag(Tag)
                .WithSummary("Enumerate black box entries")
                .WithDescription("POST twin of GET /api/v1/blackbox: the filters and paging in the body (BlackBoxEnumerateRequest: vesselId, voyageId, missionId, captainId, fleetId, phase, step, kind, triage, resolution, state, captureMode, subjectType, fingerprint, pinned, isCurrent, path, harborId, createdAfter, createdBefore, pageNumber, pageSize, order); pageNumber and pageSize in the querystring override the body.")
                .WithRequestBody(OpenApiJson.BodyFor<BlackBoxEnumerateRequest>("Filters and paging", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<BlackBoxEntrySummary>>("One page"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/blackbox/search", Handle(authenticate, authz, async (req, ctx, svc) =>
                (object?)await svc.SearchAsync(ctx, ReadPagedBody<BlackBoxSearchRequest>(req)).ConfigureAwait(false)),
            api => api
                .WithTag(Tag)
                .WithSummary("Search black box entries")
                .WithDescription("Full-text search over the reason, summary and notes of the entries visible to the caller, with the same filters and paging as POST /api/v1/blackbox/enumerate. query is required (1 to 200 characters); 400 InvalidArgument otherwise.")
                .WithRequestBody(OpenApiJson.BodyFor<BlackBoxSearchRequest>("Query, filters and paging", true))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<BlackBoxEntrySummary>>("One page of matches"))
                .WithResponse(400, OpenApiJson.For<ApiErrorResponse>("InvalidArgument"))
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/blackbox/bundle", Handle(authenticate, authz, async (req, ctx, svc) =>
                (object?)await svc.GetBundleAsync(ctx, ReadBody<BlackBoxBundleRequest>(req)).ConfigureAwait(false)),
            api => api
                .WithTag(Tag)
                .WithSummary("Build a maintainer bundle")
                .WithDescription("Up to max (1 to 50, default 50) of the newest matching entries, grouped by fingerprint, rendered as one Markdown document of maintainer briefs (format Markdown, the default) or as JSON entry summaries (format Json). redactHosts strips absolute paths, host names and user names for sharing outside the installation. Content is capped at 1 MB (Truncated true when cut).")
                .WithRequestBody(OpenApiJson.BodyFor<BlackBoxBundleRequest>("Filters, max, format and redactHosts", false))
                .WithResponse(200, OpenApiJson.For<BlackBoxBundleResult>("The bundle"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/blackbox/hotspots/enumerate", Handle(authenticate, authz, async (req, ctx, svc) =>
                (object?)await svc.EnumerateHotspotsAsync(ctx, ReadPagedBody<BlackBoxHotspotEnumerateRequest>(req)).ConfigureAwait(false)),
            api => api
                .WithTag(Tag)
                .WithSummary("Enumerate black box hotspots")
                .WithDescription("One page of directories that recur in black box file rows (conflicted, dirty, out of scope and so on), most entries first: vesselId, directory, fileCount, entryCount, lastSeenUtc. Filters: vesselId, role (BlackBoxFileRoleEnum), createdAfter, createdBefore.")
                .WithRequestBody(OpenApiJson.BodyFor<BlackBoxHotspotEnumerateRequest>("Filters and paging", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<BlackBoxHotspot>>("One page"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/blackbox/fingerprints/{fingerprint}/trend", Handle(authenticate, authz, async (req, ctx, svc) =>
                (object?)await svc.GetFingerprintTrendAsync(ctx, Param(req, "fingerprint")).ConfigureAwait(false)),
            api => api
                .WithTag(Tag)
                .WithSummary("Get the trend of a black box failure mode")
                .WithDescription("Occurrences of one fingerprint per Armada version (ordered by first sighting, each with open and ArmadaFixed counts; at most 1000 entries and 100 versions), scoped to what the caller can see, so a maintainer can tell whether a release fixed it.")
                .WithParameter(OpenApiParameterMetadata.Path("fingerprint", "Failure fingerprint"))
                .WithResponse(200, OpenApiJson.For<BlackBoxFingerprintTrend>("The trend"))
                .WithResponse(400, OpenApiJson.For<ApiErrorResponse>("InvalidArgument"))
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/blackbox/fingerprints/enumerate", Handle(authenticate, authz, async (req, ctx, svc) =>
                (object?)await svc.EnumerateFingerprintsAsync(ctx, ReadPagedBody<BlackBoxFingerprintEnumerateRequest>(req)).ConfigureAwait(false)),
            api => api
                .WithTag(Tag)
                .WithSummary("Enumerate black box fingerprints")
                .WithDescription("One page of failure modes (entries grouped by fingerprint) with count, open count, first and last seen, the newest entry and its phase, step and kind. Filters: vesselId, resolution, createdAfter, createdBefore.")
                .WithRequestBody(OpenApiJson.BodyFor<BlackBoxFingerprintEnumerateRequest>("Filters and paging", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<BlackBoxFingerprintSummary>>("One page"))
                .WithResponse(400, OpenApiResponseMetadata.BadRequest())
                .WithSecurity("ApiKey"));
        }

        private void RegisterWriteRoutes(Webserver app, Func<HttpContextBase, Task<AuthContext>> authenticate, IAuthorizationService authz)
        {
            app.Post("/api/v1/blackbox/{id}/notes", Handle(authenticate, authz, async (req, ctx, svc) =>
                (object?)await svc.AddNoteAsync(ctx, Param(req, "id"), ReadBody<BlackBoxNoteAddRequest>(req)).ConfigureAwait(false)),
            api => api
                .WithTag(Tag)
                .WithSummary("Add a note to a black box entry")
                .WithDescription("Adds a note (body 1 to 8000 characters; authorKind defaults to Operator). Briefs include the ten newest notes. 400 InvalidArgument for an empty or oversized body.")
                .WithParameter(OpenApiParameterMetadata.Path("id", EntryIdDescription))
                .WithRequestBody(OpenApiJson.BodyFor<BlackBoxNoteAddRequest>("Note", true))
                .WithResponse(200, OpenApiJson.For<BlackBoxNote>("The note"))
                .WithResponse(400, OpenApiJson.For<ApiErrorResponse>("InvalidArgument"))
                .WithResponse(404, OpenApiJson.For<ApiErrorResponse>("BlackBoxNotFound"))
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/blackbox/{id}/resolve", Handle(authenticate, authz, async (req, ctx, svc) =>
                (object?)await svc.ResolveAsync(ctx, Param(req, "id"), ReadBody<BlackBoxResolveRequest>(req)).ConfigureAwait(false)),
            api => api
                .WithTag(Tag)
                .WithSummary("Resolve a black box entry")
                .WithDescription("Sets the resolution (Open, Resolved, Duplicate, WontFix or ArmadaFixed), an optional note (up to 2000 characters) and link (up to 1000), recomputes the expiry and emits blackbox.resolved. Open clears ResolvedUtc.")
                .WithParameter(OpenApiParameterMetadata.Path("id", EntryIdDescription))
                .WithRequestBody(OpenApiJson.BodyFor<BlackBoxResolveRequest>("Resolution, note and link", true))
                .WithResponse(200, OpenApiJson.For<BlackBoxEntry>("The updated entry"))
                .WithResponse(400, OpenApiJson.For<ApiErrorResponse>("InvalidArgument"))
                .WithResponse(404, OpenApiJson.For<ApiErrorResponse>("BlackBoxNotFound"))
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/blackbox/{id}/pin", Handle(authenticate, authz, async (req, ctx, svc) =>
                (object?)await svc.PinAsync(ctx, Param(req, "id"), true).ConfigureAwait(false)),
            api => PinMetadata(api, "Pin a black box entry", "Pins the entry so garbage collection keeps it (no expiry)."));

            app.Post("/api/v1/blackbox/{id}/unpin", Handle(authenticate, authz, async (req, ctx, svc) =>
                (object?)await svc.PinAsync(ctx, Param(req, "id"), false).ConfigureAwait(false)),
            api => PinMetadata(api, "Unpin a black box entry", "Unpins the entry and recomputes its expiry from the retention policy."));

            app.Post("/api/v1/blackbox/{id}/recovery/{pathId}", Handle(authenticate, authz, async (req, ctx, svc) =>
            {
                string id = Param(req, "id");
                BlackBoxRecoveryRunRequest body = ReadBody<BlackBoxRecoveryRunRequest>(req);
                if (!body.AuthorKind.HasValue) body.AuthorKind = BlackBoxNoteAuthorKindEnum.Operator;
                // Visibility first, so an entry the caller cannot see is 404 whatever the state of the recovery runner.
                BlackBoxEntry entry = await svc.GetEntryAsync(ctx, id, false).ConfigureAwait(false);
                return await svc.RunRecoveryAsync(ctx, entry.Id, Param(req, "pathId"), body).ConfigureAwait(false);
            }),
            api => api
                .WithTag(Tag)
                .WithSummary("Run a recovery path of a black box entry")
                .WithDescription("Runs one recovery path recorded in the entry's analysis; never a free-form command. dryRun defaults to true and returns the plan (each step's tool and arguments) and a confirmToken that expires in 10 minutes and binds the entry, the path and the mission's current version. The real run (dryRun false) requires that token and, for a path marked dataLossRisk, acknowledgeDataLoss true; each step is authorized as if the caller had called that tool directly. Every dry run and run is noted on the entry (authorKind, default Operator) and emits blackbox.recovery_run. A token is single use and is refused when expired, for another entry, path or caller, or when the mission or the plan changed since the dry run: 409 BlackBoxConfirmationRequired asks for a new dry run. 403 when the caller may not call a step's tool; 400 when the path names an unsupported tool or another subject; 404 for an unknown path.")
                .WithParameter(OpenApiParameterMetadata.Path("id", EntryIdDescription))
                .WithParameter(OpenApiParameterMetadata.Path("pathId", "Recovery path ID from the entry's analysis (for example bbx-land-001.stash-retry)"))
                .WithRequestBody(OpenApiJson.BodyFor<BlackBoxRecoveryRunRequest>("dryRun (default true), confirmToken, acknowledgeDataLoss, authorKind (default Operator)", false))
                .WithResponse(200, OpenApiJson.For<BlackBoxRecoveryRunResult>("Plan (dry run) or step results"))
                .WithResponse(400, OpenApiJson.For<ApiErrorResponse>("InvalidArgument"))
                .WithResponse(404, OpenApiJson.For<ApiErrorResponse>("BlackBoxNotFound"))
                .WithResponse(409, OpenApiJson.For<ApiErrorResponse>("BlackBoxConfirmationRequired"))
                .WithResponse(501, OpenApiJson.For<ApiStatusErrorResponse>("NotImplemented"))
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/blackbox/{id}/recommendations/{recommendationId}/apply", Handle(authenticate, authz, async (req, ctx, svc) =>
            {
                string id = Param(req, "id");
                BlackBoxRecommendationApplyRequest body = ReadBody<BlackBoxRecommendationApplyRequest>(req);
                if (!body.AuthorKind.HasValue) body.AuthorKind = BlackBoxNoteAuthorKindEnum.Operator;
                BlackBoxEntry entry = await svc.GetEntryAsync(ctx, id, false).ConfigureAwait(false);
                return await svc.ApplyRecommendationAsync(ctx, entry.Id, Param(req, "recommendationId"), body).ConfigureAwait(false);
            }),
            api => api
                .WithTag(Tag)
                .WithSummary("Apply a configuration recommendation of a black box entry")
                .WithDescription("Applies one configuration recommendation's recorded applyAction (for example an update_vessel patch), with the same flow as recovery: dryRun defaults to true and returns the action and a confirmToken (10 minutes); the real run requires the token and is authorized as the underlying tool. 409 BlackBoxConfirmationRequired without a valid token; 400 for an advice-only recommendation; 404 for an unknown recommendation.")
                .WithParameter(OpenApiParameterMetadata.Path("id", EntryIdDescription))
                .WithParameter(OpenApiParameterMetadata.Path("recommendationId", "Recommendation ID from the entry's analysis"))
                .WithRequestBody(OpenApiJson.BodyFor<BlackBoxRecommendationApplyRequest>("dryRun (default true), confirmToken, authorKind (default Operator)", false))
                .WithResponse(200, OpenApiJson.For<BlackBoxRecommendationApplyResult>("Action (dry run) or result"))
                .WithResponse(404, OpenApiJson.For<ApiErrorResponse>("BlackBoxNotFound"))
                .WithResponse(409, OpenApiJson.For<ApiErrorResponse>("BlackBoxConfirmationRequired"))
                .WithResponse(501, OpenApiJson.For<ApiStatusErrorResponse>("NotImplemented"))
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/blackbox/{id}/reanalyze", Handle(authenticate, authz, async (req, ctx, svc) =>
                (object?)await svc.ReanalyzeAsync(ctx, Param(req, "id")).ConfigureAwait(false)),
            api => api
                .WithTag(Tag)
                .WithSummary("Re-run enrichment and analysis of a black box entry")
                .WithDescription("Re-runs the enrichers and the advisor on the entry with the current rules and rewrites record.json and the briefs. Tenant admins and global admins only.")
                .WithParameter(OpenApiParameterMetadata.Path("id", EntryIdDescription))
                .WithResponse(200, OpenApiJson.For<BlackBoxEntry>("The re-analyzed entry"))
                .WithResponse(403, OpenApiJson.For<ApiErrorResponse>("Not a tenant admin"))
                .WithResponse(404, OpenApiJson.For<ApiErrorResponse>("BlackBoxNotFound"))
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/blackbox/{id}/export-fixture", Handle(authenticate, authz, async (req, ctx, svc) =>
            {
                BlackBoxFixtureExportRequest body = ReadBody<BlackBoxFixtureExportRequest>(req);
                return await svc.ExportFixtureAsync(ctx, Param(req, "id"), String.IsNullOrWhiteSpace(body.Name) ? null : body.Name.Trim()).ConfigureAwait(false);
            }),
            api => api
                .WithTag(Tag)
                .WithSummary("Export a black box entry as a regression fixture")
                .WithDescription("The entry as a sanitized fixture (record with secrets, tenant and user ids, host paths and the repository URL removed, plus the rule ids and triage the advisor produces now) for src/Test.Shared/Fixtures/BlackBox. Global admins only.")
                .WithParameter(OpenApiParameterMetadata.Path("id", EntryIdDescription))
                .WithRequestBody(OpenApiJson.BodyFor<BlackBoxFixtureExportRequest>("Fixture name (optional)", false))
                .WithResponse(200, OpenApiJson.For<BlackBoxFixture>("The fixture"))
                .WithResponse(403, OpenApiJson.For<ApiErrorResponse>("Not a global admin"))
                .WithResponse(404, OpenApiJson.For<ApiErrorResponse>("BlackBoxNotFound"))
                .WithSecurity("ApiKey"));

            app.Delete("/api/v1/blackbox/{id}", Handle(authenticate, authz, async (req, ctx, svc) =>
                (object?)await svc.DeleteAsync(ctx, Param(req, "id")).ConfigureAwait(false)),
            api => api
                .WithTag(Tag)
                .WithSummary("Delete a black box entry")
                .WithDescription("Deletes the entry and its objects, files, links and notes (two-phase: objects first; an entry whose store delete fails stays Deleting and is retried by garbage collection, counted in pending). Tenant admins and global admins only; 404 BlackBoxNotFound when missing or not visible.")
                .WithParameter(OpenApiParameterMetadata.Path("id", EntryIdDescription))
                .WithResponse(200, OpenApiJson.For<BlackBoxDeleteResult>("Counts"))
                .WithResponse(403, OpenApiJson.For<ApiErrorResponse>("Not a tenant admin"))
                .WithResponse(404, OpenApiJson.For<ApiErrorResponse>("BlackBoxNotFound"))
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/blackbox/delete", Handle(authenticate, authz, async (req, ctx, svc) =>
                (object?)await svc.DeleteManyAsync(ctx, ReadBody<BlackBoxDeleteManyRequest>(req)).ConfigureAwait(false)),
            api => api
                .WithTag(Tag)
                .WithSummary("Delete black box entries")
                .WithDescription("Deletes up to 1000 entries by id (ids); ids that are missing or not visible are skipped. More than 1000 ids is 400 CollectionLimitExceeded before anything is deleted. Tenant admins and global admins only.")
                .WithRequestBody(OpenApiJson.BodyFor<BlackBoxDeleteManyRequest>("Ids (at most 1000)", true))
                .WithResponse(200, OpenApiJson.For<BlackBoxDeleteResult>("Counts and skipped ids"))
                .WithResponse(400, OpenApiJson.For<ApiErrorResponse>("CollectionLimitExceeded"))
                .WithResponse(403, OpenApiJson.For<ApiErrorResponse>("Not a tenant admin"))
                .WithSecurity("ApiKey"));
        }

        private void RegisterAdminRoutes(Webserver app, Func<HttpContextBase, Task<AuthContext>> authenticate, IAuthorizationService authz)
        {
            app.Post("/api/v1/blackbox/purge", Handle(authenticate, authz, async (req, ctx, svc) =>
                (object?)await svc.PurgeAsync(ctx, ReadBody<BlackBoxPurgeRequest>(req)).ConfigureAwait(false)),
            api => api
                .WithTag(Tag)
                .WithSummary("Purge black box entries by filter")
                .WithDescription("Deletes every entry matching the filters (BlackBoxEnumerateRequest fields), oldest first, up to maxEntries (1 to 100000, default 10000). dryRun defaults to true and reports what would go (matched, a sample of ids, bytes) without deleting; pinned entries are skipped unless includePinned. Global admins only.")
                .WithRequestBody(OpenApiJson.BodyFor<BlackBoxPurgeRequest>("Filters, dryRun (default true), includePinned, maxEntries", false))
                .WithResponse(200, OpenApiJson.For<BlackBoxPurgeResult>("What matched and what was deleted"))
                .WithResponse(403, OpenApiJson.For<ApiErrorResponse>("Not a global admin"))
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/blackbox/gc", Handle(authenticate, authz, async (req, ctx, svc) =>
                (object?)await svc.RunGcAsync(ctx, ReadBody<BlackBoxGcRunRequest>(req)).ConfigureAwait(false)),
            api => api
                .WithTag(Tag)
                .WithSummary("Run black box garbage collection now")
                .WithDescription("Runs one garbage collection pass (expiry, quotas, cascades, retried deletes, orphan sweep) and returns its report; dryRun true lists the candidate entries without deleting. Global admins only.")
                .WithRequestBody(OpenApiJson.BodyFor<BlackBoxGcRunRequest>("dryRun (default false)", false))
                .WithResponse(200, OpenApiJson.For<BlackBoxGcReport>("The report"))
                .WithResponse(403, OpenApiJson.For<ApiErrorResponse>("Not a global admin"))
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/blackbox/retention", Handle(authenticate, authz, async (req, ctx, svc) =>
                (object?)await svc.GetRetentionStatusAsync(ctx).ConfigureAwait(false)),
            api => api
                .WithTag(Tag)
                .WithSummary("Get black box retention status")
                .WithDescription("The retention policy, entry counts (total, pinned, deleting, expired), bytes, the next garbage collection run and the last run's report. A tenant admin sees its own tenant's counts; a global admin sees every tenant.")
                .WithResponse(200, OpenApiJson.For<BlackBoxRetentionStatus>("Retention status"))
                .WithResponse(403, OpenApiJson.For<ApiErrorResponse>("Not a tenant admin"))
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/blackbox/storage", Handle(authenticate, authz, async (req, ctx, svc) =>
                (object?)await svc.GetStorageStatusAsync(ctx).ConfigureAwait(false)),
            api => api
                .WithTag(Tag)
                .WithSummary("Get black box storage status")
                .WithDescription("The configured storage target, its health check, whether writes fall back to the database, and bytes per target. Global admins only.")
                .WithResponse(200, OpenApiJson.For<BlackBoxStorageStatus>("Storage status"))
                .WithResponse(403, OpenApiJson.For<ApiErrorResponse>("Not a global admin"))
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/blackbox/storage/migrate", Handle(authenticate, authz, async (req, ctx, svc) =>
                (object?)await svc.StartStorageMigrationAsync(ctx, ReadBody<BlackBoxStorageMigrationRequest>(req)).ConfigureAwait(false)),
            api => api
                .WithTag(Tag)
                .WithSummary("Start a black box storage migration")
                .WithDescription("Starts a resumable job that copies every entry's objects to another storage target, verifies them, switches the entry and optionally deletes the source. Returns the job; poll GET /api/v1/blackbox/storage/migrations/{id}. Global admins only. 501 NotImplemented on a server where storage migration is not merged yet.")
                .WithRequestBody(OpenApiJson.BodyFor<BlackBoxStorageMigrationRequest>("Target, source, deleteSource and batchSize", true))
                .WithResponse(200, OpenApiJson.For<BlackBoxStorageMigrationJob>("The job"))
                .WithResponse(403, OpenApiJson.For<ApiErrorResponse>("Not a global admin"))
                .WithResponse(501, OpenApiJson.For<ApiStatusErrorResponse>("NotImplemented"))
                .WithSecurity("ApiKey"));

            app.Get("/api/v1/blackbox/storage/migrations/{id}", Handle(authenticate, authz, async (req, ctx, svc) =>
                (object?)await svc.GetStorageMigrationAsync(ctx, Param(req, "id")).ConfigureAwait(false)),
            api => api
                .WithTag(Tag)
                .WithSummary("Get a black box storage migration job")
                .WithDescription("One storage migration job with its progress. Global admins only. 501 NotImplemented on a server where storage migration is not merged yet.")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Migration job ID"))
                .WithResponse(200, OpenApiJson.For<BlackBoxStorageMigrationJob>("The job"))
                .WithResponse(403, OpenApiJson.For<ApiErrorResponse>("Not a global admin"))
                .WithResponse(404, OpenApiJson.For<ApiErrorResponse>("Not found"))
                .WithResponse(501, OpenApiJson.For<ApiStatusErrorResponse>("NotImplemented"))
                .WithSecurity("ApiKey"));

            app.Post("/api/v1/blackbox/backfill", Handle(authenticate, authz, async (req, ctx, svc) =>
                (object?)await svc.BackfillAsync(ctx, ReadBody<BlackBoxBackfillRequest>(req)).ConfigureAwait(false)),
            api => api
                .WithTag(Tag)
                .WithSummary("Backfill black box entries")
                .WithDescription("Captures entries (CaptureMode Backfill) for failed missions and voyages that have none, updated since since (optional), up to maxSubjects (default 1000), and re-queues entries whose enrichment did not finish. Global admins only.")
                .WithRequestBody(OpenApiJson.BodyFor<BlackBoxBackfillRequest>("since and maxSubjects", false))
                .WithResponse(200, OpenApiJson.For<BlackBoxBackfillResult>("Counts"))
                .WithResponse(403, OpenApiJson.For<ApiErrorResponse>("Not a global admin"))
                .WithSecurity("ApiKey"));
        }

        private Func<ApiRequest, Task<object>> Handle(
            Func<HttpContextBase, Task<AuthContext>> authenticate,
            IAuthorizationService authz,
            Func<ApiRequest, AuthContext, IBlackBoxService, Task<object?>> body)
        {
            return async (ApiRequest req) =>
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

                if (_Service == null)
                {
                    req.Http.Response.StatusCode = 503;
                    return new ApiStatusErrorResponse(ApiStatusErrorCodeEnum.ServiceUnavailable, "The black box is not available on this server.")
                    {
                        Data = new BlackBoxErrorDetail(BlackBoxStoreUnavailableException.ErrorCode)
                    };
                }

                try
                {
                    return (await body(req, ctx, _Service).ConfigureAwait(false))!;
                }
                catch (Exception ex) when (RouteErrorMapper.IsBlackBoxMapped(ex))
                {
                    return RouteErrorMapper.ToBlackBoxResponse(req, ex);
                }
            };
        }

        private static OpenApiRouteMetadata BriefMetadata(OpenApiRouteMetadata api, string summary, string idDescription)
        {
            return api
                .WithTag(Tag)
                .WithSummary(summary)
                .WithDescription("The FIX THIS brief as text/markdown, rendered live from the record (with the ten newest notes), with an ETag (its SHA-256). audience agent (default) is the brief for a captain fixing the work, user the plain account for the operator (no stack frames), maintainer the Armada-defect view. 400 InvalidArgument (field audience) for any other audience; 404 BlackBoxNotFound when there is no visible entry.")
                .WithParameter(OpenApiParameterMetadata.Path("id", idDescription))
                .WithParameter(OpenApiParameterMetadata.Query("audience", AudienceDescription, false))
                .WithParameter(OpenApiParameterMetadata.Query("redactHosts", RedactHostsDescription, false, OpenApiSchemaMetadata.Boolean()))
                .WithResponse(200, OpenApiResponseMetadata.Text("Markdown brief (text/markdown)"))
                .WithResponse(400, OpenApiJson.For<ApiErrorResponse>("InvalidArgument"))
                .WithResponse(404, OpenApiJson.For<ApiErrorResponse>("BlackBoxNotFound"))
                .WithSecurity("ApiKey");
        }

        private static OpenApiRouteMetadata PinMetadata(OpenApiRouteMetadata api, string summary, string description)
        {
            return api
                .WithTag(Tag)
                .WithSummary(summary)
                .WithDescription(description)
                .WithParameter(OpenApiParameterMetadata.Path("id", EntryIdDescription))
                .WithResponse(200, OpenApiJson.For<BlackBoxEntry>("The updated entry"))
                .WithResponse(404, OpenApiJson.For<ApiErrorResponse>("BlackBoxNotFound"))
                .WithSecurity("ApiKey");
        }

        private static OpenApiRouteMetadata WithEnumerateQueryParameters(OpenApiRouteMetadata api)
        {
            return api
                .WithParameter(OpenApiParameterMetadata.Query("tenantId", "Tenant filter (global admins only; ignored otherwise)", false))
                .WithParameter(OpenApiParameterMetadata.Query("fleetId", "Fleet filter (flt_ prefix)", false))
                .WithParameter(OpenApiParameterMetadata.Query("vesselId", "Vessel filter (vsl_ prefix)", false))
                .WithParameter(OpenApiParameterMetadata.Query("voyageId", "Voyage filter (vyg_ prefix)", false))
                .WithParameter(OpenApiParameterMetadata.Query("missionId", "Mission filter (msn_ prefix)", false))
                .WithParameter(OpenApiParameterMetadata.Query("captainId", "Captain filter (cpt_ prefix)", false))
                .WithParameter(OpenApiParameterMetadata.Query("harborId", "Harbor filter (hbr_ prefix)", false))
                .WithParameter(OpenApiParameterMetadata.Query("subjectType", "Mission or Voyage", false))
                .WithParameter(OpenApiParameterMetadata.Query("phase", "Phase (BlackBoxPhaseEnum)", false))
                .WithParameter(OpenApiParameterMetadata.Query("step", "Step (BlackBoxStepEnum)", false))
                .WithParameter(OpenApiParameterMetadata.Query("kind", "Failure kind (MissionFailureKindEnum)", false))
                .WithParameter(OpenApiParameterMetadata.Query("triage", "Triage verdict (BlackBoxTriageEnum)", false))
                .WithParameter(OpenApiParameterMetadata.Query("resolution", "Resolution (BlackBoxResolutionEnum)", false))
                .WithParameter(OpenApiParameterMetadata.Query("state", "State (BlackBoxStateEnum)", false))
                .WithParameter(OpenApiParameterMetadata.Query("captureMode", "Inline, Sweeper or Backfill", false))
                .WithParameter(OpenApiParameterMetadata.Query("fingerprint", "Failure fingerprint", false))
                .WithParameter(OpenApiParameterMetadata.Query("pinned", "Pinned entries only (true) or unpinned only (false)", false, OpenApiSchemaMetadata.Boolean()))
                .WithParameter(OpenApiParameterMetadata.Query("isCurrent", "Current entries only (true) or superseded only (false)", false, OpenApiSchemaMetadata.Boolean()))
                .WithParameter(OpenApiParameterMetadata.Query("path", "Repository path recorded in any file role", false))
                .WithParameter(OpenApiParameterMetadata.Query("createdAfter", "Created at or after (ISO 8601 UTC)", false))
                .WithParameter(OpenApiParameterMetadata.Query("createdBefore", "Created before (ISO 8601 UTC)", false))
                .WithParameter(OpenApiParameterMetadata.Query("pageNumber", "Page number (default 1)", false, OpenApiSchemaMetadata.Integer()))
                .WithParameter(OpenApiParameterMetadata.Query("pageSize", "Page size (1-1000, default 10)", false, OpenApiSchemaMetadata.Integer()))
                .WithParameter(OpenApiParameterMetadata.Query("order", "CreatedDescending (default) or CreatedAscending", false));
        }

        /// <summary>
        /// Parse the black box list filters from a querystring (GET /api/v1/blackbox). Shared with the WebSocket and
        /// test code paths that read the same names.
        /// </summary>
        /// <param name="value">Querystring reader.</param>
        /// <returns>The request.</returns>
        /// <exception cref="ArgumentNullException">Thrown when value is null.</exception>
        /// <exception cref="ArgumentException">Thrown for an invalid enum, boolean, number or date.</exception>
        internal static BlackBoxEnumerateRequest ParseEnumerateQuery(Func<string, string?> value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            BlackBoxEnumerateRequest query = new BlackBoxEnumerateRequest
            {
                TenantId = Blank(value("tenantId")),
                FleetId = Blank(value("fleetId")),
                VesselId = Blank(value("vesselId")),
                VoyageId = Blank(value("voyageId")),
                MissionId = Blank(value("missionId")),
                CaptainId = Blank(value("captainId")),
                HarborId = Blank(value("harborId")),
                Fingerprint = Blank(value("fingerprint")),
                Path = Blank(value("path")),
                SubjectType = ParseEnum<BlackBoxSubjectTypeEnum>(value("subjectType"), "subjectType"),
                Phase = ParseEnum<BlackBoxPhaseEnum>(value("phase"), "phase"),
                Step = ParseEnum<BlackBoxStepEnum>(value("step"), "step"),
                Kind = ParseEnum<MissionFailureKindEnum>(value("kind"), "kind"),
                Triage = ParseEnum<BlackBoxTriageEnum>(value("triage"), "triage"),
                Resolution = ParseEnum<BlackBoxResolutionEnum>(value("resolution"), "resolution"),
                State = ParseEnum<BlackBoxStateEnum>(value("state"), "state"),
                CaptureMode = ParseEnum<BlackBoxCaptureModeEnum>(value("captureMode"), "captureMode"),
                Pinned = ParseBool(value("pinned"), "pinned"),
                IsCurrent = ParseBool(value("isCurrent"), "isCurrent"),
                CreatedAfter = ParseUtc(value("createdAfter"), "createdAfter"),
                CreatedBefore = ParseUtc(value("createdBefore"), "createdBefore")
            };

            EnumerationOrderEnum? order = ParseEnum<EnumerationOrderEnum>(value("order"), "order");
            if (order.HasValue) query.Order = order.Value;
            query.ApplyQuerystringOverrides(value);
            return query;
        }

        private EnumerationQuery ReadPageQuery(ApiRequest req)
        {
            EnumerationQuery query = new EnumerationQuery();
            EnumerationOrderEnum? order = ParseEnum<EnumerationOrderEnum>(req.Query.GetValueOrDefault("order"), "order");
            if (order.HasValue) query.Order = order.Value;
            query.ApplyQuerystringOverrides(key => req.Query.GetValueOrDefault(key));
            return query;
        }

        private T ReadBody<T>(ApiRequest req) where T : class, new()
        {
            string body = req.Http.Request.DataAsString ?? String.Empty;
            if (String.IsNullOrWhiteSpace(body)) return new T();
            try
            {
                return JsonSerializer.Deserialize<T>(body, _JsonOptions) ?? new T();
            }
            catch (JsonException ex)
            {
                throw new ArgumentException("Invalid request body: " + ex.Message, ex);
            }
        }

        private T ReadPagedBody<T>(ApiRequest req) where T : EnumerationQuery, new()
        {
            T body = ReadBody<T>(req);
            body.ApplyQuerystringOverrides(key => req.Query.GetValueOrDefault(key));
            return body;
        }

        private static string Param(ApiRequest req, string name)
        {
            return RouteParameters.Get(req, name);
        }

        private static BlackBoxAudienceEnum ReadAudience(ApiRequest req)
        {
            string? text = Blank(req.Query.GetValueOrDefault("audience"));
            if (text == null) return BlackBoxAudienceEnum.Agent;
            if (!EnumNames.TryParse(text, true, out BlackBoxAudienceEnum audience) || audience == BlackBoxAudienceEnum.Unknown)
                throw new ArgumentException("audience must be agent, user or maintainer.", "audience");
            return audience;
        }

        private static BlackBoxSectionEnum ReadSection(string text)
        {
            if (!EnumNames.TryParse(text, true, out BlackBoxSectionEnum section) || section == BlackBoxSectionEnum.Unknown)
                throw new ArgumentException("section must be one of Subject, Status, Site, Mechanism, Classification, Workspace, Conflict, Scope, Configuration, Attempts, Evidence, Analysis, Related, Voyage, Redaction or Truncation.", "section");
            return section;
        }

        private static bool? ReadBool(ApiRequest req, string name)
        {
            return ParseBool(req.Query.GetValueOrDefault(name), name);
        }

        private static long? ReadLong(ApiRequest req, string name)
        {
            string? text = Blank(req.Query.GetValueOrDefault(name));
            if (text == null) return null;
            if (!Int64.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)) throw new ArgumentException(name + " must be an integer.", name);
            return parsed;
        }

        private static bool? ParseBool(string? value, string name)
        {
            string? text = Blank(value);
            if (text == null) return null;
            if (!Boolean.TryParse(text, out bool parsed)) throw new ArgumentException(name + " must be true or false.", name);
            return parsed;
        }

        private static TEnum? ParseEnum<TEnum>(string? value, string name) where TEnum : struct, Enum
        {
            string? text = Blank(value);
            if (text == null) return null;
            if (!EnumNames.TryParse(text, true, out TEnum parsed)) throw new ArgumentException("Invalid " + name + ": " + text, name);
            return parsed;
        }

        private static DateTime? ParseUtc(string? value, string name)
        {
            string? text = Blank(value);
            if (text == null) return null;
            if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime parsed))
                throw new ArgumentException("Invalid " + name + ": " + text, name);
            return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
        }

        private static string? Blank(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static async Task<object?> SendTextAsync(ApiRequest req, string text, string contentType)
        {
            byte[] data = Encoding.UTF8.GetBytes(text ?? String.Empty);
            return await SendBytesAsync(req, data, contentType, Sha256Hex(data), null).ConfigureAwait(false);
        }

        private static async Task<object?> SendBytesAsync(ApiRequest req, byte[] data, string contentType, string etag, string? fileName)
        {
            string quoted = "\"" + etag + "\"";
            req.Http.Response.Headers.Add("ETag", quoted);
            string? ifNoneMatch = req.Http.Request.Headers.Get("If-None-Match");
            if (!String.IsNullOrEmpty(ifNoneMatch) && (String.Equals(ifNoneMatch.Trim(), quoted, StringComparison.Ordinal) || String.Equals(ifNoneMatch.Trim(), etag, StringComparison.Ordinal)))
            {
                req.Http.Response.StatusCode = 304;
                await req.Http.Response.Send().ConfigureAwait(false);
                return null;
            }

            req.Http.Response.ContentType = contentType;
            if (fileName != null) req.Http.Response.Headers.Add("Content-Disposition", "attachment; filename=\"" + fileName + "\"");
            await req.Http.Response.Send(data).ConfigureAwait(false);
            return null;
        }

        private static string Sha256Hex(byte[] data)
        {
            byte[] hash = SHA256.HashData(data ?? Array.Empty<byte>());
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static string SafeFileName(string id)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in id ?? String.Empty)
            {
                if (Char.IsLetterOrDigit(c) || c == '_' || c == '-') sb.Append(c);
            }

            return sb.Length == 0 ? "blackbox" : sb.ToString();
        }

        #endregion
    }
}

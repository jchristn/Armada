namespace Armada.Client
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client.Http;
    using Armada.Client.Models;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Mission and voyage black box calls (<c>/api/v1/blackbox</c>, <c>/api/v1/missions/{id}/blackbox</c>,
    /// <c>/api/v1/voyages/{id}/blackbox</c>) (dashboard <c>api/client.ts</c> parity). Errors carry the black box code in
    /// <see cref="ArmadaApiException"/>: 404 BlackBoxNotFound, 409 BlackBoxDeleting, BlackBoxObjectCorrupt or
    /// BlackBoxConfirmationRequired, 410 BlackBoxObjectMissing, 503 BlackBoxStoreUnavailable or
    /// BlackBoxActionsUnavailable, 400 InvalidArgument or CollectionLimitExceeded.
    /// </summary>
    public sealed class BlackBoxClient
    {
        #region Private-Members

        private readonly ArmadaTransport _Transport;

        #endregion

        #region Constructors-and-Factories

        internal BlackBoxClient(ArmadaTransport transport)
        {
            _Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Dashboard <c>getMissionBlackBox</c>: GET /api/v1/missions/{id}/blackbox. The mission's current entry with its
        /// record; 404 BlackBoxNotFound when it has none.
        /// </summary>
        /// <param name="missionId">Mission id (msn_ prefix).</param>
        /// <param name="includeRecord">Include the record (default true).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The entry.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getMissionBlackBox")]
        public Task<BlackBoxEntry?> GetForMissionAsync(string missionId, bool includeRecord = true, CancellationToken token = default)
        {
            return _Transport.GetAsync<BlackBoxEntry>($"/api/v1/missions/{ArmadaQueryString.Escape(missionId)}/blackbox{IncludeRecord(includeRecord)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>getMissionBlackBoxBrief</c>: GET /api/v1/missions/{id}/blackbox/brief. The Markdown FIX THIS brief of
        /// the mission's current entry.
        /// </summary>
        /// <param name="missionId">Mission id (msn_ prefix).</param>
        /// <param name="redactHosts">Strip absolute paths, host names and user names.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The Markdown brief.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getMissionBlackBoxBrief")]
        public Task<string?> GetMissionBriefAsync(string missionId, bool redactHosts = false, CancellationToken token = default)
        {
            return _Transport.GetAsync<string>($"/api/v1/missions/{ArmadaQueryString.Escape(missionId)}/blackbox/brief{BriefQuery(redactHosts)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>listMissionBlackBoxEntries</c>: GET /api/v1/missions/{id}/blackbox/entries. One page of the mission's
        /// entries, newest first (GET twin of <see cref="EnumerateMissionEntriesAsync"/>).
        /// </summary>
        /// <param name="missionId">Mission id (msn_ prefix).</param>
        /// <param name="pageNumber">Page number, or null for 1.</param>
        /// <param name="pageSize">Page size (1 to 1000), or null for 10.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("listMissionBlackBoxEntries")]
        public Task<EnumerationResult<BlackBoxEntrySummary>?> ListMissionEntriesAsync(string missionId, int? pageNumber = null, int? pageSize = null, CancellationToken token = default)
        {
            return _Transport.GetAsync<EnumerationResult<BlackBoxEntrySummary>>($"/api/v1/missions/{ArmadaQueryString.Escape(missionId)}/blackbox/entries{PageQuery(pageNumber, pageSize)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>enumerateMissionBlackBoxEntries</c>: POST /api/v1/missions/{id}/blackbox/entries/enumerate. One page of
        /// the mission's entries (one per failed attempt), newest first.
        /// </summary>
        /// <param name="missionId">Mission id (msn_ prefix).</param>
        /// <param name="query">Paging, or null for the first page of 10.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("enumerateMissionBlackBoxEntries")]
        public Task<EnumerationResult<BlackBoxEntrySummary>?> EnumerateMissionEntriesAsync(string missionId, EnumerationQuery? query = null, CancellationToken token = default)
        {
            return _Transport.PostAsync<EnumerationResult<BlackBoxEntrySummary>>($"/api/v1/missions/{ArmadaQueryString.Escape(missionId)}/blackbox/entries/enumerate", query ?? new EnumerationQuery(), null, token);
        }

        /// <summary>
        /// Dashboard <c>getVoyageBlackBox</c>: GET /api/v1/voyages/{id}/blackbox. The voyage's current entry with its record.
        /// </summary>
        /// <param name="voyageId">Voyage id (vyg_ prefix).</param>
        /// <param name="includeRecord">Include the record (default true).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The entry.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getVoyageBlackBox")]
        public Task<BlackBoxEntry?> GetForVoyageAsync(string voyageId, bool includeRecord = true, CancellationToken token = default)
        {
            return _Transport.GetAsync<BlackBoxEntry>($"/api/v1/voyages/{ArmadaQueryString.Escape(voyageId)}/blackbox{IncludeRecord(includeRecord)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>getVoyageBlackBoxBrief</c>: GET /api/v1/voyages/{id}/blackbox/brief. The Markdown brief of the voyage's
        /// current entry.
        /// </summary>
        /// <param name="voyageId">Voyage id (vyg_ prefix).</param>
        /// <param name="redactHosts">Strip absolute paths, host names and user names.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The Markdown brief.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getVoyageBlackBoxBrief")]
        public Task<string?> GetVoyageBriefAsync(string voyageId, bool redactHosts = false, CancellationToken token = default)
        {
            return _Transport.GetAsync<string>($"/api/v1/voyages/{ArmadaQueryString.Escape(voyageId)}/blackbox/brief{BriefQuery(redactHosts)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>getBlackBox</c>: GET /api/v1/blackbox/{id}. One entry with its record.
        /// </summary>
        /// <param name="entryId">Entry id (bbx_ prefix).</param>
        /// <param name="includeRecord">Include the record (default true).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The entry.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getBlackBox")]
        public Task<BlackBoxEntry?> GetAsync(string entryId, bool includeRecord = true, CancellationToken token = default)
        {
            return _Transport.GetAsync<BlackBoxEntry>($"/api/v1/blackbox/{ArmadaQueryString.Escape(entryId)}{IncludeRecord(includeRecord)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>getBlackBoxBrief</c>: GET /api/v1/blackbox/{id}/brief. The Markdown FIX THIS brief.
        /// </summary>
        /// <param name="entryId">Entry id (bbx_ prefix).</param>
        /// <param name="redactHosts">Strip absolute paths, host names and user names.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The Markdown brief.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getBlackBoxBrief")]
        public Task<string?> GetBriefAsync(string entryId, bool redactHosts = false, CancellationToken token = default)
        {
            return _Transport.GetAsync<string>($"/api/v1/blackbox/{ArmadaQueryString.Escape(entryId)}/brief{BriefQuery(redactHosts)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>getBlackBoxSection</c>: GET /api/v1/blackbox/{id}/sections/{section}. One record section.
        /// </summary>
        /// <param name="entryId">Entry id (bbx_ prefix).</param>
        /// <param name="section">Section.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The section.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getBlackBoxSection")]
        public Task<BlackBoxSectionResult?> GetSectionAsync(string entryId, BlackBoxSectionEnum section, CancellationToken token = default)
        {
            return _Transport.GetAsync<BlackBoxSectionResult>($"/api/v1/blackbox/{ArmadaQueryString.Escape(entryId)}/sections/{ArmadaQueryString.Escape(section.ToString())}", null, token);
        }

        /// <summary>
        /// Dashboard <c>listBlackBoxObjects</c>: GET /api/v1/blackbox/{id}/objects. One page of the entry's objects (GET twin
        /// of <see cref="EnumerateObjectsAsync"/>).
        /// </summary>
        /// <param name="entryId">Entry id (bbx_ prefix).</param>
        /// <param name="pageNumber">Page number, or null for 1.</param>
        /// <param name="pageSize">Page size (1 to 1000), or null for 10.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("listBlackBoxObjects")]
        public Task<EnumerationResult<BlackBoxObjectSummary>?> ListObjectsAsync(string entryId, int? pageNumber = null, int? pageSize = null, CancellationToken token = default)
        {
            return _Transport.GetAsync<EnumerationResult<BlackBoxObjectSummary>>($"/api/v1/blackbox/{ArmadaQueryString.Escape(entryId)}/objects{PageQuery(pageNumber, pageSize)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>enumerateBlackBoxObjects</c>: POST /api/v1/blackbox/{id}/objects/enumerate. One page of the entry's
        /// objects.
        /// </summary>
        /// <param name="entryId">Entry id (bbx_ prefix).</param>
        /// <param name="query">Paging, or null for the first page of 10.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("enumerateBlackBoxObjects")]
        public Task<EnumerationResult<BlackBoxObjectSummary>?> EnumerateObjectsAsync(string entryId, EnumerationQuery? query = null, CancellationToken token = default)
        {
            return _Transport.PostAsync<EnumerationResult<BlackBoxObjectSummary>>($"/api/v1/blackbox/{ArmadaQueryString.Escape(entryId)}/objects/enumerate", query ?? new EnumerationQuery(), null, token);
        }

        /// <summary>
        /// Dashboard <c>readBlackBoxObject</c>: GET /api/v1/blackbox/{id}/objects/{name}. One range of an object (at most
        /// 1 MB); follow <see cref="BlackBoxObjectContent.NextOffset"/> to read the rest. The name is sent as one encoded
        /// path segment.
        /// </summary>
        /// <param name="entryId">Entry id (bbx_ prefix).</param>
        /// <param name="name">Object name (for example evidence/git-status.txt).</param>
        /// <param name="offset">Byte offset, or null for 0.</param>
        /// <param name="length">Bytes to read (1 to 1048576), or null for 1 MB.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The bytes and paging.</returns>
        /// <exception cref="ArgumentNullException">Thrown when name is null or empty.</exception>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("readBlackBoxObject")]
        public async Task<BlackBoxObjectContent> ReadObjectAsync(string entryId, string name, long? offset = null, long? length = null, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            List<string> parts = new List<string>();
            if (offset.HasValue) parts.Add("offset=" + offset.Value.ToString(CultureInfo.InvariantCulture));
            if (length.HasValue) parts.Add("length=" + length.Value.ToString(CultureInfo.InvariantCulture));
            string path = $"/api/v1/blackbox/{ArmadaQueryString.Escape(entryId)}/objects/{ArmadaQueryString.Escape(name)}" + (parts.Count == 0 ? String.Empty : "?" + String.Join("&", parts));
            using (HttpResponseMessage response = await _Transport.SendRawAsync(HttpMethod.Get, path, null, null, null, token).ConfigureAwait(false))
            {
                await ThrowIfFailedAsync(response, "GET", path).ConfigureAwait(false);
                BlackBoxObjectContent content = new BlackBoxObjectContent();
                content.Name = name;
                content.ContentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
                content.ETag = response.Headers.ETag?.Tag?.Trim('"');
                content.StoredSha256 = response.Headers.TryGetValues("X-BlackBox-Stored-Sha256", out IEnumerable<string>? stored) ? stored.FirstOrDefault() : null;
                content.Content = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                content.Offset = HeaderLong(response, "X-BlackBox-Offset") ?? (offset ?? 0);
                content.TotalBytes = HeaderLong(response, "X-BlackBox-Total-Bytes") ?? content.Content.LongLength;
                content.NextOffset = HeaderLong(response, "X-BlackBox-Next-Offset");
                return content;
            }
        }

        /// <summary>
        /// Dashboard <c>downloadBlackBox</c>: GET /api/v1/blackbox/{id}/download. The zip of every object plus manifest.json.
        /// </summary>
        /// <param name="entryId">Entry id (bbx_ prefix).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The zip and its file name.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("downloadBlackBox")]
        public async Task<BlackBoxDownload> DownloadAsync(string entryId, CancellationToken token = default)
        {
            string path = $"/api/v1/blackbox/{ArmadaQueryString.Escape(entryId)}/download";
            using (HttpResponseMessage response = await _Transport.SendRawAsync(HttpMethod.Get, path, null, null, 600000, token).ConfigureAwait(false))
            {
                await ThrowIfFailedAsync(response, "GET", path).ConfigureAwait(false);
                BlackBoxDownload download = new BlackBoxDownload();
                download.Content = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                string? fileName = response.Content.Headers.ContentDisposition?.FileName?.Trim('"');
                download.FileName = !String.IsNullOrEmpty(fileName) ? fileName! : entryId + ".zip";
                return download;
            }
        }

        /// <summary>
        /// Dashboard <c>listBlackBox</c>: GET /api/v1/blackbox. One page of the entries visible to the caller, with the
        /// filters as query parameters (GET twin of <see cref="EnumerateAsync"/>).
        /// </summary>
        /// <param name="query">Filters and paging, or null for the first page of everything visible.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("listBlackBox")]
        public Task<EnumerationResult<BlackBoxEntrySummary>?> ListAsync(BlackBoxEnumerateRequest? query = null, CancellationToken token = default)
        {
            return _Transport.GetAsync<EnumerationResult<BlackBoxEntrySummary>>($"/api/v1/blackbox{Armada.Client.Http.ArmadaQueryString.FromObject(query)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>enumerateBlackBox</c>: POST /api/v1/blackbox/enumerate. One page of the entries visible to the caller
        /// with the filters in the request, newest first.
        /// </summary>
        /// <param name="query">Filters and paging, or null for the first page of everything visible.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("enumerateBlackBox")]
        public Task<EnumerationResult<BlackBoxEntrySummary>?> EnumerateAsync(BlackBoxEnumerateRequest? query = null, CancellationToken token = default)
        {
            return _Transport.PostAsync<EnumerationResult<BlackBoxEntrySummary>>("/api/v1/blackbox/enumerate", query ?? new BlackBoxEnumerateRequest(), null, token);
        }

        /// <summary>
        /// Dashboard <c>searchBlackBox</c>: POST /api/v1/blackbox/search. Full-text search over reason, summary and notes.
        /// </summary>
        /// <param name="request">Query (1 to 200 characters), filters and paging.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page of matches.</returns>
        /// <exception cref="ArgumentNullException">Thrown when request is null.</exception>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("searchBlackBox")]
        public Task<EnumerationResult<BlackBoxEntrySummary>?> SearchAsync(BlackBoxSearchRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return _Transport.PostAsync<EnumerationResult<BlackBoxEntrySummary>>("/api/v1/blackbox/search", request, null, token);
        }

        /// <summary>
        /// Dashboard <c>addBlackBoxNote</c>: POST /api/v1/blackbox/{id}/notes. Adds a note (1 to 8000 characters).
        /// </summary>
        /// <param name="entryId">Entry id (bbx_ prefix).</param>
        /// <param name="request">Note.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The note.</returns>
        /// <exception cref="ArgumentNullException">Thrown when request is null.</exception>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("addBlackBoxNote")]
        public Task<BlackBoxNote?> AddNoteAsync(string entryId, BlackBoxNoteAddRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return _Transport.PostAsync<BlackBoxNote>($"/api/v1/blackbox/{ArmadaQueryString.Escape(entryId)}/notes", request, null, token);
        }

        /// <summary>
        /// Dashboard <c>listBlackBoxNotes</c>: GET /api/v1/blackbox/{id}/notes. One page of the entry's notes, newest first
        /// (GET twin of <see cref="EnumerateNotesAsync"/>).
        /// </summary>
        /// <param name="entryId">Entry id (bbx_ prefix).</param>
        /// <param name="pageNumber">Page number, or null for 1.</param>
        /// <param name="pageSize">Page size (1 to 1000), or null for 10.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("listBlackBoxNotes")]
        public Task<EnumerationResult<BlackBoxNote>?> ListNotesAsync(string entryId, int? pageNumber = null, int? pageSize = null, CancellationToken token = default)
        {
            return _Transport.GetAsync<EnumerationResult<BlackBoxNote>>($"/api/v1/blackbox/{ArmadaQueryString.Escape(entryId)}/notes{PageQuery(pageNumber, pageSize)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>enumerateBlackBoxNotes</c>: POST /api/v1/blackbox/{id}/notes/enumerate. One page of the entry's notes,
        /// newest first.
        /// </summary>
        /// <param name="entryId">Entry id (bbx_ prefix).</param>
        /// <param name="query">Paging, or null for the first page of 10.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("enumerateBlackBoxNotes")]
        public Task<EnumerationResult<BlackBoxNote>?> EnumerateNotesAsync(string entryId, EnumerationQuery? query = null, CancellationToken token = default)
        {
            return _Transport.PostAsync<EnumerationResult<BlackBoxNote>>($"/api/v1/blackbox/{ArmadaQueryString.Escape(entryId)}/notes/enumerate", query ?? new EnumerationQuery(), null, token);
        }

        /// <summary>
        /// Dashboard <c>resolveBlackBox</c>: POST /api/v1/blackbox/{id}/resolve. Sets the resolution, note and link.
        /// </summary>
        /// <param name="entryId">Entry id (bbx_ prefix).</param>
        /// <param name="request">Resolution, note and link.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated entry.</returns>
        /// <exception cref="ArgumentNullException">Thrown when request is null.</exception>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("resolveBlackBox")]
        public Task<BlackBoxEntry?> ResolveAsync(string entryId, BlackBoxResolveRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return _Transport.PostAsync<BlackBoxEntry>($"/api/v1/blackbox/{ArmadaQueryString.Escape(entryId)}/resolve", request, null, token);
        }

        /// <summary>
        /// Dashboard <c>runBlackBoxRecovery</c>: POST /api/v1/blackbox/{id}/recovery/{pathId}. Runs a recorded recovery path:
        /// a dry run (the default) returns the plan and a confirm token; the real run needs the token (and
        /// AcknowledgeDataLoss for a dataLossRisk path). 409 BlackBoxConfirmationRequired otherwise.
        /// </summary>
        /// <param name="entryId">Entry id (bbx_ prefix).</param>
        /// <param name="pathId">Recovery path id.</param>
        /// <param name="request">Dry run, confirm token and data-loss acknowledgement, or null for a dry run.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The plan or the step results.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("runBlackBoxRecovery")]
        public Task<BlackBoxRecoveryRunResult?> RunRecoveryAsync(string entryId, string pathId, BlackBoxRecoveryRunRequest? request = null, CancellationToken token = default)
        {
            return _Transport.PostAsync<BlackBoxRecoveryRunResult>($"/api/v1/blackbox/{ArmadaQueryString.Escape(entryId)}/recovery/{ArmadaQueryString.Escape(pathId)}", request ?? new BlackBoxRecoveryRunRequest(), null, token);
        }

        /// <summary>
        /// Dashboard <c>applyBlackBoxRecommendation</c>: POST /api/v1/blackbox/{id}/recommendations/{recommendationId}/apply.
        /// Applies a recorded configuration recommendation with the same dry-run and confirm-token flow as recovery.
        /// </summary>
        /// <param name="entryId">Entry id (bbx_ prefix).</param>
        /// <param name="recommendationId">Recommendation id.</param>
        /// <param name="request">Dry run and confirm token, or null for a dry run.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The action or the result.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("applyBlackBoxRecommendation")]
        public Task<BlackBoxRecommendationApplyResult?> ApplyRecommendationAsync(string entryId, string recommendationId, BlackBoxRecommendationApplyRequest? request = null, CancellationToken token = default)
        {
            return _Transport.PostAsync<BlackBoxRecommendationApplyResult>($"/api/v1/blackbox/{ArmadaQueryString.Escape(entryId)}/recommendations/{ArmadaQueryString.Escape(recommendationId)}/apply", request ?? new BlackBoxRecommendationApplyRequest(), null, token);
        }

        /// <summary>
        /// Dashboard <c>reanalyzeBlackBox</c>: POST /api/v1/blackbox/{id}/reanalyze. Re-runs enrichment and analysis (tenant
        /// admin).
        /// </summary>
        /// <param name="entryId">Entry id (bbx_ prefix).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The entry.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("reanalyzeBlackBox")]
        public Task<BlackBoxEntry?> ReanalyzeAsync(string entryId, CancellationToken token = default)
        {
            return _Transport.PostAsync<BlackBoxEntry>($"/api/v1/blackbox/{ArmadaQueryString.Escape(entryId)}/reanalyze", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>getBlackBoxBundle</c>: POST /api/v1/blackbox/bundle. A maintainer bundle of up to 50 entries.
        /// </summary>
        /// <param name="request">Filters, max, format and redactHosts, or null for the defaults.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The bundle.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getBlackBoxBundle")]
        public Task<BlackBoxBundleResult?> GetBundleAsync(BlackBoxBundleRequest? request = null, CancellationToken token = default)
        {
            return _Transport.PostAsync<BlackBoxBundleResult>("/api/v1/blackbox/bundle", request ?? new BlackBoxBundleRequest(), null, token);
        }

        /// <summary>
        /// Dashboard <c>enumerateBlackBoxHotspots</c>: POST /api/v1/blackbox/hotspots/enumerate. One page of recurring failure
        /// directories (role Conflicted unless set).
        /// </summary>
        /// <param name="request">Filters and paging, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("enumerateBlackBoxHotspots")]
        public Task<EnumerationResult<BlackBoxHotspot>?> EnumerateHotspotsAsync(BlackBoxHotspotEnumerateRequest? request = null, CancellationToken token = default)
        {
            return _Transport.PostAsync<EnumerationResult<BlackBoxHotspot>>("/api/v1/blackbox/hotspots/enumerate", request ?? new BlackBoxHotspotEnumerateRequest(), null, token);
        }

        /// <summary>
        /// Dashboard <c>enumerateBlackBoxFingerprints</c>: POST /api/v1/blackbox/fingerprints/enumerate. One page of failure
        /// modes grouped by fingerprint.
        /// </summary>
        /// <param name="request">Filters and paging, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("enumerateBlackBoxFingerprints")]
        public Task<EnumerationResult<BlackBoxFingerprintSummary>?> EnumerateFingerprintsAsync(BlackBoxFingerprintEnumerateRequest? request = null, CancellationToken token = default)
        {
            return _Transport.PostAsync<EnumerationResult<BlackBoxFingerprintSummary>>("/api/v1/blackbox/fingerprints/enumerate", request ?? new BlackBoxFingerprintEnumerateRequest(), null, token);
        }

        /// <summary>
        /// Dashboard <c>enumerateBlackBoxResilienceGaps</c>: POST /api/v1/blackbox/resilience-gaps/enumerate. One page (at
        /// most 50) of Armada resilience gaps: handoff and landing failure modes triaged ArmadaDefect, grouped by fingerprint.
        /// </summary>
        /// <param name="request">Filters and paging, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("enumerateBlackBoxResilienceGaps")]
        public Task<EnumerationResult<BlackBoxResilienceGap>?> EnumerateResilienceGapsAsync(BlackBoxResilienceGapRequest? request = null, CancellationToken token = default)
        {
            return _Transport.PostAsync<EnumerationResult<BlackBoxResilienceGap>>("/api/v1/blackbox/resilience-gaps/enumerate", request ?? new BlackBoxResilienceGapRequest(), null, token);
        }

        /// <summary>
        /// Dashboard <c>getBlackBoxFingerprintTrend</c>: GET /api/v1/blackbox/fingerprints/{fingerprint}/trend. Occurrences of
        /// one failure mode per Armada version.
        /// </summary>
        /// <param name="fingerprint">Fingerprint.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The trend.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getBlackBoxFingerprintTrend")]
        public Task<BlackBoxFingerprintTrend?> GetFingerprintTrendAsync(string fingerprint, CancellationToken token = default)
        {
            return _Transport.GetAsync<BlackBoxFingerprintTrend>($"/api/v1/blackbox/fingerprints/{ArmadaQueryString.Escape(fingerprint)}/trend", null, token);
        }

        /// <summary>
        /// Dashboard <c>exportBlackBoxFixture</c>: POST /api/v1/blackbox/{id}/export-fixture. The entry as a sanitized
        /// regression fixture (global admin).
        /// </summary>
        /// <param name="entryId">Entry id (bbx_ prefix).</param>
        /// <param name="name">Fixture name, or null for the mission id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The fixture.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("exportBlackBoxFixture")]
        public Task<BlackBoxFixture?> ExportFixtureAsync(string entryId, string? name = null, CancellationToken token = default)
        {
            return _Transport.PostAsync<BlackBoxFixture>($"/api/v1/blackbox/{ArmadaQueryString.Escape(entryId)}/export-fixture", new BlackBoxFixtureExportRequest { Name = name }, null, token);
        }

        /// <summary>
        /// Dashboard <c>pinBlackBox</c>: POST /api/v1/blackbox/{id}/pin. Garbage collection keeps a pinned entry.
        /// </summary>
        /// <param name="entryId">Entry id (bbx_ prefix).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The entry.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("pinBlackBox")]
        public Task<BlackBoxEntry?> PinAsync(string entryId, CancellationToken token = default)
        {
            return _Transport.PostAsync<BlackBoxEntry>($"/api/v1/blackbox/{ArmadaQueryString.Escape(entryId)}/pin", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>unpinBlackBox</c>: POST /api/v1/blackbox/{id}/unpin. Recomputes the expiry from the policy.
        /// </summary>
        /// <param name="entryId">Entry id (bbx_ prefix).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The entry.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("unpinBlackBox")]
        public Task<BlackBoxEntry?> UnpinAsync(string entryId, CancellationToken token = default)
        {
            return _Transport.PostAsync<BlackBoxEntry>($"/api/v1/blackbox/{ArmadaQueryString.Escape(entryId)}/unpin", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteBlackBox</c>: DELETE /api/v1/blackbox/{id}. Deletes one entry (tenant admin).
        /// </summary>
        /// <param name="entryId">Entry id (bbx_ prefix).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Counts.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("deleteBlackBox")]
        public Task<BlackBoxDeleteResult?> DeleteAsync(string entryId, CancellationToken token = default)
        {
            return _Transport.DeleteAsync<BlackBoxDeleteResult>($"/api/v1/blackbox/{ArmadaQueryString.Escape(entryId)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteBlackBoxEntries</c>: POST /api/v1/blackbox/delete. Deletes up to 1000 entries (tenant admin); 400
        /// CollectionLimitExceeded for more.
        /// </summary>
        /// <param name="ids">Entry ids.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Counts and skipped ids.</returns>
        /// <exception cref="ArgumentNullException">Thrown when ids is null.</exception>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("deleteBlackBoxEntries")]
        public Task<BlackBoxDeleteResult?> DeleteEntriesAsync(List<string> ids, CancellationToken token = default)
        {
            if (ids == null) throw new ArgumentNullException(nameof(ids));
            Dictionary<string, object?> body = new Dictionary<string, object?> { ["Ids"] = ids.ToList() };
            return _Transport.PostAsync<BlackBoxDeleteResult>("/api/v1/blackbox/delete", body, null, token);
        }

        /// <summary>
        /// Dashboard <c>purgeBlackBox</c>: POST /api/v1/blackbox/purge. Deletes entries by filter (global admin); a dry run
        /// unless DryRun is false.
        /// </summary>
        /// <param name="request">Filters, dry run, include pinned and maximum.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>What matched and what was deleted.</returns>
        /// <exception cref="ArgumentNullException">Thrown when request is null.</exception>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("purgeBlackBox")]
        public Task<BlackBoxPurgeResult?> PurgeAsync(BlackBoxPurgeRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return _Transport.PostAsync<BlackBoxPurgeResult>("/api/v1/blackbox/purge", request, null, token);
        }

        /// <summary>
        /// Dashboard <c>runBlackBoxGc</c>: POST /api/v1/blackbox/gc. Runs garbage collection now (global admin).
        /// </summary>
        /// <param name="dryRun">List the candidates without deleting.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The report.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("runBlackBoxGc")]
        public Task<BlackBoxGcReport?> RunGcAsync(bool dryRun = false, CancellationToken token = default)
        {
            return _Transport.PostAsync<BlackBoxGcReport>("/api/v1/blackbox/gc", new BlackBoxGcRunRequest { DryRun = dryRun }, null, token);
        }

        /// <summary>
        /// Dashboard <c>getBlackBoxRetention</c>: GET /api/v1/blackbox/retention. Policy, counts, bytes and garbage collection
        /// runs (tenant admin).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Retention status.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getBlackBoxRetention")]
        public Task<BlackBoxRetentionStatus?> GetRetentionAsync(CancellationToken token = default)
        {
            return _Transport.GetAsync<BlackBoxRetentionStatus>("/api/v1/blackbox/retention", null, token);
        }

        /// <summary>
        /// Dashboard <c>getBlackBoxStorage</c>: GET /api/v1/blackbox/storage. Target, health and bytes (global admin).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Storage status.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getBlackBoxStorage")]
        public Task<BlackBoxStorageStatus?> GetStorageAsync(CancellationToken token = default)
        {
            return _Transport.GetAsync<BlackBoxStorageStatus>("/api/v1/blackbox/storage", null, token);
        }

        /// <summary>
        /// Dashboard <c>migrateBlackBoxStorage</c>: POST /api/v1/blackbox/storage/migrate. Starts a storage migration job (global
        /// admin).
        /// </summary>
        /// <param name="request">Target, source, deleteSource and batch size.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The job.</returns>
        /// <exception cref="ArgumentNullException">Thrown when request is null.</exception>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("migrateBlackBoxStorage")]
        public Task<BlackBoxStorageMigrationJob?> MigrateStorageAsync(BlackBoxStorageMigrationRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return _Transport.PostAsync<BlackBoxStorageMigrationJob>("/api/v1/blackbox/storage/migrate", request, null, token);
        }

        /// <summary>
        /// Dashboard <c>getBlackBoxStorageMigration</c>: GET /api/v1/blackbox/storage/migrations/{id}. One migration job (global
        /// admin).
        /// </summary>
        /// <param name="migrationId">Job id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The job.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getBlackBoxStorageMigration")]
        public Task<BlackBoxStorageMigrationJob?> GetStorageMigrationAsync(string migrationId, CancellationToken token = default)
        {
            return _Transport.GetAsync<BlackBoxStorageMigrationJob>($"/api/v1/blackbox/storage/migrations/{ArmadaQueryString.Escape(migrationId)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>backfillBlackBox</c>: POST /api/v1/blackbox/backfill. Captures entries for failures that have none
        /// (global admin).
        /// </summary>
        /// <param name="request">Since and maximum subjects, or null for the defaults.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Counts.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("backfillBlackBox")]
        public Task<BlackBoxBackfillResult?> BackfillAsync(BlackBoxBackfillRequest? request = null, CancellationToken token = default)
        {
            return _Transport.PostAsync<BlackBoxBackfillResult>("/api/v1/blackbox/backfill", request ?? new BlackBoxBackfillRequest(), null, token);
        }

        #endregion

        #region Private-Methods

        private static string IncludeRecord(bool includeRecord)
        {
            return includeRecord ? String.Empty : "?includeRecord=false";
        }

        private static string BriefQuery(bool redactHosts)
        {
            return redactHosts ? "?redactHosts=true" : String.Empty;
        }

        private static string PageQuery(int? pageNumber, int? pageSize)
        {
            List<string> parts = new List<string>();
            if (pageNumber.HasValue) parts.Add("pageNumber=" + pageNumber.Value.ToString(CultureInfo.InvariantCulture));
            if (pageSize.HasValue) parts.Add("pageSize=" + pageSize.Value.ToString(CultureInfo.InvariantCulture));
            return parts.Count == 0 ? String.Empty : "?" + String.Join("&", parts);
        }

        private static long? HeaderLong(HttpResponseMessage response, string name)
        {
            if (!response.Headers.TryGetValues(name, out IEnumerable<string>? values)) return null;
            foreach (string value in values)
            {
                if (Int64.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)) return parsed;
            }

            return null;
        }

        private async Task ThrowIfFailedAsync(HttpResponseMessage response, string method, string path)
        {
            if (response.IsSuccessStatusCode) return;
            string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            ArmadaApiException ex = ArmadaTransport.BuildException(method, path, "", response, text);
            if (ex.IsUnauthorized) _Transport.RaiseUnauthorized(ex);
            throw ex;
        }

        #endregion
    }
}

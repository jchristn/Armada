namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.IO.Compression;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Net.WebSockets;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.BlackBox;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using Armada.Runtimes.Mcp;
    using McpToolCallResult = Armada.Runtimes.Mcp.McpToolCallResult;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// End-to-end black box surfaces through the real server (MISSION_BLACKBOX_PLAN.md sections 16.6 and 16.7): every
    /// REST route, MCP tool, MCP resource and prompt, and WebSocket command, positive and negative. Entries are captured
    /// through the server's own runtime for a tenant user's failed mission and voyage; a 3 MB evidence object is paged by
    /// REST (1 MB reads) and MCP (64 KB chunks). Negative cases: unknown mission, mission without an entry, unknown entry
    /// and another tenant's entry are 404 BlackBoxNotFound (no existence leak); invalid section, audience, offset and
    /// length are 400 with the field; page size 0 and 1001 are normalized; 1001 ids are 400 CollectionLimitExceeded; a
    /// regular user calling the admin routes is 403; an entry in the Deleting state is 409 BlackBoxDeleting; recovery and
    /// recommendation runs answer 501 NotImplemented until the recovery runner merges.
    /// </summary>
    public sealed class BlackBoxApiSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "E2E.BlackBox";
        private const int BigObjectBytes = 3 * 1024 * 1024 + 12345;
        private const string BigObjectName = "evidence/big.log";

        private E2EServerFixture? _Fx;
        private E2ETenantUser? _AdminA;
        private E2ETenantUser? _UserA;
        private E2ETenantUser? _AdminB;
        private Mission? _Mission;
        private Mission? _NoEntryMission;
        private Voyage? _Voyage;
        private string _EntryId = "";
        private string _VoyageEntryId = "";
        private readonly string _Marker = "bbapi-marker-" + Guid.NewGuid().ToString("N").Substring(0, 10);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("setup_capture_entries", "A tenant user's failed mission and voyage are captured by the server's runtime; a 3 MB evidence object is added", TestTags.Positive, SetupAsync));

            cases.Add(CaseAsync("rest_subject_reads", "Mission and voyage black box reads, LastBlackBoxEntryId on the mission and voyage reads, and the entries list", TestTags.Positive, async () =>
            {
                RequireSetup();
                using HttpClient ua = _UserA!.CreateClient(_Fx!.BaseUrl);
                BlackBoxEntry entry = await GetJsonAsync<BlackBoxEntry>(ua, "/api/v1/missions/" + _Mission!.Id + "/blackbox").ConfigureAwait(false);
                AssertEqual(_EntryId, entry.Id, "mission entry");
                AssertNotNull(entry.Record, "record included by default");
                AssertEqual(_Mission.Id, entry.Record!.Subject!.MissionId, "record subject");
                BlackBoxEntry bare = await GetJsonAsync<BlackBoxEntry>(ua, "/api/v1/missions/" + _Mission.Id + "/blackbox?includeRecord=false").ConfigureAwait(false);
                AssertNull(bare.Record, "includeRecord=false");

                Mission mission = await GetJsonAsync<Mission>(ua, "/api/v1/missions/" + _Mission.Id).ConfigureAwait(false);
                AssertEqual(_EntryId, mission.LastBlackBoxEntryId, "GET /missions/{id} carries LastBlackBoxEntryId");

                using HttpClient a = _AdminA!.CreateClient(_Fx.BaseUrl);
                BlackBoxEntry voyageEntry = await GetJsonAsync<BlackBoxEntry>(a, "/api/v1/voyages/" + _Voyage!.Id + "/blackbox").ConfigureAwait(false);
                AssertEqual(_VoyageEntryId, voyageEntry.Id, "voyage entry");
                VoyageProgress voyage = await GetJsonAsync<VoyageProgress>(a, "/api/v1/voyages/" + _Voyage.Id).ConfigureAwait(false);
                AssertEqual(_VoyageEntryId, voyage.Voyage.LastBlackBoxEntryId, "voyage read carries LastBlackBoxEntryId");
                string voyageBrief = await GetTextAsync(a, "/api/v1/voyages/" + _Voyage.Id + "/blackbox/brief").ConfigureAwait(false);
                AssertTrue(voyageBrief.Length > 0, "voyage brief");

                EnumerationResult<BlackBoxEntrySummary> entries = await GetJsonAsync<EnumerationResult<BlackBoxEntrySummary>>(ua, "/api/v1/missions/" + _Mission.Id + "/blackbox/entries").ConfigureAwait(false);
                AssertEqual(1L, entries.TotalRecords, "one attempt");
                AssertEqual(10, entries.PageSize, "default page size 10");
                EnumerationResult<BlackBoxEntrySummary> viaPost = await PostJsonAsync<EnumerationResult<BlackBoxEntrySummary>>(ua, "/api/v1/missions/" + _Mission.Id + "/blackbox/entries/enumerate", new { PageSize = 5 }).ConfigureAwait(false);
                AssertEqual(_EntryId, viaPost.Objects.Single().Id, "POST twin");
            }));

            cases.Add(CaseAsync("rest_briefs_by_audience", "Briefs are text/markdown with an ETag, differ by audience, honor If-None-Match, and reject an unknown audience", TestTags.Positive, async () =>
            {
                RequireSetup();
                using HttpClient ua = _UserA!.CreateClient(_Fx!.BaseUrl);
                Dictionary<string, string> briefs = new Dictionary<string, string>();
                string? etag = null;
                foreach (string audience in new[] { "agent", "user", "maintainer" })
                {
                    using HttpResponseMessage response = await ua.GetAsync("/api/v1/blackbox/" + _EntryId + "/brief?audience=" + audience).ConfigureAwait(false);
                    string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    AssertStatusCode(HttpStatusCode.OK, response, audience + " brief: " + body);
                    AssertEqual("text/markdown", response.Content.Headers.ContentType?.MediaType, audience + " content type");
                    AssertTrue(response.Headers.ETag != null, audience + " ETag");
                    if (audience == "agent") etag = response.Headers.ETag!.Tag;
                    AssertTrue(body.Length > 50, audience + " brief has content");
                    briefs[audience] = body;
                }

                AssertNotEqual(briefs["agent"], briefs["user"], "agent and user briefs differ");
                AssertNotEqual(briefs["agent"], briefs["maintainer"], "agent and maintainer briefs differ");
                AssertNotEqual(briefs["user"], briefs["maintainer"], "user and maintainer briefs differ");

                string missionBrief = await GetTextAsync(ua, "/api/v1/missions/" + _Mission!.Id + "/blackbox/brief").ConfigureAwait(false);
                AssertEqual(briefs["agent"], missionBrief, "mission brief is the entry's agent brief");

                using (HttpRequestMessage conditional = new HttpRequestMessage(HttpMethod.Get, "/api/v1/blackbox/" + _EntryId + "/brief"))
                {
                    conditional.Headers.TryAddWithoutValidation("If-None-Match", etag);
                    using HttpResponseMessage notModified = await ua.SendAsync(conditional).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.NotModified, notModified.StatusCode, "If-None-Match with the current ETag");
                }

                await ExpectErrorAsync(ua, HttpMethod.Get, "/api/v1/blackbox/" + _EntryId + "/brief?audience=robot", null, HttpStatusCode.BadRequest, "InvalidArgument", "audience").ConfigureAwait(false);
                await ExpectErrorAsync(ua, HttpMethod.Get, "/api/v1/missions/" + _Mission.Id + "/blackbox/brief?audience=4", null, HttpStatusCode.BadRequest, "InvalidArgument", "audience").ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("rest_sections_objects_download", "Sections, the object list, whole and ranged object reads with ETag, and the zip download with manifest.json", TestTags.Positive, async () =>
            {
                RequireSetup();
                using HttpClient ua = _UserA!.CreateClient(_Fx!.BaseUrl);
                BlackBoxSectionResult status = await GetJsonAsync<BlackBoxSectionResult>(ua, "/api/v1/blackbox/" + _EntryId + "/sections/status").ConfigureAwait(false);
                AssertEqual(BlackBoxSectionEnum.Status, status.Section, "section parsed case-insensitively");
                AssertNotNull(status.Record.Status, "status section present");
                AssertNull(status.Record.Subject, "other sections absent");
                await ExpectErrorAsync(ua, HttpMethod.Get, "/api/v1/blackbox/" + _EntryId + "/sections/Bogus", null, HttpStatusCode.BadRequest, "InvalidArgument", "section").ConfigureAwait(false);
                await ExpectErrorAsync(ua, HttpMethod.Get, "/api/v1/blackbox/" + _EntryId + "/sections/Unknown", null, HttpStatusCode.BadRequest, "InvalidArgument", "section").ConfigureAwait(false);

                EnumerationResult<BlackBoxObjectSummary> objects = await GetJsonAsync<EnumerationResult<BlackBoxObjectSummary>>(ua, "/api/v1/blackbox/" + _EntryId + "/objects?pageSize=100").ConfigureAwait(false);
                AssertTrue(objects.Objects.Any(o => o.Name == BlackBoxObjectNames.Record), "record.json listed");
                AssertTrue(objects.Objects.Any(o => o.Name == BigObjectName), "big object listed");
                EnumerationResult<BlackBoxObjectSummary> objectsPost = await PostJsonAsync<EnumerationResult<BlackBoxObjectSummary>>(ua, "/api/v1/blackbox/" + _EntryId + "/objects/enumerate", new { PageSize = 1 }).ConfigureAwait(false);
                AssertEqual(1, objectsPost.Objects.Count, "POST twin page of one");
                AssertEqual(objects.TotalRecords, objectsPost.TotalRecords, "same total");

                using (HttpResponseMessage record = await ua.GetAsync("/api/v1/blackbox/" + _EntryId + "/objects/record.json").ConfigureAwait(false))
                {
                    string body = await record.Content.ReadAsStringAsync().ConfigureAwait(false);
                    AssertStatusCode(HttpStatusCode.OK, record, body);
                    AssertEqual("application/json", record.Content.Headers.ContentType?.MediaType, "object content type");
                    AssertTrue(record.Headers.ETag != null, "object ETag");
                    AssertContains(_EntryId, body, "record.json names the entry");
                }

                // A 3 MB object read at most 1 MB at a time, following X-BlackBox-Next-Offset to the end.
                long offset = 0;
                int reads = 0;
                long total = 0;
                using (MemoryStream all = new MemoryStream())
                {
                    while (true)
                    {
                        using HttpResponseMessage chunk = await ua.GetAsync("/api/v1/blackbox/" + _EntryId + "/objects/" + Uri.EscapeDataString(BigObjectName) + "?offset=" + offset).ConfigureAwait(false);
                        byte[] data = await chunk.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                        AssertStatusCode(HttpStatusCode.OK, chunk, "chunk at " + offset);
                        AssertTrue(data.Length <= 1048576, "at most 1 MB per read: " + data.Length);
                        total = Int64.Parse(chunk.Headers.GetValues("X-BlackBox-Total-Bytes").Single());
                        AssertEqual(offset, Int64.Parse(chunk.Headers.GetValues("X-BlackBox-Offset").Single()), "offset header");
                        all.Write(data, 0, data.Length);
                        reads++;
                        if (!chunk.Headers.TryGetValues("X-BlackBox-Next-Offset", out IEnumerable<string>? next)) break;
                        offset = Int64.Parse(next.Single());
                        AssertTrue(reads < 10, "bounded number of reads");
                    }

                    AssertEqual((long)BigObjectBytes, total, "total bytes");
                    AssertEqual((long)BigObjectBytes, all.Length, "every byte read once");
                    AssertEqual(4, reads, "four 1 MB reads");
                    AssertTrue(all.ToArray().SequenceEqual(BigObject()), "bytes match");
                }

                using (HttpResponseMessage ranged = await ua.GetAsync("/api/v1/blackbox/" + _EntryId + "/objects/" + Uri.EscapeDataString(BigObjectName) + "?offset=10&length=5").ConfigureAwait(false))
                {
                    byte[] data = await ranged.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    AssertEqual(5, data.Length, "explicit length");
                    AssertTrue(data.SequenceEqual(BigObject().Skip(10).Take(5)), "ranged bytes");
                }

                await ExpectErrorAsync(ua, HttpMethod.Get, "/api/v1/blackbox/" + _EntryId + "/objects/record.json?offset=-1", null, HttpStatusCode.BadRequest, "InvalidArgument", "offset").ConfigureAwait(false);
                await ExpectErrorAsync(ua, HttpMethod.Get, "/api/v1/blackbox/" + _EntryId + "/objects/record.json?length=1048577", null, HttpStatusCode.BadRequest, "InvalidArgument", "length").ConfigureAwait(false);
                await ExpectErrorAsync(ua, HttpMethod.Get, "/api/v1/blackbox/" + _EntryId + "/objects/record.json?length=0", null, HttpStatusCode.BadRequest, "InvalidArgument", "length").ConfigureAwait(false);
                await ExpectErrorAsync(ua, HttpMethod.Get, "/api/v1/blackbox/" + _EntryId + "/objects/record.json?offset=abc", null, HttpStatusCode.BadRequest, "InvalidArgument", "offset").ConfigureAwait(false);
                await ExpectErrorAsync(ua, HttpMethod.Get, "/api/v1/blackbox/" + _EntryId + "/objects/nope.txt", null, HttpStatusCode.NotFound, BlackBoxNotFoundException.ErrorCode, null).ConfigureAwait(false);

                using (HttpResponseMessage zip = await ua.GetAsync("/api/v1/blackbox/" + _EntryId + "/download").ConfigureAwait(false))
                {
                    AssertStatusCode(HttpStatusCode.OK, zip, "download");
                    AssertEqual("application/zip", zip.Content.Headers.ContentType?.MediaType, "zip content type");
                    byte[] bytes = await zip.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    using ZipArchive archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
                    ZipArchiveEntry? manifestEntry = archive.GetEntry(BlackBoxObjectNames.Manifest);
                    AssertNotNull(manifestEntry, "manifest.json in the zip");
                    AssertNotNull(archive.GetEntry(BlackBoxObjectNames.Record), "record.json in the zip");
                    using StreamReader reader = new StreamReader(manifestEntry!.Open());
                    BlackBoxDownloadManifest manifest = JsonHelper.Deserialize<BlackBoxDownloadManifest>(await reader.ReadToEndAsync().ConfigureAwait(false));
                    AssertEqual(_EntryId, manifest.EntryId, "manifest entry");
                    AssertTrue(manifest.Objects.Count >= 2, "manifest lists the objects");
                }
            }));

            cases.Add(CaseAsync("rest_lists_and_analytics", "List, enumerate, search, hotspots, fingerprints and the maintainer bundle are caller scoped and paged", TestTags.Positive, async () =>
            {
                RequireSetup();
                using HttpClient ua = _UserA!.CreateClient(_Fx!.BaseUrl);
                using HttpClient b = _AdminB!.CreateClient(_Fx.BaseUrl);
                EnumerationResult<BlackBoxEntrySummary> list = await GetJsonAsync<EnumerationResult<BlackBoxEntrySummary>>(ua, "/api/v1/blackbox?missionId=" + _Mission!.Id + "&isCurrent=true&phase=Completion").ConfigureAwait(false);
                AssertEqual(_EntryId, list.Objects.Single().Id, "GET list with filters");
                EnumerationResult<BlackBoxEntrySummary> enumerated = await PostJsonAsync<EnumerationResult<BlackBoxEntrySummary>>(ua, "/api/v1/blackbox/enumerate", new { MissionId = _Mission.Id, Step = "ScopeViolation" }).ConfigureAwait(false);
                AssertEqual(_EntryId, enumerated.Objects.Single().Id, "POST enumerate with enum filter");
                EnumerationResult<BlackBoxEntrySummary> zero = await PostJsonAsync<EnumerationResult<BlackBoxEntrySummary>>(ua, "/api/v1/blackbox/enumerate", new { PageSize = 0 }).ConfigureAwait(false);
                AssertEqual(1, zero.PageSize, "page size 0 is normalized to 1 (docs/ENUMERATION.md)");
                EnumerationResult<BlackBoxEntrySummary> big = await PostJsonAsync<EnumerationResult<BlackBoxEntrySummary>>(ua, "/api/v1/blackbox/enumerate?pageSize=1001", new { }).ConfigureAwait(false);
                AssertEqual(1000, big.PageSize, "page size 1001 is normalized to 1000");
                await ExpectErrorAsync(ua, HttpMethod.Get, "/api/v1/blackbox?phase=NotAPhase", null, HttpStatusCode.BadRequest, "InvalidArgument", "phase").ConfigureAwait(false);
                await ExpectErrorAsync(ua, HttpMethod.Get, "/api/v1/blackbox?pinned=maybe", null, HttpStatusCode.BadRequest, "InvalidArgument", "pinned").ConfigureAwait(false);

                EnumerationResult<BlackBoxEntrySummary> foreign = await GetJsonAsync<EnumerationResult<BlackBoxEntrySummary>>(b, "/api/v1/blackbox?missionId=" + _Mission.Id).ConfigureAwait(false);
                AssertEqual(0L, foreign.TotalRecords, "another tenant's list never holds the entry");

                EnumerationResult<BlackBoxEntrySummary> found = await PostJsonAsync<EnumerationResult<BlackBoxEntrySummary>>(ua, "/api/v1/blackbox/search", new { Query = _Marker }).ConfigureAwait(false);
                AssertTrue(found.Objects.Any(o => o.Id == _EntryId), "search finds the reason marker");
                EnumerationResult<BlackBoxEntrySummary> foreignSearch = await PostJsonAsync<EnumerationResult<BlackBoxEntrySummary>>(b, "/api/v1/blackbox/search", new { Query = _Marker }).ConfigureAwait(false);
                AssertEqual(0L, foreignSearch.TotalRecords, "search is tenant scoped");
                await ExpectErrorAsync(ua, HttpMethod.Post, "/api/v1/blackbox/search", new { Query = "" }, HttpStatusCode.BadRequest, "InvalidArgument", null).ConfigureAwait(false);
                await ExpectErrorAsync(ua, HttpMethod.Post, "/api/v1/blackbox/search", new { Query = new string('q', 201) }, HttpStatusCode.BadRequest, "InvalidArgument", null).ConfigureAwait(false);

                EnumerationResult<BlackBoxHotspot> hotspots = await PostJsonAsync<EnumerationResult<BlackBoxHotspot>>(ua, "/api/v1/blackbox/hotspots/enumerate", new { PageSize = 10, Role = "OutOfScope" }).ConfigureAwait(false);
                AssertTrue(hotspots.Objects.Any(h => h.Directory.StartsWith("src", StringComparison.Ordinal)), "out-of-scope directory is a hotspot: " + String.Join(",", hotspots.Objects.Select(h => h.Directory)));
                EnumerationResult<BlackBoxFingerprintSummary> fingerprints = await PostJsonAsync<EnumerationResult<BlackBoxFingerprintSummary>>(ua, "/api/v1/blackbox/fingerprints/enumerate", new { PageSize = 10 }).ConfigureAwait(false);
                AssertTrue(fingerprints.Objects.Any(f => f.LatestEntryId == _EntryId || f.Count >= 1), "fingerprints");
                EnumerationResult<BlackBoxHotspot> foreignHotspots = await PostJsonAsync<EnumerationResult<BlackBoxHotspot>>(b, "/api/v1/blackbox/hotspots/enumerate", new { PageSize = 10, Role = "OutOfScope" }).ConfigureAwait(false);
                AssertEqual(0L, foreignHotspots.TotalRecords, "hotspots are tenant scoped");

                BlackBoxBundleResult markdown = await PostJsonAsync<BlackBoxBundleResult>(ua, "/api/v1/blackbox/bundle", new { MissionId = _Mission.Id, Max = 5 }).ConfigureAwait(false);
                AssertEqual(BlackBoxBundleFormatEnum.Markdown, markdown.Format, "Markdown by default");
                AssertEqual(1, markdown.EntryCount, "one entry");
                AssertContains("maintainer bundle", markdown.Content, "bundle heading");
                BlackBoxBundleResult json = await PostJsonAsync<BlackBoxBundleResult>(ua, "/api/v1/blackbox/bundle", new { MissionId = _Mission.Id, Format = "Json" }).ConfigureAwait(false);
                AssertContains(_EntryId, json.Content, "JSON bundle lists the entry");
            }));

            cases.Add(CaseAsync("rest_writes", "Notes, resolve, pin and unpin; validation errors carry the field", TestTags.Positive, async () =>
            {
                RequireSetup();
                using HttpClient ua = _UserA!.CreateClient(_Fx!.BaseUrl);
                BlackBoxNote note = await PostJsonAsync<BlackBoxNote>(ua, "/api/v1/blackbox/" + _EntryId + "/notes", new { Body = "Operator note " + _Marker }).ConfigureAwait(false);
                AssertEqual(BlackBoxNoteAuthorKindEnum.Operator, note.AuthorKind, "operator by default");
                EnumerationResult<BlackBoxNote> notes = await GetJsonAsync<EnumerationResult<BlackBoxNote>>(ua, "/api/v1/blackbox/" + _EntryId + "/notes").ConfigureAwait(false);
                AssertTrue(notes.Objects.Any(n => n.Id == note.Id), "note listed");
                EnumerationResult<BlackBoxNote> notesPost = await PostJsonAsync<EnumerationResult<BlackBoxNote>>(ua, "/api/v1/blackbox/" + _EntryId + "/notes/enumerate", new { PageSize = 1 }).ConfigureAwait(false);
                AssertEqual(1, notesPost.Objects.Count, "POST twin");
                string brief = await GetTextAsync(ua, "/api/v1/blackbox/" + _EntryId + "/brief").ConfigureAwait(false);
                AssertContains("Operator note " + _Marker, brief, "briefs include the newest notes");
                await ExpectErrorAsync(ua, HttpMethod.Post, "/api/v1/blackbox/" + _EntryId + "/notes", new { Body = "  " }, HttpStatusCode.BadRequest, "InvalidArgument", null).ConfigureAwait(false);
                await ExpectErrorAsync(ua, HttpMethod.Post, "/api/v1/blackbox/" + _EntryId + "/notes", new { Body = new string('n', 8001) }, HttpStatusCode.BadRequest, "InvalidArgument", null).ConfigureAwait(false);
                await ExpectErrorAsync(ua, HttpMethod.Post, "/api/v1/blackbox/" + _EntryId + "/notes", "{\"Body\":", HttpStatusCode.BadRequest, "InvalidArgument", null).ConfigureAwait(false);

                BlackBoxEntry resolved = await PostJsonAsync<BlackBoxEntry>(ua, "/api/v1/blackbox/" + _EntryId + "/resolve", new { Resolution = "Resolved", Note = "fixed", Link = "msn landed" }).ConfigureAwait(false);
                AssertEqual(BlackBoxResolutionEnum.Resolved, resolved.Resolution, "resolved");
                AssertNotNull(resolved.ResolvedUtc, "resolved time");
                BlackBoxEntry reopened = await PostJsonAsync<BlackBoxEntry>(ua, "/api/v1/blackbox/" + _EntryId + "/resolve", new { Resolution = "Open" }).ConfigureAwait(false);
                AssertNull(reopened.ResolvedUtc, "Open clears ResolvedUtc");
                await ExpectErrorAsync(ua, HttpMethod.Post, "/api/v1/blackbox/" + _EntryId + "/resolve", new { Resolution = "Unknown" }, HttpStatusCode.BadRequest, "InvalidArgument", null).ConfigureAwait(false);

                BlackBoxEntry pinned = await PostJsonAsync<BlackBoxEntry>(ua, "/api/v1/blackbox/" + _EntryId + "/pin", new { }).ConfigureAwait(false);
                AssertTrue(pinned.Pinned, "pinned");
                AssertNull(pinned.ExpiresUtc, "a pinned entry never expires");
                BlackBoxEntry unpinned = await PostJsonAsync<BlackBoxEntry>(ua, "/api/v1/blackbox/" + _EntryId + "/unpin", new { }).ConfigureAwait(false);
                AssertFalse(unpinned.Pinned, "unpinned");
            }));

            cases.Add(CaseAsync("rest_not_found_without_leak", "Unknown mission, mission without an entry, unknown entry and another tenant's entry are all 404 BlackBoxNotFound", TestTags.Negative, async () =>
            {
                RequireSetup();
                using HttpClient ua = _UserA!.CreateClient(_Fx!.BaseUrl);
                using HttpClient b = _AdminB!.CreateClient(_Fx.BaseUrl);
                string code = BlackBoxNotFoundException.ErrorCode;
                await ExpectErrorAsync(ua, HttpMethod.Get, "/api/v1/missions/msn_doesnotexist/blackbox", null, HttpStatusCode.NotFound, code, null).ConfigureAwait(false);
                await ExpectErrorAsync(ua, HttpMethod.Get, "/api/v1/missions/" + _NoEntryMission!.Id + "/blackbox", null, HttpStatusCode.NotFound, code, null).ConfigureAwait(false);
                await ExpectErrorAsync(ua, HttpMethod.Get, "/api/v1/missions/" + _NoEntryMission.Id + "/blackbox/brief", null, HttpStatusCode.NotFound, code, null).ConfigureAwait(false);
                await ExpectErrorAsync(ua, HttpMethod.Get, "/api/v1/blackbox/bbx_doesnotexist", null, HttpStatusCode.NotFound, code, null).ConfigureAwait(false);
                await ExpectErrorAsync(ua, HttpMethod.Get, "/api/v1/voyages/vyg_doesnotexist/blackbox", null, HttpStatusCode.NotFound, code, null).ConfigureAwait(false);

                // Another tenant reads exactly what an unknown id reads (same status and code), never 403.
                string[] reads =
                {
                    "/api/v1/blackbox/" + _EntryId,
                    "/api/v1/blackbox/" + _EntryId + "/brief",
                    "/api/v1/blackbox/" + _EntryId + "/sections/Status",
                    "/api/v1/blackbox/" + _EntryId + "/objects",
                    "/api/v1/blackbox/" + _EntryId + "/objects/record.json",
                    "/api/v1/blackbox/" + _EntryId + "/download",
                    "/api/v1/blackbox/" + _EntryId + "/notes",
                    "/api/v1/missions/" + _Mission!.Id + "/blackbox",
                    "/api/v1/missions/" + _Mission.Id + "/blackbox/brief",
                    "/api/v1/voyages/" + _Voyage!.Id + "/blackbox"
                };
                foreach (string path in reads)
                {
                    string body = await ExpectErrorAsync(b, HttpMethod.Get, path, null, HttpStatusCode.NotFound, code, null).ConfigureAwait(false);
                    AssertFalse(body.Contains(_Marker, StringComparison.Ordinal), path + " leaked the marker");
                }

                await ExpectErrorAsync(b, HttpMethod.Post, "/api/v1/blackbox/" + _EntryId + "/notes", new { Body = "x" }, HttpStatusCode.NotFound, code, null).ConfigureAwait(false);
                await ExpectErrorAsync(b, HttpMethod.Post, "/api/v1/blackbox/" + _EntryId + "/resolve", new { Resolution = "Resolved" }, HttpStatusCode.NotFound, code, null).ConfigureAwait(false);
                await ExpectErrorAsync(b, HttpMethod.Post, "/api/v1/blackbox/" + _EntryId + "/pin", new { }, HttpStatusCode.NotFound, code, null).ConfigureAwait(false);
                await ExpectErrorAsync(b, HttpMethod.Post, "/api/v1/blackbox/" + _EntryId + "/recovery/rp1", new { DryRun = true }, HttpStatusCode.NotFound, code, null).ConfigureAwait(false);
                await ExpectErrorAsync(b, HttpMethod.Post, "/api/v1/blackbox/" + _EntryId + "/recommendations/rc1/apply", new { DryRun = true }, HttpStatusCode.NotFound, code, null).ConfigureAwait(false);
                await ExpectErrorAsync(b, HttpMethod.Post, "/api/v1/blackbox/" + _EntryId + "/reanalyze", new { }, HttpStatusCode.NotFound, code, null).ConfigureAwait(false);
                await ExpectErrorAsync(b, HttpMethod.Delete, "/api/v1/blackbox/" + _EntryId, null, HttpStatusCode.NotFound, code, null).ConfigureAwait(false);
                BlackBoxDeleteResult skipped = await PostJsonAsync<BlackBoxDeleteResult>(b, "/api/v1/blackbox/delete", new { Ids = new[] { _EntryId } }).ConfigureAwait(false);
                AssertEqual(0, skipped.Deleted, "another tenant's id is skipped");

                using HttpClient a = _AdminA!.CreateClient(_Fx.BaseUrl);
                BlackBoxEntry still = await GetJsonAsync<BlackBoxEntry>(a, "/api/v1/blackbox/" + _EntryId + "?includeRecord=false").ConfigureAwait(false);
                AssertEqual(_EntryId, still.Id, "the entry survives tenant B's calls");
            }));

            cases.Add(CaseAsync("rest_limits_and_authorization", "1001 ids is CollectionLimitExceeded; a regular user calling the admin routes is 403; the tenant admin routes refuse a regular user", TestTags.Negative, async () =>
            {
                RequireSetup();
                using HttpClient a = _AdminA!.CreateClient(_Fx!.BaseUrl);
                using HttpClient ua = _UserA!.CreateClient(_Fx.BaseUrl);
                List<string> ids = Enumerable.Range(0, 1001).Select(i => "bbx_limit" + i).ToList();
                using (HttpResponseMessage limited = await a.PostAsync("/api/v1/blackbox/delete", JsonHelper.ToJsonContent(new { Ids = ids })).ConfigureAwait(false))
                {
                    string body = await limited.Content.ReadAsStringAsync().ConfigureAwait(false);
                    AssertStatusCode(HttpStatusCode.BadRequest, limited, body);
                    AssertContains("CollectionLimitExceeded", body, "typed limit error");
                }

                string[] adminOnly =
                {
                    "POST /api/v1/blackbox/purge", "POST /api/v1/blackbox/gc", "GET /api/v1/blackbox/storage", "POST /api/v1/blackbox/storage/migrate",
                    "GET /api/v1/blackbox/storage/migrations/bbm_x", "POST /api/v1/blackbox/backfill"
                };
                foreach (string route in adminOnly)
                {
                    await ExpectStatusAsync(ua, route, HttpStatusCode.Forbidden).ConfigureAwait(false);
                    await ExpectStatusAsync(a, route, HttpStatusCode.Forbidden).ConfigureAwait(false);
                }

                string[] tenantAdmin =
                {
                    "POST /api/v1/blackbox/" + _EntryId + "/reanalyze", "GET /api/v1/blackbox/retention", "DELETE /api/v1/blackbox/" + _EntryId, "POST /api/v1/blackbox/delete"
                };
                foreach (string route in tenantAdmin) await ExpectStatusAsync(ua, route, HttpStatusCode.Forbidden).ConfigureAwait(false);

                BlackBoxRetentionStatus retention = await GetJsonAsync<BlackBoxRetentionStatus>(a, "/api/v1/blackbox/retention").ConfigureAwait(false);
                AssertTrue(retention.TotalEntries >= 2, "tenant admin sees its tenant's entries: " + retention.TotalEntries);
                BlackBoxEntry reanalyzed = await PostJsonAsync<BlackBoxEntry>(a, "/api/v1/blackbox/" + _EntryId + "/reanalyze", new { }).ConfigureAwait(false);
                AssertEqual(_EntryId, reanalyzed.Id, "reanalyze as tenant admin");

                using (HttpResponseMessage unauthenticated = await _Fx.UnauthClient.GetAsync("/api/v1/blackbox/" + _EntryId).ConfigureAwait(false))
                    AssertEqual(HttpStatusCode.Unauthorized, unauthenticated.StatusCode, "no credential");
            }));

            cases.Add(CaseAsync("rest_global_admin_routes", "Storage, retention, GC dry run, purge dry run and backfill for a global admin; storage migration starts a job and reports it by id", TestTags.Positive, async () =>
            {
                RequireSetup();
                HttpClient admin = _Fx!.AuthClient;
                BlackBoxStorageStatus storage = await GetJsonAsync<BlackBoxStorageStatus>(admin, "/api/v1/blackbox/storage").ConfigureAwait(false);
                AssertEqual(BlackBoxStorageTargetEnum.Database, storage.Target, "database target");
                BlackBoxRetentionStatus retention = await GetJsonAsync<BlackBoxRetentionStatus>(admin, "/api/v1/blackbox/retention").ConfigureAwait(false);
                AssertTrue(retention.TotalEntries >= 2, "global admin counts every tenant");
                BlackBoxGcReport gc = await PostJsonAsync<BlackBoxGcReport>(admin, "/api/v1/blackbox/gc", new { DryRun = true }).ConfigureAwait(false);
                AssertTrue(gc.DryRun, "dry run report");
                BlackBoxPurgeResult purge = await PostJsonAsync<BlackBoxPurgeResult>(admin, "/api/v1/blackbox/purge", new { MissionId = _Mission!.Id }).ConfigureAwait(false);
                AssertTrue(purge.DryRun, "purge is a dry run by default");
                AssertEqual(1L, purge.Matched, "purge dry run matches the mission's entry");
                AssertEqual(0, purge.Deleted, "nothing deleted on a dry run");
                BlackBoxBackfillResult backfill = await PostJsonAsync<BlackBoxBackfillResult>(admin, "/api/v1/blackbox/backfill", new { MaxSubjects = 5 }).ConfigureAwait(false);
                AssertEqual(BlackBoxCaptureModeEnum.Backfill, backfill.Mode, "backfill mode");

                BlackBoxEntry main = await GetJsonAsync<BlackBoxEntry>(admin, "/api/v1/blackbox/" + _EntryId + "?includeRecord=false").ConfigureAwait(false);
                BlackBoxFingerprintTrend trend = await GetJsonAsync<BlackBoxFingerprintTrend>(admin, "/api/v1/blackbox/fingerprints/" + Uri.EscapeDataString(main.Fingerprint!) + "/trend").ConfigureAwait(false);
                AssertEqual(main.Fingerprint, trend.Fingerprint, "trend fingerprint");
                AssertTrue(trend.Total >= 1 && trend.Versions.Count >= 1, "trend counts per version");
                BlackBoxFixture fixture = await PostJsonAsync<BlackBoxFixture>(admin, "/api/v1/blackbox/" + _EntryId + "/export-fixture", new { Name = "bbapi-rest" }).ConfigureAwait(false);
                AssertEqual("bbapi-rest", fixture.Name, "fixture name");
                using (HttpClient tenantAdmin = _AdminA!.CreateClient(_Fx.BaseUrl))
                    await ExpectStatusAsync(tenantAdmin, "POST /api/v1/blackbox/" + _EntryId + "/export-fixture", HttpStatusCode.Forbidden).ConfigureAwait(false);

                // Migrate from an empty source (Disk) so the shared fixture's entries stay on the database target.
                BlackBoxStorageMigrationJob job = await PostJsonAsync<BlackBoxStorageMigrationJob>(admin, "/api/v1/blackbox/storage/migrate", new { From = "Disk", To = "Database" }).ConfigureAwait(false);
                AssertTrue(!String.IsNullOrEmpty(job.Id) && job.Id.StartsWith("bbm_", StringComparison.Ordinal), "migration job id");
                AssertEqual(BlackBoxStorageTargetEnum.Database, job.To, "migration target");
                BlackBoxStorageMigrationJob status = await GetJsonAsync<BlackBoxStorageMigrationJob>(admin, "/api/v1/blackbox/storage/migrations/" + job.Id).ConfigureAwait(false);
                AssertEqual(job.Id, status.Id, "migration status by id");
                await ExpectStatusAsync(admin, "GET /api/v1/blackbox/storage/migrations/bbm_x", HttpStatusCode.NotFound).ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("rest_recovery_dry_run_and_confirm", "Recovery: dry run returns a plan and a token; a confirmed run without, with a bogus or with a used token is 409; the token runs the recorded steps once, notes the entry (Operator) and emits blackbox.recovery_run; a caller who may not run a step is 403; unknown path and recommendation are 404", TestTags.Negative, async () =>
            {
                RequireSetup();
                using HttpClient a = _AdminA!.CreateClient(_Fx!.BaseUrl);
                using HttpClient ua = _UserA!.CreateClient(_Fx.BaseUrl);
                RecoveryTarget target = await CaptureRecoveryTargetAsync(a, "rest").ConfigureAwait(false);
                string route = "/api/v1/blackbox/" + target.EntryId + "/recovery/" + Uri.EscapeDataString(target.PathId);
                await ExpectErrorAsync(ua, HttpMethod.Post, route, new { DryRun = true }, HttpStatusCode.Forbidden, "", null).ConfigureAwait(false);

                BlackBoxRecoveryRunResult dry = await PostJsonAsync<BlackBoxRecoveryRunResult>(a, route, new { }).ConfigureAwait(false);
                AssertTrue(dry.DryRun, "dry run by default");
                AssertTrue(dry.Plan.Count > 0, "the plan lists the recorded steps");
                AssertTrue(!String.IsNullOrEmpty(dry.ConfirmToken), "a confirm token");
                AssertNotNull(dry.ConfirmTokenExpiresUtc, "token expiry");

                string confirmation = BlackBoxConfirmationException.ErrorCode;
                await ExpectErrorAsync(a, HttpMethod.Post, route, new { DryRun = false }, HttpStatusCode.Conflict, confirmation, null).ConfigureAwait(false);
                await ExpectErrorAsync(a, HttpMethod.Post, route, new { DryRun = false, ConfirmToken = "bbc_bogus" }, HttpStatusCode.Conflict, confirmation, null).ConfigureAwait(false);
                await ExpectErrorAsync(a, HttpMethod.Post, "/api/v1/blackbox/" + target.EntryId + "/recovery/no-such-path", new { DryRun = true }, HttpStatusCode.NotFound, BlackBoxNotFoundException.ErrorCode, null).ConfigureAwait(false);
                await ExpectErrorAsync(a, HttpMethod.Post, "/api/v1/blackbox/" + target.EntryId + "/recommendations/no-such-recommendation/apply", new { DryRun = true }, HttpStatusCode.NotFound, BlackBoxNotFoundException.ErrorCode, null).ConfigureAwait(false);

                // The token is not consumed by a refused call without it, so it still confirms the run once.
                BlackBoxRecoveryRunResult dry2 = await PostJsonAsync<BlackBoxRecoveryRunResult>(a, route, new { DryRun = true }).ConfigureAwait(false);
                BlackBoxRecoveryRunResult run = await PostJsonAsync<BlackBoxRecoveryRunResult>(a, route, new { DryRun = false, ConfirmToken = dry2.ConfirmToken }).ConfigureAwait(false);
                AssertFalse(run.DryRun, "a real run");
                AssertEqual(dry2.Plan.Count, run.StepResults.Count, "one result per step");
                await ExpectErrorAsync(a, HttpMethod.Post, route, new { DryRun = false, ConfirmToken = dry2.ConfirmToken }, HttpStatusCode.Conflict, confirmation, null).ConfigureAwait(false);

                EnumerationResult<BlackBoxNote> notes = await GetJsonAsync<EnumerationResult<BlackBoxNote>>(a, "/api/v1/blackbox/" + target.EntryId + "/notes?pageSize=50").ConfigureAwait(false);
                AssertTrue(notes.Objects.Any(n => n.AuthorKind == BlackBoxNoteAuthorKindEnum.Operator), "REST recovery runs are noted as Operator: " + String.Join(" | ", notes.Objects.Select(n => n.AuthorKind + " " + n.Body)));
                EnumerationResult<ArmadaEvent> events = await GetJsonAsync<EnumerationResult<ArmadaEvent>>(_Fx.AuthClient, "/api/v1/events?type=blackbox.recovery_run&pageSize=100").ConfigureAwait(false);
                AssertTrue(events.Objects.Any(e => e.EntityId == target.EntryId), "blackbox.recovery_run emitted for the entry");
            }));

            cases.Add(CaseAsync("mcp_read_tools", "get_blackbox (summary, brief, json), sections, objects, 64 KB chunk paging of a 3 MB object, lists and enumerate blackbox", TestTags.Positive, async () =>
            {
                RequireSetup();
                using McpToolClient client = Mcp(_UserA!);
                await client.InitializeAsync().ConfigureAwait(false);
                BlackBoxDigest digest = await OkAsync<BlackBoxDigest>(client, "get_blackbox", new { missionId = _Mission!.Id }).ConfigureAwait(false);
                AssertEqual(_EntryId, digest.EntryId, "summary by mission");
                AssertEqual(BlackBoxStepEnum.ScopeViolation, digest.Step, "summary step");
                McpToolCallResult raw = await client.CallToolResultAsync("get_blackbox", JsonHelper.Serialize(new { entryId = _EntryId })).ConfigureAwait(false);
                AssertTrue(Encoding.UTF8.GetByteCount(raw.Text) < 8192, "summary stays small: " + raw.Text.Length);
                BlackBoxBriefResult agent = await OkAsync<BlackBoxBriefResult>(client, "get_blackbox", new { entryId = _EntryId, format = "brief" }).ConfigureAwait(false);
                BlackBoxBriefResult user = await OkAsync<BlackBoxBriefResult>(client, "get_blackbox", new { entryId = _EntryId, format = "brief", audience = "user" }).ConfigureAwait(false);
                AssertEqual(BlackBoxAudienceEnum.User, user.Audience, "audience echoed");
                AssertNotEqual(agent.Brief, user.Brief, "audiences differ over MCP");
                BlackBoxRecord record = await OkAsync<BlackBoxRecord>(client, "get_blackbox", new { voyageId = _Voyage!.Id, format = "json" }).ConfigureAwait(false);
                AssertEqual(_VoyageEntryId, record.EntryId, "json by voyage");
                await RefusedAsync(client, "get_blackbox", new { entryId = _EntryId, format = "xml" }).ConfigureAwait(false);
                await RefusedAsync(client, "get_blackbox", new { entryId = _EntryId, format = "brief", audience = "robot" }).ConfigureAwait(false);
                await ErrorAsync(client, "get_blackbox", new { }, McpToolErrorCodeEnum.InvalidArgument, null).ConfigureAwait(false);

                BlackBoxSectionResult section = await OkAsync<BlackBoxSectionResult>(client, "read_blackbox_section", new { entryId = _EntryId, section = "scope" }).ConfigureAwait(false);
                AssertNotNull(section.Record.Scope, "scope section");
                await ErrorAsync(client, "read_blackbox_section", new { entryId = _EntryId, section = "Nope" }, McpToolErrorCodeEnum.InvalidArgument, null).ConfigureAwait(false);

                EnumerationResult<BlackBoxObjectSummary> objects = await OkAsync<EnumerationResult<BlackBoxObjectSummary>>(client, "list_blackbox_objects", new { entryId = _EntryId, pageSize = 100 }).ConfigureAwait(false);
                AssertTrue(objects.Objects.Any(o => o.Name == BigObjectName), "objects listed");

                long offset = 0;
                int chunks = 0;
                StringBuilder text = new StringBuilder();
                while (true)
                {
                    BlackBoxObjectChunk chunk = await OkAsync<BlackBoxObjectChunk>(client, "read_blackbox_object", new { entryId = _EntryId, name = BigObjectName, offset = offset }).ConfigureAwait(false);
                    AssertTrue(chunk.Length <= 65536, "at most 64 KB per chunk: " + chunk.Length);
                    AssertEqual((long)BigObjectBytes, chunk.TotalBytes, "totalBytes");
                    AssertEqual("utf-8", chunk.Encoding, "text object");
                    text.Append(chunk.Content);
                    chunks++;
                    if (!chunk.NextOffset.HasValue) break;
                    offset = chunk.NextOffset.Value;
                    AssertTrue(chunks < 100, "bounded number of chunks");
                }

                AssertEqual((BigObjectBytes + 65535) / 65536, chunks, "chunks of 64 KB until the end");
                AssertEqual(Encoding.UTF8.GetString(BigObject()), text.ToString(), "content reassembles");
                await ErrorAsync(client, "read_blackbox_object", new { entryId = _EntryId, name = BigObjectName, length = 65537 }, McpToolErrorCodeEnum.InvalidArgument, null).ConfigureAwait(false);
                await ErrorAsync(client, "read_blackbox_object", new { entryId = _EntryId, name = BigObjectName, offset = -1 }, McpToolErrorCodeEnum.InvalidArgument, null).ConfigureAwait(false);
                await ErrorAsync(client, "read_blackbox_object", new { entryId = _EntryId, name = "missing.txt" }, McpToolErrorCodeEnum.NotFound, BlackBoxNotFoundException.ErrorCode).ConfigureAwait(false);

                EnumerationResult<BlackBoxEntrySummary> entries = await OkAsync<EnumerationResult<BlackBoxEntrySummary>>(client, "list_blackbox_entries", new { missionId = _Mission.Id }).ConfigureAwait(false);
                AssertEqual(_EntryId, entries.Objects.Single().Id, "list_blackbox_entries");
                AssertEqual(10, entries.PageSize, "default page size 10");
                EnumerationResult<BlackBoxEntrySummary> searched = await OkAsync<EnumerationResult<BlackBoxEntrySummary>>(client, "search_blackbox", new { query = _Marker }).ConfigureAwait(false);
                AssertTrue(searched.Objects.Any(o => o.Id == _EntryId), "search_blackbox");
                await ErrorAsync(client, "list_blackbox_entries", new { phase = "NotAPhase" }, McpToolErrorCodeEnum.InvalidArgument, null).ConfigureAwait(false);
                EnumerationResult<BlackBoxNote> notes = await OkAsync<EnumerationResult<BlackBoxNote>>(client, "list_blackbox_notes", new { entryId = _EntryId }).ConfigureAwait(false);
                AssertTrue(notes.TotalRecords >= 0, "list_blackbox_notes");
                await OkAsync<EnumerationResult<BlackBoxHotspot>>(client, "list_blackbox_hotspots", new { pageSize = 5 }).ConfigureAwait(false);
                await OkAsync<EnumerationResult<BlackBoxFingerprintSummary>>(client, "list_blackbox_fingerprints", new { pageSize = 5 }).ConfigureAwait(false);
                BlackBoxBundleResult bundle = await OkAsync<BlackBoxBundleResult>(client, "get_blackbox_bundle", new { missionId = _Mission.Id, max = 1 }).ConfigureAwait(false);
                AssertEqual(1, bundle.EntryCount, "bundle");
                EnumerationResult<BlackBoxEntrySummary> viaEnumerate = await OkAsync<EnumerationResult<BlackBoxEntrySummary>>(client, "enumerate", new { entityType = "blackbox", missionId = _Mission.Id }).ConfigureAwait(false);
                AssertEqual(_EntryId, viaEnumerate.Objects.Single().Id, "enumerate entityType blackbox");
                await ErrorAsync(client, "enumerate", new { entityType = "blackbox", status = "Bogus" }, McpToolErrorCodeEnum.InvalidArgument, null).ConfigureAwait(false);

                // LastBlackBoxEntryId on mission_status and voyage_status (plan 16.6 #5).
                Mission status = await OkAsync<Mission>(client, "mission_status", new { missionId = _Mission.Id }).ConfigureAwait(false);
                AssertEqual(_EntryId, status.LastBlackBoxEntryId, "mission_status carries LastBlackBoxEntryId");
            }));

            cases.Add(CaseAsync("mcp_write_tools_and_errors", "annotate (Agent), resolve, pin; another tenant is NotFound; admin tools are Forbidden to a regular user; recovery answers NotImplemented until the runner merges", TestTags.Negative, async () =>
            {
                RequireSetup();
                using McpToolClient user = Mcp(_UserA!);
                using McpToolClient other = Mcp(_AdminB!);
                using McpToolClient admin = Mcp(_AdminA!);
                await user.InitializeAsync().ConfigureAwait(false);
                await other.InitializeAsync().ConfigureAwait(false);
                await admin.InitializeAsync().ConfigureAwait(false);

                BlackBoxNote note = await OkAsync<BlackBoxNote>(user, "annotate_blackbox", new { missionId = _Mission!.Id, body = "Agent finding" }).ConfigureAwait(false);
                AssertEqual(BlackBoxNoteAuthorKindEnum.Agent, note.AuthorKind, "MCP notes are Agent notes");
                BlackBoxEntry resolved = await OkAsync<BlackBoxEntry>(user, "resolve_blackbox", new { entryId = _EntryId, resolution = "WontFix", note = "n" }).ConfigureAwait(false);
                AssertEqual(BlackBoxResolutionEnum.WontFix, resolved.Resolution, "resolve_blackbox");
                await OkAsync<BlackBoxEntry>(user, "resolve_blackbox", new { entryId = _EntryId, resolution = "Open" }).ConfigureAwait(false);
                await RefusedAsync(user, "resolve_blackbox", new { entryId = _EntryId, resolution = "Maybe" }).ConfigureAwait(false);
                BlackBoxEntry pinned = await OkAsync<BlackBoxEntry>(user, "pin_blackbox", new { entryId = _EntryId }).ConfigureAwait(false);
                AssertTrue(pinned.Pinned, "pin_blackbox pins by default");
                BlackBoxEntry unpinned = await OkAsync<BlackBoxEntry>(user, "pin_blackbox", new { entryId = _EntryId, pinned = false }).ConfigureAwait(false);
                AssertFalse(unpinned.Pinned, "pin_blackbox pinned false unpins");

                foreach (string tool in new[] { "get_blackbox", "read_blackbox_section", "list_blackbox_objects", "read_blackbox_object", "list_blackbox_notes", "annotate_blackbox", "resolve_blackbox", "pin_blackbox", "run_blackbox_recovery", "apply_blackbox_recommendation", "reanalyze_blackbox" })
                {
                    object args = new { entryId = _EntryId, section = "Status", name = "record.json", body = "x", resolution = "Resolved", pathId = "rp1", recommendationId = "rc1" };
                    McpToolCallResult result = await other.CallToolResultAsync(tool, JsonHelper.Serialize(args)).ConfigureAwait(false);
                    McpToolResultProbe probe = McpToolResultProbe.From(result);
                    AssertEqual(McpToolErrorCodeEnum.NotFound, probe.ErrorCode, tool + " for another tenant: " + result.Text);
                    AssertEqual(BlackBoxNotFoundException.ErrorCode, probe.Code, tool + " code");
                    AssertFalse(result.Text.Contains(_Marker, StringComparison.Ordinal), tool + " leaked the marker");
                }

                BlackBoxDeleteResult foreignDelete = await OkAsync<BlackBoxDeleteResult>(other, "delete_blackbox_entries", new { entryIds = new[] { _EntryId } }).ConfigureAwait(false);
                AssertEqual(0, foreignDelete.Deleted, "another tenant's delete skips the id");
                await ErrorAsync(admin, "delete_blackbox_entries", new { entryIds = Enumerable.Range(0, 1001).Select(i => "bbx_" + i).ToArray() }, McpToolErrorCodeEnum.InvalidArgument, "CollectionLimitExceeded").ConfigureAwait(false);

                foreach (string tool in new[] { "purge_blackbox", "run_blackbox_gc", "get_blackbox_storage" })
                {
                    await ErrorAsync(user, tool, new { }, McpToolErrorCodeEnum.Forbidden, null).ConfigureAwait(false);
                    await ErrorAsync(admin, tool, new { }, McpToolErrorCodeEnum.Forbidden, null).ConfigureAwait(false);
                }

                await ErrorAsync(user, "reanalyze_blackbox", new { entryId = _EntryId }, McpToolErrorCodeEnum.Forbidden, null).ConfigureAwait(false);
                await ErrorAsync(user, "delete_blackbox_entries", new { entryIds = new[] { _EntryId } }, McpToolErrorCodeEnum.Forbidden, null).ConfigureAwait(false);
                await ErrorAsync(user, "get_blackbox_retention", new { }, McpToolErrorCodeEnum.Forbidden, null).ConfigureAwait(false);
                BlackBoxRetentionStatus retention = await OkAsync<BlackBoxRetentionStatus>(admin, "get_blackbox_retention", new { }).ConfigureAwait(false);
                AssertTrue(retention.TotalEntries >= 2, "get_blackbox_retention");

                using (HttpClient adminRest = _AdminA!.CreateClient(_Fx!.BaseUrl))
                {
                    RecoveryTarget target = await CaptureRecoveryTargetAsync(adminRest, "mcp").ConfigureAwait(false);
                    await ErrorAsync(user, "run_blackbox_recovery", new { entryId = target.EntryId, pathId = target.PathId }, McpToolErrorCodeEnum.Forbidden, null).ConfigureAwait(false);
                    BlackBoxRecoveryRunResult dry = await OkAsync<BlackBoxRecoveryRunResult>(admin, "run_blackbox_recovery", new { entryId = target.EntryId, pathId = target.PathId }).ConfigureAwait(false);
                    AssertTrue(dry.DryRun && !String.IsNullOrEmpty(dry.ConfirmToken), "MCP dry run by default");
                    await ErrorAsync(admin, "run_blackbox_recovery", new { entryId = target.EntryId, pathId = target.PathId, dryRun = false }, McpToolErrorCodeEnum.Conflict, BlackBoxConfirmationException.ErrorCode).ConfigureAwait(false);
                    BlackBoxRecoveryRunResult run = await OkAsync<BlackBoxRecoveryRunResult>(admin, "run_blackbox_recovery", new { entryId = target.EntryId, pathId = target.PathId, dryRun = false, confirmToken = dry.ConfirmToken }).ConfigureAwait(false);
                    AssertFalse(run.DryRun, "MCP real run");
                    await ErrorAsync(admin, "run_blackbox_recovery", new { entryId = target.EntryId, pathId = target.PathId, dryRun = false, confirmToken = dry.ConfirmToken }, McpToolErrorCodeEnum.Conflict, BlackBoxConfirmationException.ErrorCode).ConfigureAwait(false);
                    EnumerationResult<BlackBoxNote> notes = await OkAsync<EnumerationResult<BlackBoxNote>>(admin, "list_blackbox_notes", new { entryId = target.EntryId, pageSize = 50 }).ConfigureAwait(false);
                    AssertTrue(notes.Objects.Any(n => n.AuthorKind == BlackBoxNoteAuthorKindEnum.Agent), "MCP recovery runs are noted as Agent");
                    await ErrorAsync(admin, "apply_blackbox_recommendation", new { entryId = target.EntryId, recommendationId = "no-such-recommendation" }, McpToolErrorCodeEnum.NotFound, BlackBoxNotFoundException.ErrorCode).ConfigureAwait(false);
                }

                BlackBoxFingerprintTrend trend = await OkAsync<BlackBoxFingerprintTrend>(user, "get_blackbox_fingerprint_trend", new { fingerprint = (await OkAsync<BlackBoxEntry>(user, "pin_blackbox", new { entryId = _EntryId, pinned = false }).ConfigureAwait(false)).Fingerprint }).ConfigureAwait(false);
                AssertTrue(trend.Total >= 1, "fingerprint trend counts the entry");
                await ErrorAsync(admin, "export_blackbox_fixture", new { entryId = _EntryId }, McpToolErrorCodeEnum.Forbidden, null).ConfigureAwait(false);
                await RefusedAsync(user, "run_blackbox_recovery", new { entryId = _EntryId }).ConfigureAwait(false);

                using McpToolClient root = new McpToolClient("http://127.0.0.1:" + _Fx!.McpPort + "/mcp", null, new Dictionary<string, string> { ["X-Api-Key"] = _Fx.ApiKey });
                await root.InitializeAsync().ConfigureAwait(false);
                BlackBoxGcReport gc = await OkAsync<BlackBoxGcReport>(root, "run_blackbox_gc", new { dryRun = true }).ConfigureAwait(false);
                AssertTrue(gc.DryRun, "run_blackbox_gc dry run as a global admin");
                BlackBoxPurgeResult purge = await OkAsync<BlackBoxPurgeResult>(root, "purge_blackbox", new { missionId = _Mission.Id }).ConfigureAwait(false);
                AssertTrue(purge.DryRun && purge.Deleted == 0, "purge_blackbox is a dry run by default");
                BlackBoxStorageStatus storage = await OkAsync<BlackBoxStorageStatus>(root, "get_blackbox_storage", new { }).ConfigureAwait(false);
                BlackBoxFixture fixture = await OkAsync<BlackBoxFixture>(root, "export_blackbox_fixture", new { entryId = _EntryId, name = "bbapi-fixture" }).ConfigureAwait(false);
                AssertEqual("bbapi-fixture", fixture.Name, "export_blackbox_fixture name");
                AssertFalse(JsonHelper.Serialize(fixture).Contains(_UserA!.TenantId, StringComparison.Ordinal), "the fixture drops the tenant id");
                AssertEqual(BlackBoxStorageTargetEnum.Database, storage.Target, "get_blackbox_storage");
                VoyageStatusProbe voyage = await OkAsync<VoyageStatusProbe>(root, "voyage_status", new { voyageId = _Voyage!.Id }).ConfigureAwait(false);
                AssertEqual(_VoyageEntryId, voyage.Voyage?.LastBlackBoxEntryId, "voyage_status summary carries LastBlackBoxEntryId");
            }));

            cases.Add(CaseAsync("mcp_resources_and_prompts", "The black box MCP resources read through resources/read and the prompts expand through prompts/get", TestTags.Positive, async () =>
            {
                RequireSetup();
                string templates = await RawMcpAsync(_UserA!, "resources/templates/list", new { }).ConfigureAwait(false);
                AssertContains("armada://blackbox/{entryId}/brief.md", templates, "brief template listed");
                AssertContains("armada://missions/{missionId}/blackbox", templates, "mission template listed");
                string brief = await RawMcpAsync(_UserA!, "resources/read", new { uri = "armada://blackbox/" + _EntryId + "/brief.md" }).ConfigureAwait(false);
                AssertContains("text/markdown", brief, "brief resource mime type");
                string mission = await RawMcpAsync(_UserA!, "resources/read", new { uri = "armada://missions/" + _Mission!.Id + "/blackbox" }).ConfigureAwait(false);
                AssertContains(_EntryId, mission, "mission resource is the digest");
                string obj = await RawMcpAsync(_UserA!, "resources/read", new { uri = "armada://blackbox/" + _EntryId + "/objects/record.json" }).ConfigureAwait(false);
                AssertContains("record.json", obj, "object resource");
                string foreign = await RawMcpAsync(_AdminB!, "resources/read", new { uri = "armada://blackbox/" + _EntryId + "/record.json" }).ConfigureAwait(false);
                AssertFalse(foreign.Contains(_Marker, StringComparison.Ordinal), "another tenant cannot read the record resource");
                string prompts = await RawMcpAsync(_UserA!, "prompts/list", new { }).ConfigureAwait(false);
                AssertContains("review_blackbox", prompts, "review prompt listed");
                AssertContains("explain_blackbox", prompts, "explain prompt listed");
                string review = await RawMcpAsync(_UserA!, "prompts/get", new { name = "review_blackbox", arguments = new { missionId = _Mission.Id, goal = "fix it" } }).ConfigureAwait(false);
                AssertContains(_Mission.Id, review, "review prompt names the mission");
                AssertContains("dry run", review, "review prompt carries the safety guidance");
            }));

            cases.Add(CaseAsync("mcp_resource_not_found", "resources/read for a missing entry, a missing object or another tenant's entry is the MCP resource-not-found error (-32002) with the typed NotFound data, not -32603", TestTags.Negative, async () =>
            {
                RequireSetup();
                string missing = await RawMcpAsync(_UserA!, "resources/read", new { uri = "armada://blackbox/bbx_missing/record.json" }).ConfigureAwait(false);
                AssertContains("-32002", missing, "missing entry is resource not found");
                AssertContains("BlackBoxNotFound", missing, "missing entry carries the black box code");
                AssertContains("NotFound", missing, "missing entry carries the typed error code");
                AssertFalse(missing.Contains("-32603", StringComparison.Ordinal), "missing entry is not an internal error");
                string missingBrief = await RawMcpAsync(_UserA!, "resources/read", new { uri = "armada://blackbox/bbx_missing/brief.md" }).ConfigureAwait(false);
                AssertContains("-32002", missingBrief, "missing brief is resource not found");
                string missingMission = await RawMcpAsync(_UserA!, "resources/read", new { uri = "armada://missions/msn_missing/blackbox" }).ConfigureAwait(false);
                AssertContains("-32002", missingMission, "a mission with no entry is resource not found");
                string missingObject = await RawMcpAsync(_UserA!, "resources/read", new { uri = "armada://blackbox/" + _EntryId + "/objects/no-such-object.txt" }).ConfigureAwait(false);
                AssertContains("-32002", missingObject, "missing object is resource not found");
                AssertFalse(missingObject.Contains("-32603", StringComparison.Ordinal), "missing object is not an internal error");
                string foreign = await RawMcpAsync(_AdminB!, "resources/read", new { uri = "armada://blackbox/" + _EntryId + "/record.json" }).ConfigureAwait(false);
                AssertContains("-32002", foreign, "another tenant's entry is resource not found");
                string present = await RawMcpAsync(_UserA!, "resources/read", new { uri = "armada://blackbox/" + _EntryId + "/record.json" }).ConfigureAwait(false);
                AssertFalse(present.Contains("-32002", StringComparison.Ordinal), "a visible entry reads without an error");
                AssertNull(Armada.Server.Mcp.Tools.McpBlackBoxResources.ToResourceError("armada://x", new InvalidOperationException("boom")), "a non black box error is not mapped");
                AssertEqual(-32002, Armada.Server.Mcp.Tools.McpBlackBoxResources.ToResourceError("armada://x", new BlackBoxObjectMissingException("gone"))!.Code, "object missing maps to -32002");
            }));

            cases.Add(CaseAsync("websocket_commands", "blackbox.get, blackbox.brief and blackbox.list over the WebSocket command route", TestTags.Positive, async () =>
            {
                RequireSetup();
                using ClientWebSocket ws = await E2EServerFixture.ConnectAuthenticatedWebSocketAsync(_Fx!.RestPort).ConfigureAwait(false);
                string get = await CommandAsync(ws, "blackbox.get", new { Route = "command", Action = "blackbox.get", Id = _Mission!.Id }).ConfigureAwait(false);
                AssertContains("command.result", get, "get result");
                AssertContains(_EntryId, get, "get by mission id");
                string brief = await CommandAsync(ws, "blackbox.brief", new { Route = "command", Action = "blackbox.brief", Data = new { EntryId = _EntryId, Audience = "maintainer" } }).ConfigureAwait(false);
                AssertContains("Maintainer", brief, "brief audience");
                string list = await CommandAsync(ws, "blackbox.list", new { Route = "command", Action = "blackbox.list", Data = new { MissionId = _Mission.Id } }).ConfigureAwait(false);
                AssertContains(_EntryId, list, "list");
                string missing = await CommandAsync(ws, "blackbox.get", new { Route = "command", Action = "blackbox.get", Id = "bbx_missing" }).ConfigureAwait(false);
                AssertContains("command.error", missing, "missing entry is an error");
                AssertContains("NotFound", missing, "NotFound code");
                string badAudience = await CommandAsync(ws, "blackbox.brief", new { Route = "command", Action = "blackbox.brief", Id = _EntryId, Data = new { Audience = "robot" } }).ConfigureAwait(false);
                AssertContains("InvalidArgument", badAudience, "invalid audience");
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("deleting_state_and_delete", "An entry in the Deleting state is 409 BlackBoxDeleting; a missing object is 410 and a corrupt one 409; a tenant admin deletes an entry and it is then 404", TestTags.Negative, async () =>
            {
                RequireSetup();
                Mission doomed;
                string doomedEntry;
                using (DatabaseDriver db = await OpenDatabaseAsync().ConfigureAwait(false))
                {
                    doomed = await CreateFailedMissionAsync(db, "deleting").ConfigureAwait(false);
                }

                doomedEntry = (await _Fx!.Server.BlackBox!.Capture.CaptureMissionFailureAsync(new BlackBoxCaptureContext { Mission = doomed, StatusBefore = MissionStatusEnum.InProgress, Step = BlackBoxStepEnum.ScopeViolation }).ConfigureAwait(false))!;
                // Let enrichment finish first, so it does not move the state back while the test holds it at Deleting.
                await _Fx.Server.BlackBox.Queue.DrainAsync().ConfigureAwait(false);
                using HttpClient a = _AdminA!.CreateClient(_Fx.BaseUrl);
                using (DatabaseDriver db = await OpenDatabaseAsync().ConfigureAwait(false))
                {
                    AssertTrue(await db.BlackBoxEntries.UpdateStateAsync(doomedEntry, BlackBoxStateEnum.Deleting).ConfigureAwait(false), "set Deleting");
                }

                await ExpectErrorAsync(a, HttpMethod.Get, "/api/v1/blackbox/" + doomedEntry, null, HttpStatusCode.Conflict, BlackBoxDeletingException.ErrorCode, null).ConfigureAwait(false);
                await ExpectErrorAsync(a, HttpMethod.Get, "/api/v1/blackbox/" + doomedEntry + "/brief", null, HttpStatusCode.Conflict, BlackBoxDeletingException.ErrorCode, null).ConfigureAwait(false);
                using (McpToolClient client = Mcp(_AdminA!))
                {
                    await client.InitializeAsync().ConfigureAwait(false);
                    await ErrorAsync(client, "get_blackbox", new { entryId = doomedEntry }, McpToolErrorCodeEnum.Conflict, BlackBoxDeletingException.ErrorCode).ConfigureAwait(false);
                }

                using (DatabaseDriver db = await OpenDatabaseAsync().ConfigureAwait(false))
                {
                    await db.BlackBoxEntries.UpdateStateAsync(doomedEntry, BlackBoxStateEnum.Captured).ConfigureAwait(false);
                }

                // A missing object is 410 BlackBoxObjectMissing; bytes that no longer match their SHA-256 are 409 BlackBoxObjectCorrupt.
                BlackBoxEntry doomedRow;
                using (DatabaseDriver db = await OpenDatabaseAsync().ConfigureAwait(false))
                {
                    doomedRow = (await db.BlackBoxEntries.ReadAsync(doomedEntry).ConfigureAwait(false))!;
                }

                BlackBoxObject small = await _Fx.Server.BlackBox.Objects.WriteAsync(doomedRow, "evidence/small.txt", "text/plain", Encoding.UTF8.GetBytes("small evidence"), false, true).ConfigureAwait(false);
                BlackBoxObject gone = await _Fx.Server.BlackBox.Objects.WriteAsync(doomedRow, "evidence/gone.txt", "text/plain", Encoding.UTF8.GetBytes("gone evidence"), false, true).ConfigureAwait(false);
                using (DatabaseDriver db = await OpenDatabaseAsync().ConfigureAwait(false))
                {
                    AssertTrue(await db.BlackBoxObjects.WriteDataAsync(small.Id, Encoding.UTF8.GetBytes("tampered bytes")).ConfigureAwait(false), "tamper");
                    AssertTrue(await db.BlackBoxObjects.SetMissingAsync(gone.Id, true).ConfigureAwait(false), "mark missing");
                }

                await ExpectErrorAsync(a, HttpMethod.Get, "/api/v1/blackbox/" + doomedEntry + "/objects/" + Uri.EscapeDataString("evidence/small.txt"), null, HttpStatusCode.Conflict, BlackBoxObjectCorruptException.ErrorCode, null).ConfigureAwait(false);
                await ExpectErrorAsync(a, HttpMethod.Get, "/api/v1/blackbox/" + doomedEntry + "/objects/" + Uri.EscapeDataString("evidence/gone.txt"), null, HttpStatusCode.Gone, BlackBoxObjectMissingException.ErrorCode, null).ConfigureAwait(false);

                BlackBoxDeleteResult deleted = await DeleteJsonAsync<BlackBoxDeleteResult>(a, "/api/v1/blackbox/" + doomedEntry).ConfigureAwait(false);
                AssertEqual(1, deleted.Deleted, "deleted");
                await ExpectErrorAsync(a, HttpMethod.Get, "/api/v1/blackbox/" + doomedEntry, null, HttpStatusCode.NotFound, BlackBoxNotFoundException.ErrorCode, null).ConfigureAwait(false);
                await ExpectErrorAsync(a, HttpMethod.Delete, "/api/v1/blackbox/" + doomedEntry, null, HttpStatusCode.NotFound, BlackBoxNotFoundException.ErrorCode, null).ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("operator_transition_captured", "A manual transition to Failed, Cancelled or Blocked over REST, MCP and WebSocket is captured as External/OperatorTransition; a transition to InProgress is not", TestTags.Positive, async () =>
            {
                RequireSetup();
                using HttpClient a = _AdminA!.CreateClient(_Fx!.BaseUrl);
                Mission rest = await CreateMissionAsync("transition-rest", MissionStatusEnum.InProgress, null).ConfigureAwait(false);
                using (HttpResponseMessage response = await a.PutAsync("/api/v1/missions/" + rest.Id + "/status", JsonHelper.ToJsonContent(new { Status = "Failed" })).ConfigureAwait(false))
                    AssertStatusCode(HttpStatusCode.OK, response, "REST transition");
                await ExpectOperatorEntryAsync(a, rest.Id, BlackBoxStepEnum.OperatorTransition, "REST", MissionStatusEnum.InProgress).ConfigureAwait(false);
                await ExpectOneStatusChangedAsync(rest.Id, MissionStatusEnum.Failed, MissionStatusEnum.InProgress, "REST").ConfigureAwait(false);

                Mission mcp = await CreateMissionAsync("transition-mcp", MissionStatusEnum.InProgress, null).ConfigureAwait(false);
                using (McpToolClient client = Mcp(_AdminA!))
                {
                    await client.InitializeAsync().ConfigureAwait(false);
                    await OkAsync<Mission>(client, "transition_mission_status", new { missionId = mcp.Id, status = "Cancelled" }).ConfigureAwait(false);
                }

                await ExpectOperatorEntryAsync(a, mcp.Id, BlackBoxStepEnum.OperatorTransition, "MCP", MissionStatusEnum.InProgress).ConfigureAwait(false);
                await ExpectOneStatusChangedAsync(mcp.Id, MissionStatusEnum.Cancelled, MissionStatusEnum.InProgress, "MCP").ConfigureAwait(false);

                Mission ws = await CreateMissionAsync("transition-ws", MissionStatusEnum.InProgress, null).ConfigureAwait(false);
                using (ClientWebSocket socket = await E2EServerFixture.ConnectAuthenticatedWebSocketAsync(_Fx.RestPort).ConfigureAwait(false))
                {
                    string reply = await CommandAsync(socket, "transition_mission_status", new { Route = "command", Action = "transition_mission_status", Id = ws.Id, Status = "Failed" }).ConfigureAwait(false);
                    AssertContains("command.result", reply, "WebSocket transition");
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).ConfigureAwait(false);
                }

                await ExpectOperatorEntryAsync(a, ws.Id, BlackBoxStepEnum.OperatorTransition, "WebSocket", MissionStatusEnum.InProgress).ConfigureAwait(false);
                await ExpectOneStatusChangedAsync(ws.Id, MissionStatusEnum.Failed, MissionStatusEnum.InProgress, "WebSocket").ConfigureAwait(false);

                Mission notCaptured = await CreateMissionAsync("transition-none", MissionStatusEnum.Assigned, null).ConfigureAwait(false);
                using (HttpResponseMessage response = await a.PutAsync("/api/v1/missions/" + notCaptured.Id + "/status", JsonHelper.ToJsonContent(new { Status = "InProgress" })).ConfigureAwait(false))
                    AssertStatusCode(HttpStatusCode.OK, response, "REST transition to InProgress");
                EnumerationResult<BlackBoxEntrySummary> none = await GetJsonAsync<EnumerationResult<BlackBoxEntrySummary>>(a, "/api/v1/blackbox?missionId=" + notCaptured.Id).ConfigureAwait(false);
                AssertEqual(0L, none.TotalRecords, "a transition to InProgress is not captured");
                await ExpectOneStatusChangedAsync(notCaptured.Id, MissionStatusEnum.InProgress, MissionStatusEnum.Assigned, "REST (not captured)").ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("vessel_deletion_captured", "Deleting a vessel over REST, MCP and WebSocket captures each active mission it cancels as External/VesselDeleted, and the entry outlives the mission row", TestTags.Positive, async () =>
            {
                RequireSetup();
                using HttpClient a = _AdminA!.CreateClient(_Fx!.BaseUrl);
                // deleteWithVessel (default true) would remove the entries with the vessel; keep them to inspect the capture.
                bool deleteWithVessel = _Fx.Settings.BlackBox.Retention.DeleteWithVessel;
                _Fx.Settings.BlackBox.Retention.DeleteWithVessel = false;
                try
                {
                foreach (string surface in new[] { "REST", "MCP", "WebSocket" })
                {
                    Vessel vessel;
                    using (DatabaseDriver db = await OpenDatabaseAsync().ConfigureAwait(false))
                    {
                        vessel = new Vessel("bbapi-delete-" + surface.ToLowerInvariant() + "-" + Guid.NewGuid().ToString("N").Substring(0, 6), "https://example.invalid/bbapi-" + surface.ToLowerInvariant() + ".git");
                        vessel.TenantId = _AdminA.TenantId;
                        vessel.UserId = _AdminA.UserId;
                        vessel = await db.Vessels.CreateAsync(vessel).ConfigureAwait(false);
                    }

                    Mission mission = await CreateMissionAsync("vessel-" + surface, MissionStatusEnum.Pending, vessel.Id).ConfigureAwait(false);
                    if (surface == "REST")
                    {
                        using HttpResponseMessage response = await a.DeleteAsync("/api/v1/vessels/" + vessel.Id).ConfigureAwait(false);
                        AssertTrue(response.IsSuccessStatusCode, "REST delete vessel: " + (int)response.StatusCode + " " + await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                    }
                    else if (surface == "MCP")
                    {
                        using McpToolClient client = Mcp(_AdminA!);
                        await client.InitializeAsync().ConfigureAwait(false);
                        McpToolCallResult result = await client.CallToolResultAsync("delete_vessel", JsonHelper.Serialize(new { vesselId = vessel.Id })).ConfigureAwait(false);
                        AssertFalse(result.IsError, "MCP delete_vessel: " + result.Text);
                        AssertNull(McpToolResultProbe.From(result).ErrorCode, "MCP delete_vessel: " + result.Text);
                    }
                    else
                    {
                        using ClientWebSocket socket = await E2EServerFixture.ConnectAuthenticatedWebSocketAsync(_Fx.RestPort).ConfigureAwait(false);
                        string reply = await CommandAsync(socket, "delete_vessel", new { Route = "command", Action = "delete_vessel", Id = vessel.Id }).ConfigureAwait(false);
                        AssertContains("command.result", reply, "WebSocket delete_vessel");
                        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).ConfigureAwait(false);
                    }

                    await ExpectOperatorEntryAsync(a, mission.Id, BlackBoxStepEnum.VesselDeleted, surface, MissionStatusEnum.Pending).ConfigureAwait(false);
                }
                }
                finally
                {
                    _Fx.Settings.BlackBox.Retention.DeleteWithVessel = deleteWithVessel;
                }
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Black box REST, MCP and WebSocket surfaces", cases: cases);
        }

        #endregion

        #region Private-Methods

        private async Task SetupAsync()
        {
            _Fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
            _AdminA = await E2ETenantUser.CreateAsync(_Fx.AuthClient, "bbapi-admin-a", true).ConfigureAwait(false);
            _UserA = await E2ETenantUser.CreateAsync(_Fx.AuthClient, "bbapi-user-a", false, _AdminA.TenantId).ConfigureAwait(false);
            _AdminB = await E2ETenantUser.CreateAsync(_Fx.AuthClient, "bbapi-admin-b", true).ConfigureAwait(false);
            BlackBoxRuntime runtime = _Fx.Server.BlackBox ?? throw new InvalidOperationException("the server has no black box runtime");

            using (DatabaseDriver db = await OpenDatabaseAsync().ConfigureAwait(false))
            {
                _Mission = await CreateFailedMissionAsync(db, "main").ConfigureAwait(false);
                _NoEntryMission = await CreateFailedMissionAsync(db, "none").ConfigureAwait(false);
                Voyage voyage = new Voyage("bbapi voyage", "voyage " + _Marker);
                voyage.TenantId = _AdminA.TenantId;
                voyage.UserId = _UserA.UserId;
                voyage.Status = VoyageStatusEnum.Failed;
                _Voyage = await db.Voyages.CreateAsync(voyage).ConfigureAwait(false);
            }

            BlackBoxCaptureContext context = new BlackBoxCaptureContext
            {
                Mission = _Mission,
                StatusBefore = MissionStatusEnum.InProgress,
                Step = BlackBoxStepEnum.ScopeViolation,
                CommandOutput = "build output " + _Marker
            };
            context.OutOfScopeFiles.AddRange(new[] { "src/feature/a.cs", "src/feature/b.cs" });
            _EntryId = await runtime.Capture.CaptureMissionFailureAsync(context).ConfigureAwait(false) ?? throw new InvalidOperationException("mission capture returned no entry");
            _VoyageEntryId = await runtime.Capture.CaptureVoyageFailureAsync(new BlackBoxVoyageCaptureContext
            {
                Voyage = _Voyage,
                StatusBefore = VoyageStatusEnum.InProgress,
                Step = BlackBoxStepEnum.SettlementFailed,
                Reason = "Voyage settlement failed " + _Marker
            }).ConfigureAwait(false) ?? throw new InvalidOperationException("voyage capture returned no entry");

            await runtime.Queue.DrainAsync().ConfigureAwait(false);
            using (DatabaseDriver db = await OpenDatabaseAsync().ConfigureAwait(false))
            {
                BlackBoxEntry entry = (await db.BlackBoxEntries.ReadAsync(_EntryId).ConfigureAwait(false))!;
                await runtime.Objects.WriteAsync(entry, BigObjectName, "text/plain", BigObject(), false, true).ConfigureAwait(false);
            }
        }

        private async Task<Mission> CreateFailedMissionAsync(DatabaseDriver db, string label)
        {
            Mission mission = new Mission("bbapi " + label + " mission", "Fix it " + _Marker);
            mission.TenantId = _UserA!.TenantId;
            mission.UserId = _UserA.UserId;
            mission.Status = MissionStatusEnum.Failed;
            mission.FailureKind = MissionFailureKindEnum.ScopeViolation;
            mission.FailureReason = "Mission modified files outside its scoped file list: src/feature/a.cs " + _Marker;
            mission.LastUpdateUtc = DateTime.UtcNow.AddSeconds(-5);
            return await db.Missions.CreateAsync(mission).ConfigureAwait(false);
        }

        private async Task<Mission> CreateMissionAsync(string label, MissionStatusEnum status, string? vesselId)
        {
            using (DatabaseDriver db = await OpenDatabaseAsync().ConfigureAwait(false))
            {
                Mission mission = new Mission("bbapi " + label + " mission", "Operator action " + _Marker);
                mission.TenantId = _UserA!.TenantId;
                mission.UserId = _UserA.UserId;
                mission.Status = status;
                mission.VesselId = vesselId;
                return await db.Missions.CreateAsync(mission).ConfigureAwait(false);
            }
        }

        private static async Task ExpectOperatorEntryAsync(HttpClient client, string missionId, BlackBoxStepEnum step, string surface, MissionStatusEnum statusBefore)
        {
            // The current entry (a vessel delete through REST or MCP first cancels through the admiral, whose own
            // OperatorTransition entry the VesselDeleted entry supersedes).
            EnumerationResult<BlackBoxEntrySummary> page = await GetJsonAsync<EnumerationResult<BlackBoxEntrySummary>>(client, "/api/v1/blackbox?isCurrent=true&missionId=" + missionId).ConfigureAwait(false);
            AssertEqual(1L, page.TotalRecords, surface + ": one current entry for " + missionId);
            BlackBoxEntrySummary entry = page.Objects.Single();
            AssertEqual(statusBefore.ToString(), entry.StatusBefore, surface + " status before");
            AssertEqual(step, entry.Step, surface + " step");
            AssertEqual(BlackBoxPhaseEnum.External, entry.Phase, surface + " phase");
            AssertEqual(BlackBoxTriageEnum.External, entry.Triage, surface + " triage");
            string brief = await GetTextAsync(client, "/api/v1/blackbox/" + entry.Id + "/brief").ConfigureAwait(false);
            AssertContains(surface, brief, surface + " named in the brief's note");
        }

        private async Task<RecoveryTarget> CaptureRecoveryTargetAsync(HttpClient admin, string label)
        {
            Mission mission = await CreateMissionAsync("recovery-" + label, MissionStatusEnum.Failed, null).ConfigureAwait(false);
            mission.FailureKind = MissionFailureKindEnum.ScopeViolation;
            mission.FailureReason = "Mission modified files outside its scoped file list: src/feature/c.cs";
            BlackBoxCaptureContext context = new BlackBoxCaptureContext { Mission = mission, StatusBefore = MissionStatusEnum.InProgress, Step = BlackBoxStepEnum.ScopeViolation };
            context.OutOfScopeFiles.Add("src/feature/c.cs");
            string entryId = (await _Fx!.Server.BlackBox!.Capture.CaptureMissionFailureAsync(context).ConfigureAwait(false))!;
            await _Fx.Server.BlackBox.Queue.DrainAsync().ConfigureAwait(false);
            BlackBoxEntry entry = await GetJsonAsync<BlackBoxEntry>(admin, "/api/v1/blackbox/" + entryId).ConfigureAwait(false);
            List<BlackBoxRecoveryPath> paths = entry.Record?.Analysis?.RecoveryPaths ?? new List<BlackBoxRecoveryPath>();
            BlackBoxRecoveryPath? path = paths.FirstOrDefault(p => !p.DataLossRisk && p.Steps.Any(st => st.Action != null && !String.IsNullOrEmpty(st.Action.Tool)));
            AssertNotNull(path, "the advisor records a runnable recovery path for a scope violation: " + String.Join(", ", paths.Select(p => p.Id)));
            RecoveryTarget target = new RecoveryTarget();
            target.EntryId = entryId;
            target.PathId = path!.Id;
            return target;
        }

        private static byte[] BigObject()
        {
            StringBuilder sb = new StringBuilder(BigObjectBytes);
            int line = 0;
            while (sb.Length < BigObjectBytes)
            {
                sb.Append("log line ").Append(line++).Append(": build step output with ascii text only\n");
            }

            return Encoding.ASCII.GetBytes(sb.ToString(0, BigObjectBytes));
        }

        private void RequireSetup()
        {
            if (_Fx == null || _Mission == null || String.IsNullOrEmpty(_EntryId)) throw new InvalidOperationException("setup_capture_entries did not run");
        }

        private async Task ExpectOneStatusChangedAsync(string missionId, MissionStatusEnum status, MissionStatusEnum previous, string label)
        {
            using DatabaseDriver db = await OpenDatabaseAsync().ConfigureAwait(false);
            EnumerationResult<ArmadaEvent> page = await db.Events.EnumerateAsync(new EnumerationQuery { MissionId = missionId, EventType = "mission.status_changed", PageSize = 50 }).ConfigureAwait(false);
            AssertEqual(1, page.Objects.Count, label + ": exactly one mission.status_changed per transition");
            MissionStatusChangedPayload payload = JsonHelper.Deserialize<MissionStatusChangedPayload>(page.Objects[0].Payload!);
            AssertEqual(status, payload.Status, label + ": Status");
            AssertEqual(previous, payload.PreviousStatus, label + ": PreviousStatus");
        }

        private async Task<DatabaseDriver> OpenDatabaseAsync()
        {
            LoggingModule quiet = new LoggingModule();
            quiet.Settings.EnableConsole = false;
            DatabaseSettings settings = new DatabaseSettings { Type = DatabaseTypeEnum.Sqlite, Filename = Path.Combine(_Fx!.TempDir, "armada.db") };
            DatabaseDriver db = DatabaseDriverFactory.Create(settings, quiet);
            await db.InitializeAsync().ConfigureAwait(false);
            return db;
        }

        private static async Task<T> GetJsonAsync<T>(HttpClient client, string path)
        {
            using HttpResponseMessage response = await client.GetAsync(path).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            AssertStatusCode(HttpStatusCode.OK, response, "GET " + path + ": " + body);
            return JsonHelper.Deserialize<T>(body);
        }

        private static async Task<string> GetTextAsync(HttpClient client, string path)
        {
            using HttpResponseMessage response = await client.GetAsync(path).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            AssertStatusCode(HttpStatusCode.OK, response, "GET " + path + ": " + body);
            return body;
        }

        private static async Task<T> PostJsonAsync<T>(HttpClient client, string path, object body)
        {
            using HttpResponseMessage response = await client.PostAsync(path, JsonHelper.ToJsonContent(body)).ConfigureAwait(false);
            string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            AssertStatusCode(HttpStatusCode.OK, response, "POST " + path + ": " + text);
            return JsonHelper.Deserialize<T>(text);
        }

        private static async Task<T> DeleteJsonAsync<T>(HttpClient client, string path)
        {
            using HttpResponseMessage response = await client.DeleteAsync(path).ConfigureAwait(false);
            string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            AssertStatusCode(HttpStatusCode.OK, response, "DELETE " + path + ": " + text);
            return JsonHelper.Deserialize<T>(text);
        }

        private static async Task<string> ExpectErrorAsync(HttpClient client, HttpMethod method, string path, object? body, HttpStatusCode status, string code, string? field)
        {
            using HttpRequestMessage request = new HttpRequestMessage(method, path);
            if (body is string raw) request.Content = new StringContent(raw, Encoding.UTF8, "application/json");
            else if (body != null) request.Content = JsonHelper.ToJsonContent(body);
            using HttpResponseMessage response = await client.SendAsync(request).ConfigureAwait(false);
            string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            AssertEqual(status, response.StatusCode, method + " " + path + ": " + text);
            BlackBoxApiErrorBody error = JsonHelper.Deserialize<BlackBoxApiErrorBody>(text);
            if (code.Length > 0) AssertEqual(code, error.Data?.Code, method + " " + path + " code: " + text);
            if (field != null) AssertEqual(field, error.Data?.Field, method + " " + path + " field: " + text);
            return text;
        }

        private static async Task ExpectStatusAsync(HttpClient client, string route, HttpStatusCode status)
        {
            string[] parts = route.Split(' ', 2);
            using HttpRequestMessage request = new HttpRequestMessage(new HttpMethod(parts[0]), parts[1]);
            if (parts[0] != "GET" && parts[0] != "DELETE") request.Content = JsonHelper.ToJsonContent(new { });
            using HttpResponseMessage response = await client.SendAsync(request).ConfigureAwait(false);
            string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            AssertEqual(status, response.StatusCode, route + ": " + text);
        }

        private McpToolClient Mcp(E2ETenantUser user)
        {
            Dictionary<string, string> headers = new Dictionary<string, string> { ["Authorization"] = "Bearer " + user.BearerToken };
            return new McpToolClient("http://127.0.0.1:" + _Fx!.McpPort + "/mcp", null, headers);
        }

        private static async Task<T> OkAsync<T>(McpToolClient client, string tool, object args)
        {
            McpToolCallResult result = await client.CallToolResultAsync(tool, JsonHelper.Serialize(args)).ConfigureAwait(false);
            AssertFalse(result.IsError, tool + ": " + result.Text);
            McpToolResultProbe probe = McpToolResultProbe.From(result);
            AssertNull(probe.ErrorCode, tool + " returned an error: " + result.Text);
            return JsonHelper.Deserialize<T>(result.Text);
        }

        private static async Task ErrorAsync(McpToolClient client, string tool, object args, McpToolErrorCodeEnum expected, string? code)
        {
            McpToolCallResult result = await client.CallToolResultAsync(tool, JsonHelper.Serialize(args)).ConfigureAwait(false);
            McpToolResultProbe probe = McpToolResultProbe.From(result);
            AssertEqual(expected, probe.ErrorCode, tool + ": " + result.Text);
            if (code != null) AssertEqual(code, probe.Code, tool + " code: " + result.Text);
        }

        /// <summary>
        /// The call is refused as invalid input: either the tool's typed InvalidArgument, or Voltaic's own input-schema
        /// validation (an isError result naming the argument) before the handler runs.
        /// </summary>
        private static async Task RefusedAsync(McpToolClient client, string tool, object args)
        {
            McpToolCallResult result = await client.CallToolResultAsync(tool, JsonHelper.Serialize(args)).ConfigureAwait(false);
            McpToolResultProbe probe = McpToolResultProbe.From(result);
            AssertTrue(probe.ErrorCode == McpToolErrorCodeEnum.InvalidArgument || (result.IsError && result.Text.Contains("argument", StringComparison.OrdinalIgnoreCase)), tool + " refused as invalid input: " + result.Text);
        }

        private async Task<string> RawMcpAsync(E2ETenantUser user, string method, object parameters)
        {
            using HttpClient http = new HttpClient();
            http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bearer " + user.BearerToken);
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            string endpoint = "http://127.0.0.1:" + _Fx!.McpPort + "/mcp";
            string init = JsonHelper.Serialize(new { jsonrpc = "2.0", id = "1", method = "initialize", @params = new { protocolVersion = "2024-11-05", capabilities = new { }, clientInfo = new { name = "bbapi", version = "1.0" } } });
            using HttpResponseMessage initialized = await http.PostAsync(endpoint, new StringContent(init, Encoding.UTF8, "application/json")).ConfigureAwait(false);
            AssertTrue(initialized.IsSuccessStatusCode, "initialize: " + (int)initialized.StatusCode);
            if (initialized.Headers.TryGetValues("Mcp-Session-Id", out IEnumerable<string>? sessions))
                http.DefaultRequestHeaders.TryAddWithoutValidation("Mcp-Session-Id", sessions.First());
            string ack = JsonHelper.Serialize(new { jsonrpc = "2.0", method = "notifications/initialized" });
            using (await http.PostAsync(endpoint, new StringContent(ack, Encoding.UTF8, "application/json")).ConfigureAwait(false)) { }
            string call = JsonHelper.Serialize(new { jsonrpc = "2.0", id = "2", method = method, @params = parameters });
            using HttpResponseMessage response = await http.PostAsync(endpoint, new StringContent(call, Encoding.UTF8, "application/json")).ConfigureAwait(false);
            return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        }

        private static async Task<string> CommandAsync(ClientWebSocket ws, string action, object message)
        {
            byte[] payload = Encoding.UTF8.GetBytes(JsonHelper.Serialize(message));
            await ws.SendAsync(new ArraySegment<byte>(payload), WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false);
            DateTime deadline = DateTime.UtcNow.AddSeconds(15);
            byte[] buffer = new byte[1024 * 1024];
            while (DateTime.UtcNow < deadline)
            {
                using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                StringBuilder text = new StringBuilder();
                WebSocketReceiveResult received;
                do
                {
                    received = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token).ConfigureAwait(false);
                    text.Append(Encoding.UTF8.GetString(buffer, 0, received.Count));
                }
                while (!received.EndOfMessage);
                string reply = text.ToString();
                if ((reply.Contains("command.result", StringComparison.Ordinal) || reply.Contains("command.error", StringComparison.Ordinal))
                    && reply.Contains("\"" + action + "\"", StringComparison.Ordinal)) return reply;
            }

            throw new TimeoutException("no reply to " + action);
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { TestTags.EndToEnd, tag });
        }

        #endregion
    }
}

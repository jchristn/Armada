namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Net.Sockets;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// End-to-end REST coverage for release CRUD and derivation flows. Ported from the retired
    /// automated <c>ReleaseTests</c> suite; every case drives the real REST API exposed by the
    /// shared in-process server obtained through <see cref="E2EServerFixture"/>.
    /// </summary>
    public sealed class ReleaseSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the end-to-end Releases suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("create_list_read_update_refresh_and_delete", "Releases_CreateListReadUpdateRefreshAndDelete", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                string workingDirectory = Path.Combine(Path.GetTempPath(), "armada-releases-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path.Combine(workingDirectory, "artifacts"));
                await File.WriteAllTextAsync(Path.Combine(workingDirectory, "version.txt"), "2.3.4").ConfigureAwait(false);
                await File.WriteAllTextAsync(Path.Combine(workingDirectory, "artifacts", "app.zip"), "artifact").ConfigureAwait(false);

                string vesselId = String.Empty;
                string workflowProfileId = String.Empty;
                string voyageId = String.Empty;
                string checkRunId = String.Empty;
                string releaseId = String.Empty;

                try
                {
                    HttpResponseMessage vesselResponse = await authClient.PostAsync("/api/v1/vessels",
                        JsonHelper.ToJsonContent(new
                        {
                            Name = "Release Vessel",
                            RepoUrl = "file:///tmp/release-vessel.git",
                            LocalPath = workingDirectory,
                            WorkingDirectory = workingDirectory,
                            DefaultBranch = "main"
                        })).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.Created, vesselResponse.StatusCode);

                    Vessel vessel = await JsonHelper.DeserializeAsync<Vessel>(vesselResponse).ConfigureAwait(false);
                    vesselId = vessel.Id;

                    HttpResponseMessage workflowProfileResponse = await authClient.PostAsync("/api/v1/workflow-profiles",
                        JsonHelper.ToJsonContent(new
                        {
                            Name = "Release Workflow",
                            Scope = WorkflowProfileScopeEnum.Vessel,
                            VesselId = vesselId,
                            IsDefault = true,
                            ReleaseVersioningCommand = BuildEmitFileCommand("version.txt"),
                            ExpectedArtifacts = new[] { "artifacts/app.zip" }
                        })).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.Created, workflowProfileResponse.StatusCode);

                    WorkflowProfile workflowProfile = await JsonHelper.DeserializeAsync<WorkflowProfile>(workflowProfileResponse).ConfigureAwait(false);
                    workflowProfileId = workflowProfile.Id;

                    Voyage voyage = await CreateVoyageAsync(authClient, vesselId, "Release Voyage", 2).ConfigureAwait(false);
                    voyageId = voyage.Id;

                    HttpResponseMessage checkRunResponse = await authClient.PostAsync("/api/v1/check-runs",
                        JsonHelper.ToJsonContent(new
                        {
                            VesselId = vesselId,
                            WorkflowProfileId = workflowProfileId,
                            VoyageId = voyageId,
                            Type = CheckRunTypeEnum.ReleaseVersioning,
                            Label = "Version Check"
                        })).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.Created, checkRunResponse.StatusCode);

                    CheckRun checkRun = await JsonHelper.DeserializeAsync<CheckRun>(checkRunResponse).ConfigureAwait(false);
                    checkRunId = checkRun.Id;
                    AssertEqual(CheckRunStatusEnum.Passed, checkRun.Status);

                    HttpResponseMessage createReleaseResponse = await authClient.PostAsync("/api/v1/releases",
                        JsonHelper.ToJsonContent(new
                        {
                            WorkflowProfileId = workflowProfileId,
                            VoyageIds = new[] { voyageId },
                            CheckRunIds = new[] { checkRunId },
                            Status = ReleaseStatusEnum.Candidate
                        })).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.Created, createReleaseResponse.StatusCode);

                    Release release = await JsonHelper.DeserializeAsync<Release>(createReleaseResponse).ConfigureAwait(false);
                    releaseId = release.Id;
                    AssertStartsWith("rel_", releaseId);
                    AssertEqual("2.3.4", release.Version);
                    AssertEqual("v2.3.4", release.TagName);
                    AssertEqual(ReleaseStatusEnum.Candidate, release.Status);
                    AssertTrue(release.MissionIds.Count == 2, "Expected release to aggregate voyage missions.");
                    AssertTrue(release.Artifacts.Count == 1, "Expected one derived artifact.");

                    HttpResponseMessage getReleaseResponse = await authClient.GetAsync("/api/v1/releases/" + releaseId).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.OK, getReleaseResponse.StatusCode);
                    Release loadedRelease = await JsonHelper.DeserializeAsync<Release>(getReleaseResponse).ConfigureAwait(false);
                    AssertEqual(releaseId, loadedRelease.Id);

                    HttpResponseMessage listResponse = await authClient.GetAsync(
                        "/api/v1/releases?status=Candidate&checkRunId=" + Uri.EscapeDataString(checkRunId) + "&pageSize=100").ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.OK, listResponse.StatusCode);
                    EnumerationResult<Release> releases = await JsonHelper.DeserializeAsync<EnumerationResult<Release>>(listResponse).ConfigureAwait(false);
                    AssertTrue(releases.Objects.Exists(current => current.Id == releaseId), "Expected release to appear in filtered list.");

                    HttpResponseMessage updateReleaseResponse = await authClient.PutAsync("/api/v1/releases/" + releaseId,
                        JsonHelper.ToJsonContent(new
                        {
                            VesselId = vesselId,
                            WorkflowProfileId = workflowProfileId,
                            Title = "Release 2.3.4",
                            Version = "2.3.4",
                            TagName = "v2.3.4",
                            Summary = "Ready for shipment",
                            Notes = "Edited release notes",
                            Status = ReleaseStatusEnum.Shipped,
                            VoyageIds = new[] { voyageId },
                            MissionIds = release.MissionIds,
                            CheckRunIds = new[] { checkRunId }
                        })).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.OK, updateReleaseResponse.StatusCode);

                    Release updatedRelease = await JsonHelper.DeserializeAsync<Release>(updateReleaseResponse).ConfigureAwait(false);
                    AssertEqual(ReleaseStatusEnum.Shipped, updatedRelease.Status);
                    AssertTrue(updatedRelease.PublishedUtc.HasValue, "Expected published timestamp when shipped.");
                    AssertEqual("Edited release notes", updatedRelease.Notes);

                    HttpResponseMessage refreshResponse = await authClient.PostAsync("/api/v1/releases/" + releaseId + "/refresh", null).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.OK, refreshResponse.StatusCode);
                    Release refreshedRelease = await JsonHelper.DeserializeAsync<Release>(refreshResponse).ConfigureAwait(false);
                    AssertEqual(releaseId, refreshedRelease.Id);
                    AssertTrue(refreshedRelease.Artifacts.Count == 1, "Expected refreshed release to keep derived artifact metadata.");

                    HttpResponseMessage deleteReleaseResponse = await authClient.DeleteAsync("/api/v1/releases/" + releaseId).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.NoContent, deleteReleaseResponse.StatusCode);
                    releaseId = String.Empty;

                    HttpResponseMessage deletedReleaseResponse = await authClient.GetAsync("/api/v1/releases/" + refreshedRelease.Id).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.NotFound, deletedReleaseResponse.StatusCode);
                }
                finally
                {
                    if (!String.IsNullOrWhiteSpace(releaseId))
                    {
                        try { await authClient.DeleteAsync("/api/v1/releases/" + releaseId).ConfigureAwait(false); } catch { }
                    }
                    if (!String.IsNullOrWhiteSpace(checkRunId))
                    {
                        try { await authClient.DeleteAsync("/api/v1/check-runs/" + checkRunId).ConfigureAwait(false); } catch { }
                    }
                    if (!String.IsNullOrWhiteSpace(voyageId))
                    {
                        try { await authClient.DeleteAsync("/api/v1/voyages/" + voyageId).ConfigureAwait(false); } catch { }
                        try { await authClient.DeleteAsync("/api/v1/voyages/" + voyageId + "/purge").ConfigureAwait(false); } catch { }
                    }
                    if (!String.IsNullOrWhiteSpace(workflowProfileId))
                    {
                        try { await authClient.DeleteAsync("/api/v1/workflow-profiles/" + workflowProfileId).ConfigureAwait(false); } catch { }
                    }
                    if (!String.IsNullOrWhiteSpace(vesselId))
                    {
                        try { await authClient.DeleteAsync("/api/v1/vessels/" + vesselId).ConfigureAwait(false); } catch { }
                    }

                    try
                    {
                        if (Directory.Exists(workingDirectory))
                            Directory.Delete(workingDirectory, true);
                    }
                    catch
                    {
                    }
                }
            }));

            cases.Add(CaseAsync("create_without_auth_returns_401", "Releases_CreateWithoutAuthReturns401", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient unauthClient = fx.UnauthClient;

                HttpResponseMessage response = await unauthClient.PostAsync("/api/v1/releases",
                    JsonHelper.ToJsonContent(new
                    {
                        Title = "Unauthorized Release"
                    })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.Unauthorized, response.StatusCode);
            }));

            cases.Add(CaseAsync("encoded_query_decodes_once", "Releases_EncodedQueryDecodesOnce", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                string workingDirectory = TestTemp.NewDirectory("release-query");
                string token = "q" + Guid.NewGuid().ToString("N");
                string title = token + " %3A " + token + "+00:00 " + token + "%X";
                string vesselId = String.Empty;
                string releaseId = String.Empty;

                try
                {
                    HttpResponseMessage vesselResponse = await authClient.PostAsync("/api/v1/vessels",
                        JsonHelper.ToJsonContent(new
                        {
                            Name = "Release Query " + token,
                            RepoUrl = "file:///tmp/release-query-" + token + ".git",
                            LocalPath = workingDirectory,
                            WorkingDirectory = workingDirectory,
                            DefaultBranch = "main"
                        })).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.Created, vesselResponse.StatusCode);
                    Vessel vessel = await JsonHelper.DeserializeAsync<Vessel>(vesselResponse).ConfigureAwait(false);
                    vesselId = vessel.Id;

                    HttpResponseMessage createReleaseResponse = await authClient.PostAsync("/api/v1/releases",
                        JsonHelper.ToJsonContent(new
                        {
                            VesselId = vesselId,
                            Title = title,
                            Version = "1.2.3",
                            Status = ReleaseStatusEnum.Draft
                        })).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.Created, createReleaseResponse.StatusCode);
                    Release release = await JsonHelper.DeserializeAsync<Release>(createReleaseResponse).ConfigureAwait(false);
                    releaseId = release.Id;

                    string encodedInstant = "2026-09-14T00%3A00%3A00.000Z";
                    RawQueryResponse upperBound = await SendReleaseQueryAsync(
                        fx,
                        "pageSize=100&search=" + token + "&toUtc=" + encodedInstant).ConfigureAwait(false);
                    AssertEqual(200, upperBound.StatusCode);
                    AssertFalse(
                        ReleaseListed(upperBound.Body, releaseId),
                        "Encoded toUtc 2026-09-14T00:00:00.000Z must exclude a later release.");

                    RawQueryResponse once = await SendReleaseQueryAsync(
                        fx,
                        "pageSize=100&search=" + token + "%20%253A").ConfigureAwait(false);
                    AssertEqual(200, once.StatusCode);
                    AssertTrue(
                        ReleaseListed(once.Body, releaseId),
                        "A value that contains %25 must be decoded once and still match a percent sign.");

                    RawQueryResponse plus = await SendReleaseQueryAsync(
                        fx,
                        "pageSize=100&search=" + token + "+00:00").ConfigureAwait(false);
                    AssertEqual(200, plus.StatusCode);
                    AssertTrue(
                        ReleaseListed(plus.Body, releaseId),
                        "A literal plus in the query value must stay a plus.");

                    RawQueryResponse brokenEscape = await SendReleaseQueryAsync(
                        fx,
                        "pageSize=100&search=" + token + "%X").ConfigureAwait(false);
                    AssertEqual(200, brokenEscape.StatusCode);
                    AssertTrue(
                        ReleaseListed(brokenEscape.Body, releaseId),
                        "A lone percent sign must stay raw and the route must still answer.");

                    RawQueryResponse lowerBound = await SendReleaseQueryAsync(
                        fx,
                        "pageSize=100&search=" + token + "&fromUtc=" + encodedInstant).ConfigureAwait(false);
                    AssertEqual(200, lowerBound.StatusCode);
                    AssertTrue(
                        ReleaseListed(lowerBound.Body, releaseId),
                        "Encoded fromUtc 2026-09-14T00:00:00.000Z must include a later release.");
                }
                finally
                {
                    if (!String.IsNullOrWhiteSpace(releaseId))
                    {
                        try { await authClient.DeleteAsync("/api/v1/releases/" + releaseId).ConfigureAwait(false); } catch { }
                    }
                    if (!String.IsNullOrWhiteSpace(vesselId))
                    {
                        try { await authClient.DeleteAsync("/api/v1/vessels/" + vesselId).ConfigureAwait(false); } catch { }
                    }

                    try
                    {
                        if (Directory.Exists(workingDirectory))
                            Directory.Delete(workingDirectory, true);
                    }
                    catch
                    {
                    }
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Releases",
                cases: cases);
        }

        #endregion

        #region Private-Members

        private const string SuiteId = "E2E.Release";

        #endregion

        #region Private-Methods

        private static async Task<Voyage> CreateVoyageAsync(HttpClient authClient, string vesselId, string title, int missionCount)
        {
            List<object> missions = new List<object>();
            for (int i = 1; i <= missionCount; i++)
            {
                missions.Add(new
                {
                    Title = "Release Mission " + i,
                    Description = "Release mission " + i
                });
            }

            HttpResponseMessage response = await authClient.PostAsync("/api/v1/voyages",
                JsonHelper.ToJsonContent(new
                {
                    Title = title,
                    Description = "Release voyage description",
                    VesselId = vesselId,
                    Missions = missions
                })).ConfigureAwait(false);
            AssertEqual(HttpStatusCode.Created, response.StatusCode);
            return await JsonHelper.DeserializeAsync<Voyage>(response).ConfigureAwait(false);
        }

        private static string BuildEmitFileCommand(string relativePath)
        {
            return OperatingSystem.IsWindows()
                ? "type .\\" + relativePath.Replace('/', '\\')
                : "cat \"" + relativePath + "\"";
        }

        private static async Task<RawQueryResponse> SendReleaseQueryAsync(E2EServerFixture fx, string query)
        {
            string requestText = "GET /api/v1/releases?" + query + " HTTP/1.1\r\n"
                + "Host: 127.0.0.1:" + fx.RestPort + "\r\n"
                + "X-Api-Key: " + fx.ApiKey + "\r\n"
                + "Connection: close\r\n"
                + "\r\n";
            using (TcpClient client = new TcpClient())
            {
                await client.ConnectAsync(IPAddress.Loopback, fx.RestPort).ConfigureAwait(false);
                using (NetworkStream stream = client.GetStream())
                {
                    byte[] bytes = Encoding.ASCII.GetBytes(requestText);
                    await stream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
                    using (MemoryStream buffer = new MemoryStream())
                    {
                        byte[] chunk = new byte[8192];
                        int read = await stream.ReadAsync(chunk, 0, chunk.Length).ConfigureAwait(false);
                        while (read > 0)
                        {
                            buffer.Write(chunk, 0, read);
                            read = await stream.ReadAsync(chunk, 0, chunk.Length).ConfigureAwait(false);
                        }

                        string text = Encoding.UTF8.GetString(buffer.ToArray());
                        int headerEnd = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                        string header = headerEnd >= 0 ? text.Substring(0, headerEnd) : text;
                        string body = headerEnd >= 0 ? text.Substring(headerEnd + 4) : "";
                        body = DecodeHttpBody(header, body);
                        int statusCode = 0;
                        int firstSpace = header.IndexOf(' ');
                        if (firstSpace >= 0 && firstSpace + 1 < header.Length)
                        {
                            int secondSpace = header.IndexOf(' ', firstSpace + 1);
                            string statusText = secondSpace > firstSpace
                                ? header.Substring(firstSpace + 1, secondSpace - firstSpace - 1)
                                : header.Substring(firstSpace + 1);
                            Int32.TryParse(statusText, out statusCode);
                        }

                        return new RawQueryResponse
                        {
                            StatusCode = statusCode,
                            Body = body
                        };
                    }
                }
            }
        }

        private static string DecodeHttpBody(string header, string body)
        {
            if (header.IndexOf("Transfer-Encoding: chunked", StringComparison.OrdinalIgnoreCase) < 0)
                return body;

            StringBuilder decoded = new StringBuilder();
            int index = 0;
            while (index < body.Length)
            {
                int lineEnd = body.IndexOf("\r\n", index, StringComparison.Ordinal);
                if (lineEnd < 0) break;
                string sizeText = body.Substring(index, lineEnd - index);
                int semicolon = sizeText.IndexOf(';');
                if (semicolon >= 0) sizeText = sizeText.Substring(0, semicolon);
                int size;
                if (!Int32.TryParse(sizeText.Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out size))
                    break;
                if (size == 0) break;
                int dataStart = lineEnd + 2;
                if (dataStart + size > body.Length) break;
                decoded.Append(body.Substring(dataStart, size));
                index = dataStart + size + 2;
            }

            return decoded.ToString();
        }

        private static bool ReleaseListed(string body, string releaseId)
        {
            EnumerationResult<Release> page = JsonHelper.Deserialize<EnumerationResult<Release>>(body);
            if (page == null || page.Objects == null) return false;
            return page.Objects.Exists(current => current.Id == releaseId);
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        /// <summary>
        /// Status and body of one raw release-list request.
        /// </summary>
        private sealed class RawQueryResponse
        {
            /// <summary>
            /// HTTP status code, or zero when the status line cannot be read.
            /// </summary>
            public int StatusCode { get; set; }

            /// <summary>
            /// Response body.
            /// </summary>
            public string Body { get; set; } = "";
        }

        #endregion
    }
}

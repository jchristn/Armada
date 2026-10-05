namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using WatsonWebserver.Core;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// End-to-end Log API descriptors covering mission and captain log routes (content retrieval,
    /// line/offset pagination, pointer resolution, and auth behavior) against a live in-process
    /// Armada server provided by <see cref="E2EServerFixture"/>.
    /// </summary>
    public sealed class LogSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Log API end-to-end suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            #region Mission-Log-Tests

            cases.Add(CaseAsync("mission_log_not_found_returns_error", "MissionLog_NotFound_ReturnsError", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/missions/msn_nonexistent/log").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NotFound, response.StatusCode, "missing mission");
                ApiErrorProbe errorResp = ApiErrorProbe.From(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                AssertEqual(ApiResultEnum.NotFound, errorResp.Error, "typed error code");
            }));

            cases.Add(CaseAsync("mission_log_not_found_returns_not_found_status", "MissionLog_NotFound_ReturnsNotFoundStatus", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/missions/msn_doesnotexist/log").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NotFound, response.StatusCode, "not found status");
                ApiErrorProbe errorResp = ApiErrorProbe.From(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                AssertEqual(ApiResultEnum.NotFound, errorResp.Error, "typed error code");
                AssertEqual(404, errorResp.StatusCode, "status in the body");
                AssertFalse(String.IsNullOrEmpty(errorResp.Message), "a message accompanies the error");
            }));

            cases.Add(CaseAsync("mission_log_no_file_returns_empty", "MissionLog_NoFile_ReturnsEmpty", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                string missionId = await CreateMissionAsync(authClient, "NoLogFile").ConfigureAwait(false);

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/missions/" + missionId + "/log").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                MissionLogResponse logResp = await JsonHelper.DeserializeAsync<MissionLogResponse>(response).ConfigureAwait(false);
                AssertEqual("", logResp.Log);
                AssertEqual(0, logResp.Lines);
                AssertEqual(0, logResp.TotalLines);
            }));

            cases.Add(CaseAsync("mission_log_no_file_returns_mission_id", "MissionLog_NoFile_ReturnsMissionId", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                string missionId = await CreateMissionAsync(authClient, "NoLogFileId").ConfigureAwait(false);

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/missions/" + missionId + "/log").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                MissionLogResponse logResp = await JsonHelper.DeserializeAsync<MissionLogResponse>(response).ConfigureAwait(false);
                AssertEqual(missionId, logResp.MissionId);
            }));

            cases.Add(CaseAsync("mission_log_with_file_returns_content", "MissionLog_WithFile_ReturnsContent", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                string tempDir = fx.TempDir;

                string missionId = await CreateMissionAsync(authClient, "WithLog").ConfigureAwait(false);
                WriteMissionLog(tempDir, missionId, "line one\nline two\nline three");

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/missions/" + missionId + "/log").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                MissionLogResponse logResp = await JsonHelper.DeserializeAsync<MissionLogResponse>(response).ConfigureAwait(false);
                AssertEqual(3, logResp.TotalLines);
                AssertEqual(3, logResp.Lines);
                AssertLines(new List<string> { "line one", "line two", "line three" }, logResp.Log, "whole log");
            }));

            cases.Add(CaseAsync("mission_log_with_file_returns_mission_id", "MissionLog_WithFile_ReturnsMissionId", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                string tempDir = fx.TempDir;

                string missionId = await CreateMissionAsync(authClient, "WithLogId").ConfigureAwait(false);
                WriteMissionLog(tempDir, missionId, "some log output");

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/missions/" + missionId + "/log").ConfigureAwait(false);
                MissionLogResponse logResp = await JsonHelper.DeserializeAsync<MissionLogResponse>(response).ConfigureAwait(false);
                AssertEqual(missionId, logResp.MissionId);
            }));

            cases.Add(CaseAsync("mission_log_lines_param_limits_output", "MissionLog_LinesParam_LimitsOutput", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                string tempDir = fx.TempDir;

                string missionId = await CreateMissionAsync(authClient, "LinesLimit").ConfigureAwait(false);
                WriteMissionLogLines(tempDir, missionId, 50);

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/missions/" + missionId + "/log?lines=10").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                MissionLogResponse logResp = await JsonHelper.DeserializeAsync<MissionLogResponse>(response).ConfigureAwait(false);
                AssertEqual(10, logResp.Lines);
                AssertEqual(50, logResp.TotalLines);
            }));

            cases.Add(CaseAsync("mission_log_lines_param_returns_correct_content", "MissionLog_LinesParam_ReturnsCorrectContent", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                string tempDir = fx.TempDir;

                string missionId = await CreateMissionAsync(authClient, "LinesContent").ConfigureAwait(false);
                WriteMissionLogLines(tempDir, missionId, 50);

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/missions/" + missionId + "/log?lines=10").ConfigureAwait(false);
                MissionLogResponse logResp = await JsonHelper.DeserializeAsync<MissionLogResponse>(response).ConfigureAwait(false);
                string log = logResp.Log!;

                AssertLines(Numbered("log line ", 1, 10), log, "first 10 lines exactly");
            }));

            cases.Add(CaseAsync("mission_log_offset_param_skips_lines", "MissionLog_OffsetParam_SkipsLines", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                string tempDir = fx.TempDir;

                string missionId = await CreateMissionAsync(authClient, "OffsetTest").ConfigureAwait(false);
                WriteMissionLogLines(tempDir, missionId, 50);

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/missions/" + missionId + "/log?offset=40").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                MissionLogResponse logResp = await JsonHelper.DeserializeAsync<MissionLogResponse>(response).ConfigureAwait(false);
                string log = logResp.Log!;

                AssertLines(Numbered("log line ", 41, 50), log, "lines 41-50 exactly");
            }));

            cases.Add(CaseAsync("mission_log_offset_param_total_lines_unchanged", "MissionLog_OffsetParam_TotalLinesUnchanged", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                string tempDir = fx.TempDir;

                string missionId = await CreateMissionAsync(authClient, "OffsetTotal").ConfigureAwait(false);
                WriteMissionLogLines(tempDir, missionId, 50);

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/missions/" + missionId + "/log?offset=40").ConfigureAwait(false);
                MissionLogResponse logResp = await JsonHelper.DeserializeAsync<MissionLogResponse>(response).ConfigureAwait(false);
                AssertEqual(50, logResp.TotalLines);
                AssertEqual(10, logResp.Lines);
            }));

            cases.Add(CaseAsync("mission_log_lines_and_offset_combined", "MissionLog_LinesAndOffset_Combined", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                string tempDir = fx.TempDir;

                string missionId = await CreateMissionAsync(authClient, "CombinedTest").ConfigureAwait(false);
                WriteMissionLogLines(tempDir, missionId, 50);

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/missions/" + missionId + "/log?offset=10&lines=5").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                MissionLogResponse logResp = await JsonHelper.DeserializeAsync<MissionLogResponse>(response).ConfigureAwait(false);
                AssertEqual(5, logResp.Lines);
                AssertEqual(50, logResp.TotalLines);

                string log = logResp.Log!;
                AssertLines(Numbered("log line ", 11, 15), log, "lines 11-15 exactly");
            }));

            cases.Add(CaseAsync("mission_log_default_lines_returns_200", "MissionLog_DefaultLines_Returns200", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                string tempDir = fx.TempDir;

                string missionId = await CreateMissionAsync(authClient, "DefaultLines").ConfigureAwait(false);
                WriteMissionLogLines(tempDir, missionId, 300);

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/missions/" + missionId + "/log").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                MissionLogResponse logResp = await JsonHelper.DeserializeAsync<MissionLogResponse>(response).ConfigureAwait(false);
                AssertEqual(200, logResp.Lines);
                AssertEqual(300, logResp.TotalLines);
            }));

            cases.Add(CaseAsync("mission_log_default_lines_returns_first_200_lines", "MissionLog_DefaultLines_ReturnsFirst200Lines", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                string tempDir = fx.TempDir;

                string missionId = await CreateMissionAsync(authClient, "DefaultContent").ConfigureAwait(false);
                WriteMissionLogLines(tempDir, missionId, 300);

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/missions/" + missionId + "/log").ConfigureAwait(false);
                MissionLogResponse logResp = await JsonHelper.DeserializeAsync<MissionLogResponse>(response).ConfigureAwait(false);
                string log = logResp.Log!;

                AssertLines(Numbered("log line ", 1, 200), log, "first 200 lines exactly");
            }));

            cases.Add(CaseAsync("mission_log_very_large_offset_returns_empty", "MissionLog_VeryLargeOffset_ReturnsEmpty", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                string tempDir = fx.TempDir;

                string missionId = await CreateMissionAsync(authClient, "LargeOffset").ConfigureAwait(false);
                WriteMissionLogLines(tempDir, missionId, 10);

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/missions/" + missionId + "/log?offset=9999").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                MissionLogResponse logResp = await JsonHelper.DeserializeAsync<MissionLogResponse>(response).ConfigureAwait(false);
                AssertEqual("", logResp.Log);
                AssertEqual(0, logResp.Lines);
                AssertEqual(10, logResp.TotalLines);
            }));

            cases.Add(CaseAsync("mission_log_content_preserved_exact_line_content", "MissionLog_ContentPreserved_ExactLineContent", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                string tempDir = fx.TempDir;

                string missionId = await CreateMissionAsync(authClient, "PreserveContent").ConfigureAwait(false);
                string logContent = "first line with special chars: <>&\"\nsecond line with tabs:\t\there\nthird line";
                WriteMissionLog(tempDir, missionId, logContent);

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/missions/" + missionId + "/log").ConfigureAwait(false);
                MissionLogResponse logResp = await JsonHelper.DeserializeAsync<MissionLogResponse>(response).ConfigureAwait(false);
                string log = logResp.Log!;

                AssertLines(new List<string> { "first line with special chars: <>&\"", "second line with tabs:\t\there", "third line" }, log, "lines preserved exactly");
            }));

            cases.Add(CaseAsync("mission_log_total_lines_reflects_file_size_regardless_of_lines_param", "MissionLog_TotalLines_ReflectsFileSize_RegardlessOfLinesParam", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                string tempDir = fx.TempDir;

                string missionId = await CreateMissionAsync(authClient, "TotalLinesCheck").ConfigureAwait(false);
                WriteMissionLogLines(tempDir, missionId, 75);

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/missions/" + missionId + "/log?lines=5").ConfigureAwait(false);
                MissionLogResponse logResp = await JsonHelper.DeserializeAsync<MissionLogResponse>(response).ConfigureAwait(false);

                AssertEqual(5, logResp.Lines);
                AssertEqual(75, logResp.TotalLines);
            }));

            cases.Add(CaseAsync("mission_log_lines_exceeds_file_returns_all_available", "MissionLog_LinesExceedsFile_ReturnsAllAvailable", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                string tempDir = fx.TempDir;

                string missionId = await CreateMissionAsync(authClient, "ExceedLines").ConfigureAwait(false);
                WriteMissionLogLines(tempDir, missionId, 5);

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/missions/" + missionId + "/log?lines=100").ConfigureAwait(false);
                MissionLogResponse logResp = await JsonHelper.DeserializeAsync<MissionLogResponse>(response).ConfigureAwait(false);

                AssertEqual(5, logResp.Lines);
                AssertEqual(5, logResp.TotalLines);
            }));

            cases.Add(CaseAsync("mission_log_single_line_works", "MissionLog_SingleLine_Works", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                string tempDir = fx.TempDir;

                string missionId = await CreateMissionAsync(authClient, "SingleLine").ConfigureAwait(false);
                WriteMissionLog(tempDir, missionId, "only one line");

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/missions/" + missionId + "/log").ConfigureAwait(false);
                MissionLogResponse logResp = await JsonHelper.DeserializeAsync<MissionLogResponse>(response).ConfigureAwait(false);

                AssertEqual(1, logResp.TotalLines);
                AssertEqual(1, logResp.Lines);
                AssertEqual("only one line", logResp.Log);
            }));

            cases.Add(CaseAsync("mission_log_offset_at_exact_end_returns_empty", "MissionLog_OffsetAtExactEnd_ReturnsEmpty", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                string tempDir = fx.TempDir;

                string missionId = await CreateMissionAsync(authClient, "OffsetAtEnd").ConfigureAwait(false);
                WriteMissionLogLines(tempDir, missionId, 10);

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/missions/" + missionId + "/log?offset=10").ConfigureAwait(false);
                MissionLogResponse logResp = await JsonHelper.DeserializeAsync<MissionLogResponse>(response).ConfigureAwait(false);

                AssertEqual("", logResp.Log);
                AssertEqual(0, logResp.Lines);
                AssertEqual(10, logResp.TotalLines);
            }));

            cases.Add(CaseAsync("mission_log_offset_and_lines_at_boundary", "MissionLog_OffsetAndLinesAtBoundary", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                string tempDir = fx.TempDir;

                string missionId = await CreateMissionAsync(authClient, "Boundary").ConfigureAwait(false);
                WriteMissionLogLines(tempDir, missionId, 10);

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/missions/" + missionId + "/log?offset=8&lines=5").ConfigureAwait(false);
                MissionLogResponse logResp = await JsonHelper.DeserializeAsync<MissionLogResponse>(response).ConfigureAwait(false);

                AssertEqual(2, logResp.Lines);
                AssertEqual(10, logResp.TotalLines);
                string log = logResp.Log!;
                AssertLines(Numbered("log line ", 9, 10), log, "lines 9-10 exactly");
            }));

            cases.Add(CaseAsync("mission_log_response_is_json", "MissionLog_ResponseIsJson", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                string tempDir = fx.TempDir;

                string missionId = await CreateMissionAsync(authClient, "JsonCheck").ConfigureAwait(false);
                WriteMissionLogLines(tempDir, missionId, 3);

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/missions/" + missionId + "/log").ConfigureAwait(false);
                string contentType = response.Content.Headers.ContentType?.MediaType ?? "";
                AssertEqual("application/json", contentType);
            }));

            #endregion

            #region Captain-Log-Tests

            cases.Add(CaseAsync("captain_log_not_found_returns_error", "CaptainLog_NotFound_ReturnsError", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/captains/cpt_nonexistent/log").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NotFound, response.StatusCode, "missing captain");
                ApiErrorProbe errorResp = ApiErrorProbe.From(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                AssertEqual(ApiResultEnum.NotFound, errorResp.Error, "typed error code");
            }));

            cases.Add(CaseAsync("captain_log_not_found_message_indicates_not_found", "CaptainLog_NotFound_MessageIndicatesNotFound", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/captains/cpt_doesnotexist/log").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NotFound, response.StatusCode, "not found status");
                ApiErrorProbe errorResp = ApiErrorProbe.From(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                AssertEqual(ApiResultEnum.NotFound, errorResp.Error, "typed error code");
                AssertEqual(404, errorResp.StatusCode, "status in the body");
                AssertFalse(String.IsNullOrEmpty(errorResp.Message), "a message accompanies the error");
            }));

            cases.Add(CaseAsync("captain_log_no_pointer_or_file_returns_empty", "CaptainLog_NoPointerOrFile_ReturnsEmpty", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                string captainId = await CreateCaptainAsync(authClient, "no-log-captain").ConfigureAwait(false);

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/captains/" + captainId + "/log").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                CaptainLogResponse logResp = await JsonHelper.DeserializeAsync<CaptainLogResponse>(response).ConfigureAwait(false);
                AssertEqual("", logResp.Log);
                AssertEqual(0, logResp.Lines);
                AssertEqual(0, logResp.TotalLines);
            }));

            cases.Add(CaseAsync("captain_log_no_pointer_or_file_returns_captain_id", "CaptainLog_NoPointerOrFile_ReturnsCaptainId", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                string captainId = await CreateCaptainAsync(authClient, "no-log-captain-id").ConfigureAwait(false);

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/captains/" + captainId + "/log").ConfigureAwait(false);
                CaptainLogResponse logResp = await JsonHelper.DeserializeAsync<CaptainLogResponse>(response).ConfigureAwait(false);
                AssertEqual(captainId, logResp.CaptainId);
            }));

            cases.Add(CaseAsync("captain_log_pointer_to_missing_file_returns_empty", "CaptainLog_PointerToMissingFile_ReturnsEmpty", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                string tempDir = fx.TempDir;

                string captainId = await CreateCaptainAsync(authClient, "stale-pointer-captain").ConfigureAwait(false);

                WriteCaptainPointer(tempDir, captainId, "/nonexistent/path/to/log.log");

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/captains/" + captainId + "/log").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                CaptainLogResponse logResp = await JsonHelper.DeserializeAsync<CaptainLogResponse>(response).ConfigureAwait(false);
                AssertEqual("", logResp.Log);
                AssertEqual(0, logResp.Lines);
                AssertEqual(0, logResp.TotalLines);
            }));

            cases.Add(CaseAsync("captain_log_with_pointer_and_file_returns_content", "CaptainLog_WithPointerAndFile_ReturnsContent", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                string tempDir = fx.TempDir;

                string captainId = await CreateCaptainAsync(authClient, "log-captain").ConfigureAwait(false);

                string missionsLogDir = EnsureMissionLogDir(tempDir);
                string missionLogPath = Path.Combine(missionsLogDir, "msn_test_captain_log.log");
                await File.WriteAllTextAsync(missionLogPath, "captain output line 1\ncaptain output line 2").ConfigureAwait(false);

                WriteCaptainPointer(tempDir, captainId, missionLogPath);

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/captains/" + captainId + "/log").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                CaptainLogResponse logResp = await JsonHelper.DeserializeAsync<CaptainLogResponse>(response).ConfigureAwait(false);
                AssertEqual(2, logResp.TotalLines);
                AssertEqual(2, logResp.Lines);
                AssertLines(new List<string> { "captain output line 1", "captain output line 2" }, logResp.Log, "whole log");
            }));

            cases.Add(CaseAsync("captain_log_with_pointer_and_file_returns_captain_id", "CaptainLog_WithPointerAndFile_ReturnsCaptainId", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                string tempDir = fx.TempDir;

                string captainId = await CreateCaptainAsync(authClient, "log-captain-id").ConfigureAwait(false);

                string missionsLogDir = EnsureMissionLogDir(tempDir);
                string missionLogPath = Path.Combine(missionsLogDir, "msn_id_check.log");
                await File.WriteAllTextAsync(missionLogPath, "content").ConfigureAwait(false);

                WriteCaptainPointer(tempDir, captainId, missionLogPath);

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/captains/" + captainId + "/log").ConfigureAwait(false);
                CaptainLogResponse logResp = await JsonHelper.DeserializeAsync<CaptainLogResponse>(response).ConfigureAwait(false);
                AssertEqual(captainId, logResp.CaptainId);
            }));

            cases.Add(CaseAsync("captain_log_lines_param_works", "CaptainLog_LinesParam_Works", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                string tempDir = fx.TempDir;

                string captainId = await CreateCaptainAsync(authClient, "lines-captain").ConfigureAwait(false);

                string missionsLogDir = EnsureMissionLogDir(tempDir);
                string logPath = Path.Combine(missionsLogDir, "msn_captain_lines.log");
                string[] lines = Enumerable.Range(1, 30).Select(i => "captain line " + i).ToArray();
                await File.WriteAllLinesAsync(logPath, lines).ConfigureAwait(false);

                WriteCaptainPointer(tempDir, captainId, logPath);

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/captains/" + captainId + "/log?lines=5").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                CaptainLogResponse logResp = await JsonHelper.DeserializeAsync<CaptainLogResponse>(response).ConfigureAwait(false);
                AssertEqual(5, logResp.Lines);
                AssertEqual(30, logResp.TotalLines);
                string log = logResp.Log!;
                AssertLines(Numbered("captain line ", 1, 5), log, "first 5 lines exactly");
            }));

            cases.Add(CaseAsync("captain_log_offset_param_works", "CaptainLog_OffsetParam_Works", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                string tempDir = fx.TempDir;

                string captainId = await CreateCaptainAsync(authClient, "offset-captain").ConfigureAwait(false);

                string missionsLogDir = EnsureMissionLogDir(tempDir);
                string logPath = Path.Combine(missionsLogDir, "msn_captain_offset.log");
                string[] lines = Enumerable.Range(1, 20).Select(i => "captain line " + i).ToArray();
                await File.WriteAllLinesAsync(logPath, lines).ConfigureAwait(false);

                WriteCaptainPointer(tempDir, captainId, logPath);

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/captains/" + captainId + "/log?offset=15").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                CaptainLogResponse logResp = await JsonHelper.DeserializeAsync<CaptainLogResponse>(response).ConfigureAwait(false);
                AssertEqual(5, logResp.Lines);
                AssertEqual(20, logResp.TotalLines);
                string log = logResp.Log!;
                AssertLines(Numbered("captain line ", 16, 20), log, "lines 16-20 exactly");
            }));

            cases.Add(CaseAsync("captain_log_lines_and_offset_combined", "CaptainLog_LinesAndOffset_Combined", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                string tempDir = fx.TempDir;

                string captainId = await CreateCaptainAsync(authClient, "combined-captain").ConfigureAwait(false);

                string missionsLogDir = EnsureMissionLogDir(tempDir);
                string logPath = Path.Combine(missionsLogDir, "msn_captain_combined.log");
                string[] lines = Enumerable.Range(1, 30).Select(i => "captain line " + i).ToArray();
                await File.WriteAllLinesAsync(logPath, lines).ConfigureAwait(false);

                WriteCaptainPointer(tempDir, captainId, logPath);

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/captains/" + captainId + "/log?offset=10&lines=5").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                CaptainLogResponse logResp = await JsonHelper.DeserializeAsync<CaptainLogResponse>(response).ConfigureAwait(false);
                AssertEqual(5, logResp.Lines);
                AssertEqual(30, logResp.TotalLines);

                string log = logResp.Log!;
                AssertLines(Numbered("captain line ", 11, 15), log, "lines 11-15 exactly");
            }));

            cases.Add(CaseAsync("captain_log_pointer_with_trailing_whitespace_still_resolves", "CaptainLog_PointerWithTrailingWhitespace_StillResolves", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                string tempDir = fx.TempDir;

                string captainId = await CreateCaptainAsync(authClient, "whitespace-captain").ConfigureAwait(false);

                string missionsLogDir = EnsureMissionLogDir(tempDir);
                string logPath = Path.Combine(missionsLogDir, "msn_captain_whitespace.log");
                await File.WriteAllTextAsync(logPath, "whitespace test line 1\nwhitespace test line 2").ConfigureAwait(false);

                string captainLogDir = EnsureCaptainLogDir(tempDir);
                string pointerPath = Path.Combine(captainLogDir, captainId + ".current");
                await File.WriteAllTextAsync(pointerPath, logPath + "  \n  \r\n").ConfigureAwait(false);

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/captains/" + captainId + "/log").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                CaptainLogResponse logResp = await JsonHelper.DeserializeAsync<CaptainLogResponse>(response).ConfigureAwait(false);
                AssertEqual(2, logResp.TotalLines);
                AssertLines(new List<string> { "whitespace test line 1", "whitespace test line 2" }, logResp.Log, "whole log");
            }));

            cases.Add(CaseAsync("captain_log_default_lines_returns_50", "CaptainLog_DefaultLines_Returns50", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                string tempDir = fx.TempDir;

                string captainId = await CreateCaptainAsync(authClient, "default-lines-captain").ConfigureAwait(false);

                string missionsLogDir = EnsureMissionLogDir(tempDir);
                string logPath = Path.Combine(missionsLogDir, "msn_captain_default.log");
                string[] lines = Enumerable.Range(1, 150).Select(i => "captain line " + i).ToArray();
                await File.WriteAllLinesAsync(logPath, lines).ConfigureAwait(false);

                WriteCaptainPointer(tempDir, captainId, logPath);

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/captains/" + captainId + "/log").ConfigureAwait(false);
                CaptainLogResponse logResp = await JsonHelper.DeserializeAsync<CaptainLogResponse>(response).ConfigureAwait(false);

                AssertEqual(50, logResp.Lines);
                AssertEqual(150, logResp.TotalLines);
                AssertLines(Numbered("captain line ", 1, 50), logResp.Log, "first 50 lines exactly");
            }));

            cases.Add(CaseAsync("captain_log_large_offset_returns_empty", "CaptainLog_LargeOffset_ReturnsEmpty", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                string tempDir = fx.TempDir;

                string captainId = await CreateCaptainAsync(authClient, "large-offset-captain").ConfigureAwait(false);

                string missionsLogDir = EnsureMissionLogDir(tempDir);
                string logPath = Path.Combine(missionsLogDir, "msn_captain_large_offset.log");
                string[] lines = Enumerable.Range(1, 5).Select(i => "captain line " + i).ToArray();
                await File.WriteAllLinesAsync(logPath, lines).ConfigureAwait(false);

                WriteCaptainPointer(tempDir, captainId, logPath);

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/captains/" + captainId + "/log?offset=9999").ConfigureAwait(false);
                CaptainLogResponse logResp = await JsonHelper.DeserializeAsync<CaptainLogResponse>(response).ConfigureAwait(false);

                AssertEqual("", logResp.Log);
                AssertEqual(0, logResp.Lines);
                AssertEqual(5, logResp.TotalLines);
            }));

            cases.Add(CaseAsync("captain_log_response_is_json", "CaptainLog_ResponseIsJson", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                string captainId = await CreateCaptainAsync(authClient, "json-captain").ConfigureAwait(false);

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/captains/" + captainId + "/log").ConfigureAwait(false);
                string contentType = response.Content.Headers.ContentType?.MediaType ?? "";
                AssertEqual("application/json", contentType);
            }));

            #endregion

            #region Auth-Tests-For-Log-Endpoints

            cases.Add(CaseAsync("mission_log_without_auth_returns_response", "MissionLog_WithoutAuth_ReturnsResponse", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient unauthClient = fx.UnauthClient;

                HttpResponseMessage response = await unauthClient.GetAsync("/api/v1/missions/msn_any/log").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.Unauthorized, response.StatusCode, "unauthenticated log read is refused");
                ApiErrorProbe errorResp = ApiErrorProbe.From(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                AssertEqual(ApiResultEnum.NotAuthorized, errorResp.Error, "typed error code");
            }));

            cases.Add(CaseAsync("captain_log_without_auth_returns_response", "CaptainLog_WithoutAuth_ReturnsResponse", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient unauthClient = fx.UnauthClient;

                HttpResponseMessage response = await unauthClient.GetAsync("/api/v1/captains/cpt_any/log").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.Unauthorized, response.StatusCode, "unauthenticated log read is refused");
                ApiErrorProbe errorResp = ApiErrorProbe.From(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                AssertEqual(ApiResultEnum.NotAuthorized, errorResp.Error, "typed error code");
            }));

            #endregion

            return new TestSuiteDescriptor(
                suiteId: "E2E.Log",
                displayName: "Log Routes",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Creates a mission and returns its ID.
        /// </summary>
        private static async Task<string> CreateMissionAsync(HttpClient client, string title = "Test Mission")
        {
            StringContent content = JsonHelper.ToJsonContent(new { Title = title, Description = "Test description" });
            HttpResponseMessage resp = await client.PostAsync("/api/v1/missions", content).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            MissionCreateResponse wrapper = JsonHelper.Deserialize<MissionCreateResponse>(body);
            Mission mission;
            if (wrapper.Mission != null)
                mission = wrapper.Mission;
            else
                mission = JsonHelper.Deserialize<Mission>(body);

            return mission.Id!;
        }

        /// <summary>
        /// Creates a captain and returns its ID.
        /// </summary>
        private static async Task<string> CreateCaptainAsync(HttpClient client, string name = "test-captain")
        {
            StringContent content = JsonHelper.ToJsonContent(new { Name = name, Runtime = "ClaudeCode" });
            HttpResponseMessage resp = await client.PostAsync("/api/v1/captains", content).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            Captain captain = await JsonHelper.DeserializeAsync<Captain>(resp).ConfigureAwait(false);
            return captain.Id!;
        }

        /// <summary>
        /// Ensures the mission log directory exists under the server temp directory and returns its path.
        /// </summary>
        private static string EnsureMissionLogDir(string tempDir)
        {
            string dir = Path.Combine(tempDir, "logs", "missions");
            Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>
        /// Ensures the captain log directory exists under the server temp directory and returns its path.
        /// </summary>
        private static string EnsureCaptainLogDir(string tempDir)
        {
            string dir = Path.Combine(tempDir, "logs", "captains");
            Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>
        /// Writes the given content to a mission log file.
        /// </summary>
        private static void WriteMissionLog(string tempDir, string missionId, string logContent)
        {
            string dir = EnsureMissionLogDir(tempDir);
            File.WriteAllText(Path.Combine(dir, missionId + ".log"), logContent);
        }

        /// <summary>
        /// Writes the given number of numbered lines to a mission log file.
        /// </summary>
        private static void WriteMissionLogLines(string tempDir, string missionId, int lineCount)
        {
            string dir = EnsureMissionLogDir(tempDir);
            string[] lines = Enumerable.Range(1, lineCount).Select(i => "log line " + i).ToArray();
            File.WriteAllLines(Path.Combine(dir, missionId + ".log"), lines);
        }

        /// <summary>
        /// Writes a captain log pointer file targeting the given path.
        /// </summary>
        private static void WriteCaptainPointer(string tempDir, string captainId, string targetPath)
        {
            string dir = EnsureCaptainLogDir(tempDir);
            File.WriteAllText(Path.Combine(dir, captainId + ".current"), targetPath);
        }

        /// <summary>
        /// Split a returned log into its lines (the server joins with '\n').
        /// </summary>
        private static List<string> LogLines(string? log)
        {
            if (String.IsNullOrEmpty(log)) return new List<string>();
            return log.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        }

        /// <summary>
        /// The numbered lines first..last written by the helpers.
        /// </summary>
        private static List<string> Numbered(string prefix, int first, int last)
        {
            return Enumerable.Range(first, last - first + 1).Select(i => prefix + i).ToList();
        }

        /// <summary>
        /// Assert the log is exactly these lines in order.
        /// </summary>
        private static void AssertLines(List<string> expected, string? log, string label)
        {
            List<string> actual = LogLines(log);
            if (!expected.SequenceEqual(actual))
                throw new AssertionException(label + ": expected " + expected.Count + " lines [" + String.Join(" | ", expected.Take(5)) + (expected.Count > 5 ? " ..." : "") + "] but got " + actual.Count + " [" + String.Join(" | ", actual.Take(5)) + (actual.Count > 5 ? " ..." : "") + "]");
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: "E2E.Log",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion
    }
}

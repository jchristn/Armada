namespace Armada.Test.Automated.Suites
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Armada.Test.Common;

    /// <summary>
    /// Tests for mission and captain log routes.
    /// </summary>
    public class LogTests : TestSuite
    {
        #region Public-Members

        /// <summary>
        /// Name of this test suite.
        /// </summary>
        public override string Name => "Log Routes";

        #endregion

        #region Private-Members

        private HttpClient _AuthClient;
        private HttpClient _UnauthClient;
        private string _TempDir;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Create a new LogTests suite with shared HTTP clients and temp directory.
        /// </summary>
        public LogTests(HttpClient authClient, HttpClient unauthClient, string tempDir)
        {
            _AuthClient = authClient ?? throw new ArgumentNullException(nameof(authClient));
            _UnauthClient = unauthClient ?? throw new ArgumentNullException(nameof(unauthClient));
            _TempDir = tempDir ?? throw new ArgumentNullException(nameof(tempDir));
        }

        #endregion

        #region Private-Methods

        private async Task<string> CreateMissionAsync(string title = "Test Mission")
        {
            StringContent content = JsonHelper.ToJsonContent(new { Title = title, Description = "Test description" });
            HttpResponseMessage resp = await _AuthClient.PostAsync("/api/v1/missions", content).ConfigureAwait(false);
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

        private async Task<string> CreateCaptainAsync(string name = "test-captain")
        {
            StringContent content = JsonHelper.ToJsonContent(new { Name = name, Runtime = "ClaudeCode" });
            HttpResponseMessage resp = await _AuthClient.PostAsync("/api/v1/captains", content).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            Captain captain = await JsonHelper.DeserializeAsync<Captain>(resp).ConfigureAwait(false);
            return captain.Id!;
        }

        private string EnsureMissionLogDir()
        {
            string dir = Path.Combine(_TempDir, "logs", "missions");
            Directory.CreateDirectory(dir);
            return dir;
        }

        private string EnsureCaptainLogDir()
        {
            string dir = Path.Combine(_TempDir, "logs", "captains");
            Directory.CreateDirectory(dir);
            return dir;
        }

        private void WriteMissionLog(string missionId, string logContent)
        {
            string dir = EnsureMissionLogDir();
            File.WriteAllText(Path.Combine(dir, missionId + ".log"), logContent);
        }

        private void WriteMissionLogLines(string missionId, int lineCount)
        {
            string dir = EnsureMissionLogDir();
            string[] lines = Enumerable.Range(1, lineCount).Select(i => "log line " + i).ToArray();
            File.WriteAllLines(Path.Combine(dir, missionId + ".log"), lines);
        }

        private void WriteCaptainPointer(string captainId, string targetPath)
        {
            string dir = EnsureCaptainLogDir();
            File.WriteAllText(Path.Combine(dir, captainId + ".current"), targetPath);
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task RunTestsAsync()
        {
            #region Mission-Log-Tests

            await RunTest("MissionLog_NotFound_ReturnsError", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/missions/msn_nonexistent/log").ConfigureAwait(false);
                ArmadaErrorResponse errorResp = await JsonHelper.DeserializeAsync<ArmadaErrorResponse>(response).ConfigureAwait(false);
                Assert(
                    errorResp.Error != null ||
                    errorResp.Message != null,
                    "Not found should return error or message");
            }).ConfigureAwait(false);

            await RunTest("MissionLog_NotFound_ReturnsNotFoundStatus", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/missions/msn_doesnotexist/log").ConfigureAwait(false);
                ArmadaErrorResponse errorResp = await JsonHelper.DeserializeAsync<ArmadaErrorResponse>(response).ConfigureAwait(false);

                if (errorResp.Message != null)
                {
                    string msgStr = errorResp.Message;
                    Assert(msgStr.Contains("not found", StringComparison.OrdinalIgnoreCase),
                        "Expected message to contain 'not found'");
                }
            }).ConfigureAwait(false);

            await RunTest("MissionLog_NoFile_ReturnsEmpty", async () =>
            {
                string missionId = await CreateMissionAsync("NoLogFile").ConfigureAwait(false);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/missions/" + missionId + "/log").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                MissionLogResponse logResp = await JsonHelper.DeserializeAsync<MissionLogResponse>(response).ConfigureAwait(false);
                AssertEqual("", logResp.Log);
                AssertEqual(0, logResp.Lines);
                AssertEqual(0, logResp.TotalLines);
            }).ConfigureAwait(false);

            await RunTest("MissionLog_NoFile_ReturnsMissionId", async () =>
            {
                string missionId = await CreateMissionAsync("NoLogFileId").ConfigureAwait(false);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/missions/" + missionId + "/log").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                MissionLogResponse logResp = await JsonHelper.DeserializeAsync<MissionLogResponse>(response).ConfigureAwait(false);
                AssertEqual(missionId, logResp.MissionId);
            }).ConfigureAwait(false);

            await RunTest("MissionLog_WithFile_ReturnsContent", async () =>
            {
                string missionId = await CreateMissionAsync("WithLog").ConfigureAwait(false);
                WriteMissionLog(missionId, "line one\nline two\nline three");

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/missions/" + missionId + "/log").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                MissionLogResponse logResp = await JsonHelper.DeserializeAsync<MissionLogResponse>(response).ConfigureAwait(false);
                AssertEqual(3, logResp.TotalLines);
                AssertEqual(3, logResp.Lines);
                AssertContains("line one", logResp.Log!);
                AssertContains("line two", logResp.Log!);
                AssertContains("line three", logResp.Log!);
            }).ConfigureAwait(false);

            await RunTest("MissionLog_WithFile_ReturnsMissionId", async () =>
            {
                string missionId = await CreateMissionAsync("WithLogId").ConfigureAwait(false);
                WriteMissionLog(missionId, "some log output");

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/missions/" + missionId + "/log").ConfigureAwait(false);
                MissionLogResponse logResp = await JsonHelper.DeserializeAsync<MissionLogResponse>(response).ConfigureAwait(false);
                AssertEqual(missionId, logResp.MissionId);
            }).ConfigureAwait(false);

            await RunTest("MissionLog_LinesParam_LimitsOutput", async () =>
            {
                string missionId = await CreateMissionAsync("LinesLimit").ConfigureAwait(false);
                WriteMissionLogLines(missionId, 50);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/missions/" + missionId + "/log?lines=10").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                MissionLogResponse logResp = await JsonHelper.DeserializeAsync<MissionLogResponse>(response).ConfigureAwait(false);
                AssertEqual(10, logResp.Lines);
                AssertEqual(50, logResp.TotalLines);
            }).ConfigureAwait(false);

            await RunTest("MissionLog_LinesParam_ReturnsCorrectContent", async () =>
            {
                string missionId = await CreateMissionAsync("LinesContent").ConfigureAwait(false);
                WriteMissionLogLines(missionId, 50);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/missions/" + missionId + "/log?lines=10").ConfigureAwait(false);
                MissionLogResponse logResp = await JsonHelper.DeserializeAsync<MissionLogResponse>(response).ConfigureAwait(false);
                string log = logResp.Log!;

                AssertContains("log line 1", log);
                AssertContains("log line 10", log);
                AssertFalse(log.Contains("log line 11"), "Should not contain log line 11");
            }).ConfigureAwait(false);

            await RunTest("MissionLog_OffsetParam_SkipsLines", async () =>
            {
                string missionId = await CreateMissionAsync("OffsetTest").ConfigureAwait(false);
                WriteMissionLogLines(missionId, 50);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/missions/" + missionId + "/log?offset=40").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                MissionLogResponse logResp = await JsonHelper.DeserializeAsync<MissionLogResponse>(response).ConfigureAwait(false);
                string log = logResp.Log!;

                AssertContains("log line 41", log);
                AssertContains("log line 50", log);
                AssertFalse(log.Contains("log line 40\n"), "Should not contain log line 40");
                AssertFalse(log.Contains("log line 1\n"), "Should not contain log line 1");
            }).ConfigureAwait(false);

            await RunTest("MissionLog_OffsetParam_TotalLinesUnchanged", async () =>
            {
                string missionId = await CreateMissionAsync("OffsetTotal").ConfigureAwait(false);
                WriteMissionLogLines(missionId, 50);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/missions/" + missionId + "/log?offset=40").ConfigureAwait(false);
                MissionLogResponse logResp = await JsonHelper.DeserializeAsync<MissionLogResponse>(response).ConfigureAwait(false);
                AssertEqual(50, logResp.TotalLines);
                AssertEqual(10, logResp.Lines);
            }).ConfigureAwait(false);

            await RunTest("MissionLog_LinesAndOffset_Combined", async () =>
            {
                string missionId = await CreateMissionAsync("CombinedTest").ConfigureAwait(false);
                WriteMissionLogLines(missionId, 50);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/missions/" + missionId + "/log?offset=10&lines=5").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                MissionLogResponse logResp = await JsonHelper.DeserializeAsync<MissionLogResponse>(response).ConfigureAwait(false);
                AssertEqual(5, logResp.Lines);
                AssertEqual(50, logResp.TotalLines);

                string log = logResp.Log!;
                AssertContains("log line 11", log);
                AssertContains("log line 15", log);
                AssertFalse(log.Contains("log line 10\n"), "Should not contain log line 10");
                AssertFalse(log.Contains("log line 16"), "Should not contain log line 16");
            }).ConfigureAwait(false);

            await RunTest("MissionLog_DefaultLines_Returns200", async () =>
            {
                string missionId = await CreateMissionAsync("DefaultLines").ConfigureAwait(false);
                WriteMissionLogLines(missionId, 300);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/missions/" + missionId + "/log").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                MissionLogResponse logResp = await JsonHelper.DeserializeAsync<MissionLogResponse>(response).ConfigureAwait(false);
                AssertEqual(200, logResp.Lines);
                AssertEqual(300, logResp.TotalLines);
            }).ConfigureAwait(false);

            await RunTest("MissionLog_DefaultLines_ReturnsFirst200Lines", async () =>
            {
                string missionId = await CreateMissionAsync("DefaultContent").ConfigureAwait(false);
                WriteMissionLogLines(missionId, 300);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/missions/" + missionId + "/log").ConfigureAwait(false);
                MissionLogResponse logResp = await JsonHelper.DeserializeAsync<MissionLogResponse>(response).ConfigureAwait(false);
                string log = logResp.Log!;

                AssertContains("log line 1", log);
                AssertContains("log line 200", log);
                AssertFalse(log.Contains("log line 201"), "Should not contain log line 201");
            }).ConfigureAwait(false);

            await RunTest("MissionLog_VeryLargeOffset_ReturnsEmpty", async () =>
            {
                string missionId = await CreateMissionAsync("LargeOffset").ConfigureAwait(false);
                WriteMissionLogLines(missionId, 10);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/missions/" + missionId + "/log?offset=9999").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                MissionLogResponse logResp = await JsonHelper.DeserializeAsync<MissionLogResponse>(response).ConfigureAwait(false);
                AssertEqual("", logResp.Log);
                AssertEqual(0, logResp.Lines);
                AssertEqual(10, logResp.TotalLines);
            }).ConfigureAwait(false);

            await RunTest("MissionLog_ContentPreserved_ExactLineContent", async () =>
            {
                string missionId = await CreateMissionAsync("PreserveContent").ConfigureAwait(false);
                string logContent = "first line with special chars: <>&\"\nsecond line with tabs:\t\there\nthird line";
                WriteMissionLog(missionId, logContent);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/missions/" + missionId + "/log").ConfigureAwait(false);
                MissionLogResponse logResp = await JsonHelper.DeserializeAsync<MissionLogResponse>(response).ConfigureAwait(false);
                string log = logResp.Log!;

                AssertContains("first line with special chars: <>&\"", log);
                AssertContains("second line with tabs:", log);
                AssertContains("third line", log);
            }).ConfigureAwait(false);

            await RunTest("MissionLog_TotalLines_ReflectsFileSize_RegardlessOfLinesParam", async () =>
            {
                string missionId = await CreateMissionAsync("TotalLinesCheck").ConfigureAwait(false);
                WriteMissionLogLines(missionId, 75);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/missions/" + missionId + "/log?lines=5").ConfigureAwait(false);
                MissionLogResponse logResp = await JsonHelper.DeserializeAsync<MissionLogResponse>(response).ConfigureAwait(false);

                AssertEqual(5, logResp.Lines);
                AssertEqual(75, logResp.TotalLines);
            }).ConfigureAwait(false);

            await RunTest("MissionLog_LinesExceedsFile_ReturnsAllAvailable", async () =>
            {
                string missionId = await CreateMissionAsync("ExceedLines").ConfigureAwait(false);
                WriteMissionLogLines(missionId, 5);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/missions/" + missionId + "/log?lines=100").ConfigureAwait(false);
                MissionLogResponse logResp = await JsonHelper.DeserializeAsync<MissionLogResponse>(response).ConfigureAwait(false);

                AssertEqual(5, logResp.Lines);
                AssertEqual(5, logResp.TotalLines);
            }).ConfigureAwait(false);

            await RunTest("MissionLog_SingleLine_Works", async () =>
            {
                string missionId = await CreateMissionAsync("SingleLine").ConfigureAwait(false);
                WriteMissionLog(missionId, "only one line");

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/missions/" + missionId + "/log").ConfigureAwait(false);
                MissionLogResponse logResp = await JsonHelper.DeserializeAsync<MissionLogResponse>(response).ConfigureAwait(false);

                AssertEqual(1, logResp.TotalLines);
                AssertEqual(1, logResp.Lines);
                AssertEqual("only one line", logResp.Log);
            }).ConfigureAwait(false);

            await RunTest("MissionLog_OffsetAtExactEnd_ReturnsEmpty", async () =>
            {
                string missionId = await CreateMissionAsync("OffsetAtEnd").ConfigureAwait(false);
                WriteMissionLogLines(missionId, 10);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/missions/" + missionId + "/log?offset=10").ConfigureAwait(false);
                MissionLogResponse logResp = await JsonHelper.DeserializeAsync<MissionLogResponse>(response).ConfigureAwait(false);

                AssertEqual("", logResp.Log);
                AssertEqual(0, logResp.Lines);
                AssertEqual(10, logResp.TotalLines);
            }).ConfigureAwait(false);

            await RunTest("MissionLog_OffsetAndLinesAtBoundary", async () =>
            {
                string missionId = await CreateMissionAsync("Boundary").ConfigureAwait(false);
                WriteMissionLogLines(missionId, 10);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/missions/" + missionId + "/log?offset=8&lines=5").ConfigureAwait(false);
                MissionLogResponse logResp = await JsonHelper.DeserializeAsync<MissionLogResponse>(response).ConfigureAwait(false);

                AssertEqual(2, logResp.Lines);
                AssertEqual(10, logResp.TotalLines);
                string log = logResp.Log!;
                AssertContains("log line 9", log);
                AssertContains("log line 10", log);
            }).ConfigureAwait(false);

            await RunTest("MissionLog_ResponseIsJson", async () =>
            {
                string missionId = await CreateMissionAsync("JsonCheck").ConfigureAwait(false);
                WriteMissionLogLines(missionId, 3);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/missions/" + missionId + "/log").ConfigureAwait(false);
                string contentType = response.Content.Headers.ContentType?.MediaType ?? "";
                AssertEqual("application/json", contentType);
            }).ConfigureAwait(false);

            #endregion

            #region Captain-Log-Tests

            await RunTest("CaptainLog_NotFound_ReturnsError", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/captains/cpt_nonexistent/log").ConfigureAwait(false);
                ArmadaErrorResponse errorResp = await JsonHelper.DeserializeAsync<ArmadaErrorResponse>(response).ConfigureAwait(false);
                Assert(
                    errorResp.Error != null ||
                    errorResp.Message != null,
                    "Not found should return error or message");
            }).ConfigureAwait(false);

            await RunTest("CaptainLog_NotFound_MessageIndicatesNotFound", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/captains/cpt_doesnotexist/log").ConfigureAwait(false);
                ArmadaErrorResponse errorResp = await JsonHelper.DeserializeAsync<ArmadaErrorResponse>(response).ConfigureAwait(false);

                if (errorResp.Message != null)
                {
                    string msgStr = errorResp.Message;
                    Assert(msgStr.Contains("not found", StringComparison.OrdinalIgnoreCase),
                        "Expected message to contain 'not found'");
                }
            }).ConfigureAwait(false);

            await RunTest("CaptainLog_NoPointerOrFile_ReturnsEmpty", async () =>
            {
                string captainId = await CreateCaptainAsync("no-log-captain").ConfigureAwait(false);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/captains/" + captainId + "/log").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                CaptainLogResponse logResp = await JsonHelper.DeserializeAsync<CaptainLogResponse>(response).ConfigureAwait(false);
                AssertEqual("", logResp.Log);
                AssertEqual(0, logResp.Lines);
                AssertEqual(0, logResp.TotalLines);
            }).ConfigureAwait(false);

            await RunTest("CaptainLog_NoPointerOrFile_ReturnsCaptainId", async () =>
            {
                string captainId = await CreateCaptainAsync("no-log-captain-id").ConfigureAwait(false);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/captains/" + captainId + "/log").ConfigureAwait(false);
                CaptainLogResponse logResp = await JsonHelper.DeserializeAsync<CaptainLogResponse>(response).ConfigureAwait(false);
                AssertEqual(captainId, logResp.CaptainId);
            }).ConfigureAwait(false);

            await RunTest("CaptainLog_PointerToMissingFile_ReturnsEmpty", async () =>
            {
                string captainId = await CreateCaptainAsync("stale-pointer-captain").ConfigureAwait(false);

                WriteCaptainPointer(captainId, "/nonexistent/path/to/log.log");

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/captains/" + captainId + "/log").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                CaptainLogResponse logResp = await JsonHelper.DeserializeAsync<CaptainLogResponse>(response).ConfigureAwait(false);
                AssertEqual("", logResp.Log);
                AssertEqual(0, logResp.Lines);
                AssertEqual(0, logResp.TotalLines);
            }).ConfigureAwait(false);

            await RunTest("CaptainLog_WithPointerAndFile_ReturnsContent", async () =>
            {
                string captainId = await CreateCaptainAsync("log-captain").ConfigureAwait(false);

                string missionsLogDir = EnsureMissionLogDir();
                string missionLogPath = Path.Combine(missionsLogDir, "msn_test_captain_log.log");
                await File.WriteAllTextAsync(missionLogPath, "captain output line 1\ncaptain output line 2").ConfigureAwait(false);

                WriteCaptainPointer(captainId, missionLogPath);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/captains/" + captainId + "/log").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                CaptainLogResponse logResp = await JsonHelper.DeserializeAsync<CaptainLogResponse>(response).ConfigureAwait(false);
                AssertEqual(2, logResp.TotalLines);
                AssertEqual(2, logResp.Lines);
                AssertContains("captain output line 1", logResp.Log!);
                AssertContains("captain output line 2", logResp.Log!);
            }).ConfigureAwait(false);

            await RunTest("CaptainLog_WithPointerAndFile_ReturnsCaptainId", async () =>
            {
                string captainId = await CreateCaptainAsync("log-captain-id").ConfigureAwait(false);

                string missionsLogDir = EnsureMissionLogDir();
                string missionLogPath = Path.Combine(missionsLogDir, "msn_id_check.log");
                await File.WriteAllTextAsync(missionLogPath, "content").ConfigureAwait(false);

                WriteCaptainPointer(captainId, missionLogPath);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/captains/" + captainId + "/log").ConfigureAwait(false);
                CaptainLogResponse logResp = await JsonHelper.DeserializeAsync<CaptainLogResponse>(response).ConfigureAwait(false);
                AssertEqual(captainId, logResp.CaptainId);
            }).ConfigureAwait(false);

            await RunTest("CaptainLog_LinesParam_Works", async () =>
            {
                string captainId = await CreateCaptainAsync("lines-captain").ConfigureAwait(false);

                string missionsLogDir = EnsureMissionLogDir();
                string logPath = Path.Combine(missionsLogDir, "msn_captain_lines.log");
                string[] lines = Enumerable.Range(1, 30).Select(i => "captain line " + i).ToArray();
                await File.WriteAllLinesAsync(logPath, lines).ConfigureAwait(false);

                WriteCaptainPointer(captainId, logPath);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/captains/" + captainId + "/log?lines=5").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                CaptainLogResponse logResp = await JsonHelper.DeserializeAsync<CaptainLogResponse>(response).ConfigureAwait(false);
                AssertEqual(5, logResp.Lines);
                AssertEqual(30, logResp.TotalLines);
                string log = logResp.Log!;
                AssertContains("captain line 1", log);
                AssertContains("captain line 5", log);
                AssertFalse(log.Contains("captain line 6"), "Should not contain captain line 6");
            }).ConfigureAwait(false);

            await RunTest("CaptainLog_OffsetParam_Works", async () =>
            {
                string captainId = await CreateCaptainAsync("offset-captain").ConfigureAwait(false);

                string missionsLogDir = EnsureMissionLogDir();
                string logPath = Path.Combine(missionsLogDir, "msn_captain_offset.log");
                string[] lines = Enumerable.Range(1, 20).Select(i => "captain line " + i).ToArray();
                await File.WriteAllLinesAsync(logPath, lines).ConfigureAwait(false);

                WriteCaptainPointer(captainId, logPath);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/captains/" + captainId + "/log?offset=15").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                CaptainLogResponse logResp = await JsonHelper.DeserializeAsync<CaptainLogResponse>(response).ConfigureAwait(false);
                AssertEqual(5, logResp.Lines);
                AssertEqual(20, logResp.TotalLines);
                string log = logResp.Log!;
                AssertContains("captain line 16", log);
                AssertContains("captain line 20", log);
                AssertFalse(log.Contains("captain line 15\n"), "Should not contain captain line 15");
            }).ConfigureAwait(false);

            await RunTest("CaptainLog_LinesAndOffset_Combined", async () =>
            {
                string captainId = await CreateCaptainAsync("combined-captain").ConfigureAwait(false);

                string missionsLogDir = EnsureMissionLogDir();
                string logPath = Path.Combine(missionsLogDir, "msn_captain_combined.log");
                string[] lines = Enumerable.Range(1, 30).Select(i => "captain line " + i).ToArray();
                await File.WriteAllLinesAsync(logPath, lines).ConfigureAwait(false);

                WriteCaptainPointer(captainId, logPath);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/captains/" + captainId + "/log?offset=10&lines=5").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                CaptainLogResponse logResp = await JsonHelper.DeserializeAsync<CaptainLogResponse>(response).ConfigureAwait(false);
                AssertEqual(5, logResp.Lines);
                AssertEqual(30, logResp.TotalLines);

                string log = logResp.Log!;
                AssertContains("captain line 11", log);
                AssertContains("captain line 15", log);
                AssertFalse(log.Contains("captain line 10\n"), "Should not contain captain line 10");
                AssertFalse(log.Contains("captain line 16"), "Should not contain captain line 16");
            }).ConfigureAwait(false);

            await RunTest("CaptainLog_PointerWithTrailingWhitespace_StillResolves", async () =>
            {
                string captainId = await CreateCaptainAsync("whitespace-captain").ConfigureAwait(false);

                string missionsLogDir = EnsureMissionLogDir();
                string logPath = Path.Combine(missionsLogDir, "msn_captain_whitespace.log");
                await File.WriteAllTextAsync(logPath, "whitespace test line 1\nwhitespace test line 2").ConfigureAwait(false);

                string captainLogDir = EnsureCaptainLogDir();
                string pointerPath = Path.Combine(captainLogDir, captainId + ".current");
                await File.WriteAllTextAsync(pointerPath, logPath + "  \n  \r\n").ConfigureAwait(false);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/captains/" + captainId + "/log").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                CaptainLogResponse logResp = await JsonHelper.DeserializeAsync<CaptainLogResponse>(response).ConfigureAwait(false);
                AssertEqual(2, logResp.TotalLines);
                AssertContains("whitespace test line 1", logResp.Log!);
            }).ConfigureAwait(false);

            await RunTest("CaptainLog_DefaultLines_Returns50", async () =>
            {
                string captainId = await CreateCaptainAsync("default-lines-captain").ConfigureAwait(false);

                string missionsLogDir = EnsureMissionLogDir();
                string logPath = Path.Combine(missionsLogDir, "msn_captain_default.log");
                string[] lines = Enumerable.Range(1, 150).Select(i => "captain line " + i).ToArray();
                await File.WriteAllLinesAsync(logPath, lines).ConfigureAwait(false);

                WriteCaptainPointer(captainId, logPath);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/captains/" + captainId + "/log").ConfigureAwait(false);
                CaptainLogResponse logResp = await JsonHelper.DeserializeAsync<CaptainLogResponse>(response).ConfigureAwait(false);

                AssertEqual(50, logResp.Lines);
                AssertEqual(150, logResp.TotalLines);
            }).ConfigureAwait(false);

            await RunTest("CaptainLog_LargeOffset_ReturnsEmpty", async () =>
            {
                string captainId = await CreateCaptainAsync("large-offset-captain").ConfigureAwait(false);

                string missionsLogDir = EnsureMissionLogDir();
                string logPath = Path.Combine(missionsLogDir, "msn_captain_large_offset.log");
                string[] lines = Enumerable.Range(1, 5).Select(i => "captain line " + i).ToArray();
                await File.WriteAllLinesAsync(logPath, lines).ConfigureAwait(false);

                WriteCaptainPointer(captainId, logPath);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/captains/" + captainId + "/log?offset=9999").ConfigureAwait(false);
                CaptainLogResponse logResp = await JsonHelper.DeserializeAsync<CaptainLogResponse>(response).ConfigureAwait(false);

                AssertEqual("", logResp.Log);
                AssertEqual(0, logResp.Lines);
                AssertEqual(5, logResp.TotalLines);
            }).ConfigureAwait(false);

            await RunTest("CaptainLog_ResponseIsJson", async () =>
            {
                string captainId = await CreateCaptainAsync("json-captain").ConfigureAwait(false);

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/captains/" + captainId + "/log").ConfigureAwait(false);
                string contentType = response.Content.Headers.ContentType?.MediaType ?? "";
                AssertEqual("application/json", contentType);
            }).ConfigureAwait(false);

            #endregion

            #region Auth-Tests-For-Log-Endpoints

            await RunTest("MissionLog_WithoutAuth_ReturnsResponse", async () =>
            {
                HttpResponseMessage response = await _UnauthClient.GetAsync("/api/v1/missions/msn_any/log").ConfigureAwait(false);
                AssertNotNull(response);
            }).ConfigureAwait(false);

            await RunTest("CaptainLog_WithoutAuth_ReturnsResponse", async () =>
            {
                HttpResponseMessage response = await _UnauthClient.GetAsync("/api/v1/captains/cpt_any/log").ConfigureAwait(false);
                AssertNotNull(response);
            }).ConfigureAwait(false);

            #endregion
        }

        #endregion
    }
}

namespace Test.Shared.Suites.Tui.ActivitySystem
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Threading;
    using Armada.Tui.Screens;
    using Armada.Tui.Screens.Admin;
    using Armada.Tui.Widgets;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Headless flows for the Settings, Server tab against a stubbed server: sections and values, per-group save
    /// with Ctrl+S, validation, retention, backup to a file, restore from a file, rebuild log polling, confirmed
    /// server actions, proxy-mode blocking, and tenant-admin gating.
    /// </summary>
    public sealed class TuiServerSettingsSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.System.Server";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "renders_sections", "Every section renders with values from the settings stub", () =>
            {
                StubHttpHandler stub = Stub();
                using (TuiTestHost host = Open(stub, out ServerSettingsScreen screen))
                {
                    string frame = host.Screen();
                    TuiScreenDump.Write("server-settings", frame);
                    TuiCase.Contains(frame, "Server Settings", "title");
                    TuiCase.Contains(frame, "Healthy", "health card");
                    TuiCase.Contains(frame, "Online (HTTP)", "connection card");
                    TuiCase.Contains(frame, "0.9.0", "version");
                    List<string> sections = screen.Form.Rows.Where(r => r.IsSection).Select(r => r.Label).ToList();
                    foreach (string s in new[] { "Server Configuration", "Rebuild Armada", "Agent Settings", "Planning Session Settings", "Repository Health", "Scored criteria", "Thresholds", "Vessel Import", "Fleet Actions", "Data Retention", "Remote Control", "MCP Configuration", "System Paths", "Database Backup", "Server Actions" })
                    {
                        AssertTrue(sections.Contains(s), "section " + s);
                    }

                    AssertEqual("7890", screen.AdmiralPort.Value, "admiral port");
                    AssertEqual("12", screen.MaxCaptains.Value, "max captains");
                    AssertEqual("vsl_self", screen.SelfVessel.Value, "self vessel");
                    AssertTrue(screen.AutoCreatePr.Value, "auto pr");
                    AssertEqual("360", screen.HealthFields["intervalMinutes"].Value, "rh interval");
                    AssertTrue(screen.Criteria[Armada.Core.Enums.VesselHealthCriterionEnum.Branches].Value, "criterion on");
                    AssertFalse(screen.Criteria[Armada.Core.Enums.VesselHealthCriterionEnum.CommitRecency].Value, "criterion off");
                    AssertEqual("/src\n/work", screen.AllowedRoots.Value, "roots");
                    AssertEqual("8", screen.FleetMaxConcurrency.Value, "fleet");
                    AssertEqual("45", screen.RetentionJobs.Value, "retention jobs");
                    AssertEqual("https://proxy.example/tunnel", screen.TunnelUrl.Value, "tunnel url");
                    AssertTrue(screen.ProxyPassword.Masked, "password masked");
                    AssertEqual("/data/armada", screen.Values["Data Directory"].Text, "data dir");
                    TuiCase.Contains(screen.Values["mcp.claude.http"].Text, "http://localhost:7891/rpc", "mcp http");
                    AssertEqual("claude mcp add --scope user armada -- armada mcp stdio", screen.Values["mcp.claude.stdio"].Text, "mcp stdio");
                    foreach (ServerSettingsGroup g in screen.Groups.Values) AssertFalse(g.IsDirty, "clean after load: " + g.Key);
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "save_with_ctrl_s", "Editing a number and Ctrl+S saves only that group", () =>
            {
                StubHttpHandler stub = Stub();
                using (TuiTestHost host = Open(stub, out ServerSettingsScreen screen))
                {
                    screen.Form.Scope.Focus(screen.MaxCaptains);
                    host.Press("ctrl+u").Type("20").Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => stub.Count("PUT /api/v1/settings") == 1), "PUT sent");
                    string body = stub.Bodies.Last(b => b.Contains("MaxCaptains"));
                    AssertTrue(body.Contains("\"MaxCaptains\":20"), "new value: " + body);
                    AssertFalse(body.Contains("Retention\""), "only the group: " + body);
                    AssertTrue(host.WaitForText("Server configuration saved"), "toast");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "validation_blocks_save", "Out-of-range values and fail below warn block saving with the dashboard messages", () =>
            {
                StubHttpHandler stub = Stub();
                using (TuiTestHost host = Open(stub, out ServerSettingsScreen screen))
                {
                    screen.Form.Scope.Focus(screen.ImportMaxDepth);
                    host.Press("ctrl+u").Type("99");
                    AssertEqual("Must be a whole number from 1 to 16.", screen.ImportMaxDepth.FieldError, "range message");
                    TuiCase.Contains(host.Screen(), "Must be a whole number from 1 to 16.", "message shown");
                    host.Press("ctrl+s");
                    host.Pump();
                    AssertEqual(0, stub.Count("PUT /api/v1/settings"), "blocked");
                    screen.HealthFields["thresholds.behindWarn"].Value = "10";
                    screen.HealthFields["thresholds.behindFail"].Value = "5";
                    screen.Form.Scope.Focus(screen.HealthFields["thresholds.behindFail"]);
                    host.Screen();
                    TuiCase.Contains(host.Screen(), "Behind: fail at must be at least the warn value.", "cross message");
                    AssertFalse(screen.Groups["repositoryHealth"].CanSave(), "rh blocked");
                    host.Press("ctrl+s");
                    host.Pump();
                    AssertEqual(0, stub.Count("PUT /api/v1/settings"), "still blocked");
                    screen.HealthFields["intervalMinutes"].Value = "abc";
                    AssertEqual("Enter a whole number.", screen.HealthFields["intervalMinutes"].FieldError, "rh integer message");
                    screen.ConnectTimeout.Value = "1";
                    AssertEqual("Must be a whole number from 5 to 300.", screen.ConnectTimeout.FieldError, "remote range");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "retention_save", "Data Retention saves its group only when changed", () =>
            {
                StubHttpHandler stub = Stub();
                using (TuiTestHost host = Open(stub, out ServerSettingsScreen screen))
                {
                    AssertFalse(screen.Groups["retention"].CanSave(), "clean group cannot save");
                    screen.Form.Scope.Focus(screen.RetentionJobs);
                    host.Press("ctrl+u").Type("60");
                    AssertTrue(screen.Groups["retention"].IsDirty, "dirty");
                    TuiCase.Contains(host.Screen(), "Unsaved changes", "dirty marker");
                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => stub.Count("PUT /api/v1/settings") == 1), "PUT sent");
                    string body = stub.Bodies.Last(b => b.Contains("Retention"));
                    AssertTrue(body.Contains("\"JobRetentionDays\":60") && body.Contains("\"AskThreadArchiveAfterDays\":90"), "retention body: " + body);
                    AssertTrue(host.WaitForText("Retention settings saved and applied."), "toast");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "backup_to_file", "Backup Now saves the server's ZIP to the chosen path", () =>
            {
                StubHttpHandler stub = Stub();
                byte[] zip = new byte[] { 0x50, 0x4b, 0x03, 0x04, 1, 2, 3 };
                stub.On("GET", "/api/v1/backup", b =>
                {
                    HttpResponseMessage r = new HttpResponseMessage(HttpStatusCode.OK);
                    r.Content = new ByteArrayContent(zip);
                    r.Content.Headers.ContentDisposition = new System.Net.Http.Headers.ContentDispositionHeaderValue("attachment") { FileName = "armada-backup-test.zip" };
                    return r;
                });
                string dir = Path.Combine(Path.GetTempPath(), "armada-tui-backup-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                string target = Path.Combine(dir, "saved.zip");
                try
                {
                    using (TuiTestHost host = Open(stub, out ServerSettingsScreen screen))
                    {
                        screen.BackupButtons!.Buttons[0].Press();
                        AssertTrue(host.WaitForText("File path"), "path prompt");
                        TuiCase.Contains(host.Screen(), "armada-backup-test.zip", "server file name suggested");
                        host.Press("ctrl+u").Type(target).Press("ctrl+s");
                        AssertTrue(host.PumpUntil(() => File.Exists(target)), "file written");
                        AssertTrue(File.ReadAllBytes(target).SequenceEqual(zip), "bytes");
                        AssertTrue(host.WaitForText("Backup saved to"), "toast");
                    }
                }
                finally
                {
                    try { Directory.Delete(dir, true); } catch (Exception) { }
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "restore_from_file", "Restore reads the chosen file, confirms, then posts it", () =>
            {
                StubHttpHandler stub = Stub();
                stub.Json("POST", "/api/v1/restore", "{\"Success\":true}");
                string file = Path.Combine(Path.GetTempPath(), "armada-tui-restore-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".zip");
                File.WriteAllText(file, "ZIPDATA");
                try
                {
                    using (TuiTestHost host = Open(stub, out ServerSettingsScreen screen))
                    {
                        screen.BackupButtons!.Buttons[1].Press();
                        AssertTrue(host.WaitForText("File path"), "open prompt");
                        host.Type(file).Press("ctrl+s");
                        AssertTrue(host.WaitForText("Restore the database from"), "confirm");
                        AssertEqual(0, stub.Count("POST /api/v1/restore"), "not before confirm");
                        host.Press("y");
                        AssertTrue(host.PumpUntil(() => stub.Count("POST /api/v1/restore") == 1), "posted");
                        AssertTrue(stub.Bodies.Any(b => b == "ZIPDATA"), "file bytes posted");
                        AssertTrue(host.WaitForText("Restore completed successfully."), "toast");
                    }
                }
                finally
                {
                    try { File.Delete(file); } catch (Exception) { }
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "rebuild_log", "Rebuild confirms, shows the build log, and stops polling when done", () =>
            {
                StubHttpHandler stub = Stub();
                int polls = 0;
                stub.Json("POST", "/api/v1/server/rebuild", "{\"Status\":\"Building\",\"Slot\":\"slot-2\",\"Log\":\"restore packages\\n\"}");
                stub.On("GET", "/api/v1/server/rebuild/status", b =>
                {
                    int n = Interlocked.Increment(ref polls);
                    string json = n < 3
                        ? "{\"Status\":\"Building\",\"Slot\":\"slot-2\",\"Log\":\"restore packages\\ncompiling\\n\"}"
                        : "{\"Status\":\"Succeeded\",\"Slot\":\"slot-2\",\"PreviousSlot\":\"slot-1\",\"Log\":\"restore packages\\ncompiling\\npublished slot-2\\n\"}";
                    return StubHttpHandler.Response(HttpStatusCode.OK, json);
                });
                using (TuiTestHost host = Open(stub, out ServerSettingsScreen screen))
                {
                    screen.RebuildPollIntervalMs = 30;
                    AssertTrue(host.PumpUntil(() => screen.BranchPicker.Options.Count == 3), "branches loaded: " + String.Join(",", stub.Requests));
                    AssertEqual("main", screen.BuildRef.Value, "default branch preselected as ref");
                    screen.ActionRows[1].Buttons[0].Press();
                    AssertTrue(host.WaitForText("Rebuild the Admiral from source"), "confirm");
                    host.Press("y");
                    AssertTrue(host.PumpUntil(() => stub.Count("POST /api/v1/server/rebuild") == 1), "rebuild posted");
                    AssertTrue(stub.Bodies.Any(b => b.Contains("\"Ref\":\"main\"")), "ref sent");
                    AssertTrue(host.WaitForText("published slot-2", 5000), "final log shown");
                    AssertTrue(host.PumpUntil(() => !screen.RebuildPolling), "polling stopped");
                    int after = polls;
                    Thread.Sleep(150);
                    host.Pump();
                    AssertEqual(after, polls, "no polls after done");
                    TuiScreenDump.Write("server-rebuild-log", host.Screen());
                    host.Press("esc");
                    AssertTrue(screen.ActionRows[1].Buttons[2].Visible, "Roll Back offered after success");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "actions_confirm", "Restart, stop, and factory reset call the server only after confirming", () =>
            {
                StubHttpHandler stub = Stub();
                stub.Json("POST", "/api/v1/server/restart", "{}");
                stub.Json("POST", "/api/v1/server/stop", "{}");
                stub.Json("POST", "/api/v1/server/reset", "{}");
                using (TuiTestHost host = Open(stub, out ServerSettingsScreen screen))
                {
                    screen.ActionRows[0].Buttons[2].Press();
                    AssertTrue(host.WaitForText("Restart the Admiral server?"), "restart confirm");
                    host.Press("n");
                    host.Pump();
                    AssertEqual(0, stub.Count("POST /api/v1/server/restart"), "cancelled");
                    screen.ActionRows[0].Buttons[2].Press();
                    AssertTrue(host.WaitForText("Restart the Admiral server?"), "restart confirm again");
                    host.Press("y");
                    AssertTrue(host.PumpUntil(() => stub.Count("POST /api/v1/server/restart") == 1), "restart sent");

                    screen.ActionRows[2].Buttons[0].Press();
                    AssertTrue(host.WaitForText("This will shut down everything."), "stop confirm");
                    AssertEqual(0, stub.Count("POST /api/v1/server/stop"), "stop waits");
                    host.Press("y");
                    AssertTrue(host.PumpUntil(() => stub.Count("POST /api/v1/server/stop") == 1), "stop sent");

                    screen.ActionRows[2].Buttons[1].Press();
                    AssertTrue(host.WaitForText("Factory reset will delete ALL data"), "reset confirm");
                    AssertEqual(0, stub.Count("POST /api/v1/server/reset"), "reset waits");
                    host.Press("esc");
                    host.Pump();
                    AssertEqual(0, stub.Count("POST /api/v1/server/reset"), "reset cancelled");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "proxy_mode_blocks", "Through Armada.Proxy, local-only actions and settings are blocked", () =>
            {
                StubHttpHandler stub = Stub();
                stub.Json("GET", "/proxy-api/v1/session/context", "{\"IsAuthenticated\":true,\"SelectedInstanceId\":\"inst_remote\"}");
                stub.Json("POST", "/api/v1/server/restart", "{}");
                using (TuiTestHost host = Open(stub, out ServerSettingsScreen screen))
                {
                    AssertTrue(host.PumpUntil(() => screen.ProxyMode), "proxy mode");
                    AssertTrue(host.WaitForText("connected through Armada.Proxy for inst_remote"), "banner");
                    host.Screen();
                    AssertFalse(screen.ActionRows[0].Buttons[2].Enabled, "restart disabled");
                    AssertFalse(screen.BackupButtons!.Buttons[1].Enabled, "restore disabled");
                    AssertTrue(screen.BackupButtons!.Buttons[0].Enabled, "backup allowed");
                    AssertTrue(screen.ActionRows[0].Buttons[1].Enabled, "health check allowed");
                    screen.ActionRows[0].Buttons[2].Press();
                    host.Pump();
                    AssertEqual(0, stub.Count("POST /api/v1/server/restart"), "no restart");
                    AssertFalse(screen.AdmiralPort.CanFocus, "settings locked");
                    AssertFalse(screen.Groups["server"].CanSave(), "save blocked");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "tenant_admin_gating", "A tenant admin does not get backup, server actions, or repository health editing", () =>
            {
                StubHttpHandler stub = Stub();
                stub.On("GET", "/api/v1/whoami", b => StubHttpHandler.Response(HttpStatusCode.OK,
                    "{\"Tenant\":{\"Id\":\"ten_default\",\"Name\":\"Default Tenant\",\"Active\":true},\"User\":{\"Id\":\"usr_ta\",\"TenantId\":\"ten_default\",\"Email\":\"ta@armada\",\"IsAdmin\":false,\"IsTenantAdmin\":true,\"Active\":true}}"));
                using (TuiTestHost host = Open(stub, out ServerSettingsScreen screen))
                {
                    AssertNull(screen.BackupButtons, "no backup");
                    AssertEqual(0, screen.ActionRows.Count, "no server actions");
                    List<string> sections = screen.Form.Rows.Where(r => r.IsSection).Select(r => r.Label).ToList();
                    AssertFalse(sections.Contains("Database Backup"), "backup section hidden");
                    AssertFalse(sections.Contains("Server Actions"), "actions section hidden");
                    AssertTrue(screen.Form.Rows.Any(r => r.Field is TextBlock tb && tb.Text == "Only administrators can change these settings."), "rh note");
                    AssertFalse(screen.HealthFields["intervalMinutes"].CanFocus, "rh read-only");
                    AssertFalse(screen.Groups["repositoryHealth"].CanSave(), "rh save blocked");
                    AssertTrue(screen.Groups["server"].Editable, "other groups editable");
                }
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI Settings: Server tab", cases: cases);
        }

        private static TuiTestHost Open(StubHttpHandler stub, out ServerSettingsScreen screen)
        {
            TuiTestHost host = TuiCase.SignedIn(160, 50, "/server?tab=server", stub);
            ServerSettingsScreen? found = null;
            host.PumpUntil(() =>
            {
                ScreenBase? s = host.Tui.Shell.Screen;
                if (s is HubScreen hub) s = hub.Content;
                found = s as ServerSettingsScreen;
                return found != null && found.Settings != null && found.Health != null;
            }, 5000);
            if (found == null) throw new AssertionException("ServerSettingsScreen did not open");
            host.PumpUntil(() => found.SelfVessel.Options.Count > 1, 2000);
            host.Pump();
            screen = found;
            return host;
        }

        private static StubHttpHandler Stub()
        {
            StubHttpHandler stub = TuiFixtures.SignedInServer();
            stub.Json("GET", "/api/v1/status/health", "{\"Status\":\"healthy\",\"Version\":\"0.9.0\",\"Uptime\":\"1.02:03:04\",\"Timestamp\":\"2026-10-04T10:00:00Z\",\"StartUtc\":\"2026-10-03T08:00:00Z\",\"Ports\":{\"Admiral\":7890,\"Mcp\":7891},\"RemoteTunnel\":{\"Enabled\":false,\"State\":\"Disabled\"}}");
            string settings = "{\"AdmiralPort\":7890,\"McpPort\":7891,\"MaxCaptains\":12,\"HeartbeatIntervalSeconds\":30,\"StallThresholdMinutes\":10,\"IdleCaptainTimeoutSeconds\":0,"
                + "\"PlanningSessionInactivityTimeoutMinutes\":30,\"PlanningSessionAbandonmentTimeoutMinutes\":240,\"PlanningSessionRetentionDays\":14,\"AutoCreatePr\":true,"
                + "\"DataDirectory\":\"/data/armada\",\"DatabasePath\":\"/data/armada/armada.db\",\"LogDirectory\":\"/data/armada/logs\",\"DocksDirectory\":\"/data/armada/docks\",\"ReposDirectory\":\"/data/armada/repos\","
                + "\"SelfVesselId\":\"vsl_self\",\"RebuildSlotRetentionCount\":3,"
                + "\"RemoteControl\":{\"Enabled\":false,\"TunnelUrl\":\"https://proxy.example/tunnel\",\"Password\":\"armadaadmin\",\"ConnectTimeoutSeconds\":15,\"HeartbeatIntervalSeconds\":30,\"ReconnectBaseDelaySeconds\":5,\"ReconnectMaxDelaySeconds\":60,\"AllowInvalidCertificates\":false},"
                + "\"Import\":{\"AllowedRoots\":[\"/src\",\"/work\"],\"MaxDepth\":6,\"ExcludedDirectoryNames\":[\"bin\",\"obj\"],\"InlineBatchLimit\":25,\"CategorizationTimeoutMinutes\":20},"
                + "\"FleetActions\":{\"MaxConcurrency\":8,\"DefaultTimeoutSeconds\":300,\"MaxOutputBytes\":65536,\"RunRetentionDays\":30},"
                + "\"RepositoryHealth\":{\"IntervalMinutes\":360,\"MaxConcurrency\":4,\"FetchBeforeEvaluate\":true,\"DependencyMaxAgeHours\":24,\"DependencyCommandTimeoutSeconds\":120,\"StaleBranchDays\":90,\"MissionWindowDays\":7,"
                + "\"ScoredCriteria\":[\"GitDivergence\",\"WorkingTree\",\"Branches\",\"Dependencies\"],\"Thresholds\":{\"BehindWarn\":1,\"BehindFail\":21,\"StaleBranchWarn\":4,\"StaleBranchFail\":11,\"MissionFailureWarn\":1,\"MissionFailureFail\":3}},"
                + "\"Retention\":{\"AskThreadArchiveAfterDays\":90,\"AskThreadDeleteAfterDays\":0,\"JobRetentionDays\":45,\"ImportBatchRetentionDays\":90}}";
            stub.Json("GET", "/api/v1/settings", settings);
            stub.On("PUT", "/api/v1/settings", body => StubHttpHandler.Response(HttpStatusCode.OK, settings));
            stub.Json("GET", "/api/v1/vessels", "{\"Objects\":[{\"Id\":\"vsl_self\",\"Name\":\"armada\"},{\"Id\":\"vsl_2\",\"Name\":\"other\"}],\"TotalRecords\":2}");
            stub.Json("GET", "/api/v1/vessels/vsl_self/branches", "{\"VesselId\":\"vsl_self\",\"DefaultBranch\":\"main\",\"Branches\":[{\"Name\":\"main\",\"IsDefault\":true},{\"Name\":\"dev\"}],\"BranchCount\":2}");
            return stub;
        }
    }
}

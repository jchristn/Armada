namespace Test.Shared.Suites.Tui.Build
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Threading;
    using Armada.Core.Models;
    using Armada.Tui.Screens.Build;
    using Armada.Tui.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// The import wizard (W4.2) against a stubbed client: background discovery, review with selection and defaults,
    /// fleet recommendations opt-in, background import with progress, recommendations editing and apply, the Browse
    /// source, and the import history.
    /// </summary>
    public sealed class TuiBuildImportSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.Build.Import";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "flow", "Discovers in the background, reviews, imports with categorization, and applies fleets", () =>
            {
                int polls = 0;
                StubHttpHandler stub = Stub(() => Interlocked.Increment(ref polls));
                using (TuiTestHost host = TuiCase.SignedIn(170, 50, "/vessels/import", stub))
                {
                    AssertTrue(host.WaitForText("Folders on the Admiral host, one per line"), "source\n" + host.Screen());
                    ImportWizard wizard = (ImportWizard)host.Tui.Shell.Screen!;
                    wizard.PollMilliseconds = 100;
                    wizard.MaxDepth.Value = "40";
                    TuiCase.Contains(host.Screen(), "Max depth must be a whole number from 1 to 16.", "depth validation");
                    wizard.MaxDepth.Value = "3";
                    host.Type("/repos").Press("enter").Type("/repos").Press("enter").Type("\"/other\"");
                    AssertTrue(host.WaitForText("2 paths"), "paths parsed\n" + host.Screen());
                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", "/api/v1/vessels/import/discover") == 1), "discover call");
                    StubRequest discover = stub.Last("POST", "/api/v1/vessels/import/discover");
                    Armada.Core.Models.VesselDiscoveryRequest discovered = discover.BodyAs<Armada.Core.Models.VesselDiscoveryRequest>();
                    AssertEqual("/repos|/other", String.Join("|", discovered.Directories), "discover directories: " + discover.Body);
                    AssertEqual(3, discovered.MaxDepth, "discover depth: " + discover.Body);
                    AssertTrue(discovered.RunInBackground, "discover in background: " + discover.Body);
                    AssertTrue(host.WaitForText("Scanning for repositories in the background."), "discovering\n" + host.Screen());
                    AssertTrue(host.WaitForText("[2 Review]", 8000), "review\n" + host.Screen());
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "api-service", "candidate");
                    TuiCase.Contains(frame, "Already a vessel", "existing status");
                    TuiCase.Contains(frame, "Import 1 repository", "new selected by default");
                    host.Press("a");
                    AssertEqual(1, wizard.CandidateGrid.Marked.Count, "select all new");
                    wizard.CandidateGrid.MoveCursor(1);
                    host.Press("space");
                    AssertEqual(1, wizard.CandidateGrid.Marked.Count, "existing vessels cannot be selected");
                    host.Press("]");
                    AssertTrue(host.WaitForText("Defaults for the new vessels"), "options\n" + host.Screen());
                    wizard.DefaultFleet.Choose(wizard.DefaultFleet.Options.First(o => o.Value == "flt_web"));
                    wizard.DefaultLanding.Choose(wizard.DefaultLanding.Options.First(o => o.Value == "PullRequest"));
                    wizard.Categorize.Checked = true;
                    AssertTrue(host.PumpUntil(() => wizard.CategorizePrompt.Text.Contains("Group these repositories")), "default prompt");
                    AssertTrue(host.PumpUntil(() => wizard.CategorizeCaptain.Options.Count == 2), "captains");
                    AssertTrue(!wizard.CategorizeCaptain.Options.First(o => o.Value == "cpt_2").Enabled, "busy captain disabled");
                    host.Press("ctrl+s");
                    AssertTrue(host.WaitForText("Choose the captain that will recommend fleets."), "captain required\n" + host.Screen());
                    wizard.CategorizeCaptain.Choose(wizard.CategorizeCaptain.Options.First(o => o.Value == "cpt_1"));
                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", "/api/v1/vessels/import") == 1), "import call");
                    StubRequest importCall = stub.Last("POST", "/api/v1/vessels/import");
                    Armada.Core.Models.VesselImportRequest import = importCall.BodyAs<Armada.Core.Models.VesselImportRequest>();
                    AssertEqual("vib_1", import.BatchId, "import batch: " + importCall.Body);
                    AssertEqual("/repos/api-service", String.Join("|", import.Paths), "import paths: " + importCall.Body);
                    AssertEqual("flt_web", import.FleetId, "import fleet: " + importCall.Body);
                    AssertEqual(Armada.Core.Enums.LandingModeEnum.PullRequest, import.Defaults?.LandingMode, "import landing mode: " + importCall.Body);
                    AssertNotNull(import.Categorization, "categorization sent: " + importCall.Body);
                    AssertEqual("cpt_1", import.Categorization!.CaptainId, "categorization captain: " + importCall.Body);
                    AssertNull(import.Categorization.Prompt, "categorization with default prompt as null: " + importCall.Body);
                    AssertTrue(host.WaitForText("[3 Results]"), "results\n" + host.Screen());
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Notifications.ActiveToasts().Any(t => t.Severity == NotificationSeverityEnum.Success && t.Text.Contains("Import finished: 1 vessel created.")), 8000), "finished toast");
                    AssertTrue(host.WaitForText("Fleets recommended", 8000), "recommendations\n" + host.Screen());
                    host.Press("]");
                    AssertTrue(host.WaitForText("Backend"), "fleet drafts\n" + host.Screen());
                    TuiCase.Contains(host.Screen(), "- api-service", "vessel in fleet");
                    host.Press("r");
                    AssertTrue(host.WaitForText("Fleet name"), "rename prompt");
                    host.Press("ctrl+u").Type("Services").Press("ctrl+s");
                    AssertTrue(host.WaitForText("Services  (1)"), "renamed\n" + host.Screen());
                    host.Press("A");
                    TuiCase.Contains(host.Screen(), "1 repository will be assigned", "apply confirm");
                    host.Press("y");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", "/api/v1/vessels/import/batches/vib_1/fleet-recommendations/apply") == 1), "apply call");
                    StubRequest apply = stub.Last("POST", "/api/v1/vessels/import/batches/vib_1/fleet-recommendations/apply");
                    AssertTrue(apply.BodyAs<Armada.Core.Models.FleetRecommendationApplyRequest>().Fleets.Any(f => f.Name == "Services" && String.Join("|", f.VesselIds) == "vsl_new"), "apply body: " + apply.Body);
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Notifications.ActiveToasts().Any(t => t.Severity == NotificationSeverityEnum.Success && t.Text.Contains("Fleets applied: 1 vessel assigned."))), "applied toast");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "categorization_error_labels", "Every fleet categorization error code has an English label", () =>
            {
                List<string> codes = typeof(VesselImportCategorizationCodes).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                    .Where(f => f.IsLiteral && f.FieldType == typeof(string))
                    .Select(f => (string)f.GetRawConstantValue()!)
                    .ToList();
                AssertEqual(2, codes.Count, "categorization codes");
                foreach (string code in codes) AssertFalse(String.IsNullOrEmpty(ImportText.ErrorLabel(code)), "label for " + code);
                AssertEqual("Choose a captain to recommend fleets, or turn fleet recommendations off.", ImportText.ErrorLabel(VesselImportCategorizationCodes.CategorizationCaptainRequired), "required label");
            }));

            cases.Add(TuiCase.Sync(Suite, "browse_history", "Browse marks repositories and worktrees; history lists batches and opens one", () =>
            {
                StubHttpHandler stub = Stub(() => 99);
                using (TuiTestHost host = TuiCase.SignedIn(170, 50, "/vessels/import", stub))
                {
                    AssertTrue(host.WaitForText("Paste paths"), "source");
                    ImportWizard wizard = (ImportWizard)host.Tui.Shell.Screen!;
                    wizard.SourceMode.Choose(wizard.SourceMode.Options.First(o => o.Value == "browse"));
                    AssertTrue(host.WaitForText("Code"), "roots\n" + host.Screen());
                    host.Press("enter");
                    AssertTrue(host.WaitForText("[Git repository]"), "children\n" + host.Screen());
                    TuiCase.Contains(host.Screen(), "[Worktree]", "worktree tag");
                    host.Press("down").Press("space");
                    host.Press("down").Press("space");
                    AssertEqual(1, wizard.Browse.Selected.Count, "worktree not selectable");
                    wizard.AllowWorktrees.Checked = true;
                    host.Press("space");
                    AssertEqual(2, wizard.Browse.Selected.Count, "worktree allowed");
                    TuiCase.Contains(host.Screen(), "2 folders selected", "selection summary");
                    AssertTrue(stub.Saw("GET", "/api/v1/vessels/import/browse", r => r.QueryValue("path") != null), "browse folder call");
                    host.Press("h");
                    AssertTrue(host.WaitForText("Completed with failures"), "history\n" + host.Screen());
                    host.Press("enter");
                    AssertTrue(host.WaitForText("Not selected"), "batch view\n" + host.Screen());
                    TuiCase.Contains(host.Screen(), "Created 1  |  Skipped 0  |  Failed 1", "counts");
                    host.Press("b");
                    AssertTrue(host.WaitForText("Completed with failures"), "back to history");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "esc_leaves_fields", "Esc in any text field (last one included) leaves it, and the next Esc closes the wizard", () =>
            {
                StubHttpHandler stub = Stub(() => 0);
                using (TuiTestHost host = TuiCase.SignedIn(170, 50, "/vessels/import", stub))
                {
                    AssertTrue(host.WaitForText("Folders on the Admiral host, one per line"), "source");
                    ImportWizard wizard = (ImportWizard)host.Tui.Shell.Screen!;
                    wizard.PollMilliseconds = 50;
                    wizard.Scope.Focus(wizard.MaxDepth);
                    host.Press("esc");
                    AssertFalse(wizard.MaxDepth.IsFocused, "Esc leaves the last text field of the source step");
                    AssertTrue(wizard.SourceMode.IsFocused, "focus moves to the source mode");
                    wizard.Scope.Focus(wizard.PasteArea);
                    host.Type("/repos").Press("ctrl+s");
                    AssertTrue(host.WaitForText("[2 Review]", 8000), "review\n" + host.Screen());
                    host.Press("/");
                    AssertTrue(wizard.CandidateSearch.IsFocused, "/ focuses the search");
                    host.Press("esc");
                    AssertTrue(wizard.CandidateGrid.IsFocused, "Esc in the search returns to the candidates");
                    host.Press("]");
                    AssertTrue(host.WaitForText("Defaults for the new vessels"), "options");
                    wizard.Categorize.Checked = true;
                    AssertTrue(host.PumpUntil(() => wizard.CategorizePrompt.Text.Length > 0), "prompt shown");
                    wizard.Options.Scope.Focus(wizard.CategorizePrompt);
                    host.Press("esc");
                    AssertFalse(wizard.CategorizePrompt.IsFocused, "Esc leaves the prompt");
                    AssertFalse(host.App.Modals.IsActive, "no close confirmation yet");
                    host.Press("esc");
                    AssertTrue(host.WaitForText("Leave the import review?"), "the next Esc asks to leave the review\n" + host.Screen());
                    host.Press("y");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Router.Current!.FullPath == "/vessels"), "closed to Vessels");
                }
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI import wizard", cases: cases);
        }

        /// <summary>
        /// The import wizard stub: discovery of two candidates (one new, one already a vessel), a background import that
        /// creates vsl_new, fleet recommendations, browse, and history. Also used by Tui.KeyboardFlows.Build.
        /// </summary>
        /// <param name="nextPoll">Called on every batch poll.</param>
        /// <returns>Stub.</returns>
        internal static StubHttpHandler Stub(Func<int> nextPoll)
        {
            StubHttpHandler stub = BuildStubs.Server();
            stub.Json("GET", "/api/v1/captains", "{\"Success\":true,\"Objects\":[{\"Id\":\"cpt_1\",\"Name\":\"claude-1\",\"Runtime\":\"ClaudeCode\",\"State\":\"Idle\",\"TenantId\":\"ten_default\"},{\"Id\":\"cpt_2\",\"Name\":\"busy\",\"Runtime\":\"Codex\",\"State\":\"Working\",\"TenantId\":\"ten_default\"}],\"TotalRecords\":2}");
            string discovering = "{\"Id\":\"vib_1\",\"Status\":\"Discovering\",\"RequestedPathCount\":2,\"CreatedUtc\":\"2026-10-04T10:00:00Z\",\"LastUpdateUtc\":\"2026-10-04T10:00:00Z\"}";
            stub.Json("POST", "/api/v1/vessels/import/discover", "{\"BatchId\":\"vib_1\",\"JobId\":\"job_d\",\"RunsInBackground\":true,\"Batch\":" + discovering + ",\"Candidates\":[],\"Hints\":[]}", HttpStatusCode.Accepted);
            string items = "[{\"Id\":\"vii_1\",\"Path\":\"/repos/api-service\",\"ProposedName\":\"api-service\",\"RemoteUrl\":\"https://x/api.git\",\"DefaultBranch\":\"main\",\"CandidateStatus\":\"New\",\"Outcome\":\"Pending\"}," +
                "{\"Id\":\"vii_2\",\"Path\":\"/repos/demo\",\"ProposedName\":\"demo\",\"CandidateStatus\":\"AlreadyOnboarded\",\"ExistingVesselId\":\"vsl_demo\",\"Outcome\":\"Pending\"}]";
            string discovered = "{\"Batch\":{\"Id\":\"vib_1\",\"Status\":\"Discovered\",\"RequestedPathCount\":2,\"CandidateCount\":2,\"CreatedUtc\":\"2026-10-04T10:00:00Z\",\"LastUpdateUtc\":\"2026-10-04T10:00:00Z\"},\"Items\":" + items + ",\"Hints\":[],\"FleetRecommendations\":[]}";
            string importing = "{\"Batch\":{\"Id\":\"vib_1\",\"Status\":\"Importing\",\"CandidateCount\":2,\"CategorizationStatus\":\"Pending\",\"CreatedUtc\":\"2026-10-04T10:00:00Z\",\"LastUpdateUtc\":\"2026-10-04T10:00:00Z\"},\"Items\":[],\"Hints\":[],\"FleetRecommendations\":[]}";
            string done = "{\"Batch\":{\"Id\":\"vib_1\",\"Status\":\"Completed\",\"CandidateCount\":2,\"CreatedCount\":1,\"SkippedCount\":1,\"CategorizationStatus\":\"Completed\",\"CreatedUtc\":\"2026-10-04T10:00:00Z\",\"LastUpdateUtc\":\"2026-10-04T10:00:00Z\"}," +
                "\"Items\":[{\"Id\":\"vii_1\",\"Path\":\"/repos/api-service\",\"ProposedName\":\"api-service\",\"CandidateStatus\":\"New\",\"Outcome\":\"Created\",\"VesselId\":\"vsl_new\",\"Selected\":true}]," +
                "\"Hints\":[],\"FleetRecommendations\":[{\"Id\":\"vfr_1\",\"BatchId\":\"vib_1\",\"Name\":\"Backend\",\"Rationale\":\"APIs\",\"VesselIds\":[\"vsl_new\"]}]}";
            int phase = 0;
            stub.On("GET", "/api/v1/vessels/import/batches/vib_1", b =>
            {
                nextPoll();
                string json = phase == 0 ? "{\"Batch\":" + discovering + ",\"Items\":[],\"Hints\":[],\"FleetRecommendations\":[]}" : phase == 1 ? discovered : phase == 2 ? importing : done;
                if (phase == 0 || phase == 2) phase++;
                return StubHttpHandler.Response(HttpStatusCode.OK, json);
            });
            stub.On("POST", "/api/v1/vessels/import", b =>
            {
                phase = 2;
                return StubHttpHandler.Response(HttpStatusCode.Accepted, "{\"BatchId\":\"vib_1\",\"JobId\":\"job_i\",\"RunsInBackground\":true,\"Batch\":{\"Id\":\"vib_1\",\"Status\":\"Importing\",\"CategorizationStatus\":\"Pending\",\"CreatedUtc\":\"2026-10-04T10:00:00Z\",\"LastUpdateUtc\":\"2026-10-04T10:00:00Z\"},\"Items\":[]}");
            });
            stub.Json("GET", "/api/v1/vessels/import/categorization/default-prompt", "{\"TemplateName\":\"import.fleet_categorization\",\"Prompt\":\"Group these repositories into fleets.\",\"TimeoutMinutes\":10}");
            stub.Json("POST", "/api/v1/vessels/import/batches/vib_1/fleet-recommendations/apply", "{\"BatchId\":\"vib_1\",\"Fleets\":[],\"CreatedFleetIds\":[],\"Assignments\":[{\"VesselId\":\"vsl_new\",\"FleetId\":\"flt_s\"}]}");
            stub.Json("GET", "/api/v1/vessels/import/browse", "{\"Path\":null,\"Entries\":[{\"Name\":\"Code\",\"Path\":\"/Users/me/Code\",\"HasSubdirectories\":true}]}");
            stub.On("GET", "/api/v1/vessels/import/browse?path=L1VzZXJzL21lL0NvZGU", b => StubHttpHandler.Response(HttpStatusCode.OK, "{\"Path\":\"/Users/me/Code\",\"Entries\":[{\"Name\":\"api\",\"Path\":\"/Users/me/Code/api\",\"IsGitRepository\":true},{\"Name\":\"wt\",\"Path\":\"/Users/me/Code/wt\",\"IsWorktree\":true,\"IsGitRepository\":true}]}"));
            stub.Json("POST", "/api/v1/vessels/import/batches/enumerate", "{\"Success\":true,\"PageNumber\":1,\"PageSize\":25,\"TotalPages\":1,\"TotalRecords\":1,\"Objects\":[{\"Id\":\"vib_9\",\"Status\":\"CompletedWithFailures\",\"RequestedPathCount\":1,\"CandidateCount\":2,\"CreatedCount\":1,\"FailedCount\":1,\"CreatedUtc\":\"2026-10-03T10:00:00Z\",\"LastUpdateUtc\":\"2026-10-03T10:00:00Z\"}]}");
            stub.Json("GET", "/api/v1/vessels/import/batches/vib_9", "{\"Batch\":{\"Id\":\"vib_9\",\"Status\":\"CompletedWithFailures\",\"RequestedPathCount\":1,\"CandidateCount\":2,\"CreatedCount\":1,\"FailedCount\":1,\"CreatedUtc\":\"2026-10-03T10:00:00Z\",\"LastUpdateUtc\":\"2026-10-03T10:00:00Z\"}," +
                "\"Items\":[{\"Id\":\"vii_8\",\"Path\":\"/r/a\",\"ProposedName\":\"a\",\"Outcome\":\"Created\",\"VesselId\":\"vsl_a\"},{\"Id\":\"vii_9\",\"Path\":\"/r/b\",\"ProposedName\":\"b\",\"Outcome\":\"SkippedNotSelected\"}],\"Hints\":[],\"FleetRecommendations\":[]}");
            return stub;
        }
    }
}

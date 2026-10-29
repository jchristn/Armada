namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Client.Models;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Helm;
    using Armada.Helm.Commands;
    using Armada.Helm.Infrastructure;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// The Helm commands for black box prevention, forensics and operations (plan section 13) against a recording
    /// Admiral stub: <c>blackbox timeline|diff|nearby|cascade|recovery-stats</c>, <c>voyage cascade</c>,
    /// <c>vessel overlap-preview|reservations|doctor|update --serialize-overlapping-missions</c> and
    /// <c>mission lint-brief</c>. Each reaches its route with its options, prints a table or <c>--json</c> alone on
    /// stdout, and uses the black box exit codes (0 ok, 1 error, 2 credential or target, 3 not found).
    /// </summary>
    public sealed class HelmBlackBoxInsightSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.HelmBlackBoxInsight";
        private const string Token = "bbi-token";
        private const string VesselJson = "{\"Id\":\"vsl_1\",\"Name\":\"my-repo\",\"RepoUrl\":\"https://example.com/r.git\",\"DefaultBranch\":\"main\",\"AllowConcurrentMissions\":true,\"SerializeOverlappingMissions\":false}";
        private const string NotFound = "{\"Error\":\"NotFound\",\"Message\":\"Not found.\"}";

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("timeline_order_paging_and_json", "timeline reads oldest first by default (--newest-first flips it), pages, renders the events, and --json prints the page alone", TestTags.Positive, async () =>
            {
                using (RecordingAdmiralStub stub = new RecordingAdmiralStub())
                {
                    stub.Replies["POST /api/v1/blackbox/bbx_1/timeline/enumerate"] = StubAdmiralReply.Json("{\"EntryId\":\"bbx_1\",\"PageNumber\":1,\"PageSize\":25,\"TotalPages\":1,\"TotalRecords\":1,\"Objects\":[{\"TimestampUtc\":\"2026-10-20T11:00:00Z\",\"Source\":\"Mission\",\"Kind\":\"StatusChanged\",\"Message\":\"InProgress to LandingFailed\",\"EntityId\":\"msn_1\"}]}");
                    HelmCliRun run = await Run(stub, "blackbox", "timeline", "bbx_1").ConfigureAwait(false);
                    Ok(run);
                    EnumerationQuery sent = Body<EnumerationQuery>(stub, "POST", "/api/v1/blackbox/bbx_1/timeline/enumerate");
                    AssertEqual(EnumerationOrderEnum.CreatedAscending, sent.Order, "oldest first");
                    AssertEqual(25, sent.PageSize, "default page size");
                    AssertContains("LandingFailed", run.Output, "event");
                    HelmCliRun newest = await Run(stub, "blackbox", "timeline", "bbx_1", "--newest-first", "--page", "2", "--page-size", "10").ConfigureAwait(false);
                    Ok(newest);
                    sent = Body<EnumerationQuery>(stub, "POST", "/api/v1/blackbox/bbx_1/timeline/enumerate");
                    AssertEqual(EnumerationOrderEnum.CreatedDescending, sent.Order, "newest first");
                    AssertEqual(2, sent.PageNumber, "page");
                    AssertEqual(10, sent.PageSize, "page size");
                    HelmCliRun json = await Run(stub, "blackbox", "timeline", "bbx_1", "--json").ConfigureAwait(false);
                    Ok(json);
                    BlackBoxTimelinePage? page = JsonSerializer.Deserialize<BlackBoxTimelinePage>(json.StandardOutput, ArmadaJson.Options);
                    AssertEqual(1, page?.Objects.Count, "page JSON on stdout");
                    AssertEqual("", json.Output.Trim(), "nothing else on the console");
                }
            }));

            cases.Add(CaseAsync("timeline_not_found_and_auth", "timeline of an unknown entry exits 3 with the hint; a refused credential exits 2", TestTags.Negative, async () =>
            {
                using (RecordingAdmiralStub stub = new RecordingAdmiralStub())
                {
                    stub.Replies["POST /api/v1/blackbox/bbx_none/timeline/enumerate"] = StubAdmiralReply.Json(NotFound, 404);
                    stub.Replies["POST /api/v1/blackbox/bbx_auth/timeline/enumerate"] = StubAdmiralReply.Json("{\"Error\":\"Unauthorized\",\"Message\":\"Bad token.\"}", 401);
                    HelmCliRun none = await Run(stub, "blackbox", "timeline", "bbx_none", "--json").ConfigureAwait(false);
                    AssertEqual(BlackBoxCli.NotFoundExitCode, none.ExitCode, "404");
                    AssertEqual("", none.StandardOutput, "stdout empty");
                    AssertContains("armada blackbox list", none.StandardError, "hint");
                    HelmCliRun auth = await Run(stub, "blackbox", "timeline", "bbx_auth").ConfigureAwait(false);
                    AssertEqual(HelmErrorHandler.TargetErrorExitCode, auth.ExitCode, "401");
                }
            }));

            cases.Add(CaseAsync("diff_route_and_refusals", "diff reads the pair's route and renders the differences and files; the same id twice is refused without a call; 404 exits 3", TestTags.Positive, async () =>
            {
                using (RecordingAdmiralStub stub = new RecordingAdmiralStub())
                {
                    stub.Replies["GET /api/v1/blackbox/bbx_1/diff/bbx_2"] = StubAdmiralReply.Json("{\"EntryId\":\"bbx_1\",\"OtherEntryId\":\"bbx_2\",\"SameMission\":true,\"Attempt\":2,\"OtherAttempt\":1,\"DifferenceCount\":1,"
                        + "\"Differences\":[{\"Section\":\"Subject\",\"Field\":\"CaptainId\",\"Value\":\"cpt_a\",\"OtherValue\":\"cpt_b\"}],\"ChangedFilesKnown\":true,\"FilesOnlyInEntry\":[\"src/a.cs\"],\"FilesOnlyInEntryCount\":1}");
                    stub.Replies["GET /api/v1/blackbox/bbx_1/diff/bbx_9"] = StubAdmiralReply.Json(NotFound, 404);
                    HelmCliRun run = await Run(stub, "blackbox", "diff", "bbx_1", "bbx_2").ConfigureAwait(false);
                    Ok(run);
                    AssertContains("CaptainId", run.Output, "difference");
                    AssertContains("cpt_b", run.Output, "other value");
                    AssertContains("+ src/a.cs", run.Output, "file only in the entry");
                    HelmCliRun json = await Run(stub, "blackbox", "diff", "bbx_1", "bbx_2", "--json").ConfigureAwait(false);
                    AssertEqual(1, JsonSerializer.Deserialize<BlackBoxAttemptDiff>(json.StandardOutput, ArmadaJson.Options)?.DifferenceCount, "json");
                    int calls = stub.Requests().Count(r => r.Path.Contains("/diff/"));
                    HelmCliRun same = await Run(stub, "blackbox", "diff", "bbx_1", "bbx_1").ConfigureAwait(false);
                    AssertEqual(1, same.ExitCode, "same entry refused");
                    AssertEqual(calls, stub.Requests().Count(r => r.Path.Contains("/diff/")), "no call");
                    HelmCliRun missing = await Run(stub, "blackbox", "diff", "bbx_1", "bbx_9").ConfigureAwait(false);
                    AssertEqual(BlackBoxCli.NotFoundExitCode, missing.ExitCode, "404");
                }
            }));

            cases.Add(CaseAsync("nearby_paging_and_empty", "nearby pages the successes and renders them; an empty page says so", TestTags.Positive, async () =>
            {
                using (RecordingAdmiralStub stub = new RecordingAdmiralStub())
                {
                    stub.Replies["POST /api/v1/blackbox/bbx_1/nearby-successes/enumerate"] = StubAdmiralReply.Json("{\"PageNumber\":2,\"PageSize\":5,\"TotalPages\":2,\"TotalRecords\":6,\"Objects\":[{\"SuccessId\":\"bbs_1\",\"MissionId\":\"msn_ok\",\"MissionTitle\":\"Landed fine\",\"LandedUtc\":\"2026-10-20T09:00:00Z\",\"HoursFromFailure\":-2.5,\"OverlappingFileCount\":2,\"BaseCommit\":\"abcdef123456\"}]}");
                    stub.Replies["POST /api/v1/blackbox/bbx_2/nearby-successes/enumerate"] = StubAdmiralReply.Json("{\"PageNumber\":1,\"PageSize\":25,\"TotalPages\":0,\"TotalRecords\":0,\"Objects\":[]}");
                    HelmCliRun run = await Run(stub, "blackbox", "nearby", "bbx_1", "--page", "2", "--page-size", "5").ConfigureAwait(false);
                    Ok(run);
                    EnumerationQuery sent = Body<EnumerationQuery>(stub, "POST", "/api/v1/blackbox/bbx_1/nearby-successes/enumerate");
                    AssertEqual(2, sent.PageNumber, "page");
                    AssertEqual(5, sent.PageSize, "size");
                    AssertContains("msn_ok", run.Output, "mission");
                    AssertContains("-2.5 h", run.Output, "hours");
                    AssertContains("abcdef12", run.Output, "short base commit");
                    HelmCliRun empty = await Run(stub, "blackbox", "nearby", "bbx_2").ConfigureAwait(false);
                    Ok(empty);
                    AssertContains("No successful landings", empty.Output, "empty");
                }
            }));

            cases.Add(CaseAsync("recovery_stats_filters", "recovery-stats sends --rule and --vessel and renders the success rate; none tracked says so", TestTags.Positive, async () =>
            {
                using (RecordingAdmiralStub stub = new RecordingAdmiralStub())
                {
                    stub.Replies["POST /api/v1/blackbox/recovery-stats/enumerate"] = StubAdmiralReply.Json("{\"PageNumber\":1,\"PageSize\":25,\"TotalPages\":1,\"TotalRecords\":1,\"Objects\":[{\"RuleId\":\"BBX-LAND-001\",\"PathId\":\"bbx-land-001.retry\",\"Runs\":4,\"Succeeded\":3,\"Failed\":1,\"SuccessRate\":0.75}]}");
                    HelmCliRun run = await Run(stub, "blackbox", "recovery-stats", "--rule", "BBX-LAND-001", "--vessel", "vsl_1").ConfigureAwait(false);
                    Ok(run);
                    BlackBoxRecoveryStatsRequest sent = Body<BlackBoxRecoveryStatsRequest>(stub, "POST", "/api/v1/blackbox/recovery-stats/enumerate");
                    AssertEqual("BBX-LAND-001", sent.RuleId, "rule");
                    AssertEqual("vsl_1", sent.VesselId, "vessel");
                    AssertContains("75%", run.Output, "success rate");
                    AssertContains("3/1/0/0", run.Output, "outcomes");
                }

                using (RecordingAdmiralStub stub = new RecordingAdmiralStub())
                {
                    stub.Replies["POST /api/v1/blackbox/recovery-stats/enumerate"] = StubAdmiralReply.Json("{\"PageNumber\":1,\"PageSize\":25,\"TotalPages\":0,\"TotalRecords\":0,\"Objects\":[]}");
                    HelmCliRun run = await Run(stub, "blackbox", "recovery-stats").ConfigureAwait(false);
                    Ok(run);
                    AssertContains("No recovery runs", run.Output, "empty");
                    AssertNull(Body<BlackBoxRecoveryStatsRequest>(stub, "POST", "/api/v1/blackbox/recovery-stats/enumerate").RuleId, "no rule filter");
                }
            }));

            cases.Add(CaseAsync("cascade_both_forms", "voyage cascade and blackbox cascade read the voyage's cascade and print the tree from the root; 404 exits 3 with the voyage hint", TestTags.Positive, async () =>
            {
                using (RecordingAdmiralStub stub = new RecordingAdmiralStub())
                {
                    stub.Replies["GET /api/v1/voyages/vyg_1/blackbox/cascade"] = StubAdmiralReply.Json("{\"VoyageId\":\"vyg_1\",\"VoyageTitle\":\"Batch\",\"VoyageStatus\":\"Failed\",\"RootMissionId\":\"msn_a\",\"RootEntryId\":\"bbx_a\",\"MissionCount\":2,"
                        + "\"Nodes\":[{\"MissionId\":\"msn_b\",\"Title\":\"Second\",\"Status\":\"Cancelled\"},{\"MissionId\":\"msn_a\",\"Title\":\"First\",\"Status\":\"Failed\",\"FailureKind\":\"LandingConflict\",\"EntryId\":\"bbx_a\",\"IsRoot\":true}],"
                        + "\"Edges\":[{\"FromMissionId\":\"msn_a\",\"ToMissionId\":\"msn_b\",\"Kind\":\"CancelledBecause\"}]}");
                    stub.Replies["GET /api/v1/voyages/vyg_none/blackbox/cascade"] = StubAdmiralReply.Json(NotFound, 404);
                    HelmCliRun voyage = await Run(stub, "voyage", "cascade", "vyg_1").ConfigureAwait(false);
                    Ok(voyage);
                    AssertContains("msn_a", voyage.Output, "root");
                    AssertContains("CancelledBecause", voyage.Output, "edge");
                    AssertTrue(voyage.Output.IndexOf("msn_a", StringComparison.Ordinal) < voyage.Output.IndexOf("msn_b", StringComparison.Ordinal), "the root comes first");
                    AssertContains("armada blackbox show bbx_a", voyage.Output, "next command");
                    HelmCliRun alias = await Run(stub, "blackbox", "cascade", "vyg_1", "--json").ConfigureAwait(false);
                    Ok(alias);
                    AssertEqual("msn_a", JsonSerializer.Deserialize<BlackBoxCascadeGraph>(alias.StandardOutput, ArmadaJson.Options)?.RootMissionId, "json");
                    AssertEqual(2, stub.Requests().Count(r => r.Path == "/api/v1/voyages/vyg_1/blackbox/cascade"), "both forms call the route");
                    HelmCliRun none = await Run(stub, "voyage", "cascade", "vyg_none").ConfigureAwait(false);
                    AssertEqual(BlackBoxCli.NotFoundExitCode, none.ExitCode, "404");
                    AssertContains("armada voyage list", none.StandardError, "voyage hint");
                }
            }));

            cases.Add(CaseAsync("overlap_preview_brief_file_and_name", "vessel overlap-preview resolves a vessel name, reads --brief-file, posts the brief and prints the overlaps and lint", TestTags.Positive, async () =>
            {
                string dir = Path.Combine(Path.GetTempPath(), "armada-helm-bbi-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, "brief.md");
                File.WriteAllText(file, "Touch only src/a.cs after mission 2 lands");
                try
                {
                    using (RecordingAdmiralStub stub = VesselStub())
                    {
                        stub.Replies["POST /api/v1/vessels/vsl_1/overlap-preview"] = StubAdmiralReply.Json("{\"VesselId\":\"vsl_1\",\"LikelyFiles\":[\"src/a.cs\"],\"LikelyFileCount\":1,\"WarningCount\":1,"
                            + "\"Warnings\":[{\"Kind\":\"ActiveMission\",\"OverlappingMissionId\":\"msn_busy\",\"OverlappingMissionStatus\":\"InProgress\",\"FileCount\":1,\"Message\":\"Both touch src/a.cs\"}],\"SuggestedDependsOnMissionId\":\"msn_busy\",\"ActiveMissionsCompared\":2,"
                            + "\"BriefLint\":{\"FindingCount\":1,\"Clean\":false,\"PathsChecked\":true,\"Findings\":[{\"RuleId\":\"BRIEF-DEP-001\",\"Severity\":\"Warning\",\"Message\":\"Dependency language without a dependency\",\"Excerpt\":\"after mission 2 lands\"}]}}");
                        HelmCliRun run = await Run(stub, "vessel", "overlap-preview", "my-repo", "--brief-file", file, "--title", "Fix a").ConfigureAwait(false);
                        Ok(run);
                        MissionOverlapPreviewRequest sent = Body<MissionOverlapPreviewRequest>(stub, "POST", "/api/v1/vessels/vsl_1/overlap-preview");
                        AssertEqual("Touch only src/a.cs after mission 2 lands", sent.Description, "brief from the file");
                        AssertEqual("Fix a", sent.Title, "title");
                        AssertContains("nothing was dispatched", run.Output, "preview notice");
                        AssertContains("msn_busy", run.Output, "overlap");
                        AssertContains("BRIEF-DEP-001", run.Output, "lint");
                        AssertContains("--serialize-overlapping-missions true", run.Output, "how to chain");
                        HelmCliRun json = await Run(stub, "vessel", "overlap-preview", "vsl_1", "--brief", "x", "--json").ConfigureAwait(false);
                        Ok(json);
                        AssertEqual(1, JsonSerializer.Deserialize<MissionOverlapPreview>(json.StandardOutput, ArmadaJson.Options)?.WarningCount, "json");
                    }
                }
                finally
                {
                    try { Directory.Delete(dir, true); } catch (Exception) { }
                }
            }));

            cases.Add(CaseAsync("overlap_preview_refusals", "overlap-preview without a brief, with both brief options, or with a missing file exits 1 without a call; an unknown vessel exits 3", TestTags.Negative, async () =>
            {
                using (RecordingAdmiralStub stub = VesselStub())
                {
                    HelmCliRun none = await Run(stub, "vessel", "overlap-preview", "vsl_1").ConfigureAwait(false);
                    AssertEqual(1, none.ExitCode, "no brief");
                    AssertContains("--brief-file", none.StandardError, "says how");
                    HelmCliRun both = await Run(stub, "vessel", "overlap-preview", "vsl_1", "--brief", "a", "--brief-file", "b.md").ConfigureAwait(false);
                    AssertEqual(1, both.ExitCode, "both");
                    HelmCliRun missing = await Run(stub, "vessel", "overlap-preview", "vsl_1", "--brief-file", Path.Combine(Path.GetTempPath(), "no-such-brief-" + Guid.NewGuid().ToString("N") + ".md")).ConfigureAwait(false);
                    AssertEqual(1, missing.ExitCode, "missing file");
                    AssertEqual(0, stub.Requests().Count(r => r.Path.EndsWith("/overlap-preview", StringComparison.Ordinal)), "no preview call");
                    HelmCliRun unknown = await Run(stub, "vessel", "overlap-preview", "nope-repo", "--brief", "x").ConfigureAwait(false);
                    AssertEqual(BlackBoxCli.NotFoundExitCode, unknown.ExitCode, "unknown vessel name");
                    AssertContains("armada vessel list", unknown.StandardError, "hint");
                    HelmCliRun unknownId = await Run(stub, "vessel", "overlap-preview", "vsl_none", "--brief", "x").ConfigureAwait(false);
                    AssertEqual(BlackBoxCli.NotFoundExitCode, unknownId.ExitCode, "unknown vessel id (404)");
                }
            }));

            cases.Add(CaseAsync("reservations_and_doctor", "vessel reservations and vessel doctor page their routes (doctor sends --window-days) and render the rows; a bad window is refused", TestTags.Positive, async () =>
            {
                using (RecordingAdmiralStub stub = VesselStub())
                {
                    stub.Replies["POST /api/v1/vessels/vsl_1/reservations/enumerate"] = StubAdmiralReply.Json("{\"VesselId\":\"vsl_1\",\"Threshold\":3,\"HotDirectoryCount\":1,\"ActiveMissionsScanned\":2,\"PageNumber\":1,\"PageSize\":25,\"TotalPages\":1,\"TotalRecords\":1,"
                        + "\"Objects\":[{\"VesselId\":\"vsl_1\",\"Directory\":\"src/Landing\",\"MissionId\":\"msn_res\",\"MissionStatus\":\"InProgress\",\"HotspotEntryCount\":5,\"FileCount\":2,\"SinceUtc\":\"2026-10-20T10:00:00Z\"}]}");
                    stub.Replies["POST /api/v1/vessels/vsl_1/blackbox-doctor/enumerate"] = StubAdmiralReply.Json("{\"VesselId\":\"vsl_1\",\"WindowDays\":14,\"EntriesInWindow\":9,\"EntriesScanned\":9,\"PageNumber\":1,\"PageSize\":25,\"TotalPages\":1,\"TotalRecords\":1,"
                        + "\"Objects\":[{\"Key\":\"k1\",\"RuleId\":\"BBX-LAND-001\",\"RecommendationId\":\"rec_1\",\"Setting\":\"LandingMode\",\"Scope\":\"Vessel\",\"CurrentValue\":\"LocalMerge\",\"SuggestedValue\":\"PullRequest\",\"Count\":6,\"ExampleEntryId\":\"bbx_ex\",\"Applicable\":true}]}");
                    HelmCliRun res = await Run(stub, "vessel", "reservations", "my-repo").ConfigureAwait(false);
                    Ok(res);
                    AssertContains("src/Landing", res.Output, "reservation");
                    AssertContains("msn_res", res.Output, "mission");
                    AssertEqual(25, Body<EnumerationQuery>(stub, "POST", "/api/v1/vessels/vsl_1/reservations/enumerate").PageSize, "paged");
                    HelmCliRun doc = await Run(stub, "vessel", "doctor", "vsl_1", "--window-days", "14").ConfigureAwait(false);
                    Ok(doc);
                    AssertEqual(14, Body<BlackBoxDoctorRequest>(stub, "POST", "/api/v1/vessels/vsl_1/blackbox-doctor/enumerate").WindowDays, "window");
                    AssertContains("BBX-LAND-001", doc.Output, "rule");
                    AssertContains("PullRequest", doc.Output, "suggested value");
                    AssertContains("rec_1", doc.Output, "applicable recommendation id");
                    int calls = stub.Requests().Count(r => r.Path.EndsWith("/blackbox-doctor/enumerate", StringComparison.Ordinal));
                    HelmCliRun bad = await Run(stub, "vessel", "doctor", "vsl_1", "--window-days", "0").ConfigureAwait(false);
                    AssertEqual(1, bad.ExitCode, "bad window");
                    AssertEqual(calls, stub.Requests().Count(r => r.Path.EndsWith("/blackbox-doctor/enumerate", StringComparison.Ordinal)), "no call");
                    HelmCliRun json = await Run(stub, "vessel", "doctor", "vsl_1", "--json").ConfigureAwait(false);
                    Ok(json);
                    AssertEqual(1, JsonSerializer.Deserialize<BlackBoxDoctorPage>(json.StandardOutput, ArmadaJson.Options)?.Objects.Count, "json");
                }
            }));

            cases.Add(CaseAsync("lint_brief", "mission lint-brief posts the brief (with the resolved vessel) and prints the findings; no brief exits 1", TestTags.Positive, async () =>
            {
                using (RecordingAdmiralStub stub = VesselStub())
                {
                    stub.Replies["POST /api/v1/missions/lint-brief"] = StubAdmiralReply.Json("{\"FindingCount\":1,\"Clean\":false,\"PathsChecked\":true,\"Findings\":[{\"RuleId\":\"BRIEF-NOOP-001\",\"Severity\":\"Warning\",\"Message\":\"Verification-only brief\",\"Suggestion\":\"Dispatch it as Audit\"}]}");
                    HelmCliRun run = await Run(stub, "mission", "lint-brief", "--brief", "Verify the build passes", "--vessel", "my-repo", "--depends-on", "msn_2").ConfigureAwait(false);
                    Ok(run);
                    BriefLintRequest sent = Body<BriefLintRequest>(stub, "POST", "/api/v1/missions/lint-brief");
                    AssertEqual("Verify the build passes", sent.Description, "brief");
                    AssertEqual("vsl_1", sent.VesselId, "resolved vessel");
                    AssertEqual("msn_2", sent.DependsOnMissionId, "dependency");
                    AssertContains("BRIEF-NOOP-001", run.Output, "finding");
                    AssertContains("Dispatch it as Audit", run.Output, "suggestion");
                    HelmCliRun none = await Run(stub, "mission", "lint-brief").ConfigureAwait(false);
                    AssertEqual(1, none.ExitCode, "no brief");
                    AssertEqual(1, stub.Requests().Count(r => r.Path == "/api/v1/missions/lint-brief"), "no second call");
                }
            }));

            cases.Add(CaseAsync("vessel_update_serialize", "vessel update --serialize-overlapping-missions writes the whole vessel back with only that setting changed; no option exits 1; an unknown vessel exits 3", TestTags.Positive, async () =>
            {
                using (RecordingAdmiralStub stub = VesselStub())
                {
                    stub.Replies["PUT /api/v1/vessels/vsl_1"] = StubAdmiralReply.Json(VesselJson.Replace("\"SerializeOverlappingMissions\":false", "\"SerializeOverlappingMissions\":true"));
                    HelmCliRun run = await Run(stub, "vessel", "update", "my-repo", "--serialize-overlapping-missions", "true").ConfigureAwait(false);
                    Ok(run);
                    VesselUpsertRequest put = Body<VesselUpsertRequest>(stub, "PUT", "/api/v1/vessels/vsl_1");
                    AssertTrue(put.SerializeOverlappingMissions, "turned on");
                    AssertTrue(put.AllowConcurrentMissions, "other settings kept");
                    AssertEqual("my-repo", put.Name, "name kept");
                    AssertContains("Serialize overlapping missions: on", run.Output, "result");
                    HelmCliRun off = await Run(stub, "vessel", "update", "vsl_1", "--serialize-overlapping-missions", "false").ConfigureAwait(false);
                    Ok(off);
                    AssertFalse(Body<VesselUpsertRequest>(stub, "PUT", "/api/v1/vessels/vsl_1").SerializeOverlappingMissions, "turned off");
                    int puts = stub.Requests().Count(r => r.Method == "PUT");
                    HelmCliRun nothing = await Run(stub, "vessel", "update", "vsl_1").ConfigureAwait(false);
                    AssertEqual(1, nothing.ExitCode, "nothing to update");
                    AssertEqual(puts, stub.Requests().Count(r => r.Method == "PUT"), "no write");
                    HelmCliRun unknown = await Run(stub, "vessel", "update", "vsl_none", "--serialize-overlapping-missions", "true").ConfigureAwait(false);
                    AssertEqual(BlackBoxCli.NotFoundExitCode, unknown.ExitCode, "unknown vessel");
                    AssertEqual(puts, stub.Requests().Count(r => r.Method == "PUT"), "still no write");
                }
            }));

            cases.Add(Case("command_model", "The CLI model registers every insight command, the voyage cascade alias, and the vessel update option", TestTags.Positive, () =>
            {
                string xml = Program.DescribeCommandModelXml();
                foreach (string name in new[] { "timeline", "diff", "nearby", "cascade", "recovery-stats", "overlap-preview", "reservations", "doctor", "update", "lint-brief" })
                    AssertContains("Name=\"" + name + "\"", xml, "command " + name);
                AssertContains("\"serialize-overlapping-missions\"", xml, "vessel update option");
                AssertContains("\"brief-file\"", xml, "brief file option");
                AssertContains("\"window-days\"", xml, "doctor window option");
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Helm black box insight commands", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static RecordingAdmiralStub VesselStub()
        {
            RecordingAdmiralStub stub = new RecordingAdmiralStub();
            stub.Replies["GET /api/v1/vessels"] = StubAdmiralReply.Json("{\"Success\":true,\"PageNumber\":1,\"PageSize\":9999,\"TotalPages\":1,\"TotalRecords\":1,\"Objects\":[" + VesselJson + "]}");
            stub.Replies["GET /api/v1/vessels/vsl_1"] = StubAdmiralReply.Json(VesselJson);
            stub.Replies["GET /api/v1/vessels/vsl_none"] = StubAdmiralReply.Json(NotFound, 404);
            return stub;
        }

        private static Task<HelmCliRun> Run(RecordingAdmiralStub stub, params string[] args)
        {
            List<string> all = new List<string>(args);
            all.Add("--server");
            all.Add(stub.BaseUrl);
            all.Add("--token");
            all.Add(Token);
            return HelmCliHarness.RunCapturedAsync(all.ToArray());
        }

        private static void Ok(HelmCliRun run, string label = "exit 0")
        {
            AssertEqual(0, run.ExitCode, label + ": " + run.StandardError + run.Output);
        }

        private static T Body<T>(RecordingAdmiralStub stub, string method, string path) where T : class
        {
            List<T> bodies = stub.Requests().Where(r => r.Method == method && r.Path == path).Select(r => JsonSerializer.Deserialize<T>(r.Body, ArmadaJson.Options)!).ToList();
            AssertTrue(bodies.Count > 0, "a " + method + " " + path + " request");
            return bodies.Last();
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) =>
                {
                    body();
                    return Task.CompletedTask;
                },
                tags: new List<string> { tag });
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

        #endregion
    }
}

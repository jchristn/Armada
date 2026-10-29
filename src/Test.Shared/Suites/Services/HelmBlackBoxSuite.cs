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
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Helm.Commands;
    using Armada.Helm;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// <c>armada blackbox</c> and the <c>mission failure</c> and <c>voyage failure</c> aliases (plan 11.3 and 16.9)
    /// against a recording Admiral stub: <c>--brief</c> prints only the Markdown to stdout and exits non-zero on 404 with
    /// the message on stderr, the id prefix picks the route, filters and paging reach the server, recover and purge are
    /// dry runs by default and refuse <c>--execute</c> without confirmation or the data-loss acknowledgement, and every
    /// other command calls its route.
    /// </summary>
    public sealed class HelmBlackBoxSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.HelmBlackBox";
        private const string Token = "bb-token";

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("brief_prints_only_markdown", "show <msn> --brief prints only the brief to stdout (nothing on the console or stderr)", TestTags.Positive, async () =>
            {
                BlackBoxUiFixture fx = BlackBoxUiFixture.Load();
                using (RecordingAdmiralStub stub = new RecordingAdmiralStub())
                {
                    stub.Replies["GET /api/v1/missions/msn_x/blackbox/brief"] = StubAdmiralReply.Text(fx.AgentBrief);
                    HelmCliRun run = await Run(stub, "blackbox", "show", "msn_x", "--brief").ConfigureAwait(false);
                    AssertEqual(0, run.ExitCode, run.StandardError);
                    AssertEqual(fx.AgentBrief.EndsWith("\n", StringComparison.Ordinal) ? fx.AgentBrief : fx.AgentBrief + Environment.NewLine, run.StandardOutput, "stdout is the brief alone");
                    AssertEqual("", run.Output.Trim(), "nothing on the Spectre console");
                    AssertEqual("", run.StandardError.Trim(), "nothing on stderr");
                    AssertTrue(stub.Requests().Any(r => r.Path == "/api/v1/missions/msn_x/blackbox/brief" && r.Authorization == "Bearer " + Token), "mission brief route with the bearer token");
                }
            }));

            cases.Add(CaseAsync("brief_404_exits_nonzero", "show --brief on a mission without an entry exits non-zero with the message on stderr and nothing on stdout", TestTags.Negative, async () =>
            {
                using (RecordingAdmiralStub stub = new RecordingAdmiralStub())
                {
                    stub.Replies["GET /api/v1/missions/msn_none/blackbox/brief"] = StubAdmiralReply.Json("{\"Error\":\"NotFound\",\"Code\":\"BlackBoxNotFound\",\"Message\":\"No black box entry for mission msn_none.\"}", 404);
                    HelmCliRun run = await Run(stub, "blackbox", "show", "msn_none", "--brief").ConfigureAwait(false);
                    AssertEqual(BlackBoxCli.NotFoundExitCode, run.ExitCode, "404 exit code");
                    AssertEqual("", run.StandardOutput, "stdout stays empty for a pipe");
                    AssertContains("HTTP 404", run.StandardError, "status on stderr");
                    AssertContains("No black box entry", run.StandardError, "server message on stderr");
                    HelmCliRun alias = await Run(stub, "mission", "failure", "msn_none").ConfigureAwait(false);
                    AssertNotEqual(0, alias.ExitCode, "the alias fails the same way");
                }
            }));

            cases.Add(CaseAsync("id_prefix_picks_the_route", "msn_ reads the mission route, vyg_ the voyage route, bbx_ the entry route; the aliases force their subject; --audience goes in the query", TestTags.Positive, async () =>
            {
                BlackBoxUiFixture fx = BlackBoxUiFixture.Load();
                using (RecordingAdmiralStub stub = new RecordingAdmiralStub())
                {
                    stub.Replies["GET /api/v1/voyages/vyg_1/blackbox/brief"] = StubAdmiralReply.Text("voyage brief");
                    stub.Replies["GET /api/v1/blackbox/bbx_1/brief?audience=maintainer"] = StubAdmiralReply.Text("maintainer brief");
                    stub.Replies["GET /api/v1/missions/msn_1/blackbox"] = StubAdmiralReply.Json(fx.EntryJson());
                    stub.Replies["GET /api/v1/voyages/odd_1/blackbox"] = StubAdmiralReply.Json(fx.EntryJson());
                    HelmCliRun voyage = await Run(stub, "blackbox", "show", "vyg_1", "--brief").ConfigureAwait(false);
                    AssertEqual("voyage brief" + Environment.NewLine, voyage.StandardOutput, "voyage route");
                    HelmCliRun maintainer = await Run(stub, "blackbox", "show", "bbx_1", "--brief", "--audience", "maintainer").ConfigureAwait(false);
                    AssertEqual("maintainer brief" + Environment.NewLine, maintainer.StandardOutput, "entry route with the audience");
                    HelmCliRun json = await Run(stub, "mission", "failure", "msn_1", "--json").ConfigureAwait(false);
                    AssertEqual(0, json.ExitCode, json.StandardError);
                    BlackBoxEntry? parsed = JsonSerializer.Deserialize<BlackBoxEntry>(json.StandardOutput, ArmadaJson.Options);
                    AssertEqual(fx.EntryId, parsed?.Id, "--json prints the entry alone");
                    HelmCliRun forced = await Run(stub, "voyage", "failure", "odd_1", "--json").ConfigureAwait(false);
                    AssertEqual(0, forced.ExitCode, "voyage failure uses the voyage route whatever the prefix: " + forced.StandardError);
                    HelmCliRun bad = await Run(stub, "blackbox", "show", "bbx_1", "--brief", "--audience", "everyone").ConfigureAwait(false);
                    AssertEqual(1, bad.ExitCode, "unknown audience");
                    AssertContains("Agent, User, Maintainer", bad.StandardError, "valid audiences listed");
                }
            }));

            cases.Add(CaseAsync("show_summary", "show without --brief renders the summary and the recovery paths with the next commands", TestTags.Positive, async () =>
            {
                BlackBoxUiFixture fx = BlackBoxUiFixture.Load();
                using (RecordingAdmiralStub stub = new RecordingAdmiralStub())
                {
                    stub.Replies["GET /api/v1/blackbox/" + fx.EntryId] = StubAdmiralReply.Json(fx.EntryJson());
                    HelmCliRun run = await Run(stub, "blackbox", "show", fx.EntryId).ConfigureAwait(false);
                    AssertEqual(0, run.ExitCode, run.StandardError);
                    AssertContains(fx.EntryId, run.Output);
                    AssertContains(fx.Entry.Triage.ToString(), run.Output, "triage");
                    AssertContains(fx.FirstPath.Id, run.Output, "recovery path id");
                    AssertContains("armada blackbox recover " + fx.EntryId + " " + fx.FirstPath.Id, run.Output, "next command");
                    AssertContains("--brief | pbcopy", run.Output, "brief hint");
                }
            }));

            cases.Add(CaseAsync("list_filters_and_paging", "list sends the filters (case-insensitive enums, --since 30d) and paging; --json prints the page alone; a bad enum is refused", TestTags.Positive, async () =>
            {
                BlackBoxUiFixture fx = BlackBoxUiFixture.Load();
                using (RecordingAdmiralStub stub = new RecordingAdmiralStub())
                {
                    stub.Replies["POST /api/v1/blackbox/enumerate"] = StubAdmiralReply.Json("{\"Success\":true,\"PageNumber\":2,\"PageSize\":10,\"TotalPages\":2,\"TotalRecords\":11,\"Objects\":[" + fx.SummaryJson() + "]}");
                    HelmCliRun run = await Run(stub, "blackbox", "list", "--vessel", "vsl_1", "--triage", "user-config", "--kind", "landingconflict", "--since", "30d", "--page", "2", "--page-size", "10", "--json").ConfigureAwait(false);
                    AssertEqual(0, run.ExitCode, run.StandardError);
                    BlackBoxEnumerateRequest sent = Body<BlackBoxEnumerateRequest>(stub, "POST", "/api/v1/blackbox/enumerate");
                    AssertEqual("vsl_1", sent.VesselId);
                    AssertEqual(BlackBoxTriageEnum.UserConfig, sent.Triage);
                    AssertEqual(MissionFailureKindEnum.LandingConflict, sent.Kind);
                    AssertEqual(2, sent.PageNumber);
                    AssertEqual(10, sent.PageSize);
                    AssertTrue(sent.CreatedAfter.HasValue && Math.Abs((DateTime.UtcNow.AddDays(-30) - sent.CreatedAfter.Value).TotalMinutes) < 5, "since 30d: " + sent.CreatedAfter);
                    AssertContains(fx.EntryId, run.StandardOutput, "page JSON on stdout");
                    HelmCliRun table = await Run(stub, "blackbox", "list").ConfigureAwait(false);
                    AssertContains(fx.EntryId, table.Output, "table row");
                    HelmCliRun bad = await Run(stub, "blackbox", "list", "--triage", "nope").ConfigureAwait(false);
                    AssertEqual(1, bad.ExitCode, "bad triage");
                    AssertContains("UserConfig", bad.StandardError, "valid values listed");
                }
            }));

            cases.Add(CaseAsync("recover_dry_run_by_default", "recover is a dry run by default: one Operator dry-run call and the plan, nothing runs", TestTags.Positive, async () =>
            {
                using (RecordingAdmiralStub stub = RecoveryStub(false))
                {
                    HelmCliRun run = await Run(stub, "blackbox", "recover", "bbx_1", "rp1").ConfigureAwait(false);
                    AssertEqual(0, run.ExitCode, run.StandardError);
                    List<BlackBoxRecoveryRunRequest> calls = Bodies<BlackBoxRecoveryRunRequest>(stub, "POST", "/api/v1/blackbox/bbx_1/recovery/rp1");
                    AssertEqual(1, calls.Count, "only the dry run");
                    AssertTrue(calls[0].DryRun, "dry run");
                    AssertEqual(BlackBoxNoteAuthorKindEnum.Operator, calls[0].AuthorKind, "operator");
                    AssertContains("Retry the landing", run.Output, "plan step");
                    AssertContains("Dry run: nothing ran", run.Output, "dry run notice");
                }
            }));

            cases.Add(CaseAsync("recover_execute_refusals", "recover --execute refuses without --yes when not interactive, and a data-loss path without --acknowledge-data-loss; with both it runs with the confirm token", TestTags.Negative, async () =>
            {
                using (RecordingAdmiralStub stub = RecoveryStub(false))
                {
                    HelmCliRun refused = await Run(stub, "blackbox", "recover", "bbx_1", "rp1", "--execute").ConfigureAwait(false);
                    AssertEqual(BlackBoxCli.RefusedExitCode, refused.ExitCode, "not confirmed");
                    AssertContains("--yes", refused.StandardError, "says how to confirm");
                    AssertEqual(1, Bodies<BlackBoxRecoveryRunRequest>(stub, "POST", "/api/v1/blackbox/bbx_1/recovery/rp1").Count, "nothing ran");
                    HelmCliRun ran = await Run(stub, "blackbox", "recover", "bbx_1", "rp1", "--execute", "--yes").ConfigureAwait(false);
                    AssertEqual(0, ran.ExitCode, ran.StandardError);
                    BlackBoxRecoveryRunRequest last = Bodies<BlackBoxRecoveryRunRequest>(stub, "POST", "/api/v1/blackbox/bbx_1/recovery/rp1").Last();
                    AssertFalse(last.DryRun, "real run");
                    AssertEqual("bbc_cli", last.ConfirmToken, "confirm token from the dry run");
                    AssertEqual(BlackBoxNoteAuthorKindEnum.Operator, last.AuthorKind, "operator");
                    AssertContains("Recovery path ran.", ran.Output, "result");
                }

                using (RecordingAdmiralStub stub = RecoveryStub(true))
                {
                    HelmCliRun refused = await Run(stub, "blackbox", "recover", "bbx_1", "rp1", "--execute", "--yes").ConfigureAwait(false);
                    AssertEqual(BlackBoxCli.RefusedExitCode, refused.ExitCode, "data loss not acknowledged");
                    AssertContains("--acknowledge-data-loss", refused.StandardError);
                    AssertEqual(1, Bodies<BlackBoxRecoveryRunRequest>(stub, "POST", "/api/v1/blackbox/bbx_1/recovery/rp1").Count, "only the dry run");
                    HelmCliRun ran = await Run(stub, "blackbox", "recover", "bbx_1", "rp1", "--execute", "--yes", "--acknowledge-data-loss").ConfigureAwait(false);
                    AssertEqual(0, ran.ExitCode, ran.StandardError);
                    AssertTrue(Bodies<BlackBoxRecoveryRunRequest>(stub, "POST", "/api/v1/blackbox/bbx_1/recovery/rp1").Last().AcknowledgeDataLoss, "acknowledgement sent");
                }

                using (RecordingAdmiralStub stub = new RecordingAdmiralStub())
                {
                    stub.Replies["POST /api/v1/blackbox/bbx_1/recovery/rp9"] = StubAdmiralReply.Json("{\"Error\":\"NotFound\",\"Code\":\"BlackBoxNotFound\",\"Message\":\"Recovery path rp9 is not in the entry.\"}", 404);
                    HelmCliRun missing = await Run(stub, "blackbox", "recover", "bbx_1", "rp9", "--execute", "--yes").ConfigureAwait(false);
                    AssertEqual(BlackBoxCli.NotFoundExitCode, missing.ExitCode, "unknown path");
                }
            }));

            cases.Add(CaseAsync("purge_dry_run_and_refusals", "purge is a dry run unless --execute; --execute needs --yes when not interactive; no filter is refused", TestTags.Negative, async () =>
            {
                using (RecordingAdmiralStub stub = new RecordingAdmiralStub())
                {
                    stub.Replies["POST /api/v1/blackbox/purge"] = StubAdmiralReply.Json("{\"DryRun\":true,\"Matched\":7,\"Deleted\":0,\"SkippedPinned\":1}");
                    HelmCliRun none = await Run(stub, "blackbox", "purge").ConfigureAwait(false);
                    AssertEqual(BlackBoxCli.RefusedExitCode, none.ExitCode, "no filter");
                    AssertEqual(0, Bodies<BlackBoxPurgeRequest>(stub, "POST", "/api/v1/blackbox/purge").Count, "no call without a filter");
                    HelmCliRun dry = await Run(stub, "blackbox", "purge", "--vessel", "vsl_1", "--before", "2026-09-01").ConfigureAwait(false);
                    AssertEqual(0, dry.ExitCode, dry.StandardError);
                    BlackBoxPurgeRequest sent = Bodies<BlackBoxPurgeRequest>(stub, "POST", "/api/v1/blackbox/purge").Single();
                    AssertTrue(sent.DryRun, "dry run");
                    AssertEqual("vsl_1", sent.VesselId);
                    AssertEqual(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), sent.CreatedBefore, "before date");
                    AssertContains("7 entries match", dry.Output);
                    HelmCliRun refused = await Run(stub, "blackbox", "purge", "--vessel", "vsl_1", "--execute").ConfigureAwait(false);
                    AssertEqual(BlackBoxCli.RefusedExitCode, refused.ExitCode, "not confirmed");
                    AssertTrue(Bodies<BlackBoxPurgeRequest>(stub, "POST", "/api/v1/blackbox/purge").All(b => b.DryRun), "nothing deleted");
                    HelmCliRun executed = await Run(stub, "blackbox", "purge", "--vessel", "vsl_1", "--execute", "--yes").ConfigureAwait(false);
                    AssertEqual(0, executed.ExitCode, executed.StandardError);
                    AssertFalse(Bodies<BlackBoxPurgeRequest>(stub, "POST", "/api/v1/blackbox/purge").Last().DryRun, "deleted after confirmation");
                }
            }));

            cases.Add(CaseAsync("object_commands", "objects lists, cat prints a byte range to stdout, download -o writes the zip", TestTags.Positive, async () =>
            {
                string dir = Path.Combine(Path.GetTempPath(), "armada-helm-bb-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                try
                {
                    using (RecordingAdmiralStub stub = new RecordingAdmiralStub())
                    {
                        stub.Replies["GET /api/v1/blackbox/bbx_1/objects"] = StubAdmiralReply.Json("{\"Success\":true,\"PageNumber\":1,\"PageSize\":100,\"TotalPages\":1,\"TotalRecords\":1,\"Objects\":[{\"Name\":\"git-status.txt\",\"ContentType\":\"text/plain\",\"ContentEncoding\":\"identity\",\"SizeBytes\":20,\"UncompressedBytes\":20}]}");
                        StubAdmiralReply chunk = StubAdmiralReply.Text(" M src/a.cs\n", "text/plain");
                        chunk.Headers["X-BlackBox-Offset"] = "10";
                        chunk.Headers["X-BlackBox-Total-Bytes"] = "20";
                        stub.Replies["GET /api/v1/blackbox/bbx_1/objects/git-status.txt?offset=10&length=12"] = chunk;
                        StubAdmiralReply zip = new StubAdmiralReply { Body = "PK-zip", ContentType = "application/zip" };
                        stub.Replies["GET /api/v1/blackbox/bbx_1/download"] = zip;

                        HelmCliRun objects = await Run(stub, "blackbox", "objects", "bbx_1").ConfigureAwait(false);
                        AssertEqual(0, objects.ExitCode, objects.StandardError);
                        AssertContains("git-status.txt", objects.Output);
                        HelmCliRun cat = await Run(stub, "blackbox", "cat", "bbx_1", "git-status.txt", "--offset", "10", "--length", "12").ConfigureAwait(false);
                        AssertEqual(0, cat.ExitCode, cat.StandardError);
                        AssertEqual(" M src/a.cs\n", cat.StandardOutput, "the bytes alone on stdout");
                        HelmCliRun badCat = await Run(stub, "blackbox", "cat", "bbx_1", "git-status.txt", "--length", "0").ConfigureAwait(false);
                        AssertEqual(1, badCat.ExitCode, "length 0 refused");
                        string file = Path.Combine(dir, "out", "failure.zip");
                        HelmCliRun download = await Run(stub, "blackbox", "download", "bbx_1", "-o", file).ConfigureAwait(false);
                        AssertEqual(0, download.ExitCode, download.StandardError);
                        AssertEqual("PK-zip", File.ReadAllText(file), "zip written");
                    }
                }
                finally
                {
                    try { Directory.Delete(dir, true); } catch (Exception) { }
                }
            }));

            cases.Add(CaseAsync("entry_actions", "resolve, note (Operator), pin, unpin, and delete call their routes; resolve without --resolution and an empty note are refused", TestTags.Positive, async () =>
            {
                BlackBoxUiFixture fx = BlackBoxUiFixture.Load();
                using (RecordingAdmiralStub stub = new RecordingAdmiralStub())
                {
                    stub.Replies["POST /api/v1/blackbox/bbx_1/resolve"] = StubAdmiralReply.Json(fx.EntryJson());
                    stub.Replies["POST /api/v1/blackbox/bbx_1/notes"] = StubAdmiralReply.Json("{\"Id\":\"bbn_1\",\"EntryId\":\"bbx_1\",\"AuthorKind\":\"Operator\",\"Body\":\"x\"}");
                    stub.Replies["POST /api/v1/blackbox/bbx_1/pin"] = StubAdmiralReply.Json(fx.EntryJson());
                    stub.Replies["POST /api/v1/blackbox/bbx_2/pin"] = StubAdmiralReply.Json(fx.EntryJson());
                    stub.Replies["POST /api/v1/blackbox/bbx_1/unpin"] = StubAdmiralReply.Json(fx.EntryJson());
                    stub.Replies["DELETE /api/v1/blackbox/bbx_1"] = StubAdmiralReply.Json("{\"Requested\":1,\"Deleted\":1}");
                    stub.Replies["POST /api/v1/blackbox/delete"] = StubAdmiralReply.Json("{\"Requested\":2,\"Deleted\":2}");

                    AssertEqual(1, (await Run(stub, "blackbox", "resolve", "bbx_1").ConfigureAwait(false)).ExitCode, "resolution required");
                    Ok(await Run(stub, "blackbox", "resolve", "bbx_1", "--resolution", "wont-fix", "--note", "by design", "--link", "msn_9").ConfigureAwait(false));
                    BlackBoxResolveRequest resolve = Body<BlackBoxResolveRequest>(stub, "POST", "/api/v1/blackbox/bbx_1/resolve");
                    AssertEqual(BlackBoxResolutionEnum.WontFix, resolve.Resolution);
                    AssertEqual("by design", resolve.Note);
                    AssertEqual("msn_9", resolve.Link);
                    AssertEqual(1, (await Run(stub, "blackbox", "note", "bbx_1", "  ").ConfigureAwait(false)).ExitCode, "empty note refused");
                    Ok(await Run(stub, "blackbox", "note", "bbx_1", "Checked the dock").ConfigureAwait(false));
                    BlackBoxNoteAddRequest note = Body<BlackBoxNoteAddRequest>(stub, "POST", "/api/v1/blackbox/bbx_1/notes");
                    AssertEqual("Checked the dock", note.Body);
                    AssertEqual(BlackBoxNoteAuthorKindEnum.Operator, note.AuthorKind);
                    Ok(await Run(stub, "blackbox", "pin", "bbx_1", "bbx_2").ConfigureAwait(false));
                    Ok(await Run(stub, "blackbox", "unpin", "bbx_1").ConfigureAwait(false));
                    Ok(await Run(stub, "blackbox", "delete", "bbx_1").ConfigureAwait(false));
                    Ok(await Run(stub, "blackbox", "delete", "bbx_1", "bbx_2").ConfigureAwait(false));
                    List<RecordedAdmiralRequest> r = stub.Requests();
                    AssertTrue(r.Any(x => x.Method == "POST" && x.Path == "/api/v1/blackbox/bbx_2/pin"), "second pin");
                    AssertTrue(r.Any(x => x.Method == "POST" && x.Path == "/api/v1/blackbox/bbx_1/unpin"), "unpin");
                    AssertTrue(r.Any(x => x.Method == "DELETE" && x.Path == "/api/v1/blackbox/bbx_1"), "single delete");
                    AssertContains("bbx_2", Body<BlackBoxDeleteManyRequest>(stub, "POST", "/api/v1/blackbox/delete").Ids.Last(), "bulk delete ids");
                }
            }));

            cases.Add(CaseAsync("admin_commands", "gc (--dry-run), retention, storage status and migrate, bundle (stdout and -o), and backfill call their routes", TestTags.Positive, async () =>
            {
                string dir = Path.Combine(Path.GetTempPath(), "armada-helm-bb-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                try
                {
                    using (RecordingAdmiralStub stub = new RecordingAdmiralStub())
                    {
                        stub.Replies["POST /api/v1/blackbox/gc"] = StubAdmiralReply.Json("{\"DryRun\":true,\"Expired\":3,\"BytesFreed\":10}");
                        stub.Replies["GET /api/v1/blackbox/retention"] = StubAdmiralReply.Json("{\"Policy\":{\"RetentionDays\":30},\"TotalEntries\":4,\"TotalBytes\":2048}");
                        stub.Replies["GET /api/v1/blackbox/storage"] = StubAdmiralReply.Json("{\"Target\":\"Database\",\"Health\":{\"Target\":\"Database\",\"Healthy\":true,\"LatencyMs\":2},\"Usage\":[{\"Target\":\"Database\",\"Entries\":4,\"Bytes\":2048}]}");
                        stub.Replies["POST /api/v1/blackbox/storage/migrate"] = StubAdmiralReply.Json("{\"Id\":\"bbm_1\",\"State\":\"Pending\",\"To\":\"Disk\"}");
                        stub.Replies["POST /api/v1/blackbox/bundle"] = StubAdmiralReply.Json("{\"Format\":\"Markdown\",\"EntryCount\":2,\"FingerprintCount\":1,\"Content\":\"# Bundle\\n\"}");
                        stub.Replies["POST /api/v1/blackbox/backfill"] = StubAdmiralReply.Json("{\"Scanned\":5,\"Captured\":2}");

                        Ok(await Run(stub, "blackbox", "gc", "--dry-run").ConfigureAwait(false));
                        AssertTrue(Body<BlackBoxGcRunRequest>(stub, "POST", "/api/v1/blackbox/gc").DryRun, "--dry-run");
                        Ok(await Run(stub, "blackbox", "gc").ConfigureAwait(false));
                        AssertFalse(Body<BlackBoxGcRunRequest>(stub, "POST", "/api/v1/blackbox/gc").DryRun, "a real run without --dry-run");
                        HelmCliRun retention = await Run(stub, "blackbox", "retention").ConfigureAwait(false);
                        AssertEqual(0, retention.ExitCode, retention.StandardError);
                        AssertContains("Retention days", retention.Output);
                        HelmCliRun status = await Run(stub, "blackbox", "storage", "status").ConfigureAwait(false);
                        AssertEqual(0, status.ExitCode, status.StandardError);
                        AssertContains("healthy", status.Output);
                        AssertEqual(1, (await Run(stub, "blackbox", "storage", "migrate").ConfigureAwait(false)).ExitCode, "--to required");
                        Ok(await Run(stub, "blackbox", "storage", "migrate", "--to", "disk").ConfigureAwait(false));
                        AssertEqual(BlackBoxStorageTargetEnum.Disk, Body<BlackBoxStorageMigrationRequest>(stub, "POST", "/api/v1/blackbox/storage/migrate").To, "target");
                        HelmCliRun bundle = await Run(stub, "blackbox", "bundle", "--triage", "ArmadaDefect", "--max", "5").ConfigureAwait(false);
                        AssertEqual("# Bundle\n", bundle.StandardOutput, "bundle alone on stdout");
                        BlackBoxBundleRequest sent = Body<BlackBoxBundleRequest>(stub, "POST", "/api/v1/blackbox/bundle");
                        AssertEqual(5, sent.Max);
                        AssertEqual(BlackBoxTriageEnum.ArmadaDefect, sent.Triage);
                        string file = Path.Combine(dir, "bundle.md");
                        Ok(await Run(stub, "blackbox", "bundle", "-o", file).ConfigureAwait(false));
                        AssertEqual("# Bundle\n", File.ReadAllText(file), "bundle file");
                        Ok(await Run(stub, "blackbox", "backfill", "--since", "7d").ConfigureAwait(false));
                        BlackBoxBackfillRequest backfill = Body<BlackBoxBackfillRequest>(stub, "POST", "/api/v1/blackbox/backfill");
                        AssertTrue(backfill.Since.HasValue && Math.Abs((DateTime.UtcNow.AddDays(-7) - backfill.Since.Value).TotalMinutes) < 5, "since 7d");
                    }
                }
                finally
                {
                    try { Directory.Delete(dir, true); } catch (Exception) { }
                }
            }));

            cases.Add(CaseAsync("export_fixture", "export-fixture writes the server's fixture with -o; inside the repository the default is src/Test.Shared/Fixtures/BlackBox", TestTags.Positive, async () =>
            {
                BlackBoxUiFixture fx = BlackBoxUiFixture.Load();
                BlackBoxFixture fixture = new BlackBoxFixture { Name = "land-099-cli-export", ExpectedTriage = BlackBoxTriageEnum.UserConfig, Record = fx.Entry.Record! };
                fixture.ExpectedRuleIds.Add("BBX-LAND-001");
                string dir = Path.Combine(Path.GetTempPath(), "armada-helm-fixture-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                try
                {
                    using (RecordingAdmiralStub stub = new RecordingAdmiralStub())
                    {
                        stub.Replies["POST /api/v1/blackbox/bbx_1/export-fixture"] = StubAdmiralReply.Json(ArmadaJson.Serialize(fixture));
                        HelmCliRun run = await Run(stub, "blackbox", "export-fixture", "bbx_1", "--name", "land-099-cli-export", "-o", dir).ConfigureAwait(false);
                        AssertEqual(0, run.ExitCode, run.StandardError);
                        string file = Path.Combine(dir, "land-099-cli-export.json");
                        AssertTrue(File.Exists(file), "fixture file");
                        BlackBoxFixture parsed = Armada.Core.BlackBox.BlackBoxFixtureExporter.Parse(File.ReadAllText(file));
                        AssertEqual("land-099-cli-export", parsed.Name);
                        AssertContains("land-099-cli-export", Body<BlackBoxFixtureExportRequest>(stub, "POST", "/api/v1/blackbox/bbx_1/export-fixture").Name ?? "", "name sent");
                    }
                }
                finally
                {
                    try { Directory.Delete(dir, true); } catch (Exception) { }
                }

                string root = DashboardSource.RepoRoot();
                AssertEqual(Path.Combine(root, "src", "Test.Shared", "Fixtures", "BlackBox"), BlackBoxCli.FindFixtureDirectory(Path.Combine(root, "src", "Armada.Helm")), "found from inside the repository");
                AssertNull(BlackBoxCli.FindFixtureDirectory(Path.GetPathRoot(Path.GetTempPath()) ?? "/"), "not inside the repository");
            }));

            cases.Add(Case("since_and_enum_parsing", "--since takes durations and dates and refuses anything else; enum options ignore case and dashes", TestTags.Negative, () =>
            {
                DateTime now = new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
                AssertEqual(now.AddDays(-30), BlackBoxCli.ParseSince("30d", now));
                AssertEqual(now.AddHours(-12), BlackBoxCli.ParseSince("12h", now));
                AssertEqual(now.AddMinutes(-90), BlackBoxCli.ParseSince("90m", now));
                AssertEqual(now.AddDays(-14), BlackBoxCli.ParseSince("2w", now));
                AssertEqual(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), BlackBoxCli.ParseSince("2026-09-01", now));
                foreach (string bad in new[] { "", "soon", "-3d", "0d", "d" })
                {
                    bool threw = false;
                    try { BlackBoxCli.ParseSince(bad, now); }
                    catch (ArgumentException) { threw = true; }
                    AssertTrue(threw, "refused: '" + bad + "'");
                }

                AssertEqual(BlackBoxTriageEnum.ArmadaDefect, BlackBoxCli.ParseEnum<BlackBoxTriageEnum>("--triage", "armada_defect"));
                AssertNull(BlackBoxCli.ParseEnum<BlackBoxTriageEnum>("--triage", " "));
                AssertEqual(BlackBoxSubjectTypeEnum.Mission, BlackBoxCli.SubjectOf("msn_1"));
                AssertEqual(BlackBoxSubjectTypeEnum.Voyage, BlackBoxCli.SubjectOf("vyg_1"));
                AssertNull(BlackBoxCli.SubjectOf("bbx_1"));
            }));

            cases.Add(Case("command_model", "The CLI model registers every black box command and both aliases", TestTags.Positive, () =>
            {
                string xml = Program.DescribeCommandModelXml();
                foreach (string name in new[] { "blackbox", "show", "list", "objects", "cat", "download", "recover", "resolve", "note", "pin", "unpin", "delete", "purge", "gc", "retention", "storage", "status", "migrate", "migration", "bundle", "backfill", "export-fixture", "failure" })
                    AssertContains("Name=\"" + name + "\"", xml, "command " + name);
                AssertContains("\"acknowledge-data-loss\"", xml, "recover --acknowledge-data-loss");
                AssertContains("\"execute\"", xml, "recover and purge --execute");
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Helm black box commands", cases: cases);
        }

        #endregion

        #region Private-Methods

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

        private static RecordingAdmiralStub RecoveryStub(bool dataLoss)
        {
            RecordingAdmiralStub stub = new RecordingAdmiralStub();
            stub.Replies["POST /api/v1/blackbox/bbx_1/recovery/rp1"] = StubAdmiralReply.Json("{\"EntryId\":\"bbx_1\",\"PathId\":\"rp1\",\"DryRun\":true,\"ConfirmToken\":\"bbc_cli\",\"Plan\":[{\"Order\":1,\"Description\":\"Retry the landing\",\"DataLossRisk\":" + (dataLoss ? "true" : "false") + ",\"Action\":{\"Tool\":\"retry_landing\"}}],\"Succeeded\":true,\"StepResults\":[{\"Order\":1,\"Tool\":\"retry_landing\",\"Succeeded\":true}]}");
            return stub;
        }

        private static T Body<T>(RecordingAdmiralStub stub, string method, string path) where T : class
        {
            List<T> bodies = Bodies<T>(stub, method, path);
            AssertTrue(bodies.Count > 0, "a " + method + " " + path + " request");
            return bodies.Last();
        }

        private static List<T> Bodies<T>(RecordingAdmiralStub stub, string method, string path) where T : class
        {
            return stub.Requests().Where(r => r.Method == method && r.Path == path).Select(r => JsonSerializer.Deserialize<T>(r.Body, ArmadaJson.Options)!).ToList();
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

namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using Armada.Client;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// A black box entry built from a real failure fixture (src/Test.Shared/Fixtures/BlackBox) for the TUI and Helm
    /// suites: the fixture's record analyzed by the advisor, wrapped in an entry, with its user and agent briefs rendered
    /// by the server's renderer, and helpers that stub the black box routes on a <see cref="StubHttpHandler"/>.
    /// </summary>
    public sealed class BlackBoxUiFixture
    {
        #region Public-Members

        /// <summary>
        /// Entry (with its record).
        /// </summary>
        public BlackBoxEntry Entry { get; }

        /// <summary>
        /// FIX THIS brief.
        /// </summary>
        public string Brief { get; }

        /// <summary>
        /// Entry id.
        /// </summary>
        public string EntryId
        {
            get { return Entry.Id; }
        }

        /// <summary>
        /// Mission id of the entry.
        /// </summary>
        public string MissionId
        {
            get { return Entry.MissionId ?? ""; }
        }

        /// <summary>
        /// The best recovery path.
        /// </summary>
        public BlackBoxRecoveryPath FirstPath
        {
            get { return Entry.Record!.Analysis!.RecoveryPaths.OrderBy(p => p.Rank).First(); }
        }

        #endregion

        #region Constructors-and-Factories

        private BlackBoxUiFixture(BlackBoxEntry entry, string brief)
        {
            Entry = entry;
            Brief = brief;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build a fixture entry.
        /// </summary>
        /// <param name="name">Fixture name.</param>
        /// <param name="entryId">Entry id.</param>
        /// <param name="dataLossRisk">Mark the best recovery path (and its steps) with a data-loss risk.</param>
        /// <param name="withPaths">Keep the recovery paths (false clears them).</param>
        /// <returns>The fixture.</returns>
        public static BlackBoxUiFixture Load(string name = "land-001-dirty-checkout", string entryId = "bbx_fx1", bool dataLossRisk = false, bool withPaths = true)
        {
            string dir = Path.Combine(DashboardSource.RepoRoot(), "src", "Test.Shared", "Fixtures", "BlackBox");
            BlackBoxFixture fixture = BlackBoxFixtureExporter.Parse(File.ReadAllText(Path.Combine(dir, name + ".json")));
            BlackBoxRecord record = fixture.Record;
            record.EntryId = entryId;
            record.Analysis = BlackBoxAdvisor.Analyze(record, BlackBoxRuleCatalog.Default);
            if (!withPaths) record.Analysis.RecoveryPaths.Clear();
            else if (record.Analysis.RecoveryPaths.Count == 0)
            {
                BlackBoxRecoveryPath path = new BlackBoxRecoveryPath { Id = "rp1", Rank = 1, Title = "Retry landing", ExpectedOutcome = "The mission lands." };
                path.Steps.Add(new BlackBoxRecoveryStep { Order = 1, Description = "Retry landing", Action = new BlackBoxAction { Tool = "retry_landing" } });
                record.Analysis.RecoveryPaths.Add(path);
            }

            if (dataLossRisk && record.Analysis.RecoveryPaths.Count > 0)
            {
                BlackBoxRecoveryPath first = record.Analysis.RecoveryPaths.OrderBy(p => p.Rank).First();
                first.DataLossRisk = true;
                foreach (BlackBoxRecoveryStep s in first.Steps) s.DataLossRisk = true;
            }

            BlackBoxEntry entry = new BlackBoxEntry();
            entry.Id = entryId;
            entry.SubjectType = BlackBoxSubjectTypeEnum.Mission;
            entry.MissionId = record.Subject?.MissionId;
            entry.VoyageId = record.Subject?.VoyageId;
            entry.VesselId = record.Subject?.VesselId;
            entry.VesselName = record.Subject?.VesselName;
            entry.MissionTitle = record.Subject?.Title;
            entry.Attempt = 1;
            entry.IsCurrent = true;
            entry.Phase = record.Site?.Phase ?? BlackBoxPhaseEnum.Unknown;
            entry.Step = record.Site?.Step ?? BlackBoxStepEnum.Unknown;
            entry.FailureKind = record.Classification?.Kind;
            entry.StatusBefore = record.Status?.Before;
            entry.StatusAfter = record.Status?.After;
            entry.Triage = record.Analysis.Triage?.Verdict ?? BlackBoxTriageEnum.Unknown;
            entry.Fingerprint = "fp_fixture_" + name;
            entry.Resolution = BlackBoxResolutionEnum.Open;
            entry.ReasonExcerpt = record.Classification?.RawReason;
            entry.CreatedUtc = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
            entry.LastUpdateUtc = entry.CreatedUtc;
            entry.Record = record;
            BlackBoxBriefRenderer renderer = new BlackBoxBriefRenderer();
            return new BlackBoxUiFixture(entry, renderer.Render(entry, record));
        }

        /// <summary>
        /// The entry as the server sends it.
        /// </summary>
        /// <returns>JSON.</returns>
        public string EntryJson()
        {
            return ArmadaJson.Serialize(Entry);
        }

        /// <summary>
        /// The entry summary as the list routes send it.
        /// </summary>
        /// <returns>JSON.</returns>
        public string SummaryJson()
        {
            BlackBoxEntrySummary s = new BlackBoxEntrySummary();
            s.Id = Entry.Id;
            s.SubjectType = Entry.SubjectType;
            s.MissionId = Entry.MissionId;
            s.VoyageId = Entry.VoyageId;
            s.VesselId = Entry.VesselId;
            s.VesselName = Entry.VesselName;
            s.MissionTitle = Entry.MissionTitle;
            s.Attempt = Entry.Attempt;
            s.IsCurrent = true;
            s.Phase = Entry.Phase;
            s.Step = Entry.Step;
            s.FailureKind = Entry.FailureKind;
            s.Triage = Entry.Triage;
            s.Fingerprint = Entry.Fingerprint;
            s.Resolution = Entry.Resolution;
            s.ReasonExcerpt = Entry.ReasonExcerpt;
            s.CreatedUtc = Entry.CreatedUtc;
            return ArmadaJson.Serialize(s);
        }

        /// <summary>
        /// Stub the entry routes (by id), both briefs, pin, unpin, notes, resolve, and the recovery dry run and run.
        /// </summary>
        /// <param name="stub">Stub.</param>
        /// <returns>The stub.</returns>
        public StubHttpHandler StubEntry(StubHttpHandler stub)
        {
            if (stub == null) throw new ArgumentNullException(nameof(stub));
            string e = EntryId;
            stub.Json("GET", "/api/v1/blackbox/" + e, EntryJson());
            stub.Json("GET", "/api/v1/blackbox/" + e + "?includeRecord=false", EntryJson());
            stub.Json("GET", "/api/v1/blackbox/" + e + "/brief", Brief);
            stub.Json("POST", "/api/v1/blackbox/" + e + "/pin", EntryJson());
            stub.Json("POST", "/api/v1/blackbox/" + e + "/unpin", EntryJson());
            stub.Json("POST", "/api/v1/blackbox/" + e + "/notes", "{\"Id\":\"bbn_1\",\"EntryId\":\"" + e + "\",\"AuthorKind\":\"Operator\",\"Body\":\"x\"}");
            stub.Json("GET", "/api/v1/blackbox/" + e + "/notes", StubHttpHandler.PageJson("[{\"Id\":\"bbn_1\",\"EntryId\":\"" + e + "\",\"AuthorKind\":\"Operator\",\"Body\":\"Checked the dock by hand.\",\"CreatedUtc\":\"2026-10-20T12:05:00Z\"}]"));
            stub.Json("POST", "/api/v1/blackbox/" + e + "/resolve", EntryJson());
            if (Entry.Record?.Analysis?.RecoveryPaths.Count > 0) stub.On("POST", "/api/v1/blackbox/" + e + "/recovery/" + FirstPath.Id, body =>
            {
                BlackBoxRecoveryRunRequest? request = System.Text.Json.JsonSerializer.Deserialize<BlackBoxRecoveryRunRequest>(body, ArmadaJson.Options);
                bool dry = request == null || request.DryRun;
                string json = dry
                    ? "{\"EntryId\":\"" + e + "\",\"PathId\":\"" + FirstPath.Id + "\",\"DryRun\":true,\"ConfirmToken\":\"bbc_token1\",\"Plan\":[{\"Order\":1,\"Description\":\"Retry the landing\",\"Action\":{\"Tool\":\"retry_landing\"}}],\"Succeeded\":true}"
                    : "{\"EntryId\":\"" + e + "\",\"PathId\":\"" + FirstPath.Id + "\",\"DryRun\":false,\"Succeeded\":true,\"Message\":\"Landed.\",\"StepResults\":[{\"Order\":1,\"Tool\":\"retry_landing\",\"Succeeded\":true,\"Message\":\"ok\"}]}";
                return StubHttpHandler.Response(System.Net.HttpStatusCode.OK, json);
            });
            return stub;
        }

        /// <summary>
        /// Stub the entry routes plus the mission's black box routes and the mission itself.
        /// </summary>
        /// <param name="stub">Stub.</param>
        /// <param name="missionStatus">Mission status.</param>
        /// <returns>The stub.</returns>
        public StubHttpHandler StubMission(StubHttpHandler stub, string missionStatus = "LandingFailed")
        {
            StubEntry(stub);
            string m = MissionId;
            stub.Json("GET", "/api/v1/missions/" + m, "{\"Id\":\"" + m + "\",\"Title\":\"" + (Entry.MissionTitle ?? "Fixture mission") + "\",\"Status\":\"" + missionStatus + "\",\"VesselId\":\"" + Entry.VesselId
                + "\",\"Priority\":100,\"LastBlackBoxEntryId\":\"" + EntryId + "\",\"FailureReason\":\"Landing refused\",\"CreatedUtc\":\"2026-10-20T11:00:00Z\",\"LastUpdateUtc\":\"2026-10-20T12:00:00Z\"}");
            stub.Json("GET", "/api/v1/missions/" + m + "/blackbox", EntryJson());
            stub.Json("GET", "/api/v1/missions/" + m + "/blackbox/brief", Brief);
            return stub;
        }

        #endregion
    }
}

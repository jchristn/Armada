namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Helm.Commands;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// The 1.5.0 Helm reliability commands against a recording Admiral stub: <c>mission create --persona --mode</c>,
    /// <c>persona ladder</c>, <c>model-profile update</c> (each flag changes only its own field) and
    /// <c>battery report</c>.
    /// </summary>
    public sealed class HelmReliabilitySuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.HelmReliability";
        private const string Token = "reliability-token";
        private const string ProfilePath = "/api/v1/model-profiles/mdl_1";

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("mission_create_sends_persona_and_mode", "mission create --persona --mode sends both on the mission", TestTags.Positive, async () =>
            {
                using (RecordingAdmiralStub stub = new RecordingAdmiralStub())
                {
                    Mission reply = new Mission("Audit the parser", "Find defects") { Persona = "Judge", Mode = MissionModeEnum.Audit };
                    stub.MissionCreateReply = ArmadaJson.Serialize(reply);
                    HelmCliRun run = await Run(stub, "mission", "create", "Audit the parser", "--description", "Find defects", "--persona", "Judge", "--mode", "audit").ConfigureAwait(false);
                    AssertEqual(0, run.ExitCode, run.StandardError + run.Output);
                    Mission sent = Body<Mission>(stub, "POST", "/api/v1/missions");
                    AssertEqual("Judge", sent.Persona, "persona");
                    AssertEqual(MissionModeEnum.Audit, sent.Mode, "mode");
                }
            }));

            cases.Add(CaseAsync("mission_create_rejects_unknown_mode", "An unknown --mode is refused before any write", TestTags.Negative, async () =>
            {
                using (RecordingAdmiralStub stub = new RecordingAdmiralStub())
                {
                    HelmCliRun run = await Run(stub, "mission", "create", "X", "--mode", "Sprint").ConfigureAwait(false);
                    AssertEqual(1, run.ExitCode, "refused");
                    AssertFalse(stub.Requests().Any(r => r.Method == "POST" && r.Path == "/api/v1/missions"), "nothing written");
                }
            }));

            cases.Add(CaseAsync("persona_ladder_puts_parsed_rungs", "persona ladder --rung sends the rungs in order and nothing else", TestTags.Positive, async () =>
            {
                using (RecordingAdmiralStub stub = new RecordingAdmiralStub())
                {
                    Persona saved = new Persona("Worker", "persona.worker");
                    saved.EscalationLadder = new List<PersonaCaptainPriority> { new PersonaCaptainPriority { Model = "qwen3.8" } };
                    stub.Replies["PUT /api/v1/personas/Worker"] = StubAdmiralReply.Json(ArmadaJson.Serialize(saved));
                    HelmCliRun run = await Run(stub, "persona", "ladder", "Worker", "--rung", "model=qwen3.8,tier=Standard", "--rung", "runtime=ClaudeCode,tier=Premium").ConfigureAwait(false);
                    AssertEqual(0, run.ExitCode, run.StandardError + run.Output);
                    RecordedAdmiralRequest put = stub.Requests().Last(r => r.Method == "PUT");
                    PersonaLadderUpdate body = JsonSerializer.Deserialize<PersonaLadderUpdate>(put.Body, ArmadaJson.Options)!;
                    AssertEqual(2, body.EscalationLadder.Count, "two rungs");
                    AssertEqual("qwen3.8", body.EscalationLadder[0].Model, "first rung model");
                    AssertEqual(CaptainTierEnum.Standard, body.EscalationLadder[0].MinimumTier, "first rung tier");
                    AssertEqual(AgentRuntimeEnum.ClaudeCode, body.EscalationLadder[1].Runtime, "second rung runtime");
                    AssertFalse(put.Body.Contains("CaptainPriorities"), "priorities untouched");
                }
            }));

            cases.Add(CaseAsync("persona_ladder_rejects_bad_rung", "A rung with an unknown key is refused before any write", TestTags.Negative, async () =>
            {
                using (RecordingAdmiralStub stub = new RecordingAdmiralStub())
                {
                    HelmCliRun run = await Run(stub, "persona", "ladder", "Worker", "--rung", "colour=blue").ConfigureAwait(false);
                    AssertEqual(1, run.ExitCode, "refused");
                    AssertFalse(stub.Requests().Any(r => r.Method == "PUT"), "nothing written");
                }
            }));

            cases.Add(CaseAsync("model_profile_update_changes_only_named_fields", "model-profile update keeps the other overrides and clears one with -1", TestTags.Positive, async () =>
            {
                using (RecordingAdmiralStub stub = new RecordingAdmiralStub())
                {
                    ModelProfile current = new ModelProfile();
                    current.Id = "mdl_1";
                    current.Recovery = new ModelProfileRecovery { MaxResumes = 3, OutputRepairAttempts = 2 };
                    current.Timeouts = new ModelProfileTimeouts { MaxRuntimeMinutes = 300 };
                    stub.Replies["GET " + ProfilePath] = StubAdmiralReply.Json(ArmadaJson.Serialize(current));
                    stub.Replies["PUT " + ProfilePath] = StubAdmiralReply.Json(ArmadaJson.Serialize(current));
                    HelmCliRun run = await Run(stub, "model-profile", "update", "mdl_1", "--tier", "Standard", "--max-resumes", "5", "--output-repair-attempts", "-1", "--stall-minutes", "20").ConfigureAwait(false);
                    AssertEqual(0, run.ExitCode, run.StandardError + run.Output);
                    ModelProfileUpdateRequest sent = Body<ModelProfileUpdateRequest>(stub, "PUT", ProfilePath);
                    AssertEqual(CaptainTierEnum.Standard, sent.Tiering?.Tier, "tier");
                    AssertEqual(5, sent.Recovery?.MaxResumes, "changed resume count");
                    AssertNull(sent.Recovery?.OutputRepairAttempts, "-1 clears the override");
                    AssertEqual(20, sent.Timeouts?.StallThresholdMinutes, "stall minutes");
                    AssertEqual(300, sent.Timeouts?.MaxRuntimeMinutes, "untouched timeout kept");
                }
            }));

            cases.Add(CaseAsync("model_profile_update_rejects_out_of_range", "A value outside the server limits is refused before any write", TestTags.Negative, async () =>
            {
                using (RecordingAdmiralStub stub = new RecordingAdmiralStub())
                {
                    ModelProfile current = new ModelProfile();
                    current.Id = "mdl_1";
                    stub.Replies["GET " + ProfilePath] = StubAdmiralReply.Json(ArmadaJson.Serialize(current));
                    HelmCliRun run = await Run(stub, "model-profile", "update", "mdl_1", "--max-resumes", "50").ConfigureAwait(false);
                    AssertEqual(1, run.ExitCode, "refused");
                    AssertFalse(stub.Requests().Any(r => r.Method == "PUT"), "nothing written");
                }
            }));

            cases.Add(CaseAsync("battery_report_reads_latest", "battery report with no ID reads the latest gate report and prints measures", TestTags.Positive, async () =>
            {
                using (RecordingAdmiralStub stub = new RecordingAdmiralStub())
                {
                    GateReport report = new GateReport { Id = "gate-1", Passed = false, ArmadaVersion = "1.5.0", StartedUtc = DateTime.UtcNow.AddHours(-1), FinishedUtc = DateTime.UtcNow };
                    report.Pipelines.Add(new GatePipelineResult { Pipeline = "mixed-open", Status = "Completed", Passed = 7, Failed = 1 });
                    report.Measures.Add(new GateMeasure { Id = "G1", Name = "Completion rate", Value = "0.88", Threshold = ">= 0.90", Status = GateMeasureStatusEnum.Fail });
                    stub.Replies["GET /api/v1/reliability/gate-reports/latest"] = StubAdmiralReply.Json(ArmadaJson.Serialize(report));
                    HelmCliRun run = await Run(stub, "battery", "report").ConfigureAwait(false);
                    AssertEqual(0, run.ExitCode, run.StandardError + run.Output);
                    AssertContains("gate-1", run.Output, "report id");
                    AssertContains("mixed-open", run.Output, "pipeline row");
                    AssertContains("Completion rate", run.Output, "measure row");
                }
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Helm reliability commands", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static T Body<T>(RecordingAdmiralStub stub, string method, string path) where T : class
        {
            List<T> bodies = stub.Requests().Where(r => r.Method == method && r.Path == path).Select(r => JsonSerializer.Deserialize<T>(r.Body, ArmadaJson.Options)!).ToList();
            AssertTrue(bodies.Count > 0, "a " + method + " " + path + " request");
            return bodies.Last();
        }

        private static Task<HelmCliRun> Run(RecordingAdmiralStub stub, params string[] args)
        {
            List<string> all = new List<string>(args) { "--server", stub.BaseUrl, "--token", Token };
            return HelmCliHarness.RunCapturedAsync(all.ToArray());
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
            => new TestCaseDescriptor(suiteId: SuiteId, caseId: caseId, displayName: displayName, executeAsync: (CancellationToken ct) => body(), tags: new List<string> { tag });

        #endregion
    }
}

namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Worker eligibility envelope (docs/MODEL_HETEROGENEITY.md, 3.3, Phase 6.1): validation and tighten-versus-loosen
    /// rules, eligibility from the model profile, dispatch enforcement for the Worker stage, escalation to a stronger
    /// compatible captain, the decision events, the prompt placeholder and persistence. No envelope keeps routing unchanged.
    /// </summary>
    public sealed class WorkerEnvelopeSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.WorkerEnvelope";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Worker envelope suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("normalize_defaults_and_validation", "Empty or minimally deserialized envelopes are no envelope; invalid values are rejected", TestTags.Negative, () =>
            {
                AssertNull(WorkerEnvelopeRules.Normalize(null), "null is no envelope");
                AssertNull(WorkerEnvelopeRules.Normalize(new WorkerEnvelope()), "an envelope with no field is no envelope");
                WorkerEnvelope? minimal = JsonSerializer.Deserialize<WorkerEnvelope>("{}");
                AssertNotNull(minimal, "{} deserializes");
                AssertEqual(0, minimal!.RequiredTools.Count, "requiredTools defaults to empty");
                AssertNull(WorkerEnvelopeRules.Normalize(minimal), "{} is no envelope");
                AssertNull(WorkerEnvelopeRules.Normalize(JsonSerializer.Deserialize<WorkerEnvelope>("{\"requiredTools\":null}")), "null tools list is no envelope");

                WorkerEnvelope? normalized = WorkerEnvelopeRules.Normalize(new WorkerEnvelope { MaxFiles = 3, RequiredTools = new List<string> { " MCP ", "tools", "mcp" } });
                AssertNotNull(normalized, "populated envelope normalizes");
                AssertEqual("mcp,tools", String.Join(",", normalized!.RequiredTools), "tools are lower-cased, de-duplicated and sorted");

                AssertThrows<ArgumentException>(() => WorkerEnvelopeRules.Normalize(new WorkerEnvelope { MaxFiles = 0 }), "maxFiles 0 is invalid");
                AssertThrows<ArgumentException>(() => WorkerEnvelopeRules.Normalize(new WorkerEnvelope { MaxSteps = 1001 }), "maxSteps above 1000 is invalid");
                AssertThrows<ArgumentException>(() => WorkerEnvelopeRules.Normalize(new WorkerEnvelope { MinServedContextTokens = -1 }), "negative context is invalid");
                ArgumentException unknown = Catch(() => WorkerEnvelopeRules.Normalize(new WorkerEnvelope { RequiredTools = new List<string> { "browser" } }), "unknown tool is invalid");
                AssertContains("browser", unknown.Message, "the reason names the unknown tool");

                AssertNull(WorkerEnvelopeRules.FromJson(null), "null column is no envelope");
                AssertNull(WorkerEnvelopeRules.FromJson("not json"), "unreadable column is no envelope");
                AssertNull(WorkerEnvelopeRules.ToJson(new WorkerEnvelope()), "an empty envelope is stored as null");
                WorkerEnvelope? round = WorkerEnvelopeRules.FromJson(WorkerEnvelopeRules.ToJson(normalized));
                AssertEqual(3, round!.MaxFiles, "stored envelope round-trips");
                return Task.CompletedTask;
            }));

            cases.Add(Case("tighten_accepted_loosen_rejected_or_clamped", "A mission may tighten the vessel default; a looser envelope is rejected at dispatch and clamped from the Architect", TestTags.Negative, () =>
            {
                WorkerEnvelope vesselDefault = new WorkerEnvelope { MaxFiles = 5, MaxSteps = 8, MinServedContextTokens = 32768, RequiredTools = new List<string> { "tools" } };
                WorkerEnvelope tighter = new WorkerEnvelope { MaxFiles = 3, MaxSteps = 6, MinServedContextTokens = 65536, RequiredTools = new List<string> { "tools", "mcp" } };
                AssertEqual(0, WorkerEnvelopeRules.Loosenings(vesselDefault, tighter).Count, "every field tighter is no loosening");
                WorkerEnvelope? accepted = WorkerEnvelopeRules.ValidateMissionEnvelope(vesselDefault, tighter);
                AssertEqual(3, accepted!.MaxFiles, "the tighter envelope is kept");
                AssertNull(WorkerEnvelopeRules.ValidateMissionEnvelope(vesselDefault, null), "no mission envelope uses the default");
                AssertEqual(10, WorkerEnvelopeRules.ValidateMissionEnvelope(null, new WorkerEnvelope { MaxFiles = 10 })!.MaxFiles, "nothing is looser than no vessel default");

                WorkerEnvelope looser = new WorkerEnvelope { MaxFiles = 9, MinServedContextTokens = 8192 };
                List<string> reasons = WorkerEnvelopeRules.Loosenings(vesselDefault, looser);
                AssertEqual(4, reasons.Count, "more files, dropped steps, smaller context and a dropped tool are four loosenings: " + String.Join(" | ", reasons));
                ArgumentException rejected = Catch(() => WorkerEnvelopeRules.ValidateMissionEnvelope(vesselDefault, looser), "a loosened envelope is rejected");
                AssertContains("never loosen", rejected.Message, "the rejection says why");
                AssertContains("maxFiles 9", rejected.Message, "the rejection names the loosened field");

                WorkerEnvelope? clamped = WorkerEnvelopeRules.Tighten(vesselDefault, looser, out List<string> clampReasons);
                AssertEqual(4, clampReasons.Count, "the clamp reports every loosened field");
                AssertEqual(5, clamped!.MaxFiles, "files clamp to the default");
                AssertEqual(8, clamped.MaxSteps, "a dropped step limit keeps the default");
                AssertEqual(32768, clamped.MinServedContextTokens, "context clamps to the default minimum");
                AssertEqual("tools", String.Join(",", clamped.RequiredTools), "the default's tools stay required");

                WorkerEnvelope? mixed = WorkerEnvelopeRules.Tighten(vesselDefault, new WorkerEnvelope { MaxFiles = 2 }, out List<string> mixedReasons);
                AssertEqual(2, mixed!.MaxFiles, "a tighter field is kept");
                AssertEqual(8, mixed.MaxSteps, "an omitted field keeps the default");
                AssertEqual(3, mixedReasons.Count, "omitting a field the default sets is reported (steps, context, tool)");
                return Task.CompletedTask;
            }));

            cases.Add(Case("eligibility_from_model_profile", "Eligibility: no profile, missing context, small context and missing tools exclude; a matching profile is eligible", TestTags.Negative, () =>
            {
                WorkerEnvelope envelope = new WorkerEnvelope { MinServedContextTokens = 32768, RequiredTools = new List<string> { "tools", "mcp" } };
                Captain captain = new Captain("eligibility");
                AssertContains("no model profile", WorkerEnvelopeRules.IneligibilityReason(envelope, captain, null) ?? "", "no profile is excluded");
                captain.ModelProfileId = "mdl_missing";
                AssertContains("not found", WorkerEnvelopeRules.IneligibilityReason(envelope, captain, null) ?? "", "a dangling profile is excluded");

                ModelProfile profile = Profile(null, null, null);
                AssertContains("records no served context", WorkerEnvelopeRules.IneligibilityReason(envelope, captain, profile) ?? "", "missing served context is excluded");
                profile = Profile(16384, true, true);
                AssertContains("below the Worker envelope minimum", WorkerEnvelopeRules.IneligibilityReason(envelope, captain, profile) ?? "", "small context is excluded");
                profile = Profile(131072, true, null);
                AssertContains("does not record MCP tools", WorkerEnvelopeRules.IneligibilityReason(envelope, captain, profile) ?? "", "an unrecorded tool is excluded");
                profile = Profile(131072, false, true);
                AssertContains("has no tool calls", WorkerEnvelopeRules.IneligibilityReason(envelope, captain, profile) ?? "", "an unavailable tool is excluded");
                profile = Profile(131072, true, true);
                AssertNull(WorkerEnvelopeRules.IneligibilityReason(envelope, captain, profile), "a matching profile is eligible");

                Captain noProfile = new Captain("files-only");
                AssertNull(WorkerEnvelopeRules.IneligibilityReason(new WorkerEnvelope { MaxFiles = 3, MaxSteps = 6 }, noProfile, null), "file and step limits never exclude a captain");
                AssertNull(WorkerEnvelopeRules.IneligibilityReason(null, noProfile, null), "no envelope never excludes");
                return Task.CompletedTask;
            }));

            cases.Add(Case("prompt_placeholder_for_architect_and_judge", "The {WorkerEnvelope} placeholder is empty without an envelope and gives the Architect and Judge a generated description", TestTags.Positive, () =>
            {
                Vessel vessel = new Vessel("prompt-vessel", "https://github.com/test/repo.git");
                Mission architect = new Mission("plan", "plan it") { Persona = "Architect" };
                AssertTrue(MissionPromptBuilder.TemplateParameterNames.Contains("WorkerEnvelope"), "the placeholder is listed");
                AssertEqual("", MissionPromptBuilder.BuildTemplateParams(architect, vessel, null, null, null, null)["WorkerEnvelope"], "no envelope renders nothing");

                vessel.WorkerEnvelope = new WorkerEnvelope { MaxFiles = 3, MaxSteps = 6, MinServedContextTokens = 32768, RequiredTools = new List<string> { "tools" } };
                string architectText = MissionPromptBuilder.BuildTemplateParams(architect, vessel, null, null, null, null)["WorkerEnvelope"];
                AssertContains("## Worker Eligibility Envelope", architectText, "a heading");
                AssertContains("at most 3 files changed; at most 6 steps; Worker served context at least 32,768 tokens; Worker must have tool calls", architectText, "the generated description");
                AssertContains("never loosen", architectText, "the Architect is told it may only tighten");

                Mission worker = new Mission("work", "do it") { Persona = "Worker", WorkerEnvelope = new WorkerEnvelope { MaxFiles = 2 } };
                Mission judge = new Mission("review", "review it") { Persona = "Judge" };
                string judgeText = MissionPromptBuilder.BuildTemplateParams(judge, vessel, null, null, worker, null)["WorkerEnvelope"];
                AssertContains("at most 2 files changed", judgeText, "the Judge sees the Worker stage's tightened envelope");
                AssertContains("say which limit", judgeText, "the Judge is told to make findings actionable");
                AssertTrue(judgeText.All(c => c < 128), "the generated text is ASCII");
                return Task.CompletedTask;
            }));

            cases.Add(Case("templates_carry_placeholder_and_upgrade", "Built-in Architect and Judge templates (Current, Lean, Compact) carry {WorkerEnvelope}; an untouched seeded copy is upgraded and an edited one is kept", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    LoggingModule logging = new LoggingModule();
                    logging.Settings.EnableConsole = false;
                    PromptTemplateService templates = new PromptTemplateService(testDb.Driver, logging);
                    string[] names = { "persona.architect", "persona.judge", "persona.architect.lean", "persona.judge.lean", "persona.architect.compact", "persona.judge.compact" };
                    foreach (string name in names)
                        AssertContains("{WorkerEnvelope}", templates.GetEmbeddedDefault(name) ?? "", name + " carries the placeholder");
                    AssertFalse((templates.GetEmbeddedDefault("persona.worker") ?? "").Contains("{WorkerEnvelope}"), "the Worker template is unchanged");

                    await templates.SeedDefaultsAsync().ConfigureAwait(false);
                    PromptTemplate architect = (await testDb.Driver.PromptTemplates.ReadByNameAsync("persona.architect").ConfigureAwait(false))!;
                    PromptTemplate judge = (await testDb.Driver.PromptTemplates.ReadByNameAsync("persona.judge").ConfigureAwait(false))!;
                    AssertContains("{WorkerEnvelope}", architect.Content, "seeded with the placeholder");
                    architect.Content = architect.Content.Replace("{WorkerEnvelope}", "");
                    await testDb.Driver.PromptTemplates.UpdateAsync(architect).ConfigureAwait(false);
                    judge.Content = "an operator's own judge prompt";
                    await testDb.Driver.PromptTemplates.UpdateAsync(judge).ConfigureAwait(false);

                    await new PromptTemplateService(testDb.Driver, logging).SeedDefaultsAsync().ConfigureAwait(false);
                    AssertContains("{WorkerEnvelope}", (await testDb.Driver.PromptTemplates.ReadByNameAsync("persona.architect").ConfigureAwait(false))!.Content, "an untouched pre-envelope copy is upgraded");
                    AssertEqual("an operator's own judge prompt", (await testDb.Driver.PromptTemplates.ReadByNameAsync("persona.judge").ConfigureAwait(false))!.Content, "an edited template is kept");
                }
            }));

            cases.Add(Case("persistence_round_trip", "Vessel default and mission envelope persist (migration 110) and clear back to null", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    Vessel vessel = new Vessel("persist-vessel", "https://github.com/test/repo.git");
                    vessel.WorkerEnvelope = new WorkerEnvelope { MaxFiles = 4, RequiredTools = new List<string> { "mcp" } };
                    vessel = await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);
                    Vessel? read = await testDb.Driver.Vessels.ReadAsync(vessel.Id).ConfigureAwait(false);
                    AssertEqual(4, read!.WorkerEnvelope!.MaxFiles, "vessel envelope persists");
                    AssertEqual("mcp", String.Join(",", read.WorkerEnvelope.RequiredTools), "required tools persist");
                    read.WorkerEnvelope = null;
                    await testDb.Driver.Vessels.UpdateAsync(read).ConfigureAwait(false);
                    AssertNull((await testDb.Driver.Vessels.ReadAsync(vessel.Id).ConfigureAwait(false))!.WorkerEnvelope, "clearing persists as null");

                    Mission mission = new Mission("persist", "x") { VesselId = vessel.Id, WorkerEnvelope = new WorkerEnvelope { MinServedContextTokens = 65536 } };
                    mission = await testDb.Driver.Missions.CreateAsync(mission).ConfigureAwait(false);
                    Mission? readMission = await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                    AssertEqual(65536, readMission!.WorkerEnvelope!.MinServedContextTokens, "mission envelope persists");
                    readMission.WorkerEnvelope = new WorkerEnvelope { MaxSteps = 2 };
                    await testDb.Driver.Missions.UpdateAsync(readMission).ConfigureAwait(false);
                    AssertEqual(2, (await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!.WorkerEnvelope!.MaxSteps, "mission envelope updates");
                    Mission plain = await testDb.Driver.Missions.CreateAsync(new Mission("plain", "x") { VesselId = vessel.Id }).ConfigureAwait(false);
                    AssertNull((await testDb.Driver.Missions.ReadAsync(plain.Id).ConfigureAwait(false))!.WorkerEnvelope, "a mission without an envelope reads null");
                }
            }));

            cases.Add(Case("no_envelope_routing_unchanged", "Without an envelope a captain with no model profile is assigned and no envelope event is recorded", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    Harness harness = await BuildHarnessAsync(testDb.Driver, null).ConfigureAwait(false);
                    Captain plain = await AddCaptainAsync(testDb.Driver, "plain", CaptainStateEnum.Idle, CaptainTierEnum.Economy, null).ConfigureAwait(false);
                    Mission mission = await AddMissionAsync(testDb.Driver, harness.Vessel, null).ConfigureAwait(false);
                    AssertTrue(await harness.Missions.TryAssignAsync(mission, harness.Vessel).ConfigureAwait(false), "assigned");
                    AssertEqual(plain.Id, (await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!.CaptainId, "the only captain is assigned as before");
                    AssertEqual(0, (await EnvelopeEventsAsync(testDb.Driver, mission.Id).ConfigureAwait(false)).Count, "no envelope decision without an envelope");

                    // File and step limits alone guide slicing but never change routing.
                    harness.Vessel.WorkerEnvelope = new WorkerEnvelope { MaxFiles = 3, MaxSteps = 6 };
                    await testDb.Driver.Vessels.UpdateAsync(harness.Vessel).ConfigureAwait(false);
                    Captain second = await AddCaptainAsync(testDb.Driver, "second", CaptainStateEnum.Idle, CaptainTierEnum.Economy, null).ConfigureAwait(false);
                    Mission next = await AddMissionAsync(testDb.Driver, harness.Vessel, null).ConfigureAwait(false);
                    AssertTrue(await harness.Missions.TryAssignAsync(next, harness.Vessel).ConfigureAwait(false), "files/steps-only envelope still assigns");
                    AssertEqual(second.Id, (await testDb.Driver.Missions.ReadAsync(next.Id).ConfigureAwait(false))!.CaptainId, "a captain without a profile still runs it");
                }
            }));

            cases.Add(Case("envelope_filters_ineligible_captains", "With an envelope only a captain whose profile meets it is assigned, and the exclusions are recorded", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    Harness harness = await BuildHarnessAsync(testDb.Driver, new WorkerEnvelope { MinServedContextTokens = 32768, RequiredTools = new List<string> { "tools" } }).ConfigureAwait(false);
                    ModelProfile small = await AddProfileAsync(testDb.Driver, "small", 8192, true, null).ConfigureAwait(false);
                    ModelProfile noTools = await AddProfileAsync(testDb.Driver, "notools", 131072, null, null).ConfigureAwait(false);
                    ModelProfile good = await AddProfileAsync(testDb.Driver, "good", 131072, true, null).ConfigureAwait(false);
                    Captain a = await AddCaptainAsync(testDb.Driver, "a-none", CaptainStateEnum.Idle, CaptainTierEnum.Economy, null).ConfigureAwait(false);
                    Captain b = await AddCaptainAsync(testDb.Driver, "b-small", CaptainStateEnum.Idle, CaptainTierEnum.Economy, small.Id).ConfigureAwait(false);
                    Captain c = await AddCaptainAsync(testDb.Driver, "c-notools", CaptainStateEnum.Idle, CaptainTierEnum.Economy, noTools.Id).ConfigureAwait(false);
                    Captain d = await AddCaptainAsync(testDb.Driver, "d-good", CaptainStateEnum.Idle, CaptainTierEnum.Economy, good.Id).ConfigureAwait(false);
                    Mission mission = await AddMissionAsync(testDb.Driver, harness.Vessel, null).ConfigureAwait(false);

                    MissionAssignmentBlocker? before = await harness.Missions.GetAssignmentBlockerAsync(mission).ConfigureAwait(false);
                    AssertNotNull(before, "blocker before assignment");
                    AssertEqual(MissionAssignmentBlockerReasonEnum.AwaitingDispatch, before!.Reason, "the blocker agrees an eligible captain is idle");

                    AssertTrue(await harness.Missions.TryAssignAsync(mission, harness.Vessel).ConfigureAwait(false), "assigned");
                    AssertEqual(d.Id, (await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!.CaptainId, "only the matching profile is eligible");

                    List<WorkerEnvelopeDecision> decisions = await EnvelopeEventsAsync(testDb.Driver, mission.Id).ConfigureAwait(false);
                    AssertEqual(1, decisions.Count, "one decision event");
                    AssertEqual(WorkerEnvelopeOutcomeEnum.Assigned, decisions[0].Outcome, "assigned within the envelope");
                    AssertEqual(3, decisions[0].Excluded.Count, "three captains excluded");
                    AssertContains("no model profile", decisions[0].Excluded.First(e => e.CaptainId == a.Id).Reason, "profile missing");
                    AssertContains("below the Worker envelope minimum", decisions[0].Excluded.First(e => e.CaptainId == b.Id).Reason, "context too small");
                    AssertContains("does not record tool calls", decisions[0].Excluded.First(e => e.CaptainId == c.Id).Reason, "tools missing");
                }
            }));

            cases.Add(Case("judge_stage_not_constrained", "The envelope applies to the Worker stage only: a Judge is routed as before", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    Harness harness = await BuildHarnessAsync(testDb.Driver, new WorkerEnvelope { MinServedContextTokens = 32768 }).ConfigureAwait(false);
                    Captain plain = await AddCaptainAsync(testDb.Driver, "judge-captain", CaptainStateEnum.Idle, CaptainTierEnum.Economy, null).ConfigureAwait(false);
                    Mission judge = await AddMissionAsync(testDb.Driver, harness.Vessel, null, "Judge").ConfigureAwait(false);
                    AssertTrue(await harness.Missions.TryAssignAsync(judge, harness.Vessel).ConfigureAwait(false), "judge assigned");
                    AssertEqual(plain.Id, (await testDb.Driver.Missions.ReadAsync(judge.Id).ConfigureAwait(false))!.CaptainId, "a captain without a profile reviews");
                }
            }));

            cases.Add(Case("escalates_to_stronger_compatible_captain", "When no idle captain routing would use meets the envelope, a stronger compatible captain is assigned instead of waiting", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    Harness harness = await BuildHarnessAsync(testDb.Driver, new WorkerEnvelope { MinServedContextTokens = 32768 }).ConfigureAwait(false);
                    ModelProfile good = await AddProfileAsync(testDb.Driver, "good", 131072, true, true).ConfigureAwait(false);
                    ModelProfile small = await AddProfileAsync(testDb.Driver, "small", 4096, true, true).ConfigureAwait(false);
                    // The Worker persona prefers the Codex captain and waits when it is busy (no general fallback).
                    Persona persona = new Persona("Worker", "persona.worker");
                    persona.CaptainPriorities.Add(new PersonaCaptainPriority { Runtime = AgentRuntimeEnum.Codex });
                    await testDb.Driver.Personas.CreateAsync(persona).ConfigureAwait(false);
                    Captain preferred = await AddCaptainAsync(testDb.Driver, "preferred-busy", CaptainStateEnum.Working, CaptainTierEnum.Economy, good.Id, AgentRuntimeEnum.Codex).ConfigureAwait(false);
                    Captain sameTier = await AddCaptainAsync(testDb.Driver, "same-tier", CaptainStateEnum.Idle, CaptainTierEnum.Economy, good.Id).ConfigureAwait(false);
                    Captain tooSmall = await AddCaptainAsync(testDb.Driver, "premium-small", CaptainStateEnum.Idle, CaptainTierEnum.Premium, small.Id).ConfigureAwait(false);
                    Captain premium = await AddCaptainAsync(testDb.Driver, "premium-good", CaptainStateEnum.Idle, CaptainTierEnum.Premium, good.Id).ConfigureAwait(false);
                    Captain standard = await AddCaptainAsync(testDb.Driver, "standard-good", CaptainStateEnum.Idle, CaptainTierEnum.Standard, good.Id).ConfigureAwait(false);
                    Captain judgeOnly = await AddCaptainAsync(testDb.Driver, "standard-judge-only", CaptainStateEnum.Idle, CaptainTierEnum.Standard, good.Id).ConfigureAwait(false);
                    judgeOnly.AllowedPersonas = "[\"Judge\"]";
                    await testDb.Driver.Captains.UpdateAsync(judgeOnly).ConfigureAwait(false);
                    Mission mission = await AddMissionAsync(testDb.Driver, harness.Vessel, null).ConfigureAwait(false);

                    MissionAssignmentBlocker? blocker = await harness.Missions.GetAssignmentBlockerAsync(mission).ConfigureAwait(false);
                    AssertEqual(MissionAssignmentBlockerReasonEnum.AwaitingDispatch, blocker!.Reason, "the blocker agrees escalation can assign");

                    AssertTrue(await harness.Missions.TryAssignAsync(mission, harness.Vessel).ConfigureAwait(false), "escalated instead of waiting");
                    Mission read = (await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!;
                    AssertEqual(standard.Id, read.CaptainId, "the lowest tier above the busy preferred captain that meets the envelope and may run Worker");
                    AssertNull(read.RequestedCaptainId, "escalation does not pin a captain");

                    List<WorkerEnvelopeDecision> decisions = await EnvelopeEventsAsync(testDb.Driver, mission.Id).ConfigureAwait(false);
                    AssertEqual(1, decisions.Count, "one decision event");
                    AssertEqual(WorkerEnvelopeOutcomeEnum.Escalated, decisions[0].Outcome, "escalated");
                    AssertEqual(CaptainTierEnum.Standard, decisions[0].CaptainTier, "escalated tier recorded");
                    AssertEqual(CaptainTierEnum.Economy, decisions[0].BaselineTier, "baseline is the busy preferred captain's tier");
                    AssertTrue(decisions[0].Excluded.Any(e => e.CaptainId == tooSmall.Id), "the incompatible premium captain is listed as excluded");
                    AssertContains("escalated to standard-good", decisions[0].Summary, "summary names the captain");
                    AssertTrue(sameTier.Id != read.CaptainId && premium.Id != read.CaptainId && preferred.Id != read.CaptainId, "not a lateral or the strongest captain");
                }
            }));

            cases.Add(Case("waits_when_compatible_captain_busy", "With no stronger compatible idle captain the mission waits; the decision is recorded once per change", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    Harness harness = await BuildHarnessAsync(testDb.Driver, new WorkerEnvelope { RequiredTools = new List<string> { "mcp" } }).ConfigureAwait(false);
                    ModelProfile good = await AddProfileAsync(testDb.Driver, "good", 131072, true, true).ConfigureAwait(false);
                    await AddCaptainAsync(testDb.Driver, "busy-good", CaptainStateEnum.Working, CaptainTierEnum.Premium, good.Id).ConfigureAwait(false);
                    await AddCaptainAsync(testDb.Driver, "idle-none", CaptainStateEnum.Idle, CaptainTierEnum.Premium, null).ConfigureAwait(false);
                    Mission mission = await AddMissionAsync(testDb.Driver, harness.Vessel, null).ConfigureAwait(false);

                    AssertFalse(await harness.Missions.TryAssignAsync(mission, harness.Vessel).ConfigureAwait(false), "not assigned");
                    AssertFalse(await harness.Missions.TryAssignAsync(mission, harness.Vessel).ConfigureAwait(false), "second tick not assigned");
                    Mission read = (await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!;
                    AssertEqual(MissionStatusEnum.Pending, read.Status, "stays Pending");
                    AssertNull(read.CaptainId, "no captain");
                    List<WorkerEnvelopeDecision> decisions = await EnvelopeEventsAsync(testDb.Driver, mission.Id).ConfigureAwait(false);
                    AssertEqual(1, decisions.Count, "an unchanged decision is recorded once");
                    AssertEqual(WorkerEnvelopeOutcomeEnum.Waiting, decisions[0].Outcome, "waiting for the compatible captain");

                    MissionAssignmentBlocker? blocker = await harness.Missions.GetAssignmentBlockerAsync(read).ConfigureAwait(false);
                    AssertEqual(MissionAssignmentBlockerReasonEnum.NoEligibleCaptain, blocker!.Reason, "idle captains exist but none may take it");
                    MissionAssignmentCaptainStatus? idleStatus = blocker.Captains.FirstOrDefault(c => c.CaptainName == "idle-none");
                    AssertNotNull(idleStatus, "the idle captain is described");
                    AssertContains("no model profile", idleStatus!.Detail ?? "", "the blocker names the envelope reason");
                }
            }));

            cases.Add(Case("no_compatible_captain_stays_pending_with_reason", "When no captain at all meets the envelope the mission stays Pending with a visible reason", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    Harness harness = await BuildHarnessAsync(testDb.Driver, new WorkerEnvelope { MinServedContextTokens = 200000 }).ConfigureAwait(false);
                    ModelProfile small = await AddProfileAsync(testDb.Driver, "small", 131072, true, true).ConfigureAwait(false);
                    Captain requested = await AddCaptainAsync(testDb.Driver, "requested-small", CaptainStateEnum.Idle, CaptainTierEnum.Premium, small.Id).ConfigureAwait(false);
                    await AddCaptainAsync(testDb.Driver, "idle-none", CaptainStateEnum.Idle, CaptainTierEnum.Economy, null).ConfigureAwait(false);
                    Mission mission = await AddMissionAsync(testDb.Driver, harness.Vessel, requested.Id).ConfigureAwait(false);

                    AssertFalse(await harness.Missions.TryAssignAsync(mission, harness.Vessel).ConfigureAwait(false), "a requested captain that misses the envelope is not assigned");
                    Mission read = (await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!;
                    AssertEqual(MissionStatusEnum.Pending, read.Status, "stays Pending");
                    MissionAssignmentBlocker? blocker = await harness.Missions.GetAssignmentBlockerAsync(read).ConfigureAwait(false);
                    AssertEqual(MissionAssignmentBlockerReasonEnum.NoEligibleCaptain, blocker!.Reason, "no eligible captain");
                    AssertContains("no captain meets the Worker eligibility envelope", blocker.Summary ?? "", "visible reason");
                    AssertContains("at least 200,000 tokens", blocker.Summary ?? "", "the reason describes the envelope");
                    List<WorkerEnvelopeDecision> decisions = await EnvelopeEventsAsync(testDb.Driver, mission.Id).ConfigureAwait(false);
                    AssertEqual(WorkerEnvelopeOutcomeEnum.NoEligibleCaptain, decisions[decisions.Count - 1].Outcome, "decision recorded");
                    AssertEqual(2, decisions[decisions.Count - 1].Excluded.Count, "both captains excluded with reasons");
                }
            }));

            cases.Add(Case("mission_envelope_tightens_vessel_default", "A mission's tighter envelope is combined with the vessel default at dispatch", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    Harness harness = await BuildHarnessAsync(testDb.Driver, new WorkerEnvelope { MinServedContextTokens = 32768 }).ConfigureAwait(false);
                    ModelProfile mid = await AddProfileAsync(testDb.Driver, "mid", 65536, true, null).ConfigureAwait(false);
                    ModelProfile big = await AddProfileAsync(testDb.Driver, "big", 262144, true, null).ConfigureAwait(false);
                    await AddCaptainAsync(testDb.Driver, "a-mid", CaptainStateEnum.Idle, CaptainTierEnum.Economy, mid.Id).ConfigureAwait(false);
                    Captain bigCaptain = await AddCaptainAsync(testDb.Driver, "b-big", CaptainStateEnum.Idle, CaptainTierEnum.Economy, big.Id).ConfigureAwait(false);
                    Mission mission = await AddMissionAsync(testDb.Driver, harness.Vessel, null).ConfigureAwait(false);
                    mission.WorkerEnvelope = new WorkerEnvelope { MinServedContextTokens = 131072 };
                    await testDb.Driver.Missions.UpdateAsync(mission).ConfigureAwait(false);
                    AssertTrue(await harness.Missions.TryAssignAsync(mission, harness.Vessel).ConfigureAwait(false), "assigned");
                    AssertEqual(bigCaptain.Id, (await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!.CaptainId, "the mission's larger minimum applies");

                    // A stored looser mission envelope cannot loosen the vessel default at dispatch.
                    WorkerEnvelope? effective = MissionService.EffectiveWorkerEnvelope(new Mission { WorkerEnvelope = new WorkerEnvelope { MinServedContextTokens = 1024 } }, harness.Vessel);
                    AssertEqual(32768, effective!.MinServedContextTokens, "the tightest of both applies");
                }
            }));

            return new TestSuiteDescriptor(SuiteId, "Worker eligibility envelope", cases);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        private static ArgumentException Catch(Action action, string label)
        {
            try
            {
                action();
            }
            catch (ArgumentException ex)
            {
                return ex;
            }
            throw new Exception(label + ": expected an ArgumentException");
        }

        private static ModelProfile Profile(int? servedContext, bool? tools, bool? mcp)
        {
            ModelProfile profile = new ModelProfile();
            profile.Identity = new ModelProfileIdentity { Runtime = "ClaudeCode", Model = "m", ServedContextTokens = servedContext };
            profile.Capabilities = new ModelProfileCapabilities { ToolsAvailable = tools, McpAvailable = mcp };
            return profile;
        }

        private static async Task<ModelProfile> AddProfileAsync(DatabaseDriver db, string key, int? servedContext, bool? tools, bool? mcp)
        {
            return await db.ModelProfiles.CreateAsync(Profile(servedContext, tools, mcp), "envelope-" + key + "-" + Guid.NewGuid().ToString("N")).ConfigureAwait(false);
        }

        private static async Task<Harness> BuildHarnessAsync(DatabaseDriver db, WorkerEnvelope? vesselEnvelope)
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;

            ArmadaSettings settings = new ArmadaSettings();
            settings.DocksDirectory = Path.Combine(Path.GetTempPath(), "armada_envelope_docks_" + Guid.NewGuid().ToString("N"));
            settings.ReposDirectory = Path.Combine(Path.GetTempPath(), "armada_envelope_repos_" + Guid.NewGuid().ToString("N"));

            DirCreatingGitService git = new DirCreatingGitService();
            IDockService dockService = new DockService(logging, db, settings, git);
            CaptainService captainService = new CaptainService(logging, db, settings, git, dockService);
            MissionService missionService = new MissionService(logging, db, settings, dockService, captainService, git: git);

            int nextPid = 6000;
            captainService.OnLaunchAgent = (_, _, _) =>
            {
                nextPid++;
                return Task.FromResult(nextPid);
            };
            missionService.OnGetMissionOutput = _ => "envelope output";

            Vessel vessel = new Vessel("envelope-vessel", "https://github.com/test/repo.git");
            vessel.LocalPath = Path.Combine(Path.GetTempPath(), "armada_envelope_bare_" + Guid.NewGuid().ToString("N"));
            vessel.WorkingDirectory = Path.Combine(Path.GetTempPath(), "armada_envelope_work_" + Guid.NewGuid().ToString("N"));
            vessel.DefaultBranch = "main";
            vessel.AllowConcurrentMissions = true;
            vessel.WorkerEnvelope = vesselEnvelope;
            vessel = await db.Vessels.CreateAsync(vessel).ConfigureAwait(false);

            return new Harness { Missions = missionService, Vessel = vessel };
        }

        private static async Task<Captain> AddCaptainAsync(DatabaseDriver db, string name, CaptainStateEnum state, CaptainTierEnum? tier, string? profileId, AgentRuntimeEnum runtime = AgentRuntimeEnum.ClaudeCode)
        {
            Captain captain = new Captain(name);
            captain.State = state;
            captain.Tier = tier;
            captain.Runtime = runtime;
            captain.ModelProfileId = profileId;
            captain = await db.Captains.CreateAsync(captain).ConfigureAwait(false);
            if (profileId != null) await db.ModelProfiles.LinkCaptainAsync(captain.Id, profileId).ConfigureAwait(false);
            return captain;
        }

        private static async Task<Mission> AddMissionAsync(DatabaseDriver db, Vessel vessel, string? requestedCaptainId, string persona = "Worker")
        {
            Mission mission = new Mission("envelope " + persona, "do the work");
            mission.VesselId = vessel.Id;
            mission.Persona = persona;
            mission.RequestedCaptainId = requestedCaptainId;
            mission.Status = MissionStatusEnum.Pending;
            return await db.Missions.CreateAsync(mission).ConfigureAwait(false);
        }

        private static async Task<List<WorkerEnvelopeDecision>> EnvelopeEventsAsync(DatabaseDriver db, string missionId)
        {
            List<ArmadaEvent> events = await db.Events.EnumerateByMissionAsync(missionId, 100).ConfigureAwait(false);
            return events.Where(e => e.EventType == WorkerEnvelopeRules.EventType)
                .OrderBy(e => e.CreatedUtc)
                .Select(e => JsonSerializer.Deserialize<WorkerEnvelopeDecision>(e.Payload ?? "{}")!)
                .ToList();
        }

        #endregion

        #region Private-Types

        private sealed class Harness
        {
            public MissionService Missions { get; set; } = null!;
            public Vessel Vessel { get; set; } = null!;
        }

        #endregion
    }
}

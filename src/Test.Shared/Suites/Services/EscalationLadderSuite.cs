namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
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
    /// Descriptors for the escalation ladder (RS-5.6): a work stage moves to the next rung of its persona's ladder, skips
    /// rungs with no eligible captain, stops at the top, takes at most MaxEscalations hops, escalates standalone missions,
    /// and never escalates planning or review stages.
    /// </summary>
    public sealed class EscalationLadderSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the escalation ladder suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("ladder_moves_up_one_rung", "Qwen3.8 escalates to GPT-6.1, GPT-6.1 to Opus, and Opus at the top does not escalate", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                await CreateLadderPersonaAsync(testDb).ConfigureAwait(false);
                Captain qwen = await CreateCaptainAsync(testDb, "qwen", "qwen3.8").ConfigureAwait(false);
                Captain gpt = await CreateCaptainAsync(testDb, "gpt", "gpt-6.1").ConfigureAwait(false);
                Captain opus = await CreateCaptainAsync(testDb, "opus", "claude-opus-5-5").ConfigureAwait(false);
                Mission mission = new Mission("Build", "Build it") { Persona = "Worker" };

                EscalationChoice? first = await CaptainEscalationSelector.ChooseAsync(testDb.Driver, mission, qwen.Id, new List<Mission>()).ConfigureAwait(false);
                AssertEqual(gpt.Id, first!.Captain.Id);
                AssertEqual(1, first.Rung);
                AssertEqual("escalation ladder rung 2 of 3", first.Describe());
                EscalationChoice? second = await CaptainEscalationSelector.ChooseAsync(testDb.Driver, mission, gpt.Id, new List<Mission>()).ConfigureAwait(false);
                AssertEqual(opus.Id, second!.Captain.Id);
                AssertNull(await CaptainEscalationSelector.ChooseAsync(testDb.Driver, mission, opus.Id, new List<Mission>()).ConfigureAwait(false), "the top rung has nowhere to go");
            }));

            cases.Add(CaseAsync("ladder_skips_rung_without_eligible_captain", "A rung whose captains are quarantined is skipped, and an idle captain is preferred over a busy one on a rung", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                await CreateLadderPersonaAsync(testDb).ConfigureAwait(false);
                Captain qwen = await CreateCaptainAsync(testDb, "qwen", "qwen3.8").ConfigureAwait(false);
                Captain gpt = await CreateCaptainAsync(testDb, "gpt", "gpt-6.1", CaptainStateEnum.Quarantined).ConfigureAwait(false);
                Captain busyOpus = await CreateCaptainAsync(testDb, "opus-a", "claude-opus-5-5", CaptainStateEnum.Working).ConfigureAwait(false);
                Captain idleOpus = await CreateCaptainAsync(testDb, "opus-b", "claude-opus-5-5").ConfigureAwait(false);
                Mission mission = new Mission("Build", "Build it") { Persona = "Worker" };
                EscalationChoice? choice = await CaptainEscalationSelector.ChooseAsync(testDb.Driver, mission, qwen.Id, new List<Mission>()).ConfigureAwait(false);
                AssertEqual(idleOpus.Id, choice!.Captain.Id, "the quarantined GPT rung is skipped and the idle Opus wins");
                AssertEqual(2, choice.Rung);
                AssertNotEqual(gpt.Id, choice.Captain.Id);
                AssertNotEqual(busyOpus.Id, choice.Captain.Id);
            }));

            cases.Add(CaseAsync("captain_off_the_ladder_starts_at_rung_one", "A failed captain that matches no rung escalates to the first rung", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                await CreateLadderPersonaAsync(testDb).ConfigureAwait(false);
                Captain other = await CreateCaptainAsync(testDb, "other", "some-local-model").ConfigureAwait(false);
                Captain qwen = await CreateCaptainAsync(testDb, "qwen", "qwen3.8").ConfigureAwait(false);
                EscalationChoice? choice = await CaptainEscalationSelector.ChooseAsync(testDb.Driver, new Mission("Build", "Build it") { Persona = "Worker" }, other.Id, new List<Mission>()).ConfigureAwait(false);
                AssertEqual(qwen.Id, choice!.Captain.Id);
                AssertEqual(0, choice.Rung);
            }));

            cases.Add(CaseAsync("choose_escalation_guards", "Standalone missions escalate; planning and review stages, spent hops and MaxEscalations 0 do not", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = new ArmadaSettings();
                MissionService missions = CreateMissionService(testDb, settings);
                await CreateLadderPersonaAsync(testDb).ConfigureAwait(false);
                Captain qwen = await CreateCaptainAsync(testDb, "qwen", "qwen3.8").ConfigureAwait(false);
                Captain gpt = await CreateCaptainAsync(testDb, "gpt", "gpt-6.1").ConfigureAwait(false);

                Mission standalone = new Mission("Build", "Build it") { Persona = "Worker" };
                AssertEqual(gpt.Id, (await missions.ChooseEscalationAsync(standalone, qwen.Id).ConfigureAwait(false))!.Captain.Id, "a mission with no voyage escalates");
                standalone.EscalationCount = 2;
                AssertNull(await missions.ChooseEscalationAsync(standalone, qwen.Id).ConfigureAwait(false), "two hops spent with MaxEscalations 2");
                standalone.EscalationCount = 0;
                settings.MaxEscalations = 0;
                AssertNull(await missions.ChooseEscalationAsync(standalone, qwen.Id).ConfigureAwait(false), "MaxEscalations 0 turns escalation off");
                settings.MaxEscalations = 2;
                foreach (string persona in new[] { Armada.Core.PersonaCatalog.Judge, Armada.Core.PersonaCatalog.Architect, Armada.Core.PersonaCatalog.ProductManager, Armada.Core.PersonaCatalog.Recorder })
                    AssertNull(await missions.ChooseEscalationAsync(new Mission("Stage", "x") { Persona = persona }, qwen.Id).ConfigureAwait(false), persona + " is not escalated");
            }));

            cases.Add(CaseAsync("apply_escalation_pins_and_records", "Escalation returns the mission to Pending pinned to the chosen captain, counts the hop and records where it came from", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                MissionService missions = CreateMissionService(testDb, new ArmadaSettings());
                await CreateLadderPersonaAsync(testDb).ConfigureAwait(false);
                Captain qwen = await CreateCaptainAsync(testDb, "qwen", "qwen3.8").ConfigureAwait(false);
                Captain gpt = await CreateCaptainAsync(testDb, "gpt", "gpt-6.1").ConfigureAwait(false);
                Mission mission = await testDb.Driver.Missions.CreateAsync(new Mission("Build", "Build it") { Persona = "Worker", Status = MissionStatusEnum.InProgress, CaptainId = qwen.Id, BranchName = "armada/b" }).ConfigureAwait(false);
                EscalationChoice choice = (await missions.ChooseEscalationAsync(mission, qwen.Id).ConfigureAwait(false))!;
                AssertTrue(await missions.ApplyEscalationAsync(mission, qwen, choice, "exhausted resumes").ConfigureAwait(false));
                Mission stored = (await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!;
                AssertEqual(MissionStatusEnum.Pending, stored.Status);
                AssertEqual(gpt.Id, stored.RequestedCaptainId);
                AssertNull(stored.CaptainId);
                AssertEqual(1, stored.EscalationCount);
                AssertEqual(qwen.Id, stored.EscalatedFromCaptainId);
                AssertEqual("qwen3.8", stored.EscalatedFromModel);
                AssertEqual("armada/b", stored.BranchName, "the work continues on the same branch");
                AssertContains("## Escalated to gpt", MissionNotes.EffectiveDescription(stored));
            }));

            cases.Add(Case("ladder_entries_validated", "A ladder rung needs at least one matching field and is named in the error", TestTags.Negative, () =>
            {
                Persona persona = new Persona("Worker", "persona.worker");
                persona.EscalationLadder = new List<PersonaCaptainPriority> { new PersonaCaptainPriority { Model = "qwen3.8" }, new PersonaCaptainPriority() };
                AssertEqual("EscalationLadder[1] requires at least one matching field", PersonaCaptainPrioritySelector.Validate(persona));
                persona.EscalationLadder = new List<PersonaCaptainPriority> { new PersonaCaptainPriority { Endpoint = "x" } };
                AssertEqual("EscalationLadder[0].Endpoint requires Runtime Mux or ApiEndpoint", PersonaCaptainPrioritySelector.Validate(persona));
                persona.EscalationLadder = null!;
                AssertEqual(0, persona.EscalationLadder.Count, "null becomes an empty ladder");
                AssertNull(PersonaCaptainPrioritySelector.Validate(persona));
            }));

            return new TestSuiteDescriptor(
                suiteId: _SuiteId,
                displayName: "Escalation Ladder",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private const string _SuiteId = "Services.EscalationLadder";

        private static async Task CreateLadderPersonaAsync(TestDatabase testDb)
        {
            Persona worker = new Persona("Worker", "persona.worker");
            worker.EscalationLadder = new List<PersonaCaptainPriority>
            {
                new PersonaCaptainPriority { Model = "qwen3.8" },
                new PersonaCaptainPriority { Model = "gpt-6.1" },
                new PersonaCaptainPriority { Model = "claude-opus-5-5" }
            };
            await testDb.Driver.Personas.CreateAsync(worker).ConfigureAwait(false);
        }

        private static async Task<Captain> CreateCaptainAsync(TestDatabase testDb, string name, string model, CaptainStateEnum state = CaptainStateEnum.Idle)
        {
            Captain captain = new Captain(name, AgentRuntimeEnum.ClaudeCode);
            captain.Model = model;
            captain.State = state;
            if (state == CaptainStateEnum.Quarantined) captain.QuarantineUntilUtc = DateTime.UtcNow.AddHours(1);
            return await CaptainAssignmentFixture.CreateAsync(testDb.Driver, captain).ConfigureAwait(false);
        }

        private static MissionService CreateMissionService(TestDatabase testDb, ArmadaSettings settings)
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            settings.DocksDirectory = Path.Combine(Path.GetTempPath(), "armada_ladder_docks_" + Guid.NewGuid().ToString("N"));
            settings.ReposDirectory = Path.Combine(Path.GetTempPath(), "armada_ladder_repos_" + Guid.NewGuid().ToString("N"));
            StubGitService git = new StubGitService();
            IDockService docks = new DockService(logging, testDb.Driver, settings, git);
            ICaptainService captains = new CaptainService(logging, testDb.Driver, settings, git, docks);
            return new MissionService(logging, testDb.Driver, settings, docks, captains, git: git);
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return CaseAsync(caseId, displayName, tag, () =>
            {
                body();
                return Task.CompletedTask;
            });
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: _SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion
    }
}

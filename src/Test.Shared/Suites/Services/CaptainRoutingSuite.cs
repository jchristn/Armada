namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Database.Sqlite;
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
    /// Descriptors for per-step captain selection: the voyage captain-override serialization contract and the
    /// persistence round-trip of the persona default captain, mission requested captain, and voyage captain
    /// overrides across create/read/update. Covers both positive round-trips and negative (null / malformed)
    /// inputs.
    /// </summary>
    public sealed class CaptainRoutingSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.CaptainRouting";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Captain Routing suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("overrides_serialize_roundtrip", "Captain overrides serialize and deserialize round-trip", TestTags.Positive, () =>
            {
                List<CaptainAssignmentOverride> overrides = new List<CaptainAssignmentOverride>
                {
                    new CaptainAssignmentOverride("Worker", "cpt_worker", CaptainTierEnum.Economy) { StrictCaptain = true },
                    new CaptainAssignmentOverride("Judge", "cpt_judge", CaptainTierEnum.Premium),
                    new CaptainAssignmentOverride("Architect", null, CaptainTierEnum.Standard)
                };

                string? json = MissionService.SerializeCaptainOverrides(overrides);
                AssertNotNull(json, "Serialized overrides should not be null");

                List<CaptainAssignmentOverride> parsed = MissionService.DeserializeCaptainOverrides(json);
                AssertEqual(3, parsed.Count, "Round-trip should preserve every override");
                AssertTrue(parsed[0].StrictCaptain, "Strict pin survives storage");
                AssertFalse(parsed[1].StrictCaptain, "Legacy fallback remains default");
                AssertEqual("Worker", parsed[0].Persona, "First persona should round-trip");
                AssertEqual("cpt_worker", parsed[0].CaptainId, "First captain id should round-trip");
                AssertEqual(CaptainTierEnum.Economy, parsed[0].FallbackTier, "First fallback tier should round-trip");
                AssertNull(parsed[2].CaptainId, "A tier-only override should round-trip with a null captain id");
                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("empty_overrides_serialize_to_null", "Empty or null overrides serialize to null (column stays null)", TestTags.Positive, () =>
            {
                AssertNull(MissionService.SerializeCaptainOverrides(null), "Null overrides should serialize to null");
                AssertNull(MissionService.SerializeCaptainOverrides(new List<CaptainAssignmentOverride>()), "Empty overrides should serialize to null");
                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("malformed_overrides_deserialize_to_empty", "Malformed override JSON deserializes to an empty list, never throws", TestTags.Negative, () =>
            {
                AssertEqual(0, MissionService.DeserializeCaptainOverrides("this is not json").Count, "Malformed JSON should yield an empty list");
                AssertEqual(0, MissionService.DeserializeCaptainOverrides(null).Count, "Null JSON should yield an empty list");
                AssertEqual(0, MissionService.DeserializeCaptainOverrides("   ").Count, "Whitespace JSON should yield an empty list");
                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("persona_default_captain_persists", "Persona default captain persists and round-trips through create and read", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    Captain captain = new Captain("routing-default");
                    captain.Tier = CaptainTierEnum.Premium;
                    captain = await testDb.Driver.Captains.CreateAsync(captain).ConfigureAwait(false);

                    Persona persona = new Persona("Worker", "persona.worker");
                    persona.DefaultCaptainId = captain.Id;
                    persona = await testDb.Driver.Personas.CreateAsync(persona).ConfigureAwait(false);

                    Persona? read = await testDb.Driver.Personas.ReadAsync(persona.Id).ConfigureAwait(false);
                    AssertNotNull(read, "Persona should be readable");
                    AssertEqual(captain.Id, read!.DefaultCaptainId, "Default captain id should persist");

                    read.DefaultCaptainId = null;
                    await testDb.Driver.Personas.UpdateAsync(read).ConfigureAwait(false);
                    Persona? cleared = await testDb.Driver.Personas.ReadAsync(persona.Id).ConfigureAwait(false);
                    AssertNull(cleared!.DefaultCaptainId, "Clearing the default captain should persist as null");
                }
            }));

            cases.Add(CaseAsync("persona_null_default_persists_as_null", "Persona with no default captain persists as null", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    Persona persona = new Persona("Judge", "persona.judge");
                    persona = await testDb.Driver.Personas.CreateAsync(persona).ConfigureAwait(false);

                    Persona? read = await testDb.Driver.Personas.ReadAsync(persona.Id).ConfigureAwait(false);
                    AssertNotNull(read, "Persona should be readable");
                    AssertNull(read!.DefaultCaptainId, "Default captain should be null when unset");
                }
            }));

            cases.Add(CaseAsync("mission_requested_captain_persists", "Mission requested captain persists and round-trips", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    Mission mission = new Mission("routing mission", "route me");
                    mission.RequestedCaptainId = "cpt_preferred";
                    mission.Tier = CaptainTierEnum.Premium;
                    mission = await testDb.Driver.Missions.CreateAsync(mission).ConfigureAwait(false);

                    Mission? read = await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                    AssertNotNull(read, "Mission should be readable");
                    AssertEqual("cpt_preferred", read!.RequestedCaptainId, "Requested captain id should persist");
                    AssertEqual(CaptainTierEnum.Premium, read.Tier, "Fallback tier should persist");
                }
            }));

            cases.Add(CaseAsync("mission_null_requested_captain_persists_as_null", "Mission with no requested captain persists as null", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    Mission mission = new Mission("unrouted mission", "no preference");
                    mission = await testDb.Driver.Missions.CreateAsync(mission).ConfigureAwait(false);

                    Mission? read = await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                    AssertNotNull(read, "Mission should be readable");
                    AssertNull(read!.RequestedCaptainId, "Requested captain should be null when unset");
                }
            }));

            cases.Add(CaseAsync("voyage_overrides_persist", "Voyage captain overrides persist and deserialize back to the same entries", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    List<CaptainAssignmentOverride> overrides = new List<CaptainAssignmentOverride>
                    {
                        new CaptainAssignmentOverride("Worker", "cpt_w", CaptainTierEnum.Economy) { StrictCaptain = true }
                    };

                    Voyage voyage = new Voyage("routing voyage", "carries overrides");
                    voyage.CaptainOverridesJson = MissionService.SerializeCaptainOverrides(overrides);
                    voyage = await testDb.Driver.Voyages.CreateAsync(voyage).ConfigureAwait(false);

                    Voyage? read = await testDb.Driver.Voyages.ReadAsync(voyage.Id).ConfigureAwait(false);
                    AssertNotNull(read, "Voyage should be readable");
                    AssertNotNull(read!.CaptainOverridesJson, "Overrides JSON should persist");

                    List<CaptainAssignmentOverride> parsed = MissionService.DeserializeCaptainOverrides(read.CaptainOverridesJson);
                    AssertEqual(1, parsed.Count, "Persisted overrides should deserialize");
                    AssertTrue(parsed[0].StrictCaptain, "Strict pin survives storage");
                    AssertEqual("Worker", parsed[0].Persona, "Persisted persona should round-trip");
                    AssertEqual("cpt_w", parsed[0].CaptainId, "Persisted captain id should round-trip");
                    AssertEqual(CaptainTierEnum.Economy, parsed[0].FallbackTier, "Persisted fallback tier should round-trip");
                }
            }));

            cases.Add(CaseAsync("preferred_idle_captain_is_assigned", "Preferred captain, when idle, is assigned even against the persona fence", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    RoutingHarness harness = await BuildHarnessAsync(testDb.Driver).ConfigureAwait(false);
                    // The preferred captain only allows "Judge", yet the mission is a "RoutingWorker" -- the
                    // explicit choice must override the AllowedPersonas fence.
                    Captain preferred = await AddCaptainAsync(testDb.Driver, "pref-idle", CaptainStateEnum.Idle, "[\"Judge\"]", null).ConfigureAwait(false);
                    Mission mission = await AddMissionAsync(testDb.Driver, harness.Vessel, "RoutingWorker", preferred.Id, null).ConfigureAwait(false);

                    bool assigned = await harness.Missions.TryAssignAsync(mission, harness.Vessel).ConfigureAwait(false);
                    AssertTrue(assigned, "Idle preferred captain should be assignable");

                    Mission? read = await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                    AssertEqual(preferred.Id, read!.CaptainId, "Idle preferred captain should be assigned despite the persona fence");
                }
            }));

            cases.Add(CaseAsync("persona_default_applies_at_assignment", "Persona default captain is preferred at assignment without being persisted as a pin", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    RoutingHarness harness = await BuildHarnessAsync(testDb.Driver).ConfigureAwait(false);
                    Captain defaultCaptain = await AddCaptainAsync(testDb.Driver, "persona-default", CaptainStateEnum.Idle, null, null).ConfigureAwait(false);
                    Persona persona = new Persona("RoutingDefaultRole", "persona.worker");
                    persona.DefaultCaptainId = defaultCaptain.Id;
                    await testDb.Driver.Personas.CreateAsync(persona).ConfigureAwait(false);

                    Mission mission = await AddMissionAsync(testDb.Driver, harness.Vessel, "RoutingDefaultRole", null, null).ConfigureAwait(false);
                    bool assigned = await harness.Missions.TryAssignAsync(mission, harness.Vessel).ConfigureAwait(false);
                    AssertTrue(assigned, "Persona default captain should be assignable");

                    Mission? read = await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                    AssertNull(read!.RequestedCaptainId, "A persona default is a preference, never persisted as a pin");
                    AssertNull(read.Tier, "A persona default never becomes a tier floor");
                    AssertEqual(defaultCaptain.Id, read.CaptainId, "Mission should be assigned to the persona default captain");
                }
            }));

            cases.Add(CaseAsync("busy_persona_default_falls_back_without_tier_floor", "A busy Premium persona default does not keep its mission waiting while a Standard captain is idle", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                RoutingHarness harness = await BuildHarnessAsync(testDb.Driver).ConfigureAwait(false);
                Captain premiumDefault = await AddCaptainAsync(testDb.Driver, "premium-default", CaptainStateEnum.Working, null, CaptainTierEnum.Premium).ConfigureAwait(false);
                Captain standard = await AddCaptainAsync(testDb.Driver, "standard-idle", CaptainStateEnum.Idle, null, CaptainTierEnum.Standard).ConfigureAwait(false);
                Persona persona = new Persona("RoutingBusyDefaultRole", "persona.worker");
                persona.DefaultCaptainId = premiumDefault.Id;
                await testDb.Driver.Personas.CreateAsync(persona).ConfigureAwait(false);

                Mission mission = await AddMissionAsync(testDb.Driver, harness.Vessel, "RoutingBusyDefaultRole", null, null).ConfigureAwait(false);
                AssertTrue(await harness.Missions.TryAssignAsync(mission, harness.Vessel).ConfigureAwait(false), "An idle Standard captain takes the work while the Premium default is busy");
                Mission? read = await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                AssertEqual(standard.Id, read!.CaptainId);
                AssertNull(read.RequestedCaptainId, "No pin is left behind");
                AssertNull(read.Tier, "No tier floor is inherited from the busy default");
            }));

            cases.Add(CaseAsync("escalated_busy_waits_for_alternative", "An escalated mission cannot fall back to the original captain while its alternative is busy", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                RoutingHarness harness = await BuildHarnessAsync(testDb.Driver).ConfigureAwait(false);
                Captain failed = await AddCaptainAsync(testDb.Driver, "failed", CaptainStateEnum.Idle, null, null).ConfigureAwait(false);
                Captain alternative = await AddCaptainAsync(testDb.Driver, "alternative", CaptainStateEnum.Working, null, null).ConfigureAwait(false);
                Mission mission = await AddMissionAsync(testDb.Driver, harness.Vessel, "Worker", alternative.Id, null).ConfigureAwait(false);
                mission.Title = "[Worker] Rework 3 (escalated): Finish";
                await testDb.Driver.Missions.UpdateAsync(mission).ConfigureAwait(false);
                AssertFalse(await harness.Missions.TryAssignAsync(mission, harness.Vessel).ConfigureAwait(false), "Wait for the alternative instead of redispatching the failed captain");
                alternative.State = CaptainStateEnum.Idle;
                await testDb.Driver.Captains.UpdateAsync(alternative).ConfigureAwait(false);
                AssertTrue(await harness.Missions.TryAssignAsync(mission, harness.Vessel).ConfigureAwait(false), "The chosen alternative becomes assignable when idle");
                AssertEqual(alternative.Id, (await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!.CaptainId);
            }));

            cases.Add(CaseAsync("strict_override_waits_for_selected_captain", "Strict voyage assignments wait for their selected captain while legacy assignments still allow fallback", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                RoutingHarness harness = await BuildHarnessAsync(testDb.Driver).ConfigureAwait(false);
                Captain failed = await AddCaptainAsync(testDb.Driver, "failed", CaptainStateEnum.Idle, null, null).ConfigureAwait(false);
                Captain alternative = await AddCaptainAsync(testDb.Driver, "alternative", CaptainStateEnum.Working, null, null).ConfigureAwait(false);
                Mission mission = await AddMissionAsync(testDb.Driver, harness.Vessel, "Worker", alternative.Id, null).ConfigureAwait(false);
                Voyage voyage = new Voyage("Strict run");
                voyage.CaptainOverridesJson = MissionService.SerializeCaptainOverrides(new List<CaptainAssignmentOverride>
                    { new CaptainAssignmentOverride("Worker", alternative.Id, null) { StrictCaptain = true } });
                voyage = await testDb.Driver.Voyages.CreateAsync(voyage).ConfigureAwait(false);
                mission.VoyageId = voyage.Id;
                await testDb.Driver.Missions.UpdateAsync(mission).ConfigureAwait(false);
                AssertFalse(await harness.Missions.TryAssignAsync(mission, harness.Vessel).ConfigureAwait(false), "Wait for the alternative instead of redispatching the failed captain");
                alternative.State = CaptainStateEnum.Idle;
                await testDb.Driver.Captains.UpdateAsync(alternative).ConfigureAwait(false);
                AssertTrue(await harness.Missions.TryAssignAsync(mission, harness.Vessel).ConfigureAwait(false), "The chosen alternative becomes assignable when idle");
                AssertEqual(alternative.Id, (await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!.CaptainId);
            }));

            cases.Add(CaseAsync("judge_review_author_exclusion_off_by_default", "ExcludeReviewAuthorsFromJudge defaults to false, including when absent from a minimal settings file", TestTags.Positive, () =>
            {
                AssertFalse(new ArmadaSettings().ExcludeReviewAuthorsFromJudge, "Default-constructed settings do not exclude review authors");
                ArmadaSettings? minimal = System.Text.Json.JsonSerializer.Deserialize<ArmadaSettings>("{}");
                AssertNotNull(minimal, "minimal settings");
                AssertFalse(minimal!.ExcludeReviewAuthorsFromJudge, "A settings file without the property does not exclude review authors");
                ArmadaSettings? enabled = System.Text.Json.JsonSerializer.Deserialize<ArmadaSettings>("{\"ExcludeReviewAuthorsFromJudge\":true}");
                AssertTrue(enabled!.ExcludeReviewAuthorsFromJudge, "The setting can be turned on from a settings file");
                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("judge_reviews_author_when_only_eligible", "With the exclusion off, a Judge whose only eligible Premium captain is the Worker's captain is assigned to it", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                RoutingHarness harness = await BuildHarnessAsync(testDb.Driver).ConfigureAwait(false);
                Captain author = await AddCaptainAsync(testDb.Driver, "premium author", CaptainStateEnum.Idle, null, CaptainTierEnum.Premium).ConfigureAwait(false);
                await AddCaptainAsync(testDb.Driver, "economy bystander", CaptainStateEnum.Idle, null, CaptainTierEnum.Economy).ConfigureAwait(false);
                Mission judge = await AddJudgeOfWorkerAsync(testDb.Driver, harness.Vessel, author.Id, null, CaptainTierEnum.Premium).ConfigureAwait(false);
                MissionAssignmentBlocker? blocker = await harness.Missions.GetAssignmentBlockerAsync(judge).ConfigureAwait(false);
                AssertNotNull(blocker, "assignment blocker");
                AssertEqual(MissionAssignmentBlockerReasonEnum.AwaitingDispatch, blocker!.Reason, "The author captain counts as eligible when the exclusion is off");
                AssertTrue(await harness.Missions.TryAssignAsync(judge, harness.Vessel).ConfigureAwait(false), "The Judge is not left unassignable because only the author is eligible");
                AssertEqual(author.Id, (await testDb.Driver.Missions.ReadAsync(judge.Id).ConfigureAwait(false))!.CaptainId);
            }));

            cases.Add(CaseAsync("judge_prefers_non_author_when_exclusion_off", "With the exclusion off, an idle non-author captain is preferred over the Worker's captain", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                RoutingHarness harness = await BuildHarnessAsync(testDb.Driver).ConfigureAwait(false);
                // The author is created first and also prefers the Judge persona, so without the preference it would win.
                Captain author = await AddCaptainAsync(testDb.Driver, "author", CaptainStateEnum.Idle, null, CaptainTierEnum.Premium).ConfigureAwait(false);
                author.PreferredPersona = "Judge";
                await testDb.Driver.Captains.UpdateAsync(author).ConfigureAwait(false);
                Captain reviewer = await AddCaptainAsync(testDb.Driver, "independent reviewer", CaptainStateEnum.Idle, null, CaptainTierEnum.Premium).ConfigureAwait(false);
                Mission judge = await AddJudgeOfWorkerAsync(testDb.Driver, harness.Vessel, author.Id, null, CaptainTierEnum.Premium).ConfigureAwait(false);
                AssertTrue(await harness.Missions.TryAssignAsync(judge, harness.Vessel).ConfigureAwait(false), "The Judge is assigned");
                AssertEqual(reviewer.Id, (await testDb.Driver.Missions.ReadAsync(judge.Id).ConfigureAwait(false))!.CaptainId, "The non-author captain reviews the work");
            }));

            cases.Add(CaseAsync("judge_falls_back_to_author_when_non_author_ineligible", "With the exclusion off, an idle non-author below the Judge's tier does not block the author fallback", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                RoutingHarness harness = await BuildHarnessAsync(testDb.Driver).ConfigureAwait(false);
                Captain author = await AddCaptainAsync(testDb.Driver, "premium author", CaptainStateEnum.Idle, null, CaptainTierEnum.Premium).ConfigureAwait(false);
                Captain bystander = await AddCaptainAsync(testDb.Driver, "standard bystander", CaptainStateEnum.Idle, null, CaptainTierEnum.Standard).ConfigureAwait(false);
                Mission judge = await AddJudgeOfWorkerAsync(testDb.Driver, harness.Vessel, author.Id, null, CaptainTierEnum.Premium).ConfigureAwait(false);
                AssertTrue(await harness.Missions.TryAssignAsync(judge, harness.Vessel).ConfigureAwait(false), "The Judge is assigned");
                AssertEqual(author.Id, (await testDb.Driver.Missions.ReadAsync(judge.Id).ConfigureAwait(false))!.CaptainId, "The tier floor still applies to the non-author");
                AssertEqual(CaptainStateEnum.Idle, (await testDb.Driver.Captains.ReadAsync(bystander.Id).ConfigureAwait(false))!.State);
            }));

            cases.Add(CaseAsync("judge_requested_author_honored_when_exclusion_off", "With the exclusion off, a requested author captain reviews the work even when a non-author is idle", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                RoutingHarness harness = await BuildHarnessAsync(testDb.Driver).ConfigureAwait(false);
                Captain author = await AddCaptainAsync(testDb.Driver, "author", CaptainStateEnum.Idle, null, null).ConfigureAwait(false);
                await AddCaptainAsync(testDb.Driver, "independent reviewer", CaptainStateEnum.Idle, null, null).ConfigureAwait(false);
                Mission judge = await AddJudgeOfWorkerAsync(testDb.Driver, harness.Vessel, author.Id, author.Id, null).ConfigureAwait(false);
                AssertTrue(await harness.Missions.TryAssignAsync(judge, harness.Vessel).ConfigureAwait(false), "The requested captain is assigned");
                AssertEqual(author.Id, (await testDb.Driver.Missions.ReadAsync(judge.Id).ConfigureAwait(false))!.CaptainId, "The requested author captain is honored");
            }));

            cases.Add(CaseAsync("judge_blocker_names_excluded_authors_only_when_on", "With the exclusion on, the assignment blocker names the excluded authors; a non-Judge blocker never does", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                RoutingHarness harness = await BuildHarnessAsync(testDb.Driver, true).ConfigureAwait(false);
                Captain author = await AddCaptainAsync(testDb.Driver, "premium author", CaptainStateEnum.Idle, null, CaptainTierEnum.Premium).ConfigureAwait(false);
                Mission judge = await AddJudgeOfWorkerAsync(testDb.Driver, harness.Vessel, author.Id, null, CaptainTierEnum.Premium).ConfigureAwait(false);
                AssertFalse(await harness.Missions.TryAssignAsync(judge, harness.Vessel).ConfigureAwait(false), "The only captain authored the work");
                MissionAssignmentBlocker? blocker = await harness.Missions.GetAssignmentBlockerAsync(judge).ConfigureAwait(false);
                AssertNotNull(blocker, "assignment blocker");
                AssertEqual(MissionAssignmentBlockerReasonEnum.NoEligibleCaptain, blocker!.Reason);
                AssertContains("ExcludeReviewAuthorsFromJudge is on", blocker.Summary ?? "", "The blocker explains the author exclusion");

                // This assertion isolates tier routing from the preceding standalone review group's admission.
                harness.Vessel.MaxParallelVoyages = 0;
                await testDb.Driver.Vessels.UpdateAsync(harness.Vessel);
                Mission premiumWorker = await AddMissionAsync(testDb.Driver, harness.Vessel, "Worker", null, CaptainTierEnum.Premium).ConfigureAwait(false);
                author.Tier = CaptainTierEnum.Standard;
                await testDb.Driver.Captains.UpdateAsync(author).ConfigureAwait(false);
                MissionAssignmentBlocker? tierBlocker = await harness.Missions.GetAssignmentBlockerAsync(premiumWorker).ConfigureAwait(false);
                AssertNotNull(tierBlocker, "tier blocker");
                AssertEqual(MissionAssignmentBlockerReasonEnum.NoEligibleCaptain, tierBlocker!.Reason);
                AssertFalse((tierBlocker.Summary ?? "").Contains("ExcludeReviewAuthorsFromJudge", StringComparison.Ordinal), "Authors are not blamed for a non-Judge blocker");
            }));

            cases.Add(CaseAsync("judge_blocker_omits_authors_when_off", "With the exclusion off, a blocked Judge's explanation does not mention review authors", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                RoutingHarness harness = await BuildHarnessAsync(testDb.Driver).ConfigureAwait(false);
                Captain author = await AddCaptainAsync(testDb.Driver, "standard author", CaptainStateEnum.Idle, null, CaptainTierEnum.Standard).ConfigureAwait(false);
                Mission judge = await AddJudgeOfWorkerAsync(testDb.Driver, harness.Vessel, author.Id, null, CaptainTierEnum.Premium).ConfigureAwait(false);
                AssertFalse(await harness.Missions.TryAssignAsync(judge, harness.Vessel).ConfigureAwait(false), "The author is below the Judge's tier");
                MissionAssignmentBlocker? blocker = await harness.Missions.GetAssignmentBlockerAsync(judge).ConfigureAwait(false);
                AssertNotNull(blocker, "assignment blocker");
                AssertEqual(MissionAssignmentBlockerReasonEnum.NoEligibleCaptain, blocker!.Reason);
                AssertFalse((blocker.Summary ?? "").Contains("ExcludeReviewAuthorsFromJudge", StringComparison.Ordinal), "Authors are not named when they are not excluded");
            }));

            cases.Add(CaseAsync("judge_cannot_review_own_escalated_work", "With ExcludeReviewAuthorsFromJudge on, Judge routing excludes the preceding implementation captain even when explicitly requested", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                RoutingHarness harness = await BuildHarnessAsync(testDb.Driver, true).ConfigureAwait(false);
                Captain author = await AddCaptainAsync(testDb.Driver, "author", CaptainStateEnum.Idle, null, null).ConfigureAwait(false);
                Mission worker = await AddMissionAsync(testDb.Driver, harness.Vessel, "Worker", null, null).ConfigureAwait(false);
                worker.Status = MissionStatusEnum.WorkProduced;
                worker.CaptainId = author.Id;
                await testDb.Driver.Missions.UpdateAsync(worker).ConfigureAwait(false);
                Mission judge = await AddMissionAsync(testDb.Driver, harness.Vessel, "Judge", author.Id, null).ConfigureAwait(false);
                judge.DependsOnMissionId = worker.Id;
                await testDb.Driver.Missions.UpdateAsync(judge).ConfigureAwait(false);
                AssertFalse(await harness.Missions.TryAssignAsync(judge, harness.Vessel).ConfigureAwait(false), "A captain cannot approve its own implementation");
                Captain reviewer = await AddCaptainAsync(testDb.Driver, "independent reviewer", CaptainStateEnum.Idle, null, null).ConfigureAwait(false);
                AssertTrue(await harness.Missions.TryAssignAsync(judge, harness.Vessel).ConfigureAwait(false), "An independent captain can review the work");
                AssertEqual(reviewer.Id, (await testDb.Driver.Missions.ReadAsync(judge.Id).ConfigureAwait(false))!.CaptainId);
            }));

            cases.Add(CaseAsync("judge_excludes_full_pipeline_authors", "With ExcludeReviewAuthorsFromJudge on, Judge routing excludes the Worker behind verification stages and all intervening contributors", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                RoutingHarness harness = await BuildHarnessAsync(testDb.Driver, true).ConfigureAwait(false);
                Captain author = await AddCaptainAsync(testDb.Driver, "author", CaptainStateEnum.Idle, null, null).ConfigureAwait(false);
                Mission worker = await AddMissionAsync(testDb.Driver, harness.Vessel, "Worker", null, null).ConfigureAwait(false);
                worker.Status = MissionStatusEnum.WorkProduced;
                worker.CaptainId = author.Id;
                await testDb.Driver.Missions.UpdateAsync(worker).ConfigureAwait(false);
                Mission judge = await AddMissionAsync(testDb.Driver, harness.Vessel, "Judge", author.Id, null).ConfigureAwait(false);
                Captain contributor = await AddCaptainAsync(testDb.Driver, "contributor", CaptainStateEnum.Idle, null, null).ConfigureAwait(false);
                Mission preceding = worker;
                foreach (string persona in new[] { "Usability Engineer", "Test Engineer", "Linter" })
                {
                    Mission stage = await AddMissionAsync(testDb.Driver, harness.Vessel, persona, null, null).ConfigureAwait(false);
                    stage.Status = MissionStatusEnum.WorkProduced;
                    stage.CaptainId = contributor.Id;
                    stage.DependsOnMissionId = preceding.Id;
                    await testDb.Driver.Missions.UpdateAsync(stage).ConfigureAwait(false);
                    preceding = stage;
                }
                judge.DependsOnMissionId = preceding.Id;
                await testDb.Driver.Missions.UpdateAsync(judge).ConfigureAwait(false);
                AssertFalse(await harness.Missions.TryAssignAsync(judge, harness.Vessel).ConfigureAwait(false), "A captain cannot approve its own implementation");
                Captain reviewer = await AddCaptainAsync(testDb.Driver, "independent reviewer", CaptainStateEnum.Idle, null, null).ConfigureAwait(false);
                AssertTrue(await harness.Missions.TryAssignAsync(judge, harness.Vessel).ConfigureAwait(false), "An independent captain can review the work");
                AssertEqual(reviewer.Id, (await testDb.Driver.Missions.ReadAsync(judge.Id).ConfigureAwait(false))!.CaptainId);
            }));

            cases.Add(CaseAsync("preferred_busy_falls_back_by_tier", "A busy preferred captain falls back to an idle captain at or above the fallback tier", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    RoutingHarness harness = await BuildHarnessAsync(testDb.Driver).ConfigureAwait(false);
                    Captain busyPreferred = await AddCaptainAsync(testDb.Driver, "busy-pref", CaptainStateEnum.Working, null, CaptainTierEnum.Premium).ConfigureAwait(false);
                    Captain fallback = await AddCaptainAsync(testDb.Driver, "tier-fallback", CaptainStateEnum.Idle, null, CaptainTierEnum.Standard).ConfigureAwait(false);

                    Mission mission = await AddMissionAsync(testDb.Driver, harness.Vessel, "RoutingWorker", busyPreferred.Id, CaptainTierEnum.Standard).ConfigureAwait(false);
                    bool assigned = await harness.Missions.TryAssignAsync(mission, harness.Vessel).ConfigureAwait(false);
                    AssertTrue(assigned, "A tier-eligible idle captain should take the fallback");

                    Mission? read = await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                    AssertEqual(fallback.Id, read!.CaptainId, "Busy preferred captain should fall back to the tier-eligible idle captain");
                }
            }));

            cases.Add(CaseAsync("deleted_preferred_captain_falls_back_to_normal_routing", "A preferred captain that no longer exists falls back to normal persona routing", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    RoutingHarness harness = await BuildHarnessAsync(testDb.Driver).ConfigureAwait(false);
                    Captain normal = await AddCaptainAsync(testDb.Driver, "normal-worker", CaptainStateEnum.Idle, "[\"RoutingWorker\"]", null).ConfigureAwait(false);

                    Mission mission = await AddMissionAsync(testDb.Driver, harness.Vessel, "RoutingWorker", "cpt_does_not_exist", null).ConfigureAwait(false);
                    bool assigned = await harness.Missions.TryAssignAsync(mission, harness.Vessel).ConfigureAwait(false);
                    AssertTrue(assigned, "A deleted preferred captain should not block normal routing");

                    Mission? read = await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                    AssertEqual(normal.Id, read!.CaptainId, "Deleted preferred captain should fall back to a persona-eligible idle captain");
                }
            }));

            cases.Add(CaseAsync("no_tier_eligible_captain_stays_pending", "A busy preferred captain with no tier-eligible fallback leaves the mission Pending", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    RoutingHarness harness = await BuildHarnessAsync(testDb.Driver).ConfigureAwait(false);
                    Captain busyPremium = await AddCaptainAsync(testDb.Driver, "busy-premium", CaptainStateEnum.Working, null, CaptainTierEnum.Premium).ConfigureAwait(false);
                    // The only idle captain is Economy, below the Premium fallback tier.
                    await AddCaptainAsync(testDb.Driver, "idle-economy", CaptainStateEnum.Idle, null, CaptainTierEnum.Economy).ConfigureAwait(false);

                    Mission mission = await AddMissionAsync(testDb.Driver, harness.Vessel, "RoutingWorker", busyPremium.Id, CaptainTierEnum.Premium).ConfigureAwait(false);
                    bool assigned = await harness.Missions.TryAssignAsync(mission, harness.Vessel).ConfigureAwait(false);
                    AssertFalse(assigned, "No Premium-or-above idle captain should mean no assignment");

                    Mission? read = await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                    AssertNull(read!.CaptainId, "Mission should remain unassigned");
                    AssertEqual(MissionStatusEnum.Pending, read.Status, "Mission should remain Pending for the next tick");
                }
            }));

            cases.Add(CaseAsync("no_preference_routes_normally", "A mission with no preferred captain and no persona default routes normally (regression guard)", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    RoutingHarness harness = await BuildHarnessAsync(testDb.Driver).ConfigureAwait(false);
                    Captain worker = await AddCaptainAsync(testDb.Driver, "plain-worker", CaptainStateEnum.Idle, "[\"RoutingPlainRole\"]", null).ConfigureAwait(false);

                    Mission mission = await AddMissionAsync(testDb.Driver, harness.Vessel, "RoutingPlainRole", null, null).ConfigureAwait(false);
                    bool assigned = await harness.Missions.TryAssignAsync(mission, harness.Vessel).ConfigureAwait(false);
                    AssertTrue(assigned, "Normal persona routing should still assign");

                    Mission? read = await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                    AssertEqual(worker.Id, read!.CaptainId, "Mission should route to the persona-eligible idle captain");
                    AssertNull(read.RequestedCaptainId, "No preferred captain should be recorded when there is no default");
                }
            }));

            cases.Add(CaseAsync("priority_first_idle", "priority first idle", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    RoutingHarness harness = await BuildHarnessAsync(testDb.Driver).ConfigureAwait(false);
                    Captain first = await AddCaptainAsync(testDb.Driver, "first", CaptainStateEnum.Idle, null, CaptainTierEnum.Premium).ConfigureAwait(false);
                    first.Runtime = AgentRuntimeEnum.Codex;
                    Captain second = await AddCaptainAsync(testDb.Driver, "second", CaptainStateEnum.Idle, null, CaptainTierEnum.Premium).ConfigureAwait(false);
                    Persona persona = new Persona("PriorityRole", "persona.worker");
                    persona.CaptainPriorities.Add(new PersonaCaptainPriority { Runtime = AgentRuntimeEnum.Codex });
                    persona.CaptainPriorities.Add(new PersonaCaptainPriority { Runtime = AgentRuntimeEnum.ClaudeCode });
                    Mission mission = await AddMissionAsync(testDb.Driver, harness.Vessel, persona.Name, null, null).ConfigureAwait(false);

                    await testDb.Driver.Captains.UpdateAsync(first).ConfigureAwait(false);
                    await testDb.Driver.Captains.UpdateAsync(second).ConfigureAwait(false);
                    await testDb.Driver.Personas.CreateAsync(persona).ConfigureAwait(false);
                    await testDb.Driver.Missions.UpdateAsync(mission).ConfigureAwait(false);
                    bool assigned = await harness.Missions.TryAssignAsync(mission, harness.Vessel).ConfigureAwait(false);
                    AssertEqual(true, assigned);
                    Mission read = (await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!;
                    AssertEqual(first.Id, read.CaptainId);
                    if (String.IsNullOrEmpty(mission.RequestedCaptainId)) AssertNull(read.RequestedCaptainId, "Hierarchy must not pin a captain");
                    if (!assigned)
                    {
                        MissionAssignmentBlocker? blocker = await harness.Missions.GetAssignmentBlockerAsync(read).ConfigureAwait(false);
                        AssertNotNull(blocker, "assignment blocker");
                        AssertEqual(MissionAssignmentBlockerReasonEnum.NoEligibleCaptain, blocker.Reason);
                    }
                }
            }));
            cases.Add(CaseAsync("priority_specific_api_endpoint", "Specific API endpoint wins over another API captain", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                RoutingHarness harness = await BuildHarnessAsync(testDb.Driver).ConfigureAwait(false);
                Captain other = await AddCaptainAsync(testDb.Driver, "other-api", CaptainStateEnum.Idle, null, CaptainTierEnum.Premium).ConfigureAwait(false);
                Captain selected = await AddCaptainAsync(testDb.Driver, "selected-api", CaptainStateEnum.Idle, null, CaptainTierEnum.Premium).ConfigureAwait(false);
                other.Runtime = AgentRuntimeEnum.ApiEndpoint; other.ModelEndpointId = "mep_other";
                selected.Runtime = AgentRuntimeEnum.ApiEndpoint; selected.ModelEndpointId = "mep_selected";
                await testDb.Driver.Captains.UpdateAsync(other).ConfigureAwait(false);
                await testDb.Driver.Captains.UpdateAsync(selected).ConfigureAwait(false);
                Persona persona = new Persona("SpecificApiRole", "persona.worker");
                persona.CaptainPriorities.Add(new PersonaCaptainPriority { Runtime = AgentRuntimeEnum.ApiEndpoint, Endpoint = "mep_selected" });
                await testDb.Driver.Personas.CreateAsync(persona).ConfigureAwait(false);
                Mission mission = await AddMissionAsync(testDb.Driver, harness.Vessel, persona.Name, null, null).ConfigureAwait(false);
                AssertTrue(await harness.Missions.TryAssignAsync(mission, harness.Vessel).ConfigureAwait(false));
                AssertEqual(selected.Id, (await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!.CaptainId);
            }));
            cases.Add(CaseAsync("priority_busy_falls_through", "priority busy falls through", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    RoutingHarness harness = await BuildHarnessAsync(testDb.Driver).ConfigureAwait(false);
                    Captain first = await AddCaptainAsync(testDb.Driver, "first", CaptainStateEnum.Idle, null, CaptainTierEnum.Premium).ConfigureAwait(false);
                    first.Runtime = AgentRuntimeEnum.Codex;
                    Captain second = await AddCaptainAsync(testDb.Driver, "second", CaptainStateEnum.Idle, null, CaptainTierEnum.Premium).ConfigureAwait(false);
                    Persona persona = new Persona("PriorityRole", "persona.worker");
                    persona.CaptainPriorities.Add(new PersonaCaptainPriority { Runtime = AgentRuntimeEnum.Codex });
                    persona.CaptainPriorities.Add(new PersonaCaptainPriority { Runtime = AgentRuntimeEnum.ClaudeCode });
                    Mission mission = await AddMissionAsync(testDb.Driver, harness.Vessel, persona.Name, null, null).ConfigureAwait(false);
                    first.State = CaptainStateEnum.Working;
                    await testDb.Driver.Captains.UpdateAsync(first).ConfigureAwait(false);
                    await testDb.Driver.Captains.UpdateAsync(second).ConfigureAwait(false);
                    await testDb.Driver.Personas.CreateAsync(persona).ConfigureAwait(false);
                    await testDb.Driver.Missions.UpdateAsync(mission).ConfigureAwait(false);
                    bool assigned = await harness.Missions.TryAssignAsync(mission, harness.Vessel).ConfigureAwait(false);
                    AssertEqual(true, assigned);
                    Mission read = (await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!;
                    AssertEqual(second.Id, read.CaptainId);
                    if (String.IsNullOrEmpty(mission.RequestedCaptainId)) AssertNull(read.RequestedCaptainId, "Hierarchy must not pin a captain");
                    if (!assigned)
                    {
                        MissionAssignmentBlocker? blocker = await harness.Missions.GetAssignmentBlockerAsync(read).ConfigureAwait(false);
                        AssertNotNull(blocker, "assignment blocker");
                        AssertEqual(MissionAssignmentBlockerReasonEnum.NoEligibleCaptain, blocker.Reason);
                    }
                }
            }));
            cases.Add(CaseAsync("priority_busy_waits", "priority busy waits", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    RoutingHarness harness = await BuildHarnessAsync(testDb.Driver).ConfigureAwait(false);
                    Captain first = await AddCaptainAsync(testDb.Driver, "first", CaptainStateEnum.Idle, null, CaptainTierEnum.Premium).ConfigureAwait(false);
                    first.Runtime = AgentRuntimeEnum.Codex;
                    Captain second = await AddCaptainAsync(testDb.Driver, "second", CaptainStateEnum.Idle, null, CaptainTierEnum.Premium).ConfigureAwait(false);
                    Persona persona = new Persona("PriorityRole", "persona.worker");
                    persona.CaptainPriorities.Add(new PersonaCaptainPriority { Runtime = AgentRuntimeEnum.Codex });
                    persona.CaptainPriorities.Add(new PersonaCaptainPriority { Runtime = AgentRuntimeEnum.ClaudeCode });
                    Mission mission = await AddMissionAsync(testDb.Driver, harness.Vessel, persona.Name, null, null).ConfigureAwait(false);
                    first.State = CaptainStateEnum.Working; persona.PriorityAvailability = PersonaPriorityAvailabilityEnum.WaitForFirstPriority;
                    await testDb.Driver.Captains.UpdateAsync(first).ConfigureAwait(false);
                    await testDb.Driver.Captains.UpdateAsync(second).ConfigureAwait(false);
                    await testDb.Driver.Personas.CreateAsync(persona).ConfigureAwait(false);
                    await testDb.Driver.Missions.UpdateAsync(mission).ConfigureAwait(false);
                    bool assigned = await harness.Missions.TryAssignAsync(mission, harness.Vessel).ConfigureAwait(false);
                    AssertEqual(false, assigned);
                    Mission read = (await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!;
                    AssertEqual(null, read.CaptainId);
                    if (String.IsNullOrEmpty(mission.RequestedCaptainId)) AssertNull(read.RequestedCaptainId, "Hierarchy must not pin a captain");
                    if (!assigned)
                    {
                        MissionAssignmentBlocker? blocker = await harness.Missions.GetAssignmentBlockerAsync(read).ConfigureAwait(false);
                        AssertNotNull(blocker, "assignment blocker");
                        AssertEqual(MissionAssignmentBlockerReasonEnum.NoEligibleCaptain, blocker.Reason);
                    }
                }
            }));
            cases.Add(CaseAsync("priority_missing_waits", "priority missing waits", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    RoutingHarness harness = await BuildHarnessAsync(testDb.Driver).ConfigureAwait(false);
                    Captain first = await AddCaptainAsync(testDb.Driver, "first", CaptainStateEnum.Idle, null, CaptainTierEnum.Premium).ConfigureAwait(false);
                    first.Runtime = AgentRuntimeEnum.Codex;
                    Captain second = await AddCaptainAsync(testDb.Driver, "second", CaptainStateEnum.Idle, null, CaptainTierEnum.Premium).ConfigureAwait(false);
                    Persona persona = new Persona("PriorityRole", "persona.worker");
                    persona.CaptainPriorities.Add(new PersonaCaptainPriority { Runtime = AgentRuntimeEnum.Codex });
                    persona.CaptainPriorities.Add(new PersonaCaptainPriority { Runtime = AgentRuntimeEnum.ClaudeCode });
                    Mission mission = await AddMissionAsync(testDb.Driver, harness.Vessel, persona.Name, null, null).ConfigureAwait(false);
                    first.Runtime = AgentRuntimeEnum.Gemini; persona.PriorityAvailability = PersonaPriorityAvailabilityEnum.WaitForFirstPriority;
                    await testDb.Driver.Captains.UpdateAsync(first).ConfigureAwait(false);
                    await testDb.Driver.Captains.UpdateAsync(second).ConfigureAwait(false);
                    await testDb.Driver.Personas.CreateAsync(persona).ConfigureAwait(false);
                    await testDb.Driver.Missions.UpdateAsync(mission).ConfigureAwait(false);
                    bool assigned = await harness.Missions.TryAssignAsync(mission, harness.Vessel).ConfigureAwait(false);
                    AssertEqual(false, assigned);
                    Mission read = (await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!;
                    AssertEqual(null, read.CaptainId);
                    if (String.IsNullOrEmpty(mission.RequestedCaptainId)) AssertNull(read.RequestedCaptainId, "Hierarchy must not pin a captain");
                    if (!assigned)
                    {
                        MissionAssignmentBlocker? blocker = await harness.Missions.GetAssignmentBlockerAsync(read).ConfigureAwait(false);
                        AssertNotNull(blocker, "assignment blocker");
                        AssertEqual(MissionAssignmentBlockerReasonEnum.NoEligibleCaptain, blocker.Reason);
                    }
                }
            }));
            cases.Add(CaseAsync("priority_exhaustion_waits", "priority exhaustion waits", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    RoutingHarness harness = await BuildHarnessAsync(testDb.Driver).ConfigureAwait(false);
                    Captain first = await AddCaptainAsync(testDb.Driver, "first", CaptainStateEnum.Idle, null, CaptainTierEnum.Premium).ConfigureAwait(false);
                    first.Runtime = AgentRuntimeEnum.Codex;
                    Captain second = await AddCaptainAsync(testDb.Driver, "second", CaptainStateEnum.Idle, null, CaptainTierEnum.Premium).ConfigureAwait(false);
                    Persona persona = new Persona("PriorityRole", "persona.worker");
                    persona.CaptainPriorities.Add(new PersonaCaptainPriority { Runtime = AgentRuntimeEnum.Codex });
                    persona.CaptainPriorities.Add(new PersonaCaptainPriority { Runtime = AgentRuntimeEnum.ClaudeCode });
                    Mission mission = await AddMissionAsync(testDb.Driver, harness.Vessel, persona.Name, null, null).ConfigureAwait(false);
                    first.Runtime = AgentRuntimeEnum.Gemini; second.Runtime = AgentRuntimeEnum.Gemini;
                    await testDb.Driver.Captains.UpdateAsync(first).ConfigureAwait(false);
                    await testDb.Driver.Captains.UpdateAsync(second).ConfigureAwait(false);
                    await testDb.Driver.Personas.CreateAsync(persona).ConfigureAwait(false);
                    await testDb.Driver.Missions.UpdateAsync(mission).ConfigureAwait(false);
                    bool assigned = await harness.Missions.TryAssignAsync(mission, harness.Vessel).ConfigureAwait(false);
                    AssertEqual(false, assigned);
                    Mission read = (await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!;
                    AssertEqual(null, read.CaptainId);
                    if (String.IsNullOrEmpty(mission.RequestedCaptainId)) AssertNull(read.RequestedCaptainId, "Hierarchy must not pin a captain");
                    if (!assigned)
                    {
                        MissionAssignmentBlocker? blocker = await harness.Missions.GetAssignmentBlockerAsync(read).ConfigureAwait(false);
                        AssertNotNull(blocker, "assignment blocker");
                        AssertEqual(MissionAssignmentBlockerReasonEnum.NoEligibleCaptain, blocker.Reason);
                    }
                }
            }));
            cases.Add(CaseAsync("priority_exhaustion_general_fallback", "priority exhaustion general fallback", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    RoutingHarness harness = await BuildHarnessAsync(testDb.Driver).ConfigureAwait(false);
                    Captain first = await AddCaptainAsync(testDb.Driver, "first", CaptainStateEnum.Idle, null, CaptainTierEnum.Premium).ConfigureAwait(false);
                    first.Runtime = AgentRuntimeEnum.Codex;
                    Captain second = await AddCaptainAsync(testDb.Driver, "second", CaptainStateEnum.Idle, null, CaptainTierEnum.Premium).ConfigureAwait(false);
                    Persona persona = new Persona("PriorityRole", "persona.worker");
                    persona.CaptainPriorities.Add(new PersonaCaptainPriority { Runtime = AgentRuntimeEnum.Codex });
                    persona.CaptainPriorities.Add(new PersonaCaptainPriority { Runtime = AgentRuntimeEnum.ClaudeCode });
                    Mission mission = await AddMissionAsync(testDb.Driver, harness.Vessel, persona.Name, null, null).ConfigureAwait(false);
                    first.Runtime = AgentRuntimeEnum.Gemini; second.Runtime = AgentRuntimeEnum.Gemini; persona.PriorityExhaustion = PersonaPriorityExhaustionEnum.UseEligibleCaptain;
                    await testDb.Driver.Captains.UpdateAsync(first).ConfigureAwait(false);
                    await testDb.Driver.Captains.UpdateAsync(second).ConfigureAwait(false);
                    await testDb.Driver.Personas.CreateAsync(persona).ConfigureAwait(false);
                    await testDb.Driver.Missions.UpdateAsync(mission).ConfigureAwait(false);
                    bool assigned = await harness.Missions.TryAssignAsync(mission, harness.Vessel).ConfigureAwait(false);
                    AssertEqual(true, assigned);
                    Mission read = (await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!;
                    AssertEqual(first.Id, read.CaptainId);
                    if (String.IsNullOrEmpty(mission.RequestedCaptainId)) AssertNull(read.RequestedCaptainId, "Hierarchy must not pin a captain");
                    if (!assigned)
                    {
                        MissionAssignmentBlocker? blocker = await harness.Missions.GetAssignmentBlockerAsync(read).ConfigureAwait(false);
                        AssertNotNull(blocker, "assignment blocker");
                        AssertEqual(MissionAssignmentBlockerReasonEnum.NoEligibleCaptain, blocker.Reason);
                    }
                }
            }));
            cases.Add(CaseAsync("priority_tier_floor", "priority tier floor", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    RoutingHarness harness = await BuildHarnessAsync(testDb.Driver).ConfigureAwait(false);
                    Captain first = await AddCaptainAsync(testDb.Driver, "first", CaptainStateEnum.Idle, null, CaptainTierEnum.Premium).ConfigureAwait(false);
                    first.Runtime = AgentRuntimeEnum.Codex;
                    Captain second = await AddCaptainAsync(testDb.Driver, "second", CaptainStateEnum.Idle, null, CaptainTierEnum.Premium).ConfigureAwait(false);
                    Persona persona = new Persona("PriorityRole", "persona.worker");
                    persona.CaptainPriorities.Add(new PersonaCaptainPriority { Runtime = AgentRuntimeEnum.Codex });
                    persona.CaptainPriorities.Add(new PersonaCaptainPriority { Runtime = AgentRuntimeEnum.ClaudeCode });
                    Mission mission = await AddMissionAsync(testDb.Driver, harness.Vessel, persona.Name, null, null).ConfigureAwait(false);
                    first.Tier = CaptainTierEnum.Economy; mission.Tier = CaptainTierEnum.Premium;
                    await testDb.Driver.Captains.UpdateAsync(first).ConfigureAwait(false);
                    await testDb.Driver.Captains.UpdateAsync(second).ConfigureAwait(false);
                    await testDb.Driver.Personas.CreateAsync(persona).ConfigureAwait(false);
                    await testDb.Driver.Missions.UpdateAsync(mission).ConfigureAwait(false);
                    bool assigned = await harness.Missions.TryAssignAsync(mission, harness.Vessel).ConfigureAwait(false);
                    AssertEqual(true, assigned);
                    Mission read = (await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!;
                    AssertEqual(second.Id, read.CaptainId);
                    if (String.IsNullOrEmpty(mission.RequestedCaptainId)) AssertNull(read.RequestedCaptainId, "Hierarchy must not pin a captain");
                    if (!assigned)
                    {
                        MissionAssignmentBlocker? blocker = await harness.Missions.GetAssignmentBlockerAsync(read).ConfigureAwait(false);
                        AssertNotNull(blocker, "assignment blocker");
                        AssertEqual(MissionAssignmentBlockerReasonEnum.NoEligibleCaptain, blocker.Reason);
                    }
                }
            }));
            cases.Add(CaseAsync("priority_persona_fence", "priority persona fence", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    RoutingHarness harness = await BuildHarnessAsync(testDb.Driver).ConfigureAwait(false);
                    Captain first = await AddCaptainAsync(testDb.Driver, "first", CaptainStateEnum.Idle, null, CaptainTierEnum.Premium).ConfigureAwait(false);
                    first.Runtime = AgentRuntimeEnum.Codex;
                    Captain second = await AddCaptainAsync(testDb.Driver, "second", CaptainStateEnum.Idle, null, CaptainTierEnum.Premium).ConfigureAwait(false);
                    Persona persona = new Persona("PriorityRole", "persona.worker");
                    persona.CaptainPriorities.Add(new PersonaCaptainPriority { Runtime = AgentRuntimeEnum.Codex });
                    persona.CaptainPriorities.Add(new PersonaCaptainPriority { Runtime = AgentRuntimeEnum.ClaudeCode });
                    Mission mission = await AddMissionAsync(testDb.Driver, harness.Vessel, persona.Name, null, null).ConfigureAwait(false);
                    first.AllowedPersonas = "[\"OtherRole\"]";
                    await testDb.Driver.Captains.UpdateAsync(first).ConfigureAwait(false);
                    await testDb.Driver.Captains.UpdateAsync(second).ConfigureAwait(false);
                    await testDb.Driver.Personas.CreateAsync(persona).ConfigureAwait(false);
                    await testDb.Driver.Missions.UpdateAsync(mission).ConfigureAwait(false);
                    bool assigned = await harness.Missions.TryAssignAsync(mission, harness.Vessel).ConfigureAwait(false);
                    AssertEqual(true, assigned);
                    Mission read = (await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!;
                    AssertEqual(second.Id, read.CaptainId);
                    if (String.IsNullOrEmpty(mission.RequestedCaptainId)) AssertNull(read.RequestedCaptainId, "Hierarchy must not pin a captain");
                    if (!assigned)
                    {
                        MissionAssignmentBlocker? blocker = await harness.Missions.GetAssignmentBlockerAsync(read).ConfigureAwait(false);
                        AssertNotNull(blocker, "assignment blocker");
                        AssertEqual(MissionAssignmentBlockerReasonEnum.NoEligibleCaptain, blocker.Reason);
                    }
                }
            }));
            cases.Add(CaseAsync("priority_explicit_override", "priority explicit override", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    RoutingHarness harness = await BuildHarnessAsync(testDb.Driver).ConfigureAwait(false);
                    Captain first = await AddCaptainAsync(testDb.Driver, "first", CaptainStateEnum.Idle, null, CaptainTierEnum.Premium).ConfigureAwait(false);
                    first.Runtime = AgentRuntimeEnum.Codex;
                    Captain second = await AddCaptainAsync(testDb.Driver, "second", CaptainStateEnum.Idle, null, CaptainTierEnum.Premium).ConfigureAwait(false);
                    Persona persona = new Persona("PriorityRole", "persona.worker");
                    persona.CaptainPriorities.Add(new PersonaCaptainPriority { Runtime = AgentRuntimeEnum.Codex });
                    persona.CaptainPriorities.Add(new PersonaCaptainPriority { Runtime = AgentRuntimeEnum.ClaudeCode });
                    Mission mission = await AddMissionAsync(testDb.Driver, harness.Vessel, persona.Name, null, null).ConfigureAwait(false);
                    mission.RequestedCaptainId = second.Id;
                    await testDb.Driver.Captains.UpdateAsync(first).ConfigureAwait(false);
                    await testDb.Driver.Captains.UpdateAsync(second).ConfigureAwait(false);
                    await testDb.Driver.Personas.CreateAsync(persona).ConfigureAwait(false);
                    await testDb.Driver.Missions.UpdateAsync(mission).ConfigureAwait(false);
                    bool assigned = await harness.Missions.TryAssignAsync(mission, harness.Vessel).ConfigureAwait(false);
                    AssertEqual(true, assigned);
                    Mission read = (await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!;
                    AssertEqual(second.Id, read.CaptainId);
                    if (String.IsNullOrEmpty(mission.RequestedCaptainId)) AssertNull(read.RequestedCaptainId, "Hierarchy must not pin a captain");
                    if (!assigned)
                    {
                        MissionAssignmentBlocker? blocker = await harness.Missions.GetAssignmentBlockerAsync(read).ConfigureAwait(false);
                        AssertNotNull(blocker, "assignment blocker");
                        AssertEqual(MissionAssignmentBlockerReasonEnum.NoEligibleCaptain, blocker.Reason);
                    }
                }
            }));
            cases.Add(CaseAsync("priority_supersedes_default", "priority supersedes default", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    RoutingHarness harness = await BuildHarnessAsync(testDb.Driver).ConfigureAwait(false);
                    Captain first = await AddCaptainAsync(testDb.Driver, "first", CaptainStateEnum.Idle, null, CaptainTierEnum.Premium).ConfigureAwait(false);
                    first.Runtime = AgentRuntimeEnum.Codex;
                    Captain second = await AddCaptainAsync(testDb.Driver, "second", CaptainStateEnum.Idle, null, CaptainTierEnum.Premium).ConfigureAwait(false);
                    Persona persona = new Persona("PriorityRole", "persona.worker");
                    persona.CaptainPriorities.Add(new PersonaCaptainPriority { Runtime = AgentRuntimeEnum.Codex });
                    persona.CaptainPriorities.Add(new PersonaCaptainPriority { Runtime = AgentRuntimeEnum.ClaudeCode });
                    Mission mission = await AddMissionAsync(testDb.Driver, harness.Vessel, persona.Name, null, null).ConfigureAwait(false);
                    persona.DefaultCaptainId = second.Id;
                    await testDb.Driver.Captains.UpdateAsync(first).ConfigureAwait(false);
                    await testDb.Driver.Captains.UpdateAsync(second).ConfigureAwait(false);
                    await testDb.Driver.Personas.CreateAsync(persona).ConfigureAwait(false);
                    await testDb.Driver.Missions.UpdateAsync(mission).ConfigureAwait(false);
                    bool assigned = await harness.Missions.TryAssignAsync(mission, harness.Vessel).ConfigureAwait(false);
                    AssertEqual(true, assigned);
                    Mission read = (await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!;
                    AssertEqual(first.Id, read.CaptainId);
                    if (String.IsNullOrEmpty(mission.RequestedCaptainId)) AssertNull(read.RequestedCaptainId, "Hierarchy must not pin a captain");
                    if (!assigned)
                    {
                        MissionAssignmentBlocker? blocker = await harness.Missions.GetAssignmentBlockerAsync(read).ConfigureAwait(false);
                        AssertNotNull(blocker, "assignment blocker");
                        AssertEqual(MissionAssignmentBlockerReasonEnum.NoEligibleCaptain, blocker.Reason);
                    }
                }
            }));
            cases.Add(CaseAsync("priority_quarantined_skipped", "priority quarantined skipped", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    RoutingHarness harness = await BuildHarnessAsync(testDb.Driver).ConfigureAwait(false);
                    Captain first = await AddCaptainAsync(testDb.Driver, "first", CaptainStateEnum.Idle, null, CaptainTierEnum.Premium).ConfigureAwait(false);
                    first.Runtime = AgentRuntimeEnum.Codex;
                    Captain second = await AddCaptainAsync(testDb.Driver, "second", CaptainStateEnum.Idle, null, CaptainTierEnum.Premium).ConfigureAwait(false);
                    Persona persona = new Persona("PriorityRole", "persona.worker");
                    persona.CaptainPriorities.Add(new PersonaCaptainPriority { Runtime = AgentRuntimeEnum.Codex });
                    persona.CaptainPriorities.Add(new PersonaCaptainPriority { Runtime = AgentRuntimeEnum.ClaudeCode });
                    Mission mission = await AddMissionAsync(testDb.Driver, harness.Vessel, persona.Name, null, null).ConfigureAwait(false);
                    first.QuarantineUntilUtc = DateTime.UtcNow.AddHours(1);
                    await testDb.Driver.Captains.UpdateAsync(first).ConfigureAwait(false);
                    await testDb.Driver.Captains.UpdateAsync(second).ConfigureAwait(false);
                    await testDb.Driver.Personas.CreateAsync(persona).ConfigureAwait(false);
                    await testDb.Driver.Missions.UpdateAsync(mission).ConfigureAwait(false);
                    bool assigned = await harness.Missions.TryAssignAsync(mission, harness.Vessel).ConfigureAwait(false);
                    AssertEqual(true, assigned);
                    Mission read = (await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!;
                    AssertEqual(second.Id, read.CaptainId);
                    if (String.IsNullOrEmpty(mission.RequestedCaptainId)) AssertNull(read.RequestedCaptainId, "Hierarchy must not pin a captain");
                    if (!assigned)
                    {
                        MissionAssignmentBlocker? blocker = await harness.Missions.GetAssignmentBlockerAsync(read).ConfigureAwait(false);
                        AssertNotNull(blocker, "assignment blocker");
                        AssertEqual(MissionAssignmentBlockerReasonEnum.NoEligibleCaptain, blocker.Reason);
                    }
                }
            }));
            cases.Add(CaseAsync("priority_owner_fence", "priority owner fence", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    RoutingHarness harness = await BuildHarnessAsync(testDb.Driver).ConfigureAwait(false);
                    Captain first = await AddCaptainAsync(testDb.Driver, "first", CaptainStateEnum.Idle, null, CaptainTierEnum.Premium).ConfigureAwait(false);
                    first.Runtime = AgentRuntimeEnum.Codex;
                    Captain second = await AddCaptainAsync(testDb.Driver, "second", CaptainStateEnum.Idle, null, CaptainTierEnum.Premium).ConfigureAwait(false);
                    Persona persona = new Persona("PriorityRole", "persona.worker");
                    persona.CaptainPriorities.Add(new PersonaCaptainPriority { Runtime = AgentRuntimeEnum.Codex });
                    persona.CaptainPriorities.Add(new PersonaCaptainPriority { Runtime = AgentRuntimeEnum.ClaudeCode });
                    Mission mission = await AddMissionAsync(testDb.Driver, harness.Vessel, persona.Name, null, null).ConfigureAwait(false);
                    TenantMetadata tenant = await testDb.Driver.Tenants.CreateAsync(new TenantMetadata("OtherOwner")).ConfigureAwait(false);
                    UserMaster other = await testDb.Driver.Users.CreateAsync(new UserMaster(tenant.Id, "other@example.com", "password")).ConfigureAwait(false);
                    first.UserId = other.Id;
                    await testDb.Driver.Captains.UpdateAsync(first).ConfigureAwait(false);
                    await testDb.Driver.Captains.UpdateAsync(second).ConfigureAwait(false);
                    await testDb.Driver.Personas.CreateAsync(persona).ConfigureAwait(false);
                    await testDb.Driver.Missions.UpdateAsync(mission).ConfigureAwait(false);
                    bool assigned = await harness.Missions.TryAssignAsync(mission, harness.Vessel).ConfigureAwait(false);
                    AssertEqual(true, assigned);
                    Mission read = (await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!;
                    AssertEqual(second.Id, read.CaptainId);
                    if (String.IsNullOrEmpty(mission.RequestedCaptainId)) AssertNull(read.RequestedCaptainId, "Hierarchy must not pin a captain");
                    if (!assigned)
                    {
                        MissionAssignmentBlocker? blocker = await harness.Missions.GetAssignmentBlockerAsync(read).ConfigureAwait(false);
                        AssertNotNull(blocker, "assignment blocker");
                        AssertEqual(MissionAssignmentBlockerReasonEnum.NoEligibleCaptain, blocker.Reason);
                    }
                }
            }));
            cases.Add(CaseAsync("priority_cross_tenant_fence", "Priority hierarchy never selects another tenant's captain", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    RoutingHarness harness = await BuildHarnessAsync(testDb.Driver).ConfigureAwait(false);
                    TenantMetadata tenant = await testDb.Driver.Tenants.CreateAsync(new TenantMetadata("Foreign tenant")).ConfigureAwait(false);
                    Captain foreign = new Captain("foreign", AgentRuntimeEnum.Codex);
                    foreign.TenantId = tenant.Id;
                    await testDb.Driver.Captains.CreateAsync(foreign).ConfigureAwait(false);
                    Persona persona = new Persona("TenantPriorityRole", "persona.worker");
                    persona.CaptainPriorities.Add(new PersonaCaptainPriority { Runtime = AgentRuntimeEnum.Codex });
                    await testDb.Driver.Personas.CreateAsync(persona).ConfigureAwait(false);
                    Mission mission = await AddMissionAsync(testDb.Driver, harness.Vessel, persona.Name, null, null).ConfigureAwait(false);
                    AssertFalse(await harness.Missions.TryAssignAsync(mission, harness.Vessel).ConfigureAwait(false));
                    AssertNull((await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!.CaptainId);
                }
            }));

            return new TestSuiteDescriptor(SuiteId, "Captain routing and per-step selection", cases);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        private static async Task<RoutingHarness> BuildHarnessAsync(DatabaseDriver db, bool excludeReviewAuthors = false)
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;

            ArmadaSettings settings = new ArmadaSettings();
            settings.DocksDirectory = Path.Combine(Path.GetTempPath(), "armada_routing_docks_" + Guid.NewGuid().ToString("N"));
            settings.ReposDirectory = Path.Combine(Path.GetTempPath(), "armada_routing_repos_" + Guid.NewGuid().ToString("N"));
            settings.ExcludeReviewAuthorsFromJudge = excludeReviewAuthors;

            DirCreatingGitService git = new DirCreatingGitService();
            IDockService dockService = new DockService(logging, db, settings, git);
            CaptainService captainService = new CaptainService(logging, db, settings, git, dockService);
            MissionService missionService = new MissionService(logging, db, settings, dockService, captainService, git: git);

            int nextPid = 5000;
            captainService.OnLaunchAgent = (_, _, _) =>
            {
                nextPid++;
                return Task.FromResult(nextPid);
            };
            missionService.OnGetMissionOutput = _ => "routing output";

            Vessel vessel = new Vessel("routing-vessel", "https://github.com/test/repo.git");
            vessel.LocalPath = Path.Combine(Path.GetTempPath(), "armada_routing_bare_" + Guid.NewGuid().ToString("N"));
            vessel.WorkingDirectory = Path.Combine(Path.GetTempPath(), "armada_routing_work_" + Guid.NewGuid().ToString("N"));
            vessel.DefaultBranch = "main";
            vessel = await db.Vessels.CreateAsync(vessel).ConfigureAwait(false);

            return new RoutingHarness { Missions = missionService, Vessel = vessel };
        }

        private static async Task<Captain> AddCaptainAsync(DatabaseDriver db, string name, CaptainStateEnum state, string? allowedPersonasJson, CaptainTierEnum? tier)
        {
            Captain captain = new Captain(name);
            captain.State = state;
            captain.AllowedPersonas = allowedPersonasJson;
            captain.Tier = tier;
            return await db.Captains.CreateAsync(captain).ConfigureAwait(false);
        }

        private static async Task<Mission> AddMissionAsync(DatabaseDriver db, Vessel vessel, string persona, string? requestedCaptainId, CaptainTierEnum? tier)
        {
            Mission mission = new Mission("route " + persona, "do the work");
            mission.VesselId = vessel.Id;
            mission.Persona = persona;
            mission.RequestedCaptainId = requestedCaptainId;
            mission.Tier = tier;
            mission.Status = MissionStatusEnum.Pending;
            return await db.Missions.CreateAsync(mission).ConfigureAwait(false);
        }

        private static async Task<Mission> AddJudgeOfWorkerAsync(DatabaseDriver db, Vessel vessel, string authorCaptainId, string? requestedCaptainId, CaptainTierEnum? tier)
        {
            Mission worker = await AddMissionAsync(db, vessel, "Worker", null, null).ConfigureAwait(false);
            worker.Status = MissionStatusEnum.WorkProduced;
            worker.CaptainId = authorCaptainId;
            await db.Missions.UpdateAsync(worker).ConfigureAwait(false);
            Mission judge = await AddMissionAsync(db, vessel, "Judge", requestedCaptainId, tier).ConfigureAwait(false);
            judge.DependsOnMissionId = worker.Id;
            return await db.Missions.UpdateAsync(judge).ConfigureAwait(false);
        }

        #endregion

        #region Private-Types

        private sealed class RoutingHarness
        {
            public MissionService Missions { get; set; } = null!;
            public Vessel Vessel { get; set; } = null!;
        }

        #endregion
    }
}

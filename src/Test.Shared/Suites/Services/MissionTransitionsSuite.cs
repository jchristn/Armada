namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Test.Shared.Infrastructure.ApiSurface;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// RELIABILITY_SIMPLICITY_PLAN.md RS-3.5 and RS-3.6: every mission status change goes through
    /// <see cref="MissionTransitions"/>, which checks the state machine, writes with a compare-and-set, emits
    /// <c>mission.status_changed</c> and re-evaluates the voyage. The source scan keeps direct status writes out of
    /// Armada.Core and Armada.Server.
    /// </summary>
    public sealed class MissionTransitionsSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("internal_table_extends_operator_table", "Every operator edge is an internal edge, and the internal edges Armada takes are present", TestTags.Positive, () =>
            {
                foreach (MissionStatusEnum from in Enum.GetValues<MissionStatusEnum>())
                    foreach (MissionStatusEnum to in Enum.GetValues<MissionStatusEnum>())
                        if (MissionStateMachine.IsValidTransition(from, to))
                            AssertTrue(MissionStateMachine.IsValidInternalTransition(from, to), from + " -> " + to + " is an operator edge but not an internal one");

                AssertTrue(MissionStateMachine.IsValidInternalTransition(MissionStatusEnum.Assigned, MissionStatusEnum.Pending), "Assigned -> Pending (rollback, redispatch)");
                AssertTrue(MissionStateMachine.IsValidInternalTransition(MissionStatusEnum.InProgress, MissionStatusEnum.Pending), "InProgress -> Pending (escalation)");
                AssertTrue(MissionStateMachine.IsValidInternalTransition(MissionStatusEnum.InProgress, MissionStatusEnum.Verifying), "InProgress -> Verifying");
                AssertFalse(MissionStateMachine.IsValidTransition(MissionStatusEnum.InProgress, MissionStatusEnum.Verifying), "operators cannot move a mission into Verifying");
                AssertFalse(MissionStateMachine.IsValidInternalTransition(MissionStatusEnum.Complete, MissionStatusEnum.Cancelled), "a completed mission is never cancelled");
                AssertFalse(MissionStateMachine.IsValidInternalTransition(MissionStatusEnum.Cancelled, MissionStatusEnum.Cancelled), "a terminal status is not rewritten");
                AssertTrue(MissionStateMachine.IsValidInternalTransition(MissionStatusEnum.WorkProduced, MissionStatusEnum.WorkProduced), "a non-terminal status may be rewritten to update fields");
                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("transition_writes_and_emits", "A transition from an allowed status writes the mission and emits mission.status_changed with the previous status", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Mission mission = await testDb.Driver.Missions.CreateAsync(new Mission("transition", "brief") { Status = MissionStatusEnum.InProgress }).ConfigureAwait(false);
                MissionTransitions transitions = new MissionTransitions(testDb.Driver);

                bool written = await transitions.TryTransitionAsync(mission, new[] { MissionStatusEnum.InProgress }, MissionStatusEnum.Failed, m => m.FailureReason = "boom").ConfigureAwait(false);

                AssertTrue(written, "written");
                Mission stored = (await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!;
                AssertEqual(MissionStatusEnum.Failed, stored.Status);
                AssertEqual("boom", stored.FailureReason, "the mutation is written with the status");
                List<ArmadaEvent> events = (await testDb.Driver.Events.EnumerateByMissionAsync(mission.Id).ConfigureAwait(false)).Where(e => e.EventType == "mission.status_changed").ToList();
                AssertEqual(1, events.Count, "one status event");
                MissionStatusChangedPayload payload = JsonSerializer.Deserialize<MissionStatusChangedPayload>(events[0].Payload!, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
                AssertEqual(MissionStatusEnum.Failed, payload.Status);
                AssertEqual(MissionStatusEnum.InProgress, payload.PreviousStatus);
            }));

            cases.Add(CaseAsync("stale_status_is_refused", "A transition whose stored status already moved on is refused, writes nothing, and reports the stored status", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Mission mission = await testDb.Driver.Missions.CreateAsync(new Mission("race", "brief") { Status = MissionStatusEnum.InProgress }).ConfigureAwait(false);
                MissionTransitions transitions = new MissionTransitions(testDb.Driver);
                Mission other = (await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!;
                AssertTrue(await transitions.TryTransitionAsync(other, new[] { MissionStatusEnum.InProgress }, MissionStatusEnum.Cancelled).ConfigureAwait(false), "the other writer wins");

                bool written = await transitions.TryTransitionAsync(mission, new[] { MissionStatusEnum.InProgress }, MissionStatusEnum.WorkProduced, m => m.FailureReason = "late").ConfigureAwait(false);

                AssertFalse(written, "refused");
                AssertEqual(MissionStatusEnum.Cancelled, mission.Status, "the caller sees the stored status");
                Mission stored = (await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!;
                AssertEqual(MissionStatusEnum.Cancelled, stored.Status);
                AssertNull(stored.FailureReason, "nothing of the refused write is stored");
                int statusEvents = (await testDb.Driver.Events.EnumerateByMissionAsync(mission.Id).ConfigureAwait(false)).Count(e => e.EventType == "mission.status_changed");
                AssertEqual(1, statusEvents, "only the winning transition emitted");
            }));

            cases.Add(CaseAsync("stale_version_is_refused", "A transition based on a stale read is refused even when the status still matches", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Mission mission = await testDb.Driver.Missions.CreateAsync(new Mission("version", "brief") { Status = MissionStatusEnum.WorkProduced }).ConfigureAwait(false);
                DateTime staleVersion = mission.LastUpdateUtc.AddSeconds(-5);
                MissionTransitions transitions = new MissionTransitions(testDb.Driver);
                MissionTransitionRequest request = new MissionTransitionRequest { To = MissionStatusEnum.LandingFailed, ExpectedLastUpdateUtc = staleVersion };
                request.AllowedFrom.Add(MissionStatusEnum.WorkProduced);

                AssertFalse(await transitions.TryTransitionAsync(mission, request).ConfigureAwait(false), "refused");
                AssertEqual(MissionStatusEnum.WorkProduced, (await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!.Status);
            }));

            cases.Add(CaseAsync("illegal_edge_is_refused", "An edge the state machine does not have is never written; operator requests use the operator table", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Mission complete = await testDb.Driver.Missions.CreateAsync(new Mission("done", "brief") { Status = MissionStatusEnum.Complete }).ConfigureAwait(false);
                Mission running = await testDb.Driver.Missions.CreateAsync(new Mission("running", "brief") { Status = MissionStatusEnum.InProgress }).ConfigureAwait(false);
                MissionTransitions transitions = new MissionTransitions(testDb.Driver);

                AssertFalse(await transitions.TryTransitionAsync(complete, new[] { MissionStatusEnum.Complete }, MissionStatusEnum.Cancelled).ConfigureAwait(false), "Complete -> Cancelled refused");
                AssertEqual(MissionStatusEnum.Complete, (await testDb.Driver.Missions.ReadAsync(complete.Id).ConfigureAwait(false))!.Status);

                MissionTransitionRequest operatorRequest = new MissionTransitionRequest { To = MissionStatusEnum.Pending, Operator = true };
                operatorRequest.AllowedFrom.Add(MissionStatusEnum.InProgress);
                AssertFalse(await transitions.TryTransitionAsync(running, operatorRequest).ConfigureAwait(false), "operators cannot requeue a running mission");
                AssertTrue(await transitions.TryTransitionAsync(running, new[] { MissionStatusEnum.InProgress }, MissionStatusEnum.Pending).ConfigureAwait(false), "Armada can");
            }));

            cases.Add(CaseAsync("hooks_receive_change_and_terminal_voyage", "The status hook receives every change; the voyage hook runs only when a voyage mission becomes terminal", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Voyage voyage = await testDb.Driver.Voyages.CreateAsync(new Voyage("hooks")).ConfigureAwait(false);
                Mission mission = await testDb.Driver.Missions.CreateAsync(new Mission("hooks", "brief") { Status = MissionStatusEnum.Assigned, VoyageId = voyage.Id }).ConfigureAwait(false);
                MissionTransitions transitions = new MissionTransitions(testDb.Driver);
                List<string> changes = new List<string>();
                List<string> voyages = new List<string>();
                transitions.StatusChanged = (m, previous, message) => { changes.Add(previous + "->" + m.Status); return Task.CompletedTask; };
                transitions.VoyageChanged = (voyageId, token) => { voyages.Add(voyageId); return Task.CompletedTask; };

                AssertTrue(await transitions.TryTransitionAsync(mission, new[] { MissionStatusEnum.Assigned }, MissionStatusEnum.InProgress).ConfigureAwait(false));
                AssertEqual(0, voyages.Count, "a non-terminal change does not re-evaluate the voyage");
                AssertTrue(await transitions.TryTransitionAsync(mission, new[] { MissionStatusEnum.InProgress }, MissionStatusEnum.InProgress, m => m.ProcessId = 42).ConfigureAwait(false), "a field update keeps the status");
                AssertTrue(await transitions.TryTransitionAsync(mission, new[] { MissionStatusEnum.InProgress }, MissionStatusEnum.Failed).ConfigureAwait(false));

                AssertEqual("Assigned->InProgress|InProgress->Failed", String.Join("|", changes), "a field update emits no status change");
                AssertEqual(voyage.Id, voyages.Single(), "the voyage is re-evaluated once, at the terminal status");
                AssertEqual(0, (await testDb.Driver.Events.EnumerateByMissionAsync(mission.Id).ConfigureAwait(false)).Count(e => e.EventType == "mission.status_changed"), "the hook replaces the default event row");
            }));

            cases.Add(CaseAsync("no_direct_status_writes", "Armada.Core and Armada.Server change mission status only through MissionTransitions", TestTags.Positive, () =>
            {
                string src = Path.Combine(ApiSurfaceFiles.FindRepositoryRoot(), "src");
                List<string> offenders = new List<string>();
                foreach (string project in new[] { "Armada.Core", "Armada.Server" })
                    foreach (string file in Directory.EnumerateFiles(Path.Combine(src, project), "*.cs", SearchOption.AllDirectories))
                    {
                        string relative = Path.GetRelativePath(src, file).Replace('\\', '/');
                        if (relative.Contains("/obj/") || relative.Contains("/bin/") || IsAllowed(relative)) continue;
                        string[] lines = File.ReadAllLines(file);
                        for (int i = 0; i < lines.Length; i++)
                            if (IsDirectStatusWrite(lines[i])) offenders.Add(relative + ":" + (i + 1) + ": " + lines[i].Trim());
                    }

                AssertEqual(0, offenders.Count, "direct mission status writes (use MissionTransitions):\n" + String.Join("\n", offenders));
                AssertTrue(IsDirectStatusWrite("            mission.Status = MissionStatusEnum.Failed;"), "the scan recognizes a literal write");
                AssertTrue(IsDirectStatusWrite("            dependent.Status = blockThis ? MissionStatusEnum.Blocked : MissionStatusEnum.Cancelled;"), "the scan recognizes a conditional write");
                AssertTrue(IsDirectStatusWrite("            tmMission.Status = tmNewStatus;"), "the scan recognizes a variable write to a mission");
                AssertFalse(IsDirectStatusWrite("            voyage.Status = VoyageStatusEnum.Complete;"), "other entities are not mission writes");
                return Task.CompletedTask;
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.MissionTransitions",
                displayName: "Mission Transitions",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static readonly Regex _StatusAssignment = new Regex(@"(?<lhs>[\w\.\]\)]*)\bStatus\s*=\s*(?!=)(?<rhs>[^;]*)", RegexOptions.Compiled);

        private static bool IsAllowed(string relative)
        {
            // The transition itself, and the database layer: row readers, the admission compare-and-set, and migrations.
            return relative == "Armada.Core/Services/MissionTransitions.cs" || relative.StartsWith("Armada.Core/Database/", StringComparison.Ordinal);
        }

        private static bool IsDirectStatusWrite(string line)
        {
            string code = line.Split(new[] { "//" }, StringSplitOptions.None)[0];
            // A new mission's initial status is not a transition.
            if (code.Contains("new Mission", StringComparison.Ordinal)) return false;
            Match match = _StatusAssignment.Match(code);
            if (!match.Success) return false;
            string lhs = match.Groups["lhs"].Value;
            string rhs = match.Groups["rhs"].Value;
            if (rhs.Contains("VoyageStatusEnum", StringComparison.Ordinal)) return false;
            if (rhs.Contains("MissionStatusEnum.", StringComparison.Ordinal) && !rhs.Contains(".ToString()", StringComparison.Ordinal)) return true;
            return Regex.IsMatch(lhs, @"[mM]ission\w*\.$");
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.MissionTransitions",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion
    }
}

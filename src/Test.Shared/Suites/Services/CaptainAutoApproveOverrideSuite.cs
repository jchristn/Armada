namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Ask;
    using Armada.Runtimes;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Auto-approve overrides on top of the per-captain setting: the per-vessel override for missions (W1.5) and
    /// Ask thread turns running without auto-approve unless <c>Ask.CaptainAutoApprove</c> is set (O-02). Covers the
    /// launch-copy helper, the Claude Code flags it produces, the Ask coordinator handing the runner a forced-off
    /// captain while the stored captain is unchanged, and the vessel column round trip on the configured provider.
    /// </summary>
    public sealed class CaptainAutoApproveOverrideSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.CaptainAutoApproveOverride";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("null_or_equal_override_returns_same_captain", "A null override, or one equal to the captain's setting, launches the stored captain", () =>
            {
                Captain on = new Captain("on");
                Captain off = new Captain("off") { RuntimeOptionsJson = "{\"autoApprove\":false}" };
                AssertTrue(Object.ReferenceEquals(on, CaptainRuntimeOptions.WithEffectiveAutoApprove(on, null)), "null override");
                AssertTrue(Object.ReferenceEquals(on, CaptainRuntimeOptions.WithEffectiveAutoApprove(on, true)), "equal override (on)");
                AssertTrue(Object.ReferenceEquals(off, CaptainRuntimeOptions.WithEffectiveAutoApprove(off, false)), "equal override (off)");
                return Task.CompletedTask;
            }));

            cases.Add(Case("override_copies_and_never_mutates", "An override yields a copy with the setting flipped; the stored captain keeps its options", () =>
            {
                Captain stored = new Captain("stored") { RuntimeOptionsJson = "{\"approvalPolicy\":\"ask\"}", Model = "m1", SystemInstructions = "be brief" };
                Captain launched = CaptainRuntimeOptions.WithEffectiveAutoApprove(stored, false);
                AssertFalse(Object.ReferenceEquals(stored, launched), "copy");
                AssertFalse(CaptainRuntimeOptions.GetAutoApprove(launched), "launched copy has auto-approve off");
                AssertTrue(CaptainRuntimeOptions.GetAutoApprove(stored), "stored captain unchanged");
                AssertEqual("{\"approvalPolicy\":\"ask\"}", stored.RuntimeOptionsJson);
                AssertEqual("ask", JsonHelper.Deserialize<RuntimeOptionsProbe>(launched.RuntimeOptionsJson!).ApprovalPolicy, "other runtime options kept");
                AssertEqual(stored.Id, launched.Id);
                AssertEqual(stored.Name, launched.Name);
                AssertEqual("m1", launched.Model);
                AssertEqual("be brief", launched.SystemInstructions);
                AssertEqual(stored.Runtime, launched.Runtime);

                Captain storedOff = new Captain("off") { RuntimeOptionsJson = "{\"autoApprove\":false}" };
                Captain allowed = CaptainRuntimeOptions.WithEffectiveAutoApprove(storedOff, true);
                AssertTrue(CaptainRuntimeOptions.GetAutoApprove(allowed), "vessel override true wins over captain false");
                AssertFalse(CaptainRuntimeOptions.GetAutoApprove(storedOff), "stored captain unchanged");
                return Task.CompletedTask;
            }));

            cases.Add(Case("claude_code_flags_follow_the_override", "Claude Code gets acceptEdits for a forced-off launch and the bypass flag for a forced-on launch", () =>
            {
                InspectableClaude runtime = new InspectableClaude(Logging());
                List<string> forcedOff = runtime.Args(CaptainRuntimeOptions.WithEffectiveAutoApprove(new Captain("c"), false));
                AssertFalse(forcedOff.Contains("--dangerously-skip-permissions"), "no bypass when forced off");
                AssertTrue(forcedOff.Contains("--permission-mode") && forcedOff.Contains("acceptEdits"), "acceptEdits when forced off");
                int allowed = forcedOff.IndexOf("--allowedTools");
                AssertTrue(allowed >= 0 && allowed + 1 < forcedOff.Count && forcedOff[allowed + 1] == "mcp__armada", "Armada MCP tools stay allowed when forced off");

                Captain storedOff = new Captain("c2") { RuntimeOptionsJson = "{\"autoApprove\":false}" };
                List<string> forcedOn = runtime.Args(CaptainRuntimeOptions.WithEffectiveAutoApprove(storedOff, true));
                AssertTrue(forcedOn.Contains("--dangerously-skip-permissions"), "bypass when a vessel allows it");
                return Task.CompletedTask;
            }));

            cases.Add(Case("ask_turn_runs_without_auto_approve_by_default", "An Ask turn hands the runner the captain with auto-approve off (Ask.CaptainAutoApprove default false)", async () =>
            {
                using AskTestHarness h = await AskTestHarness.CreateAsync().ConfigureAwait(false);
                AssertFalse(h.Settings.Ask.CaptainAutoApprove, "default is off");
                Captain options = await RunOneTurnAsync(h, "usr_aa_default").ConfigureAwait(false);
                AssertFalse(CaptainRuntimeOptions.GetAutoApprove(options), "turn captain runs without auto-approve");

                Captain? stored = await h.Db.Driver.Captains.ReadAsync(options.Id).ConfigureAwait(false);
                AssertTrue(CaptainRuntimeOptions.GetAutoApprove(stored), "stored captain keeps auto-approve on");
            }));

            cases.Add(Case("ask_turn_uses_captain_setting_when_enabled", "With Ask.CaptainAutoApprove true the captain's own setting applies", async () =>
            {
                using AskTestHarness h = await AskTestHarness.CreateAsync().ConfigureAwait(false);
                h.Settings.Ask.CaptainAutoApprove = true;
                Captain options = await RunOneTurnAsync(h, "usr_aa_enabled").ConfigureAwait(false);
                AssertTrue(CaptainRuntimeOptions.GetAutoApprove(options), "captain's own setting (on)");
            }));

            cases.Add(Case("vessel_auto_approve_round_trips", "Vessel.AutoApprove persists null, true, and false", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    DatabaseDriver db = testDb.Driver;
                    Vessel vessel = new Vessel("aa-vessel-" + Guid.NewGuid().ToString("N").Substring(0, 8), "https://example.invalid/aa.git");
                    vessel.TenantId = Constants.DefaultTenantId;
                    vessel = await db.Vessels.CreateAsync(vessel).ConfigureAwait(false);
                    Vessel? read = await db.Vessels.ReadAsync(vessel.Id).ConfigureAwait(false);
                    AssertNull(read!.AutoApprove, "default null");

                    read.AutoApprove = false;
                    await db.Vessels.UpdateAsync(read).ConfigureAwait(false);
                    read = await db.Vessels.ReadAsync(vessel.Id).ConfigureAwait(false);
                    AssertEqual(false, read!.AutoApprove);

                    read.AutoApprove = true;
                    await db.Vessels.UpdateAsync(read).ConfigureAwait(false);
                    read = await db.Vessels.ReadAsync(vessel.Id).ConfigureAwait(false);
                    AssertEqual(true, read!.AutoApprove);

                    read.AutoApprove = null;
                    await db.Vessels.UpdateAsync(read).ConfigureAwait(false);
                    read = await db.Vessels.ReadAsync(vessel.Id).ConfigureAwait(false);
                    AssertNull(read!.AutoApprove, "cleared");

                    Vessel created = new Vessel("aa-vessel-off-" + Guid.NewGuid().ToString("N").Substring(0, 8), "https://example.invalid/aa2.git");
                    created.TenantId = Constants.DefaultTenantId;
                    created.AutoApprove = false;
                    created = await db.Vessels.CreateAsync(created).ConfigureAwait(false);
                    AssertEqual(false, (await db.Vessels.ReadAsync(created.Id).ConfigureAwait(false))!.AutoApprove, "set on create");
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Captain auto-approve overrides (vessel, Ask)",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task<Captain> RunOneTurnAsync(AskTestHarness h, string userId)
        {
            AuthContext owner = AskTestHarness.User(userId);
            Captain captain = await h.Db.Driver.Captains.CreateAsync(new Captain("captain-" + userId) { TenantId = Constants.DefaultTenantId }).ConfigureAwait(false);
            AskThread thread = await h.Threads.CreateThreadAsync(owner, new AskThreadCreateRequest { CaptainId = captain.Id }).ConfigureAwait(false);
            h.Runner.Reply = "ok";
            await h.Turns.SendMessageAsync(owner, thread.Id, new AskMessageSendRequest { Content = "hello" }).ConfigureAwait(false);
            bool done = await AskTestHarness.WaitUntilAsync(() => Task.FromResult(h.Runner.Calls.Count > 0 && h.Turns.ActiveTurnId(thread.Id) == null), 10000).ConfigureAwait(false);
            AssertTrue(done, "turn finished");
            return h.Runner.Calls.Last().Captain;
        }

        private static LoggingModule Logging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { TestTags.Positive });
        }

        #endregion

        #region Nested-Types

        private sealed class InspectableClaude : ClaudeCodeRuntime
        {
            public InspectableClaude(LoggingModule logging) : base(logging) { }

            public List<string> Args(Captain captain) => BuildArguments(Path.GetTempPath(), "p", null, null, captain);
        }

        #endregion
    }
}

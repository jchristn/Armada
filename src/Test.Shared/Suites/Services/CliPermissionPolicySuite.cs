namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Protocol;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using Armada.Runtimes;
    using Armada.Server;
    using Armada.Server.Ask;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// CLI tool permission policy resolution (Ask turns and missions, with the legacy auto-approve mapping and the
    /// ApproveInArmada fallbacks), the flags each runtime gets for Refuse, ApproveInArmada, and Bypass, and the typed
    /// permission denial report of Claude Code's stream-json result event.
    /// </summary>
    public sealed class CliPermissionPolicySuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.CliPermissionPolicy";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("ask_defaults_to_approve_in_armada", "With no overrides an Ask turn uses Permissions.AskDefaultPolicy (ApproveInArmada)", () =>
            {
                ArmadaSettings settings = new ArmadaSettings();
                AssertEqual(CliPermissionPolicyEnum.ApproveInArmada, settings.Permissions.AskDefaultPolicy, "default Ask policy");
                AssertEqual(CliPermissionPolicyEnum.Bypass, settings.Permissions.MissionDefaultPolicy, "default mission policy keeps the previous behavior");
                AssertFalse(settings.Permissions.AllowOwnerApproval, "owner approval off by default");
                AssertEqual(600, settings.Permissions.PromptTimeoutSeconds);

                CliPermissionResolution r = CliPermissionPolicyResolver.ResolveForAsk(settings, new AskThread(), new Captain("c"), true);
                AssertEqual(CliPermissionPolicyEnum.ApproveInArmada, r.Effective);
                AssertEqual(CliPermissionPolicySourceEnum.ServerDefault, r.Source);
                AssertNull(r.FallbackReason);
                AssertContains("Permissions.AskDefaultPolicy", r.Note);
            }));

            cases.Add(Case("ask_resolution_order", "Thread override, then captain policy, then legacy auto-approve (only with Ask.CaptainAutoApprove), then the default", () =>
            {
                ArmadaSettings settings = new ArmadaSettings();
                Captain bypassCaptain = new Captain("b") { CliPermissionPolicy = CliPermissionPolicyEnum.Bypass };
                Captain refuseCaptain = new Captain("r") { CliPermissionPolicy = CliPermissionPolicyEnum.Refuse };
                Captain legacyOn = new Captain("l");
                Captain legacyOff = new Captain("lo") { RuntimeOptionsJson = "{\"autoApprove\":false}" };

                AskThread overridden = new AskThread { CliPermissionPolicy = CliPermissionPolicyEnum.Refuse };
                CliPermissionResolution thread = CliPermissionPolicyResolver.ResolveForAsk(settings, overridden, bypassCaptain, true);
                AssertEqual(CliPermissionPolicyEnum.Refuse, thread.Effective, "thread override wins");
                AssertEqual(CliPermissionPolicySourceEnum.AskThread, thread.Source);

                CliPermissionResolution captain = CliPermissionPolicyResolver.ResolveForAsk(settings, new AskThread(), refuseCaptain, true);
                AssertEqual(CliPermissionPolicyEnum.Refuse, captain.Effective, "captain policy");
                AssertEqual(CliPermissionPolicySourceEnum.Captain, captain.Source);

                CliPermissionResolution untrusted = CliPermissionPolicyResolver.ResolveForAsk(settings, new AskThread(), bypassCaptain, true);
                AssertEqual(CliPermissionPolicyEnum.ApproveInArmada, untrusted.Effective, "a captain-level Bypass is not honored for Ask unless Ask.CaptainAutoApprove");
                AssertEqual(CliPermissionPolicySourceEnum.ServerDefault, untrusted.Source);
                AssertEqual(CliPermissionPolicyEnum.ApproveInArmada, CliPermissionPolicyResolver.ResolveForAsk(settings, new AskThread(), legacyOn, true).Effective, "legacy autoApprove ignored for Ask by default");

                settings.Ask.CaptainAutoApprove = true;
                AssertEqual(CliPermissionPolicyEnum.Bypass, CliPermissionPolicyResolver.ResolveForAsk(settings, new AskThread(), bypassCaptain, true).Effective, "captain Bypass honored with Ask.CaptainAutoApprove");
                CliPermissionResolution legacy = CliPermissionPolicyResolver.ResolveForAsk(settings, new AskThread(), legacyOn, true);
                AssertEqual(CliPermissionPolicyEnum.Bypass, legacy.Effective, "legacy autoApprove (absent = true) maps to Bypass");
                AssertEqual(CliPermissionPolicySourceEnum.CaptainAutoApprove, legacy.Source);
                AssertEqual(CliPermissionPolicyEnum.Refuse, CliPermissionPolicyResolver.ResolveForAsk(settings, new AskThread(), legacyOff, true).Effective, "legacy autoApprove false maps to Refuse");

                AskThread bypassThread = new AskThread { CliPermissionPolicy = CliPermissionPolicyEnum.Bypass };
                settings.Ask.CaptainAutoApprove = false;
                AssertEqual(CliPermissionPolicyEnum.Bypass, CliPermissionPolicyResolver.ResolveForAsk(settings, bypassThread, refuseCaptain, true).Effective, "an admin's thread Bypass is honored");
            }));

            cases.Add(Case("mission_resolution_order", "Vessel auto-approve, captain policy, captain legacy auto-approve, then the mission default; vessel false caps Bypass", () =>
            {
                ArmadaSettings settings = new ArmadaSettings();
                Captain plain = new Captain("p");
                CliPermissionResolution def = CliPermissionPolicyResolver.ResolveForMission(settings, plain, null, true, false);
                AssertEqual(CliPermissionPolicyEnum.Bypass, def.Effective, "previous behavior: captains without autoApprove bypass");
                AssertEqual(CliPermissionPolicySourceEnum.ServerDefault, def.Source);

                settings.Permissions.MissionDefaultPolicy = CliPermissionPolicyEnum.ApproveInArmada;
                AssertEqual(CliPermissionPolicyEnum.ApproveInArmada, CliPermissionPolicyResolver.ResolveForMission(settings, plain, null, true, false).Effective);

                Captain legacyOff = new Captain("lo") { RuntimeOptionsJson = "{\"autoApprove\":false}" };
                CliPermissionResolution legacy = CliPermissionPolicyResolver.ResolveForMission(settings, legacyOff, null, true, false);
                AssertEqual(CliPermissionPolicyEnum.Refuse, legacy.Effective, "explicit legacy false maps to Refuse");
                AssertEqual(CliPermissionPolicySourceEnum.CaptainAutoApprove, legacy.Source);
                Captain legacyOn = new Captain("ln") { RuntimeOptionsJson = "{\"autoApprove\":true}" };
                AssertEqual(CliPermissionPolicyEnum.Bypass, CliPermissionPolicyResolver.ResolveForMission(settings, legacyOn, null, true, false).Effective, "explicit legacy true maps to Bypass");

                Captain policy = new Captain("pol") { CliPermissionPolicy = CliPermissionPolicyEnum.ApproveInArmada, RuntimeOptionsJson = "{\"autoApprove\":true}" };
                CliPermissionResolution captain = CliPermissionPolicyResolver.ResolveForMission(settings, policy, null, true, false);
                AssertEqual(CliPermissionPolicyEnum.ApproveInArmada, captain.Effective, "captain policy wins over legacy autoApprove");
                AssertEqual(CliPermissionPolicySourceEnum.Captain, captain.Source);

                Vessel allow = new Vessel { AutoApprove = true };
                Vessel deny = new Vessel { AutoApprove = false };
                CliPermissionResolution vesselOn = CliPermissionPolicyResolver.ResolveForMission(settings, policy, allow, true, false);
                AssertEqual(CliPermissionPolicyEnum.Bypass, vesselOn.Effective, "vessel auto-approve true is Bypass");
                AssertEqual(CliPermissionPolicySourceEnum.VesselAutoApprove, vesselOn.Source);
                CliPermissionResolution capped = CliPermissionPolicyResolver.ResolveForMission(settings, legacyOn, deny, true, false);
                AssertEqual(CliPermissionPolicyEnum.Refuse, capped.Effective, "vessel auto-approve false caps Bypass to Refuse");
                AssertEqual(CliPermissionPolicySourceEnum.VesselAutoApprove, capped.Source);
                AssertEqual(CliPermissionPolicyEnum.ApproveInArmada, CliPermissionPolicyResolver.ResolveForMission(settings, policy, deny, true, false).Effective, "vessel false keeps a non-Bypass policy");
            }));

            cases.Add(Case("approve_in_armada_fallbacks", "ApproveInArmada runs as Refuse without a prompt hook, without a scoped token, or on a Harbor, with the reason", () =>
            {
                ArmadaSettings settings = new ArmadaSettings();
                settings.Permissions.MissionDefaultPolicy = CliPermissionPolicyEnum.ApproveInArmada;
                foreach (AgentRuntimeEnum runtime in Enum.GetValues<AgentRuntimeEnum>())
                {
                    Captain captain = new Captain("c") { Runtime = runtime };
                    CliPermissionResolution r = CliPermissionPolicyResolver.ResolveForMission(settings, captain, null, true, false);
                    bool hook = runtime == AgentRuntimeEnum.ClaudeCode || runtime == AgentRuntimeEnum.ApiEndpoint;
                    AssertEqual(hook, CliPermissionPolicyResolver.SupportsPromptHook(runtime), "hook support: " + runtime);
                    AssertEqual(hook ? CliPermissionPolicyEnum.ApproveInArmada : CliPermissionPolicyEnum.Refuse, r.Effective, "effective: " + runtime);
                    AssertEqual(CliPermissionPolicyEnum.ApproveInArmada, r.Requested, "requested: " + runtime);
                    if (!hook)
                    {
                        AssertEqual(CliPermissionFallbackReasonEnum.RuntimeUnsupported, r.FallbackReason, "reason: " + runtime);
                        AssertContains("running as Refuse", r.Note);
                    }
                }

                Captain claude = new Captain("cc") { Runtime = AgentRuntimeEnum.ClaudeCode };
                AssertEqual(CliPermissionFallbackReasonEnum.NoSessionToken, CliPermissionPolicyResolver.ResolveForMission(settings, claude, null, false, false).FallbackReason);
                AssertEqual(CliPermissionFallbackReasonEnum.RemoteHarbor, CliPermissionPolicyResolver.ResolveForMission(settings, claude, null, true, true).FallbackReason);
                AssertEqual(CliPermissionFallbackReasonEnum.NoSessionToken, CliPermissionPolicyResolver.ResolveForAsk(settings, new AskThread(), claude, false).FallbackReason);
                AssertNull(CliPermissionPolicyResolver.ResolveForMission(settings, new Captain("api") { Runtime = AgentRuntimeEnum.ApiEndpoint }, null, false, false).FallbackReason, "the in-process gate needs no token");
                AssertEqual(CliPermissionFallbackReasonEnum.RemoteHarbor, CliPermissionPolicyResolver.ResolveForMission(settings, new Captain("api2") { Runtime = AgentRuntimeEnum.ApiEndpoint }, null, true, true).FallbackReason, "Harbor launches fall back");
                settings.Permissions.MissionDefaultPolicy = CliPermissionPolicyEnum.Bypass;
                AssertNull(CliPermissionPolicyResolver.ResolveForMission(settings, claude, null, false, true).FallbackReason, "only ApproveInArmada falls back");
            }));

            cases.Add(Case("prompt_timeout_is_clamped", "Permissions.PromptTimeoutSeconds is clamped to 10..3600", () =>
            {
                CliPermissionSettings p = new CliPermissionSettings();
                p.PromptTimeoutSeconds = 1;
                AssertEqual(10, p.PromptTimeoutSeconds);
                p.PromptTimeoutSeconds = 99999;
                AssertEqual(3600, p.PromptTimeoutSeconds);
                ArmadaSettings settings = new ArmadaSettings();
                settings.Permissions = null!;
                AssertNotNull(settings.Permissions, "null restores defaults");
            }));

            cases.Add(Case("claude_flags_per_policy", "Claude Code: Refuse is acceptEdits, ApproveInArmada adds --permission-prompt-tool and MCP_TOOL_TIMEOUT, Bypass skips permissions", () =>
            {
                Captain refuse = CaptainRuntimeOptions.WithEffectiveAutoApprove(new Captain("c"), false);
                InspectableClaude refuseRuntime = new InspectableClaude(Logging());
                AssertFalse(CliPermissionLaunch.Apply(refuseRuntime, CliPermissionPolicyEnum.Refuse, 600), "Refuse configures no prompt tool");
                List<string> refuseArgs = refuseRuntime.Args(refuse);
                AssertTrue(refuseArgs.Contains("acceptEdits"), "acceptEdits");
                AssertFalse(refuseArgs.Contains("--permission-prompt-tool"), "no prompt tool under Refuse");
                AssertFalse(refuseArgs.Contains("--dangerously-skip-permissions"));

                InspectableClaude approveRuntime = new InspectableClaude(Logging());
                AssertTrue(CliPermissionLaunch.Apply(approveRuntime, CliPermissionPolicyEnum.ApproveInArmada, 300), "ApproveInArmada configures the prompt tool");
                List<string> approveArgs = approveRuntime.Args(refuse);
                int at = approveArgs.IndexOf("--permission-prompt-tool");
                AssertTrue(at >= 0 && approveArgs[at + 1] == "mcp__armada__cli_permission_prompt", "prompt tool flag: " + String.Join(" ", approveArgs));
                AssertTrue(approveArgs.Contains("acceptEdits"), "edits still accepted");
                int allowed = approveArgs.IndexOf("--allowedTools");
                AssertTrue(allowed >= 0 && approveArgs[allowed + 1] == "mcp__armada", "Armada MCP tools stay allowed");
                Dictionary<string, string?> env = approveRuntime.Env(refuse);
                AssertEqual("420000", env.TryGetValue("MCP_TOOL_TIMEOUT", out string? t) ? t : null, "MCP tool timeout above the prompt timeout");

                Captain bypass = CaptainRuntimeOptions.WithEffectiveAutoApprove(new Captain("b") { RuntimeOptionsJson = "{\"autoApprove\":false}" }, true);
                List<string> bypassArgs = approveRuntime.Args(bypass);
                AssertTrue(bypassArgs.Contains("--dangerously-skip-permissions"), "Bypass");
                AssertFalse(bypassArgs.Contains("--permission-prompt-tool"), "no prompt tool with the bypass flag");
                AssertFalse(approveRuntime.Env(bypass).ContainsKey("MCP_TOOL_TIMEOUT"), "no timeout override with the bypass flag");
            }));

            cases.Add(Case("other_runtime_flags_per_policy", "Codex, Gemini, Cursor, OpenCode, and Mux map Refuse and Bypass onto their existing flags and never get a prompt tool", () =>
            {
                Captain refuse = CaptainRuntimeOptions.WithEffectiveAutoApprove(new Captain("c"), false);
                Captain bypass = new Captain("b");

                InspectableCodex codex = new InspectableCodex(Logging());
                AssertFalse(CliPermissionLaunch.Apply(codex, CliPermissionPolicyEnum.ApproveInArmada, 600), "Codex has no prompt hook");
                List<string> codexRefuse = codex.Args(refuse);
                AssertTrue(codexRefuse.Contains("--sandbox") && codexRefuse.Contains("workspace-write"), "Codex Refuse sandboxes");
                AssertFalse(codexRefuse.Contains("--dangerously-bypass-approvals-and-sandbox"));
                codex.ApprovalMode = "dangerous";
                AssertTrue(codex.Args(bypass).Contains("--dangerously-bypass-approvals-and-sandbox"), "Codex Bypass (dangerous mode)");
                AssertFalse(codex.Args(refuse).Contains("--dangerously-bypass-approvals-and-sandbox"), "Refuse never bypasses, whatever the mode");

                InspectableGemini gemini = new InspectableGemini(Logging());
                AssertTrue(gemini.Args(refuse).Contains("auto_edit"), "Gemini Refuse is auto_edit");
                AssertTrue(gemini.Args(bypass).Contains("yolo"), "Gemini Bypass is yolo");

                InspectableCursor cursor = new InspectableCursor(Logging());
                AssertFalse(cursor.Args(refuse).Contains("--force"), "Cursor Refuse has no --force");
                AssertTrue(cursor.Args(bypass).Contains("--force"), "Cursor Bypass is --force");

                InspectableOpenCode openCode = new InspectableOpenCode(Logging());
                AssertFalse(openCode.Args(refuse).Contains("--auto"), "OpenCode Refuse has no --auto");
                AssertTrue(openCode.Args(bypass).Contains("--auto"), "OpenCode Bypass is --auto");

                InspectableMux mux = new InspectableMux(Logging());
                Captain muxRefuse = CaptainRuntimeOptions.WithEffectiveAutoApprove(new Captain("m") { Runtime = AgentRuntimeEnum.Mux }, false);
                List<string> muxRefuseArgs = mux.Args(muxRefuse);
                int policy = muxRefuseArgs.IndexOf("--approval-policy");
                AssertTrue(policy >= 0 && muxRefuseArgs[policy + 1] == "deny", "Mux Refuse is --approval-policy deny: " + String.Join(" ", muxRefuseArgs));
                AssertTrue(mux.Args(new Captain("m2") { Runtime = AgentRuntimeEnum.Mux }).Contains("--yolo"), "Mux Bypass is --yolo");
            }));

            cases.Add(Case("stream_json_permission_denials_are_typed", "The result event's permission_denials parse into typed entries and mark the matching tool calls", () =>
            {
                string line = "{\"type\":\"result\",\"subtype\":\"success\",\"is_error\":false,\"result\":\"done\",\"permission_denials\":[{\"tool_name\":\"Bash\",\"tool_use_id\":\"toolu_1\",\"tool_input\":{\"command\":\"rm -rf build\"}}]}";
                AssertTrue(ClaudeStreamLine.TryParse(line, out ClaudeStreamLine? parsed), "parsed");
                AssertEqual(1, parsed!.PermissionDenials!.Count);
                ClaudePermissionDenial denial = parsed.PermissionDenials[0];
                AssertEqual("Bash", denial.ToolName);
                AssertEqual("toolu_1", denial.ToolUseId);
                AssertEqual("rm -rf build", JsonHelper.Deserialize<CliToolInput>(denial.ToolInput!).Command);

                ToolCallCollector collector = new ToolCallCollector();
                collector.Observe(new CaptainToolActivity { Phase = "started", Id = "toolu_1", Name = "Bash", Arguments = "{\"command\":\"rm -rf build\"}" });
                collector.Observe(new CaptainToolActivity { Phase = "completed", Id = "toolu_1", Ok = false, Result = "refused", ElapsedMs = 5 });
                collector.Observe(new CaptainToolActivity { Phase = "completed", Id = "toolu_1", Name = "Bash", Ok = false, PermissionDenied = true });
                collector.Observe(new CaptainToolActivity { Phase = "started", Id = "toolu_2", Name = "Read" });
                collector.Observe(new CaptainToolActivity { Phase = "completed", Id = "toolu_2", Ok = true, Result = "ok" });
                AskMessageToolCall refused = collector.ToList().Single(c => c.CallId == "toolu_1");
                AssertEqual(true, refused.PermissionDenied, "marked from the typed report");
                AssertEqual(false, refused.Ok);
                AssertEqual("refused", refused.ResultText, "the earlier result text is kept");
                AssertEqual(5L, refused.ElapsedMs, "timing kept");
                AssertNull(collector.ToList().Single(c => c.CallId == "toolu_2").PermissionDenied, "other calls unmarked");

                AssertTrue(ClaudeStreamLine.TryParse("{\"type\":\"result\",\"result\":\"x\"}", out ClaudeStreamLine? none), "parsed without denials");
                AssertNull(none!.PermissionDenials);
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "CLI permission policy and runtime flags", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static LoggingModule Logging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => { body(); return Task.CompletedTask; },
                tags: new List<string> { TestTags.Positive });
        }

        #endregion

        #region Nested-Types

        private sealed class InspectableClaude : ClaudeCodeRuntime
        {
            public InspectableClaude(LoggingModule logging) : base(logging) { }

            public List<string> Args(Captain captain) => BuildArguments(Path.GetTempPath(), "p", null, null, captain);

            public Dictionary<string, string?> Env(Captain captain)
            {
                System.Diagnostics.ProcessStartInfo info = new System.Diagnostics.ProcessStartInfo("x");
                info.Environment.Remove("MCP_TOOL_TIMEOUT");
                ApplyEnvironment(info, captain);
                return new Dictionary<string, string?>(info.Environment);
            }
        }

        private sealed class InspectableCodex : CodexRuntime
        {
            public InspectableCodex(LoggingModule logging) : base(logging) { }

            public List<string> Args(Captain captain) => BuildArguments(Path.GetTempPath(), "p", null, null, captain);
        }

        private sealed class InspectableGemini : GeminiRuntime
        {
            public InspectableGemini(LoggingModule logging) : base(logging) { }

            public List<string> Args(Captain captain) => BuildArguments(Path.GetTempPath(), "p", null, null, captain);
        }

        private sealed class InspectableCursor : CursorRuntime
        {
            public InspectableCursor(LoggingModule logging) : base(logging) { }

            public List<string> Args(Captain captain) => BuildArguments(Path.GetTempPath(), "p", null, null, captain);
        }

        private sealed class InspectableOpenCode : OpenCodeRuntime
        {
            public InspectableOpenCode(LoggingModule logging) : base(logging) { }

            public List<string> Args(Captain captain) => BuildArguments(Path.GetTempPath(), "p", null, null, captain);
        }

        private sealed class InspectableMux : MuxRuntime
        {
            public InspectableMux(LoggingModule logging) : base(logging) { }

            public List<string> Args(Captain captain) => BuildArguments(Path.GetTempPath(), "p", null, null, captain);
        }

        #endregion
    }
}

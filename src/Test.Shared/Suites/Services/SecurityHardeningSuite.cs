namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Authorization;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Runtimes;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Unit coverage for the W1 hardening helpers: per-captain auto-approve flags in every CLI runtime, managed-path
    /// delete guards, request-history secret key detection, credential redaction, the default-password check, and the
    /// loopback hostname test.
    /// </summary>
    public sealed class SecurityHardeningSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.SecurityHardening";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("auto_approve_defaults_to_true", "Auto-approve stays on unless a captain turns it off", TestTags.Positive, () =>
            {
                AssertTrue(CaptainRuntimeOptions.GetAutoApprove(null));
                AssertTrue(CaptainRuntimeOptions.GetAutoApprove(new Captain("c1")));
                AssertTrue(CaptainRuntimeOptions.GetAutoApprove(new Captain("c2") { RuntimeOptionsJson = "{\"autoApprove\":true}" }));
                AssertFalse(CaptainRuntimeOptions.GetAutoApprove(new Captain("c3") { RuntimeOptionsJson = "{\"autoApprove\":false}" }));
                AssertTrue(CaptainRuntimeOptions.GetAutoApprove(new Captain("c4") { RuntimeOptionsJson = "not json" }));
            }));

            cases.Add(Case("with_auto_approve_preserves_other_keys", "Setting autoApprove keeps runtime-specific options", TestTags.Positive, () =>
            {
                string? json = CaptainRuntimeOptions.WithAutoApprove("{\"endpoint\":\"local\",\"approvalPolicy\":\"ask\"}", false);
                AssertNotNull(json);
                AssertContains("\"endpoint\":\"local\"", json!);
                AssertContains("\"autoApprove\":false", json);
                AssertEqual(false, CaptainRuntimeOptions.GetExplicitAutoApprove(json));
                AssertNull(CaptainRuntimeOptions.WithAutoApprove("{\"autoApprove\":false}", null));
            }));

            cases.Add(Case("claude_code_without_auto_approve", "Claude Code drops --dangerously-skip-permissions when auto-approve is off", TestTags.Positive, () =>
            {
                InspectableClaude runtime = new InspectableClaude(Logging());
                List<string> on = runtime.Args(new Captain("on"));
                List<string> off = runtime.Args(Off());
                AssertTrue(on.Contains("--dangerously-skip-permissions"));
                AssertFalse(off.Contains("--dangerously-skip-permissions"));
                AssertTrue(off.Contains("--permission-mode") && off.Contains("acceptEdits"));
            }));

            cases.Add(Case("codex_without_auto_approve", "Codex drops --full-auto and the bypass flag when auto-approve is off", TestTags.Positive, () =>
            {
                InspectableCodex runtime = new InspectableCodex(Logging());
                List<string> off = runtime.Args(Off());
                AssertFalse(off.Contains("--full-auto"));
                AssertFalse(off.Contains("--dangerously-bypass-approvals-and-sandbox"));
                AssertTrue(off.Contains("--sandbox") && off.Contains("workspace-write"));
                List<string> on = runtime.Args(new Captain("on"));
                AssertTrue(on.Contains("--full-auto") || on.Contains("--dangerously-bypass-approvals-and-sandbox"));
            }));

            cases.Add(Case("gemini_without_auto_approve", "Gemini uses auto_edit instead of yolo when auto-approve is off", TestTags.Positive, () =>
            {
                InspectableGemini runtime = new InspectableGemini(Logging());
                AssertTrue(runtime.Args(new Captain("on")).Contains("yolo"));
                List<string> off = runtime.Args(Off());
                AssertFalse(off.Contains("yolo"));
                AssertTrue(off.Contains("auto_edit"));
            }));

            cases.Add(Case("cursor_without_auto_approve", "Cursor drops --force when auto-approve is off", TestTags.Positive, () =>
            {
                InspectableCursor runtime = new InspectableCursor(Logging());
                AssertTrue(runtime.Args(new Captain("on")).Contains("--force"));
                AssertFalse(runtime.Args(Off()).Contains("--force"));
            }));

            cases.Add(Case("opencode_without_auto_approve", "OpenCode drops --auto when auto-approve is off", TestTags.Positive, () =>
            {
                InspectableOpenCode runtime = new InspectableOpenCode(Logging());
                AssertTrue(runtime.Args(new Captain("on")).Contains("--auto"));
                AssertFalse(runtime.Args(Off()).Contains("--auto"));
            }));

            cases.Add(Case("mux_without_auto_approve", "Mux uses --approval-policy deny when auto-approve is off and no policy is set", TestTags.Positive, () =>
            {
                List<string> on = MuxCommandBuilder.BuildPrintArguments(Path.GetTempPath(), "p", null, null, new MuxCaptainOptions { Endpoint = "local" }, null, false);
                AssertTrue(on.Contains("--yolo"));
                List<string> off = MuxCommandBuilder.BuildPrintArguments(Path.GetTempPath(), "p", null, null, new MuxCaptainOptions { Endpoint = "local", AutoApprove = false }, null, false);
                AssertFalse(off.Contains("--yolo"));
                AssertTrue(off.Contains("--approval-policy") && off.Contains("deny"));
                List<string> explicitPolicy = MuxCommandBuilder.BuildPrintArguments(Path.GetTempPath(), "p", null, null, new MuxCaptainOptions { Endpoint = "local", AutoApprove = false, ApprovalPolicy = "auto" }, null, false);
                AssertTrue(explicitPolicy.Contains("--yolo"), "an explicit Mux policy wins");
            }));

            cases.Add(Case("managed_paths_guard", "Only paths strictly inside the managed root are deletable", TestTags.Negative, () =>
            {
                string root = Path.Combine(Path.GetTempPath(), "armada-managed-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path.Combine(root, "repo.git"));
                try
                {
                    AssertTrue(ManagedPaths.IsStrictlyUnder(Path.Combine(root, "repo.git"), root));
                    AssertFalse(ManagedPaths.IsStrictlyUnder(root, root), "the root itself");
                    AssertFalse(ManagedPaths.IsStrictlyUnder(Path.Combine(root, "..", "elsewhere"), root), "dot-dot escape");
                    AssertFalse(ManagedPaths.IsStrictlyUnder(Path.GetTempPath(), root), "parent");
                    AssertFalse(ManagedPaths.IsStrictlyUnder(root + "-sibling", root), "prefix sibling");
                    AssertFalse(ManagedPaths.IsStrictlyUnder(null, root));
                    AssertFalse(ManagedPaths.IsStrictlyUnder(Path.Combine(root, "x"), null));
                }
                finally
                {
                    Directory.Delete(root, true);
                }
            }));

            cases.Add(Case("sensitive_key_detection", "Secret-bearing names are detected without catching token counts", TestTags.Positive, () =>
            {
                foreach (string key in new string[] { "password", "Password", "enrollmentToken", "sessionTokenEncryptionKey", "passwordProofSha256", "GITHUB_TOKEN", "ANTHROPIC_API_KEY", "x-access-key", "clientSecret", "bearerToken", "ownerToken" })
                    AssertTrue(RequestHistoryCaptureService.IsSensitiveKey(key), key);
                foreach (string key in new string[] { "inputTokens", "outputTokens", "maxTokens", "name", "tokenCount", "description" })
                    AssertFalse(RequestHistoryCaptureService.IsSensitiveKey(key), key);
            }));

            cases.Add(Case("credential_redaction", "Credential reads mask the bearer token", TestTags.Positive, () =>
            {
                Credential credential = new Credential("default", "default");
                string token = credential.BearerToken;
                Credential redacted = Credential.Redact(credential);
                AssertFalse(redacted.BearerToken.Contains(token.Substring(0, 8)));
                AssertTrue(redacted.BearerToken.EndsWith(token.Substring(token.Length - 4)));
                AssertEqual(credential.Id, redacted.Id);
            }));

            cases.Add(Case("default_password_detection", "Only admin@armada with the default password counts as default", TestTags.Positive, () =>
            {
                AssertTrue(new UserMaster("default", "admin@armada", "password").UsesDefaultPassword());
                AssertFalse(new UserMaster("default", "admin@armada", "something-else").UsesDefaultPassword());
                AssertFalse(new UserMaster("default", "someone@example.com", "password").UsesDefaultPassword());
            }));

            cases.Add(Case("loopback_hostnames", "Loopback hostname detection", TestTags.Positive, () =>
            {
                foreach (string host in new string[] { "localhost", "127.0.0.1", "127.0.0.2", "::1", "[::1]", "LOCALHOST" })
                    AssertTrue(DefaultCredentialService.IsLoopbackHostname(host), host);
                foreach (string host in new string[] { "*", "+", "0.0.0.0", "192.168.1.10", "armada.example.com", "", "::" })
                    AssertFalse(DefaultCredentialService.IsLoopbackHostname(host), host);
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Security Hardening",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static LoggingModule Logging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private static Captain Off()
        {
            return new Captain("off") { RuntimeOptionsJson = "{\"autoApprove\":false}" };
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => { body(); return Task.CompletedTask; },
                tags: new List<string> { tag });
        }

        #endregion

        #region Private-Types

        private sealed class InspectableClaude : ClaudeCodeRuntime
        {
            public InspectableClaude(LoggingModule logging) : base(logging) { }
            public List<string> Args(Captain captain) => BuildArguments(Path.GetTempPath(), "p", null, null, captain);
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

        #endregion
    }
}

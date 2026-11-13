namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.IO.Compression;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.BlackBox;
    using Armada.Core.BlackBox.Storage;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// BlackBoxRedactor: every built-in rule, registered secrets, rule counts without values, record walking, and text that must not change.
    /// </summary>
    public sealed class BlackBoxRedactorSuite : IArmadaTestSuite
    {
        private const string SuiteId = "Services.BlackBoxRedactor";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("rules_redact_and_report", "Each secret pattern is replaced and reported by rule name and count, never by value", TestTags.Positive, () =>
            {
                BlackBoxRedactor redactor = new BlackBoxRedactor();
                Dictionary<string, string> samples = new Dictionary<string, string>
                {
                    { "PrivateKeyBlock", "-----BEGIN RSA PRIVATE KEY-----\nMIIEowIBAAKCAQEA\n-----END RSA PRIVATE KEY-----" },
                    { "AuthorizationHeader", "Authorization: Bearer abc.def.ghi123" },
                    { "UrlCredentials", "https://bob:hunter2@github.com/x/y.git" },
                    { "GitHubToken", "ghp_ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789" },
                    { "AnthropicKey", "sk-ant-api03-ABCDEFGHIJKLMNOPQRST" },
                    { "OpenAiKey", "sk-proj-ABCDEFGHIJKLMNOPQRSTUVWX" },
                    { "AwsAccessKeyId", "AKIAABCDEFGHIJKLMNOP" },
                    { "AwsSecretAccessKey", "aws_secret_access_key = wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY" },
                    { "AzureKey", "DefaultEndpointsProtocol=https;AccountName=a;AccountKey=abcdefghijklmnopqrstuvwxyz0123456789==" },
                    { "GoogleApiKey", "AIzaSyA1234567890abcdefghijklmnopqrstuv" },
                    { "SlackToken", "xoxb-1234567890-abcdefghij" },
                    { "JsonWebToken", "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.dozjgNryP4J3jVmNHl0w5N_XgL0n3I9PlFUP0THsR8U" },
                    { "SecretPair", "password=correct-horse-battery" }
                };
                foreach (KeyValuePair<string, string> kv in samples)
                {
                    BlackBoxRedaction report = new BlackBoxRedaction();
                    string text = "before " + kv.Value + " after";
                    string redacted = redactor.Redact(text, report)!;
                    AssertContains(BlackBoxRedactor.Marker, redacted, kv.Key + " marker");
                    AssertContains("before ", redacted, kv.Key + " keeps context");
                    AssertTrue(report.RulesApplied.Any(r => r.Rule == kv.Key), kv.Key + " reported, got " + String.Join(",", report.RulesApplied.Select(r => r.Rule)));
                    AssertTrue(report.RedactedCount >= 1, kv.Key + " counted");
                    string json = JsonSerializer.Serialize(report);
                    foreach (string secretPart in new[] { "hunter2", "correct-horse", "MIIEowIBAAKCAQEA", "ABCDEFGHIJKLMNOPQRST" })
                        AssertFalse(json.Contains(secretPart, StringComparison.Ordinal), kv.Key + " report carries no values");
                }
                AssertEqual("Authorization: Bearer [REDACTED]", redactor.Redact("Authorization: Bearer abc.def.ghi123"), "keeps the header name");
                AssertEqual("https://[REDACTED]@github.com/x/y.git", redactor.Redact("https://bob:hunter2@github.com/x/y.git"), "keeps the host");
                return Task.CompletedTask;
            }));

            cases.Add(Case("known_secrets_and_plain_text", "Registered secret values are redacted anywhere; ordinary text, ids and SHAs are untouched; null stays null", TestTags.Negative, () =>
            {
                BlackBoxRedactor redactor = new BlackBoxRedactor();
                redactor.RegisterSecrets(new[] { "my-endpoint-key-42", "short", null });
                BlackBoxRedaction report = new BlackBoxRedaction();
                AssertEqual("key [REDACTED] and [REDACTED]", redactor.Redact("key my-endpoint-key-42 and my-endpoint-key-42", report));
                AssertEqual(2, report.RulesApplied.Single(r => r.Rule == BlackBoxRedactor.KnownSecretRule).Count, "counted twice");
                string plain = "Mission msn_mvsqzal6_b1BfR4tnZ29 failed at commit 1a2b3c4d5e6f on src/Armada.Core/Services/MissionService.cs:1582 (exit 1).";
                AssertEqual(plain, redactor.Redact(plain), "plain text unchanged");
                AssertEqual("short and sweet", redactor.Redact("short and sweet"), "values under 6 characters are not registered");
                AssertNull(redactor.Redact(null), "null");
                AssertEqual("", redactor.Redact(""), "empty");
                return Task.CompletedTask;
            }));

            cases.Add(Case("record_walk", "RedactRecord reaches nested strings, lists and dictionaries and fills the record's Redaction section", TestTags.Positive, () =>
            {
                BlackBoxRedactor redactor = new BlackBoxRedactor();
                BlackBoxRecord record = new BlackBoxRecord();
                record.Classification = new BlackBoxClassification { RawReason = "push failed: https://u:p4ssw0rd@host/repo" };
                record.Mechanism = new BlackBoxMechanism { CommandOutputTail = "token=abcdef123456" };
                record.Mechanism.StackFrames.Add("Armada.Core.X() password=topsecretvalue");
                record.Analysis = new BlackBoxAnalysis();
                BlackBoxRecoveryPath path = new BlackBoxRecoveryPath { Id = "rp1", Title = "t", Action = new BlackBoxAction { Tool = "x" } };
                path.Action.Arguments["header"] = "Authorization: Basic dXNlcjpwYXNz";
                record.Analysis.RecoveryPaths.Add(path);
                redactor.RedactRecord(record);
                string json = Encoding.UTF8.GetString(BlackBoxJson.SerializeRecord(record));
                foreach (string secret in new[] { "p4ssw0rd", "abcdef123456", "topsecretvalue", "dXNlcjpwYXNz" }) AssertFalse(json.Contains(secret, StringComparison.Ordinal), "no " + secret);
                AssertTrue(record.Redaction.RedactedCount >= 4, "counted: " + record.Redaction.RedactedCount);
                return Task.CompletedTask;
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Black box redactor", cases: cases);
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(suiteId: SuiteId, caseId: caseId, displayName: displayName, executeAsync: (CancellationToken ct) => body(), tags: new List<string> { tag });
        }
    }
}

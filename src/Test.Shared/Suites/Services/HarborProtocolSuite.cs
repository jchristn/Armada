namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Harbor;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for the Harbor wire protocol: every message type must round-trip through
    /// <see cref="HarborProtocol"/> preserving its concrete type and fields, and malformed payloads must be
    /// rejected. The protocol is the contract between the Admiral and the Harbor, so both the type
    /// discriminator and the field values are asserted.
    /// </summary>
    public sealed class HarborProtocolSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Harbor protocol suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("handshake_roundtrips", "Handshake round-trips with capabilities", TestTags.Positive, () =>
            {
                HarborHandshake message = new HarborHandshake
                {
                    CorrelationId = "corr-1",
                    TraceParent = "00-abc-def-01",
                    HarborId = "hbr_test",
                    Name = "Workstation",
                    ProtocolVersion = HarborProtocol.Version,
                    OsPlatform = "Windows",
                    Architecture = "X64",
                    MaxConcurrentJobs = 8,
                    Capabilities = new List<HarborCapability> { new HarborCapability { Name = "claude", Available = true } }
                };

                HarborMessage decoded = RoundTrip(message);
                HarborHandshake typed = AssertType<HarborHandshake>(decoded);
                AssertEqual("hbr_test", typed.HarborId);
                AssertEqual("corr-1", typed.CorrelationId);
                AssertEqual("00-abc-def-01", typed.TraceParent);
                AssertEqual(8, typed.MaxConcurrentJobs);
                AssertEqual(1, typed.Capabilities.Count);
                AssertEqual("claude", typed.Capabilities[0].Name);
            }));

            cases.Add(Case("handshake_ack_roundtrips", "HandshakeAck round-trips", TestTags.Positive, () =>
            {
                HarborHandshakeAck message = new HarborHandshakeAck { Accepted = true, McpBaseUrl = "http://127.0.0.1:7891/mcp" };
                HarborHandshakeAck typed = AssertType<HarborHandshakeAck>(RoundTrip(message));
                AssertTrue(typed.Accepted, "Expected Accepted to survive.");
                AssertEqual("http://127.0.0.1:7891/mcp", typed.McpBaseUrl);
            }));

            cases.Add(Case("launch_roundtrips", "Launch round-trips with args and environment", TestTags.Positive, () =>
            {
                HarborLaunchRequest message = new HarborLaunchRequest
                {
                    JobId = "job-1",
                    Runtime = "claude",
                    WorkingDirectory = "/docks/v/msn",
                    Model = "claude-opus-4-8",
                    Prompt = "do the thing",
                    PromptViaStdin = true,
                    Arguments = new List<string> { "--print", "--verbose" },
                    Environment = new Dictionary<string, string> { { "FOO", "bar" } }
                };

                HarborLaunchRequest typed = AssertType<HarborLaunchRequest>(RoundTrip(message));
                AssertEqual("job-1", typed.JobId);
                AssertEqual("claude", typed.Runtime);
                AssertEqual(2, typed.Arguments.Count);
                AssertEqual("bar", typed.Environment["FOO"]);
                AssertTrue(typed.PromptViaStdin, "Expected PromptViaStdin to survive.");
            }));

            cases.Add(Case("stdin_kill_git_roundtrip", "Stdin, Kill, and Git requests round-trip", TestTags.Positive, () =>
            {
                HarborStdinRequest stdin = AssertType<HarborStdinRequest>(RoundTrip(new HarborStdinRequest { JobId = "j", Data = "hello" }));
                AssertEqual("hello", stdin.Data);

                HarborKillRequest kill = AssertType<HarborKillRequest>(RoundTrip(new HarborKillRequest { JobId = "j", GracefulTimeoutMs = 5000 }));
                AssertEqual(5000, kill.GracefulTimeoutMs);

                HarborGitRequest git = AssertType<HarborGitRequest>(RoundTrip(new HarborGitRequest
                {
                    RequestId = "r1",
                    Executable = "git",
                    WorkingDirectory = "/repo",
                    Arguments = new List<string> { "worktree", "add", "path" }
                }));
                AssertEqual(3, git.Arguments.Count);
                AssertEqual("git", git.Executable);
            }));

            cases.Add(Case("events_roundtrip", "Started, Output, Exited, GitResult, Heartbeat, Error round-trip", TestTags.Positive, () =>
            {
                HarborStarted started = AssertType<HarborStarted>(RoundTrip(new HarborStarted { JobId = "j", ProcessId = 4242 }));
                AssertEqual(4242, started.ProcessId);

                HarborOutput output = AssertType<HarborOutput>(RoundTrip(new HarborOutput { JobId = "j", Stream = HarborOutputStreamEnum.Stderr, Data = "warn" }));
                AssertEqual(HarborOutputStreamEnum.Stderr, output.Stream);
                AssertEqual("warn", output.Data);

                HarborExited exited = AssertType<HarborExited>(RoundTrip(new HarborExited { JobId = "j", ExitCode = 1 }));
                AssertEqual(1, exited.ExitCode);

                HarborGitResult gitResult = AssertType<HarborGitResult>(RoundTrip(new HarborGitResult { RequestId = "r1", ExitCode = 0, StandardOutput = "ok" }));
                AssertEqual("ok", gitResult.StandardOutput);

                HarborHeartbeat heartbeat = AssertType<HarborHeartbeat>(RoundTrip(new HarborHeartbeat { LiveJobIds = new List<string> { "a", "b" } }));
                AssertEqual(2, heartbeat.LiveJobIds.Count);

                HarborError error = AssertType<HarborError>(RoundTrip(new HarborError { JobId = "j", Message = "boom" }));
                AssertEqual("boom", error.Message);
            }));

            cases.Add(Case("null_message_serialize_throws", "Serialize null throws", TestTags.Negative, () =>
            {
                AssertThrows<ArgumentNullException>(() => HarborProtocol.Serialize(null!));
            }));

            cases.Add(Case("empty_payload_deserialize_throws", "Deserialize empty throws", TestTags.Negative, () =>
            {
                AssertThrows<ArgumentNullException>(() => HarborProtocol.Deserialize("   "));
            }));

            cases.Add(Case("malformed_payload_deserialize_throws", "Deserialize malformed throws FormatException", TestTags.Negative, () =>
            {
                AssertThrows<FormatException>(() => HarborProtocol.Deserialize("{ not valid json"));
            }));

            cases.Add(Case("unknown_type_deserialize_throws", "Deserialize unknown discriminator throws", TestTags.Negative, () =>
            {
                AssertThrows<FormatException>(() => HarborProtocol.Deserialize("{\"type\":\"bogus\",\"correlationId\":\"x\"}"));
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.HarborProtocol",
                displayName: "Harbor Protocol",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static HarborMessage RoundTrip(HarborMessage message)
        {
            string json = HarborProtocol.Serialize(message);
            return HarborProtocol.Deserialize(json);
        }

        private static TMessage AssertType<TMessage>(HarborMessage message) where TMessage : HarborMessage
        {
            if (message is TMessage typed) return typed;
            throw new InvalidOperationException("Expected message of type " + typeof(TMessage).Name + " but got " + message.GetType().Name + ".");
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.HarborProtocol",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) =>
                {
                    body();
                    return Task.CompletedTask;
                },
                tags: new List<string> { tag });
        }

        #endregion
    }
}

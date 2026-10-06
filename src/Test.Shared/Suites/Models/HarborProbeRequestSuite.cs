namespace Test.Shared.Suites.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// <see cref="HarborProbeRequest.ToHostCommandRequest"/>: the command a Harbor probe runs and audits. A request body
    /// can set fields to JSON null, which deserializes to null despite the non-nullable declarations; the probe route
    /// used to pass a null argument list straight through (the CS8601 warning at that assignment).
    /// </summary>
    public sealed class HarborProbeRequestSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Models.HarborProbeRequest";

        private static readonly JsonSerializerOptions _RouteJsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("defaults_run_git_version", "An empty probe body runs git --version in the Harbor's default directory", TestTags.Positive, () =>
            {
                HostCommandRequest command = Parse("{}").ToHostCommandRequest();
                AssertEqual("git", command.Executable);
                AssertEqual("--version", String.Join(" ", command.Arguments));
                AssertEqual(String.Empty, command.WorkingDirectory);
                AssertEqual(15000, command.TimeoutMs);
            }));

            cases.Add(Case("json_nulls_mean_defaults", "JSON null for executable, arguments, or working directory means the default, never a null command field", TestTags.Negative, () =>
            {
                HarborProbeRequest probe = Parse("{\"executable\":null,\"arguments\":null,\"workingDirectory\":null}");
                AssertNull(probe.Arguments, "the body really does deserialize a null argument list");

                HostCommandRequest command = probe.ToHostCommandRequest();
                AssertEqual("git", command.Executable);
                AssertNotNull(command.Arguments, "arguments are never null");
                AssertEqual("--version", String.Join(" ", command.Arguments));
                AssertNotNull(command.WorkingDirectory, "working directory is never null");
                AssertEqual(String.Empty, command.WorkingDirectory);
            }));

            cases.Add(Case("explicit_command_is_kept", "An explicit executable, arguments, working directory, and timeout are passed through; the argument list is copied", TestTags.Positive, () =>
            {
                HarborProbeRequest probe = Parse("{\"executable\":\" gh \",\"arguments\":[\"auth\",\"status\"],\"workingDirectory\":\"/repo\",\"timeoutMs\":5000}");
                HostCommandRequest command = probe.ToHostCommandRequest();
                AssertEqual("gh", command.Executable);
                AssertEqual("auth status", String.Join(" ", command.Arguments));
                AssertEqual("/repo", command.WorkingDirectory);
                AssertEqual(5000, command.TimeoutMs);

                command.Arguments.Add("--extra");
                AssertEqual(2, probe.Arguments.Count, "the command owns its argument list");

                HostCommandRequest noArgs = Parse("{\"executable\":\"hostname\",\"arguments\":[]}").ToHostCommandRequest();
                AssertEqual(0, noArgs.Arguments.Count, "an explicit empty list stays empty");
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Harbor Probe Request",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static HarborProbeRequest Parse(string json)
        {
            HarborProbeRequest? probe = JsonSerializer.Deserialize<HarborProbeRequest>(json, _RouteJsonOptions);
            AssertNotNull(probe, "probe body parsed");
            return probe!;
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
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

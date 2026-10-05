namespace Test.Shared.Suites.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="Vessel"/>: identity and defaults, name validation, project
    /// context/style guide fields, JSON round-tripping, and the write-only GitHub token override
    /// behavior. Ported from the retired unit suite plus added Id/Name-validation negatives.
    /// </summary>
    public sealed class VesselModelSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Vessel model suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("default_constructor_generates_id_with_prefix", "Vessel default constructor generates id with prefix", TestTags.Positive, () =>
            {
                Vessel vessel = new Vessel();
                AssertStartsWith(Constants.VesselIdPrefix, vessel.Id);
            }));

            cases.Add(Case("name_repo_constructor_sets_properties", "Vessel name repo constructor sets properties", TestTags.Positive, () =>
            {
                Vessel vessel = new Vessel("MyRepo", "https://github.com/user/repo");
                AssertEqual("MyRepo", vessel.Name);
                AssertEqual("https://github.com/user/repo", vessel.RepoUrl);
            }));

            cases.Add(Case("default_values_are_correct", "Vessel default values are correct", TestTags.Positive, () =>
            {
                Vessel vessel = new Vessel();
                AssertEqual("My Vessel", vessel.Name);
                AssertEqual("main", vessel.DefaultBranch);
                AssertTrue(vessel.Active);
                AssertNull(vessel.FleetId);
                AssertNull(vessel.LocalPath);
            }));

            cases.Add(Case("set_name_null_throws", "Vessel set name null throws", TestTags.Negative, () =>
            {
                Vessel vessel = new Vessel();
                AssertThrows<ArgumentNullException>(() => vessel.Name = null!);
            }));

            // Added audit coverage: Name also rejects empty, and Id rejects null/empty, never exercised by the legacy suite.
            cases.Add(Case("set_name_empty_throws", "Vessel set name empty throws", TestTags.Negative, () =>
            {
                Vessel vessel = new Vessel();
                AssertThrows<ArgumentNullException>(() => vessel.Name = "");
            }));

            cases.Add(Case("set_id_null_throws", "Vessel set id null throws", TestTags.Negative, () =>
            {
                Vessel vessel = new Vessel();
                AssertThrows<ArgumentNullException>(() => vessel.Id = null!);
            }));

            cases.Add(Case("set_id_empty_throws", "Vessel set id empty throws", TestTags.Negative, () =>
            {
                Vessel vessel = new Vessel();
                AssertThrows<ArgumentNullException>(() => vessel.Id = "");
            }));

            cases.Add(Case("set_repo_url_nullable", "Vessel set repo url nullable", TestTags.Positive, () =>
            {
                Vessel vessel = new Vessel();
                vessel.RepoUrl = "";
                AssertEqual("", vessel.RepoUrl);
                vessel.RepoUrl = null;
                AssertNull(vessel.RepoUrl);
            }));

            cases.Add(Case("serialization_round_trip", "Vessel serialization round trip", TestTags.Positive, () =>
            {
                Vessel vessel = new Vessel("TestVessel", "https://github.com/test/repo");
                vessel.FleetId = "flt_test";
                vessel.DefaultBranch = "develop";

                string json = JsonSerializer.Serialize(vessel);
                Vessel deserialized = JsonSerializer.Deserialize<Vessel>(json)!;

                AssertEqual(vessel.Id, deserialized.Id);
                AssertEqual(vessel.Name, deserialized.Name);
                AssertEqual(vessel.RepoUrl, deserialized.RepoUrl);
                AssertEqual(vessel.FleetId, deserialized.FleetId);
                AssertEqual(vessel.DefaultBranch, deserialized.DefaultBranch);
            }));

            cases.Add(Case("unique_ids_across_instances", "Vessel unique ids across instances", TestTags.Positive, () =>
            {
                Vessel v1 = new Vessel();
                Vessel v2 = new Vessel();
                AssertNotEqual(v1.Id, v2.Id);
            }));

            cases.Add(Case("project_context_defaults_to_null", "Vessel project context defaults to null", TestTags.Positive, () =>
            {
                Vessel vessel = new Vessel();
                AssertNull(vessel.ProjectContext);
            }));

            cases.Add(Case("style_guide_defaults_to_null", "Vessel style guide defaults to null", TestTags.Positive, () =>
            {
                Vessel vessel = new Vessel();
                AssertNull(vessel.StyleGuide);
            }));

            cases.Add(Case("project_context_set_and_get", "Vessel project context set and get", TestTags.Positive, () =>
            {
                Vessel vessel = new Vessel();
                vessel.ProjectContext = "A .NET 8 microservice with Redis caching.";
                AssertEqual("A .NET 8 microservice with Redis caching.", vessel.ProjectContext);
            }));

            cases.Add(Case("style_guide_set_and_get", "Vessel style guide set and get", TestTags.Positive, () =>
            {
                Vessel vessel = new Vessel();
                vessel.StyleGuide = "Use async/await everywhere. No blocking calls.";
                AssertEqual("Use async/await everywhere. No blocking calls.", vessel.StyleGuide);
            }));

            cases.Add(Case("project_context_nullable", "Vessel project context nullable", TestTags.Positive, () =>
            {
                Vessel vessel = new Vessel();
                vessel.ProjectContext = "Some context";
                vessel.ProjectContext = null;
                AssertNull(vessel.ProjectContext);
            }));

            cases.Add(Case("style_guide_nullable", "Vessel style guide nullable", TestTags.Positive, () =>
            {
                Vessel vessel = new Vessel();
                vessel.StyleGuide = "Some style";
                vessel.StyleGuide = null;
                AssertNull(vessel.StyleGuide);
            }));

            cases.Add(Case("serialization_round_trip_with_project_context_and_style_guide", "Vessel serialization round trip with project context and style guide", TestTags.Positive, () =>
            {
                Vessel vessel = new Vessel("ContextVessel", "https://github.com/test/repo");
                vessel.ProjectContext = "Multi-line\nproject context\nwith details.";
                vessel.StyleGuide = "Follow C# coding conventions.\nUse explicit local types when type is obvious.";

                string json = JsonSerializer.Serialize(vessel);
                Vessel deserialized = JsonSerializer.Deserialize<Vessel>(json)!;

                AssertEqual(vessel.Id, deserialized.Id);
                AssertEqual(vessel.Name, deserialized.Name);
                AssertEqual(vessel.ProjectContext, deserialized.ProjectContext);
                AssertEqual(vessel.StyleGuide, deserialized.StyleGuide);
            }));

            cases.Add(Case("serialization_round_trip_with_null_project_context_and_style_guide", "Vessel serialization round trip with null project context and style guide", TestTags.Positive, () =>
            {
                Vessel vessel = new Vessel("NullContextVessel", "https://github.com/test/repo");

                string json = JsonSerializer.Serialize(vessel);
                Vessel deserialized = JsonSerializer.Deserialize<Vessel>(json)!;

                AssertNull(deserialized.ProjectContext);
                AssertNull(deserialized.StyleGuide);
            }));

            cases.Add(Case("serialization_json_contains_project_context_and_style_guide", "Vessel serialization json contains project context and style guide", TestTags.Positive, () =>
            {
                Vessel vessel = new Vessel("JsonFieldVessel", "https://github.com/test/repo");
                vessel.ProjectContext = "test context";
                vessel.StyleGuide = "test style";

                string json = JsonSerializer.Serialize(vessel);
                List<JsonPropertyShape> top = JsonShape.TopLevel(json);
                AssertEqual("test context", top.Find(p => p.Name == "ProjectContext")?.ScalarText, "ProjectContext on the wire");
                AssertEqual("test style", top.Find(p => p.Name == "StyleGuide")?.ScalarText, "StyleGuide on the wire");
            }));

            cases.Add(Case("github_token_override_deserialize_sets_hidden_property", "Vessel GitHub token override deserialize sets hidden property", TestTags.Positive, () =>
            {
                string json = "{\"Name\":\"Token Vessel\",\"RepoUrl\":\"https://github.com/test/repo\",\"gitHubTokenOverride\":\"  ghp_test_token  \"}";
                Vessel vessel = JsonSerializer.Deserialize<Vessel>(json)!;

                AssertTrue(vessel.GitHubTokenOverrideSpecified);
                AssertEqual("  ghp_test_token  ", vessel.GitHubTokenOverride);
                AssertTrue(vessel.HasGitHubTokenOverride);
            }));

            cases.Add(Case("github_token_override_serialize_does_not_leak_token", "Vessel GitHub token override serialize does not leak token", TestTags.Positive, () =>
            {
                Vessel vessel = new Vessel("Token Vessel", "https://github.com/test/repo");
                vessel.GitHubTokenOverride = "ghp_hidden";

                string json = JsonSerializer.Serialize(vessel);
                AssertFalse(json.Contains("ghp_hidden", StringComparison.Ordinal), "the token value is never serialized");
                AssertFalse(JsonShape.HasPropertyAnywhere(json, "GitHubTokenOverride"), "no GitHubTokenOverride property in any casing");
                AssertEqual(System.Text.Json.JsonTokenType.True, JsonShape.TopLevelProperty(json, "HasGitHubTokenOverride")?.ValueToken, "HasGitHubTokenOverride is true");
            }));

            cases.Add(Case("has_github_token_override_deserializes_from_read_model", "Vessel HasGitHubTokenOverride deserializes from read model", TestTags.Positive, () =>
            {
                string json = "{\"Name\":\"Token Vessel\",\"RepoUrl\":\"https://github.com/test/repo\",\"HasGitHubTokenOverride\":true}";
                Vessel vessel = JsonSerializer.Deserialize<Vessel>(json)!;

                AssertNull(vessel.GitHubTokenOverride);
                AssertTrue(vessel.HasGitHubTokenOverride);
            }));

            cases.Add(Case("normalize_github_token_override_trims_and_clears_blank", "Vessel NormalizeGitHubTokenOverride trims and clears blank", TestTags.Positive, () =>
            {
                Vessel vessel = new Vessel("Normalize Vessel", "https://github.com/test/repo");
                vessel.GitHubTokenOverride = "  ghp_trim_me  ";
                vessel.NormalizeGitHubTokenOverride();
                AssertEqual("ghp_trim_me", vessel.GitHubTokenOverride);

                vessel.GitHubTokenOverride = "   ";
                vessel.NormalizeGitHubTokenOverride();
                AssertNull(vessel.GitHubTokenOverride);
                AssertFalse(vessel.HasGitHubTokenOverride);
            }));

            return new TestSuiteDescriptor(
                suiteId: "Models.VesselModel",
                displayName: "Vessel Model",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: "Models.VesselModel",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) =>
                {
                    body();
                    return Task.CompletedTask;
                },
                tags: new List<string> { tag });
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: "Models.VesselModel",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion
    }
}

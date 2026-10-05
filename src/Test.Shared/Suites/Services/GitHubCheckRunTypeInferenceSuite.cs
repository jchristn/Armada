namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="GitHubIntegrationService.InferCheckRunType"/>. Positive cases confirm whole-word
    /// workflow names map to their check-run types; negative cases confirm substrings of longer words do not.
    /// </summary>
    public sealed class GitHubCheckRunTypeInferenceSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("whole_words_map_to_types", "Whole workflow-name words map to check-run types", TestTags.Positive, () =>
            {
                AssertEqual(CheckRunTypeEnum.RollbackVerification, GitHubIntegrationService.InferCheckRunType("Rollback Verify"));
                AssertEqual(CheckRunTypeEnum.Rollback, GitHubIntegrationService.InferCheckRunType("prod-rollback"));
                AssertEqual(CheckRunTypeEnum.DeploymentVerification, GitHubIntegrationService.InferCheckRunType("Deploy Verify"));
                AssertEqual(CheckRunTypeEnum.SmokeTest, GitHubIntegrationService.InferCheckRunType("smoke_tests"));
                AssertEqual(CheckRunTypeEnum.Deploy, GitHubIntegrationService.InferCheckRunType("Deployment (staging)"));
                AssertEqual(CheckRunTypeEnum.IntegrationTest, GitHubIntegrationService.InferCheckRunType("integration"));
                AssertEqual(CheckRunTypeEnum.E2ETest, GitHubIntegrationService.InferCheckRunType("End-to-End"));
                AssertEqual(CheckRunTypeEnum.UnitTest, GitHubIntegrationService.InferCheckRunType("Unit Tests"));
                AssertEqual(CheckRunTypeEnum.SecurityScan, GitHubIntegrationService.InferCheckRunType("CodeQL"));
                AssertEqual(CheckRunTypeEnum.Performance, GitHubIntegrationService.InferCheckRunType("Nightly load benchmark"));
                AssertEqual(CheckRunTypeEnum.Migration, GitHubIntegrationService.InferCheckRunType("db migrations"));
            }));

            cases.Add(Case("substrings_do_not_count", "Substrings of longer words do not decide the type", TestTags.Negative, () =>
            {
                // The old substring check classified these from fragments: "upload"/"download" (load),
                // "community" (unit), "latest" (test), "unhealthy-alerts"... fragments are not words.
                AssertEqual(CheckRunTypeEnum.Build, GitHubIntegrationService.InferCheckRunType("Upload artifacts"));
                AssertEqual(CheckRunTypeEnum.Build, GitHubIntegrationService.InferCheckRunType("Community docs"));
                AssertEqual(CheckRunTypeEnum.Build, GitHubIntegrationService.InferCheckRunType("Build latest"));
                AssertEqual(CheckRunTypeEnum.Build, GitHubIntegrationService.InferCheckRunType("Redeployer lint"));
                AssertEqual(CheckRunTypeEnum.Build, GitHubIntegrationService.InferCheckRunType(null));
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.GitHubCheckRunTypeInference",
                displayName: "GitHub Check Run Type Inference",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.GitHubCheckRunTypeInference",
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

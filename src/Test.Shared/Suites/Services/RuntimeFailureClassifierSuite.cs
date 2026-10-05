namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="RuntimeFailureClassifier"/>. Positive cases confirm structured provider errors
    /// (HTTP status, provider error type) classify into usage-limit, auth, and model-unavailable kinds; negative
    /// cases confirm that a non-zero exit without a structured provider error is a crash, so output text such as
    /// "error CS0403" or "BillingService.cs" can never quarantine a captain.
    /// </summary>
    public sealed class RuntimeFailureClassifierSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("clean_exit_is_clean", "Exit code 0 is Clean even with a provider error", TestTags.Positive, () =>
            {
                AssertEqual(RuntimeFailureKindEnum.Clean, RuntimeFailureClassifier.Classify(0, null));
                AssertEqual(RuntimeFailureKindEnum.Clean, RuntimeFailureClassifier.Classify(0, new RuntimeProviderError { HttpStatusCode = 429 }));
            }));

            cases.Add(Case("usage_limit_from_status", "HTTP 429, 402, and 529 classify as UsageLimit", TestTags.Positive, () =>
            {
                AssertEqual(RuntimeFailureKindEnum.UsageLimit, RuntimeFailureClassifier.Classify(1, new RuntimeProviderError { HttpStatusCode = 429 }));
                AssertEqual(RuntimeFailureKindEnum.UsageLimit, RuntimeFailureClassifier.Classify(1, new RuntimeProviderError { HttpStatusCode = 402 }));
                AssertEqual(RuntimeFailureKindEnum.UsageLimit, RuntimeFailureClassifier.Classify(1, new RuntimeProviderError { HttpStatusCode = 529 }));
            }));

            cases.Add(Case("usage_limit_from_error_type", "Provider usage error types classify as UsageLimit", TestTags.Positive, () =>
            {
                AssertEqual(RuntimeFailureKindEnum.UsageLimit, RuntimeFailureClassifier.Classify(1, new RuntimeProviderError { ErrorType = "rate_limit_error" }));
                AssertEqual(RuntimeFailureKindEnum.UsageLimit, RuntimeFailureClassifier.Classify(1, new RuntimeProviderError { ErrorType = "insufficient_quota" }));
                AssertEqual(RuntimeFailureKindEnum.UsageLimit, RuntimeFailureClassifier.Classify(1, new RuntimeProviderError { ErrorType = "usage_limit_reached" }));
            }));

            cases.Add(Case("auth_failure_from_status_and_type", "HTTP 401/403 and auth error types classify as AuthFailure", TestTags.Positive, () =>
            {
                AssertEqual(RuntimeFailureKindEnum.AuthFailure, RuntimeFailureClassifier.Classify(1, new RuntimeProviderError { HttpStatusCode = 401 }));
                AssertEqual(RuntimeFailureKindEnum.AuthFailure, RuntimeFailureClassifier.Classify(1, new RuntimeProviderError { HttpStatusCode = 403 }));
                AssertEqual(RuntimeFailureKindEnum.AuthFailure, RuntimeFailureClassifier.Classify(1, new RuntimeProviderError { ErrorType = "authentication_error" }));
                AssertEqual(RuntimeFailureKindEnum.AuthFailure, RuntimeFailureClassifier.Classify(1, new RuntimeProviderError { ErrorType = "permission_error" }));
            }));

            cases.Add(Case("model_unavailable_from_status_and_type", "HTTP 404 and not-found error types classify as ModelUnavailable", TestTags.Positive, () =>
            {
                AssertEqual(RuntimeFailureKindEnum.ModelUnavailable, RuntimeFailureClassifier.Classify(1, new RuntimeProviderError { HttpStatusCode = 404 }));
                AssertEqual(RuntimeFailureKindEnum.ModelUnavailable, RuntimeFailureClassifier.Classify(1, new RuntimeProviderError { ErrorType = "not_found_error" }));
            }));

            cases.Add(Case("error_type_wins_over_status", "A known provider error type decides before the HTTP status", TestTags.Positive, () =>
            {
                AssertEqual(RuntimeFailureKindEnum.UsageLimit, RuntimeFailureClassifier.Classify(1, new RuntimeProviderError { HttpStatusCode = 403, ErrorType = "rate_limit_error" }));
            }));

            cases.Add(Case("no_provider_error_is_crash", "A non-zero exit without a provider error is Crash", TestTags.Negative, () =>
            {
                AssertEqual(RuntimeFailureKindEnum.Crash, RuntimeFailureClassifier.Classify(1, null));
                AssertEqual(RuntimeFailureKindEnum.Crash, RuntimeFailureClassifier.Classify(null, null));
                AssertEqual(RuntimeFailureKindEnum.Crash, RuntimeFailureClassifier.Classify(139, null));
            }));

            cases.Add(Case("unknown_status_and_type_is_crash", "A provider error with an unknown status and type is Crash", TestTags.Negative, () =>
            {
                AssertEqual(RuntimeFailureKindEnum.Crash, RuntimeFailureClassifier.Classify(1, new RuntimeProviderError { HttpStatusCode = 500, ErrorType = "api_error" }));
                AssertEqual(RuntimeFailureKindEnum.Crash, RuntimeFailureClassifier.Classify(1, new RuntimeProviderError { Message = "rate limit 429 billing permission denied" }));
            }));

            cases.Add(Case("decide_builds_exit_info", "Decide records the exit code, provider error, and kind once", TestTags.Positive, () =>
            {
                RuntimeProviderError error = new RuntimeProviderError { HttpStatusCode = 401, Message = "invalid x-api-key" };
                RuntimeExitInfo info = RuntimeFailureClassifier.Decide(1, error);
                AssertEqual(1, info.ExitCode);
                AssertEqual(RuntimeFailureKindEnum.AuthFailure, info.FailureKind);
                AssertTrue(Object.ReferenceEquals(error, info.ProviderError), "provider error is carried");
                AssertTrue(RuntimeFailureClassifier.IsCaptainUnavailable(info.FailureKind), "auth failure makes the captain unavailable");
                AssertFalse(RuntimeFailureClassifier.IsCaptainUnavailable(RuntimeFailureKindEnum.Crash), "a crash does not");
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.RuntimeFailureClassifier",
                displayName: "Runtime Failure Classifier",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.RuntimeFailureClassifier",
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

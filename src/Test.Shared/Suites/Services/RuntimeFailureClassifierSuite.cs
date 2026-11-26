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

            cases.Add(Case("usage_limit_from_status", "HTTP 429 and 402 classify as UsageLimit", TestTags.Positive, () =>
            {
                AssertEqual(RuntimeFailureKindEnum.UsageLimit, RuntimeFailureClassifier.Classify(1, new RuntimeProviderError { HttpStatusCode = 429 }));
                AssertEqual(RuntimeFailureKindEnum.UsageLimit, RuntimeFailureClassifier.Classify(1, new RuntimeProviderError { HttpStatusCode = 402 }));
            }));

            cases.Add(Case("overload_is_transient_not_quota", "HTTP 529 and overloaded_error are Transient, so overload never quarantines a captain", TestTags.Positive, () =>
            {
                AssertEqual(RuntimeFailureKindEnum.Transient, RuntimeFailureClassifier.Classify(1, new RuntimeProviderError { HttpStatusCode = 529 }));
                AssertEqual(RuntimeFailureKindEnum.Transient, RuntimeFailureClassifier.Classify(1, new RuntimeProviderError { ErrorType = "overloaded_error" }));
                AssertEqual(RuntimeFailureKindEnum.Transient, RuntimeFailureClassifier.Classify(1, RuntimeProviderErrorParser.TryParseClaudeTextLine("API Error: 529 Overloaded")));
                AssertFalse(RuntimeFailureClassifier.IsCaptainUnavailable(RuntimeFailureClassifier.Classify(1, new RuntimeProviderError { HttpStatusCode = 529 })), "overload leaves the captain available");
            }));

            cases.Add(Case("stream_and_timeout_errors_are_transient", "Lost streams, timeouts and 408 are Transient", TestTags.Positive, () =>
            {
                foreach (string type in new[] { "llm_stream_error", "llm_timeout", "llm_connection_error", "timeout_error", "server_error" })
                {
                    AssertEqual(RuntimeFailureKindEnum.Transient, RuntimeFailureClassifier.Classify(1, new RuntimeProviderError { ErrorType = type }));
                }
                AssertEqual(RuntimeFailureKindEnum.Transient, RuntimeFailureClassifier.Classify(1, new RuntimeProviderError { HttpStatusCode = 408 }));
                AssertEqual(RuntimeFailureKindEnum.Transient, RuntimeFailureClassifier.Classify(1, new RuntimeProviderError { HttpStatusCode = 504, ErrorType = "inference_error" }));
            }));

            cases.Add(Case("context_rejections_are_task_failures", "413 and context-length 400 responses are TaskFailure; other 400s are not", TestTags.Positive, () =>
            {
                AssertEqual(RuntimeFailureKindEnum.TaskFailure, RuntimeFailureClassifier.Classify(1, new RuntimeProviderError { HttpStatusCode = 413 }));
                AssertEqual(RuntimeFailureKindEnum.TaskFailure, RuntimeFailureClassifier.Classify(1, new RuntimeProviderError { HttpStatusCode = 400, Message = "This model's maximum context length is 8192 tokens" }));
                AssertEqual(RuntimeFailureKindEnum.Unclassified, RuntimeFailureClassifier.Classify(1, new RuntimeProviderError { HttpStatusCode = 400, Message = "Unsupported tool request shape" }));
            }));

            cases.Add(Case("generic_inference_failure_uses_status_then_transient", "inference_error takes its HTTP status; without one it is Transient (bounded by recovery attempts)", TestTags.Positive, () =>
            {
                AssertEqual(RuntimeFailureKindEnum.Transient, RuntimeFailureClassifier.Classify(1, new RuntimeProviderError { ErrorType = "inference_error" }));
                AssertEqual(RuntimeFailureKindEnum.AuthFailure, RuntimeFailureClassifier.Classify(1, new RuntimeProviderError { ErrorType = "inference_error", HttpStatusCode = 401 }));
                AssertEqual(RuntimeFailureKindEnum.ModelUnavailable, RuntimeFailureClassifier.Classify(1, new RuntimeProviderError { ErrorType = "llm_error", HttpStatusCode = 404 }));
                AssertEqual(RuntimeFailureKindEnum.Unclassified, RuntimeFailureClassifier.Classify(1, new RuntimeProviderError { ErrorType = "runtime_error" }));
            }));

            cases.Add(Case("later_minor_error_does_not_mask_fatal_cause", "A non-fatal tool error or a transient blip reported later does not replace a more significant earlier error", TestTags.Negative, () =>
            {
                RuntimeProviderError quota = new RuntimeProviderError { HttpStatusCode = 429, ErrorType = "rate_limit_error" };
                RuntimeProviderError toolTimeout = new RuntimeProviderError { ErrorType = "tool_timeout" };
                RuntimeProviderError stream = new RuntimeProviderError { ErrorType = "llm_stream_error" };
                RuntimeProviderError context = new RuntimeProviderError { ErrorType = "context_limit_exceeded" };

                AssertTrue(Object.ReferenceEquals(quota, RuntimeFailureClassifier.KeepMoreSignificant(quota, toolTimeout)), "a tool timeout does not mask a quota failure");
                AssertTrue(Object.ReferenceEquals(context, RuntimeFailureClassifier.KeepMoreSignificant(context, stream)), "a stream blip does not mask a context overflow");
                AssertTrue(Object.ReferenceEquals(context, RuntimeFailureClassifier.KeepMoreSignificant(toolTimeout, context)), "a real failure replaces a non-fatal one");
                AssertTrue(Object.ReferenceEquals(stream, RuntimeFailureClassifier.KeepMoreSignificant(null, stream)));
                RuntimeProviderError secondStream = new RuntimeProviderError { ErrorType = "llm_timeout" };
                AssertTrue(Object.ReferenceEquals(secondStream, RuntimeFailureClassifier.KeepMoreSignificant(stream, secondStream)), "equal significance keeps the latest");
            }));

            cases.Add(Case("every_known_error_type_has_a_deliberate_kind", "Each error type a runtime can report has an explicit expected classification", TestTags.Positive, () =>
            {
                Dictionary<string, RuntimeFailureKindEnum> expected = new Dictionary<string, RuntimeFailureKindEnum>(StringComparer.Ordinal)
                {
                    { "rate_limit_error", RuntimeFailureKindEnum.UsageLimit },
                    { "rate_limit_exceeded", RuntimeFailureKindEnum.UsageLimit },
                    { "insufficient_quota", RuntimeFailureKindEnum.UsageLimit },
                    { "usage_limit_reached", RuntimeFailureKindEnum.UsageLimit },
                    { "billing_error", RuntimeFailureKindEnum.UsageLimit },
                    { "authentication_error", RuntimeFailureKindEnum.AuthFailure },
                    { "permission_error", RuntimeFailureKindEnum.AuthFailure },
                    { "invalid_api_key", RuntimeFailureKindEnum.AuthFailure },
                    { "not_found_error", RuntimeFailureKindEnum.ModelUnavailable },
                    { "model_not_found", RuntimeFailureKindEnum.ModelUnavailable },
                    { "max_iterations_reached", RuntimeFailureKindEnum.TaskFailure },
                    { "budget_exceeded", RuntimeFailureKindEnum.TaskFailure },
                    { "degenerate_output", RuntimeFailureKindEnum.TaskFailure },
                    { "incomplete_completion", RuntimeFailureKindEnum.TaskFailure },
                    { "output_token_limit_reached", RuntimeFailureKindEnum.TaskFailure },
                    { "tool_call_limit_reached", RuntimeFailureKindEnum.TaskFailure },
                    { "context_limit_exceeded", RuntimeFailureKindEnum.TaskFailure },
                    { "schema_validation_failed", RuntimeFailureKindEnum.TaskFailure },
                    { "llm_connection_error", RuntimeFailureKindEnum.Transient },
                    { "llm_timeout", RuntimeFailureKindEnum.Transient },
                    { "llm_stream_error", RuntimeFailureKindEnum.Transient },
                    { "api_error", RuntimeFailureKindEnum.Transient },
                    { "server_error", RuntimeFailureKindEnum.Transient },
                    { "overloaded_error", RuntimeFailureKindEnum.Transient },
                    { "timeout_error", RuntimeFailureKindEnum.Transient },
                    { "inference_error", RuntimeFailureKindEnum.Transient },
                    { "llm_error", RuntimeFailureKindEnum.Transient },
                    { "error", RuntimeFailureKindEnum.Unclassified },
                    { "turn.failed", RuntimeFailureKindEnum.Unclassified },
                    { "runtime_error", RuntimeFailureKindEnum.Unclassified },
                    { "cancelled", RuntimeFailureKindEnum.Crash }
                };

                foreach (string type in RuntimeFailureClassifier.KnownErrorTypes)
                {
                    AssertTrue(expected.ContainsKey(type), "error type " + type + " needs an explicit expected classification");
                    AssertEqual(expected[type], RuntimeFailureClassifier.Classify(1, new RuntimeProviderError { ErrorType = type }));
                }
                AssertEqual(expected.Count, RuntimeFailureClassifier.KnownErrorTypes.Count);
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

            cases.Add(Case("no_provider_error_is_unclassified", "A non-zero exit without a provider error is Unclassified unless the process died abnormally", TestTags.Negative, () =>
            {
                AssertEqual(RuntimeFailureKindEnum.Unclassified, RuntimeFailureClassifier.Classify(1, null));
                AssertEqual(RuntimeFailureKindEnum.Unclassified, RuntimeFailureClassifier.Classify(null, null));
                AssertEqual(RuntimeFailureKindEnum.Unclassified, RuntimeFailureClassifier.Classify(128, null), "128 is not a signal exit");
                AssertEqual(RuntimeFailureKindEnum.Crash, RuntimeFailureClassifier.Classify(139, null), "SIGSEGV");
                AssertEqual(RuntimeFailureKindEnum.Crash, RuntimeFailureClassifier.Classify(137, null), "SIGKILL, for example the OOM killer");
                AssertEqual(RuntimeFailureKindEnum.Crash, RuntimeFailureClassifier.Classify(unchecked((int)0xC0000005), null), "Windows access violation");
                AssertEqual(RuntimeFailureKindEnum.Unclassified, RuntimeFailureClassifier.Classify(193, null));
                AssertEqual(RuntimeFailureKindEnum.Crash, RuntimeFailureClassifier.Classify(134, new RuntimeProviderError { ErrorType = "runtime_error" }), "an abnormal death with an unrecognized error is still a crash");
            }));

            cases.Add(Case("unknown_status_and_type_is_unclassified", "A provider error with an unknown status and type is Unclassified", TestTags.Negative, () =>
            {
                AssertEqual(RuntimeFailureKindEnum.Unclassified, RuntimeFailureClassifier.Classify(1, new RuntimeProviderError { HttpStatusCode = 418, ErrorType = "teapot_error" }));
                // A 5xx usually clears on the next request (for example a local server that could not parse a tool call).
                AssertEqual(RuntimeFailureKindEnum.Transient, RuntimeFailureClassifier.Classify(1, new RuntimeProviderError { HttpStatusCode = 500, ErrorType = "api_error" }));
                AssertEqual(RuntimeFailureKindEnum.Unclassified, RuntimeFailureClassifier.Classify(1, new RuntimeProviderError { Message = "rate limit 429 billing permission denied" }));
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

            cases.Add(Case("limits_have_actionable_mission_reasons", "Turn, tool, output and context budgets have distinct remediation", TestTags.Positive, () =>
            {
                foreach (string type in new[] { "max_iterations_reached", "tool_call_limit_reached", "output_token_limit_reached", "context_limit_exceeded", "incomplete_completion" })
                {
                    RuntimeExitInfo info = RuntimeFailureClassifier.Decide(1, new RuntimeProviderError { ErrorType = type, Reason = "Precise configured limit." });
                    AssertEqual(RuntimeFailureKindEnum.TaskFailure, info.FailureKind);
                    AssertContains("Precise configured limit", info.Describe());
                    AssertContains("Remediation:", info.Describe());
                }
            }));

            cases.Add(Case("rejected_request_has_configuration_and_integration_guidance", "Rejected requests preserve provider detail and explain configuration and SDK checks", TestTags.Negative, () =>
            {
                foreach (int status in new[] { 400, 405, 415, 422 })
                {
                    RuntimeExitInfo info = RuntimeFailureClassifier.Decide(1, RuntimeProviderErrorParser.FromHttpStatus(status, "Unsupported tool request shape"));
                    AssertContains("HTTP " + status, info.Describe());
                    AssertContains("Unsupported tool request shape", info.Describe());
                    AssertContains("Remediation:", info.Describe());
                    AssertContains("inference options and tool support", info.Describe());
                    AssertContains("integration defect", info.Describe());
                }
                AssertFalse(RuntimeFailureClassifier.Decide(1, new RuntimeProviderError { Message = "HTTP 400 in ordinary output" }).Describe().Contains("provider rejected"), "guidance requires a structured status, not message keywords");
                AssertContains("credentials", RuntimeFailureClassifier.Decide(1, new RuntimeProviderError { HttpStatusCode = 401 }).Describe());
                AssertContains("quota", RuntimeFailureClassifier.Decide(1, new RuntimeProviderError { HttpStatusCode = 429 }).Describe());
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

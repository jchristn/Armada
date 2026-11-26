namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Pure classifier that decides, once, why a captain runtime process ended: from its exit code and the
    /// structured provider error (HTTP status and provider error type) the runtime reported while it ran. Output
    /// text is never searched, so a build log line such as "error CS0403" or a file named BillingService.cs cannot
    /// quarantine a captain. Side-effect free so it unit tests without launching anything.
    /// </summary>
    public static class RuntimeFailureClassifier
    {
        #region Public-Methods

        /// <summary>
        /// Decide the typed exit outcome of a runtime process.
        /// </summary>
        /// <param name="exitCode">The process exit code (null is treated as a non-zero failure).</param>
        /// <param name="providerError">The last structured provider error the runtime reported, if any.</param>
        /// <returns>The exit info with its classified failure kind.</returns>
        public static RuntimeExitInfo Decide(int? exitCode, RuntimeProviderError? providerError)
        {
            RuntimeExitInfo info = new RuntimeExitInfo();
            info.ExitCode = exitCode;
            info.ProviderError = providerError;
            info.FailureKind = Classify(exitCode, providerError);
            return info;
        }

        /// <summary>
        /// Classify a runtime exit. A zero exit code is always <see cref="RuntimeFailureKindEnum.Clean"/>. A non-zero
        /// exit is classified from the provider error's machine error type when it is a known code, otherwise from its
        /// HTTP status (429, 402: usage limit; 401, 403: auth failure; 404: model unavailable; 408, 500, 502, 503, 504,
        /// 529: transient; 413, or a 400 whose message names the context or token limit: task failure). A generic
        /// inference failure with no usable status is transient: the recovery budget bounds its retries. A process that
        /// died abnormally (see <see cref="IsAbnormalExit"/>) without a classified provider error is
        /// <see cref="RuntimeFailureKindEnum.Crash"/>. Any other non-zero exit with no provider error, or with one that
        /// carries neither a known type nor a known status, is <see cref="RuntimeFailureKindEnum.Unclassified"/>.
        /// </summary>
        /// <param name="exitCode">The process exit code (null is treated as a non-zero failure).</param>
        /// <param name="providerError">The structured provider error, if any.</param>
        /// <returns>The classified failure kind.</returns>
        public static RuntimeFailureKindEnum Classify(int? exitCode, RuntimeProviderError? providerError)
        {
            if (exitCode.HasValue && exitCode.Value == 0) return RuntimeFailureKindEnum.Clean;
            if (providerError == null) return Unexplained(exitCode);

            RuntimeFailureKindEnum? fromType = ClassifyErrorType(providerError.ErrorType);
            if (fromType.HasValue) return fromType.Value;

            // An open-model server names most failures only in its message (RS-5.3).
            RuntimeFailureKindEnum? fromMessage = ClassifyErrorType(OpenModelErrorPatterns.Match(providerError.HttpStatusCode, providerError.Message));
            if (fromMessage.HasValue) return fromMessage.Value;

            RuntimeFailureKindEnum? fromStatus = ClassifyHttpStatus(providerError.HttpStatusCode, providerError.Message);
            if (fromStatus.HasValue) return fromStatus.Value;

            if (IsGenericInferenceFailure(providerError.ErrorType)) return RuntimeFailureKindEnum.Transient;

            return Unexplained(exitCode);
        }

        /// <summary>
        /// Whether an exit code means the process died abnormally rather than exiting on its own: killed by a signal
        /// (129 to 192, the shell convention of 128 plus the signal number, on Linux and macOS) or a negative code (a
        /// Windows crash status such as 0xC0000005 read as a signed integer).
        /// </summary>
        /// <param name="exitCode">The process exit code, or null.</param>
        /// <returns>True for an abnormal death.</returns>
        public static bool IsAbnormalExit(int? exitCode)
        {
            if (!exitCode.HasValue) return false;
            return exitCode.Value < 0 || (exitCode.Value > 128 && exitCode.Value <= 192);
        }

        /// <summary>
        /// Choose which of two provider errors reported by one process explains its exit. The later error replaces the
        /// earlier one unless the earlier is more significant, so a non-fatal tool error (a denied or timed-out tool call)
        /// reported after a fatal one cannot mask it. Ranked lowest to highest: non-fatal tool errors, unclassified,
        /// transient, task failure, then a provider condition that makes the captain unavailable.
        /// </summary>
        /// <param name="current">The error kept so far, or null.</param>
        /// <param name="candidate">The newly reported error.</param>
        /// <returns>The error to keep.</returns>
        /// <exception cref="ArgumentNullException">Thrown when candidate is null.</exception>
        public static RuntimeProviderError KeepMoreSignificant(RuntimeProviderError? current, RuntimeProviderError candidate)
        {
            if (candidate == null) throw new ArgumentNullException(nameof(candidate));
            if (current == null) return candidate;
            return Significance(candidate) >= Significance(current) ? candidate : current;
        }

        /// <summary>
        /// Every machine error type a built-in runtime is known to report, so a test can assert that each one has a
        /// deliberate classification rather than falling through to <see cref="RuntimeFailureKindEnum.Unclassified"/> by accident.
        /// Types classified only through their HTTP status are listed with the status-less outcome.
        /// </summary>
        public static readonly IReadOnlyList<string> KnownErrorTypes = new List<string>
        {
            "rate_limit_error", "rate_limit_exceeded", "insufficient_quota", "usage_limit_reached", "billing_error",
            "authentication_error", "permission_error", "invalid_api_key",
            "not_found_error", "model_not_found", "model_out_of_memory", "tool_calling_unsupported",
            "max_iterations_reached", "budget_exceeded", "degenerate_output", "incomplete_completion",
            "output_token_limit_reached", "tool_call_limit_reached", "context_limit_exceeded", "schema_validation_failed",
            "tool_call_parse_error",
            "llm_connection_error", "llm_timeout", "llm_stream_error", "api_error", "server_error", "overloaded_error",
            "unavailable_error", "runner_terminated", "model_loading",
            "timeout_error", "inference_error", "llm_error", "error", "turn.failed",
            "runtime_error", "cancelled"
        };

        /// <summary>
        /// Whether a failure kind means the captain itself cannot work until something outside the mission changes
        /// (a provider limit resets, credentials or the model configuration are fixed).
        /// </summary>
        /// <param name="kind">Failure kind.</param>
        /// <returns>True for usage-limit, auth, and model-unavailable failures.</returns>
        public static bool IsCaptainUnavailable(RuntimeFailureKindEnum kind)
        {
            return kind == RuntimeFailureKindEnum.UsageLimit
                || kind == RuntimeFailureKindEnum.AuthFailure
                || kind == RuntimeFailureKindEnum.ModelUnavailable;
        }

        #endregion

        #region Private-Methods

        private static RuntimeFailureKindEnum Unexplained(int? exitCode)
        {
            return IsAbnormalExit(exitCode) ? RuntimeFailureKindEnum.Crash : RuntimeFailureKindEnum.Unclassified;
        }

        private static int Significance(RuntimeProviderError error)
        {
            if (!String.IsNullOrWhiteSpace(error.ErrorType) && MuxRunOutcomeTracker.NonFatalErrorCodes.Contains(error.ErrorType!.Trim())) return 0;
            RuntimeFailureKindEnum kind = Classify(1, error);
            if (IsCaptainUnavailable(kind)) return 4;
            if (kind == RuntimeFailureKindEnum.TaskFailure) return 3;
            if (kind == RuntimeFailureKindEnum.Transient) return 2;
            return 1;
        }

        private static RuntimeFailureKindEnum? ClassifyErrorType(string? errorType)
        {
            if (String.IsNullOrWhiteSpace(errorType)) return null;

            switch (errorType.Trim().ToLowerInvariant())
            {
                case "rate_limit_error":
                case "rate_limit_exceeded":
                case "insufficient_quota":
                case "usage_limit_reached":
                case "billing_error":
                    return RuntimeFailureKindEnum.UsageLimit;
                case "authentication_error":
                case "permission_error":
                case "invalid_api_key":
                    return RuntimeFailureKindEnum.AuthFailure;
                case "not_found_error":
                case "model_not_found":
                case OpenModelErrorPatterns.ModelOutOfMemory:
                case OpenModelErrorPatterns.ToolCallingUnsupported:
                    return RuntimeFailureKindEnum.ModelUnavailable;
                case "max_iterations_reached":
                case "budget_exceeded":
                case "degenerate_output":
                case "incomplete_completion":
                case "output_token_limit_reached":
                case "tool_call_limit_reached":
                case "context_limit_exceeded":
                case "schema_validation_failed":
                case OpenModelErrorPatterns.ToolCallParseError:
                    return RuntimeFailureKindEnum.TaskFailure;
                case "llm_connection_error":
                case "llm_timeout":
                case "llm_stream_error":
                case "api_error":
                case "server_error":
                case "timeout_error":
                case "overloaded_error":
                case "unavailable_error":
                case OpenModelErrorPatterns.RunnerTerminated:
                case OpenModelErrorPatterns.ModelLoading:
                    // Overload is capacity backpressure, not an exhausted quota: retry with backoff rather than
                    // quarantining the captain as if its account were out of credit.
                    return RuntimeFailureKindEnum.Transient;
                case "cancelled":
                    return RuntimeFailureKindEnum.Crash;
                default:
                    return null;
            }
        }

        private static RuntimeFailureKindEnum? ClassifyHttpStatus(int? status, string? message)
        {
            if (!status.HasValue) return null;

            switch (status.Value)
            {
                case 429:
                case 402:
                    return RuntimeFailureKindEnum.UsageLimit;
                case 401:
                case 403:
                    return RuntimeFailureKindEnum.AuthFailure;
                case 404:
                    return RuntimeFailureKindEnum.ModelUnavailable;
                case 408:
                case 500:
                case 502:
                case 503:
                case 504:
                case 529:
                    return RuntimeFailureKindEnum.Transient;
                case 413:
                    return RuntimeFailureKindEnum.TaskFailure;
                case 400:
                    return OpenModelErrorPatterns.MentionsContextLimit(message) ? RuntimeFailureKindEnum.TaskFailure : (RuntimeFailureKindEnum?)null;
                default:
                    return null;
            }
        }

        private static bool IsGenericInferenceFailure(string? errorType)
        {
            if (String.IsNullOrWhiteSpace(errorType)) return false;
            string normalized = errorType.Trim().ToLowerInvariant();
            return normalized == "inference_error" || normalized == "llm_error";
        }

        #endregion
    }
}

namespace Armada.Core.Services
{
    using System;
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
        /// HTTP status (429, 402, 529: usage limit; 401, 403: auth failure; 404: model unavailable). A non-zero exit
        /// with no provider error, or with one that carries neither a known type nor a known status, is
        /// <see cref="RuntimeFailureKindEnum.Crash"/>.
        /// </summary>
        /// <param name="exitCode">The process exit code (null is treated as a non-zero failure).</param>
        /// <param name="providerError">The structured provider error, if any.</param>
        /// <returns>The classified failure kind.</returns>
        public static RuntimeFailureKindEnum Classify(int? exitCode, RuntimeProviderError? providerError)
        {
            if (exitCode.HasValue && exitCode.Value == 0) return RuntimeFailureKindEnum.Clean;
            if (providerError == null) return RuntimeFailureKindEnum.Crash;

            RuntimeFailureKindEnum? fromType = ClassifyErrorType(providerError.ErrorType);
            if (fromType.HasValue) return fromType.Value;

            if (providerError.HttpStatusCode.HasValue)
            {
                switch (providerError.HttpStatusCode.Value)
                {
                    case 429:
                    case 402:
                    case 529:
                        return RuntimeFailureKindEnum.UsageLimit;
                    case 401:
                    case 403:
                        return RuntimeFailureKindEnum.AuthFailure;
                    case 404:
                        return RuntimeFailureKindEnum.ModelUnavailable;
                }
            }

            return RuntimeFailureKindEnum.Crash;
        }

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
                case "overloaded_error":
                    return RuntimeFailureKindEnum.UsageLimit;
                case "authentication_error":
                case "permission_error":
                case "invalid_api_key":
                    return RuntimeFailureKindEnum.AuthFailure;
                case "not_found_error":
                case "model_not_found":
                    return RuntimeFailureKindEnum.ModelUnavailable;
                default:
                    return null;
            }
        }

        #endregion
    }
}

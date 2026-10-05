namespace Armada.Core.Services
{
    using System;
    using System.Globalization;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using Armada.Core.Models;

    /// <summary>
    /// Builds <see cref="RuntimeProviderError"/> values from the structured error channels captain runtimes expose:
    /// an HTTP response (API-endpoint captains), Claude Code's stream-json terminal result event, and Claude Code's
    /// own protocol error lines in text mode ("API Error: STATUS {json}" and "Claude AI usage limit reached|EPOCH").
    /// A protocol line only counts when it is the whole line; its status and error type come from the status token
    /// and the deserialized JSON body, never from a keyword search. Anything else (ordinary output that mentions
    /// "429", "billing", or "permission denied") produces no provider error.
    /// </summary>
    public static class RuntimeProviderErrorParser
    {
        #region Private-Members

        private const string ApiErrorPrefix = "API Error: ";
        private const string UsageLimitPrefix = "Claude AI usage limit reached|";

        private static readonly Regex _ApiErrorLine = new Regex(
            @"^API Error: (?<status>[1-5]\d\d)(?:\s+(?<body>\{.*\}))?(?:\s.*)?$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex _UsageLimitLine = new Regex(
            @"^Claude AI usage limit reached\|(?<epoch>\d{9,11})$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build a provider error from an HTTP response status (API-endpoint captains).
        /// </summary>
        /// <param name="httpStatusCode">HTTP status code; values outside 100-599 are ignored.</param>
        /// <param name="message">Provider message, for display only.</param>
        /// <param name="retryAfterSeconds">Retry-After seconds, when the response carried the header.</param>
        /// <returns>The provider error, or null when the status is not an error status.</returns>
        public static RuntimeProviderError? FromHttpStatus(int httpStatusCode, string? message, int? retryAfterSeconds = null)
        {
            if (httpStatusCode < 400 || httpStatusCode > 599) return null;
            RuntimeProviderError error = new RuntimeProviderError();
            error.HttpStatusCode = httpStatusCode;
            error.Message = message;
            if (retryAfterSeconds.HasValue && retryAfterSeconds.Value > 0) error.RetryAfterSeconds = retryAfterSeconds.Value;
            return error;
        }

        /// <summary>
        /// Try to read a provider error from one line of Claude Code text-mode output. Recognizes only whole-line
        /// protocol errors emitted by the CLI itself.
        /// </summary>
        /// <param name="line">One output line.</param>
        /// <returns>The provider error, or null when the line is not a Claude Code protocol error line.</returns>
        public static RuntimeProviderError? TryParseClaudeTextLine(string? line)
        {
            if (String.IsNullOrEmpty(line)) return null;
            string trimmed = line.Trim();

            if (trimmed.StartsWith(UsageLimitPrefix, StringComparison.Ordinal))
            {
                Match usage = _UsageLimitLine.Match(trimmed);
                if (!usage.Success) return null;
                if (!Int64.TryParse(usage.Groups["epoch"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out long epoch)) return null;

                RuntimeProviderError limit = new RuntimeProviderError();
                limit.ErrorType = "usage_limit_reached";
                limit.Message = "Claude AI usage limit reached";
                try { limit.ResetUtc = DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime; }
                catch (ArgumentOutOfRangeException) { limit.ResetUtc = null; }
                return limit;
            }

            if (!trimmed.StartsWith(ApiErrorPrefix, StringComparison.Ordinal)) return null;

            Match match = _ApiErrorLine.Match(trimmed);
            if (!match.Success) return null;

            RuntimeProviderError error = new RuntimeProviderError();
            error.HttpStatusCode = Int32.Parse(match.Groups["status"].Value, CultureInfo.InvariantCulture);

            if (match.Groups["body"].Success)
            {
                ProviderErrorEnvelope? envelope = DeserializeEnvelope(match.Groups["body"].Value);
                if (envelope?.Error != null)
                {
                    error.ErrorType = envelope.Error.Type;
                    error.Message = envelope.Error.Message;
                }
            }

            return error;
        }

        /// <summary>
        /// Try to read a provider error from one line of Claude Code stream-json output: the terminal "result" event
        /// with is_error set. The event's result text is the CLI's own error line and is read with
        /// <see cref="TryParseClaudeTextLine"/>; an error result without a protocol error line yields a provider
        /// error with neither status nor type (which classifies as a crash).
        /// </summary>
        /// <param name="line">One stdout line.</param>
        /// <returns>The provider error, or null when the line is not an error result event.</returns>
        public static RuntimeProviderError? TryParseClaudeStreamJsonLine(string? line)
        {
            if (String.IsNullOrEmpty(line)) return null;
            string trimmed = line.Trim();
            if (!trimmed.StartsWith("{", StringComparison.Ordinal)) return null;

            ClaudeResultEvent? result;
            try
            {
                result = JsonSerializer.Deserialize<ClaudeResultEvent>(trimmed);
            }
            catch (JsonException)
            {
                return null;
            }

            if (result == null || !String.Equals(result.Type, "result", StringComparison.Ordinal) || !result.IsError) return null;

            RuntimeProviderError? fromText = TryParseClaudeTextLine(result.Result);
            if (fromText != null) return fromText;

            RuntimeProviderError generic = new RuntimeProviderError();
            generic.Message = String.IsNullOrWhiteSpace(result.Result) ? result.Subtype : result.Result;
            return generic;
        }

        #endregion

        #region Private-Methods

        private static ProviderErrorEnvelope? DeserializeEnvelope(string json)
        {
            try
            {
                return JsonSerializer.Deserialize<ProviderErrorEnvelope>(json);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        #endregion
    }
}

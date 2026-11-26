namespace Armada.Core.Services
{
    using System;
    using System.Globalization;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using Armada.Core.Models;
    using Armada.Core.Protocol;

    /// <summary>
    /// Builds <see cref="RuntimeProviderError"/> values from the structured error channels captain runtimes expose:
    /// an HTTP response (API-endpoint captains), Claude Code's stream-json terminal result event, Codex exec --json error
    /// and turn.failed events, and Claude Code's
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

        private static readonly Regex _CodexStatus = new Regex(
            @"\bstatus:?\s+(?<status>[1-5]\d\d)\b",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex _UsageLimitLine = new Regex(
            @"^Claude AI usage limit reached\|(?<epoch>\d{9,11})$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        // An HTTP error status in the forms CLIs print it: "status 429", "status code: 503", "HTTP 401", "HTTP/1.1 502",
        // "[429 Too Many Requests]". A bare three-digit number is never read as a status.
        private static readonly Regex _CliHttpStatus = new Regex(
            @"(?:\bstatus(?:\s*code)?\s*[:=]?\s*|\bHTTP(?:/\d(?:\.\d)?)?\s*[:=]?\s*)(?<status>[45]\d\d)\b"
            + @"|\b(?<status>[45]\d\d)\s+(?:Too Many Requests|Service Unavailable|Bad Gateway|Gateway Time-?out|Internal Server Error|Unauthorized|Forbidden|Request Timeout|Payload Too Large)\b",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // pi's assistant errorMessage for a provider HTTP error: the status, then usually the provider's JSON body
        // ("401 {\"type\":\"error\",...}"). Only a leading status counts.
        private static readonly Regex _PiStatusMessage = new Regex(
            @"^(?<status>[45]\d\d)\b(?:\s+(?<body>\{.*\}))?",
            RegexOptions.CultureInvariant | RegexOptions.Singleline | RegexOptions.Compiled);

        private static readonly Regex _CliNetworkError = new Regex(
            @"\b(?:ECONNREFUSED|ECONNRESET|ENOTFOUND|ETIMEDOUT|EAI_AGAIN|EHOSTUNREACH|ENETUNREACH|UND_ERR_CONNECT_TIMEOUT|UND_ERR_SOCKET)\b|\bfetch failed\b",
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
            error.ErrorType = OpenModelErrorPatterns.Match(httpStatusCode, message);
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

        /// <summary>
        /// Try to read a provider error from one line of Codex exec --json output: an <c>error</c> event or a
        /// <c>turn.failed</c> event. The message is the CLI's own; an HTTP status is taken only from the status token
        /// the CLI writes into it ("unexpected status 401 Unauthorized", "last status: 429").
        /// </summary>
        /// <param name="line">One stdout line.</param>
        /// <returns>The provider error, or null when the line is not a Codex error event.</returns>
        public static RuntimeProviderError? TryParseCodexJsonLine(string? line)
        {
            if (!CodexStreamEvent.TryParse(line, out CodexStreamEvent? evt) || evt == null) return null;

            string? message;
            if (String.Equals(evt.Type, CodexStreamEvent.TypeError, StringComparison.Ordinal))
            {
                message = evt.Message;
            }
            else if (String.Equals(evt.Type, CodexStreamEvent.TypeTurnFailed, StringComparison.Ordinal))
            {
                message = null;
                if (!String.IsNullOrWhiteSpace(evt.Error))
                {
                    try
                    {
                        CodexErrorPayload? payload = JsonSerializer.Deserialize<CodexErrorPayload>(evt.Error!);
                        message = payload?.Message;
                    }
                    catch (JsonException)
                    {
                        message = null;
                    }
                }
            }
            else
            {
                return null;
            }

            RuntimeProviderError error = new RuntimeProviderError();
            error.ErrorType = evt.Type;
            error.Message = String.IsNullOrWhiteSpace(message) ? null : message!.Trim();
            error.Reason = "codex " + (evt.Type == CodexStreamEvent.TypeTurnFailed ? "turn failed" : "error") + (error.Message != null ? ": " + Shorten(error.Message) : String.Empty);
            if (error.Message != null)
            {
                Match match = _CodexStatus.Match(error.Message);
                if (match.Success) error.HttpStatusCode = Int32.Parse(match.Groups["status"].Value, CultureInfo.InvariantCulture);
            }

            return error;
        }

        /// <summary>
        /// Try to read a provider error from one stderr line of a text-mode CLI that has no structured error channel
        /// (Gemini, Cursor, OpenCode, and pi stderr). Only explicit signals are read: an HTTP error status written as a status (never a
        /// bare number), a Google RPC status name (RESOURCE_EXHAUSTED, UNAUTHENTICATED, PERMISSION_DENIED, UNAVAILABLE,
        /// DEADLINE_EXCEEDED), or a network error code. Callers pass stderr only, so the agent's own narration on stdout
        /// is never mistaken for a provider failure.
        /// </summary>
        /// <param name="line">One stderr line.</param>
        /// <param name="runtimeName">Runtime name for the reason text.</param>
        /// <returns>The provider error, or null when the line carries no recognizable provider signal.</returns>
        public static RuntimeProviderError? TryParseCliStderrLine(string? line, string runtimeName)
        {
            if (String.IsNullOrWhiteSpace(line)) return null;
            string text = line.Trim();
            if (text.Length > 4000) text = text.Substring(0, 4000);

            RuntimeProviderError error = new RuntimeProviderError();
            error.Message = Shorten(text);
            error.Reason = runtimeName + " reported a provider error: " + error.Message;

            Match status = _CliHttpStatus.Match(text);
            if (status.Success)
            {
                error.HttpStatusCode = Int32.Parse(status.Groups["status"].Value, CultureInfo.InvariantCulture);
                error.ErrorType = OpenModelErrorPatterns.Match(error.HttpStatusCode, text);
                return error;
            }

            if (text.Contains("RESOURCE_EXHAUSTED", StringComparison.Ordinal)) { error.ErrorType = "rate_limit_exceeded"; return error; }
            if (text.Contains("UNAUTHENTICATED", StringComparison.Ordinal)) { error.ErrorType = "authentication_error"; return error; }
            if (text.Contains("PERMISSION_DENIED", StringComparison.Ordinal)) { error.ErrorType = "permission_error"; return error; }
            if (text.Contains("DEADLINE_EXCEEDED", StringComparison.Ordinal)) { error.ErrorType = "llm_timeout"; return error; }
            if (text.Contains("UNAVAILABLE", StringComparison.Ordinal) && text.Contains("\"status\"", StringComparison.Ordinal)) { error.ErrorType = "server_error"; return error; }
            if (_CliNetworkError.IsMatch(text)) { error.ErrorType = "llm_connection_error"; return error; }
            string? openModel = OpenModelErrorPatterns.Match(null, text);
            if (openModel != null) { error.ErrorType = openModel; return error; }
            return null;
        }

        /// <summary>
        /// Try to read a provider error from one line of <c>pi -p --mode json</c> output. pi exits 0 in JSON mode even when
        /// the provider fails, so the stream is the only signal. Only terminal records are read, so a failure that pi
        /// retries and then recovers from is never reported: an <c>agent_end</c> whose <c>willRetry</c> is not true and
        /// whose last assistant message stopped with <c>error</c>, or an <c>auto_retry_end</c> with <c>success: false</c>
        /// (which follows that <c>agent_end</c>; callers should raise only the first error of a run). The HTTP status is
        /// taken only from the start of pi's error text (for example <c>401 {...}</c>), with the provider's error type
        /// and message from the JSON body; other text is checked for network error codes.
        /// </summary>
        /// <param name="line">One stdout line.</param>
        /// <returns>The provider error, or null when the line is not a terminal pi error record.</returns>
        public static RuntimeProviderError? TryParsePiJsonLine(string? line)
        {
            if (!PiStreamEvent.TryParse(line, out PiStreamEvent? evt) || evt == null) return null;

            string? errorText;
            if (String.Equals(evt.Type, PiStreamEvent.TypeAgentEnd, StringComparison.Ordinal) && evt.WillRetry != true)
            {
                PiMessage? last = evt.LastAssistantMessage;
                if (last == null || !String.Equals(last.StopReason, PiMessage.StopReasonError, StringComparison.Ordinal)) return null;
                errorText = last.ErrorMessage;
            }
            else if (String.Equals(evt.Type, PiStreamEvent.TypeAutoRetryEnd, StringComparison.Ordinal) && evt.Success == false)
            {
                errorText = evt.FinalError;
            }
            else
            {
                return null;
            }

            RuntimeProviderError error = new RuntimeProviderError();
            string text = String.IsNullOrWhiteSpace(errorText) ? String.Empty : errorText!.Trim();
            if (text.Length > 4000) text = text.Substring(0, 4000);
            error.Message = text.Length > 0 ? Shorten(text) : null;
            error.Reason = "pi reported a provider error" + (error.Message != null ? ": " + error.Message : String.Empty);
            if (text.Length == 0) return error;

            Match status = _PiStatusMessage.Match(text);
            if (status.Success)
            {
                error.HttpStatusCode = Int32.Parse(status.Groups["status"].Value, CultureInfo.InvariantCulture);
                if (status.Groups["body"].Success)
                {
                    ProviderErrorEnvelope? envelope = DeserializeEnvelope(status.Groups["body"].Value);
                    if (envelope?.Error != null)
                    {
                        error.ErrorType = envelope.Error.Type;
                        if (!String.IsNullOrWhiteSpace(envelope.Error.Message)) error.Message = envelope.Error.Message;
                    }
                }

                return error;
            }

            RuntimeProviderError? cli = TryParseCliStderrLine(text, "pi");
            if (cli != null)
            {
                cli.Reason = error.Reason;
                return cli;
            }

            return error;
        }

        #endregion

        #region Private-Methods

        private static string Shorten(string text)
        {
            string flat = text.Replace("\r", " ").Replace("\n", " ").Trim();
            return flat.Length > 300 ? flat.Substring(0, 297).TrimEnd() + "..." : flat;
        }

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

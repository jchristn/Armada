namespace Armada.Runtimes
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Services;
    using PolyPrompt.Models;

    /// <summary>
    /// Builds the corrective turn and the varied completion options for a retry after a malformed tool call or a
    /// degenerate generation (RS-7.3), so the request is not resent unchanged.
    /// </summary>
    public static class MalformedCallCorrection
    {
        #region Public-Members

        /// <summary>
        /// Tool names listed in the corrective turn before the rest are counted.
        /// </summary>
        public const int MaxListedTools = 40;

        /// <summary>
        /// Highest temperature a corrective retry raises to.
        /// </summary>
        public const double MaxRetryTemperature = 1.0;

        /// <summary>
        /// Temperature a corrective retry starts from when the endpoint configures none.
        /// </summary>
        public const double DefaultStartTemperature = 0.6;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether a failed response is a malformed tool call or a degenerate generation.
        /// </summary>
        /// <param name="response">The response.</param>
        /// <returns>True when the request should be corrected rather than resent.</returns>
        /// <exception cref="ArgumentNullException">Thrown when response is null.</exception>
        public static bool Applies(ChatResponse response)
        {
            if (response == null) throw new ArgumentNullException(nameof(response));
            if (response.Success) return false;
            return OpenModelErrorPatterns.IsMalformedOrDegenerateGeneration(response.StatusCode, response.Error);
        }

        /// <summary>
        /// The corrective user turn: for a repeat loop, an instruction not to repeat; otherwise the valid tool names and
        /// how arguments must be shaped.
        /// </summary>
        /// <param name="error">The provider's error text, or null.</param>
        /// <param name="tools">Tool schemas offered this turn.</param>
        /// <returns>The message text.</returns>
        /// <exception cref="ArgumentNullException">Thrown when tools is null.</exception>
        public static string Message(string? error, IReadOnlyList<ToolDefinition> tools)
        {
            if (tools == null) throw new ArgumentNullException(nameof(tools));
            string detail = Summarize(error);
            if (IsRepeatLoop(error))
            {
                return "Your last reply repeated the same text until the server stopped it (" + detail + "). Do not repeat "
                    + "yourself. Continue the task with a short reply or the next tool call.";
            }

            List<string> names = tools.Select(t => t.Name).Where(n => !String.IsNullOrWhiteSpace(n)).ToList();
            string listed = names.Count == 0
                ? "none, so reply in text"
                : String.Join(", ", names.Take(MaxListedTools)) + (names.Count > MaxListedTools ? " and " + (names.Count - MaxListedTools) + " more" : String.Empty);
            return "The server could not parse your last tool call (" + detail + "). Call one tool at a time through the "
                + "tool-calling interface, using only these tool names: " + listed + ". The arguments must be one complete "
                + "JSON object with double-quoted keys that matches the tool's parameter schema, for example "
                + "{\"path\": \"src/app.cs\"}. Do not write tool calls as text.";
        }

        /// <summary>
        /// The completion options for corrective retry number <paramref name="attempt"/>: the same options with the
        /// temperature raised by 0.2 per attempt from the configured value (<see cref="DefaultStartTemperature"/> when
        /// none is configured), capped at <see cref="MaxRetryTemperature"/>. A configured temperature above the cap is
        /// kept as it is.
        /// </summary>
        /// <param name="options">The run's completion options.</param>
        /// <param name="attempt">1-based retry number.</param>
        /// <returns>A copy with the varied temperature.</returns>
        /// <exception cref="ArgumentNullException">Thrown when options is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when attempt is less than 1.</exception>
        public static CompletionOptions Vary(CompletionOptions options, int attempt)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (attempt < 1) throw new ArgumentOutOfRangeException(nameof(attempt));
            double start = Math.Max(0.0, options.Temperature ?? DefaultStartTemperature);
            double temperature = start > MaxRetryTemperature ? start : Math.Min(MaxRetryTemperature, start + 0.2 * attempt);
            return new CompletionOptions
            {
                Model = options.Model,
                MaxTokens = options.MaxTokens,
                Temperature = Math.Round(temperature, 2),
                TopP = options.TopP,
                ReasoningEffort = options.ReasoningEffort,
                SystemPrompt = options.SystemPrompt
            };
        }

        #endregion

        #region Private-Methods

        private static bool IsRepeatLoop(string? error)
        {
            if (String.IsNullOrEmpty(error)) return false;
            string lower = error.ToLowerInvariant();
            return lower.Contains("repeat") || lower.Contains("degenerate");
        }

        private static string Summarize(string? error)
        {
            if (String.IsNullOrWhiteSpace(error)) return "no detail";
            string flat = error.Replace("\r", " ").Replace("\n", " ").Trim();
            return flat.Length > 200 ? flat.Substring(0, 200) + "..." : flat;
        }

        #endregion
    }
}

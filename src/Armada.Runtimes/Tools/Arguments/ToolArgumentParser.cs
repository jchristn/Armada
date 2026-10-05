namespace Armada.Runtimes.Tools.Arguments
{
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.Text.Json;

    /// <summary>
    /// Converts the raw JSON arguments of a built-in tool call into a strongly typed arguments instance.
    /// Wrong-typed values, malformed shapes, and missing required parameters all become a failure that the
    /// tool reports with the <see cref="InvalidParameterCode"/> error code.
    /// </summary>
    public static class ToolArgumentParser
    {
        #region Public-Members

        /// <summary>
        /// The error code carried in the result content when tool arguments are invalid.
        /// </summary>
        public const string InvalidParameterCode = "invalid_parameter";

        #endregion

        #region Private-Members

        private static readonly JsonSerializerOptions _Options = new JsonSerializerOptions();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Deserializes the arguments into <typeparamref name="T"/> and validates them.
        /// </summary>
        /// <typeparam name="T">The typed arguments class for the tool.</typeparam>
        /// <param name="arguments">The raw JSON arguments supplied by the model.</param>
        /// <param name="value">The typed arguments when parsing succeeds; otherwise null.</param>
        /// <param name="error">A description of the problem when parsing fails; otherwise null.</param>
        /// <returns>True when the arguments were parsed and are valid.</returns>
        public static bool TryParse<T>(JsonElement arguments, [NotNullWhen(true)] out T? value, [NotNullWhen(false)] out string? error)
            where T : class, IToolArguments
        {
            value = null;
            error = null;

            T? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<T>(arguments, _Options);
            }
            catch (JsonException ex)
            {
                error = String.IsNullOrEmpty(ex.Path)
                    ? "Tool arguments are malformed: " + ex.Message
                    : "Parameter at '" + ex.Path + "' has an invalid type or value: " + ex.Message;
                return false;
            }
            catch (InvalidOperationException)
            {
                error = "Tool arguments are missing.";
                return false;
            }

            if (parsed == null)
            {
                error = "Tool arguments must be a JSON object.";
                return false;
            }

            string? problem = parsed.Validate();
            if (problem != null)
            {
                error = problem;
                return false;
            }

            value = parsed;
            return true;
        }

        /// <summary>
        /// Builds the failed result returned when tool arguments are invalid.
        /// </summary>
        /// <param name="toolCallId">The identifier of the tool call.</param>
        /// <param name="message">A description of the problem.</param>
        /// <returns>A failed <see cref="ToolResult"/> whose content carries the <see cref="InvalidParameterCode"/> error code.</returns>
        public static ToolResult InvalidParameter(string toolCallId, string message)
        {
            ToolErrorContent content = new ToolErrorContent
            {
                Error = InvalidParameterCode,
                Message = message ?? String.Empty
            };

            return new ToolResult
            {
                ToolCallId = toolCallId,
                Success = false,
                Content = JsonSerializer.Serialize(content)
            };
        }

        /// <summary>
        /// Returns a "missing required parameter" message when <paramref name="value"/> is null.
        /// </summary>
        /// <param name="value">The parameter value.</param>
        /// <param name="name">The JSON name of the parameter.</param>
        /// <returns>Null when the value is present; otherwise the problem message.</returns>
        public static string? Required(object? value, string name)
        {
            if (value == null) return "Required parameter '" + name + "' is missing.";
            return null;
        }

        #endregion
    }
}

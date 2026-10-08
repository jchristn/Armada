namespace Armada.Core.Hosting
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using Armada.Core.Settings;

    /// <summary>
    /// Checks settings text before an editor saves it, so a typo cannot leave the Admiral (or the terminal UI) unable
    /// to start. Each check returns a message for the operator, or null when the text is acceptable.
    /// </summary>
    public static class SettingsTextValidator
    {
        #region Public-Methods

        /// <summary>
        /// Check Admiral settings JSON: it must parse as <see cref="ArmadaSettings"/> exactly as the Admiral loads
        /// it, with every value in range.
        /// </summary>
        /// <param name="json">Text.</param>
        /// <returns>Problem description, or null when valid.</returns>
        public static string? ValidateArmadaSettings(string json)
        {
            string? syntax = ValidateJsonObject(json);
            if (syntax != null) return syntax;

            try
            {
                ArmadaSettings.Parse(json);
                return null;
            }
            catch (JsonException ex)
            {
                return Describe(ex);
            }
            catch (ArgumentException ex)
            {
                return ex.Message;
            }
            catch (InvalidOperationException ex)
            {
                return ex.Message;
            }
            catch (NotSupportedException ex)
            {
                return ex.Message;
            }
        }

        /// <summary>
        /// Check that text is a JSON object (comments and trailing commas are not allowed).
        /// </summary>
        /// <param name="json">Text.</param>
        /// <returns>Problem description, or null when valid.</returns>
        public static string? ValidateJsonObject(string json)
        {
            if (String.IsNullOrWhiteSpace(json)) return "The file is empty; it must hold a JSON object ({ ... }).";

            try
            {
                Dictionary<string, object>? root = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                if (root == null) return "The file must hold a JSON object ({ ... }), not null.";
                return null;
            }
            catch (JsonException ex)
            {
                return Describe(ex);
            }
        }

        #endregion

        #region Private-Methods

        private static string Describe(JsonException ex)
        {
            if (ex.LineNumber.HasValue)
            {
                long line = ex.LineNumber.Value + 1;
                long column = (ex.BytePositionInLine ?? 0) + 1;
                string message = ex.Message;
                // The position is reported above in one-based form; drop the zero-based copy at the end.
                int positionIndex = message.IndexOf(" | LineNumber:", StringComparison.Ordinal);
                if (positionIndex > 0) message = message.Substring(0, positionIndex);
                return "Line " + line + ", column " + column + ": " + message;
            }

            return ex.Message;
        }

        #endregion
    }
}

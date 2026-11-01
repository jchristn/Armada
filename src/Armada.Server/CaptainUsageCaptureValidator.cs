namespace Armada.Server
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Validates a captain's usage capture configuration before it is stored. Custom mode requires an
    /// executable-and-argument-list command (JSON object with a non-empty executable and an optional string
    /// array of arguments) that contains no shell syntax, matching the host runner's Custom contract.
    /// </summary>
    public static class CaptainUsageCaptureValidator
    {
        #region Private-Members

        private static readonly char[] _ShellSyntax = new char[] { ';', '|', '&', '<', '>', '`', '\'', '"' };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Validate the captain's usage capture configuration.
        /// </summary>
        /// <param name="captain">Captain to validate.</param>
        /// <returns>Null when valid; otherwise a message naming the problem.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="captain"/> is null.</exception>
        public static string? Validate(Captain captain)
        {
            if (captain == null) throw new ArgumentNullException(nameof(captain));
            if (captain.UsageCaptureMode != UsageCaptureModeEnum.Custom) return null;

            string? json = captain.UsageCaptureCommand;
            if (String.IsNullOrWhiteSpace(json))
                return "UsageCaptureCommandRequired: Custom usage capture needs a command with an executable and an argument list.";

            CustomCommandShape? shape;
            try
            {
                shape = JsonSerializer.Deserialize<CustomCommandShape>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException)
            {
                return "UsageCaptureCommandInvalid: the Custom command must be a JSON object with an executable and an arguments array.";
            }

            string executable = (shape?.Executable ?? String.Empty).Trim();
            if (executable.Length == 0 || executable.Contains('\0'))
                return "UsageCaptureCommandInvalid: the Custom command must name an executable.";
            if (executable.IndexOfAny(_ShellSyntax) >= 0 || executable.Contains("$("))
                return "UsageCaptureCommandInvalid: the Custom command contains shell syntax; use an executable and an argument list.";
            if (shape!.Arguments != null)
            {
                foreach (string? argument in shape.Arguments)
                {
                    if (argument == null || argument.Contains('\0'))
                        return "UsageCaptureCommandInvalid: every Custom command argument must be a string.";
                }
            }

            return null;
        }

        /// <summary>
        /// Whether two captain records differ in any usage capture field that affects scheduling or probing.
        /// </summary>
        /// <param name="before">Stored record.</param>
        /// <param name="after">Record about to be stored.</param>
        /// <returns>True when the enabled switch, interval, mode or command changed.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static bool ConfigurationChanged(Captain before, Captain after)
        {
            if (before == null) throw new ArgumentNullException(nameof(before));
            if (after == null) throw new ArgumentNullException(nameof(after));
            return before.UsageCaptureEnabled != after.UsageCaptureEnabled
                || before.UsageCaptureIntervalSeconds != after.UsageCaptureIntervalSeconds
                || before.UsageCaptureMode != after.UsageCaptureMode
                || !String.Equals(before.UsageCaptureCommand, after.UsageCaptureCommand, StringComparison.Ordinal);
        }

        #endregion

        #region Private-Classes

        private sealed class CustomCommandShape
        {
            public string? Executable { get; set; } = null;
            public List<string?>? Arguments { get; set; } = null;
        }

        #endregion
    }
}

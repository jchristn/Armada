namespace Armada.Helm.Commands
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Parsing and lookup shared by the <c>armada blackbox</c> commands: relative and absolute <c>--since</c> values,
    /// case-insensitive enum options, which route an id names (mission, voyage, or entry), and the repository's fixture
    /// directory for <c>export-fixture</c>.
    /// </summary>
    public static class BlackBoxCli
    {
        #region Public-Members

        /// <summary>
        /// Exit code when the black box (or the named entry, object, or path) does not exist (HTTP 404).
        /// </summary>
        public const int NotFoundExitCode = 3;

        /// <summary>
        /// Exit code when the command refused to act (missing --execute confirmation or data-loss acknowledgement).
        /// </summary>
        public const int RefusedExitCode = 4;

        /// <summary>
        /// The fixture directory under the repository root.
        /// </summary>
        public static readonly string FixtureRelativePath = Path.Combine("src", "Test.Shared", "Fixtures", "BlackBox");

        #endregion

        #region Public-Methods

        /// <summary>
        /// Parse <c>--since</c>: a duration (<c>30d</c>, <c>12h</c>, <c>90m</c>, <c>2w</c>) back from <paramref name="nowUtc"/>,
        /// or an ISO 8601 date or date-time (UTC unless it says otherwise).
        /// </summary>
        /// <param name="value">Text.</param>
        /// <param name="nowUtc">Now (UTC).</param>
        /// <returns>The instant (UTC).</returns>
        /// <exception cref="ArgumentException">Thrown when the value is neither.</exception>
        public static DateTime ParseSince(string value, DateTime nowUtc)
        {
            if (String.IsNullOrWhiteSpace(value)) throw new ArgumentException("--since needs a value such as 30d, 12h, or 2026-09-01.", nameof(value));
            string v = value.Trim();
            char unit = Char.ToLowerInvariant(v[v.Length - 1]);
            if ("mhdw".IndexOf(unit) >= 0 && Int32.TryParse(v.Substring(0, v.Length - 1), NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n > 0)
            {
                switch (unit)
                {
                    case 'm': return nowUtc.AddMinutes(-n);
                    case 'h': return nowUtc.AddHours(-n);
                    case 'd': return nowUtc.AddDays(-n);
                    default: return nowUtc.AddDays(-7 * n);
                }
            }

            if (DateTime.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime at)) return at;
            throw new ArgumentException("--since '" + value + "' is not a duration (30d, 12h, 90m, 2w) or a date (2026-09-01).", nameof(value));
        }

        /// <summary>
        /// Parse an enum option case-insensitively, ignoring dashes and underscores (<c>user-config</c> is UserConfig).
        /// </summary>
        /// <typeparam name="TEnum">Enum.</typeparam>
        /// <param name="option">Option name for the message.</param>
        /// <param name="value">Text, or null.</param>
        /// <returns>The value, or null for an empty value.</returns>
        /// <exception cref="ArgumentException">Thrown for an unknown value (the message lists the valid ones).</exception>
        public static TEnum? ParseEnum<TEnum>(string option, string? value) where TEnum : struct, Enum
        {
            if (String.IsNullOrWhiteSpace(value)) return null;
            string wanted = value.Replace("-", "").Replace("_", "").Trim();
            foreach (TEnum candidate in Enum.GetValues(typeof(TEnum)))
            {
                if (String.Equals(candidate.ToString(), wanted, StringComparison.OrdinalIgnoreCase)) return candidate;
            }

            throw new ArgumentException(option + " '" + value + "' is not one of: " + String.Join(", ", Enum.GetNames(typeof(TEnum))) + ".");
        }

        /// <summary>
        /// Which route an id names: <c>msn_</c> is a mission, <c>vyg_</c> a voyage, anything else an entry.
        /// </summary>
        /// <param name="id">Id.</param>
        /// <returns>Subject type, or null for an entry id.</returns>
        public static BlackBoxSubjectTypeEnum? SubjectOf(string id)
        {
            if (String.IsNullOrEmpty(id)) return null;
            if (id.StartsWith("msn_", StringComparison.Ordinal)) return BlackBoxSubjectTypeEnum.Mission;
            if (id.StartsWith("vyg_", StringComparison.Ordinal)) return BlackBoxSubjectTypeEnum.Voyage;
            return null;
        }

        /// <summary>
        /// The fixture directory of the Armada repository that contains <paramref name="startDirectory"/>, or null when
        /// it is not inside one (a directory with <c>src/Armada.sln</c> and <c>src/Test.Shared</c>).
        /// </summary>
        /// <param name="startDirectory">Where to start looking (the current directory).</param>
        /// <returns>Directory, or null.</returns>
        public static string? FindFixtureDirectory(string startDirectory)
        {
            if (String.IsNullOrEmpty(startDirectory)) return null;
            DirectoryInfo? dir = new DirectoryInfo(startDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "src", "Armada.sln")) && Directory.Exists(Path.Combine(dir.FullName, "src", "Test.Shared")))
                    return Path.Combine(dir.FullName, FixtureRelativePath);
                dir = dir.Parent;
            }

            return null;
        }

        /// <summary>
        /// Build the enumerate filters from the shared filter options.
        /// </summary>
        /// <typeparam name="TRequest">Request type.</typeparam>
        /// <param name="request">Request to fill.</param>
        /// <param name="settings">Options.</param>
        /// <param name="nowUtc">Now (UTC) for <c>--since</c>.</param>
        /// <returns>The request.</returns>
        /// <exception cref="ArgumentException">Thrown for an invalid option value.</exception>
        public static TRequest ApplyFilters<TRequest>(TRequest request, BlackBoxFilterSettings settings, DateTime nowUtc) where TRequest : BlackBoxEnumerateRequest
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (!String.IsNullOrWhiteSpace(settings.Vessel)) request.VesselId = settings.Vessel.Trim();
            if (!String.IsNullOrWhiteSpace(settings.Voyage)) request.VoyageId = settings.Voyage.Trim();
            if (!String.IsNullOrWhiteSpace(settings.Mission)) request.MissionId = settings.Mission.Trim();
            if (!String.IsNullOrWhiteSpace(settings.Captain)) request.CaptainId = settings.Captain.Trim();
            request.Kind = ParseEnum<MissionFailureKindEnum>("--kind", settings.Kind);
            request.Triage = ParseEnum<BlackBoxTriageEnum>("--triage", settings.Triage);
            request.Phase = ParseEnum<BlackBoxPhaseEnum>("--phase", settings.Phase);
            request.Step = ParseEnum<BlackBoxStepEnum>("--step", settings.Step);
            request.Resolution = ParseEnum<BlackBoxResolutionEnum>("--resolution", settings.Resolution);
            if (!String.IsNullOrWhiteSpace(settings.Fingerprint)) request.Fingerprint = settings.Fingerprint.Trim();
            if (settings.Pinned) request.Pinned = true;
            if (!String.IsNullOrWhiteSpace(settings.Since)) request.CreatedAfter = ParseSince(settings.Since, nowUtc);
            if (!String.IsNullOrWhiteSpace(settings.Before)) request.CreatedBefore = ParseSince(settings.Before, nowUtc);
            return request;
        }

        /// <summary>
        /// A byte count in B, KB, or MB.
        /// </summary>
        /// <param name="bytes">Bytes.</param>
        /// <returns>Text.</returns>
        public static string Bytes(long bytes)
        {
            if (bytes < 1024) return bytes.ToString(CultureInfo.InvariantCulture) + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " KB";
            return (bytes / (1024.0 * 1024.0)).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
        }

        #endregion
    }
}

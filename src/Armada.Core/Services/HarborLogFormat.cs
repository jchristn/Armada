namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Formatting for the Harbor activity log's summary view: short IDs, short paths, what an entry is about, command
    /// text, and durations. Works from typed fields; nothing here reads a log message.
    /// </summary>
    public static class HarborLogFormat
    {
        #region Public-Members

        /// <summary>
        /// Characters of an ID kept by <see cref="ShortId"/> (for example "msn_mv092791" of "msn_mv092791_kEYKdgFrByX").
        /// </summary>
        public const int ShortIdLength = 12;

        /// <summary>
        /// Longest command text shown in a summary line; longer commands are cut with "...".
        /// </summary>
        public const int MaxCommandLength = 120;

        #endregion

        #region Public-Methods

        /// <summary>
        /// The first <see cref="ShortIdLength"/> characters of an ID; an ID that short or shorter is returned whole.
        /// </summary>
        /// <param name="id">ID, or null.</param>
        /// <returns>Short ID; empty for null.</returns>
        public static string ShortId(string? id)
        {
            string text = (id ?? String.Empty).Trim();
            return text.Length <= ShortIdLength ? text : text.Substring(0, ShortIdLength);
        }

        /// <summary>
        /// What an entry is about, for the start of a summary line: "DocConverter msn_mv092791" for a mission dock, the
        /// vessel or the short mission alone, or null when neither is known.
        /// </summary>
        /// <param name="vesselName">Vessel name, or null.</param>
        /// <param name="missionId">Mission ID, or null.</param>
        /// <returns>Label, or null.</returns>
        public static string? SubjectLabel(string? vesselName, string? missionId)
        {
            bool hasVessel = !String.IsNullOrWhiteSpace(vesselName);
            bool hasMission = !String.IsNullOrWhiteSpace(missionId);
            if (hasVessel && hasMission) return vesselName!.Trim() + " " + ShortId(missionId);
            if (hasVessel) return vesselName!.Trim();
            if (hasMission) return ShortId(missionId);
            return null;
        }

        /// <summary>
        /// A directory as the summary view shows it: a mission dock as "DocConverter/msn_mv092791...", a vessel's checkout
        /// as the vessel's name, a scratch directory as "scratch", and anything else with the user's home folder written
        /// as "~".
        /// </summary>
        /// <param name="path">Directory, or null.</param>
        /// <param name="kind">What the directory is.</param>
        /// <param name="vesselName">Vessel name, or null.</param>
        /// <param name="missionId">Mission ID (a dock's mission), or null.</param>
        /// <returns>Short form; empty when there is no directory.</returns>
        public static string ShortPath(string? path, HarborLogPathKindEnum kind, string? vesselName, string? missionId)
        {
            if (kind == HarborLogPathKindEnum.Scratch) return "scratch";
            if (kind == HarborLogPathKindEnum.Dock && !String.IsNullOrWhiteSpace(vesselName) && !String.IsNullOrWhiteSpace(missionId))
                return vesselName!.Trim() + "/" + ShortId(missionId) + "...";
            if (kind == HarborLogPathKindEnum.Checkout && !String.IsNullOrWhiteSpace(vesselName)) return vesselName!.Trim();
            return HomeRelative(path);
        }

        /// <summary>
        /// A path with the user's home folder written as "~" (unchanged when it is not under the home folder).
        /// </summary>
        /// <param name="path">Path, or null.</param>
        /// <returns>Path; empty for null.</returns>
        public static string HomeRelative(string? path)
        {
            if (String.IsNullOrWhiteSpace(path)) return String.Empty;
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (String.IsNullOrEmpty(home)) return path!;
            string trimmedHome = home.TrimEnd('/', '\\');
            if (String.Equals(path, trimmedHome, StringComparison.Ordinal)) return "~";
            if (path!.Length > trimmedHome.Length
                && path.StartsWith(trimmedHome, StringComparison.Ordinal)
                && (path[trimmedHome.Length] == '/' || path[trimmedHome.Length] == '\\'))
                return "~" + path.Substring(trimmedHome.Length);
            return path;
        }

        /// <summary>
        /// A command line for a summary line: the executable and its arguments, cut to <see cref="MaxCommandLength"/>.
        /// </summary>
        /// <param name="executable">Executable.</param>
        /// <param name="arguments">Arguments, or null.</param>
        /// <returns>Command text.</returns>
        public static string Command(string executable, List<string>? arguments)
        {
            string text = (executable ?? String.Empty).Trim();
            if (arguments != null && arguments.Count > 0) text += " " + String.Join(" ", arguments);
            return Truncate(text, MaxCommandLength);
        }

        /// <summary>
        /// A duration as "850ms", "36.7s", or "2m5s".
        /// </summary>
        /// <param name="milliseconds">Duration in milliseconds.</param>
        /// <returns>Text.</returns>
        public static string Duration(long milliseconds)
        {
            if (milliseconds < 1000) return milliseconds + "ms";
            double seconds = milliseconds / 1000.0;
            if (seconds < 60) return seconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "s";
            long totalSeconds = milliseconds / 1000;
            return (totalSeconds / 60) + "m" + (totalSeconds % 60) + "s";
        }

        /// <summary>
        /// Text cut to a length, ending with "..." when cut.
        /// </summary>
        /// <param name="text">Text, or null.</param>
        /// <param name="maxLength">Maximum length (at least 4).</param>
        /// <returns>Text.</returns>
        public static string Truncate(string? text, int maxLength)
        {
            string value = text ?? String.Empty;
            int max = maxLength < 4 ? 4 : maxLength;
            return value.Length <= max ? value : value.Substring(0, max - 3) + "...";
        }

        /// <summary>
        /// A count with its noun, singular or plural: "1 git command", "14 git commands".
        /// </summary>
        /// <param name="count">Count.</param>
        /// <param name="singular">Singular noun.</param>
        /// <param name="plural">Plural noun.</param>
        /// <returns>Text.</returns>
        public static string Count(int count, string singular, string plural)
        {
            return count + " " + (count == 1 ? singular : plural);
        }

        #endregion
    }
}

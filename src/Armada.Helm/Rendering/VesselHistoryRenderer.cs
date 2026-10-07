namespace Armada.Helm.Rendering
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;
    using Spectre.Console;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Spectre markup for <c>armada vessel history</c>: the commit activity heatmap (weeks as columns, Sunday to
    /// Saturday as rows, five intensity levels scaled by the busiest day) and the commit list grouped by local day.
    /// Pure: every method returns markup lines and writes nothing.
    /// </summary>
    public static class VesselHistoryRenderer
    {
        #region Private-Members

        private static readonly string[] _LevelColors = new string[] { "grey30", "darkgreen", "green4", "green3", "green1" };
        private static readonly char[] _AsciiLevels = new char[] { '.', '-', '+', '*', '#' };
        private const string _UnicodeCell = "■";
        private const int _RowLabelWidth = 4;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Intensity level of a day, 0 (no commits) to 4 (the busiest days), scaled by the range's largest day count.
        /// </summary>
        /// <param name="count">Commits on the day.</param>
        /// <param name="maxDayCount">Largest day count in the range.</param>
        /// <returns>Level 0 to 4.</returns>
        public static int Level(int count, int maxDayCount)
        {
            if (count <= 0 || maxDayCount <= 0) return 0;
            int level = (int)Math.Ceiling(count * 4.0 / maxDayCount);
            return Math.Max(1, Math.Min(4, level));
        }

        /// <summary>
        /// The heatmap: a month label row, seven weekday rows, a legend, and totals.
        /// </summary>
        /// <param name="activity">Activity from GET /api/v1/vessels/{id}/history/activity.</param>
        /// <param name="colors">True to draw one glyph in five colors; false to draw five distinct ASCII characters.</param>
        /// <param name="unicode">True to draw a square glyph when colors are on; false for '#'.</param>
        /// <param name="rangeLabel">Totals wording, for example "in the last year".</param>
        /// <param name="zone">Time zone for the first and last commit dates.</param>
        /// <returns>Markup lines.</returns>
        public static List<string> HeatmapLines(VesselCommitActivity activity, bool colors, bool unicode, string rangeLabel, TimeZoneInfo zone)
        {
            if (activity == null) throw new ArgumentNullException(nameof(activity));
            if (zone == null) throw new ArgumentNullException(nameof(zone));
            List<string> lines = new List<string>();
            if (!TryParseDay(activity.From, out DateTime from) || !TryParseDay(activity.To, out DateTime to) || to < from) return lines;

            Dictionary<DateTime, int> counts = new Dictionary<DateTime, int>();
            foreach (VesselCommitActivityDay day in activity.Days)
            {
                if (TryParseDay(day.Date, out DateTime date)) counts[date] = day.Count;
            }

            DateTime start = from.AddDays(-(int)from.DayOfWeek);
            int weeks = ((to - start).Days / 7) + 1;

            // Month labels: at the column of the week that holds the 1st (or the first column), when there is room.
            char[] header = new string(' ', _RowLabelWidth + (weeks * 2) + 3).ToCharArray();
            int nextFree = 0;
            for (int w = 0; w < weeks; w++)
            {
                DateTime? labelMonth = null;
                for (int d = 0; d < 7 && labelMonth == null; d++)
                {
                    DateTime date = start.AddDays((w * 7) + d);
                    if (date >= from && date <= to && date.Day == 1) labelMonth = date;
                }

                // The first column carries the starting month only when at least two more weeks of it are shown.
                if (labelMonth == null && w == 0 && from.AddDays(14).Month == from.Month) labelMonth = from;
                if (labelMonth == null) continue;
                int column = _RowLabelWidth + (w * 2);
                if (column < nextFree) continue;
                string label = labelMonth.Value.ToString("MMM", CultureInfo.InvariantCulture);
                for (int c = 0; c < label.Length && column + c < header.Length; c++) header[column + c] = label[c];
                nextFree = column + label.Length + 1;
            }
            lines.Add(Markup.Escape(new string(header).TrimEnd()));

            string[] rowLabels = new string[] { "", "Mon", "", "Wed", "", "Fri", "" };
            for (int r = 0; r < 7; r++)
            {
                StringBuilder row = new StringBuilder();
                row.Append(rowLabels[r].PadRight(_RowLabelWidth));
                for (int w = 0; w < weeks; w++)
                {
                    DateTime date = start.AddDays((w * 7) + r);
                    if (date < from || date > to)
                    {
                        row.Append("  ");
                        continue;
                    }

                    int count = counts.TryGetValue(date, out int found) ? found : 0;
                    row.Append(Cell(Level(count, activity.MaxDayCount), colors, unicode));
                    row.Append(' ');
                }
                lines.Add(row.ToString().TrimEnd());
            }

            StringBuilder legend = new StringBuilder();
            legend.Append(new string(' ', _RowLabelWidth)).Append("[dim]Less[/] ");
            for (int level = 0; level <= 4; level++) legend.Append(Cell(level, colors, unicode)).Append(' ');
            legend.Append("[dim]More[/]");
            lines.Add(legend.ToString());
            lines.Add(String.Empty);

            string commits = activity.TotalCommits == 1 ? "commit" : "commits";
            lines.Add("[bold]" + activity.TotalCommits.ToString(CultureInfo.InvariantCulture) + "[/] " + commits + " " + Markup.Escape(rangeLabel)
                + " [dim]on " + Markup.Escape(activity.Branch) + "[/]");

            DateTime? busiest = null;
            int busiestCount = 0;
            foreach (KeyValuePair<DateTime, int> pair in counts)
            {
                if (pair.Value > busiestCount || (pair.Value == busiestCount && busiest.HasValue && pair.Key > busiest.Value && pair.Value > 0))
                {
                    busiest = pair.Key;
                    busiestCount = pair.Value;
                }
            }
            if (busiest.HasValue && busiestCount > 0)
                lines.Add("[dim]Busiest day:[/] " + busiest.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " (" + busiestCount.ToString(CultureInfo.InvariantCulture) + ")");

            if (activity.FirstCommitUtc.HasValue && activity.LastCommitUtc.HasValue)
            {
                lines.Add("[dim]First commit:[/] " + LocalDate(activity.FirstCommitUtc.Value, zone)
                    + "  [dim]Last commit:[/] " + LocalDate(activity.LastCommitUtc.Value, zone));
            }

            return lines;
        }

        /// <summary>
        /// Local calendar day key (yyyy-MM-dd) of a UTC instant, for grouping commits by day.
        /// </summary>
        /// <param name="utc">UTC instant.</param>
        /// <param name="zone">Time zone.</param>
        /// <returns>Day key.</returns>
        public static string LocalDate(DateTime utc, TimeZoneInfo zone)
        {
            return ToLocal(utc, zone).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Day header line for the commit list, for example "Tuesday, 2026-10-06".
        /// </summary>
        /// <param name="utc">A commit date on that day (UTC).</param>
        /// <param name="zone">Time zone.</param>
        /// <returns>Markup line.</returns>
        public static string DayHeader(DateTime utc, TimeZoneInfo zone)
        {
            DateTime local = ToLocal(utc, zone);
            return "[bold dodgerblue1]" + local.ToString("dddd, yyyy-MM-dd", CultureInfo.InvariantCulture) + "[/]";
        }

        /// <summary>
        /// One commit: short SHA and subject, then author, local time, relative time, line counts, and files changed;
        /// with verbose, the body and the changed files.
        /// </summary>
        /// <param name="commit">Commit.</param>
        /// <param name="zone">Time zone.</param>
        /// <param name="nowUtc">Current time, for the relative age.</param>
        /// <param name="verbose">Include the body and file list.</param>
        /// <returns>Markup lines.</returns>
        public static List<string> CommitLines(VesselCommit commit, TimeZoneInfo zone, DateTime nowUtc, bool verbose)
        {
            if (commit == null) throw new ArgumentNullException(nameof(commit));
            if (zone == null) throw new ArgumentNullException(nameof(zone));
            List<string> lines = new List<string>();
            string merge = commit.IsMerge ? " [dim](merge)[/]" : String.Empty;
            lines.Add("  [yellow]" + Markup.Escape(commit.ShortSha) + "[/] " + Markup.Escape(commit.Subject) + merge);

            string time = ToLocal(commit.CommittedUtc, zone).ToString("HH:mm", CultureInfo.InvariantCulture);
            string files = commit.FilesChanged == 1 ? "1 file" : commit.FilesChanged.ToString(CultureInfo.InvariantCulture) + " files";
            lines.Add("          [dim]" + Markup.Escape(commit.AuthorName) + ", " + time + " (" + Relative(commit.CommittedUtc, nowUtc) + ")[/]  "
                + "[green]+" + commit.AddedLines.ToString(CultureInfo.InvariantCulture) + "[/] "
                + "[red]-" + commit.DeletedLines.ToString(CultureInfo.InvariantCulture) + "[/]  [dim]" + files + "[/]");

            if (!verbose) return lines;

            if (!String.IsNullOrWhiteSpace(commit.Body))
            {
                foreach (string bodyLine in commit.Body.Replace("\r\n", "\n").Split('\n'))
                    lines.Add("          " + Markup.Escape(bodyLine));
            }

            foreach (GitChangedFile file in commit.Files)
            {
                string path = file.OldPath != null ? file.OldPath + " -> " + file.Path : file.Path;
                string stats = file.IsBinary
                    ? "[dim](binary)[/]"
                    : "[green]+" + (file.AddedLines ?? 0).ToString(CultureInfo.InvariantCulture) + "[/] [red]-" + (file.DeletedLines ?? 0).ToString(CultureInfo.InvariantCulture) + "[/]";
                lines.Add("          " + KindLetter(file.Kind) + " " + Markup.Escape(path) + " " + stats);
            }

            if (commit.FilesTruncated)
            {
                int more = commit.FilesChanged - commit.Files.Count;
                lines.Add("          [dim]... and " + more.ToString(CultureInfo.InvariantCulture) + " more files[/]");
            }

            return lines;
        }

        /// <summary>
        /// Relative age, for example "just now", "5 minutes ago", "3 days ago", "2 years ago".
        /// </summary>
        /// <param name="utc">Instant.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <returns>Text.</returns>
        public static string Relative(DateTime utc, DateTime nowUtc)
        {
            TimeSpan age = nowUtc - utc;
            if (age.TotalSeconds < 60) return "just now";
            if (age.TotalMinutes < 60) return Plural((int)age.TotalMinutes, "minute");
            if (age.TotalHours < 24) return Plural((int)age.TotalHours, "hour");
            if (age.TotalDays < 30) return Plural((int)age.TotalDays, "day");
            if (age.TotalDays < 365) return Plural((int)(age.TotalDays / 30), "month");
            return Plural((int)(age.TotalDays / 365), "year");
        }

        /// <summary>
        /// One-letter change kind (A, M, D, R, C, T, U, ?).
        /// </summary>
        /// <param name="kind">Kind.</param>
        /// <returns>Letter.</returns>
        public static string KindLetter(GitChangeKindEnum kind)
        {
            switch (kind)
            {
                case GitChangeKindEnum.Added: return "A";
                case GitChangeKindEnum.Modified: return "M";
                case GitChangeKindEnum.Deleted: return "D";
                case GitChangeKindEnum.Renamed: return "R";
                case GitChangeKindEnum.Copied: return "C";
                case GitChangeKindEnum.TypeChanged: return "T";
                case GitChangeKindEnum.Unmerged: return "U";
                default: return "?";
            }
        }

        #endregion

        #region Private-Methods

        private static string Cell(int level, bool colors, bool unicode)
        {
            if (!colors) return _AsciiLevels[level].ToString();
            return "[" + _LevelColors[level] + "]" + (unicode ? _UnicodeCell : "#") + "[/]";
        }

        private static bool TryParseDay(string value, out DateTime day)
        {
            return DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day);
        }

        private static DateTime ToLocal(DateTime utc, TimeZoneInfo zone)
        {
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);
        }

        private static string Plural(int value, string unit)
        {
            return value.ToString(CultureInfo.InvariantCulture) + " " + unit + (value == 1 ? "" : "s") + " ago";
        }

        #endregion
    }
}

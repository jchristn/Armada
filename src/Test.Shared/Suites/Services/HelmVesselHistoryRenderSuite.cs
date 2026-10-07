namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Helm.Rendering;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Rendering for <c>armada vessel history</c>: heatmap intensity levels, the 7-row grid with month labels in
    /// ASCII (colors off) and colored glyphs, totals, and commit lines with the file list.
    /// </summary>
    public sealed class HelmVesselHistoryRenderSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.HelmVesselHistoryRender";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("levels_scale_by_busiest_day", "Intensity is 0 for no commits and 1-4 scaled by the busiest day", TestTags.Positive, () =>
            {
                AssertEqual(0, VesselHistoryRenderer.Level(0, 10));
                AssertEqual(1, VesselHistoryRenderer.Level(1, 10));
                AssertEqual(2, VesselHistoryRenderer.Level(5, 10));
                AssertEqual(3, VesselHistoryRenderer.Level(7, 10));
                AssertEqual(4, VesselHistoryRenderer.Level(10, 10));
                AssertEqual(4, VesselHistoryRenderer.Level(1, 1));
                AssertEqual(0, VesselHistoryRenderer.Level(3, 0));
            }));

            cases.Add(Case("heatmap_ascii_grid", "Colors off: seven weekday rows (Sun..Sat) with distinct ASCII levels, week columns, month labels, legend, totals", TestTags.Positive, () =>
            {
                VesselCommitActivity activity = Activity("2026-01-25", "2026-02-21", new Dictionary<string, int> { ["2026-02-01"] = 4, ["2026-02-03"] = 1, ["2026-02-21"] = 2 });
                List<string> lines = VesselHistoryRenderer.HeatmapLines(activity, false, false, "in the last year", TimeZoneInfo.Utc);
                foreach (string line in lines) AssertTrue(line.All(ch => ch < 128), "ASCII only: " + line);

                AssertEqual("      Feb", lines[0], "Feb labels the week holding the 1st; January has under two weeks shown");
                AssertEqual("    . # . .", lines[1], "Sunday row: 01-25, 02-01 (busiest), 02-08, 02-15");
                AssertEqual("Mon . . . .", lines[2]);
                AssertEqual("    . - . .", lines[3], "Tuesday 02-03 has one commit of four");
                AssertEqual("    . . . +", lines[7], "Saturday 02-21 has two of four");
                AssertContains("Less", lines[8]);
                AssertContains(". - + * #", lines[8], "legend shows all five levels");
                AssertTrue(lines.Any(l => l.Contains("7[/] commits in the last year", StringComparison.Ordinal)), "totals: " + String.Join(" | ", lines));
                AssertTrue(lines.Any(l => l.Contains("2026-02-01 (4)", StringComparison.Ordinal)), "busiest day");
            }));

            cases.Add(Case("heatmap_colors_and_partial_weeks", "Colors on: one glyph in five colors; days outside the range are blank", TestTags.Positive, () =>
            {
                VesselCommitActivity activity = Activity("2026-02-04", "2026-02-10", new Dictionary<string, int> { ["2026-02-04"] = 3 });
                List<string> lines = VesselHistoryRenderer.HeatmapLines(activity, true, true, "from 2026-02-04 to 2026-02-10", TimeZoneInfo.Utc);
                AssertContains("[green1]\u25A0[/]", String.Join("\n", lines), "busiest day in the brightest color");
                AssertContains("[grey30]\u25A0[/]", String.Join("\n", lines), "empty day in grey");
                AssertTrue(lines[1].StartsWith("      [grey30]", StringComparison.Ordinal), "Sunday 02-01 is before the range, so its cell is blank: " + lines[1]);
                List<string> noUnicode = VesselHistoryRenderer.HeatmapLines(activity, true, false, "x", TimeZoneInfo.Utc);
                AssertContains("[green1]#[/]", String.Join("\n", noUnicode));
                AssertEqual(0, VesselHistoryRenderer.HeatmapLines(new VesselCommitActivity(), false, false, "x", TimeZoneInfo.Utc).Count, "no range, no lines");
            }));

            cases.Add(Case("commit_lines_and_files", "A commit renders its short SHA, subject, author, counts, and with verbose its body, files, renames, binaries, and truncation", TestTags.Positive, () =>
            {
                VesselCommit commit = new VesselCommit();
                commit.ShortSha = "abc1234";
                commit.Subject = "Fix [the] thing";
                commit.Body = "Details here.";
                commit.AuthorName = "Ada";
                commit.CommittedUtc = new DateTime(2026, 2, 10, 15, 4, 0, DateTimeKind.Utc);
                commit.IsMerge = true;
                commit.FilesChanged = 4;
                commit.AddedLines = 12;
                commit.DeletedLines = 3;
                commit.FilesTruncated = true;
                commit.Files.Add(new GitChangedFile { Kind = GitChangeKindEnum.Modified, Path = "src/a.cs", AddedLines = 12, DeletedLines = 3 });
                commit.Files.Add(new GitChangedFile { Kind = GitChangeKindEnum.Renamed, Path = "new.txt", OldPath = "old.txt", AddedLines = 0, DeletedLines = 0 });
                commit.Files.Add(new GitChangedFile { Kind = GitChangeKindEnum.Added, Path = "logo.png", IsBinary = true });

                DateTime now = new DateTime(2026, 2, 13, 15, 4, 0, DateTimeKind.Utc);
                List<string> brief = VesselHistoryRenderer.CommitLines(commit, TimeZoneInfo.Utc, now, false);
                AssertEqual(2, brief.Count);
                AssertContains("abc1234", brief[0]);
                AssertContains("Fix [[the]] thing", brief[0], "subject is markup-escaped");
                AssertContains("(merge)", brief[0]);
                AssertContains("Ada, 15:04 (3 days ago)", brief[1]);
                AssertContains("+12", brief[1]);
                AssertContains("-3", brief[1]);
                AssertContains("4 files", brief[1]);

                string verbose = String.Join("\n", VesselHistoryRenderer.CommitLines(commit, TimeZoneInfo.Utc, now, true));
                AssertContains("Details here.", verbose);
                AssertContains("M src/a.cs", verbose);
                AssertContains("R old.txt -> new.txt", verbose);
                AssertContains("A logo.png [dim](binary)[/]", verbose);
                AssertContains("and 1 more files", verbose);

                AssertEqual("Tuesday, 2026-02-10", Strip(VesselHistoryRenderer.DayHeader(commit.CommittedUtc, TimeZoneInfo.Utc)));
                AssertEqual("just now", VesselHistoryRenderer.Relative(now, now));
                AssertEqual("2 years ago", VesselHistoryRenderer.Relative(now.AddDays(-800), now));
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Helm vessel history rendering", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static VesselCommitActivity Activity(string from, string to, Dictionary<string, int> counts)
        {
            VesselCommitActivity activity = new VesselCommitActivity();
            activity.Branch = "main";
            activity.From = from;
            activity.To = to;
            DateTime start = DateTime.ParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            DateTime end = DateTime.ParseExact(to, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            for (DateTime day = start; day <= end; day = day.AddDays(1))
            {
                string key = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                int count = counts.TryGetValue(key, out int found) ? found : 0;
                activity.Days.Add(new VesselCommitActivityDay { Date = key, Count = count });
                activity.TotalCommits += count;
                activity.MaxDayCount = Math.Max(activity.MaxDayCount, count);
            }
            return activity;
        }

        private static string Strip(string markup)
        {
            return Spectre.Console.Markup.Remove(markup);
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) =>
                {
                    body();
                    return Task.CompletedTask;
                },
                tags: new List<string> { tag });
        }

        #endregion
    }
}

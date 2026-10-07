namespace Test.Shared.Suites.Client
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Client.Models;
    using Armada.Core.Models;
    using Armada.Helm.Rendering;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Armada.Client contract for vessel history against a live server: commit activity (offset bucketing, defaults,
    /// zero-filled days), commit pages (order, before, cursor paging that stays stable while a commit lands, unknown
    /// branch), validation 400s, unknown vessel 404s, and <c>armada vessel history</c> (list, heatmap, --json --all)
    /// against the same server as a remote target.
    /// </summary>
    public sealed class ClientContractVesselHistorySuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string Suite = "Client.Contract.VesselHistory";

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(ClientContract.Case(Suite, this, "activity", "Activity: per-day counts in an offset, zero-filled range, defaults, unknown branch", async (c, fx) =>
            {
                VesselSetup setup = await CreateHistoryVesselAsync(c, "hist-activity").ConfigureAwait(false);
                string id = setup.Vessel.Id;

                VesselCommitActivity utc = (await c.GetVesselCommitActivityAsync(id, new VesselCommitActivityQuery { From = "2026-02-09", To = "2026-02-12", UtcOffsetMinutes = 0 }))!;
                AssertNull(utc.Error, "no error");
                AssertEqual(id, utc.VesselId);
                AssertEqual("main", utc.Branch, "the vessel's default branch");
                AssertEqual("0,2,1,0", String.Join(",", utc.Days.Select(d => d.Count)), "zero-filled, UTC days");
                AssertEqual(3, utc.TotalCommits);
                AssertEqual(2, utc.MaxDayCount);
                AssertNotNull(utc.FirstCommitUtc);
                AssertEqual(At("2026-02-11T03:00:00Z"), utc.LastCommitUtc, "the newest commit");

                VesselCommitActivity east = (await c.GetVesselCommitActivityAsync(id, new VesselCommitActivityQuery { From = "2026-02-09", To = "2026-02-12", UtcOffsetMinutes = -240 }))!;
                AssertEqual("0,3,0,0", String.Join(",", east.Days.Select(d => d.Count)), "UTC-4 moves the 03:00Z commit to the 10th");
                AssertEqual(-240, east.UtcOffsetMinutes);

                string todayBefore = DateTime.UtcNow.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                VesselCommitActivity defaults = (await c.GetVesselCommitActivityAsync(id))!;
                string todayAfter = DateTime.UtcNow.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                AssertEqual(365, defaults.Days.Count, "default range is the last 365 days");
                AssertTrue(defaults.To == todayBefore || defaults.To == todayAfter, "to defaults to today in UTC: " + defaults.To);

                VesselCommitActivity unknown = (await c.GetVesselCommitActivityAsync(id, new VesselCommitActivityQuery { Branch = "no-such-branch", From = "2026-02-09", To = "2026-02-10" }))!;
                AssertNotNull(unknown.Error, "unknown branch is a 200 with Error");
                AssertEqual(2, unknown.Days.Count);
            }));

            cases.Add(ClientContract.Case(Suite, this, "commits", "Commits: newest first, before, cursor paging stable while a commit lands, unknown branch", async (c, fx) =>
            {
                VesselSetup setup = await CreateHistoryVesselAsync(c, "hist-commits").ConfigureAwait(false);
                string id = setup.Vessel.Id;
                string work = setup.Vessel.WorkingDirectory!;
                int total = Int32.Parse(DatedGitRepo.Git(work, null, "rev-list", "--count", "main").Trim(), CultureInfo.InvariantCulture);

                VesselCommitPage first = (await c.GetVesselCommitsAsync(id, new VesselCommitQuery { Limit = 2 }))!;
                AssertNull(first.Error);
                AssertEqual("main", first.Branch);
                AssertEqual("h3,h2", String.Join(",", first.Commits.Select(x => x.Subject)), "newest first");
                AssertNotNull(first.NextCursor, "more pages");
                AssertEqual(1, first.Commits[0].FilesChanged);
                AssertEqual("h3.txt", first.Commits[0].Files[0].Path);

                DatedGitRepo.Commit(work, "h4 landed later", At("2026-02-12T09:00:00Z"), "h4.txt", "4\n");

                List<string> seen = first.Commits.Select(x => x.Sha).ToList();
                string? cursor = first.NextCursor;
                while (cursor != null)
                {
                    VesselCommitPage page = (await c.GetVesselCommitsAsync(id, new VesselCommitQuery { Cursor = cursor, Limit = 2, Branch = "ignored-with-cursor", Before = "2000-01-01" }))!;
                    AssertNull(page.Error, page.Error);
                    AssertEqual("main", page.Branch, "the cursor carries the branch");
                    seen.AddRange(page.Commits.Select(x => x.Sha));
                    cursor = page.NextCursor;
                }
                AssertEqual(total, seen.Count, "every commit that existed at the first page, once");
                AssertEqual(total, seen.Distinct().Count(), "no duplicates");

                VesselCommitPage fresh = (await c.GetVesselCommitsAsync(id, new VesselCommitQuery { Limit = 1 }))!;
                AssertEqual("h4 landed later", fresh.Commits[0].Subject, "a new first page sees the new commit");

                VesselCommitPage beforeDay = (await c.GetVesselCommitsAsync(id, new VesselCommitQuery { Before = "2026-02-11", Limit = 1 }))!;
                AssertEqual("h2", beforeDay.Commits[0].Subject, "a bare date is the start of that day in UTC");
                VesselCommitPage beforeInstant = (await c.GetVesselCommitsAsync(id, new VesselCommitQuery { Before = "2026-02-10T18:00:00Z", Limit = 1 }))!;
                AssertEqual("h1", beforeInstant.Commits[0].Subject, "strictly before: the commit at exactly that instant is excluded");
                VesselCommitPage beforeOffset = (await c.GetVesselCommitsAsync(id, new VesselCommitQuery { Before = "2026-02-10T20:00:00+02:00", Limit = 1 }))!;
                AssertEqual("h1", beforeOffset.Commits[0].Subject, "an offset instant is converted to UTC");

                VesselCommitPage unknown = (await c.GetVesselCommitsAsync(id, new VesselCommitQuery { Branch = "no-such-branch" }))!;
                AssertNotNull(unknown.Error);
                AssertEqual(0, unknown.Commits.Count);
                AssertNull(unknown.NextCursor);
            }));

            cases.Add(ClientContract.Case(Suite, this, "validation", "Invalid dates, ranges, offsets, limits, cursors, and branches are typed 400s; an unknown vessel is a 404", async (c, fx) =>
            {
                VesselSetup setup = await CreateHistoryVesselAsync(c, "hist-validation").ConfigureAwait(false);
                string id = setup.Vessel.Id;

                List<VesselCommitActivityQuery> badActivity = new List<VesselCommitActivityQuery>
                {
                    new VesselCommitActivityQuery { From = "2026-13-01" },
                    new VesselCommitActivityQuery { To = "02/10/2026" },
                    new VesselCommitActivityQuery { From = "2026-02-10", To = "2026-02-09" },
                    new VesselCommitActivityQuery { From = "2021-01-01", To = "2026-01-06" },
                    new VesselCommitActivityQuery { UtcOffsetMinutes = 841 },
                    new VesselCommitActivityQuery { UtcOffsetMinutes = -841 },
                    new VesselCommitActivityQuery { Branch = "-x" }
                };
                foreach (VesselCommitActivityQuery query in badActivity)
                {
                    ArmadaApiException ex = await ClientContract.ExpectErrorAsync(() => c.GetVesselCommitActivityAsync(id, query), "activity " + JsonSerializer.Serialize(query), 400, 400);
                    AssertEqual("BadRequest", ex.ErrorName, "typed BadRequest");
                }
                AssertNull((await c.GetVesselCommitActivityAsync(id, new VesselCommitActivityQuery { From = "2021-01-01", To = "2026-01-05" }))!.Error, "exactly 1830 days is allowed");

                string tamperedCursor = Base64Url("{\"V\":9,\"Branch\":\"main\",\"Tip\":\"" + new string('a', 40) + "\",\"Skip\":0}");
                List<VesselCommitQuery> badCommits = new List<VesselCommitQuery>
                {
                    new VesselCommitQuery { Limit = 0 },
                    new VesselCommitQuery { Limit = 201 },
                    new VesselCommitQuery { Cursor = "not a cursor" },
                    new VesselCommitQuery { Cursor = tamperedCursor },
                    new VesselCommitQuery { Before = "not-a-date" },
                    new VesselCommitQuery { Branch = "a..b" }
                };
                foreach (VesselCommitQuery query in badCommits)
                {
                    ArmadaApiException ex = await ClientContract.ExpectErrorAsync(() => c.GetVesselCommitsAsync(id, query), "commits " + JsonSerializer.Serialize(query), 400, 400);
                    AssertEqual("BadRequest", ex.ErrorName, "typed BadRequest");
                }

                await ClientContract.ExpectErrorAsync(() => c.GetVesselCommitActivityAsync("vsl_does_not_exist"), "unknown vessel activity", 404, 404);
                await ClientContract.ExpectErrorAsync(() => c.GetVesselCommitsAsync("vsl_does_not_exist"), "unknown vessel commits", 404, 404);
            }));

            cases.Add(ClientContract.Case(Suite, this, "cli", "armada vessel history against the server as a remote target: list by day, heatmap, --json --all, unknown vessel", async (c, fx) =>
            {
                VesselSetup setup = await CreateHistoryVesselAsync(c, "hist-cli").ConfigureAwait(false);
                string name = setup.Vessel.Name;
                string[] target = new[] { "--server", fx.BaseUrl, "--token", fx.ApiKey };

                HelmCliRun list = await HelmCliHarness.RunCapturedAsync(Args(new[] { "vessel", "history", name, "--limit", "2" }, target)).ConfigureAwait(false);
                AssertEqual(0, list.ExitCode, list.Output);
                AssertContains("h3", list.Output);
                AssertContains("h2", list.Output);
                AssertFalse(list.Output.Contains("h1"), "one page of two");
                // Days are grouped in this machine's time zone, so the expected header is computed the same way.
                AssertContains(VesselHistoryRenderer.LocalDate(At("2026-02-11T03:00:00Z"), TimeZoneInfo.Local), list.Output, "day header of h3");
                AssertContains("--all", list.Output, "hint for older history");

                HelmCliRun verbose = await HelmCliHarness.RunCapturedAsync(Args(new[] { "vessel", "history", setup.Vessel.Id, "--limit", "1", "--verbose" }, target)).ConfigureAwait(false);
                AssertEqual(0, verbose.ExitCode, verbose.Output);
                AssertContains("A h3.txt", verbose.Output, "file list with kind");

                HelmCliRun heatmap = await HelmCliHarness.RunCapturedAsync(Args(new[] { "vessel", "history", name, "--heatmap", "--from", "2026-02-01", "--to", "2026-02-28" }, target)).ConfigureAwait(false);
                AssertEqual(0, heatmap.ExitCode, heatmap.Output);
                AssertContains("Mon", heatmap.Output);
                AssertContains("commits from 2026-02-01 to 2026-02-28", heatmap.Output);
                AssertContains("Less", heatmap.Output);

                HelmCliRun json = await HelmCliHarness.RunCapturedAsync(Args(new[] { "vessel", "history", name, "--json", "--all", "--limit", "1" }, target)).ConfigureAwait(false);
                AssertEqual(0, json.ExitCode, json.Output + json.StandardOutput);
                List<VesselCommitPage>? pages = JsonSerializer.Deserialize<List<VesselCommitPage>>(json.StandardOutput, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } });
                int total = Int32.Parse(DatedGitRepo.Git(setup.Vessel.WorkingDirectory!, null, "rev-list", "--count", "main").Trim(), CultureInfo.InvariantCulture);
                AssertEqual(total, pages!.Count, "one page per commit with --limit 1");
                AssertNull(pages[pages.Count - 1].NextCursor);

                HelmCliRun missing = await HelmCliHarness.RunCapturedAsync(Args(new[] { "vessel", "history", "no-such-vessel-" + ClientContract.Suffix() }, target)).ConfigureAwait(false);
                AssertEqual(1, missing.ExitCode);
                AssertContains("Vessel not found", missing.Output);
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "Armada.Client contract: vessel history (live server, CLI)", cases: cases);
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// A vessel whose working directory (the repository the history routes read) gains three dated commits on main:
        /// h1 at 2026-02-10T10:00Z, h2 at 2026-02-10T18:00Z, h3 at 2026-02-11T03:00Z.
        /// </summary>
        private static async Task<VesselSetup> CreateHistoryVesselAsync(ArmadaClient c, string label)
        {
            VesselSetup setup = await LiveServerSetup.CreateVesselAsync(c, label).ConfigureAwait(false);
            string work = setup.Vessel.WorkingDirectory!;
            DatedGitRepo.Commit(work, "h1", At("2026-02-10T10:00:00Z"), "h1.txt", "1\n");
            DatedGitRepo.Commit(work, "h2", At("2026-02-10T18:00:00Z"), "h2.txt", "2\n");
            DatedGitRepo.Commit(work, "h3", At("2026-02-11T03:00:00Z"), "h3.txt", "3\n");
            return setup;
        }

        private static DateTime At(string iso)
        {
            return DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal).UtcDateTime;
        }

        private static string[] Args(string[] command, string[] target)
        {
            return command.Concat(target).ToArray();
        }

        private static string Base64Url(string text)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        #endregion
    }
}

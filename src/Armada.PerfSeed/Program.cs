namespace Armada.PerfSeed
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// Seeds a throwaway Armada data directory for the performance baseline: settings.json bound to 127.0.0.1 on the
    /// given ports (no background schedulers, no data expiry), and armada.db with fleets, vessels and their health rows,
    /// captains, voyages, missions, jobs, Ask threads with tracked work, and a bearer credential for the default tenant's
    /// admin user. Rows go through the database driver, so the schema and constraints match a real install.
    ///
    /// No mission is Pending, Assigned, or InProgress, so the Admiral started on this data never dispatches or launches
    /// an agent. Captains are Idle.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Entry point.
        /// </summary>
        /// <param name="args">See <see cref="PerfSeedOptions"/>.</param>
        /// <returns>0 on success, 2 on bad arguments, 1 on failure.</returns>
        public static async Task<int> Main(string[] args)
        {
            PerfSeedOptions options;
            try
            {
                options = PerfSeedOptions.Parse(args);
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine("ERROR: " + ex.Message);
                Console.Error.WriteLine("Usage: Armada.PerfSeed --data-dir <dir> [--admiral-port 25060] [--mcp-port 25061] [--token t] [--vessels 500] [--missions 10000] [--voyages 1000] [--captains 50] [--fleets 10] [--jobs 2000] [--ask-threads 300] [--seed 42]");
                return 2;
            }

            Stopwatch total = Stopwatch.StartNew();
            Directory.CreateDirectory(options.DataDirectory);
            string dbPath = Path.Combine(options.DataDirectory, Constants.DefaultDatabaseFilename);
            if (File.Exists(dbPath)) throw new IOException(dbPath + " already exists; seed into an empty directory.");

            await WriteSettingsAsync(options, dbPath).ConfigureAwait(false);

            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            DatabaseSettings dbSettings = new DatabaseSettings();
            dbSettings.Type = DatabaseTypeEnum.Sqlite;
            dbSettings.Filename = dbPath;

            using (DatabaseDriver db = DatabaseDriverFactory.Create(dbSettings, logging))
            {
                await db.InitializeAsync().ConfigureAwait(false);
                Random random = new Random(options.Seed);
                DateTime now = DateTime.UtcNow;
                string tenant = Constants.DefaultTenantId;
                string user = Constants.DefaultUserId;

                Credential credential = new Credential(tenant, user);
                credential.Name = "perf-baseline";
                credential.BearerToken = options.Token;
                await db.Credentials.CreateAsync(credential).ConfigureAwait(false);

                List<Fleet> fleets = new List<Fleet>();
                for (int i = 0; i < options.Fleets; i++)
                {
                    Fleet fleet = new Fleet("perf-fleet-" + i.ToString("D3"));
                    fleet.TenantId = tenant;
                    fleet.UserId = user;
                    fleets.Add(await db.Fleets.CreateAsync(fleet).ConfigureAwait(false));
                }
                Report("fleets", fleets.Count, total);

                List<Vessel> vessels = new List<Vessel>();
                VesselHealthStatusEnum[] grades = new VesselHealthStatusEnum[] { VesselHealthStatusEnum.Pass, VesselHealthStatusEnum.Pass, VesselHealthStatusEnum.Warn, VesselHealthStatusEnum.Fail, VesselHealthStatusEnum.Unknown };
                for (int i = 0; i < options.Vessels; i++)
                {
                    Vessel vessel = new Vessel("perf-vessel-" + i.ToString("D4"), "https://example.invalid/perf/repo-" + i.ToString("D4") + ".git");
                    vessel.TenantId = tenant;
                    vessel.UserId = user;
                    vessel.FleetId = fleets[i % fleets.Count].Id;
                    vessel.DefaultBranch = "main";
                    vessel = await db.Vessels.CreateAsync(vessel).ConfigureAwait(false);
                    vessels.Add(vessel);

                    VesselHealth health = new VesselHealth();
                    health.TenantId = tenant;
                    health.VesselId = vessel.Id;
                    health.OverallStatus = grades[random.Next(grades.Length)];
                    health.EvaluatedUtc = now.AddHours(-random.Next(1, 72));
                    health.CurrentBranch = "main";
                    health.IsDirty = random.Next(10) == 0;
                    health.LastCommitUtc = now.AddDays(-random.Next(0, 60));
                    health.PrimaryLanguage = random.Next(2) == 0 ? "C#" : "TypeScript";
                    health.DependencyStatus = grades[random.Next(grades.Length)];
                    health.WorkingTreeStatus = health.IsDirty == true ? VesselHealthStatusEnum.Warn : VesselHealthStatusEnum.Pass;
                    health.BranchStatus = grades[random.Next(grades.Length)];
                    await db.VesselHealth.UpsertAsync(health).ConfigureAwait(false);
                }
                Report("vessels (+health)", vessels.Count, total);

                List<Captain> captains = new List<Captain>();
                for (int i = 0; i < options.Captains; i++)
                {
                    Captain captain = new Captain("perf-captain-" + i.ToString("D3"));
                    captain.TenantId = tenant;
                    captain.UserId = user;
                    captain.State = CaptainStateEnum.Idle;
                    captains.Add(await db.Captains.CreateAsync(captain).ConfigureAwait(false));
                }
                Report("captains", captains.Count, total);

                // About 10% of voyages are still in flight (missions WorkProduced or PullRequestOpen awaiting landing);
                // the rest finished. Missions are spread evenly across voyages.
                int activeVoyages = Math.Max(1, options.Voyages / 10);
                List<Voyage> voyages = new List<Voyage>();
                for (int i = 0; i < options.Voyages; i++)
                {
                    Voyage voyage = new Voyage("Perf voyage " + i.ToString("D4"));
                    voyage.TenantId = tenant;
                    voyage.UserId = user;
                    voyage.Status = i < activeVoyages ? VoyageStatusEnum.InProgress : (i % 10 == 1 ? VoyageStatusEnum.Failed : (i % 10 == 2 ? VoyageStatusEnum.Cancelled : VoyageStatusEnum.Complete));
                    voyage.CreatedUtc = now.AddMinutes(-random.Next(1, 25 * 24 * 60));
                    voyages.Add(await db.Voyages.CreateAsync(voyage).ConfigureAwait(false));
                }
                Report("voyages", voyages.Count, total);

                for (int i = 0; i < options.Missions; i++)
                {
                    int voyageIndex = i % voyages.Count;
                    Voyage voyage = voyages[voyageIndex];
                    Mission mission = new Mission("Perf mission " + i.ToString("D5"), "Seeded mission " + i + " for the performance baseline.");
                    mission.TenantId = tenant;
                    mission.UserId = user;
                    mission.VoyageId = voyage.Id;
                    mission.VesselId = vessels[random.Next(vessels.Count)].Id;
                    mission.CaptainId = captains.Count > 0 ? captains[random.Next(captains.Count)].Id : null;
                    mission.Status = PickStatus(voyage.Status, random);
                    mission.CreatedUtc = voyage.CreatedUtc.AddMinutes(random.Next(0, 120));
                    mission.StartedUtc = mission.CreatedUtc.AddMinutes(1);
                    if (mission.Status == MissionStatusEnum.Complete || mission.Status == MissionStatusEnum.Failed || mission.Status == MissionStatusEnum.Cancelled)
                        mission.CompletedUtc = mission.StartedUtc.Value.AddMinutes(random.Next(2, 90));
                    if (mission.Status == MissionStatusEnum.PullRequestOpen) mission.PrUrl = "https://example.invalid/perf/pull/" + i;
                    if (mission.Status == MissionStatusEnum.Failed || mission.Status == MissionStatusEnum.LandingFailed) mission.FailureReason = "Seeded failure";
                    mission.BranchName = "armada/perf/" + i.ToString("D5");
                    await db.Missions.CreateAsync(mission).ConfigureAwait(false);
                    if ((i + 1) % 2500 == 0 && i + 1 < options.Missions) Report("missions", i + 1, total);
                }
                Report("missions", options.Missions, total);

                JobKindEnum[] kinds = new JobKindEnum[] { JobKindEnum.Report, JobKindEnum.VesselDiscovery, JobKindEnum.VesselImport, JobKindEnum.FleetCategorization };
                for (int i = 0; i < options.Jobs; i++)
                {
                    Job job = new Job();
                    job.TenantId = tenant;
                    job.UserId = user;
                    job.Kind = kinds[i % kinds.Length];
                    job.Name = "Perf job " + i.ToString("D4");
                    job.Status = i % 20 == 0 ? JobStatusEnum.Failed : (i % 33 == 0 ? JobStatusEnum.Cancelled : JobStatusEnum.Succeeded);
                    job.Progress = 100;
                    job.CreatedUtc = now.AddMinutes(-random.Next(1, 25 * 24 * 60));
                    job.StartedUtc = job.CreatedUtc;
                    job.CompletedUtc = job.CreatedUtc.AddMinutes(2);
                    job.LastUpdateUtc = job.CompletedUtc.Value;
                    job.ResultJson = "{\"createdCount\":" + random.Next(0, 50) + ",\"skippedCount\":0,\"failedCount\":0}";
                    await db.Jobs.CreateAsync(job).ConfigureAwait(false);
                }
                Report("jobs", options.Jobs, total);

                for (int i = 0; i < options.AskThreads; i++)
                {
                    AskThread thread = new AskThread();
                    thread.TenantId = tenant;
                    thread.UserId = user;
                    thread.Title = "Perf conversation " + i.ToString("D3");
                    thread.LastMessageUtc = now.AddMinutes(-random.Next(1, 25 * 24 * 60));
                    thread = await db.AskThreads.CreateAsync(thread).ConfigureAwait(false);
                    for (int w = 0; w < 3; w++)
                    {
                        Voyage voyage = voyages[random.Next(voyages.Count)];
                        AskTrackedWork work = new AskTrackedWork();
                        work.TenantId = tenant;
                        work.UserId = user;
                        work.ThreadId = thread.Id;
                        work.EntityType = AskTrackedEntityTypeEnum.Voyage;
                        work.EntityId = voyage.Id;
                        work.Title = voyage.Title;
                        work.State = w == 0 ? AskTrackedWorkStateEnum.Active : AskTrackedWorkStateEnum.Succeeded;
                        await db.AskTrackedWork.CreateOrGetAsync(work).ConfigureAwait(false);
                    }
                }
                Report("ask threads (+3 tracked work each)", options.AskThreads, total);
            }

            Console.WriteLine("[perf-seed] done in " + total.Elapsed.TotalSeconds.ToString("F1") + " s: " + dbPath);
            return 0;
        }

        private static MissionStatusEnum PickStatus(VoyageStatusEnum voyageStatus, Random random)
        {
            int roll = random.Next(100);
            if (voyageStatus == VoyageStatusEnum.InProgress)
            {
                if (roll < 40) return MissionStatusEnum.WorkProduced;
                if (roll < 60) return MissionStatusEnum.PullRequestOpen;
                if (roll < 70) return MissionStatusEnum.LandingFailed;
                return MissionStatusEnum.Complete;
            }

            if (voyageStatus == VoyageStatusEnum.Failed) return roll < 60 ? MissionStatusEnum.Failed : MissionStatusEnum.Complete;
            if (voyageStatus == VoyageStatusEnum.Cancelled) return roll < 70 ? MissionStatusEnum.Cancelled : MissionStatusEnum.Complete;
            return roll < 95 ? MissionStatusEnum.Complete : MissionStatusEnum.Failed;
        }

        private static async Task WriteSettingsAsync(PerfSeedOptions options, string dbPath)
        {
            ArmadaSettings settings = new ArmadaSettings();
            settings.DataDirectory = options.DataDirectory;
            settings.DatabasePath = dbPath;
            settings.Database.Type = DatabaseTypeEnum.Sqlite;
            settings.Database.Filename = dbPath;
            settings.LogDirectory = Path.Combine(options.DataDirectory, "logs");
            settings.DocksDirectory = Path.Combine(options.DataDirectory, "docks");
            settings.ReposDirectory = Path.Combine(options.DataDirectory, "repos");
            settings.AdmiralPort = options.AdmiralPort;
            settings.McpPort = options.McpPort;
            settings.Rest.Hostname = "127.0.0.1";
            settings.DataRetentionDays = 0;
            settings.RepositoryHealth.IntervalMinutes = 0;
            settings.SyslogServers = new List<SyslogServer>();
            await settings.SaveAsync(Path.Combine(options.DataDirectory, "settings.json")).ConfigureAwait(false);
        }

        private static void Report(string what, int count, Stopwatch total)
        {
            Console.WriteLine("[perf-seed] " + what + ": " + count + " (" + total.Elapsed.TotalSeconds.ToString("F1") + " s)");
        }
    }
}

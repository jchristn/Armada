namespace Armada.DemoSeed
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// Seeds an Armada data directory for the store reviewer demo and store screenshots: settings.json, armada.db with
    /// fictional fleets, vessels, captains, model profiles, a mixed open and frontier pipeline, voyages and missions,
    /// stage outcomes, an admin password and a non-admin reviewer user, and demo-ids.json for the screenshot script.
    /// See docs/DEMO_ADMIRAL.md.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Entry point.
        /// </summary>
        /// <param name="args">See <see cref="DemoSeedOptions"/>.</param>
        /// <returns>0 on success, 2 on bad arguments, 1 on failure.</returns>
        public static async Task<int> Main(string[] args)
        {
            DemoSeedOptions options;
            try
            {
                options = DemoSeedOptions.Parse(args);
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine("ERROR: " + ex.Message);
                Console.Error.WriteLine("Usage: Armada.DemoSeed --data-dir <dir> [--settings-data-dir /app/data] [--admiral-port 7890] [--mcp-port 7891] [--hostname 127.0.0.1] [--reviewer-email reviewer@example.com] [--tunnel-url http://proxy.example:7893/tunnel] [--instance-id armada-demo]");
                Console.Error.WriteLine("Passwords: --admin-password and --reviewer-password, or " + DemoSeedOptions.AdminPasswordVariable + " and " + DemoSeedOptions.ReviewerPasswordVariable + "; the tunnel password comes from " + DemoSeedOptions.TunnelPasswordVariable + ".");
                return 2;
            }

            try
            {
                Directory.CreateDirectory(options.DataDirectory);
                string dbDirectory = Path.Combine(options.DataDirectory, "db");
                Directory.CreateDirectory(dbDirectory);
                string dbPath = Path.Combine(dbDirectory, Constants.DefaultDatabaseFilename);
                if (File.Exists(dbPath)) throw new IOException(dbPath + " already exists; seed into an empty directory (the reset helper removes it first).");

                await WriteSettingsAsync(options).ConfigureAwait(false);

                LoggingModule logging = new LoggingModule();
                logging.Settings.EnableConsole = false;
                DatabaseSettings dbSettings = new DatabaseSettings();
                dbSettings.Type = DatabaseTypeEnum.Sqlite;
                dbSettings.Filename = dbPath;

                DemoSeedIds ids;
                using (DatabaseDriver db = DatabaseDriverFactory.Create(dbSettings, logging))
                {
                    await db.InitializeAsync().ConfigureAwait(false);
                    UserMaster reviewer = await SeedUsersAsync(db, logging, options).ConfigureAwait(false);
                    ids = await new DemoWorldSeeder(db, reviewer.Id).SeedAsync().ConfigureAwait(false);
                }

                string idsPath = Path.Combine(options.DataDirectory, "demo-ids.json");
                await File.WriteAllTextAsync(idsPath, JsonSerializer.Serialize(ids, new JsonSerializerOptions { WriteIndented = true })).ConfigureAwait(false);
                Console.WriteLine("[demo-seed] seeded " + dbPath);
                Console.WriteLine("MISSION_ID=" + ids.MissionId);
                Console.WriteLine("VOYAGE_ID=" + ids.VoyageId);
                Console.WriteLine("PROFILE_ID=" + ids.ProfileId);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ERROR: " + ex.Message);
                return 1;
            }
        }

        private static async Task<UserMaster> SeedUsersAsync(DatabaseDriver db, LoggingModule logging, DemoSeedOptions options)
        {
            // Applying the admin password also retires the seeded "default" bearer token, so an Admiral listening on
            // 0.0.0.0 starts.
            DefaultCredentialService defaults = new DefaultCredentialService(db, logging);
            await defaults.ApplyInitialAdminPasswordAsync(options.AdminPassword).ConfigureAwait(false);

            UserMaster reviewer = new UserMaster(Constants.DefaultTenantId, options.ReviewerEmail, options.ReviewerPassword);
            reviewer.FirstName = "App";
            reviewer.LastName = "Reviewer";
            reviewer.IsAdmin = false;
            reviewer.IsTenantAdmin = false;
            return await db.Users.CreateAsync(reviewer).ConfigureAwait(false);
        }

        private static async Task WriteSettingsAsync(DemoSeedOptions options)
        {
            string root = options.SettingsDataDirectory ?? options.DataDirectory;
            string dbPath = Path.Combine(root, "db", Constants.DefaultDatabaseFilename);
            ArmadaSettings settings = new ArmadaSettings();
            settings.DataDirectory = root;
            settings.DatabasePath = dbPath;
            settings.Database.Type = DatabaseTypeEnum.Sqlite;
            settings.Database.Filename = dbPath;
            settings.LogDirectory = Path.Combine(root, "logs");
            settings.DocksDirectory = Path.Combine(root, "docks");
            settings.ReposDirectory = Path.Combine(root, "repos");
            settings.AdmiralPort = options.AdmiralPort;
            settings.McpPort = options.McpPort;
            settings.Rest.Hostname = options.Hostname;
            settings.DataRetentionDays = 0;
            settings.RepositoryHealth.IntervalMinutes = 0;
            settings.SyslogServers = new List<SyslogServer>();
            // No autonomous rescue missions for the seeded failures: the demo should stay as seeded.
            settings.MaxMissionRecoveryAttempts = 0;
            if (!String.IsNullOrWhiteSpace(options.TunnelUrl))
            {
                settings.RemoteControl.Enabled = true;
                settings.RemoteControl.TunnelUrl = options.TunnelUrl;
                settings.RemoteControl.InstanceId = options.InstanceId;
                settings.RemoteControl.Password = options.TunnelPassword;
            }
            else
            {
                settings.RemoteControl.Enabled = false;
            }

            await settings.SaveAsync(Path.Combine(options.DataDirectory, "settings.json")).ConfigureAwait(false);
        }
    }
}

namespace UpgradeBaselineHost
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Settings;
    using Armada.Server;
    using SyslogLogging;

    /// <summary>
    /// Runs an older Armada Admiral with explicit settings so the upgrade test never reads or writes ~/.armada.
    /// Usage: UpgradeBaselineHost --data-dir DIR --rest-port N --mcp-port N --api-key KEY
    ///        [--db-type sqlite|postgresql|mysql|sqlserver --db-host H --db-port N --db-user U --db-pass P --db-name NAME]
    /// Prints "READY" once started and stops cleanly when a line "stop" (or end of input) arrives on standard input.
    /// Agent commands point at a nonexistent executable so no captain process can ever be launched.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Entry point.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        /// <returns>Exit code.</returns>
        public static async Task<int> Main(string[] args)
        {
            Dictionary<string, string> options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i + 1 < args.Length; i += 2) options[args[i].TrimStart('-')] = args[i + 1];

            string dataDir = Require(options, "data-dir");
            Directory.CreateDirectory(dataDir);

            ArmadaSettings settings = new ArmadaSettings();
            settings.DataDirectory = dataDir;
            settings.LogDirectory = Path.Combine(dataDir, "logs");
            settings.DocksDirectory = Path.Combine(dataDir, "docks");
            settings.ReposDirectory = Path.Combine(dataDir, "repos");
            settings.AdmiralPort = Int32.Parse(Require(options, "rest-port"));
            settings.McpPort = Int32.Parse(Require(options, "mcp-port"));
            settings.ApiKey = Require(options, "api-key");
            settings.HeartbeatIntervalSeconds = 3600;
            settings.Rest.Hostname = "127.0.0.1";

            DatabaseSettings db = new DatabaseSettings();
            string type = options.TryGetValue("db-type", out string? t) ? t.ToLowerInvariant() : "sqlite";
            switch (type)
            {
                case "postgresql": db.Type = DatabaseTypeEnum.Postgresql; break;
                case "mysql": db.Type = DatabaseTypeEnum.Mysql; break;
                case "sqlserver": db.Type = DatabaseTypeEnum.SqlServer; break;
                default: db.Type = DatabaseTypeEnum.Sqlite; break;
            }

            if (db.Type == DatabaseTypeEnum.Sqlite)
            {
                string file = options.TryGetValue("db-file", out string? f) ? f : Path.Combine(dataDir, "armada.db");
                db.Filename = file;
                settings.DatabasePath = file;
            }
            else
            {
                db.Hostname = Require(options, "db-host");
                db.Port = Int32.Parse(Require(options, "db-port"));
                db.Username = Require(options, "db-user");
                db.Password = Require(options, "db-pass");
                db.DatabaseName = Require(options, "db-name");
            }

            settings.Database = db;

            foreach (AgentSettings agent in settings.Agents)
            {
                agent.Command = "armada-upgrade-test-no-such-agent";
            }

            settings.InitializeDirectories();

            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            logging.Settings.FileLogging = FileLoggingMode.SingleLogFile;
            logging.Settings.LogFilename = Path.Combine(settings.LogDirectory, "baseline-admiral.log");

            ArmadaServer server = new ArmadaServer(logging, settings, quiet: true);
            await server.StartAsync().ConfigureAwait(false);
            Console.WriteLine("READY");
            Console.Out.Flush();

            while (true)
            {
                string? line = Console.ReadLine();
                if (line == null || String.Equals(line.Trim(), "stop", StringComparison.OrdinalIgnoreCase)) break;
            }

            server.Stop();
            Console.WriteLine("STOPPED");
            return 0;
        }

        private static string Require(Dictionary<string, string> options, string name)
        {
            if (!options.TryGetValue(name, out string? value) || String.IsNullOrEmpty(value))
                throw new ArgumentException("Missing --" + name);
            return value;
        }
    }
}

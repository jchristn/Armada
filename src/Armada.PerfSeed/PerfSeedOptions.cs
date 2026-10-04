namespace Armada.PerfSeed
{
    using System;
    using System.Globalization;

    /// <summary>
    /// Command-line options for the performance seeder.
    /// </summary>
    public class PerfSeedOptions
    {
        #region Public-Members

        /// <summary>
        /// Throwaway data directory to create (settings.json and armada.db are written here). Required.
        /// </summary>
        public string DataDirectory { get; set; } = String.Empty;

        /// <summary>
        /// REST port written to settings.json. Default 25060.
        /// </summary>
        public int AdmiralPort { get; set; } = 25060;

        /// <summary>
        /// MCP port written to settings.json. Default 25061.
        /// </summary>
        public int McpPort { get; set; } = 25061;

        /// <summary>
        /// Bearer token of the credential seeded for the default tenant's admin user. Default "perf-baseline-token".
        /// </summary>
        public string Token { get; set; } = "perf-baseline-token";

        /// <summary>
        /// Number of fleets. Default 10.
        /// </summary>
        public int Fleets { get; set; } = 10;

        /// <summary>
        /// Number of vessels. Default 500.
        /// </summary>
        public int Vessels { get; set; } = 500;

        /// <summary>
        /// Number of captains. Default 50.
        /// </summary>
        public int Captains { get; set; } = 50;

        /// <summary>
        /// Number of voyages. Default 1000.
        /// </summary>
        public int Voyages { get; set; } = 1000;

        /// <summary>
        /// Number of missions. Default 10000.
        /// </summary>
        public int Missions { get; set; } = 10000;

        /// <summary>
        /// Number of background jobs. Default 2000.
        /// </summary>
        public int Jobs { get; set; } = 2000;

        /// <summary>
        /// Number of Ask threads. Default 300.
        /// </summary>
        public int AskThreads { get; set; } = 300;

        /// <summary>
        /// Random seed so runs are reproducible. Default 42.
        /// </summary>
        public int Seed { get; set; } = 42;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Parse "--name value" pairs.
        /// </summary>
        /// <param name="args">Arguments.</param>
        /// <returns>Options.</returns>
        /// <exception cref="ArgumentException">Unknown option, missing value, or missing --data-dir.</exception>
        public static PerfSeedOptions Parse(string[] args)
        {
            PerfSeedOptions options = new PerfSeedOptions();
            for (int i = 0; i < args.Length; i++)
            {
                string name = args[i];
                if (i + 1 >= args.Length) throw new ArgumentException("missing value for " + name);
                string value = args[++i];
                switch (name)
                {
                    case "--data-dir": options.DataDirectory = value; break;
                    case "--admiral-port": options.AdmiralPort = Int(value, name); break;
                    case "--mcp-port": options.McpPort = Int(value, name); break;
                    case "--token": options.Token = value; break;
                    case "--fleets": options.Fleets = Int(value, name); break;
                    case "--vessels": options.Vessels = Int(value, name); break;
                    case "--captains": options.Captains = Int(value, name); break;
                    case "--voyages": options.Voyages = Int(value, name); break;
                    case "--missions": options.Missions = Int(value, name); break;
                    case "--jobs": options.Jobs = Int(value, name); break;
                    case "--ask-threads": options.AskThreads = Int(value, name); break;
                    case "--seed": options.Seed = Int(value, name); break;
                    default: throw new ArgumentException("unknown option " + name);
                }
            }

            if (String.IsNullOrWhiteSpace(options.DataDirectory)) throw new ArgumentException("--data-dir is required");
            if (options.Fleets < 1 || options.Vessels < 1 || options.Voyages < 1) throw new ArgumentException("fleets, vessels, and voyages must be at least 1");
            return options;
        }

        #endregion

        #region Private-Methods

        private static int Int(string value, string name)
        {
            if (!Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) || parsed < 0)
                throw new ArgumentException(name + " needs a non-negative integer");
            return parsed;
        }

        #endregion
    }
}

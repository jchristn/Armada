namespace Armada.Publisher
{
    using System;

    /// <summary>
    /// Parsed command-line options for the Publisher.
    /// </summary>
    public class CliOptions
    {
        #region Public-Members

        /// <summary>
        /// The command to run.
        /// </summary>
        public CliCommandEnum Command { get; set; } = CliCommandEnum.None;

        /// <summary>
        /// Path to the manifest (defaults to "publisher.json").
        /// </summary>
        public string ManifestPath { get; set; } = "publisher.json";

        /// <summary>
        /// Channel name for the Channel command.
        /// </summary>
        public string ChannelName { get; set; } = string.Empty;

        /// <summary>
        /// Release version string.
        /// </summary>
        public string Version { get; set; } = string.Empty;

        /// <summary>
        /// Output directory for publishes and packages.
        /// </summary>
        public string OutputDirectory { get; set; } = string.Empty;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Parse arguments into options.
        /// </summary>
        /// <param name="args">Raw arguments.</param>
        /// <returns>Parsed options.</returns>
        public static CliOptions Parse(string[] args)
        {
            if (args == null) throw new ArgumentNullException(nameof(args));

            CliOptions options = new CliOptions();

            for (int i = 0; i < args.Length; i++)
            {
                string current = args[i];
                switch (current)
                {
                    case "doctor":
                        options.Command = CliCommandEnum.Doctor;
                        break;
                    case "list":
                        options.Command = CliCommandEnum.List;
                        break;
                    case "--all":
                        options.Command = CliCommandEnum.All;
                        break;
                    case "--channel":
                        options.Command = CliCommandEnum.Channel;
                        options.ChannelName = Next(args, ref i, "--channel");
                        break;
                    case "--version":
                        options.Version = Next(args, ref i, "--version");
                        break;
                    case "--manifest":
                        options.ManifestPath = Next(args, ref i, "--manifest");
                        break;
                    case "--output":
                        options.OutputDirectory = Next(args, ref i, "--output");
                        break;
                    case "-h":
                    case "--help":
                        options.Command = CliCommandEnum.None;
                        break;
                    default:
                        throw new ArgumentException("Unrecognized argument '" + current + "'.");
                }
            }

            return options;
        }

        /// <summary>
        /// Print usage to standard output.
        /// </summary>
        public static void PrintUsage()
        {
            Console.WriteLine("Armada.Publisher - packaging orchestrator");
            Console.WriteLine();
            Console.WriteLine("Commands:");
            Console.WriteLine("  doctor                         Report packaging-tool readiness for this OS.");
            Console.WriteLine("  list                           List declared channels.");
            Console.WriteLine("  --channel <name> --version x   Run one channel at a version.");
            Console.WriteLine("  --all --version x              Run every enabled channel.");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --manifest <path>              Manifest path (default: publisher.json).");
            Console.WriteLine("  --output <dir>                 Output directory (default: ./artifacts).");
        }

        #endregion

        #region Private-Methods

        private static string Next(string[] args, ref int i, string flag)
        {
            if (i + 1 >= args.Length) throw new ArgumentException("Flag '" + flag + "' requires a value.");
            i++;
            return args[i];
        }

        #endregion
    }
}

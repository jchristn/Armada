namespace Armada.DemoSeed
{
    using System;
    using System.Globalization;

    /// <summary>
    /// Command-line options for the demo seeder. Passwords come from arguments or environment variables, never from
    /// the repository.
    /// </summary>
    public class DemoSeedOptions
    {
        #region Public-Members

        /// <summary>
        /// Environment variable read when --admin-password is not given.
        /// </summary>
        public const string AdminPasswordVariable = "ARMADA_DEMO_ADMIN_PASSWORD";

        /// <summary>
        /// Environment variable read when --reviewer-password is not given.
        /// </summary>
        public const string ReviewerPasswordVariable = "ARMADA_DEMO_REVIEWER_PASSWORD";

        /// <summary>
        /// Environment variable holding the remote-control tunnel password (the proxy's shared password).
        /// </summary>
        public const string TunnelPasswordVariable = "ARMADA_DEMO_TUNNEL_PASSWORD";

        /// <summary>
        /// Data directory to create (settings.json, armada.db and demo-ids.json are written here). Required; it must not
        /// already hold an armada.db.
        /// </summary>
        public string DataDirectory { get; set; } = String.Empty;

        /// <summary>
        /// Data directory path written into settings.json, when the Admiral sees the directory under another path (for
        /// example /app/data inside a container). Defaults to <see cref="DataDirectory"/>.
        /// </summary>
        public string? SettingsDataDirectory { get; set; } = null;

        /// <summary>
        /// REST port written to settings.json. Default 7890.
        /// </summary>
        public int AdmiralPort { get; set; } = 7890;

        /// <summary>
        /// MCP port written to settings.json. Default 7891.
        /// </summary>
        public int McpPort { get; set; } = 7891;

        /// <summary>
        /// REST listen hostname written to settings.json. Default 127.0.0.1; use 0.0.0.0 in a container.
        /// </summary>
        public string Hostname { get; set; } = "127.0.0.1";

        /// <summary>
        /// New password for the default admin (admin@armada). At least 8 characters. Required.
        /// </summary>
        public string AdminPassword { get; set; } = String.Empty;

        /// <summary>
        /// Email of the non-admin reviewer user. Default reviewer@example.com.
        /// </summary>
        public string ReviewerEmail { get; set; } = "reviewer@example.com";

        /// <summary>
        /// Password of the reviewer user. At least 8 characters. Required.
        /// </summary>
        public string ReviewerPassword { get; set; } = String.Empty;

        /// <summary>
        /// Remote-control tunnel URL (for example the proxy's /tunnel endpoint), or null to leave remote control off.
        /// </summary>
        public string? TunnelUrl { get; set; } = null;

        /// <summary>
        /// Remote-control instance id shown on the proxy. Default armada-demo.
        /// </summary>
        public string InstanceId { get; set; } = "armada-demo";

        /// <summary>
        /// Remote-control tunnel password, read from <see cref="TunnelPasswordVariable"/>.
        /// </summary>
        public string? TunnelPassword { get; set; } = null;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Parse command-line arguments, falling back to the environment for the passwords.
        /// </summary>
        /// <param name="args">Arguments.</param>
        /// <returns>Options.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="args"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when an argument is unknown, missing its value, or invalid.</exception>
        public static DemoSeedOptions Parse(string[] args)
        {
            if (args == null) throw new ArgumentNullException(nameof(args));
            DemoSeedOptions options = new DemoSeedOptions();
            for (int i = 0; i < args.Length; i++)
            {
                string name = args[i];
                if (i + 1 >= args.Length) throw new ArgumentException(name + " needs a value.");
                string value = args[++i];
                switch (name)
                {
                    case "--data-dir": options.DataDirectory = value; break;
                    case "--settings-data-dir": options.SettingsDataDirectory = value; break;
                    case "--admiral-port": options.AdmiralPort = ParsePort(name, value); break;
                    case "--mcp-port": options.McpPort = ParsePort(name, value); break;
                    case "--hostname": options.Hostname = value; break;
                    case "--admin-password": options.AdminPassword = value; break;
                    case "--reviewer-email": options.ReviewerEmail = value; break;
                    case "--reviewer-password": options.ReviewerPassword = value; break;
                    case "--tunnel-url": options.TunnelUrl = value; break;
                    case "--instance-id": options.InstanceId = value; break;
                    default: throw new ArgumentException("Unknown argument " + name + ".");
                }
            }

            if (String.IsNullOrEmpty(options.AdminPassword)) options.AdminPassword = Environment.GetEnvironmentVariable(AdminPasswordVariable) ?? String.Empty;
            if (String.IsNullOrEmpty(options.ReviewerPassword)) options.ReviewerPassword = Environment.GetEnvironmentVariable(ReviewerPasswordVariable) ?? String.Empty;
            options.TunnelPassword = Environment.GetEnvironmentVariable(TunnelPasswordVariable);

            if (String.IsNullOrWhiteSpace(options.DataDirectory)) throw new ArgumentException("--data-dir is required.");
            if (options.AdminPassword.Length < 8) throw new ArgumentException("--admin-password (or " + AdminPasswordVariable + ") must be at least 8 characters.");
            if (options.ReviewerPassword.Length < 8) throw new ArgumentException("--reviewer-password (or " + ReviewerPasswordVariable + ") must be at least 8 characters.");
            if (String.IsNullOrWhiteSpace(options.ReviewerEmail) || !options.ReviewerEmail.Contains('@')) throw new ArgumentException("--reviewer-email must be an email address.");
            return options;
        }

        #endregion

        #region Private-Methods

        private static int ParsePort(string name, string value)
        {
            if (!Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int port) || port < 1 || port > 65535)
                throw new ArgumentException(name + " must be a port number.");
            return port;
        }

        #endregion
    }
}

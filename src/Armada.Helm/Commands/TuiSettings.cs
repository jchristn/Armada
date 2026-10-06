namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console.Cli;

    /// <summary>
    /// Settings for the tui command.
    /// </summary>
    public class TuiSettings : CommandSettings
    {
        /// <summary>
        /// Admiral URL to connect to (overrides the profile, ARMADA_SERVER_URL, and ARMADA_URL).
        /// </summary>
        [CommandOption("--server <URL>")]
        [Description("Admiral URL, for example http://127.0.0.1:7890")]
        public string? Server { get; set; }

        /// <summary>
        /// Saved server profile to use (created when --server is also given).
        /// </summary>
        [CommandOption("--profile <NAME>")]
        [Description("Server profile name from ~/.armada/tui.json (shared with the CLI; see armada profile list)")]
        public string? Profile { get; set; }

        /// <summary>
        /// Bearer token, session token, or API key for a scripted start (overrides ARMADA_TOKEN and the stored token).
        /// </summary>
        [CommandOption("--token <TOKEN>")]
        [Description("Bearer token for a scripted start (default: ARMADA_TOKEN or the token stored for the profile)")]
        public string? Token { get; set; }

        /// <summary>
        /// Route to open after sign-in.
        /// </summary>
        [CommandOption("--route <PATH>")]
        [Description("Route to open after sign-in, for example /missions?tab=voyages")]
        public string? Route { get; set; }
    }
}

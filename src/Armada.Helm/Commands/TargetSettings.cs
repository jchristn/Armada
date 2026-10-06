namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console.Cli;

    /// <summary>
    /// Options that pick the Admiral a command talks to. Resolution order: these options, then ARMADA_SERVER_URL and
    /// ARMADA_TOKEN, then the active profile (shared with <c>armada tui</c>), then the local Admiral.
    /// </summary>
    public class TargetSettings : CommandSettings
    {
        /// <summary>
        /// Admiral base URL (overrides ARMADA_SERVER_URL and the active profile).
        /// </summary>
        [Description("Admiral URL, for example https://armada.example.com (default: ARMADA_SERVER_URL, the active profile, or the local Admiral)")]
        [CommandOption("--server <URL>")]
        public string? Server { get; set; }

        /// <summary>
        /// Bearer token for the Admiral (overrides ARMADA_TOKEN and the token stored for the profile).
        /// </summary>
        [Description("Bearer token (default: ARMADA_TOKEN or the token stored for the profile)")]
        [CommandOption("--token <TOKEN>")]
        public string? Token { get; set; }

        /// <summary>
        /// Saved profile to use (see <c>armada profile list</c>); <c>local</c> means this machine's Admiral.
        /// </summary>
        [Description("Saved server profile (armada profile list); 'local' means this machine's Admiral")]
        [CommandOption("--profile <NAME>")]
        public string? Profile { get; set; }
    }
}

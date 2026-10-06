namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console.Cli;

    /// <summary>
    /// Settings for <c>armada profile add</c>.
    /// </summary>
    public class ProfileAddSettings : CommandSettings
    {
        /// <summary>
        /// Profile name.
        /// </summary>
        [Description("Profile name")]
        [CommandArgument(0, "<name>")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Admiral base URL.
        /// </summary>
        [Description("Admiral URL, for example https://armada.example.com")]
        [CommandOption("--server <URL>")]
        public string? Server { get; set; }

        /// <summary>
        /// Bearer token to store in the credential store (prompted for when omitted on an interactive terminal).
        /// </summary>
        [Description("Bearer token to store (OS keychain, else a 0600 file); prompted for when omitted on a terminal")]
        [CommandOption("--token <TOKEN>")]
        public string? Token { get; set; }

        /// <summary>
        /// Do not prompt for a token.
        /// </summary>
        [Description("Do not prompt for a token")]
        [CommandOption("--no-token")]
        public bool NoToken { get; set; } = false;

        /// <summary>
        /// Make the profile the active target.
        /// </summary>
        [Description("Make it the active target for the CLI and the TUI")]
        [CommandOption("--use")]
        public bool Use { get; set; } = false;
    }
}

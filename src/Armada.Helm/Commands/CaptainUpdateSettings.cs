namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console.Cli;

    /// <summary>
    /// Settings for captain update command.
    /// </summary>
    public class CaptainUpdateSettings : BaseSettings
    {
        /// <summary>Whether this captain may accept new work.</summary>
        [Description("Whether the captain may accept new work (true or false)")]
        [CommandOption("--active")]
        public bool? Active { get; set; }

        /// <summary>
        /// Captain name or ID.
        /// </summary>
        [Description("Captain name or ID")]
        [CommandArgument(0, "<captain>")]
        public string Captain { get; set; } = string.Empty;

        /// <summary>
        /// Optional name override.
        /// </summary>
        [Description("Updated captain name")]
        [CommandOption("--name|-n <NAME>")]
        public string? Name { get; set; }

        /// <summary>
        /// Optional runtime override.
        /// </summary>
        [Description("Updated runtime (claude, codex, gemini, cursor, mux, opencode, pi, api, custom; unknown values are rejected)")]
        [CommandOption("--runtime|-r <RUNTIME>")]
        public string? Runtime { get; set; }

        /// <summary>
        /// Optional model override.
        /// </summary>
        [Description("Updated model override")]
        [CommandOption("--model|-m <MODEL>")]
        public string? Model { get; set; }

        /// <summary>Managed inference endpoint for an API captain.</summary>
        [Description("Managed inference endpoint ID for an API captain")]
        [CommandOption("--model-endpoint <URL>")]
        public string? ModelEndpointId { get; set; }

        /// <summary>
        /// Optional Mux config directory override.
        /// </summary>
        [Description("Mux config directory override")]
        [CommandOption("--mux-config-dir <DIR>")]
        public string? MuxConfigDirectory { get; set; }

        /// <summary>
        /// Optional named Mux endpoint override.
        /// </summary>
        [Description("Named Mux endpoint")]
        [CommandOption("--mux-endpoint <URL>")]
        public string? MuxEndpoint { get; set; }

        /// <summary>
        /// Optional Mux base URL override.
        /// </summary>
        [Description("Mux base URL override")]
        [CommandOption("--mux-base-url <URL>")]
        public string? MuxBaseUrl { get; set; }

        /// <summary>
        /// Optional Mux adapter type override.
        /// </summary>
        [Description("Mux adapter type override")]
        [CommandOption("--mux-adapter-type <TYPE>")]
        public string? MuxAdapterType { get; set; }

        /// <summary>
        /// Optional Mux temperature override.
        /// </summary>
        [Description("Mux temperature override")]
        [CommandOption("--mux-temperature <VALUE>")]
        public double? MuxTemperature { get; set; }

        /// <summary>
        /// Optional Mux max tokens override.
        /// </summary>
        [Description("Mux max tokens override")]
        [CommandOption("--mux-max-tokens <N>")]
        public int? MuxMaxTokens { get; set; }

        /// <summary>
        /// Optional Mux system prompt path.
        /// </summary>
        [Description("Mux system prompt file path")]
        [CommandOption("--mux-system-prompt-path <PATH>")]
        public string? MuxSystemPromptPath { get; set; }

        /// <summary>
        /// Optional Mux approval policy override.
        /// </summary>
        [Description("Mux approval policy override")]
        [CommandOption("--mux-approval-policy <POLICY>")]
        public string? MuxApprovalPolicy { get; set; }
    }
}

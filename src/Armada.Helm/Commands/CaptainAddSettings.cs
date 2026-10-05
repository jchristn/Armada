namespace Armada.Helm.Commands
{
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Threading;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Helm.Rendering;

    /// <summary>
    /// Settings for captain add command.
    /// </summary>
    public class CaptainAddSettings : BaseSettings
    {
        /// <summary>
        /// Captain name.
        /// </summary>
        [Description("Name of the captain")]
        [CommandArgument(0, "<name>")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Agent runtime type.
        /// </summary>
        [Description("Agent runtime (claude, codex, gemini, cursor, mux, opencode, api, custom; unknown values are rejected)")]
        [CommandOption("--runtime|-r")]
        public string? Runtime { get; set; }

        /// <summary>
        /// Optional model override.
        /// </summary>
        [Description("Optional model override")]
        [CommandOption("--model|-m")]
        public string? Model { get; set; }

        /// <summary>
        /// Optional Mux config directory override.
        /// </summary>
        [Description("Mux config directory override")]
        [CommandOption("--mux-config-dir")]
        public string? MuxConfigDirectory { get; set; }

        /// <summary>
        /// Named Mux endpoint.
        /// </summary>
        [Description("Named Mux endpoint")]
        [CommandOption("--mux-endpoint")]
        public string? MuxEndpoint { get; set; }

        /// <summary>
        /// Optional Mux base URL override.
        /// </summary>
        [Description("Mux base URL override")]
        [CommandOption("--mux-base-url")]
        public string? MuxBaseUrl { get; set; }

        /// <summary>
        /// Optional Mux adapter type override.
        /// </summary>
        [Description("Mux adapter type override")]
        [CommandOption("--mux-adapter-type")]
        public string? MuxAdapterType { get; set; }

        /// <summary>
        /// Optional Mux temperature override.
        /// </summary>
        [Description("Mux temperature override")]
        [CommandOption("--mux-temperature")]
        public double? MuxTemperature { get; set; }

        /// <summary>
        /// Optional Mux max tokens override.
        /// </summary>
        [Description("Mux max tokens override")]
        [CommandOption("--mux-max-tokens")]
        public int? MuxMaxTokens { get; set; }

        /// <summary>
        /// Optional Mux system prompt path.
        /// </summary>
        [Description("Mux system prompt file path")]
        [CommandOption("--mux-system-prompt-path")]
        public string? MuxSystemPromptPath { get; set; }

        /// <summary>
        /// Optional Mux approval policy override.
        /// </summary>
        [Description("Mux approval policy override")]
        [CommandOption("--mux-approval-policy")]
        public string? MuxApprovalPolicy { get; set; }
    }
}

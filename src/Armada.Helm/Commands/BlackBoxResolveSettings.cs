namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console.Cli;

    /// <summary>
    /// Settings for <c>armada blackbox resolve</c>.
    /// </summary>
    public class BlackBoxResolveSettings : BlackBoxIdSettings
    {
        /// <summary>
        /// Resolution.
        /// </summary>
        [Description("Resolution: Resolved, Duplicate, WontFix, ArmadaFixed, Open")]
        [CommandOption("--resolution <RESOLUTION>")]
        public string? Resolution { get; set; }

        /// <summary>
        /// Note.
        /// </summary>
        [Description("Resolution note")]
        [CommandOption("--note <TEXT>")]
        public string? Note { get; set; }

        /// <summary>
        /// Link.
        /// </summary>
        [Description("Link (a mission id, commit, or URL)")]
        [CommandOption("--link <LINK>")]
        public string? Link { get; set; }
    }
}

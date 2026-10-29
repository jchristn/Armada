namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console.Cli;

    /// <summary>
    /// Settings for <c>armada blackbox diff</c>.
    /// </summary>
    public class BlackBoxDiffSettings : BlackBoxIdSettings
    {
        /// <summary>
        /// The entry to compare with.
        /// </summary>
        [Description("The other black box entry id (bbx_) to compare with")]
        [CommandArgument(1, "<otherId>")]
        public string OtherId { get; set; } = string.Empty;
    }
}

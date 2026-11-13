namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console.Cli;

    /// <summary>
    /// Settings for <c>armada blackbox cascade</c> and <c>armada voyage cascade</c>.
    /// </summary>
    public class BlackBoxCascadeSettings : BaseSettings
    {
        /// <summary>
        /// Voyage id.
        /// </summary>
        [Description("Voyage ID (vyg_)")]
        [CommandArgument(0, "<voyageId>")]
        public string VoyageId { get; set; } = string.Empty;
    }
}

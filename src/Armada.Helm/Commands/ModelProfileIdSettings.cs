namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console.Cli;

    /// <summary>
    /// Settings for model profile commands that take a profile ID.
    /// </summary>
    public class ModelProfileIdSettings : BaseSettings
    {
        /// <summary>
        /// Model profile ID (mdl_ prefix).
        /// </summary>
        [Description("Model profile ID (mdl_ prefix)")]
        [CommandArgument(0, "<id>")]
        public string ProfileId { get; set; } = string.Empty;
    }
}

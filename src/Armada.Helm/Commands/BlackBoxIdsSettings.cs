namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console.Cli;

    /// <summary>
    /// Settings for black box commands that take one or more entry ids (pin, unpin, delete).
    /// </summary>
    public class BlackBoxIdsSettings : BaseSettings
    {
        /// <summary>
        /// Entry ids.
        /// </summary>
        [Description("Black box entry ids (bbx_)")]
        [CommandArgument(0, "<ids>")]
        public string[] Ids { get; set; } = System.Array.Empty<string>();
    }
}

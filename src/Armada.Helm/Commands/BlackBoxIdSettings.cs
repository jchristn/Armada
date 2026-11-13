namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console.Cli;

    /// <summary>
    /// Settings for black box commands that take one id.
    /// </summary>
    public class BlackBoxIdSettings : BaseSettings
    {
        /// <summary>
        /// Entry, mission, or voyage id.
        /// </summary>
        [Description("Black box entry ID (bbx_), or a mission (msn_) or voyage (vyg_) ID where the command accepts one")]
        [CommandArgument(0, "<id>")]
        public string Id { get; set; } = string.Empty;
    }
}

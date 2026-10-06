namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console.Cli;

    /// <summary>
    /// Settings for profile commands that take a profile name (<c>use</c>, <c>remove</c>).
    /// </summary>
    public class ProfileNameSettings : CommandSettings
    {
        /// <summary>
        /// Profile name (<c>local</c> means this machine's Admiral).
        /// </summary>
        [Description("Profile name ('local' means this machine's Admiral)")]
        [CommandArgument(0, "<name>")]
        public string Name { get; set; } = string.Empty;
    }
}

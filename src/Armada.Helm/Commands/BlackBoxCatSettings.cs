namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console.Cli;

    /// <summary>
    /// Settings for <c>armada blackbox cat</c>.
    /// </summary>
    public class BlackBoxCatSettings : BlackBoxIdSettings
    {
        /// <summary>
        /// Object name.
        /// </summary>
        [Description("Object name (see armada blackbox objects)")]
        [CommandArgument(1, "<name>")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Byte offset.
        /// </summary>
        [Description("Byte offset (default 0)")]
        [CommandOption("--offset <BYTES>")]
        public long? Offset { get; set; }

        /// <summary>
        /// Bytes to read.
        /// </summary>
        [Description("Bytes to read (default the rest, at most 1 MB per request)")]
        [CommandOption("--length <BYTES>")]
        public long? Length { get; set; }
    }
}

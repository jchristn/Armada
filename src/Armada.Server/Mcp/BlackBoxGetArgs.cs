namespace Armada.Server.Mcp
{
    /// <summary>
    /// Arguments of get_blackbox.
    /// </summary>
    public class BlackBoxGetArgs : BlackBoxTargetArgs
    {
        /// <summary>
        /// summary (default), brief or json.
        /// </summary>
        public string? Format { get; set; } = null;

        /// <summary>
        /// Strip absolute paths, host names and user names from the brief.
        /// </summary>
        public bool? RedactHosts { get; set; } = null;
    }
}

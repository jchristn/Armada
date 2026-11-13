namespace Armada.Server.Mcp
{
    /// <summary>
    /// Arguments of resolve_blackbox.
    /// </summary>
    public class BlackBoxResolveArgs : BlackBoxTargetArgs
    {
        /// <summary>
        /// Open, Resolved, Duplicate, WontFix or ArmadaFixed.
        /// </summary>
        public string? Resolution { get; set; } = null;

        /// <summary>
        /// Resolution note (up to 2000 characters), or null.
        /// </summary>
        public string? Note { get; set; } = null;

        /// <summary>
        /// Link (for example a commit or pull request, up to 1000 characters), or null.
        /// </summary>
        public string? Link { get; set; } = null;
    }
}

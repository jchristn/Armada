namespace Armada.Server.Mcp
{
    /// <summary>
    /// Arguments of pin_blackbox.
    /// </summary>
    public class BlackBoxPinArgs : BlackBoxTargetArgs
    {
        /// <summary>
        /// True (default) to pin, false to unpin.
        /// </summary>
        public bool? Pinned { get; set; } = null;
    }
}

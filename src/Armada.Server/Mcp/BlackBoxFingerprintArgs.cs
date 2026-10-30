namespace Armada.Server.Mcp
{
    /// <summary>
    /// Arguments of get_blackbox_fingerprint_trend.
    /// </summary>
    public class BlackBoxFingerprintArgs
    {
        /// <summary>
        /// Failure fingerprint.
        /// </summary>
        public string? Fingerprint { get; set; } = null;
    }
}

namespace Armada.Core.Models
{
    /// <summary>
    /// Handshake payload sent by an Armada instance when the tunnel connects.
    /// </summary>
    public class RemoteTunnelHandshakePayload
    {
        #region Public-Members

        /// <summary>
        /// Tunnel protocol version.
        /// </summary>
        public string? ProtocolVersion { get; set; } = null;

        /// <summary>
        /// Armada release version.
        /// </summary>
        public string? ArmadaVersion { get; set; } = null;

        /// <summary>
        /// Stable instance identifier.
        /// </summary>
        public string? InstanceId { get; set; } = null;

        /// <summary>
        /// Optional enrollment token.
        /// </summary>
        public string? EnrollmentToken { get; set; } = null;

        /// <summary>
        /// One-time SHA256 proof of the shared password.
        /// </summary>
        public string? PasswordProofSha256 { get; set; } = null;

        /// <summary>
        /// Nonce paired with the password proof.
        /// </summary>
        public string? PasswordNonce { get; set; } = null;

        /// <summary>
        /// UTC timestamp paired with the password proof.
        /// </summary>
        public string? PasswordTimestampUtc { get; set; } = null;

        /// <summary>
        /// Capability names supported by the instance.
        /// </summary>
        public List<string> Capabilities { get; set; } = new List<string>();

        #endregion
    }
}

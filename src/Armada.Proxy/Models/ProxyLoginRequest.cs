namespace Armada.Proxy.Models
{
    /// <summary>
    /// Body of POST /proxy-api/v1/auth/login.
    /// </summary>
    public class ProxyLoginRequest
    {
        #region Public-Members

        /// <summary>
        /// Nonce from GET /proxy-api/v1/auth/challenge.
        /// </summary>
        public string? Nonce { get; set; } = null;

        /// <summary>
        /// Hex SHA-256 proof computed from the shared password and the nonce.
        /// </summary>
        public string? ProofSha256 { get; set; } = null;

        #endregion
    }
}

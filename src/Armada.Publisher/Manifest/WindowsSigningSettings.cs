namespace Armada.Publisher.Manifest
{
    /// <summary>
    /// Windows Authenticode signing configuration. Values name the secrets that carry the credential;
    /// the certificate material itself never lives in the manifest.
    /// </summary>
    public class WindowsSigningSettings
    {
        #region Public-Members

        /// <summary>
        /// Name of the secret holding the base64-encoded PFX certificate.
        /// </summary>
        public string CertBase64Secret { get; set; } = "WINDOWS_CERT_BASE64";

        /// <summary>
        /// Name of the secret holding the PFX password.
        /// </summary>
        public string CertPasswordSecret { get; set; } = "WINDOWS_CERT_PASSWORD";

        /// <summary>
        /// RFC 3161 timestamp URL used by signtool.
        /// </summary>
        public string TimestampUrl { get; set; } = "http://timestamp.digicert.com";

        #endregion
    }
}

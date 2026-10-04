namespace Armada.Publisher.Manifest
{
    /// <summary>
    /// macOS codesign and notarization configuration. Values name the secrets that carry the credential.
    /// </summary>
    public class MacosSigningSettings
    {
        #region Public-Members

        /// <summary>
        /// Name of the secret holding the base64-encoded Developer ID Application certificate (.p12).
        /// </summary>
        public string CertBase64Secret { get; set; } = "APPLE_CERT_BASE64";

        /// <summary>
        /// Name of the secret holding the certificate password.
        /// </summary>
        public string CertPasswordSecret { get; set; } = "APPLE_CERT_PASSWORD";

        /// <summary>
        /// Name of the secret holding the App Store Connect notary API key (.p8 contents).
        /// </summary>
        public string NotaryKeySecret { get; set; } = "APPLE_NOTARY_KEY";

        /// <summary>
        /// Name of the secret holding the notary API key identifier.
        /// </summary>
        public string NotaryKeyIdSecret { get; set; } = "APPLE_NOTARY_KEY_ID";

        /// <summary>
        /// Name of the secret holding the notary issuer identifier.
        /// </summary>
        public string NotaryIssuerSecret { get; set; } = "APPLE_NOTARY_ISSUER";

        /// <summary>
        /// The codesign identity string ("Developer ID Application: Name (TEAMID)").
        /// </summary>
        public string DeveloperIdApplication { get; set; } = string.Empty;

        /// <summary>
        /// The productbuild identity string for .pkg signing ("Developer ID Installer: Name (TEAMID)").
        /// </summary>
        public string DeveloperIdInstaller { get; set; } = string.Empty;

        /// <summary>
        /// Name of the environment variable that, when set, overrides the codesign identity. Lets a local
        /// build sign with an identity already in the login keychain without importing a certificate.
        /// </summary>
        public string SigningIdentitySecret { get; set; } = "APPLE_SIGNING_IDENTITY";

        /// <summary>
        /// Name of the environment variable that, when set, overrides the .pkg installer identity.
        /// </summary>
        public string InstallerIdentitySecret { get; set; } = "APPLE_INSTALLER_IDENTITY";

        #endregion
    }
}

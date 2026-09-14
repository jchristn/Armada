namespace Armada.Publisher.Manifest
{
    /// <summary>
    /// Linux repository-signing configuration. Individual packages are not signed; the apt/yum
    /// repository metadata is GPG-signed and the public key is published for users to trust.
    /// </summary>
    public class LinuxSigningSettings
    {
        #region Public-Members

        /// <summary>
        /// Name of the secret holding the ASCII-armored GPG private key.
        /// </summary>
        public string GpgPrivateKeySecret { get; set; } = "GPG_PRIVATE_KEY";

        /// <summary>
        /// GPG key identifier used to sign repository metadata. Optional; resolved from the key when blank.
        /// </summary>
        public string GpgKeyId { get; set; } = string.Empty;

        /// <summary>
        /// Name of the secret holding write credentials for the apt/yum repository host.
        /// </summary>
        public string RepoSyncCredentialsSecret { get; set; } = "REPO_SYNC_CREDENTIALS";

        #endregion
    }
}

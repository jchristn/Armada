namespace Armada.Publisher.Build
{
    /// <summary>
    /// The result of a self-contained publish for a single runtime identifier.
    /// </summary>
    public class PublishedArtifact
    {
        #region Public-Members

        /// <summary>
        /// Runtime identifier that was published (for example "win-x64").
        /// </summary>
        public string RuntimeIdentifier { get; set; } = string.Empty;

        /// <summary>
        /// Absolute path to the publish output directory.
        /// </summary>
        public string OutputDirectory { get; set; } = string.Empty;

        /// <summary>
        /// Absolute path to the produced single-file binary.
        /// </summary>
        public string BinaryPath { get; set; } = string.Empty;

        /// <summary>
        /// Lowercase hexadecimal SHA-256 of the produced binary.
        /// </summary>
        public string Sha256 { get; set; } = string.Empty;

        #endregion
    }
}

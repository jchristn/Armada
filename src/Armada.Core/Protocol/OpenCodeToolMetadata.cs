namespace Armada.Core.Protocol
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Metadata of an OpenCode tool state.
    /// </summary>
    public class OpenCodeToolMetadata
    {
        #region Public-Members

        /// <summary>
        /// Process exit code for a shell tool, or null.
        /// </summary>
        [JsonPropertyName("exit")]
        public int? Exit { get; set; } = null;

        #endregion
    }
}

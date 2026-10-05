namespace Armada.Core.Protocol
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// The <c>content</c> of a Claude <c>tool_result</c> block, which the protocol allows to be either a string or an
    /// array of content blocks. <see cref="ClaudeToolResultContentConverter"/> reads both forms into <see cref="Text"/>.
    /// </summary>
    [JsonConverter(typeof(ClaudeToolResultContentConverter))]
    public class ClaudeToolResultContent
    {
        #region Public-Members

        /// <summary>
        /// The result text: the string form verbatim, or the text blocks of the array form joined with newlines.
        /// </summary>
        public string Text { get; set; } = String.Empty;

        #endregion
    }
}

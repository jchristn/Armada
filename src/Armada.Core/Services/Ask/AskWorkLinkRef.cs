namespace Armada.Core.Services.Ask
{
    /// <summary>
    /// A nested object carrying an identifier inside a tool result (for example the batch of discover_vessels).
    /// </summary>
    public class AskWorkLinkRef
    {
        #region Public-Members

        /// <summary>
        /// Identifier, or null.
        /// </summary>
        public string? Id { get; set; } = null;

        #endregion
    }
}

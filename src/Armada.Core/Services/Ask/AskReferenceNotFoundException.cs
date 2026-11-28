namespace Armada.Core.Services.Ask
{
    using System;

    /// <summary>
    /// An Ask request names a captain or Harbor that does not exist or that the caller cannot see. It stays an
    /// <see cref="ArgumentException"/> (REST answers 400 as before); MCP reports it as NotFound by type, not by message.
    /// </summary>
    public class AskReferenceNotFoundException : ArgumentException
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="message">Message.</param>
        /// <param name="paramName">Parameter name, or null.</param>
        public AskReferenceNotFoundException(string message, string? paramName = null)
            : base(message, paramName)
        {
        }
    }
}

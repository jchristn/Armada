namespace Armada.Core.Database
{
    using System;

    /// <summary>
    /// One row of a grouped tool call count: a message id and the number of tool calls it has.
    /// </summary>
    internal class ToolCallCountRow
    {
        /// <summary>
        /// Message identifier.
        /// </summary>
        internal string MessageId { get; set; } = String.Empty;

        /// <summary>
        /// Number of tool calls.
        /// </summary>
        internal int Count { get; set; } = 0;
    }
}

namespace Test.Shared.Infrastructure
{
    using Armada.Core.Models;

    /// <summary>
    /// The error body of a black box REST route, as read by the black box API suites: Error (the result name),
    /// StatusCode, Message, and Data (a <see cref="BlackBoxErrorDetail"/> carrying Code and Field).
    /// </summary>
    public class BlackBoxApiErrorBody
    {
        /// <summary>
        /// Result name (for example NotFound, Conflict, BadRequest, NotImplemented).
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// Message.
        /// </summary>
        public string? Message { get; set; } = null;

        /// <summary>
        /// Detail with the stable code and field.
        /// </summary>
        public BlackBoxErrorDetail? Data { get; set; } = null;
    }
}

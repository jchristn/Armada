namespace Armada.Client.Socket
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// One WebSocket connection used by <see cref="ArmadaSocket"/>. The default implementation wraps
    /// <see cref="System.Net.WebSockets.ClientWebSocket"/>; tests substitute a fake to drive reconnects.
    /// </summary>
    public interface IArmadaSocketTransport : IDisposable
    {
        /// <summary>
        /// Open the connection.
        /// </summary>
        /// <param name="uri">ws(s) URI including the <c>token</c> query parameter.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A task that completes when the connection is open.</returns>
        Task ConnectAsync(Uri uri, CancellationToken token);

        /// <summary>
        /// Receive the next complete text message.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The text, or null when the connection closed.</returns>
        Task<string?> ReceiveTextAsync(CancellationToken token);

        /// <summary>
        /// Send a text message.
        /// </summary>
        /// <param name="text">Text to send.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A task that completes when sent.</returns>
        Task SendTextAsync(string text, CancellationToken token);

        /// <summary>
        /// Close the connection gracefully when possible.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A task that completes when closed.</returns>
        Task CloseAsync(CancellationToken token);
    }
}

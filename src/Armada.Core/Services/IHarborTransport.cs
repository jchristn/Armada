namespace Armada.Core.Services
{
    /// <summary>
    /// A bidirectional text transport for the Harbor link. Abstracted from the concrete WebSocket so the
    /// link client's protocol logic can be exercised without a live socket.
    /// </summary>
    public interface IHarborTransport
    {
        /// <summary>
        /// Open the transport.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        Task ConnectAsync(CancellationToken token);

        /// <summary>
        /// Send a text frame.
        /// </summary>
        /// <param name="text">Frame text.</param>
        /// <param name="token">Cancellation token.</param>
        Task SendAsync(string text, CancellationToken token);

        /// <summary>
        /// Receive the next text frame, or null when the transport has closed.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The frame text, or null on close.</returns>
        Task<string?> ReceiveAsync(CancellationToken token);

        /// <summary>
        /// Close the transport.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        Task CloseAsync(CancellationToken token);
    }
}

namespace Armada.Core.Services
{
    using Armada.Core.Harbor;

    /// <summary>
    /// Sends a message to a connected Harbor over its link. Supplied by the transport (the WebSocket
    /// handler) when a Harbor connects, so the connection manager and executor can push commands without
    /// depending on the web server.
    /// </summary>
    /// <param name="message">Message to send.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>Task.</returns>
    public delegate Task HarborSendDelegate(HarborMessage message, CancellationToken token);
}

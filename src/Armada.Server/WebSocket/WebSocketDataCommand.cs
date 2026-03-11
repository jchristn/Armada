namespace Armada.Server.WebSocket
{
    /// <summary>
    /// Generic WebSocket command wrapper for commands that include typed data.
    /// Used for create/update commands where the data field contains an entity.
    /// </summary>
    /// <typeparam name="T">Type of the data payload.</typeparam>
    public class WebSocketDataCommand<T>
    {
        /// <summary>
        /// Typed data payload.
        /// </summary>
        public T? Data { get; set; }
    }
}

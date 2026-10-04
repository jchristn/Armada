namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client.Socket;

    /// <summary>
    /// Scripted socket transport: connects (or fails), returns queued messages, then reports a close.
    /// </summary>
    public sealed class FakeSocketTransport : IArmadaSocketTransport
    {
        /// <summary>
        /// URIs connected to.
        /// </summary>
        public static ConcurrentQueue<Uri> Connected { get; } = new ConcurrentQueue<Uri>();

        /// <summary>
        /// Text sent.
        /// </summary>
        public static ConcurrentQueue<string> Sent { get; } = new ConcurrentQueue<string>();

        private readonly bool _Fail;
        private readonly Queue<string> _Messages;

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="fail">Fail to connect.</param>
        /// <param name="messages">Messages to deliver before closing.</param>
        public FakeSocketTransport(bool fail, params string[] messages)
        {
            _Fail = fail;
            _Messages = new Queue<string>(messages);
        }

        /// <inheritdoc />
        public Task ConnectAsync(Uri uri, CancellationToken token)
        {
            if (_Fail) throw new System.Net.WebSockets.WebSocketException("refused");
            Connected.Enqueue(uri);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public async Task<string?> ReceiveTextAsync(CancellationToken token)
        {
            await Task.Delay(5, token).ConfigureAwait(false);
            return _Messages.Count > 0 ? _Messages.Dequeue() : null;
        }

        /// <inheritdoc />
        public Task SendTextAsync(string text, CancellationToken token)
        {
            Sent.Enqueue(text);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task CloseAsync(CancellationToken token)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public void Dispose()
        {
        }
    }
}

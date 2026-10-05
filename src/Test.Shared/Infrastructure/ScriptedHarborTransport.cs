namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Services;

    /// <summary>
    /// Harbor transport whose inbound side is a script: queued messages are returned in order, then the transport
    /// either reports a clean close (null) or, when <see cref="FailAfterScript"/> is set, waits briefly and throws an
    /// <see cref="IOException"/> as a dropped socket would. Thread safe for one reader and concurrent senders.
    /// </summary>
    public sealed class ScriptedHarborTransport : IHarborTransport
    {
        #region Public-Members

        /// <summary>
        /// Raw messages sent by the client, in order.
        /// </summary>
        public ConcurrentQueue<string> Sent { get; } = new ConcurrentQueue<string>();

        /// <summary>
        /// When true, the receive after the scripted messages throws instead of returning null.
        /// </summary>
        public bool FailAfterScript { get; set; } = false;

        /// <summary>
        /// Delay before the scripted end of the inbound stream, in milliseconds.
        /// </summary>
        public int EndDelayMs { get; set; } = 0;

        /// <summary>
        /// When set, the scripted end of the inbound stream waits until the client has sent a message matching this
        /// predicate (bounded by <see cref="EndWaitTimeoutMs"/>), so a test can end the link after a condition rather
        /// than after a fixed delay.
        /// </summary>
        public Func<string, bool>? EndAfterSent { get; set; } = null;

        /// <summary>
        /// Upper bound, in milliseconds, on the wait for <see cref="EndAfterSent"/>.
        /// </summary>
        public int EndWaitTimeoutMs { get; set; } = 10000;

        /// <summary>
        /// True after the client closed the transport.
        /// </summary>
        public bool Closed { get; private set; } = false;

        #endregion

        #region Private-Members

        private readonly Queue<string> _Inbound = new Queue<string>();
        private readonly TaskCompletionSource<bool> _EndSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Queue an inbound message.
        /// </summary>
        /// <param name="message">Serialized message.</param>
        public void Enqueue(string message)
        {
            _Inbound.Enqueue(message);
        }

        /// <inheritdoc />
        public Task ConnectAsync(CancellationToken token)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task SendAsync(string text, CancellationToken token)
        {
            Sent.Enqueue(text);
            Func<string, bool>? endAfterSent = EndAfterSent;
            if (endAfterSent != null && endAfterSent(text)) _EndSignal.TrySetResult(true);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public async Task<string?> ReceiveAsync(CancellationToken token)
        {
            if (_Inbound.Count > 0) return _Inbound.Dequeue();
            if (EndDelayMs > 0) await Task.Delay(EndDelayMs, token).ConfigureAwait(false);
            if (EndAfterSent != null)
            {
                await Task.WhenAny(_EndSignal.Task, Task.Delay(EndWaitTimeoutMs, token)).ConfigureAwait(false);
            }
            if (FailAfterScript) throw new IOException("Simulated link drop");
            return null;
        }

        /// <inheritdoc />
        public Task CloseAsync(CancellationToken token)
        {
            Closed = true;
            return Task.CompletedTask;
        }

        #endregion
    }
}

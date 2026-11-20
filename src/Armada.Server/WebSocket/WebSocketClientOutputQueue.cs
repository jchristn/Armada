namespace Armada.Server.WebSocket
{
    using System;
    using System.Threading;
    using System.Threading.Channels;
    using System.Threading.Tasks;

    /// <summary>
    /// Ordered outbound frames for one WebSocket client. Callers enqueue without waiting for the network send.
    /// One reader preserves frame order. A full queue or a failed send stops this client only.
    /// </summary>
    public sealed class WebSocketClientOutputQueue : IDisposable
    {
        #region Private-Members

        private readonly Channel<string> _Frames;
        private readonly Func<string, Task> _Send;
        private readonly Action<Exception?> _Stopped;
        private int _Accepting = 1;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Create a bounded output queue for one connection.
        /// </summary>
        /// <param name="send">Sends one frame. The queue awaits it so frames stay in order.</param>
        /// <param name="stopped">Called once when the queue overflows or a send fails. Must not wait for an in-flight send.</param>
        /// <param name="capacity">Maximum number of frames waiting to send. At least one.</param>
        /// <exception cref="ArgumentNullException">Thrown when a callback is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when capacity is below one.</exception>
        public WebSocketClientOutputQueue(Func<string, Task> send, Action<Exception?> stopped, int capacity)
        {
            _Send = send ?? throw new ArgumentNullException(nameof(send));
            _Stopped = stopped ?? throw new ArgumentNullException(nameof(stopped));
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));

            _Frames = Channel.CreateBounded<string>(new BoundedChannelOptions(capacity)
            {
                // TryWrite returns false when the channel is full and does not block.
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });
            _ = SendLoopAsync();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Queue one frame. Returns without waiting for the network send.
        /// </summary>
        /// <param name="frame">Serialized frame.</param>
        /// <returns>True when the frame was accepted. False when the queue is stopped or full; a full queue stops the client.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the frame is null.</exception>
        public bool TryEnqueue(string frame)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            if (Volatile.Read(ref _Accepting) == 0) return false;
            if (_Frames.Writer.TryWrite(frame)) return true;
            Stop(null);
            return false;
        }

        /// <summary>
        /// Stop accepting frames. Does not wait for the current send.
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _Accepting, 0) == 1)
                _Frames.Writer.TryComplete();
        }

        #endregion

        #region Private-Methods

        private async Task SendLoopAsync()
        {
            try
            {
                await foreach (string frame in _Frames.Reader.ReadAllAsync().ConfigureAwait(false))
                {
                    await _Send(frame).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Stop(ex);
            }
        }

        private void Stop(Exception? error)
        {
            if (Interlocked.Exchange(ref _Accepting, 0) == 0) return;
            _Frames.Writer.TryComplete(error);
            _Stopped(error);
        }

        #endregion
    }
}

namespace Armada.Proxy.Services
{
    using System.Collections.Concurrent;
    using Armada.Core;
    using Armada.Core.Models;

    /// <summary>
    /// Active bidirectional session for a connected Armada instance.
    /// </summary>
    public class RemoteInstanceSession
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="sender">Envelope sender delegate.</param>
        public RemoteInstanceSession(Func<RemoteTunnelEnvelope, CancellationToken, Task> sender)
        {
            _Sender = sender ?? throw new ArgumentNullException(nameof(sender));
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// Number of pending response waiters.
        /// </summary>
        public int PendingRequestCount => _PendingRequests.Count;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Send an envelope over the session transport.
        /// </summary>
        public async Task SendAsync(RemoteTunnelEnvelope envelope, CancellationToken token)
        {
            await _Sender(envelope, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Send a request and wait for the correlated response. One deadline, started before the
        /// send, bounds both the send and the response wait, so a sender that blocks still times
        /// out. The pending entry is removed on every exit.
        /// </summary>
        /// <param name="method">Request method name.</param>
        /// <param name="payload">Optional request payload.</param>
        /// <param name="timeout">Deadline for the send and the response wait.</param>
        /// <param name="token">Caller cancellation token.</param>
        /// <param name="requesterIp">Optional requester address forwarded on the envelope.</param>
        /// <returns>The correlated response envelope.</returns>
        /// <exception cref="TimeoutException">The deadline passed before the response arrived.</exception>
        /// <exception cref="OperationCanceledException">The caller's token was cancelled.</exception>
        public async Task<RemoteTunnelEnvelope> SendRequestAsync(string method, object? payload, TimeSpan timeout, CancellationToken token, string? requesterIp = null)
        {
            string correlationId = Guid.NewGuid().ToString("N");
            TaskCompletionSource<RemoteTunnelEnvelope> tcs = new TaskCompletionSource<RemoteTunnelEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
            _PendingRequests[correlationId] = tcs;

            using (CancellationTokenSource deadline = new CancellationTokenSource(timeout))
            using (CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token))
            {
                try
                {
                    Task send = SendAsync(RemoteTunnelProtocol.CreateRequest(method, payload, correlationId, requesterIp), linked.Token);
                    ObserveFault(send);
                    await send.WaitAsync(linked.Token).ConfigureAwait(false);
                    return await tcs.Task.WaitAsync(linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested && deadline.IsCancellationRequested)
                {
                    throw new TimeoutException("Timed out waiting for tunnel response to " + method + ".");
                }
                finally
                {
                    _PendingRequests.TryRemove(correlationId, out TaskCompletionSource<RemoteTunnelEnvelope>? _);
                }
            }
        }

        /// <summary>
        /// Complete a pending response if the correlation matches.
        /// </summary>
        public bool TryCompleteResponse(RemoteTunnelEnvelope envelope)
        {
            if (String.IsNullOrWhiteSpace(envelope.CorrelationId))
            {
                return false;
            }

            if (_PendingRequests.TryRemove(envelope.CorrelationId, out TaskCompletionSource<RemoteTunnelEnvelope>? waiter))
            {
                waiter.TrySetResult(envelope);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Fail all pending requests because the transport has gone away.
        /// </summary>
        public void FailAll(Exception ex)
        {
            foreach (KeyValuePair<string, TaskCompletionSource<RemoteTunnelEnvelope>> entry in _PendingRequests.ToArray())
            {
                if (_PendingRequests.TryRemove(entry.Key, out TaskCompletionSource<RemoteTunnelEnvelope>? waiter))
                {
                    waiter.TrySetException(ex);
                }
            }
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// A send abandoned at the deadline can still fault later. Observe that fault so it is not
        /// raised as an unobserved task exception.
        /// </summary>
        /// <param name="send">Send task that may outlive the request.</param>
        private static void ObserveFault(Task send)
        {
            _ = send.ContinueWith(
                completed =>
                {
                    Exception? observed = completed.Exception;
                    _ = observed;
                },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        #endregion

        #region Private-Members

        private readonly Func<RemoteTunnelEnvelope, CancellationToken, Task> _Sender;
        private readonly ConcurrentDictionary<string, TaskCompletionSource<RemoteTunnelEnvelope>> _PendingRequests = new ConcurrentDictionary<string, TaskCompletionSource<RemoteTunnelEnvelope>>();

        #endregion
    }
}

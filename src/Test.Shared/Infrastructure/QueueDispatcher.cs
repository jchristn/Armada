namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Concurrent;
    using Armada.Tui.Services;

    /// <summary>
    /// Dispatcher that queues posted actions until <see cref="Drain"/> runs them (a stand-in for the UI loop).
    /// </summary>
    public sealed class QueueDispatcher : IUiDispatcher
    {
        private readonly ConcurrentQueue<Action> _Queue = new ConcurrentQueue<Action>();

        /// <inheritdoc />
        public void Post(Action action)
        {
            _Queue.Enqueue(action);
        }

        /// <summary>
        /// Run queued actions.
        /// </summary>
        /// <returns>Actions run.</returns>
        public int Drain()
        {
            int n = 0;
            while (_Queue.TryDequeue(out Action? a))
            {
                a();
                n++;
            }

            return n;
        }
    }
}

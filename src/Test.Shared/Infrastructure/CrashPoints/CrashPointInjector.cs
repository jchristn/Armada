namespace Test.Shared.Infrastructure.CrashPoints
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Services;

    /// <summary>
    /// A crash point injector for tests: records every point reached and, when armed, freezes the calling flow forever
    /// the first time the armed point is reached. Freezing (rather than throwing) is what a dead process looks like:
    /// no later statement, catch block, or finally block of that flow runs. Install it with
    /// <see cref="CrashPoints.Install"/>; <see cref="CrashRun"/> does this and abandons the frozen operation.
    /// </summary>
    /// <remarks>
    /// Thread safety: hits are recorded under a lock. A frozen flow keeps one thread-pool thread blocked for the life
    /// of the test process; crash tests are few, so this is accepted.
    /// </remarks>
    public sealed class CrashPointInjector : ICrashPointInjector
    {
        /// <summary>The armed crash point, or null to only observe.</summary>
        public string? Armed { get; }

        /// <summary>Completes when the armed point fires.</summary>
        public Task FiredTask => _FiredSource.Task;

        /// <summary>True once the armed point has fired.</summary>
        public bool Fired => _FiredSource.Task.IsCompleted;

        /// <summary>Every crash point reached, in order.</summary>
        public List<string> Hits
        {
            get { lock (_Lock) return new List<string>(_Hits); }
        }

        private readonly object _Lock = new object();
        private readonly List<string> _Hits = new List<string>();
        private readonly TaskCompletionSource<bool> _FiredSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private static readonly ManualResetEventSlim _Never = new ManualResetEventSlim(false);

        /// <summary>
        /// Create an injector.
        /// </summary>
        /// <param name="armed">The crash point to fire, or null to only observe.</param>
        public CrashPointInjector(string? armed)
        {
            Armed = armed;
        }

        /// <summary>
        /// Record the hit, and freeze the flow when it is the armed point reached for the first time.
        /// </summary>
        /// <param name="name">The crash point name.</param>
        public void OnHit(string name)
        {
            lock (_Lock)
            {
                _Hits.Add(name);
                if (_FiredSource.Task.IsCompleted || !String.Equals(name, Armed, StringComparison.Ordinal)) return;
            }

            _FiredSource.TrySetResult(true);
            _Never.Wait();
        }
    }
}

namespace Armada.Core.Services
{
    using System;

    /// <summary>
    /// Restores the previous crash point injector when disposed. Returned by <see cref="CrashPoints.Install"/>.
    /// </summary>
    internal sealed class CrashPointScope : IDisposable
    {
        private readonly ICrashPointInjector? _Previous;
        private bool _Disposed;

        /// <summary>
        /// Remember the injector to restore.
        /// </summary>
        /// <param name="previous">The injector in place before the install, or null.</param>
        public CrashPointScope(ICrashPointInjector? previous)
        {
            _Previous = previous;
        }

        /// <summary>
        /// Restore the previous injector. Later calls do nothing.
        /// </summary>
        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;
            CrashPoints.SetInjector(_Previous);
        }
    }
}

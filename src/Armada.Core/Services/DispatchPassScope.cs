namespace Armada.Core.Services
{
    using Armada.Core.Models;

    /// <summary>
    /// Restores the enclosing dispatch pass cache when a nested pass ends.
    /// </summary>
    internal sealed class DispatchPassScope : IDisposable
    {
        private readonly MissionService _Service;
        private readonly DispatchPassCache? _Outer;
        private bool _Disposed;
        internal DispatchPassScope(MissionService service, DispatchPassCache? outer) { _Service = service; _Outer = outer; }
        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;
            _Service._Pass.Value = _Outer;
        }
    }
}

namespace Armada.Core.Services
{
    using System;
    using System.Threading;

    /// <summary>
    /// Named points between the writes of a multi-step operation, where a crash of the Admiral would leave partial state
    /// behind. Production code calls <see cref="Hit(string)"/> at each point; it does nothing unless a test has installed
    /// an <see cref="ICrashPointInjector"/> for the current asynchronous flow with <see cref="Install"/>. Tests use this
    /// to prove that startup recovery and the sweeps bring every partial state to the same end state as an uninterrupted
    /// run (RELIABILITY_SIMPLICITY_PLAN.md RS-1.3, gate measure G9).
    /// </summary>
    /// <remarks>
    /// Thread safety: the injector is held in an <see cref="AsyncLocal{T}"/>, so it applies only to the flow that
    /// installed it and the tasks that flow starts. Concurrent tests do not see each other's injectors.
    /// </remarks>
    public static class CrashPoints
    {
        private static readonly AsyncLocal<ICrashPointInjector?> _Injector = new AsyncLocal<ICrashPointInjector?>();

        /// <summary>
        /// Report that execution reached a named crash point. Does nothing when no injector is installed on this flow.
        /// </summary>
        /// <param name="name">The crash point name, one of the <see cref="CrashPointNames"/> constants.</param>
        /// <exception cref="ArgumentNullException">When <paramref name="name"/> is null or empty.</exception>
        public static void Hit(string name)
        {
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            ICrashPointInjector? injector = _Injector.Value;
            if (injector != null) injector.OnHit(name);
        }

        /// <summary>
        /// Install an injector for the current asynchronous flow and the tasks it starts. Dispose the result to remove
        /// it. Intended for tests only.
        /// </summary>
        /// <param name="injector">The injector.</param>
        /// <returns>A handle that restores the previous injector when disposed.</returns>
        /// <exception cref="ArgumentNullException">When <paramref name="injector"/> is null.</exception>
        public static IDisposable Install(ICrashPointInjector injector)
        {
            if (injector == null) throw new ArgumentNullException(nameof(injector));
            ICrashPointInjector? previous = _Injector.Value;
            _Injector.Value = injector;
            return new CrashPointScope(previous);
        }

        internal static void SetInjector(ICrashPointInjector? injector)
        {
            _Injector.Value = injector;
        }
    }
}

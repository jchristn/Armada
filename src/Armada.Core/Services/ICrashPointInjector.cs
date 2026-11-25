namespace Armada.Core.Services
{
    /// <summary>
    /// Receives every <see cref="CrashPoints.Hit(string)"/> call on the current asynchronous flow. Tests install one to
    /// stop execution at a named point, as if the Admiral process had died there. Production code installs none.
    /// </summary>
    public interface ICrashPointInjector
    {
        /// <summary>
        /// Called when execution reaches a named crash point. An implementation that simulates a crash throws; one that
        /// only observes returns normally.
        /// </summary>
        /// <param name="name">The crash point name, one of the <see cref="CrashPointNames"/> constants.</param>
        void OnHit(string name);
    }
}

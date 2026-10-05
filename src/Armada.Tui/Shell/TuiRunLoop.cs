namespace Armada.Tui.Shell
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using TUIKit.Hosting;
    using TUIKit.Terminal;

    /// <summary>
    /// The TUI's input and render loop (W8.5): the same steps as TUIKit's <c>RunAsync</c> (pump input and posted work,
    /// compose, sleep), but a <see cref="FrameGovernor"/> decides whether each tick composes a frame and how long the
    /// loop sleeps, so an idle TUI wakes about 40 times a second to poll input and composes a few frames a second
    /// instead of 60. TUIKit's loop has no hook for this (TUIKit gap U8). The loop ends on <see cref="RequestStop"/>,
    /// cancellation, or Ctrl+C delivered as a signal. Use one instance per run; not reentrant.
    /// </summary>
    public class TuiRunLoop
    {
        #region Public-Members

        /// <summary>
        /// True after <see cref="RequestStop"/>.
        /// </summary>
        public bool StopRequested
        {
            get { return Volatile.Read(ref _Stop); }
        }

        #endregion

        #region Private-Members

        private readonly TuiApplication _App;
        private readonly ITerminalBackend _Backend;
        private readonly FrameGovernor _Frames;
        private bool _Stop = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="app">Application (started by <see cref="RunAsync"/> when needed).</param>
        /// <param name="backend">The backend the application writes to (for the terminal size).</param>
        /// <param name="frames">Frame governor.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public TuiRunLoop(TuiApplication app, ITerminalBackend backend, FrameGovernor frames)
        {
            _App = app ?? throw new ArgumentNullException(nameof(app));
            _Backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _Frames = frames ?? throw new ArgumentNullException(nameof(frames));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Ask the loop to end after the current tick. Thread-safe.
        /// </summary>
        public void RequestStop()
        {
            Volatile.Write(ref _Stop, true);
            _App.RequestStop();
        }

        /// <summary>
        /// Run until <see cref="RequestStop"/> or cancellation.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A task that completes when the loop ends.</returns>
        public async Task RunAsync(CancellationToken token = default)
        {
            _App.Start();
            ConsoleCancelEventHandler onCancel = (s, e) => Volatile.Write(ref _Stop, true);
            Console.CancelKeyPress += onCancel;
            bool wasSuspended = false;
            try
            {
                while (!StopRequested && !token.IsCancellationRequested)
                {
                    _App.PumpInputOnce();
                    if (StopRequested) break;
                    bool suspended = _App.IsSuspended;
                    if (wasSuspended && !suspended) _Frames.Invalidate();
                    wasSuspended = suspended;
                    if (_Frames.ShouldRender(_Backend.Size)) _App.RenderOnce();
                    await Task.Delay(_Frames.NextDelayMs(), token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation.
            }
            finally
            {
                Console.CancelKeyPress -= onCancel;
            }
        }

        #endregion
    }
}

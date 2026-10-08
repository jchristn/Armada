namespace Armada.Server
{
    using System;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;
    using Armada.Runtimes;
    using Armada.Runtimes.Interfaces;
    using SyslogLogging;

    /// <summary>
    /// Runs a captain's agent once on a prompt in a scratch directory on the Admiral host and waits for it. Uses the
    /// same launch sequence as the captain chat endpoint (<see cref="CaptainChatService"/>): a runtime from
    /// <see cref="AgentRuntimeFactory"/>, StartAsync with the captain's model and options, a wait on the process-exit
    /// event raced against a time limit and the caller's token, and StopAsync on timeout or cancellation. It does not
    /// touch captain state, docks, or missions; callers reserve the captain. Harbor-hosted execution is not used:
    /// callers read files the agent writes in the scratch directory, which must be local. So when requireHarborForLaunch
    /// is on (captains never run on the Admiral host) the run is refused, and a CLI missing on the Admiral host is
    /// reported as such.
    /// </summary>
    public class CaptainPromptRunner : ICaptainPromptRunner
    {
        #region Public-Members

        /// <summary>
        /// Maximum characters of standard output kept for diagnostics (the tail is kept). Default 65536, minimum 1024,
        /// maximum 4194304.
        /// </summary>
        public int MaxOutputChars
        {
            get => _MaxOutputChars;
            set => _MaxOutputChars = Math.Clamp(value, 1024, 4194304);
        }

        /// <summary>
        /// Admiral settings; when set and requireHarborForLaunch is on, runs are refused because they would run the
        /// captain on the Admiral host. Null never refuses.
        /// </summary>
        public Armada.Core.Settings.ArmadaSettings? Settings { get; set; } = null;

        #endregion

        #region Private-Members

        private readonly string _Header = "[CaptainPromptRunner] ";
        private readonly AgentRuntimeFactory _RuntimeFactory;
        private readonly LoggingModule _Logging;
        private int _MaxOutputChars = 65536;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="runtimeFactory">Agent runtime factory.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
        public CaptainPromptRunner(AgentRuntimeFactory runtimeFactory, LoggingModule logging)
        {
            _RuntimeFactory = runtimeFactory ?? throw new ArgumentNullException(nameof(runtimeFactory));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<CaptainPromptResult> RunAsync(
            Captain captain,
            string workingDirectory,
            string prompt,
            TimeSpan timeout,
            string? logFilePath,
            Func<int, Task>? onStarted,
            CancellationToken token = default)
        {
            if (captain == null) throw new ArgumentNullException(nameof(captain));
            if (String.IsNullOrEmpty(workingDirectory)) throw new ArgumentNullException(nameof(workingDirectory));
            if (prompt == null) throw new ArgumentNullException(nameof(prompt));

            CaptainPromptResult result = new CaptainPromptResult();
            if (Settings != null && Settings.RequireHarborForLaunch)
            {
                result.Error = "This captain run needs the Admiral host (it reads files the captain writes there), and requireHarborForLaunch is on, "
                    + "so captains never run on the Admiral host. Turn requireHarborForLaunch off to run it.";
                return result;
            }

            StringBuilder output = new StringBuilder();
            object outputLock = new object();
            TaskCompletionSource<int?> exitSource = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
            string finalMessagePath = Path.Combine(workingDirectory, ".final-message.txt");

            IAgentRuntime runtime;
            try
            {
                runtime = _RuntimeFactory.Create(captain.Runtime);
            }
            catch (Exception ex)
            {
                result.Error = "Runtime " + captain.Runtime + " is not available: " + ex.Message;
                return result;
            }

            runtime.OnStdoutReceived += (int pid, string line) =>
            {
                lock (outputLock)
                {
                    output.Append(line).Append('\n');
                    if (output.Length > _MaxOutputChars * 2) output.Remove(0, output.Length - _MaxOutputChars);
                }
            };
            runtime.OnProcessExited += (int pid, int? code) => exitSource.TrySetResult(code);

            int processId;
            try
            {
                processId = await runtime.StartAsync(
                    workingDirectory,
                    prompt,
                    environment: null,
                    logFilePath: logFilePath,
                    finalMessageFilePath: finalMessagePath,
                    model: captain.Model,
                    captain: captain,
                    token: token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                result.Cancelled = true;
                return result;
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "could not start captain " + captain.Id + ": " + ex.Message);
                result.Error = CaptainLaunchRouter.TryDescribeFailure(ex, out string described, out Armada.Core.Enums.CaptainChatErrorCodeEnum _)
                    ? described
                    : ex.Message;
                return result;
            }

            result.ProcessId = processId;
            if (onStarted != null)
            {
                try { await onStarted(processId).ConfigureAwait(false); }
                catch (Exception ex) { _Logging.Warn(_Header + "start callback failed: " + ex.Message); }
            }

            using (CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                limit.CancelAfter(timeout);
                Task finished = await Task.WhenAny(exitSource.Task, Task.Delay(Timeout.Infinite, limit.Token)).ConfigureAwait(false);
                if (finished != exitSource.Task)
                {
                    if (token.IsCancellationRequested) result.Cancelled = true;
                    else result.TimedOut = true;

                    _Logging.Info(_Header + "stopping captain " + captain.Id + " process " + processId + (result.Cancelled ? " (cancelled)" : " (time limit reached)"));
                    try
                    {
                        await Task.Run(() => runtime.StopAsync(processId, CancellationToken.None)).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _Logging.Warn(_Header + "stopping process " + processId + " failed: " + ex.Message);
                    }
                }
                else
                {
                    result.ExitCode = await exitSource.Task.ConfigureAwait(false);
                }
            }

            lock (outputLock)
            {
                string text = output.ToString();
                result.Output = text.Length > _MaxOutputChars ? text.Substring(text.Length - _MaxOutputChars) : text;
            }

            try
            {
                if (File.Exists(finalMessagePath)) result.FinalMessage = await File.ReadAllTextAsync(finalMessagePath, CancellationToken.None).ConfigureAwait(false);
            }
            catch (IOException)
            {
                // The final message is optional; stdout remains available.
            }

            return result;
        }

        #endregion
    }
}

namespace Armada.Server
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using SyslogLogging;
    using Armada.Core.Enums;
    using Armada.Core.Harbor;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using Armada.Runtimes;
    using Armada.Runtimes.Interfaces;

    /// <summary>
    /// Runs one Evaluate probe prompt through the captain's own runtime, the same way other interactive launches do
    /// (Model Context builds, chat): routed through <see cref="CaptainLaunchRouter"/> as a Harbor job of kind
    /// <see cref="HarborJobKindEnum.ModelProbe"/> when a router is set, otherwise created on the Admiral by the runtime
    /// factory. Each exchange runs in a fresh temporary directory (a Harbor launch uses its own scratch directory) that
    /// is deleted afterwards. A launch that cannot start is returned as <see cref="ModelProbeExchange.NotStarted"/>; this
    /// executor never throws for a launch failure.
    /// </summary>
    public class ModelProbeExecutor : IModelProbeExecutor
    {
        #region Public-Members

        /// <summary>
        /// Routes launches to a connected Harbor, or null to run every exchange on the Admiral.
        /// </summary>
        public CaptainLaunchRouter? LaunchRouter { get; set; } = null;

        /// <summary>
        /// When set and returning an executor, every exchange is answered by that executor instead of a runtime (tests use
        /// it to supply canned model answers).
        /// </summary>
        public Func<IModelProbeExecutor?>? OverrideSource { get; set; } = null;

        /// <summary>
        /// Most stdout characters kept per exchange.
        /// </summary>
        public const int MaxOutputChars = 400000;

        #endregion

        #region Private-Members

        private string _Header = "[ModelProbeExecutor] ";
        private readonly AgentRuntimeFactory _RuntimeFactory;
        private readonly LoggingModule _Logging;
        private int _Sequence = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="runtimeFactory">Creates runtimes on the Admiral.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public ModelProbeExecutor(AgentRuntimeFactory runtimeFactory, LoggingModule logging)
        {
            _RuntimeFactory = runtimeFactory ?? throw new ArgumentNullException(nameof(runtimeFactory));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run one probe prompt and capture what the runtime printed.
        /// </summary>
        /// <param name="request">The exchange.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The exchange result; NotStarted with a reason when the runtime could not be launched.</returns>
        /// <exception cref="ArgumentNullException">Thrown when request is null.</exception>
        public async Task<ModelProbeExchange> RunAsync(ModelProbeExchangeRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            IModelProbeExecutor? substitute = OverrideSource?.Invoke();
            if (substitute != null) return await substitute.RunAsync(request, token).ConfigureAwait(false);

            int sequence = Interlocked.Increment(ref _Sequence);
            string runFolder = String.IsNullOrWhiteSpace(request.RunId) ? "unassigned" : request.RunId;
            string workingDirectory = Path.Combine(Path.GetTempPath(), "armada-probes", runFolder, sequence.ToString(System.Globalization.CultureInfo.InvariantCulture));
            string finalMessageFilePath = Path.Combine(Path.GetTempPath(), "armada-probes", runFolder, "final-" + sequence.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".txt");

            try
            {
                Directory.CreateDirectory(workingDirectory);
                return await RunInAsync(request, workingDirectory, finalMessageFilePath, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "probe " + request.Label + " for captain " + request.Captain.Id + " could not run: " + ex.Message);
                return ModelProbeExchange.NotStarted(Describe(ex));
            }
            finally
            {
                TryDelete(workingDirectory, finalMessageFilePath);
            }
        }

        #endregion

        #region Private-Methods

        private async Task<ModelProbeExchange> RunInAsync(ModelProbeExchangeRequest request, string workingDirectory, string finalMessageFilePath, CancellationToken token)
        {
            Captain captain = request.Captain;
            IAgentRuntime runtime;
            string? harborId = null;
            try
            {
                if (LaunchRouter != null)
                {
                    CaptainLaunchContext launch = new CaptainLaunchContext(captain, "Evaluate probe (" + request.Label + ")")
                    {
                        Kind = HarborJobKindEnum.ModelProbe,
                        TenantId = request.TenantId,
                        UserId = request.UserId,
                        AllowScratchWorkingDirectory = true,
                        Display = new HarborLaunchDisplay { CaptainName = captain.Name, CaptainModel = captain.Model }
                    };

                    CaptainLaunchTarget target = await LaunchRouter.SelectAsync(launch, token).ConfigureAwait(false);
                    runtime = target.Runtime;
                    harborId = target.HarborId;
                }
                else
                {
                    runtime = _RuntimeFactory.Create(captain.Runtime);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                return ModelProbeExchange.NotStarted(Describe(ex));
            }

            TaskCompletionSource<int?> exitSource = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
            object outputLock = new object();
            StringBuilder output = new StringBuilder();
            Stopwatch clock = new Stopwatch();
            long? firstOutputMs = null;

            // stdout only, so CLI stderr banners never count as the model's answer.
            runtime.OnStdoutReceived += (pid, line) =>
            {
                lock (outputLock)
                {
                    if (!firstOutputMs.HasValue) firstOutputMs = clock.ElapsedMilliseconds;
                    if (output.Length < MaxOutputChars)
                    {
                        output.Append(line);
                        output.Append('\n');
                    }
                }
            };
            runtime.OnProcessExited += (pid, code) => exitSource.TrySetResult(code);

            int processId;
            clock.Start();
            try
            {
                processId = await runtime.StartAsync(
                    workingDirectory,
                    request.Prompt,
                    finalMessageFilePath: finalMessageFilePath,
                    model: captain.Model,
                    captain: captain,
                    token: token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                return ModelProbeExchange.NotStarted(Describe(ex));
            }

            bool timedOut = false;
            using (CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeoutCts.CancelAfter(request.TimeoutMs > 0 ? request.TimeoutMs : Timeout.Infinite);
                Task finished = await Task.WhenAny(exitSource.Task, Task.Delay(Timeout.Infinite, timeoutCts.Token)).ConfigureAwait(false);
                if (finished != exitSource.Task)
                {
                    timedOut = !token.IsCancellationRequested;
                    try
                    {
                        AgentStopResult stopped = await runtime.StopAsync(processId, CancellationToken.None).ConfigureAwait(false);
                        if (!stopped.Cleared)
                            _Logging.Warn(_Header + "refused to stop process " + processId + " reason " + stopped.Reason);
                    }
                    catch { }
                    if (token.IsCancellationRequested) throw new OperationCanceledException(token);
                }
            }

            clock.Stop();
            int? exitCode = timedOut ? null : await exitSource.Task.ConfigureAwait(false);

            // Prefer the runtime's final-message artifact (Mux/Codex write it); fall back to stdout (Claude Code prints the
            // answer to stdout in --print mode).
            string answer = String.Empty;
            try
            {
                if (File.Exists(finalMessageFilePath))
                {
                    string artifact = await File.ReadAllTextAsync(finalMessageFilePath, CancellationToken.None).ConfigureAwait(false);
                    if (!String.IsNullOrWhiteSpace(artifact)) answer = artifact.Trim();
                }
            }
            catch { }

            if (String.IsNullOrWhiteSpace(answer))
            {
                lock (outputLock) answer = output.ToString().Trim();
            }

            long? first;
            lock (outputLock) first = firstOutputMs;
            return new ModelProbeExchange
            {
                Output = answer,
                ExitCode = exitCode,
                TimedOut = timedOut,
                FirstOutputMs = first,
                DurationMs = clock.ElapsedMilliseconds,
                HarborId = harborId
            };
        }

        private static string Describe(Exception ex)
        {
            if (CaptainLaunchRouter.TryDescribeFailure(ex, out string message, out CaptainChatErrorCodeEnum _)) return message;
            if (ex is HarborLaunchUnavailableException) return ex.Message;
            return "The captain's runtime could not be launched: " + ex.Message;
        }

        private void TryDelete(string workingDirectory, string finalMessageFilePath)
        {
            try
            {
                if (Directory.Exists(workingDirectory)) Directory.Delete(workingDirectory, true);
                if (File.Exists(finalMessageFilePath)) File.Delete(finalMessageFilePath);
                string? runDirectory = Path.GetDirectoryName(workingDirectory);
                if (runDirectory != null && Directory.Exists(runDirectory) && Directory.GetFileSystemEntries(runDirectory).Length == 0)
                    Directory.Delete(runDirectory, false);
            }
            catch (Exception ex)
            {
                _Logging.Debug(_Header + "could not delete " + workingDirectory + ": " + ex.Message);
            }
        }

        #endregion
    }
}

namespace Armada.Core.Services.Health
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;

    /// <summary>
    /// Runs dependency tools (dotnet, npm) through an <see cref="IHostCommandExecutor"/> with a timeout, and reports a
    /// missing executable or a timeout as distinct outcomes instead of throwing. Thread-safe when the executor is.
    /// </summary>
    public class DependencyToolRunner
    {
        #region Private-Members

        private readonly IHostCommandExecutor _Executor;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="executor">Host command executor.</param>
        /// <exception cref="ArgumentNullException">Thrown when executor is null.</exception>
        public DependencyToolRunner(IHostCommandExecutor executor)
        {
            _Executor = executor ?? throw new ArgumentNullException(nameof(executor));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Resolve the platform-specific executable name for npm ("npm.cmd" on Windows, "npm" elsewhere).
        /// </summary>
        /// <returns>The npm executable name.</returns>
        public static string NpmExecutable()
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "npm.cmd" : "npm";
        }

        /// <summary>
        /// Run a tool.
        /// </summary>
        /// <param name="executable">Executable name or path.</param>
        /// <param name="arguments">Arguments (passed without shell interpretation).</param>
        /// <param name="workingDirectory">Working directory.</param>
        /// <param name="timeoutSeconds">Timeout in seconds (minimum 1).</param>
        /// <param name="token">Cancellation token. Cancellation propagates as OperationCanceledException.</param>
        /// <returns>The result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when executable or workingDirectory is null or empty.</exception>
        public async Task<DependencyToolResult> RunAsync(string executable, List<string> arguments, string workingDirectory, int timeoutSeconds, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(executable)) throw new ArgumentNullException(nameof(executable));
            if (String.IsNullOrEmpty(workingDirectory)) throw new ArgumentNullException(nameof(workingDirectory));

            HostCommandRequest request = new HostCommandRequest();
            request.Executable = executable;
            request.WorkingDirectory = workingDirectory;
            request.Arguments = arguments ?? new List<string>();
            request.TimeoutMs = Math.Max(1, timeoutSeconds) * 1000;

            HostCommandResult result;
            try
            {
                result = await _Executor.RunAsync(request, token).ConfigureAwait(false);
            }
            catch (Win32Exception)
            {
                return new DependencyToolResult { Outcome = DependencyToolOutcomeEnum.ToolMissing, ExitCode = -1 };
            }
            catch (FileNotFoundException)
            {
                return new DependencyToolResult { Outcome = DependencyToolOutcomeEnum.ToolMissing, ExitCode = -1 };
            }

            return new DependencyToolResult
            {
                Outcome = result.TimedOut ? DependencyToolOutcomeEnum.TimedOut : DependencyToolOutcomeEnum.Completed,
                ExitCode = result.ExitCode,
                StandardOutput = result.StandardOutput,
                StandardError = result.StandardError
            };
        }

        #endregion
    }
}

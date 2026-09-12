namespace Armada.Core.Services
{
    using System;
    using System.Diagnostics;
    using System.Text;

    /// <summary>
    /// Runs host commands in-process on the Admiral's own machine. This is the Local-mode (standalone)
    /// implementation of the host-command seam; the Split-mode implementation delegates the same request to a
    /// Harbor over its link.
    /// </summary>
    public class LocalHostCommandExecutor : IHostCommandExecutor
    {
        #region Public-Methods

        /// <inheritdoc />
        public async Task<HostCommandResult> RunAsync(HostCommandRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = request.Executable,
                WorkingDirectory = string.IsNullOrWhiteSpace(request.WorkingDirectory) ? Environment.CurrentDirectory : request.WorkingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = request.StandardInput != null,
                CreateNoWindow = true
            };

            foreach (string argument in request.Arguments)
                startInfo.ArgumentList.Add(argument);

            StringBuilder standardOutput = new StringBuilder();
            StringBuilder standardError = new StringBuilder();

            using (Process process = new Process())
            {
                process.StartInfo = startInfo;
                process.OutputDataReceived += (sender, e) => { if (e.Data != null) standardOutput.AppendLine(e.Data); };
                process.ErrorDataReceived += (sender, e) => { if (e.Data != null) standardError.AppendLine(e.Data); };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                if (request.StandardInput != null)
                {
                    await process.StandardInput.WriteAsync(request.StandardInput).ConfigureAwait(false);
                    process.StandardInput.Close();
                }

                using (CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    if (request.TimeoutMs > 0) linked.CancelAfter(request.TimeoutMs);

                    try
                    {
                        await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        TryKill(process);
                        if (token.IsCancellationRequested) throw;

                        return new HostCommandResult
                        {
                            ExitCode = -1,
                            StandardOutput = standardOutput.ToString(),
                            StandardError = standardError.ToString(),
                            TimedOut = true
                        };
                    }

                    return new HostCommandResult
                    {
                        ExitCode = process.ExitCode,
                        StandardOutput = standardOutput.ToString(),
                        StandardError = standardError.ToString(),
                        TimedOut = false
                    };
                }
            }
        }

        #endregion

        #region Private-Methods

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited) process.Kill(true);
            }
            catch
            {
            }
        }

        #endregion
    }
}

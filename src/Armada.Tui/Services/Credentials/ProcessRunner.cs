namespace Armada.Tui.Services.Credentials
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Runs a short-lived helper (security, secret-tool) with arguments passed as an argument list (no shell) and an
    /// optional secret on standard input, so secrets never appear on a command line. Thread-safe (stateless).
    /// </summary>
    public static class ProcessRunner
    {
        #region Public-Methods

        /// <summary>
        /// Run a process and capture its output.
        /// </summary>
        /// <param name="fileName">Executable.</param>
        /// <param name="arguments">Arguments.</param>
        /// <param name="stdin">Text written to standard input, or null.</param>
        /// <param name="timeoutMs">Timeout in milliseconds (the process is killed after it). Clamped to 500..60000.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The result; exit code -1 when the process could not start or timed out.</returns>
        public static async Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> arguments, string? stdin, int timeoutMs = 10000, CancellationToken token = default)
        {
            ProcessResult result = new ProcessResult();
            ProcessStartInfo psi = new ProcessStartInfo(fileName);
            foreach (string arg in arguments) psi.ArgumentList.Add(arg);
            psi.RedirectStandardInput = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;

            try
            {
                using (Process process = new Process())
                {
                    process.StartInfo = psi;
                    if (!process.Start()) return result;
                    if (stdin != null) await process.StandardInput.WriteAsync(stdin).ConfigureAwait(false);
                    process.StandardInput.Close();

                    Task<string> outTask = process.StandardOutput.ReadToEndAsync();
                    Task<string> errTask = process.StandardError.ReadToEndAsync();
                    using (CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(token))
                    {
                        cts.CancelAfter(Math.Clamp(timeoutMs, 500, 60000));
                        try
                        {
                            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            try { process.Kill(true); } catch (Exception) { }
                            return result;
                        }
                    }

                    result.StdOut = await outTask.ConfigureAwait(false);
                    result.StdErr = await errTask.ConfigureAwait(false);
                    result.ExitCode = process.ExitCode;
                    return result;
                }
            }
            catch (Exception ex) when (ex is Win32Exception || ex is IOException || ex is InvalidOperationException)
            {
                result.StdErr = ex.Message;
                return result;
            }
        }

        /// <summary>
        /// True when an executable is found on PATH or exists at the given absolute path.
        /// </summary>
        /// <param name="fileName">Executable name or path.</param>
        /// <returns>True when found.</returns>
        public static bool Exists(string fileName)
        {
            if (String.IsNullOrWhiteSpace(fileName)) return false;
            if (Path.IsPathRooted(fileName)) return File.Exists(fileName);
            string? path = Environment.GetEnvironmentVariable("PATH");
            if (String.IsNullOrEmpty(path)) return false;
            foreach (string dir in path.Split(Path.PathSeparator))
            {
                try
                {
                    if (File.Exists(Path.Combine(dir, fileName))) return true;
                }
                catch (ArgumentException)
                {
                    // Malformed PATH entry.
                }
            }

            return false;
        }

        #endregion
    }
}

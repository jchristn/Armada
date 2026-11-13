namespace Test.Shared.Infrastructure.BlackBoxStorage
{
    using System;
    using System.Diagnostics;
    using System.Text;

    /// <summary>
    /// Runs the docker CLI with a timeout (for the black box storage emulator containers).
    /// </summary>
    public static class DockerCommand
    {
        /// <summary>
        /// Run docker with arguments.
        /// </summary>
        /// <param name="arguments">Arguments.</param>
        /// <param name="timeoutMs">Timeout.</param>
        /// <param name="output">Standard output and error.</param>
        /// <returns>The exit code, or -1 when docker could not run or timed out.</returns>
        public static int Run(string arguments, int timeoutMs, out string output)
        {
            output = String.Empty;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("docker", arguments)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (Process? process = Process.Start(psi))
                {
                    if (process == null) return -1;
                    StringBuilder sb = new StringBuilder();
                    process.OutputDataReceived += (s, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                    process.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    if (!process.WaitForExit(timeoutMs))
                    {
                        try { process.Kill(true); } catch (Exception) { }
                        output = "timed out";
                        return -1;
                    }
                    process.WaitForExit();
                    lock (sb) output = sb.ToString().Trim();
                    return process.ExitCode;
                }
            }
            catch (Exception e)
            {
                output = e.Message;
                return -1;
            }
        }
    }
}

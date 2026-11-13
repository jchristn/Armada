namespace Armada.Runtimes.Usage
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;
    /// <summary>Bounded read-only OS credential command. Output never becomes a log or job payload.</summary>
    internal static class UsageCredentialCommand
    {
        internal static async Task<string?> ReadAsync(string executable, IEnumerable<string> arguments, CancellationToken token)
        {
            using Process process = new Process();
            process.StartInfo = new ProcessStartInfo(executable) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            foreach (string argument in arguments) process.StartInfo.ArgumentList.Add(argument);
            try { process.Start(); } catch (Win32Exception) { return null; }
            using CancellationTokenRegistration cancellation = token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
            try
            {
                char[] buffer = new char[65537];
                int count = 0;
                while (count < buffer.Length)
                {
                    int read = await process.StandardOutput.ReadAsync(buffer.AsMemory(count), token).ConfigureAwait(false);
                    if (read == 0) break;
                    count += read;
                }
                if (count > 65536) return null;
                await process.WaitForExitAsync(token).ConfigureAwait(false);
                return process.ExitCode == 0 ? new string(buffer, 0, count).TrimEnd('\r', '\n') : null;
            }
            finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); } }
        }
    }
}

namespace Armada.Runtimes.Usage
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// A bounded line-oriented conversation with a local helper process (for example <c>codex app-server</c>
    /// over stdio) started from an executable and argument list without shell interpolation. Every line read
    /// is capped, the whole stdout volume is capped, stderr is drained and discarded (it never becomes a
    /// log), and <see cref="Dispose"/> always terminates the whole process tree and waits (bounded) for the
    /// root process to be reaped, so a probe that times out or is cancelled leaves nothing running.
    /// </summary>
    public sealed class UsageProcessSession : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Operating-system process id of the helper, or 0 before it started.
        /// </summary>
        public int ProcessId { get; private set; } = 0;

        /// <summary>
        /// Total stdout bytes read so far.
        /// </summary>
        public int BytesRead { get; private set; } = 0;

        /// <summary>
        /// Whether the helper process has exited (true after <see cref="Dispose"/>).
        /// </summary>
        public bool HasExited
        {
            get
            {
                try
                {
                    return _Process.HasExited;
                }
                catch (InvalidOperationException)
                {
                    return true;
                }
            }
        }

        #endregion

        #region Private-Members

        private readonly Process _Process;
        private readonly int _MaximumLineBytes;
        private readonly int _MaximumTotalBytes;
        private readonly StringBuilder _Pending = new StringBuilder();
        private readonly char[] _Buffer = new char[4096];
        private Task? _ErrorDrain = null;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        private UsageProcessSession(Process process, int maximumLineBytes, int maximumTotalBytes)
        {
            _Process = process;
            _MaximumLineBytes = maximumLineBytes;
            _MaximumTotalBytes = maximumTotalBytes;
        }

        /// <summary>
        /// Start a helper process.
        /// </summary>
        /// <param name="executable">Executable path or name resolved through PATH.</param>
        /// <param name="arguments">Argument list; each entry is passed verbatim, never through a shell.</param>
        /// <param name="maximumLineBytes">Longest accepted stdout line in bytes.</param>
        /// <param name="maximumTotalBytes">Most stdout bytes accepted over the whole session.</param>
        /// <returns>The running session.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="executable"/> is null or empty.</exception>
        /// <exception cref="System.ComponentModel.Win32Exception">Thrown when the executable cannot be started (for example it is not installed).</exception>
        public static UsageProcessSession Start(string executable, IEnumerable<string>? arguments, int maximumLineBytes, int maximumTotalBytes)
        {
            if (String.IsNullOrWhiteSpace(executable)) throw new ArgumentNullException(nameof(executable));
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = Environment.CurrentDirectory,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardInputEncoding = new UTF8Encoding(false)
            };
            if (arguments != null)
            {
                foreach (string argument in arguments)
                    startInfo.ArgumentList.Add(argument ?? String.Empty);
            }

            Process process = new Process { StartInfo = startInfo };
            try
            {
                process.Start();
            }
            catch
            {
                process.Dispose();
                throw;
            }

            UsageProcessSession session = new UsageProcessSession(process, Math.Max(1, maximumLineBytes), Math.Max(1, maximumTotalBytes));
            session.ProcessId = process.Id;
            session._ErrorDrain = DrainAsync(process.StandardError);
            return session;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Write one line to the helper's stdin.
        /// </summary>
        /// <param name="line">Line without a terminator.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="IOException">Thrown when the helper closed its stdin.</exception>
        public async Task WriteLineAsync(string line, CancellationToken token)
        {
            await _Process.StandardInput.WriteAsync((line ?? String.Empty).AsMemory(), token).ConfigureAwait(false);
            await _Process.StandardInput.WriteAsync("\n".AsMemory(), token).ConfigureAwait(false);
            await _Process.StandardInput.FlushAsync(token).ConfigureAwait(false);
        }

        /// <summary>
        /// Read the next complete stdout line.
        /// </summary>
        /// <param name="token">Cancellation token; cancellation throws <see cref="OperationCanceledException"/>.</param>
        /// <returns>The read outcome; never null.</returns>
        public async Task<UsageProcessLine> ReadLineAsync(CancellationToken token)
        {
            while (true)
            {
                string pending = _Pending.ToString();
                int newline = pending.IndexOf('\n');
                if (newline >= 0)
                {
                    string line = pending.Substring(0, newline).TrimEnd('\r');
                    _Pending.Remove(0, newline + 1);
                    if (Encoding.UTF8.GetByteCount(line) > _MaximumLineBytes) return UsageProcessLine.Oversize();
                    return UsageProcessLine.Of(line);
                }

                if (Encoding.UTF8.GetByteCount(pending) > _MaximumLineBytes) return UsageProcessLine.Oversize();

                int read = await _Process.StandardOutput.ReadAsync(_Buffer.AsMemory(), token).ConfigureAwait(false);
                if (read <= 0)
                {
                    // End of stream: a partial line without its terminator is a truncated answer, never parsed.
                    return _Pending.Length > 0 ? UsageProcessLine.Truncated() : UsageProcessLine.EndOfStream();
                }

                BytesRead += Encoding.UTF8.GetByteCount(_Buffer, 0, read);
                if (BytesRead > _MaximumTotalBytes) return UsageProcessLine.Oversize();
                _Pending.Append(_Buffer, 0, read);
            }
        }

        /// <summary>
        /// Terminate the helper's whole process tree and wait (bounded) until the root is reaped.
        /// </summary>
        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;
            try
            {
                _Process.StandardInput.Close();
            }
            catch
            {
            }

            try
            {
                if (!_Process.HasExited) _Process.Kill(true);
            }
            catch
            {
            }

            try
            {
                _Process.WaitForExit(5000);
            }
            catch
            {
            }

            try
            {
                _ErrorDrain?.Wait(1000);
            }
            catch
            {
            }

            _Process.Dispose();
        }

        #endregion

        #region Private-Methods

        private static async Task DrainAsync(StreamReader reader)
        {
            char[] buffer = new char[1024];
            try
            {
                while (await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false) > 0)
                {
                    // Discarded: helper diagnostics may contain account details and never become a log.
                }
            }
            catch
            {
            }
        }

        #endregion
    }
}

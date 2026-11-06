namespace Armada.Runtimes.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Runtimes.Tools.Arguments;

    /// <summary>
    /// Spawns a process and captures its stdout, stderr, and exit code.
    /// When an args array is supplied, the command is started directly as the executable with those
    /// arguments and no shell. Otherwise the command line runs through cmd.exe /c on Windows or
    /// Bash with pipefail when available on Unix, otherwise /bin/sh -c.
    /// </summary>
    public class RunProcessTool : IToolExecutor
    {
        #region Private-Members

        private const int _DefaultTimeoutMs = 120000;
        private static readonly bool _IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        #endregion

        #region Public-Members

        /// <summary>
        /// The unique name of this tool.
        /// </summary>
        public string Name => "run_process";

        /// <summary>
        /// A human-readable description of what this tool does.
        /// </summary>
        public string Description => "Runs a shell command and captures its output. "
            + $"Current runtime: {GetOperatingSystemLabel()} using shell {GetShellProgram()} {GetShellInvocationArgsHint()}. "
            + "Use commands that are valid for that shell and operating system. "
            + "When args is supplied, command is run directly as the executable with args as its argument vector, without a shell. "
            + "Use timeout_ms instead of an external timeout command (which may not be installed). Returns stdout, stderr, exit code, and whether the process timed out.";

        /// <summary>
        /// The JSON Schema object describing the tool's input parameters.
        /// </summary>
        public object ParametersSchema => new
        {
            type = "object",
            description = "Execute a command using the current runtime shell. "
                + $"Runtime context: operating_system={GetOperatingSystemLabel()}, platform_family={GetPlatformFamily()}, "
                + $"shell_program={GetShellProgram()}, shell_invocation={GetShellInvocation()}.",
            mux_runtime_context = new
            {
                operating_system = GetOperatingSystemLabel(),
                platform_family = GetPlatformFamily(),
                shell_program = GetShellProgram(),
                shell_invocation = GetShellInvocation(),
                command_guidance = "Use commands and syntax compatible with the runtime shell and operating system shown here."
            },
            properties = new
            {
                command = new
                {
                    type = "string",
                    description = "The command to execute. Write it for the runtime shell and operating system in mux_runtime_context."
                },
                args = new
                {
                    type = "array",
                    description = "Optional argument vector for the command. When non-empty, command is started directly as the executable "
                        + "and each element is passed verbatim as one argument, without a shell (no quoting, globbing, pipes, or variable expansion). "
                        + "When omitted or empty, command is run as a command line through the runtime shell.",
                    items = new { type = "string" }
                },
                working_directory = new
                {
                    type = "string",
                    description = "The working directory for the process. Defaults to the agent's working directory."
                },
                timeout_ms = new
                {
                    type = "integer",
                    description = "Timeout in milliseconds. The process is killed if it exceeds this. Defaults to 120000."
                }
            },
            required = new[] { "command" }
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Executes the run_process tool.
        /// </summary>
        /// <param name="toolCallId">The unique identifier for this tool call.</param>
        /// <param name="arguments">The parsed JSON arguments containing command, optional args, working_directory, and timeout_ms.</param>
        /// <param name="workingDirectory">The current working directory for resolving relative paths.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A <see cref="ToolResult"/> containing the process execution result.</returns>
        public async Task<ToolResult> ExecuteAsync(string toolCallId, JsonElement arguments, string workingDirectory, CancellationToken cancellationToken)
        {
            if (!ToolArgumentParser.TryParse(arguments, out RunProcessToolArguments? args, out string? argumentError))
            {
                return ToolArgumentParser.InvalidParameter(toolCallId, argumentError);
            }

            try
            {
                string command = args.Command!;
                string processWorkDir = args.WorkingDirectory ?? workingDirectory;
                int timeoutMs = args.TimeoutMs ?? _DefaultTimeoutMs;
                string resolvedWorkDir = ResolvePath(processWorkDir, workingDirectory);

                ProcessStartInfo startInfo = BuildStartInfo(command, args.Args, resolvedWorkDir);

                using (Process process = new Process())
                {
                    process.StartInfo = startInfo;

                    int maxOutput = ToolSafetyLimits.MaxProcessOutputBytes;
                    BoundedOutputBuffer stdout = new BoundedOutputBuffer(maxOutput);
                    BoundedOutputBuffer stderr = new BoundedOutputBuffer(maxOutput);

                    process.Start();
                    // This tool has no interactive input channel. EOF prevents commands such as cat/read
                    // from waiting on the Harbor GUI's inherited stdin for the entire tool timeout.
                    process.StandardInput.Close();

                    bool timedOut = false;

                    using (CancellationTokenSource timeoutCts = new CancellationTokenSource(timeoutMs))
                    using (CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token))
                    {
                        Task stdoutRead = CaptureOutputAsync(process.StandardOutput.BaseStream, stdout, linkedCts.Token);
                        Task stderrRead = CaptureOutputAsync(process.StandardError.BaseStream, stderr, linkedCts.Token);
                        Task processWait = process.WaitForExitAsync(linkedCts.Token);

                        try
                        {
                            await Task.WhenAll(processWait, stdoutRead, stderrRead).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            timedOut = timeoutCts.IsCancellationRequested;

                            try
                            {
                                process.Kill(entireProcessTree: true);
                            }
                            catch (Exception)
                            {
                                // Best effort kill
                            }

                            if (!timedOut)
                            {
                                // Cancelled by the caller, not timeout
                                try
                                {
                                    await Task.WhenAll(stdoutRead, stderrRead).ConfigureAwait(false);
                                }
                                catch (OperationCanceledException)
                                {
                                    // Caller cancellation stops the active stream reads.
                                }

                                return new ToolResult
                                {
                                    ToolCallId = toolCallId,
                                    Success = false,
                                    Content = JsonSerializer.Serialize(new { error = "cancelled", message = "Process execution was cancelled." })
                                };
                            }

                            try
                            {
                                await Task.WhenAll(stdoutRead, stderrRead).ConfigureAwait(false);
                            }
                            catch (OperationCanceledException)
                            {
                                // Timeout cancels stream reads after the process tree is stopped.
                            }
                        }
                    }

                    int exitCode = timedOut ? -1 : process.ExitCode;

                    string stdoutStr = stdout.GetOutput();
                    string stderrStr = stderr.GetOutput();

                    return new ToolResult
                    {
                        ToolCallId = toolCallId,
                        Success = !timedOut && exitCode == 0,
                        Content = JsonSerializer.Serialize(new
                        {
                            stdout = stdoutStr,
                            stderr = stderrStr,
                            exit_code = exitCode,
                            timed_out = timedOut
                        })
                    };
                }
            }
            catch (Exception ex)
            {
                return new ToolResult
                {
                    ToolCallId = toolCallId,
                    Success = false,
                    Content = JsonSerializer.Serialize(new { error = "process_error", message = ex.Message })
                };
            }
        }

        #endregion

        #region Private-Methods

        private static async Task CaptureOutputAsync(Stream stream, BoundedOutputBuffer output, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
            {
                output.Append(buffer, count);
            }
        }

        private static ProcessStartInfo BuildStartInfo(string command, List<string?>? argv, string resolvedWorkDir)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.WorkingDirectory = resolvedWorkDir;
            startInfo.RedirectStandardInput = true;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;

            if (argv != null && argv.Count > 0)
            {
                // An argument vector was supplied: run the executable directly, with no shell, so each
                // argument reaches the process verbatim (no quoting, globbing, or expansion).
                startInfo.FileName = ResolveExecutable(command, resolvedWorkDir);
                foreach (string? arg in argv)
                {
                    startInfo.ArgumentList.Add(arg ?? string.Empty);
                }
            }
            else if (_IsWindows)
            {
                startInfo.FileName = "cmd.exe";
                startInfo.Arguments = "/c " + command;
            }
            else
            {
                startInfo.FileName = GetShellProgram();
                if (startInfo.FileName == "/bin/bash")
                {
                    startInfo.ArgumentList.Add("-o");
                    startInfo.ArgumentList.Add("pipefail");
                }
                startInfo.ArgumentList.Add("-c");
                startInfo.ArgumentList.Add(command);
            }

            return startInfo;
        }

        private static string ResolveExecutable(string command, string resolvedWorkDir)
        {
            bool hasDirectory = command.IndexOf('/') >= 0 || (_IsWindows && command.IndexOf('\\') >= 0);
            if (hasDirectory && !Path.IsPathRooted(command))
            {
                return Path.GetFullPath(Path.Combine(resolvedWorkDir, command));
            }

            return command;
        }

        private string ResolvePath(string filePath, string workingDirectory)
        {
            if (Path.IsPathRooted(filePath))
            {
                return Path.GetFullPath(filePath);
            }

            return Path.GetFullPath(Path.Combine(workingDirectory, filePath));
        }

        private static string GetOperatingSystemLabel()
        {
            return RuntimeInformation.OSDescription.Trim();
        }

        private static string GetPlatformFamily()
        {
            return _IsWindows ? "windows" : "unix";
        }

        private static string GetShellProgram()
        {
            return _IsWindows ? "cmd.exe" : File.Exists("/bin/bash") ? "/bin/bash" : "/bin/sh";
        }

        private static string GetShellInvocation()
        {
            return _IsWindows ? "cmd.exe /c <command>" : GetShellProgram() + (GetShellProgram() == "/bin/bash" ? " -o pipefail -c <command>" : " -c <command>");
        }

        private static string GetShellInvocationArgsHint()
        {
            return _IsWindows ? "/c" : GetShellProgram() == "/bin/bash" ? "-o pipefail -c" : "-c";
        }

        private sealed class BoundedOutputBuffer
        {
            private const string _MarkerPrefix = "[truncated - output exceeded ";
            private const string _MarkerMiddle = " bytes; omitted ";
            private const string _MarkerSuffix = " bytes]";
            private readonly int _MaxBytes;
            private readonly byte[] _Head;
            private readonly byte[] _Tail;
            private int _HeadCount;
            private int _TailCount;
            private int _TailNext;
            private long _TotalBytes;

            public BoundedOutputBuffer(int maxBytes)
            {
                _MaxBytes = Math.Max(0, maxBytes);
                _Head = new byte[_MaxBytes / 5];
                _Tail = new byte[_MaxBytes - _Head.Length];
            }

            public void Append(byte[] bytes, int count)
            {
                _TotalBytes += count;

                int offset = 0;
                int headCopy = Math.Min(_Head.Length - _HeadCount, count);
                if (headCopy > 0)
                {
                    Array.Copy(bytes, 0, _Head, _HeadCount, headCopy);
                    _HeadCount += headCopy;
                    offset += headCopy;
                }

                if (_Tail.Length == 0) return;

                while (offset < count)
                {
                    _Tail[_TailNext] = bytes[offset];
                    _TailNext = (_TailNext + 1) % _Tail.Length;
                    _TailCount = Math.Min(_TailCount + 1, _Tail.Length);
                    offset++;
                }
            }

            public string GetOutput()
            {
                if (_TotalBytes <= _MaxBytes)
                {
                    byte[] complete = new byte[_HeadCount + _TailCount];
                    Array.Copy(_Head, 0, complete, 0, _HeadCount);
                    CopyTail(complete, _HeadCount, _TailCount);
                    return Encoding.UTF8.GetString(complete);
                }

                byte[] head = GetValidHead();
                int tailLength = _TailCount;
                string marker = String.Empty;
                byte[] tail = Array.Empty<byte>();

                for (int attempt = 0; attempt < 32; attempt++)
                {
                    tailLength = Math.Max(0, Math.Min(_TailCount, _MaxBytes - head.Length - Encoding.UTF8.GetByteCount(marker) - 2));
                    tail = GetValidTail(tailLength);
                    long omittedBytes = Math.Max(0, _TotalBytes - head.Length - tail.Length);
                    string nextMarker = _MarkerPrefix + _MaxBytes + _MarkerMiddle + omittedBytes + _MarkerSuffix;
                    if (nextMarker == marker)
                    {
                        marker = nextMarker;
                        break;
                    }

                    marker = nextMarker;
                }

                return Encoding.UTF8.GetString(head) + "\n" + marker + "\n" + Encoding.UTF8.GetString(tail);
            }

            private byte[] GetValidHead()
            {
                int length = _HeadCount;
                while (length > 0 && IsContinuation(_Head[length - 1])) length--;
                if (length == 0) return Array.Empty<byte>();

                int lead = length - 1;
                while (lead >= 0 && IsContinuation(_Head[lead])) lead--;
                if (lead < 0) return Array.Empty<byte>();
                int expected = Utf8SequenceLength(_Head[lead]);
                if (expected > length - lead) length = lead;
                return Slice(_Head, 0, length);
            }

            private byte[] GetValidTail(int requestedLength)
            {
                byte[] bytes = new byte[requestedLength];
                CopyTailSuffix(bytes, requestedLength);
                int start = 0;
                while (start < bytes.Length && IsContinuation(bytes[start])) start++;
                if (start == bytes.Length) return Array.Empty<byte>();
                int expected = Utf8SequenceLength(bytes[start]);
                if (expected > bytes.Length - start) return Array.Empty<byte>();
                return Slice(bytes, start, bytes.Length - start);
            }

            private void CopyTail(byte[] destination, int destinationOffset, int count)
            {
                int start = (_TailNext - _TailCount + _Tail.Length) % _Tail.Length;
                int firstLength = Math.Min(count, _Tail.Length - start);
                Array.Copy(_Tail, start, destination, destinationOffset, firstLength);
                if (firstLength < count) Array.Copy(_Tail, 0, destination, destinationOffset + firstLength, count - firstLength);
            }

            private void CopyTailSuffix(byte[] destination, int count)
            {
                byte[] ordered = new byte[_TailCount];
                CopyTail(ordered, 0, _TailCount);
                Array.Copy(ordered, ordered.Length - count, destination, 0, count);
            }

            private static byte[] Slice(byte[] bytes, int offset, int count)
            {
                byte[] result = new byte[count];
                Array.Copy(bytes, offset, result, 0, count);
                return result;
            }

            private static bool IsContinuation(byte value)
            {
                return (value & 0xC0) == 0x80;
            }

            private static int Utf8SequenceLength(byte lead)
            {
                if ((lead & 0x80) == 0) return 1;
                if ((lead & 0xE0) == 0xC0) return 2;
                if ((lead & 0xF0) == 0xE0) return 3;
                if ((lead & 0xF8) == 0xF0) return 4;
                return 1;
            }
        }

        #endregion
    }
}

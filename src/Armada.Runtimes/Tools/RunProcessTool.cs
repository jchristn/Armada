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
    /// /bin/sh -c on other platforms.
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
            + "Returns stdout, stderr, exit code, and whether the process timed out.";

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

                    StringBuilder stdout = new StringBuilder();
                    StringBuilder stderr = new StringBuilder();

                    process.OutputDataReceived += (sender, e) =>
                    {
                        if (e.Data != null)
                        {
                            stdout.AppendLine(e.Data);
                        }
                    };

                    process.ErrorDataReceived += (sender, e) =>
                    {
                        if (e.Data != null)
                        {
                            stderr.AppendLine(e.Data);
                        }
                    };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    bool timedOut = false;

                    using (CancellationTokenSource timeoutCts = new CancellationTokenSource(timeoutMs))
                    using (CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token))
                    {
                        try
                        {
                            await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
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
                                return new ToolResult
                                {
                                    ToolCallId = toolCallId,
                                    Success = false,
                                    Content = JsonSerializer.Serialize(new { error = "cancelled", message = "Process execution was cancelled." })
                                };
                            }
                        }
                    }

                    int exitCode = timedOut ? -1 : process.ExitCode;

                    string stdoutStr = stdout.ToString();
                    string stderrStr = stderr.ToString();

                    int maxOutput = ToolSafetyLimits.MaxProcessOutputBytes;

                    if (stdoutStr.Length > maxOutput)
                    {
                        stdoutStr = stdoutStr.Substring(0, maxOutput) + "\n[truncated - output exceeded " + maxOutput + " bytes]";
                    }

                    if (stderrStr.Length > maxOutput)
                    {
                        stderrStr = stderrStr.Substring(0, maxOutput) + "\n[truncated - output exceeded " + maxOutput + " bytes]";
                    }

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

        private static ProcessStartInfo BuildStartInfo(string command, List<string?>? argv, string resolvedWorkDir)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.WorkingDirectory = resolvedWorkDir;
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
                startInfo.FileName = "/bin/sh";
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
            return _IsWindows ? "cmd.exe" : "/bin/sh";
        }

        private static string GetShellInvocation()
        {
            return _IsWindows ? "cmd.exe /c <command>" : "/bin/sh -c <command>";
        }

        private static string GetShellInvocationArgsHint()
        {
            return _IsWindows ? "/c" : "-c";
        }

        #endregion
    }
}

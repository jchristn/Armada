namespace Armada.Runtimes.Tools
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Spawns a process and captures its stdout, stderr, and exit code.
    /// On Windows, uses cmd.exe /c; on other platforms, uses /bin/sh -c.
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
                    description = "Optional array of command arguments.",
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
            try
            {
                string command = GetRequiredString(arguments, "command");
                string processWorkDir = GetOptionalString(arguments, "working_directory", workingDirectory);
                int timeoutMs = GetOptionalInt(arguments, "timeout_ms", _DefaultTimeoutMs);
                string resolvedWorkDir = ResolvePath(processWorkDir, workingDirectory);

                // Build the full command string with arguments
                StringBuilder commandBuilder = new StringBuilder(command);
                if (arguments.TryGetProperty("args", out JsonElement argsElement) && argsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement arg in argsElement.EnumerateArray())
                    {
                        if (arg.ValueKind == JsonValueKind.String)
                        {
                            commandBuilder.Append(' ');
                            commandBuilder.Append(arg.GetString());
                        }
                    }
                }

                string fullCommand = commandBuilder.ToString();

                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.WorkingDirectory = resolvedWorkDir;
                startInfo.RedirectStandardOutput = true;
                startInfo.RedirectStandardError = true;
                startInfo.UseShellExecute = false;
                startInfo.CreateNoWindow = true;

                if (_IsWindows)
                {
                    startInfo.FileName = "cmd.exe";
                    startInfo.Arguments = $"/c {fullCommand}";
                }
                else
                {
                    startInfo.FileName = "/bin/sh";
                    startInfo.Arguments = $"-c \"{fullCommand.Replace("\"", "\\\"")}\"";
                }

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
                        stdoutStr = stdoutStr.Substring(0, maxOutput) + "\n[truncated — output exceeded " + maxOutput + " bytes]";
                    }

                    if (stderrStr.Length > maxOutput)
                    {
                        stderrStr = stderrStr.Substring(0, maxOutput) + "\n[truncated — output exceeded " + maxOutput + " bytes]";
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

        private string GetRequiredString(JsonElement arguments, string propertyName)
        {
            if (arguments.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString()!;
            }

            throw new ArgumentException($"Required parameter '{propertyName}' is missing or not a string.");
        }

        private string GetOptionalString(JsonElement arguments, string propertyName, string defaultValue)
        {
            if (arguments.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString()!;
            }

            return defaultValue;
        }

        private int GetOptionalInt(JsonElement arguments, string propertyName, int defaultValue)
        {
            if (arguments.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.Number)
            {
                return value.GetInt32();
            }

            return defaultValue;
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
            return _IsWindows ? "cmd.exe /c <command>" : "/bin/sh -c \"<command>\"";
        }

        private static string GetShellInvocationArgsHint()
        {
            return _IsWindows ? "/c" : "-c";
        }

        #endregion
    }
}

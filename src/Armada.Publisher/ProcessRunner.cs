namespace Armada.Publisher
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Text;

    /// <summary>
    /// Thin wrapper around Process for invoking packaging tools with streamed output and clear failures.
    /// </summary>
    public static class ProcessRunner
    {
        #region Public-Methods

        /// <summary>
        /// Run a command, streaming stdout and stderr to the console, and throw when it exits non-zero.
        /// </summary>
        /// <param name="fileName">Executable to run.</param>
        /// <param name="arguments">Argument list (each element is passed verbatim, no shell parsing).</param>
        /// <param name="workingDirectory">Working directory, or null for the current directory.</param>
        public static void Run(string fileName, IEnumerable<string> arguments, string? workingDirectory = null)
        {
            if (string.IsNullOrEmpty(fileName)) throw new ArgumentNullException(nameof(fileName));

            ProcessStartInfo info = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            if (!string.IsNullOrEmpty(workingDirectory)) info.WorkingDirectory = workingDirectory;

            StringBuilder rendered = new StringBuilder(fileName);
            foreach (string argument in arguments)
            {
                info.ArgumentList.Add(argument);
                rendered.Append(' ').Append(argument);
            }

            Console.WriteLine("[exec] " + rendered.ToString());

            using (Process process = new Process())
            {
                process.StartInfo = info;
                process.OutputDataReceived += (sender, e) => { if (e.Data != null) Console.WriteLine(e.Data); };
                process.ErrorDataReceived += (sender, e) => { if (e.Data != null) Console.Error.WriteLine(e.Data); };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException("Command '" + fileName + "' failed with exit code " + process.ExitCode + ".");
                }
            }
        }

        /// <summary>
        /// Run a command and return its standard output. Standard error is streamed to the console.
        /// Throws when the command exits non-zero.
        /// </summary>
        /// <param name="fileName">Executable to run.</param>
        /// <param name="arguments">Argument list (each element is passed verbatim, no shell parsing).</param>
        /// <param name="workingDirectory">Working directory, or null for the current directory.</param>
        /// <returns>Captured standard output.</returns>
        public static string Capture(string fileName, IEnumerable<string> arguments, string? workingDirectory = null)
        {
            if (string.IsNullOrEmpty(fileName)) throw new ArgumentNullException(nameof(fileName));

            ProcessStartInfo info = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            if (!string.IsNullOrEmpty(workingDirectory)) info.WorkingDirectory = workingDirectory;
            foreach (string argument in arguments) info.ArgumentList.Add(argument);

            StringBuilder output = new StringBuilder();
            using (Process process = new Process())
            {
                process.StartInfo = info;
                process.OutputDataReceived += (sender, e) => { if (e.Data != null) output.AppendLine(e.Data); };
                process.ErrorDataReceived += (sender, e) => { if (e.Data != null) Console.Error.WriteLine(e.Data); };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException("Command '" + fileName + "' failed with exit code " + process.ExitCode + ".");
                }
            }

            return output.ToString();
        }

        /// <summary>
        /// Return true when an executable can be located on PATH.
        /// </summary>
        /// <param name="fileName">Executable name to probe (with or without extension).</param>
        /// <returns>True when the tool appears runnable.</returns>
        public static bool Exists(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return false;

            string? pathValue = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(pathValue)) return false;

            string[] extensions = OperatingSystem.IsWindows()
                ? new string[] { ".exe", ".cmd", ".bat", "" }
                : new string[] { "" };

            foreach (string directory in pathValue.Split(System.IO.Path.PathSeparator))
            {
                if (string.IsNullOrEmpty(directory)) continue;
                foreach (string extension in extensions)
                {
                    string candidate = System.IO.Path.Combine(directory, fileName + extension);
                    if (System.IO.File.Exists(candidate)) return true;
                }
            }

            return false;
        }

        #endregion
    }
}

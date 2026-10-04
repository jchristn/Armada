namespace Armada.Core.Hosting
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Threading.Tasks;

    /// <summary>
    /// Runs registration commands as child processes, capturing their output. Thread safe: holds no state.
    /// </summary>
    public class ProcessRegistrationCommandRunner : IRegistrationCommandRunner
    {
        #region Public-Members

        /// <summary>
        /// Maximum time to wait for one command, in milliseconds. Default 60000, minimum 1000, maximum 600000.
        /// A command still running at the deadline is killed and reported with exit code -1.
        /// </summary>
        public int TimeoutMs
        {
            get { return _TimeoutMs; }
            set
            {
                if (value < 1000) value = 1000;
                if (value > 600000) value = 600000;
                _TimeoutMs = value;
            }
        }

        #endregion

        #region Private-Members

        private int _TimeoutMs = 60000;

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public CommandResult Run(string fileName, IReadOnlyList<string> arguments)
        {
            if (String.IsNullOrWhiteSpace(fileName)) throw new ArgumentNullException(nameof(fileName));
            if (arguments == null) throw new ArgumentNullException(nameof(arguments));

            ProcessStartInfo startInfo = new ProcessStartInfo(fileName);
            foreach (string argument in arguments) startInfo.ArgumentList.Add(argument);
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;

            try
            {
                using (Process process = new Process())
                {
                    process.StartInfo = startInfo;
                    process.Start();
                    Task<string> stdout = process.StandardOutput.ReadToEndAsync();
                    Task<string> stderr = process.StandardError.ReadToEndAsync();
                    if (!process.WaitForExit(_TimeoutMs))
                    {
                        try { process.Kill(true); } catch (InvalidOperationException) { }
                        return new CommandResult(-1, String.Empty, fileName + " did not exit within " + _TimeoutMs + " ms");
                    }

                    process.WaitForExit();
                    return new CommandResult(process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
                }
            }
            catch (Win32Exception ex)
            {
                return new CommandResult(-1, String.Empty, "could not start " + fileName + ": " + ex.Message);
            }
        }

        #endregion
    }
}

namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// Starts the notification command as a process and waits up to three seconds for it.
    /// </summary>
    public class ProcessNotificationCommandRunner : INotificationCommandRunner
    {
        #region Public-Methods

        /// <inheritdoc />
        public void Run(string fileName, IReadOnlyList<string> arguments)
        {
            if (String.IsNullOrEmpty(fileName)) throw new ArgumentNullException(nameof(fileName));
            if (arguments == null) throw new ArgumentNullException(nameof(arguments));
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (string arg in arguments) startInfo.ArgumentList.Add(arg);
            using (Process? process = Process.Start(startInfo))
            {
                process?.WaitForExit(3000);
            }
        }

        #endregion
    }
}

namespace Armada.Publisher.Build
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using Armada.Publisher.Manifest;

    /// <summary>
    /// Authenticode-signs Windows installers with signtool when the certificate secret is present, and
    /// logs plainly that the output is unsigned when it is not.
    /// </summary>
    public static class AuthenticodeSigner
    {
        #region Public-Methods

        /// <summary>
        /// Sign every file in the directory that matches the pattern.
        /// </summary>
        /// <param name="settings">Windows signing settings from the manifest.</param>
        /// <param name="directory">Directory containing the installers.</param>
        /// <param name="searchPattern">File pattern, for example "*.exe" or "*.msi".</param>
        /// <param name="workingDirectory">Working directory for signtool.</param>
        /// <param name="logPrefix">Prefix for log lines, for example "[wix]".</param>
        public static void SignAll(WindowsSigningSettings settings, string directory, string searchPattern, string workingDirectory, string logPrefix)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (string.IsNullOrEmpty(directory)) throw new ArgumentNullException(nameof(directory));

            string certBase64 = Environment.GetEnvironmentVariable(settings.CertBase64Secret) ?? string.Empty;
            string certPassword = Environment.GetEnvironmentVariable(settings.CertPasswordSecret) ?? string.Empty;
            if (string.IsNullOrEmpty(certBase64))
            {
                Console.WriteLine(logPrefix + " " + settings.CertBase64Secret + " not set; produced unsigned installer(s). SmartScreen will warn on first run.");
                return;
            }

            string pfxPath = Path.Combine(directory, "codesign-" + Guid.NewGuid().ToString("N") + ".pfx");
            File.WriteAllBytes(pfxPath, Convert.FromBase64String(certBase64));
            try
            {
                foreach (string installer in Directory.EnumerateFiles(directory, searchPattern))
                {
                    List<string> arguments = new List<string>
                    {
                        "sign", "/f", pfxPath,
                        "/fd", "SHA256",
                        "/tr", settings.TimestampUrl,
                        "/td", "SHA256",
                        installer
                    };
                    if (!string.IsNullOrEmpty(certPassword)) arguments.InsertRange(3, new List<string> { "/p", certPassword });
                    ProcessRunner.Run("signtool", arguments, workingDirectory);
                }
            }
            finally
            {
                if (File.Exists(pfxPath)) File.Delete(pfxPath);
            }
        }

        #endregion
    }
}

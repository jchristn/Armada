namespace Armada.Publisher.Preflight
{
    using System;
    using System.Collections.Generic;
    using Armada.Publisher.Channels;
    using Armada.Publisher.Manifest;

    /// <summary>
    /// Preflight that reports, per enabled channel runnable on the current OS, whether the required
    /// packaging tools are present and, when they are not, the exact command to install each one.
    /// </summary>
    public class Doctor
    {
        #region Private-Members

        private readonly PublisherManifest _Manifest;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the Doctor.
        /// </summary>
        /// <param name="manifest">Loaded manifest.</param>
        public Doctor(PublisherManifest manifest)
        {
            _Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Report tooling readiness for every enabled channel.
        /// </summary>
        /// <returns>True when nothing required by a runnable channel is missing.</returns>
        public bool Report()
        {
            string currentOs = CurrentOsPrefix();
            Console.WriteLine("Armada.Publisher doctor (host OS: " + currentOs + ")");
            Console.WriteLine(new string('-', 60));

            bool allSatisfied = true;
            HashSet<string> reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (ChannelDefinition channel in _Manifest.Channels)
            {
                if (!channel.Enabled) continue;

                IChannel implementation = ChannelFactory.Create(channel.Kind);
                foreach (ToolRequirement requirement in implementation.Requirements())
                {
                    if (!AppliesToCurrentOs(requirement, currentOs)) continue;
                    if (!reported.Add(requirement.Executable)) continue;

                    bool present = ProcessRunner.Exists(requirement.Executable);
                    if (present)
                    {
                        Console.WriteLine("  [ok]      " + requirement.Executable);
                    }
                    else
                    {
                        allSatisfied = false;
                        Console.WriteLine("  [missing] " + requirement.Executable + "  ->  " + requirement.InstallCommand);
                    }
                }
            }

            Console.WriteLine(new string('-', 60));
            Console.WriteLine(allSatisfied
                ? "All tools required by channels runnable on this OS are present."
                : "One or more tools are missing. Install the commands shown above, or run this channel on CI.");
            return allSatisfied;
        }

        #endregion

        #region Private-Methods

        private static string CurrentOsPrefix()
        {
            if (OperatingSystem.IsWindows()) return "win";
            if (OperatingSystem.IsMacOS()) return "osx";
            return "linux";
        }

        private static bool AppliesToCurrentOs(ToolRequirement requirement, string currentOs)
        {
            if (string.IsNullOrEmpty(requirement.OperatingSystems)) return true;
            foreach (string token in requirement.OperatingSystems.Split(','))
            {
                if (string.Equals(token.Trim(), currentOs, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        #endregion
    }
}

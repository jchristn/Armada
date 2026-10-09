namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Harbor;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// Where a vessel's checkout lives and how to work in it: the Admiral host with the vessel's working directory, or a
    /// connected Harbor with the checkout it maps the vessel to. Commands, git, and file operations go to that host.
    /// Built by <see cref="VesselHostResolver"/>.
    /// </summary>
    public class VesselHost
    {
        #region Public-Members

        /// <summary>
        /// The vessel.
        /// </summary>
        public Vessel Vessel { get; }

        /// <summary>
        /// The Harbor that has the checkout, or null for the Admiral host.
        /// </summary>
        public string? HarborId { get; }

        /// <summary>
        /// The Harbor as people know it ("Name (hbr_...)"), or null for the Admiral host.
        /// </summary>
        public string? HarborName { get; }

        /// <summary>
        /// Whether the checkout is on a Harbor.
        /// </summary>
        public bool IsHarbor
        {
            get { return HarborId != null; }
        }

        /// <summary>
        /// The checkout's path on its host.
        /// </summary>
        public string WorkingDirectory { get; }

        /// <summary>
        /// How a Harbor found the checkout (Mapped or Discovered); None for the Admiral host.
        /// </summary>
        public HarborRepositorySourceEnum Source { get; }

        /// <summary>
        /// The host's operating system description (from the Harbor's handshake, or this machine's), or null when unknown.
        /// </summary>
        public string? OsPlatform { get; }

        /// <summary>
        /// Commands on that host.
        /// </summary>
        public IHostCommandExecutor Commands { get; }

        /// <summary>
        /// Git on that host.
        /// </summary>
        public IGitService Git { get; }

        /// <summary>
        /// File operations in the checkout.
        /// </summary>
        public IVesselCheckoutFiles Files { get; }

        /// <summary>
        /// The host for audit records and messages: "Admiral" or "Harbor Name (hbr_...)".
        /// </summary>
        public string HostLabel
        {
            get { return IsHarbor ? "Harbor " + HarborName : "Admiral"; }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="vessel">Vessel.</param>
        /// <param name="harborId">Harbor identifier, or null for the Admiral host.</param>
        /// <param name="harborName">The Harbor as people know it, or null.</param>
        /// <param name="workingDirectory">The checkout's path on its host.</param>
        /// <param name="source">How a Harbor found the checkout.</param>
        /// <param name="osPlatform">The host's operating system description, or null.</param>
        /// <param name="commands">Commands on that host.</param>
        /// <param name="git">Git on that host.</param>
        /// <param name="files">File operations in the checkout.</param>
        public VesselHost(
            Vessel vessel,
            string? harborId,
            string? harborName,
            string workingDirectory,
            HarborRepositorySourceEnum source,
            string? osPlatform,
            IHostCommandExecutor commands,
            IGitService git,
            IVesselCheckoutFiles files)
        {
            Vessel = vessel ?? throw new ArgumentNullException(nameof(vessel));
            if (String.IsNullOrWhiteSpace(workingDirectory)) throw new ArgumentNullException(nameof(workingDirectory));
            HarborId = String.IsNullOrWhiteSpace(harborId) ? null : harborId;
            HarborName = HarborId == null ? null : (String.IsNullOrWhiteSpace(harborName) ? HarborId : harborName);
            WorkingDirectory = workingDirectory;
            Source = source;
            OsPlatform = osPlatform;
            Commands = commands ?? throw new ArgumentNullException(nameof(commands));
            Git = git ?? throw new ArgumentNullException(nameof(git));
            Files = files ?? throw new ArgumentNullException(nameof(files));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Where the checkout is, for people: "working directory /x on the Admiral host" or "checkout /x on Harbor Mac
        /// (hbr_...)".
        /// </summary>
        /// <returns>The description.</returns>
        public string Describe()
        {
            return IsHarbor
                ? "checkout " + WorkingDirectory + " on Harbor " + HarborName
                : "working directory " + WorkingDirectory + " on the Admiral host";
        }

        /// <summary>
        /// Whether the host runs Windows.
        /// </summary>
        /// <returns>True for Windows.</returns>
        public bool IsWindows()
        {
            if (!IsHarbor) return OperatingSystem.IsWindows();
            return FleetActionShellCommandBuilder.IsWindowsPlatform(OsPlatform);
        }

        /// <summary>
        /// A request that runs a command line through the host's shell in the checkout: <c>cmd.exe /c</c> on Windows, and
        /// <c>/bin/sh -c</c> (or <c>-lc</c> for a login shell, which loads the user's PATH) elsewhere. The command line is
        /// one argument, so no quoting is involved.
        /// </summary>
        /// <param name="commandText">Command line.</param>
        /// <param name="timeoutMs">Timeout in milliseconds; 0 for none.</param>
        /// <param name="loginShell">Whether to run a login shell on Linux and macOS.</param>
        /// <returns>The request.</returns>
        public HostCommandRequest BuildShellCommand(string commandText, int timeoutMs, bool loginShell)
        {
            if (commandText == null) throw new ArgumentNullException(nameof(commandText));
            if (IsWindows())
            {
                return new HostCommandRequest
                {
                    Executable = "cmd.exe",
                    WorkingDirectory = WorkingDirectory,
                    Arguments = new List<string> { "/c", commandText },
                    TimeoutMs = timeoutMs
                };
            }

            return new HostCommandRequest
            {
                Executable = "/bin/sh",
                WorkingDirectory = WorkingDirectory,
                Arguments = new List<string> { loginShell ? "-lc" : "-c", commandText },
                TimeoutMs = timeoutMs
            };
        }

        #endregion
    }
}

namespace Armada.Core.Hosting
{
    using System;
    using System.Collections.Generic;
    using System.IO;

    /// <summary>
    /// Everything registration needs to know about the program and the machine. <see cref="FromCurrentProcess"/>
    /// fills it from the running process; tests build one by hand with temp directories so nothing touches the
    /// real service manager or home directory.
    /// </summary>
    public class RegistrationContext
    {
        #region Public-Members

        /// <summary>
        /// Target platform. Defaults to the current operating system.
        /// </summary>
        public HostPlatformEnum Platform { get; set; } = HostPlatformEnum.Unsupported;

        /// <summary>
        /// Program to launch: the executable, or "dotnet" when the program runs as a framework-dependent dll.
        /// </summary>
        public string ExecutablePath
        {
            get { return _ExecutablePath; }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(ExecutablePath));
                _ExecutablePath = value;
            }
        }

        /// <summary>
        /// Arguments placed before the action's own arguments (for example the dll path when
        /// <see cref="ExecutablePath"/> is "dotnet"). Never null.
        /// </summary>
        public List<string> LeadingArguments
        {
            get { return _LeadingArguments; }
            set { _LeadingArguments = value ?? new List<string>(); }
        }

        /// <summary>
        /// Working directory for the service. Defaults to the directory holding the executable.
        /// </summary>
        public string WorkingDirectory
        {
            get
            {
                if (!String.IsNullOrEmpty(_WorkingDirectory)) return _WorkingDirectory;
                string? dir = Path.GetDirectoryName(_LeadingArguments.Count > 0 ? _LeadingArguments[0] : _ExecutablePath);
                return String.IsNullOrEmpty(dir) ? "/" : dir;
            }
            set { _WorkingDirectory = value ?? String.Empty; }
        }

        /// <summary>
        /// True when the process runs as root (Linux, macOS) or as an elevated administrator (Windows).
        /// </summary>
        public bool IsElevated { get; set; } = false;

        /// <summary>
        /// Home directory of the current user.
        /// </summary>
        public string HomeDirectory
        {
            get { return _HomeDirectory; }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(HomeDirectory));
                _HomeDirectory = value;
            }
        }

        /// <summary>
        /// XDG configuration directory (Linux). Defaults to <c>$XDG_CONFIG_HOME</c>, else <c>~/.config</c>.
        /// </summary>
        public string XdgConfigHome
        {
            get { return String.IsNullOrEmpty(_XdgConfigHome) ? Path.Combine(_HomeDirectory, ".config") : _XdgConfigHome; }
            set { _XdgConfigHome = value ?? String.Empty; }
        }

        /// <summary>
        /// Directory for system-scope systemd units (Linux, elevated). Default <c>/etc/systemd/system</c>.
        /// </summary>
        public string SystemdSystemDirectory { get; set; } = "/etc/systemd/system";

        /// <summary>
        /// Directory for machine-wide launchd agents (macOS, elevated). Default <c>/Library/LaunchAgents</c>, the
        /// same location the .pkg installer uses.
        /// </summary>
        public string SystemLaunchAgentsDirectory { get; set; } = "/Library/LaunchAgents";

        /// <summary>
        /// Numeric user id of the current user (Linux, macOS), used for the launchd gui domain. -1 when unknown.
        /// </summary>
        public int UserId { get; set; } = -1;

        /// <summary>
        /// Print definitions and commands without writing files or running commands.
        /// </summary>
        public bool DryRun { get; set; } = false;

        /// <summary>
        /// Register but do not start or load the service now.
        /// </summary>
        public bool NoStart { get; set; } = false;

        /// <summary>
        /// Linux system scope only: account for <c>User=</c> in the unit. Null runs the unit as root.
        /// </summary>
        public string? ServiceUser { get; set; } = null;

        /// <summary>
        /// Service or entry name: systemd unit base name, Windows service name, autostart file base name.
        /// </summary>
        public string Name
        {
            get { return _Name; }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Name));
                _Name = value;
            }
        }

        /// <summary>
        /// Human-readable name shown by the service manager or the desktop.
        /// </summary>
        public string DisplayName
        {
            get { return _DisplayName; }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(DisplayName));
                _DisplayName = value;
            }
        }

        /// <summary>
        /// One-line description.
        /// </summary>
        public string Description
        {
            get { return _Description; }
            set { _Description = value ?? String.Empty; }
        }

        /// <summary>
        /// launchd label (reverse-DNS bundle identifier) on macOS.
        /// </summary>
        public string Label
        {
            get { return _Label; }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Label));
                _Label = value;
            }
        }

        /// <summary>
        /// Arguments the service manager or login item passes to the program (for example "--run-service").
        /// </summary>
        public List<string> RunArguments
        {
            get { return _RunArguments; }
            set { _RunArguments = value ?? new List<string>(); }
        }

        #endregion

        #region Private-Members

        private string _ExecutablePath = "armada-server";
        private List<string> _LeadingArguments = new List<string>();
        private string _WorkingDirectory = String.Empty;
        private string _HomeDirectory = "/";
        private string _XdgConfigHome = String.Empty;
        private string _Name = "armada";
        private string _DisplayName = "Armada";
        private string _Description = String.Empty;
        private string _Label = "com.joelchristner.armada";
        private List<string> _RunArguments = new List<string>();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Full argv of the registered program: executable, leading arguments, then run arguments.
        /// </summary>
        /// <returns>New list; the first element is the executable.</returns>
        public List<string> BuildProgramArguments()
        {
            List<string> argv = new List<string>();
            argv.Add(_ExecutablePath);
            argv.AddRange(_LeadingArguments);
            argv.AddRange(_RunArguments);
            return argv;
        }

        /// <summary>
        /// Detect the platform of the running process.
        /// </summary>
        /// <returns>The platform, or <see cref="HostPlatformEnum.Unsupported"/>.</returns>
        public static HostPlatformEnum DetectPlatform()
        {
            if (OperatingSystem.IsWindows()) return HostPlatformEnum.Windows;
            if (OperatingSystem.IsMacOS()) return HostPlatformEnum.MacOS;
            if (OperatingSystem.IsLinux()) return HostPlatformEnum.Linux;
            return HostPlatformEnum.Unsupported;
        }

        /// <summary>
        /// Build a context for the running process: its executable path (or dotnet plus the entry dll), the
        /// current user's home and XDG directories, elevation, and uid.
        /// </summary>
        /// <param name="commandLine">Parsed registration flags.</param>
        /// <returns>A context with identity fields (name, label, display name, run arguments) left for the caller.</returns>
        /// <exception cref="ArgumentNullException">commandLine is null.</exception>
        public static RegistrationContext FromCurrentProcess(RegistrationCommandLine commandLine)
        {
            if (commandLine == null) throw new ArgumentNullException(nameof(commandLine));

            RegistrationContext context = new RegistrationContext();
            context.Platform = DetectPlatform();
            context.IsElevated = Environment.IsPrivilegedProcess;
            context.DryRun = commandLine.DryRun;
            context.NoStart = commandLine.NoStart;
            context.ServiceUser = commandLine.ServiceUser;

            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            context.HomeDirectory = String.IsNullOrEmpty(home) ? "/" : home;
            string? xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (!String.IsNullOrWhiteSpace(xdg) && Path.IsPathRooted(xdg)) context.XdgConfigHome = xdg;

            string processPath = Environment.ProcessPath ?? String.Empty;
            string processFile = Path.GetFileNameWithoutExtension(processPath);
            if (String.Equals(processFile, "dotnet", StringComparison.OrdinalIgnoreCase))
            {
                // Framework-dependent launch (dotnet Armada.Server.dll): register "dotnet <dll>".
                string? entry = System.Reflection.Assembly.GetEntryAssembly()?.Location;
                context.ExecutablePath = processPath;
                if (!String.IsNullOrEmpty(entry)) context.LeadingArguments.Add(entry);
            }
            else if (!String.IsNullOrEmpty(processPath))
            {
                // Register the real file, not a PATH symlink such as /usr/local/bin/armada-server.
                context.ExecutablePath = ResolveSymlinks(processPath);
            }

            if (!OperatingSystem.IsWindows()) context.UserId = UnixIdentity.GetUserId();
            return context;
        }

        #endregion

        #region Private-Methods

        private static string ResolveSymlinks(string path)
        {
            try
            {
                FileSystemInfo? target = new FileInfo(path).ResolveLinkTarget(true);
                return target != null ? target.FullName : path;
            }
            catch (IOException)
            {
                return path;
            }
            catch (UnauthorizedAccessException)
            {
                return path;
            }
        }

        #endregion
    }
}

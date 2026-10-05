namespace Armada.Core.Hosting
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;

    /// <summary>
    /// Implements the Admiral's --install-service and --uninstall-service flags.
    /// <list type="bullet">
    /// <item>Windows: a Windows Service created with sc.exe (automatic start, restart on failure); needs an elevated prompt.</item>
    /// <item>Linux: a systemd unit, <c>~/.config/systemd/user/armada.service</c> for a normal user or
    /// <c>/etc/systemd/system/armada.service</c> as root, then enabled and started with systemctl.</item>
    /// <item>macOS: a launchd agent with the .pkg installer's label, in <c>~/Library/LaunchAgents</c> for a normal user or
    /// <c>/Library/LaunchAgents</c> as root, then bootstrapped into the GUI session with launchctl.</item>
    /// </list>
    /// Both actions are idempotent: installing twice leaves one registration, and uninstalling something that is not
    /// installed succeeds with nothing to do.
    /// </summary>
    public class ServiceRegistrar : RegistrarBase
    {
        #region Public-Members

        /// <summary>
        /// Label of the agent the old <c>scripts/macos/install-launchd-agent.sh</c> wrote. Install warns when it is present
        /// because both agents would start an Admiral on the same ports.
        /// </summary>
        public const string LegacyMacLabel = "com.armada.admiral";

        /// <summary>
        /// How long uninstall waits for a Windows service to stop, in seconds. Default 30, minimum 1, maximum 300.
        /// </summary>
        public int StopTimeoutSeconds
        {
            get { return _StopTimeoutSeconds; }
            set
            {
                if (value < 1) value = 1;
                if (value > 300) value = 300;
                _StopTimeoutSeconds = value;
            }
        }

        /// <summary>
        /// Delay between Windows service state polls, in milliseconds. Default 1000, minimum 0, maximum 10000.
        /// </summary>
        public int PollIntervalMs
        {
            get { return _PollIntervalMs; }
            set
            {
                if (value < 0) value = 0;
                if (value > 10000) value = 10000;
                _PollIntervalMs = value;
            }
        }

        #endregion

        #region Private-Members

        private int _StopTimeoutSeconds = 30;
        private int _PollIntervalMs = 1000;
        private const int _ErrorServiceNotActive = 1062;
        private const int _ErrorServiceDoesNotExist = 1060;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="context">Registration context for the Admiral.</param>
        /// <param name="runner">Command runner.</param>
        /// <param name="output">Progress output.</param>
        public ServiceRegistrar(RegistrationContext context, IRegistrationCommandRunner runner, TextWriter output)
            : base(context, runner, output)
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Path of the definition file this registrar manages on Linux and macOS for the current scope.
        /// </summary>
        /// <returns>The path, or an empty string on Windows (the definition lives in the service control manager).</returns>
        public string GetDefinitionPath()
        {
            if (Context.Platform == HostPlatformEnum.Linux)
            {
                string directory = Context.IsElevated
                    ? Context.SystemdSystemDirectory
                    : Path.Combine(Context.XdgConfigHome, "systemd", "user");
                return Path.Combine(directory, Context.Name + ".service");
            }

            if (Context.Platform == HostPlatformEnum.MacOS)
            {
                string directory = Context.IsElevated
                    ? Context.SystemLaunchAgentsDirectory
                    : Path.Combine(Context.HomeDirectory, "Library", "LaunchAgents");
                return Path.Combine(directory, Context.Label + ".plist");
            }

            return String.Empty;
        }

        /// <summary>
        /// Register the service (or, with <see cref="RegistrationContext.DryRun"/>, print what would be registered).
        /// </summary>
        /// <returns>A <see cref="RegistrationExitCode"/> value.</returns>
        public int Install()
        {
            SetPrefix("install-service");
            if (Context.ServiceUser != null && !(Context.Platform == HostPlatformEnum.Linux && Context.IsElevated))
            {
                Print("ERROR: " + RegistrationCommandLine.ServiceUserOption + " applies only to a Linux system unit (run as root).");
                return RegistrationExitCode.InvalidArguments;
            }

            switch (Context.Platform)
            {
                case HostPlatformEnum.Linux:
                    return InstallSystemd();
                case HostPlatformEnum.MacOS:
                    return InstallLaunchd();
                case HostPlatformEnum.Windows:
                    return InstallWindowsService();
                default:
                    Print("ERROR: service registration is not supported on this operating system.");
                    return RegistrationExitCode.UnsupportedPlatform;
            }
        }

        /// <summary>
        /// Remove the service registration (or, with <see cref="RegistrationContext.DryRun"/>, print what would be removed).
        /// </summary>
        /// <returns>A <see cref="RegistrationExitCode"/> value.</returns>
        public int Uninstall()
        {
            SetPrefix("uninstall-service");
            switch (Context.Platform)
            {
                case HostPlatformEnum.Linux:
                    return UninstallSystemd();
                case HostPlatformEnum.MacOS:
                    return UninstallLaunchd();
                case HostPlatformEnum.Windows:
                    return UninstallWindowsService();
                default:
                    Print("ERROR: service registration is not supported on this operating system.");
                    return RegistrationExitCode.UnsupportedPlatform;
            }
        }

        #endregion

        #region Private-Methods

        private List<string> Systemctl(params string[] arguments)
        {
            List<string> list = new List<string>();
            if (!Context.IsElevated) list.Add("--user");
            list.AddRange(arguments);
            return list;
        }

        private int InstallSystemd()
        {
            bool systemScope = Context.IsElevated;
            string unitName = Context.Name + ".service";
            string path = GetDefinitionPath();
            string unit = SystemdUnitBuilder.Build(Context, systemScope);
            Print((Context.DryRun ? "dry run, nothing is changed. " : String.Empty)
                + (systemScope ? "system unit" : "user unit (systemd --user)") + " " + unitName);

            if (!WriteDefinition(path, unit, out bool changed)) return RegistrationExitCode.Failed;

            if (changed || Context.DryRun)
            {
                CommandResult reload = Execute("systemctl", Systemctl("daemon-reload"), false);
                if (!reload.Succeeded) return Fail("systemctl daemon-reload failed", reload);
            }

            CommandResult enable = Execute("systemctl", Systemctl("enable", unitName), false);
            if (!enable.Succeeded) return Fail("systemctl enable failed", enable);

            if (!Context.NoStart)
            {
                // restart applies a changed unit to a running service; start is a no-op for one already running.
                CommandResult start = Execute("systemctl", Systemctl(changed ? "restart" : "start", unitName), false);
                if (!start.Succeeded) return Fail("systemctl could not start " + unitName, start);
            }

            if (!systemScope && !Context.DryRun)
            {
                Print("note: a user unit starts with your session. To start it at boot without logging in, run: sudo loginctl enable-linger " + Environment.UserName);
            }

            Print(Context.DryRun ? "dry run complete." : "done. Status: systemctl " + (systemScope ? String.Empty : "--user ") + "status " + unitName);
            return RegistrationExitCode.Success;
        }

        private int UninstallSystemd()
        {
            string unitName = Context.Name + ".service";
            string path = GetDefinitionPath();
            if (!File.Exists(path))
            {
                Print("not installed (no " + path + "); nothing to do.");
                return RegistrationExitCode.Success;
            }

            if (Context.DryRun) Print("dry run, nothing is changed.");
            CommandResult disable = Execute("systemctl", Systemctl("disable", "--now", unitName), false);
            if (!disable.Succeeded) Print("warning: systemctl disable failed (continuing): " + FirstLine(disable));

            if (!DeleteDefinition(path)) return RegistrationExitCode.Failed;

            CommandResult reload = Execute("systemctl", Systemctl("daemon-reload"), false);
            if (!reload.Succeeded) Print("warning: systemctl daemon-reload failed: " + FirstLine(reload));

            Print(Context.DryRun ? "dry run complete." : "done.");
            return RegistrationExitCode.Success;
        }

        private int InstallLaunchd()
        {
            string path = GetDefinitionPath();
            string plist = LaunchdPlistBuilder.BuildServiceAgent(Context);
            Print((Context.DryRun ? "dry run, nothing is changed. " : String.Empty)
                + (Context.IsElevated ? "launchd agent for every user (" : "launchd agent for the current user (") + Context.Label + ")");

            string legacy = Path.Combine(Context.HomeDirectory, "Library", "LaunchAgents", LegacyMacLabel + ".plist");
            if (File.Exists(legacy))
            {
                Print("warning: " + legacy + " (from scripts/macos/install-launchd-agent.sh) also starts an Admiral. Remove it with scripts/macos/remove-launchd-agent.sh.");
            }

            if (!WriteDefinition(path, plist, out bool changed)) return RegistrationExitCode.Failed;

            if (Context.NoStart)
            {
                Print("not loading now (" + RegistrationCommandLine.NoStartFlag + "); launchd loads it at the next login.");
                return RegistrationExitCode.Success;
            }

            string domain = ResolveGuiDomain();
            if (String.IsNullOrEmpty(domain))
            {
                Print("no user is logged in at the console; launchd loads the agent at the next login.");
                return RegistrationExitCode.Success;
            }

            CommandResult loaded = Probe("launchctl", new List<string> { "print", domain + "/" + Context.Label });
            if (loaded.Succeeded && !changed)
            {
                Print("already loaded in " + domain + "; nothing to do.");
                return RegistrationExitCode.Success;
            }

            if (loaded.Succeeded)
            {
                CommandResult bootout = Execute("launchctl", new List<string> { "bootout", domain + "/" + Context.Label }, false);
                if (!bootout.Succeeded) Print("warning: launchctl bootout failed (continuing): " + FirstLine(bootout));
            }

            CommandResult bootstrap = Execute("launchctl", new List<string> { "bootstrap", domain, path }, false);
            if (!bootstrap.Succeeded) return Fail("launchctl bootstrap failed", bootstrap);

            Print(Context.DryRun ? "dry run complete." : "done. Status: launchctl print " + domain + "/" + Context.Label);
            return RegistrationExitCode.Success;
        }

        private int UninstallLaunchd()
        {
            string path = GetDefinitionPath();
            if (!File.Exists(path))
            {
                Print("not installed (no " + path + "); nothing to do.");
                return RegistrationExitCode.Success;
            }

            if (Context.DryRun) Print("dry run, nothing is changed.");
            string domain = ResolveGuiDomain();
            if (!String.IsNullOrEmpty(domain))
            {
                CommandResult loaded = Probe("launchctl", new List<string> { "print", domain + "/" + Context.Label });
                if (loaded.Succeeded)
                {
                    CommandResult bootout = Execute("launchctl", new List<string> { "bootout", domain + "/" + Context.Label }, false);
                    if (!bootout.Succeeded) Print("warning: launchctl bootout failed (continuing): " + FirstLine(bootout));
                }
            }

            if (!DeleteDefinition(path)) return RegistrationExitCode.Failed;
            Print(Context.DryRun ? "dry run complete." : "done.");
            return RegistrationExitCode.Success;
        }

        private string ResolveGuiDomain()
        {
            if (!Context.IsElevated)
            {
                return Context.UserId >= 0 ? "gui/" + Context.UserId : String.Empty;
            }

            // Running as root (the .pkg postinstall, or sudo): target the user logged in at the console, as the
            // package scripts do.
            CommandResult owner = Probe("stat", new List<string> { "-f%Su", "/dev/console" });
            string user = owner.StandardOutput.Trim();
            if (!owner.Succeeded || String.IsNullOrEmpty(user) || user == "root") return String.Empty;

            CommandResult uid = Probe("id", new List<string> { "-u", user });
            string value = uid.StandardOutput.Trim();
            if (!uid.Succeeded || !Int32.TryParse(value, out int parsed) || parsed <= 0) return String.Empty;
            return "gui/" + parsed;
        }

        private int InstallWindowsService()
        {
            if (!Context.IsElevated && !Context.DryRun)
            {
                Print("ERROR: creating a Windows service needs an elevated (Run as administrator) prompt.");
                return RegistrationExitCode.InsufficientPrivileges;
            }

            if (Context.DryRun) Print("dry run, nothing is changed.");
            bool exists = Probe("sc.exe", new List<string> { "query", Context.Name }).Succeeded;
            Print(exists
                ? "service '" + Context.Name + "' is already registered; updating its configuration."
                : "registering service '" + Context.Name + "' (" + Context.DisplayName + ").");
            Print("service command line: " + WindowsCommandBuilder.BuildCommandLine(Context));

            CommandResult create = Execute("sc.exe", WindowsCommandBuilder.BuildServiceCreate(Context, exists), true);
            if (!create.Succeeded) return Fail("sc.exe " + (exists ? "config" : "create") + " failed", create);

            CommandResult describe = Execute("sc.exe", WindowsCommandBuilder.BuildServiceDescription(Context), true);
            if (!describe.Succeeded) Print("warning: could not set the description: " + FirstLine(describe));

            CommandResult failure = Execute("sc.exe", WindowsCommandBuilder.BuildServiceFailureActions(Context), true);
            if (!failure.Succeeded) Print("warning: could not set restart-on-failure: " + FirstLine(failure));

            if (!Context.NoStart)
            {
                CommandResult start = Execute("sc.exe", new List<string> { "start", Context.Name }, true);
                // 1056: ERROR_SERVICE_ALREADY_RUNNING.
                if (!start.Succeeded && start.ExitCode != 1056) return Fail("sc.exe start failed", start);
                if (start.ExitCode == 1056) Print("service is already running; restart it to pick up a changed command line.");
            }

            Print(Context.DryRun ? "dry run complete." : "done. Status: sc.exe query " + Context.Name);
            return RegistrationExitCode.Success;
        }

        private int UninstallWindowsService()
        {
            if (!Context.IsElevated && !Context.DryRun)
            {
                Print("ERROR: removing a Windows service needs an elevated (Run as administrator) prompt.");
                return RegistrationExitCode.InsufficientPrivileges;
            }

            CommandResult query = Probe("sc.exe", new List<string> { "query", Context.Name });
            if (!query.Succeeded)
            {
                Print("service '" + Context.Name + "' is not registered; nothing to do.");
                return RegistrationExitCode.Success;
            }

            if (Context.DryRun) Print("dry run, nothing is changed.");
            CommandResult stop = Execute("sc.exe", new List<string> { "stop", Context.Name }, true);
            if (stop.ExitCode != _ErrorServiceNotActive)
            {
                if (!stop.Succeeded) Print("warning: sc.exe stop failed (continuing): " + FirstLine(stop));
                if (!Context.DryRun) WaitForStopped();
            }

            CommandResult delete = Execute("sc.exe", new List<string> { "delete", Context.Name }, true);
            if (!delete.Succeeded) return Fail("sc.exe delete failed", delete);

            Print(Context.DryRun ? "dry run complete." : "done.");
            return RegistrationExitCode.Success;
        }

        private void WaitForStopped()
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(_StopTimeoutSeconds);
            while (DateTime.UtcNow < deadline)
            {
                // Decide from sc.exe exit codes, not its localized text: a stop control on a stopped service fails
                // with ERROR_SERVICE_NOT_ACTIVE, on a removed service with ERROR_SERVICE_DOES_NOT_EXIST, and on a
                // service that is still stopping with ERROR_SERVICE_CANNOT_ACCEPT_CTRL.
                CommandResult probe = Probe("sc.exe", new List<string> { "stop", Context.Name });
                if (probe.ExitCode == _ErrorServiceNotActive || probe.ExitCode == _ErrorServiceDoesNotExist) return;
                if (_PollIntervalMs > 0) Thread.Sleep(_PollIntervalMs);
            }
            Print("warning: service did not report STOPPED within " + _StopTimeoutSeconds + " s; deleting anyway (it is removed once it stops).");
        }

        #endregion
    }
}

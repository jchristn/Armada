namespace Armada.Core.Hosting
{
    using System;
    using System.Collections.Generic;
    using System.IO;

    /// <summary>
    /// Implements Harbor's --install-startup and --uninstall-startup flags: a per-user login item.
    /// <list type="bullet">
    /// <item>Windows: a value under <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>, written with reg.exe.</item>
    /// <item>macOS: a RunAtLoad launchd agent in <c>~/Library/LaunchAgents</c> labeled with Harbor's bundle identifier.</item>
    /// <item>Linux: an XDG autostart entry in <c>~/.config/autostart</c>.</item>
    /// </list>
    /// Registration takes effect at the next login; it never launches a second Harbor now. Both actions are idempotent.
    /// On Linux and macOS the flags refuse to run as root, because the entry belongs to the user who runs Harbor.
    /// </summary>
    public class StartupRegistrar : RegistrarBase
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="context">Registration context for Harbor.</param>
        /// <param name="runner">Command runner.</param>
        /// <param name="output">Progress output.</param>
        public StartupRegistrar(RegistrationContext context, IRegistrationCommandRunner runner, TextWriter output)
            : base(context, runner, output)
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Path of the login-item file on Linux and macOS.
        /// </summary>
        /// <returns>The path, or an empty string on Windows (the entry lives in the registry).</returns>
        public string GetDefinitionPath()
        {
            if (Context.Platform == HostPlatformEnum.Linux) return Path.Combine(Context.XdgConfigHome, "autostart", Context.Name + ".desktop");
            if (Context.Platform == HostPlatformEnum.MacOS) return Path.Combine(Context.HomeDirectory, "Library", "LaunchAgents", Context.Label + ".plist");
            return String.Empty;
        }

        /// <summary>
        /// Register the login item (or, with <see cref="RegistrationContext.DryRun"/>, print it).
        /// </summary>
        /// <returns>A <see cref="RegistrationExitCode"/> value.</returns>
        public int Install()
        {
            SetPrefix("install-startup");
            int guard = Guard();
            if (guard != RegistrationExitCode.Success) return guard;
            if (Context.DryRun) Print("dry run, nothing is changed.");

            if (Context.Platform == HostPlatformEnum.Windows) return InstallRunKey();

            string path = GetDefinitionPath();
            string content = Context.Platform == HostPlatformEnum.Linux
                ? XdgAutostartBuilder.Build(Context)
                : LaunchdPlistBuilder.BuildLoginItem(Context);
            if (!WriteDefinition(path, content, out bool changed)) return RegistrationExitCode.Failed;

            Print(Context.DryRun ? "dry run complete." : (changed ? "done. " : "already registered. ") + Context.DisplayName + " starts at your next login.");
            return RegistrationExitCode.Success;
        }

        /// <summary>
        /// Remove the login item (or, with <see cref="RegistrationContext.DryRun"/>, print what would be removed).
        /// </summary>
        /// <returns>A <see cref="RegistrationExitCode"/> value.</returns>
        public int Uninstall()
        {
            SetPrefix("uninstall-startup");
            int guard = Guard();
            if (guard != RegistrationExitCode.Success) return guard;

            if (Context.Platform == HostPlatformEnum.Windows) return UninstallRunKey();

            string path = GetDefinitionPath();
            if (!File.Exists(path))
            {
                Print("not registered (no " + path + "); nothing to do.");
                return RegistrationExitCode.Success;
            }

            if (Context.DryRun) Print("dry run, nothing is changed.");
            if (!DeleteDefinition(path)) return RegistrationExitCode.Failed;
            Print(Context.DryRun ? "dry run complete." : "done. A Harbor that is running now keeps running.");
            return RegistrationExitCode.Success;
        }

        #endregion

        #region Private-Methods

        private int Guard()
        {
            if (Context.Platform == HostPlatformEnum.Unsupported)
            {
                Print("ERROR: startup registration is not supported on this operating system.");
                return RegistrationExitCode.UnsupportedPlatform;
            }

            if (Context.Platform != HostPlatformEnum.Windows && Context.IsElevated)
            {
                Print("ERROR: run this as the user who should get the login item, not as root.");
                return RegistrationExitCode.InsufficientPrivileges;
            }

            return RegistrationExitCode.Success;
        }

        private int InstallRunKey()
        {
            string commandLine = WindowsCommandBuilder.BuildCommandLine(Context);
            CommandResult query = Probe("reg.exe", new List<string> { "query", WindowsCommandBuilder.RunKey, "/v", Context.DisplayName });
            if (query.Succeeded &&
                WindowsCommandBuilder.TryGetRegQueryStringValue(query.StandardOutput, Context.DisplayName, out string currentValue) &&
                String.Equals(currentValue, commandLine, StringComparison.Ordinal))
            {
                Print("already registered: " + WindowsCommandBuilder.RunKey + " \"" + Context.DisplayName + "\" = " + commandLine);
                return RegistrationExitCode.Success;
            }

            Print((query.Succeeded ? "updating " : "adding ") + WindowsCommandBuilder.RunKey + " \"" + Context.DisplayName + "\" = " + commandLine);
            CommandResult add = Execute("reg.exe", WindowsCommandBuilder.BuildRunKeyAdd(Context), true);
            if (!add.Succeeded) return Fail("reg.exe add failed", add);

            Print(Context.DryRun ? "dry run complete." : "done. " + Context.DisplayName + " starts at your next sign-in.");
            return RegistrationExitCode.Success;
        }

        private int UninstallRunKey()
        {
            CommandResult query = Probe("reg.exe", new List<string> { "query", WindowsCommandBuilder.RunKey, "/v", Context.DisplayName });
            if (!query.Succeeded)
            {
                Print("not registered (no \"" + Context.DisplayName + "\" value under " + WindowsCommandBuilder.RunKey + "); nothing to do.");
                return RegistrationExitCode.Success;
            }

            if (Context.DryRun) Print("dry run, nothing is changed.");
            CommandResult delete = Execute("reg.exe", new List<string> { "delete", WindowsCommandBuilder.RunKey, "/v", Context.DisplayName, "/f" }, true);
            if (!delete.Succeeded) return Fail("reg.exe delete failed", delete);
            Print(Context.DryRun ? "dry run complete." : "done.");
            return RegistrationExitCode.Success;
        }

        #endregion
    }
}

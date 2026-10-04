namespace Armada.Harbor
{
    using System;
    using System.Runtime.InteropServices;
    using Armada.Core.Hosting;
    using Avalonia;

    /// <summary>
    /// Entry point for the Armada Harbor host runner.
    /// </summary>
    internal static class Program
    {
        /// <summary>
        /// True when Harbor was started with --minimized (the login item does this): start in the tray without opening
        /// the window.
        /// </summary>
        public static bool StartMinimized { get; private set; } = false;

        /// <summary>
        /// Application entry point. --install-startup and --uninstall-startup register or remove the per-user login
        /// item and exit without starting the UI.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        /// <returns>Process exit code.</returns>
        [STAThread]
        public static int Main(string[] args)
        {
            RegistrationCommandLine registration = RegistrationCommandLine.Parse(args);
            if (registration.Action != RegistrationActionEnum.None || !registration.IsValid)
            {
                return RunRegistration(registration);
            }

            StartMinimized = Array.Exists(args, a => String.Equals(a, RegistrationDefaults.HarborMinimizedFlag, StringComparison.Ordinal));
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            return 0;
        }

        /// <summary>
        /// Build the Avalonia application.
        /// </summary>
        /// <returns>The configured application builder.</returns>
        public static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace();
        }

        private static int RunRegistration(RegistrationCommandLine registration)
        {
            // Harbor is a GUI (WinExe) program; attach to the launching console so the flags' output is visible.
            if (OperatingSystem.IsWindows()) AttachConsole(-1);

            if (!registration.IsValid)
            {
                foreach (string error in registration.Errors) Console.Error.WriteLine("ERROR: " + error);
                return RegistrationExitCode.InvalidArguments;
            }

            if (registration.Action != RegistrationActionEnum.InstallStartup && registration.Action != RegistrationActionEnum.UninstallStartup)
            {
                Console.Error.WriteLine("ERROR: armada-harbor supports " + RegistrationCommandLine.InstallStartupFlag + " and "
                    + RegistrationCommandLine.UninstallStartupFlag + " (with optional " + RegistrationCommandLine.DryRunFlag
                    + "). Service flags belong to armada-server.");
                return RegistrationExitCode.InvalidArguments;
            }

            RegistrationContext context = RegistrationDefaults.ApplyHarbor(RegistrationContext.FromCurrentProcess(registration));
            StartupRegistrar registrar = new StartupRegistrar(context, new ProcessRegistrationCommandRunner(), Console.Out);
            return registration.Action == RegistrationActionEnum.InstallStartup ? registrar.Install() : registrar.Uninstall();
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int processId);
    }
}

namespace Armada.Harbor
{
    using System;
    using System.Runtime.InteropServices;
    using Armada.Core.Harbor;
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
        /// What startup did to the process's PATH (the login shell's PATH imported on macOS and Linux), or null before
        /// startup ran it.
        /// </summary>
        public static HarborPathImportResult? PathImport { get; private set; } = null;

        /// <summary>
        /// True once the desktop UI has finished starting. A UI failure before that falls back to a headless link.
        /// </summary>
        public static bool UiStarted { get; set; } = false;

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

            // Started from Finder, the Dock, or a login item, Harbor has launchd's minimal PATH, without the folders agent
            // CLIs live in. Take the login shell's PATH before anything launches, so every captain inherits it.
            try
            {
                PathImport = new HarborShellPath().ImportIntoProcess();
            }
            catch (Exception ex)
            {
                PathImport = new HarborPathImportResult { Error = ex.Message };
            }

            if (Array.Exists(args, a => String.Equals(a, HarborHeadlessRunner.HeadlessFlag, StringComparison.Ordinal)))
                return HarborHeadlessRunner.Run("started with " + HarborHeadlessRunner.HeadlessFlag);

            try
            {
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            catch (Exception ex) when (!UiStarted)
            {
                ReportMissingNativeLibrary(ex);
                // The desktop UI could not start (a sleeping display fails the render timer). Keep this computer
                // linked to the Admiral without a window rather than exiting and leaving its work unserved.
                return HarborHeadlessRunner.Run("the desktop UI could not start (" + ex.GetType().Name + ": " + ex.Message + ")");
            }

            return 0;
        }

        /// <summary>
        /// Build the Avalonia application.
        /// </summary>
        /// <returns>The configured application builder.</returns>
        public static AppBuilder BuildAvaloniaApp()
        {
            // ShowInDock is applied when Avalonia starts on macOS; a login-item start (--minimized) begins as a
            // menu-bar-only app, matching LSUIElement in the bundle. App switches the policy as the window opens and closes.
            return AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .With(new MacOSPlatformOptions { ShowInDock = !StartMinimized })
                .LogToTrace();
        }

        private static void ReportMissingNativeLibrary(Exception ex)
        {
            // On Linux (and WSL, where WSLg supplies only the display) a missing X11 client library surfaces here as
            // "Unable to load shared library 'libICE.so.6'". Name the packages, then fall back to the headless link.
            if (!OperatingSystem.IsLinux()) return;
            for (Exception? current = ex; current != null; current = current.InnerException)
            {
                if (current is DllNotFoundException)
                {
                    Console.Error.WriteLine("Error: the Harbor window needs X11 client libraries this system lacks (" + current.Message + ").");
                    Console.Error.WriteLine("Install them with:");
                    Console.Error.WriteLine("  Debian/Ubuntu: sudo apt install libx11-6 libice6 libsm6 libxext6 libxrandr2 libxi6 libxcursor1 libxfixes3 libfontconfig1");
                    Console.Error.WriteLine("  Fedora:        sudo dnf install libX11 libICE libSM libXext libXrandr libXi libXcursor libXfixes fontconfig");
                    Console.Error.WriteLine("Continuing without a window: this computer stays linked to the Admiral.");
                    return;
                }
            }
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

namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Xml;
    using System.Xml.Linq;
    using Armada.Core.Hosting;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Covers the Admiral's --install-service / --uninstall-service / --run-service flags and Harbor's
    /// --install-startup / --uninstall-startup flags: the generated definitions (systemd unit, launchd plists, XDG
    /// autostart entry, sc.exe and reg.exe command lines), the registrars' idempotent flows on every platform against a
    /// recording command runner and temp directories (nothing touches the real service manager), dry-run output, and the
    /// agreement between publisher.json and the names the flags use. Windows flows are exercised through the recorded
    /// sc.exe and reg.exe argument lists; the real Windows service host cannot run on Linux or macOS.
    /// </summary>
    public sealed class ServiceRegistrationSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.ServiceRegistration";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            // Command-line parsing.
            cases.Add(Case("parse_actions_and_options", "Parse registration flags and options", TestTags.Positive, () =>
            {
                RegistrationCommandLine none = RegistrationCommandLine.Parse(new string[] { "--some-avalonia-arg" });
                AssertEqual(RegistrationActionEnum.None, none.Action);
                AssertTrue(none.IsValid);

                RegistrationCommandLine install = RegistrationCommandLine.Parse(new string[] { "--install-service", "--dry-run", "--no-start", "--service-user", "armada" });
                AssertEqual(RegistrationActionEnum.InstallService, install.Action);
                AssertTrue(install.DryRun);
                AssertTrue(install.NoStart);
                AssertEqual("armada", install.ServiceUser);
                AssertTrue(install.IsValid);

                RegistrationCommandLine equalsForm = RegistrationCommandLine.Parse(new string[] { "--install-service", "--service-user=svc_armada" });
                AssertEqual("svc_armada", equalsForm.ServiceUser);

                AssertEqual(RegistrationActionEnum.RunService, RegistrationCommandLine.Parse(new string[] { "--run-service" }).Action);
                AssertEqual(RegistrationActionEnum.InstallStartup, RegistrationCommandLine.Parse(new string[] { "--install-startup" }).Action);
                AssertEqual(RegistrationActionEnum.UninstallStartup, RegistrationCommandLine.Parse(new string[] { "--uninstall-startup", "--uninstall-startup" }).Action, "repeating one flag is fine");
                AssertTrue(RegistrationCommandLine.Parse(new string[] { "--uninstall-startup", "--uninstall-startup" }).IsValid);
            }));

            cases.Add(Case("parse_rejects_invalid", "Parse rejects conflicting or malformed flags", TestTags.Negative, () =>
            {
                AssertFalse(RegistrationCommandLine.Parse(new string[] { "--install-service", "--uninstall-service" }).IsValid, "two actions");
                AssertFalse(RegistrationCommandLine.Parse(new string[] { "--run-service", "--dry-run" }).IsValid, "dry run of run-service");
                AssertFalse(RegistrationCommandLine.Parse(new string[] { "--install-service", "--service-user" }).IsValid, "missing account");
                AssertFalse(RegistrationCommandLine.Parse(new string[] { "--install-service", "--service-user", "bob;rm -rf /" }).IsValid, "unsafe account");
                AssertFalse(RegistrationCommandLine.Parse(new string[] { "--install-service", "--service-user", "--dry-run" }).IsValid, "flag as account");
            }));

            // Generated definitions.
            cases.Add(Case("systemd_user_unit", "systemd user unit content and ExecStart quoting", TestTags.Positive, () =>
            {
                RegistrationContext context = AdmiralContext(HostPlatformEnum.Linux, "/opt/My Apps/armada-server", false, NewHome());
                string unit = SystemdUnitBuilder.Build(context, false);
                AssertContains("ExecStart=\"/opt/My Apps/armada-server\" --run-service\n", unit);
                AssertContains("WorkingDirectory=/opt/My Apps\n", unit);
                AssertContains("Description=Armada Admiral\n", unit);
                AssertContains("WantedBy=default.target\n", unit);
                AssertContains("Restart=on-failure\n", unit);
                AssertFalse(unit.Contains("User="), "user unit has no User=");
                AssertFalse(unit.Contains("multi-user.target"), "user unit is not wanted by multi-user.target");
                AssertFalse(unit.Contains("\r"), "LF line endings");
                AssertEqual("100%%", SystemdUnitBuilder.QuoteExecArgument("100%"));
                AssertEqual("\"a$$b c\"", SystemdUnitBuilder.QuoteExecArgument("a$b c"));
                AssertEqual("plain", SystemdUnitBuilder.QuoteExecArgument("plain"));
                AssertEqual("\"\"", SystemdUnitBuilder.QuoteExecArgument(""));
                VerifyWithSystemdAnalyze(unit);
            }));

            cases.Add(Case("systemd_system_unit", "systemd system unit with a service user", TestTags.Positive, () =>
            {
                RegistrationContext context = AdmiralContext(HostPlatformEnum.Linux, "/usr/lib/armada-server/Armada.Server", true, NewHome());
                context.ServiceUser = "armada";
                string unit = SystemdUnitBuilder.Build(context, true);
                AssertContains("ExecStart=/usr/lib/armada-server/Armada.Server --run-service\n", unit);
                AssertContains("User=armada\n", unit);
                AssertContains("WantedBy=multi-user.target\n", unit);
                AssertContains("After=network-online.target\n", unit);
                AssertContains("WorkingDirectory=/usr/lib/armada-server\n", unit);
            }));

            cases.Add(Case("launchd_service_plist", "launchd Admiral agent matches the .pkg agent", TestTags.Positive, () =>
            {
                RegistrationContext context = AdmiralContext(HostPlatformEnum.MacOS, "/usr/local/lib/armada-server/Armada.Server", true, NewHome());
                string plist = LaunchdPlistBuilder.BuildServiceAgent(context);
                Dictionary<string, XElement> keys = ParsePlist(plist);
                AssertEqual("com.joelchristner.armada.server", keys["Label"].Value);
                List<string> argv = keys["ProgramArguments"].Elements("string").Select(e => e.Value).ToList();
                AssertEqual(2, argv.Count);
                AssertEqual("/usr/local/lib/armada-server/Armada.Server", argv[0]);
                AssertEqual("--run-service", argv[1]);
                AssertEqual("/usr/local/lib/armada-server", keys["WorkingDirectory"].Value);
                AssertEqual("true", keys["RunAtLoad"].Name.LocalName);
                AssertEqual("Background", keys["ProcessType"].Value);
                AssertEqual("Aqua", keys["LimitLoadToSessionType"].Value);
                AssertContains("<key>SuccessfulExit</key>", keys["KeepAlive"].ToString());
                LintPlist(plist);

                RegistrationContext odd = AdmiralContext(HostPlatformEnum.MacOS, "/Users/a&b/<x>/Armada.Server", false, NewHome());
                string escaped = LaunchdPlistBuilder.BuildServiceAgent(odd);
                AssertContains("/Users/a&amp;b/&lt;x&gt;/Armada.Server", escaped);
                AssertEqual("/Users/a&b/<x>/Armada.Server", ParsePlist(escaped)["ProgramArguments"].Elements("string").First().Value);
            }));

            cases.Add(Case("target_platform_paths", "Working directories follow the target platform's path rules, not the host's", TestTags.Positive, () =>
            {
                // Generated units describe paths on the target machine. These cases fail on a host whose
                // System.IO.Path rules differ from the target's (Windows host for Linux/macOS, any other host for Windows).
                AssertEqual("/opt/My Apps", TargetPath.GetDirectoryName("/opt/My Apps/armada-server", HostPlatformEnum.Linux));
                AssertEqual("/usr/local/lib/armada-server", TargetPath.GetDirectoryName("/usr/local/lib/armada-server/Armada.Server", HostPlatformEnum.MacOS));
                AssertEqual("/", TargetPath.GetDirectoryName("/armada-server", HostPlatformEnum.Linux));
                AssertEqual("/opt/a\\b", TargetPath.GetDirectoryName("/opt/a\\b/armada-server", HostPlatformEnum.Linux), "a backslash is a file name character on Linux");
                AssertEqual("/opt/armada", TargetPath.GetDirectoryName("/opt/armada//armada-server/", HostPlatformEnum.Linux));
                AssertNull(TargetPath.GetDirectoryName("armada-server", HostPlatformEnum.Linux), "no directory part");
                AssertEqual("C:\\Program Files\\Armada", TargetPath.GetDirectoryName("C:\\Program Files\\Armada\\armada-server.exe", HostPlatformEnum.Windows));
                AssertEqual("C:\\", TargetPath.GetDirectoryName("C:\\armada-server.exe", HostPlatformEnum.Windows));
                AssertEqual("C:/Tools", TargetPath.GetDirectoryName("C:/Tools/armada-server.exe", HostPlatformEnum.Windows));
                AssertNull(TargetPath.GetDirectoryName("armada-server.exe", HostPlatformEnum.Windows), "no directory part");

                RegistrationContext linux = new RegistrationContext();
                linux.Platform = HostPlatformEnum.Linux;
                linux.ExecutablePath = "/opt/My Apps/armada-server";
                AssertEqual("/opt/My Apps", linux.WorkingDirectory, "Linux default working directory");

                RegistrationContext windows = new RegistrationContext();
                windows.Platform = HostPlatformEnum.Windows;
                windows.ExecutablePath = "C:\\Program Files\\Armada\\armada-server.exe";
                AssertEqual("C:\\Program Files\\Armada", windows.WorkingDirectory, "Windows default working directory");

                RegistrationContext bare = new RegistrationContext();
                bare.Platform = HostPlatformEnum.Linux;
                bare.ExecutablePath = "armada-server";
                AssertEqual("/", bare.WorkingDirectory, "no directory part falls back to the root");
            }));

            cases.Add(Case("launchd_login_item_plist", "launchd Harbor login item", TestTags.Positive, () =>
            {
                RegistrationContext context = HarborContext(HostPlatformEnum.MacOS, "/Applications/Armada Harbor.app/Contents/MacOS/Armada.Harbor", false, NewHome());
                string plist = LaunchdPlistBuilder.BuildLoginItem(context);
                Dictionary<string, XElement> keys = ParsePlist(plist);
                AssertEqual("com.joelchristner.armada.harbor", keys["Label"].Value);
                List<string> argv = keys["ProgramArguments"].Elements("string").Select(e => e.Value).ToList();
                AssertEqual("/Applications/Armada Harbor.app/Contents/MacOS/Armada.Harbor", argv[0]);
                AssertEqual("--minimized", argv[1]);
                AssertTrue(keys.ContainsKey("RunAtLoad"));
                AssertFalse(keys.ContainsKey("KeepAlive"), "quitting Harbor must not relaunch it");
                AssertEqual("Interactive", keys["ProcessType"].Value);
                LintPlist(plist);
            }));

            cases.Add(Case("xdg_autostart_entry", "XDG autostart desktop entry and Exec quoting", TestTags.Positive, () =>
            {
                RegistrationContext context = HarborContext(HostPlatformEnum.Linux, "/home/a b/apps/armada-harbor", false, NewHome());
                string entry = XdgAutostartBuilder.Build(context);
                AssertStartsWith("[Desktop Entry]\n", entry);
                AssertContains("Type=Application\n", entry);
                AssertContains("Name=Armada Harbor\n", entry);
                AssertContains("Exec=\"/home/a b/apps/armada-harbor\" --minimized\n", entry);
                AssertContains("Terminal=false\n", entry);
                AssertContains("X-GNOME-Autostart-enabled=true\n", entry);
                AssertEqual("\"a\\$b\"", XdgAutostartBuilder.QuoteExecArgument("a$b"));
                AssertEqual("50%%", XdgAutostartBuilder.QuoteExecArgument("50%"));
                AssertEqual("plain", XdgAutostartBuilder.QuoteExecArgument("plain"));
                ValidateDesktopFile(entry);
            }));

            cases.Add(Case("windows_command_lines", "sc.exe and reg.exe argument lists", TestTags.Positive, () =>
            {
                RegistrationContext context = AdmiralContext(HostPlatformEnum.Windows, "C:\\Program Files\\Armada Admiral\\Armada.Server.exe", true, NewHome());
                AssertEqual("\"C:\\Program Files\\Armada Admiral\\Armada.Server.exe\" --run-service", WindowsCommandBuilder.BuildCommandLine(context));

                List<string> create = WindowsCommandBuilder.BuildServiceCreate(context, false);
                AssertEqual("create|armada|binPath=|\"C:\\Program Files\\Armada Admiral\\Armada.Server.exe\" --run-service|start=|auto|DisplayName=|Armada Admiral", String.Join("|", create));
                AssertEqual("config", WindowsCommandBuilder.BuildServiceCreate(context, true)[0]);
                AssertEqual("failure|armada|reset=|86400|actions=|restart/5000/restart/5000/restart/60000", String.Join("|", WindowsCommandBuilder.BuildServiceFailureActions(context)));

                RegistrationContext harbor = HarborContext(HostPlatformEnum.Windows, "C:\\Program Files\\Armada Harbor\\Armada.Harbor.exe", false, NewHome());
                List<string> add = WindowsCommandBuilder.BuildRunKeyAdd(harbor);
                AssertEqual("add|HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run|/v|Armada Harbor|/t|REG_SZ|/d|\"C:\\Program Files\\Armada Harbor\\Armada.Harbor.exe\" --minimized|/f", String.Join("|", add));

                AssertEqual("plain", WindowsCommandBuilder.QuoteArgument("plain"));
                AssertEqual("\"\"", WindowsCommandBuilder.QuoteArgument(""));
                AssertEqual("\"C:\\dir with space\\\\\"", WindowsCommandBuilder.QuoteArgument("C:\\dir with space\\"));
                AssertEqual("\"say \\\"hi\\\"\"", WindowsCommandBuilder.QuoteArgument("say \"hi\""));
            }));

            // Linux registrar flows.
            cases.Add(Case("linux_user_install_idempotent", "Linux user-scope install is idempotent", TestTags.Positive, () =>
            {
                string home = NewHome();
                RecordingCommandRunner runner = new RecordingCommandRunner();
                RegistrationContext context = AdmiralContext(HostPlatformEnum.Linux, "/opt/armada/armada-server", false, home);
                StringWriter output = new StringWriter();
                ServiceRegistrar registrar = new ServiceRegistrar(context, runner, output);

                AssertEqual(RegistrationExitCode.Success, registrar.Install());
                string path = Path.Combine(home, ".config", "systemd", "user", "armada.service");
                AssertEqual(path, registrar.GetDefinitionPath());
                AssertTrue(File.Exists(path), "unit written");
                AssertEqual(SystemdUnitBuilder.Build(context, false), File.ReadAllText(path));
                AssertEqual("systemctl --user daemon-reload|systemctl --user enable armada.service|systemctl --user restart armada.service", String.Join("|", runner.Calls));
                AssertContains("[install-service] wrote " + path, output.ToString());

                runner.Calls.Clear();
                AssertEqual(RegistrationExitCode.Success, registrar.Install());
                AssertEqual("systemctl --user enable armada.service|systemctl --user start armada.service", String.Join("|", runner.Calls));
                AssertContains("definition unchanged", output.ToString());
                AssertEqual(1, Directory.GetFiles(Path.GetDirectoryName(path)!).Length);
            }));

            cases.Add(Case("linux_system_install_and_no_start", "Linux system-scope install with --service-user and --no-start", TestTags.Positive, () =>
            {
                string home = NewHome();
                string systemDir = Path.Combine(home, "etc-systemd-system");
                RecordingCommandRunner runner = new RecordingCommandRunner();
                RegistrationContext context = AdmiralContext(HostPlatformEnum.Linux, "/usr/lib/armada-server/Armada.Server", true, home);
                context.SystemdSystemDirectory = systemDir;
                context.ServiceUser = "armada";
                context.NoStart = true;
                ServiceRegistrar registrar = new ServiceRegistrar(context, runner, new StringWriter());

                AssertEqual(RegistrationExitCode.Success, registrar.Install());
                string unit = File.ReadAllText(Path.Combine(systemDir, "armada.service"));
                AssertContains("User=armada\n", unit);
                AssertEqual("systemctl daemon-reload|systemctl enable armada.service", String.Join("|", runner.Calls));
            }));

            cases.Add(Case("linux_dry_run_changes_nothing", "Linux --dry-run prints the unit and commands only", TestTags.Positive, () =>
            {
                string home = NewHome();
                RecordingCommandRunner runner = new RecordingCommandRunner();
                RegistrationContext context = AdmiralContext(HostPlatformEnum.Linux, "/opt/armada/armada-server", false, home);
                context.DryRun = true;
                StringWriter output = new StringWriter();
                ServiceRegistrar registrar = new ServiceRegistrar(context, runner, output);

                AssertEqual(RegistrationExitCode.Success, registrar.Install());
                AssertFalse(File.Exists(registrar.GetDefinitionPath()), "dry run must not write");
                AssertEqual(0, runner.Calls.Count, "dry run must not run commands");
                string text = output.ToString();
                AssertContains("dry run, nothing is changed", text);
                AssertContains("would write " + registrar.GetDefinitionPath() + ":", text);
                AssertContains("    ExecStart=/opt/armada/armada-server --run-service", text);
                AssertContains("would run: systemctl --user daemon-reload", text);
                AssertContains("would run: systemctl --user enable armada.service", text);
                AssertContains("would run: systemctl --user restart armada.service", text);
            }));

            cases.Add(Case("linux_uninstall_idempotent", "Linux uninstall removes the unit, then is a no-op", TestTags.Positive, () =>
            {
                string home = NewHome();
                RecordingCommandRunner runner = new RecordingCommandRunner();
                RegistrationContext context = AdmiralContext(HostPlatformEnum.Linux, "/opt/armada/armada-server", false, home);
                ServiceRegistrar registrar = new ServiceRegistrar(context, runner, new StringWriter());
                AssertEqual(RegistrationExitCode.Success, registrar.Install());
                runner.Calls.Clear();

                AssertEqual(RegistrationExitCode.Success, registrar.Uninstall());
                AssertFalse(File.Exists(registrar.GetDefinitionPath()));
                AssertEqual("systemctl --user disable --now armada.service|systemctl --user daemon-reload", String.Join("|", runner.Calls));

                runner.Calls.Clear();
                StringWriter output = new StringWriter();
                AssertEqual(RegistrationExitCode.Success, new ServiceRegistrar(context, runner, output).Uninstall());
                AssertEqual(0, runner.Calls.Count);
                AssertContains("nothing to do", output.ToString());
            }));

            cases.Add(Case("linux_failures_return_codes", "Linux command failure and misuse return non-zero codes", TestTags.Negative, () =>
            {
                string home = NewHome();
                RecordingCommandRunner runner = new RecordingCommandRunner();
                runner.Responder = (file, args) => args.Contains("enable") ? new CommandResult(1, "", "Failed to connect to bus: No medium found") : null;
                StringWriter output = new StringWriter();
                ServiceRegistrar registrar = new ServiceRegistrar(AdmiralContext(HostPlatformEnum.Linux, "/opt/a/armada-server", false, home), runner, output);
                AssertEqual(RegistrationExitCode.Failed, registrar.Install());
                AssertContains("ERROR: systemctl enable failed: Failed to connect to bus", output.ToString());

                RegistrationContext userWithServiceUser = AdmiralContext(HostPlatformEnum.Linux, "/opt/a/armada-server", false, NewHome());
                userWithServiceUser.ServiceUser = "bob";
                AssertEqual(RegistrationExitCode.InvalidArguments, new ServiceRegistrar(userWithServiceUser, new RecordingCommandRunner(), new StringWriter()).Install());

                AssertEqual(RegistrationExitCode.UnsupportedPlatform,
                    new ServiceRegistrar(AdmiralContext(HostPlatformEnum.Unsupported, "/x/y", false, NewHome()), new RecordingCommandRunner(), new StringWriter()).Install());
            }));

            // macOS registrar flows.
            cases.Add(Case("macos_user_install_flow", "macOS user install bootstraps once, reloads on change", TestTags.Positive, () =>
            {
                string home = NewHome();
                bool loaded = false;
                RecordingCommandRunner runner = new RecordingCommandRunner();
                runner.Responder = (file, args) =>
                {
                    if (file == "launchctl" && args[0] == "print") return new CommandResult(loaded ? 0 : 113, "", "");
                    if (file == "launchctl" && args[0] == "bootstrap") loaded = true;
                    return null;
                };
                RegistrationContext context = AdmiralContext(HostPlatformEnum.MacOS, "/usr/local/lib/armada-server/Armada.Server", false, home);
                context.UserId = 501;
                ServiceRegistrar registrar = new ServiceRegistrar(context, runner, new StringWriter());
                string path = Path.Combine(home, "Library", "LaunchAgents", "com.joelchristner.armada.server.plist");

                AssertEqual(RegistrationExitCode.Success, registrar.Install());
                AssertEqual(path, registrar.GetDefinitionPath());
                AssertEqual(LaunchdPlistBuilder.BuildServiceAgent(context), File.ReadAllText(path));
                AssertEqual("launchctl print gui/501/com.joelchristner.armada.server|launchctl bootstrap gui/501 " + path, String.Join("|", runner.Calls));

                runner.Calls.Clear();
                AssertEqual(RegistrationExitCode.Success, registrar.Install());
                AssertEqual("launchctl print gui/501/com.joelchristner.armada.server", String.Join("|", runner.Calls), "unchanged and loaded: nothing to do");

                runner.Calls.Clear();
                context.ExecutablePath = "/usr/local/lib/armada-server/Armada.Server.new";
                AssertEqual(RegistrationExitCode.Success, registrar.Install());
                AssertEqual("launchctl print gui/501/com.joelchristner.armada.server|launchctl bootout gui/501/com.joelchristner.armada.server|launchctl bootstrap gui/501 " + path, String.Join("|", runner.Calls));

                runner.Calls.Clear();
                AssertEqual(RegistrationExitCode.Success, registrar.Uninstall());
                AssertFalse(File.Exists(path));
                AssertTrue(runner.Ran("launchctl bootout gui/501/com.joelchristner.armada.server"));
            }));

            cases.Add(Case("macos_root_install_targets_console_user", "macOS root install writes /Library/LaunchAgents and loads for the console user", TestTags.Positive, () =>
            {
                string home = NewHome();
                string agents = Path.Combine(home, "Library-LaunchAgents");
                RecordingCommandRunner runner = new RecordingCommandRunner();
                runner.Responder = (file, args) =>
                {
                    if (file == "stat") return new CommandResult(0, "alice\n", "");
                    if (file == "id") return new CommandResult(0, "502\n", "");
                    if (file == "launchctl" && args[0] == "print") return new CommandResult(113, "", "");
                    return null;
                };
                RegistrationContext context = AdmiralContext(HostPlatformEnum.MacOS, "/usr/local/lib/armada-server/Armada.Server", true, home);
                context.SystemLaunchAgentsDirectory = agents;
                File.WriteAllText(Path.Combine(EnsureDir(Path.Combine(home, "Library", "LaunchAgents")), ServiceRegistrar.LegacyMacLabel + ".plist"), "<plist/>");
                StringWriter output = new StringWriter();

                AssertEqual(RegistrationExitCode.Success, new ServiceRegistrar(context, runner, output).Install());
                string path = Path.Combine(agents, "com.joelchristner.armada.server.plist");
                AssertTrue(File.Exists(path));
                AssertTrue(runner.Ran("stat -f%Su /dev/console"));
                AssertTrue(runner.Ran("id -u alice"));
                AssertTrue(runner.Ran("launchctl bootstrap gui/502 " + path));
                AssertContains("warning: ", output.ToString());
                AssertContains(ServiceRegistrar.LegacyMacLabel, output.ToString());
            }));

            // Windows registrar flows (recorded sc.exe argument lists).
            cases.Add(Case("windows_install_requires_elevation", "Windows install without elevation fails with exit 4", TestTags.Negative, () =>
            {
                RecordingCommandRunner runner = new RecordingCommandRunner();
                RegistrationContext context = AdmiralContext(HostPlatformEnum.Windows, "C:\\Armada\\Armada.Server.exe", false, NewHome());
                AssertEqual(RegistrationExitCode.InsufficientPrivileges, new ServiceRegistrar(context, runner, new StringWriter()).Install());
                AssertEqual(RegistrationExitCode.InsufficientPrivileges, new ServiceRegistrar(context, runner, new StringWriter()).Uninstall());
                AssertEqual(0, runner.Calls.Count);
            }));

            cases.Add(Case("windows_install_create_then_config", "Windows install creates, then reconfigures an existing service", TestTags.Positive, () =>
            {
                bool exists = false;
                RecordingCommandRunner runner = new RecordingCommandRunner();
                runner.Responder = (file, args) =>
                {
                    if (args[0] == "query") return new CommandResult(exists ? 0 : 1060, exists ? "STATE : 4 RUNNING" : "", "");
                    if (args[0] == "create") exists = true;
                    if (args[0] == "start" && exists && runner.Calls.Count(c => c.StartsWith("sc.exe start")) > 1) return new CommandResult(1056, "", "An instance of the service is already running.");
                    return null;
                };
                RegistrationContext context = AdmiralContext(HostPlatformEnum.Windows, "C:\\Program Files\\Armada Admiral\\Armada.Server.exe", true, NewHome());
                ServiceRegistrar registrar = new ServiceRegistrar(context, runner, new StringWriter());

                AssertEqual(RegistrationExitCode.Success, registrar.Install());
                AssertEqual("sc.exe query armada", runner.Calls[0]);
                AssertStartsWith("sc.exe create armada binPath= \"C:\\Program Files\\Armada Admiral\\Armada.Server.exe\" --run-service start= auto DisplayName= Armada Admiral", runner.Calls[1]);
                AssertStartsWith("sc.exe description armada", runner.Calls[2]);
                AssertStartsWith("sc.exe failure armada", runner.Calls[3]);
                AssertEqual("sc.exe start armada", runner.Calls[4]);

                runner.Calls.Clear();
                StringWriter output = new StringWriter();
                AssertEqual(RegistrationExitCode.Success, new ServiceRegistrar(context, runner, output).Install());
                AssertStartsWith("sc.exe config armada binPath=", runner.Calls[1]);
                AssertFalse(runner.Ran("sc.exe create"), "existing service is reconfigured, not recreated");
                AssertContains("already registered", output.ToString());
            }));

            cases.Add(Case("windows_uninstall_stops_then_deletes", "Windows uninstall stops, waits, deletes; absent service is a no-op", TestTags.Positive, () =>
            {
                // Exit codes only: stop accepted (0), still stopping (1061 ERROR_SERVICE_CANNOT_ACCEPT_CTRL), then
                // stopped (1062 ERROR_SERVICE_NOT_ACTIVE). Output text is deliberately not English.
                int stops = 0;
                RecordingCommandRunner runner = new RecordingCommandRunner();
                runner.Responder = (file, args) =>
                {
                    if (args[0] == "query") return new CommandResult(0, "ETAT : 4 EN COURS", "");
                    if (args[0] == "stop")
                    {
                        stops++;
                        if (stops == 1) return new CommandResult(0, "ETAT : 3 ARRET EN ATTENTE", "");
                        return stops == 2 ? new CommandResult(1061, "", "") : new CommandResult(1062, "", "");
                    }
                    return null;
                };
                RegistrationContext context = AdmiralContext(HostPlatformEnum.Windows, "C:\\Armada\\Armada.Server.exe", true, NewHome());
                ServiceRegistrar registrar = new ServiceRegistrar(context, runner, new StringWriter());
                registrar.PollIntervalMs = 0;

                AssertEqual(RegistrationExitCode.Success, registrar.Uninstall());
                AssertEqual("sc.exe query armada|sc.exe stop armada|sc.exe stop armada|sc.exe stop armada|sc.exe delete armada", String.Join("|", runner.Calls));

                // Already stopped: the first stop reports ERROR_SERVICE_NOT_ACTIVE and there is no wait.
                RecordingCommandRunner stopped = new RecordingCommandRunner();
                stopped.Responder = (file, args) => args[0] == "stop" ? new CommandResult(1062, "", "") : null;
                AssertEqual(RegistrationExitCode.Success, new ServiceRegistrar(context, stopped, new StringWriter()) { PollIntervalMs = 0 }.Uninstall());
                AssertEqual("sc.exe query armada|sc.exe stop armada|sc.exe delete armada", String.Join("|", stopped.Calls));

                RecordingCommandRunner absent = new RecordingCommandRunner();
                absent.Responder = (file, args) => new CommandResult(1060, "", "The specified service does not exist as an installed service.");
                StringWriter output = new StringWriter();
                AssertEqual(RegistrationExitCode.Success, new ServiceRegistrar(context, absent, output).Uninstall());
                AssertEqual(1, absent.Calls.Count);
                AssertContains("nothing to do", output.ToString());
            }));

            cases.Add(Case("windows_dry_run_only_probes", "Windows --dry-run runs only the read-only query", TestTags.Positive, () =>
            {
                RecordingCommandRunner runner = new RecordingCommandRunner();
                runner.Responder = (file, args) => new CommandResult(1060, "", "");
                RegistrationContext context = AdmiralContext(HostPlatformEnum.Windows, "C:\\Armada\\Armada.Server.exe", false, NewHome());
                context.DryRun = true;
                StringWriter output = new StringWriter();
                AssertEqual(RegistrationExitCode.Success, new ServiceRegistrar(context, runner, output).Install());
                AssertEqual("sc.exe query armada", String.Join("|", runner.Calls));
                AssertContains("would run: sc.exe create armada binPath= \"C:\\Armada\\Armada.Server.exe --run-service\" start= auto DisplayName= \"Armada Admiral\"", output.ToString());
                AssertContains("would run: sc.exe start armada", output.ToString());
            }));

            // Harbor startup registrar flows.
            cases.Add(Case("startup_linux_autostart", "Harbor Linux autostart install, idempotence, uninstall, root refusal", TestTags.Positive, () =>
            {
                string home = NewHome();
                RecordingCommandRunner runner = new RecordingCommandRunner();
                RegistrationContext context = HarborContext(HostPlatformEnum.Linux, "/usr/lib/armada-harbor/Armada.Harbor", false, home);
                context.XdgConfigHome = Path.Combine(home, "xdg");
                StartupRegistrar registrar = new StartupRegistrar(context, runner, new StringWriter());
                string path = Path.Combine(home, "xdg", "autostart", "armada-harbor.desktop");

                AssertEqual(RegistrationExitCode.Success, registrar.Install());
                AssertEqual(path, registrar.GetDefinitionPath());
                AssertEqual(XdgAutostartBuilder.Build(context), File.ReadAllText(path));
                StringWriter second = new StringWriter();
                AssertEqual(RegistrationExitCode.Success, new StartupRegistrar(context, runner, second).Install());
                AssertContains("already registered", second.ToString());
                AssertEqual(RegistrationExitCode.Success, registrar.Uninstall());
                AssertFalse(File.Exists(path));
                AssertEqual(RegistrationExitCode.Success, registrar.Uninstall());
                AssertEqual(0, runner.Calls.Count, "no commands on Linux");

                context.IsElevated = true;
                AssertEqual(RegistrationExitCode.InsufficientPrivileges, new StartupRegistrar(context, runner, new StringWriter()).Install());
            }));

            cases.Add(Case("startup_macos_login_item", "Harbor macOS login item is written without launching Harbor", TestTags.Positive, () =>
            {
                string home = NewHome();
                RecordingCommandRunner runner = new RecordingCommandRunner();
                RegistrationContext context = HarborContext(HostPlatformEnum.MacOS, "/Applications/Armada Harbor.app/Contents/MacOS/Armada.Harbor", false, home);
                StartupRegistrar registrar = new StartupRegistrar(context, runner, new StringWriter());
                AssertEqual(RegistrationExitCode.Success, registrar.Install());
                string path = Path.Combine(home, "Library", "LaunchAgents", "com.joelchristner.armada.harbor.plist");
                AssertEqual(LaunchdPlistBuilder.BuildLoginItem(context), File.ReadAllText(path));
                AssertEqual(0, runner.Calls.Count, "no launchctl bootstrap: a second Harbor must not start now");

                context.DryRun = true;
                StringWriter dry = new StringWriter();
                AssertEqual(RegistrationExitCode.Success, new StartupRegistrar(context, runner, dry).Uninstall());
                AssertTrue(File.Exists(path), "dry-run uninstall keeps the file");
                AssertContains("would remove " + path, dry.ToString());
            }));

            cases.Add(Case("startup_windows_run_key", "Harbor Windows Run value add, already-current, delete", TestTags.Positive, () =>
            {
                string value = String.Empty;
                RecordingCommandRunner runner = new RecordingCommandRunner();
                runner.Responder = (file, args) =>
                {
                    if (args[0] == "query") return String.IsNullOrEmpty(value) ? new CommandResult(1, "", "ERROR: The system was unable to find the specified registry key or value.") : new CommandResult(0, "    Armada Harbor    REG_SZ    " + value, "");
                    if (args[0] == "add") value = args[7];
                    if (args[0] == "delete") value = String.Empty;
                    return null;
                };
                RegistrationContext context = HarborContext(HostPlatformEnum.Windows, "C:\\Program Files\\Armada Harbor\\Armada.Harbor.exe", true, NewHome());
                StartupRegistrar registrar = new StartupRegistrar(context, runner, new StringWriter());

                AssertEqual(RegistrationExitCode.Success, registrar.Install());
                AssertEqual("\"C:\\Program Files\\Armada Harbor\\Armada.Harbor.exe\" --minimized", value);
                runner.Calls.Clear();
                AssertEqual(RegistrationExitCode.Success, registrar.Install());
                AssertEqual(1, runner.Calls.Count, "already current: query only");
                AssertEqual(RegistrationExitCode.Success, registrar.Uninstall());
                AssertTrue(runner.Ran("reg.exe delete HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run /v Armada Harbor /f"));
                AssertEqual(String.Empty, value);
            }));

            cases.Add(Case("startup_windows_run_key_requires_exact_value", "Harbor Windows Run value that merely contains the command line is rewritten", TestTags.Negative, () =>
            {
                RegistrationContext context = HarborContext(HostPlatformEnum.Windows, "C:\\Program Files\\Armada Harbor\\Armada.Harbor.exe", true, NewHome());
                string expected = WindowsCommandBuilder.BuildCommandLine(context);
                string value = expected + " --stale-extra-flag";
                RecordingCommandRunner runner = new RecordingCommandRunner();
                runner.Responder = (file, args) =>
                {
                    if (args[0] == "query") return new CommandResult(0, "\r\nHKEY_CURRENT_USER\\Software\\Microsoft\\Windows\\CurrentVersion\\Run\r\n    Armada Harbor    REG_SZ    " + value + "\r\n", "");
                    if (args[0] == "add") value = args[7];
                    return null;
                };

                AssertEqual(RegistrationExitCode.Success, new StartupRegistrar(context, runner, new StringWriter()).Install());
                AssertTrue(runner.Ran("reg.exe add"), "a value that only contains the command line must be updated");
                AssertEqual(expected, value);

                AssertTrue(WindowsCommandBuilder.TryGetRegQueryStringValue("    Armada Harbor    REG_SZ    a  b", "Armada Harbor", out string data), "parses the data");
                AssertEqual("a  b", data);
                AssertFalse(WindowsCommandBuilder.TryGetRegQueryStringValue("    Armada Harbor Beta    REG_SZ    x", "Armada Harbor", out string _), "name must match exactly");
            }));

            // Consistency with the installers and the real binary.
            cases.Add(Case("publisher_manifest_matches_flags", "publisher.json service and startup blocks match the flags", TestTags.Positive, () =>
            {
                JsonSerializerOptions options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                PublisherManifestView? manifest = JsonSerializer.Deserialize<PublisherManifestView>(File.ReadAllText(Path.Combine(FindRepositoryRoot(), "publisher.json")), options);
                AssertNotNull(manifest, "publisher.json");
                PublisherArtifactView server = manifest!.Artifacts.First(a => a.Id == "server");
                PublisherArtifactView harbor = manifest.Artifacts.First(a => a.Id == "harbor");
                AssertNotNull(server.Service, "server service block");
                AssertNotNull(harbor.Startup, "harbor startup block");
                AssertEqual(RegistrationDefaults.AdmiralServiceName, server.Service!.ServiceName);
                AssertEqual(RegistrationDefaults.AdmiralDisplayName, server.Service.DisplayName);
                AssertEqual(RegistrationDefaults.AdmiralLabel, server.BundleIdentifier);
                AssertEqual(RegistrationCommandLine.RunServiceFlag, server.Service.RunArgs);
                AssertEqual(RegistrationCommandLine.InstallServiceFlag, server.Service.InstallArgs);
                AssertEqual(RegistrationCommandLine.UninstallServiceFlag, server.Service.UninstallArgs);
                AssertEqual(RegistrationDefaults.HarborLabel, harbor.BundleIdentifier);
                AssertEqual(RegistrationDefaults.HarborDisplayName, harbor.DisplayName);
                AssertEqual(RegistrationCommandLine.InstallStartupFlag, harbor.Startup!.InstallArgs);
                AssertEqual(RegistrationCommandLine.UninstallStartupFlag, harbor.Startup.UninstallArgs);
            }));

            cases.Add(CaseAsync("server_binary_dry_run", "armada-server --install-service --dry-run exits 0 and changes nothing", TestTags.Process, async () =>
            {
                string dll = typeof(Armada.Server.ArmadaServer).Assembly.Location;
                ProcessOutcome dry = await RunDotnetAsync(dll, "--install-service", "--dry-run").ConfigureAwait(false);
                AssertEqual(0, dry.ExitCode, "exit code; output: " + dry.Output);
                AssertContains("[install-service] dry run", dry.Output);
                AssertContains("--run-service", dry.Output);

                ProcessOutcome conflict = await RunDotnetAsync(dll, "--install-service", "--uninstall-service").ConfigureAwait(false);
                AssertEqual(RegistrationExitCode.InvalidArguments, conflict.ExitCode, "conflicting flags");

                ProcessOutcome harborFlag = await RunDotnetAsync(dll, "--install-startup").ConfigureAwait(false);
                AssertEqual(RegistrationExitCode.InvalidArguments, harborFlag.ExitCode, "harbor flag on the server");
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Service and Startup Registration", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static RegistrationContext AdmiralContext(HostPlatformEnum platform, string executable, bool elevated, string home)
        {
            RegistrationContext context = new RegistrationContext();
            context.Platform = platform;
            context.ExecutablePath = executable;
            context.IsElevated = elevated;
            context.HomeDirectory = home;
            if (platform == HostPlatformEnum.Windows)
            {
                int slash = executable.LastIndexOf('\\');
                context.WorkingDirectory = slash > 0 ? executable.Substring(0, slash) : "C:\\";
            }
            return RegistrationDefaults.ApplyAdmiral(context);
        }

        private static RegistrationContext HarborContext(HostPlatformEnum platform, string executable, bool elevated, string home)
        {
            RegistrationContext context = new RegistrationContext();
            context.Platform = platform;
            context.ExecutablePath = executable;
            context.IsElevated = elevated;
            context.HomeDirectory = home;
            return RegistrationDefaults.ApplyHarbor(context);
        }

        private static string NewHome()
        {
            return TestTemp.NewDirectory("registration");
        }

        private static string EnsureDir(string path)
        {
            Directory.CreateDirectory(path);
            return path;
        }

        private static Dictionary<string, XElement> ParsePlist(string plist)
        {
            XmlReaderSettings settings = new XmlReaderSettings();
            settings.DtdProcessing = DtdProcessing.Ignore;
            settings.XmlResolver = null;
            using (StringReader text = new StringReader(plist))
            using (XmlReader reader = XmlReader.Create(text, settings))
            {
                XDocument document = XDocument.Load(reader);
                XElement dict = document.Root!.Element("dict")!;
                Dictionary<string, XElement> result = new Dictionary<string, XElement>(StringComparer.Ordinal);
                List<XElement> children = dict.Elements().ToList();
                for (int i = 0; i + 1 < children.Count; i += 2)
                {
                    AssertEqual("key", children[i].Name.LocalName, "plist dict alternates key/value");
                    result[children[i].Value] = children[i + 1];
                }
                return result;
            }
        }

        private static void LintPlist(string plist)
        {
            // plutil ships with macOS; elsewhere the XML parse above is the check.
            if (!OperatingSystem.IsMacOS() || !File.Exists("/usr/bin/plutil")) return;
            string file = TestTemp.NewFile("plist", ".plist");
            File.WriteAllText(file, plist);
            ProcessOutcome outcome = RunTool("/usr/bin/plutil", "-lint", file);
            AssertEqual(0, outcome.ExitCode, "plutil -lint: " + outcome.Output);
        }

        private static void VerifyWithSystemdAnalyze(string unit)
        {
            // systemd-analyze exists on systemd Linux hosts (the CI runner); elsewhere the content assertions are the check.
            if (!OperatingSystem.IsLinux() || !File.Exists("/usr/bin/systemd-analyze")) return;
            string directory = TestTemp.NewDirectory("unit");
            string file = Path.Combine(directory, "armada-test.service");
            File.WriteAllText(file, unit.Replace("\"/opt/My Apps/armada-server\"", "/bin/true").Replace("WorkingDirectory=/opt/My Apps", "WorkingDirectory=/tmp"));
            ProcessOutcome outcome = RunTool("/usr/bin/systemd-analyze", "verify", "--user", file);
            AssertFalse(outcome.Output.Contains("Unknown key") || outcome.Output.Contains("bad-setting") || outcome.Output.Contains("Failed to parse"),
                "systemd-analyze verify: " + outcome.Output);
        }

        private static void ValidateDesktopFile(string entry)
        {
            string? validator = null;
            foreach (string candidate in new string[] { "/usr/bin/desktop-file-validate", "/opt/homebrew/bin/desktop-file-validate", "/usr/local/bin/desktop-file-validate" })
            {
                if (File.Exists(candidate)) validator = candidate;
            }
            if (validator == null) return;
            string file = Path.Combine(TestTemp.NewDirectory("desktop"), "armada-harbor.desktop");
            File.WriteAllText(file, entry);
            ProcessOutcome outcome = RunTool(validator, file);
            AssertFalse(outcome.Output.Contains("error:"), "desktop-file-validate: " + outcome.Output);
        }

        private static ProcessOutcome RunTool(string file, params string[] args)
        {
            ProcessStartInfo info = new ProcessStartInfo(file);
            foreach (string arg in args) info.ArgumentList.Add(arg);
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.UseShellExecute = false;
            using (Process process = Process.Start(info)!)
            {
                Task<string> stdout = process.StandardOutput.ReadToEndAsync();
                Task<string> stderr = process.StandardError.ReadToEndAsync();
                process.WaitForExit(30000);
                return new ProcessOutcome(process.HasExited ? process.ExitCode : -1, stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult());
            }
        }

        private static async Task<ProcessOutcome> RunDotnetAsync(string dll, params string[] args)
        {
            ProcessStartInfo info = new ProcessStartInfo("dotnet");
            info.ArgumentList.Add(dll);
            foreach (string arg in args) info.ArgumentList.Add(arg);
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.UseShellExecute = false;
            // Point any accidental data-directory access at a throwaway location.
            info.Environment["ARMADA_DATA_DIR"] = TestTemp.NewDirectory("registration-data");
            using (Process process = Process.Start(info)!)
            using (CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(60)))
            {
                Task<string> stdout = process.StandardOutput.ReadToEndAsync();
                Task<string> stderr = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
                return new ProcessOutcome(process.ExitCode, await stdout.ConfigureAwait(false) + await stderr.ConfigureAwait(false));
            }
        }

        private static string FindRepositoryRoot()
        {
            DirectoryInfo? current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current != null)
            {
                if (File.Exists(Path.Combine(current.FullName, "publisher.json")) && Directory.Exists(Path.Combine(current.FullName, "src")))
                {
                    return current.FullName;
                }
                current = current.Parent;
            }
            throw new DirectoryNotFoundException("Could not locate repository root from test base directory.");
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) =>
                {
                    body();
                    return Task.CompletedTask;
                },
                tags: new List<string> { tag });
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion
    }
}

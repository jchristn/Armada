namespace Armada.Core.Hosting
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Parsed registration flags from a program's command line. Arguments that are not registration flags are
    /// ignored so the host program can keep its own options.
    /// </summary>
    public class RegistrationCommandLine
    {
        #region Public-Members

        /// <summary>
        /// Long flag that installs the Admiral service.
        /// </summary>
        public const string InstallServiceFlag = "--install-service";

        /// <summary>
        /// Long flag that removes the Admiral service.
        /// </summary>
        public const string UninstallServiceFlag = "--uninstall-service";

        /// <summary>
        /// Long flag the service manager passes when it starts the Admiral.
        /// </summary>
        public const string RunServiceFlag = "--run-service";

        /// <summary>
        /// Long flag that registers Harbor to start at login.
        /// </summary>
        public const string InstallStartupFlag = "--install-startup";

        /// <summary>
        /// Long flag that removes the Harbor login registration.
        /// </summary>
        public const string UninstallStartupFlag = "--uninstall-startup";

        /// <summary>
        /// Print the definition and the commands that would run, without changing anything.
        /// </summary>
        public const string DryRunFlag = "--dry-run";

        /// <summary>
        /// Register the service but do not start it now.
        /// </summary>
        public const string NoStartFlag = "--no-start";

        /// <summary>
        /// Linux system scope only: account the systemd unit runs as (User=).
        /// </summary>
        public const string ServiceUserOption = "--service-user";

        /// <summary>
        /// Requested action. <see cref="RegistrationActionEnum.None"/> when no registration flag was given.
        /// </summary>
        public RegistrationActionEnum Action { get; set; } = RegistrationActionEnum.None;

        /// <summary>
        /// True when --dry-run was given.
        /// </summary>
        public bool DryRun { get; set; } = false;

        /// <summary>
        /// True when --no-start was given.
        /// </summary>
        public bool NoStart { get; set; } = false;

        /// <summary>
        /// Value of --service-user, or null.
        /// </summary>
        public string? ServiceUser { get; set; } = null;

        /// <summary>
        /// Problems found while parsing. Empty when the command line is valid.
        /// </summary>
        public List<string> Errors
        {
            get { return _Errors; }
            set { _Errors = value ?? new List<string>(); }
        }

        /// <summary>
        /// True when the command line parsed without errors.
        /// </summary>
        public bool IsValid
        {
            get { return _Errors.Count == 0; }
        }

        #endregion

        #region Private-Members

        private List<string> _Errors = new List<string>();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Parse registration flags out of a command line.
        /// </summary>
        /// <param name="args">Program arguments. Null is treated as empty.</param>
        /// <returns>The parsed flags; check <see cref="IsValid"/> before acting on them.</returns>
        public static RegistrationCommandLine Parse(string[]? args)
        {
            RegistrationCommandLine result = new RegistrationCommandLine();
            if (args == null) return result;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i] ?? String.Empty;
                RegistrationActionEnum action = ActionForFlag(arg);
                if (action != RegistrationActionEnum.None)
                {
                    if (result.Action != RegistrationActionEnum.None && result.Action != action)
                    {
                        result.Errors.Add("only one of " + InstallServiceFlag + ", " + UninstallServiceFlag + ", " + RunServiceFlag + ", "
                            + InstallStartupFlag + ", " + UninstallStartupFlag + " may be given");
                    }
                    else
                    {
                        result.Action = action;
                    }
                    continue;
                }

                if (String.Equals(arg, DryRunFlag, StringComparison.Ordinal))
                {
                    result.DryRun = true;
                }
                else if (String.Equals(arg, NoStartFlag, StringComparison.Ordinal))
                {
                    result.NoStart = true;
                }
                else if (String.Equals(arg, ServiceUserOption, StringComparison.Ordinal))
                {
                    if (i + 1 >= args.Length || String.IsNullOrWhiteSpace(args[i + 1]) || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        result.Errors.Add(ServiceUserOption + " needs an account name");
                    }
                    else
                    {
                        result.ServiceUser = args[i + 1].Trim();
                        i++;
                    }
                }
                else if (arg.StartsWith(ServiceUserOption + "=", StringComparison.Ordinal))
                {
                    string value = arg.Substring(ServiceUserOption.Length + 1).Trim();
                    if (String.IsNullOrEmpty(value)) result.Errors.Add(ServiceUserOption + " needs an account name");
                    else result.ServiceUser = value;
                }
            }

            if (result.ServiceUser != null && !IsSafeAccountName(result.ServiceUser))
            {
                result.Errors.Add(ServiceUserOption + " value '" + result.ServiceUser + "' is not a valid account name");
            }

            if (result.Action == RegistrationActionEnum.RunService && result.DryRun)
            {
                result.Errors.Add(DryRunFlag + " applies to the install and uninstall flags, not " + RunServiceFlag);
            }

            return result;
        }

        #endregion

        #region Private-Methods

        private static RegistrationActionEnum ActionForFlag(string arg)
        {
            if (String.Equals(arg, InstallServiceFlag, StringComparison.Ordinal)) return RegistrationActionEnum.InstallService;
            if (String.Equals(arg, UninstallServiceFlag, StringComparison.Ordinal)) return RegistrationActionEnum.UninstallService;
            if (String.Equals(arg, RunServiceFlag, StringComparison.Ordinal)) return RegistrationActionEnum.RunService;
            if (String.Equals(arg, InstallStartupFlag, StringComparison.Ordinal)) return RegistrationActionEnum.InstallStartup;
            if (String.Equals(arg, UninstallStartupFlag, StringComparison.Ordinal)) return RegistrationActionEnum.UninstallStartup;
            return RegistrationActionEnum.None;
        }

        private static bool IsSafeAccountName(string name)
        {
            if (name.Length == 0 || name.Length > 64) return false;
            foreach (char c in name)
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-' || c == '.';
                if (!ok) return false;
            }
            return true;
        }

        #endregion
    }
}

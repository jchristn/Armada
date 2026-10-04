namespace Armada.Core.Hosting
{
    /// <summary>
    /// Process exit codes returned by the registration flags (--install-service, --uninstall-service,
    /// --install-startup, --uninstall-startup). Installers treat any non-zero code as a failed step.
    /// </summary>
    public static class RegistrationExitCode
    {
        /// <summary>
        /// The action completed, or there was nothing to do (already installed, already removed, dry run).
        /// </summary>
        public const int Success = 0;

        /// <summary>
        /// A file could not be written or a service-manager command failed.
        /// </summary>
        public const int Failed = 1;

        /// <summary>
        /// The command line was invalid (for example two actions, or an unknown option value).
        /// </summary>
        public const int InvalidArguments = 2;

        /// <summary>
        /// The current operating system has no implementation for the requested action.
        /// </summary>
        public const int UnsupportedPlatform = 3;

        /// <summary>
        /// The action needs different privileges (for example a Windows service needs an elevated prompt).
        /// </summary>
        public const int InsufficientPrivileges = 4;
    }
}

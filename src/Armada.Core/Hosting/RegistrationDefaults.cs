namespace Armada.Core.Hosting
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Identity of the Admiral service and the Harbor login item. These values must match the "service" and "startup"
    /// blocks and bundle identifiers in <c>publisher.json</c>, because the installers built from it register and remove
    /// the same names (a test checks this).
    /// </summary>
    public static class RegistrationDefaults
    {
        #region Public-Members

        /// <summary>
        /// Service name: Windows service name and systemd unit base name.
        /// </summary>
        public const string AdmiralServiceName = "armada";

        /// <summary>
        /// Display name of the Admiral service.
        /// </summary>
        public const string AdmiralDisplayName = "Armada Admiral";

        /// <summary>
        /// launchd label of the Admiral agent (the server bundle identifier; the .pkg uses the same label).
        /// </summary>
        public const string AdmiralLabel = "com.joelchristner.armada.server";

        /// <summary>
        /// Description of the Admiral service.
        /// </summary>
        public const string AdmiralDescription = "Armada Admiral server: REST API, MCP server, WebSocket, and web dashboard.";

        /// <summary>
        /// Harbor login-item name: autostart file base name.
        /// </summary>
        public const string HarborName = "armada-harbor";

        /// <summary>
        /// Harbor display name, also the Windows Run value name.
        /// </summary>
        public const string HarborDisplayName = "Armada Harbor";

        /// <summary>
        /// launchd label of the Harbor login item (the Harbor bundle identifier).
        /// </summary>
        public const string HarborLabel = "com.joelchristner.armada.harbor";

        /// <summary>
        /// Description of the Harbor login item.
        /// </summary>
        public const string HarborDescription = "Host-side runner for Armada captains, git, and worktrees.";

        /// <summary>
        /// Argument Harbor's login item passes so Harbor starts in the tray without opening its window.
        /// </summary>
        public const string HarborMinimizedFlag = "--minimized";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Fill the Admiral's identity and run arguments into a context.
        /// </summary>
        /// <param name="context">Context to fill.</param>
        /// <returns>The same context.</returns>
        /// <exception cref="ArgumentNullException">context is null.</exception>
        public static RegistrationContext ApplyAdmiral(RegistrationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            context.Name = AdmiralServiceName;
            context.DisplayName = AdmiralDisplayName;
            context.Description = AdmiralDescription;
            context.Label = AdmiralLabel;
            context.RunArguments = new List<string> { RegistrationCommandLine.RunServiceFlag };
            return context;
        }

        /// <summary>
        /// Fill Harbor's identity and run arguments into a context.
        /// </summary>
        /// <param name="context">Context to fill.</param>
        /// <returns>The same context.</returns>
        /// <exception cref="ArgumentNullException">context is null.</exception>
        public static RegistrationContext ApplyHarbor(RegistrationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            context.Name = HarborName;
            context.DisplayName = HarborDisplayName;
            context.Description = HarborDescription;
            context.Label = HarborLabel;
            context.RunArguments = new List<string> { HarborMinimizedFlag };
            return context;
        }

        #endregion
    }
}

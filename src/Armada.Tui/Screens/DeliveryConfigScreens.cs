namespace Armada.Tui.Screens
{
    using System;
    using System.Linq;
    using System.Reflection;
    using Armada.Tui.Routing;

    /// <summary>
    /// Registers the DELIVERY (W5) and CONFIGURATION (W6) screens with the <see cref="ScreenFactory"/>.
    /// </summary>
    public static class DeliveryConfigScreens
    {
        #region Public-Members

        /// <summary>
        /// Screen names this module implements (the route table's screen names).
        /// </summary>
        public static readonly string[] ScreenNames = new[]
        {
            "DeploymentsScreen", "DeploymentScreen", "EnvironmentsScreen", "EnvironmentScreen", "ReleasesScreen", "ReleaseScreen",
            "IncidentsScreen", "IncidentScreen", "ChecksScreen", "CheckRunScreen", "RunbooksScreen", "RunbookScreen",
            "WorkflowProfilesScreen", "WorkflowProfileScreen", "ProjectProfilesScreen", "ProjectProfileScreen", "SkillsScreen", "SkillScreen",
            "PersonasScreen", "PersonaScreen", "PipelinesScreen", "PipelineScreen", "PromptTemplatesScreen", "PromptTemplateScreen",
            "PlaybooksScreen", "PlaybookScreen", "EndpointsScreen", "HarborsScreen", "MemoryScreen"
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register every implemented screen.
        /// </summary>
        /// <param name="factory">Factory.</param>
        public static void Register(ScreenFactory factory)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            Assembly assembly = typeof(DeliveryConfigScreens).Assembly;
            foreach (string name in ScreenNames)
            {
                Type? type = assembly.GetTypes().FirstOrDefault(t => t.Name == name && typeof(ScreenBase).IsAssignableFrom(t) && !t.IsAbstract);
                if (type == null) continue;
                ConstructorInfo? ctor = type.GetConstructor(new[] { typeof(RouteMatch), typeof(TuiContext) });
                if (ctor == null) continue;
                factory.Register(name, (match, context) => (ScreenBase)ctor.Invoke(new object[] { match, context }));
            }
        }

        #endregion
    }
}

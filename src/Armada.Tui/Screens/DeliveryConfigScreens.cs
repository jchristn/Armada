namespace Armada.Tui.Screens
{
    using System;

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
            "DeploymentsScreen", "DeploymentScreen", "EnvironmentsScreen", "EnvironmentScreen", "ReleasesScreen", "ReleaseScreen", "IncidentsScreen", "IncidentScreen", "ChecksScreen", "CheckRunScreen", "RunbooksScreen", "RunbookScreen", "WorkflowProfilesScreen", "WorkflowProfileScreen", "ProjectProfilesScreen", "ProjectProfileScreen", "SkillsScreen", "SkillScreen", "PersonasScreen", "PersonaScreen", "PipelinesScreen", "PipelineScreen", "PromptTemplatesScreen", "PromptTemplateScreen", "PlaybooksScreen", "PlaybookScreen", "EndpointsScreen", "HarborsScreen", "MemoryScreen"
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register every screen.
        /// </summary>
        /// <param name="factory">Factory.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="factory"/> is null.</exception>
        public static void Register(ScreenFactory factory)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            factory.Register("DeploymentsScreen", (m, c) => new Delivery.DeploymentsScreen(m, c));
            factory.Register("DeploymentScreen", (m, c) => new Delivery.DeploymentScreen(m, c));
            factory.Register("EnvironmentsScreen", (m, c) => new Delivery.EnvironmentsScreen(m, c));
            factory.Register("EnvironmentScreen", (m, c) => new Delivery.EnvironmentScreen(m, c));
            factory.Register("ReleasesScreen", (m, c) => new Delivery.ReleasesScreen(m, c));
            factory.Register("ReleaseScreen", (m, c) => new Delivery.ReleaseScreen(m, c));
            factory.Register("IncidentsScreen", (m, c) => new Delivery.IncidentsScreen(m, c));
            factory.Register("IncidentScreen", (m, c) => new Delivery.IncidentScreen(m, c));
            factory.Register("ChecksScreen", (m, c) => new Delivery.ChecksScreen(m, c));
            factory.Register("CheckRunScreen", (m, c) => new Delivery.CheckRunScreen(m, c));
            factory.Register("RunbooksScreen", (m, c) => new Delivery.RunbooksScreen(m, c));
            factory.Register("RunbookScreen", (m, c) => new Delivery.RunbookScreen(m, c));
            factory.Register("WorkflowProfilesScreen", (m, c) => new Configuration.WorkflowProfilesScreen(m, c));
            factory.Register("WorkflowProfileScreen", (m, c) => new Configuration.WorkflowProfileScreen(m, c));
            factory.Register("ProjectProfilesScreen", (m, c) => new Configuration.ProjectProfilesScreen(m, c));
            factory.Register("ProjectProfileScreen", (m, c) => new Configuration.ProjectProfileScreen(m, c));
            factory.Register("SkillsScreen", (m, c) => new Configuration.SkillsScreen(m, c));
            factory.Register("SkillScreen", (m, c) => new Configuration.SkillScreen(m, c));
            factory.Register("PersonasScreen", (m, c) => new Configuration.PersonasScreen(m, c));
            factory.Register("PersonaScreen", (m, c) => new Configuration.PersonaScreen(m, c));
            factory.Register("PipelinesScreen", (m, c) => new Configuration.PipelinesScreen(m, c));
            factory.Register("PipelineScreen", (m, c) => new Configuration.PipelineScreen(m, c));
            factory.Register("PromptTemplatesScreen", (m, c) => new Configuration.PromptTemplatesScreen(m, c));
            factory.Register("PromptTemplateScreen", (m, c) => new Configuration.PromptTemplateScreen(m, c));
            factory.Register("PlaybooksScreen", (m, c) => new Configuration.PlaybooksScreen(m, c));
            factory.Register("PlaybookScreen", (m, c) => new Configuration.PlaybookScreen(m, c));
            factory.Register("EndpointsScreen", (m, c) => new Configuration.EndpointsScreen(m, c));
            factory.Register("HarborsScreen", (m, c) => new Configuration.HarborsScreen(m, c));
            factory.Register("MemoryScreen", (m, c) => new Configuration.MemoryScreen(m, c));
        }

        #endregion
    }
}

namespace Armada.Tui.Screens.Configuration
{
    using System.Collections.Generic;

    /// <summary>
    /// A placeholder offered by the prompt template parameter palette (dashboard <c>PARAMETER_GROUPS</c> in
    /// <c>PromptTemplateDetail.tsx</c>).
    /// </summary>
    public class PromptParameter
    {
        #region Public-Members

        /// <summary>
        /// English group label ("Mission Context", "Vessel Context", "Captain Context", "Pipeline Context", "System").
        /// </summary>
        public string Group { get; }

        /// <summary>
        /// Placeholder text inserted into the template, for example <c>{MissionId}</c>.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// English description.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// Every parameter in palette order.
        /// </summary>
        public static IReadOnlyList<PromptParameter> All { get; } = new List<PromptParameter>
        {
            new PromptParameter("Mission Context", "{MissionId}", "Mission identifier"),
            new PromptParameter("Mission Context", "{MissionTitle}", "Mission title"),
            new PromptParameter("Mission Context", "{MissionDescription}", "Full mission description"),
            new PromptParameter("Mission Context", "{MissionPersona}", "Persona assigned to this mission"),
            new PromptParameter("Mission Context", "{VoyageId}", "Parent voyage identifier"),
            new PromptParameter("Mission Context", "{BranchName}", "Git branch for this mission"),
            new PromptParameter("Vessel Context", "{VesselId}", "Vessel identifier"),
            new PromptParameter("Vessel Context", "{VesselName}", "Vessel display name"),
            new PromptParameter("Vessel Context", "{DefaultBranch}", "Default branch (e.g. main)"),
            new PromptParameter("Vessel Context", "{ProjectContext}", "User-supplied project description"),
            new PromptParameter("Vessel Context", "{StyleGuide}", "User-supplied style guide"),
            new PromptParameter("Vessel Context", "{ModelContext}", "Agent-accumulated context"),
            new PromptParameter("Vessel Context", "{FleetId}", "Parent fleet identifier"),
            new PromptParameter("Captain Context", "{CaptainId}", "Captain identifier"),
            new PromptParameter("Captain Context", "{CaptainName}", "Captain display name"),
            new PromptParameter("Captain Context", "{CaptainInstructions}", "User-supplied captain instructions"),
            new PromptParameter("Pipeline Context", "{PersonaPrompt}", "Resolved persona prompt text"),
            new PromptParameter("Pipeline Context", "{PreviousStageDiff}", "Diff from prior pipeline stage"),
            new PromptParameter("Pipeline Context", "{ExistingClaudeMd}", "Contents of repo's existing CLAUDE.md"),
            new PromptParameter("System", "{Timestamp}", "Current UTC timestamp")
        };

        /// <summary>
        /// Template categories offered by the dashboard (filter pills and the create form).
        /// </summary>
        public static IReadOnlyList<string> Categories { get; } = new List<string> { "mission", "persona", "structure", "commit", "landing", "agent", "import" };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="group">English group label.</param>
        /// <param name="name">Placeholder.</param>
        /// <param name="description">English description.</param>
        public PromptParameter(string group, string name, string description)
        {
            Group = group ?? "";
            Name = name ?? "";
            Description = description ?? "";
        }

        #endregion
    }
}

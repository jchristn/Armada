namespace Armada.Core.Database
{
    using Armada.Core.Database.Interfaces;

    /// <summary>
    /// Abstract database driver providing access to all entity methods.
    /// </summary>
    public abstract class DatabaseDriver : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Fleet operations.
        /// </summary>
        public IFleetMethods Fleets { get; protected set; } = null!;

        /// <summary>
        /// Vessel operations.
        /// </summary>
        public IVesselMethods Vessels { get; protected set; } = null!;

        /// <summary>
        /// Captain operations.
        /// </summary>
        public ICaptainMethods Captains { get; protected set; } = null!;

        /// <summary>
        /// Mission operations.
        /// </summary>
        public IMissionMethods Missions { get; protected set; } = null!;

        /// <summary>
        /// Voyage operations.
        /// </summary>
        public IVoyageMethods Voyages { get; protected set; } = null!;

        /// <summary>
        /// Planning session operations.
        /// </summary>
        public IPlanningSessionMethods PlanningSessions { get; protected set; } = null!;

        /// <summary>
        /// Planning session message operations.
        /// </summary>
        public IPlanningSessionMessageMethods PlanningSessionMessages { get; protected set; } = null!;

        /// <summary>
        /// Background job operations.
        /// </summary>
        public IJobMethods Jobs { get; protected set; } = null!;

        /// <summary>
        /// Objective/backlog operations.
        /// </summary>
        public IObjectiveMethods Objectives { get; protected set; } = null!;

        /// <summary>
        /// Objective refinement session operations.
        /// </summary>
        public IObjectiveRefinementSessionMethods ObjectiveRefinementSessions { get; protected set; } = null!;

        /// <summary>
        /// Objective refinement transcript message operations.
        /// </summary>
        public IObjectiveRefinementMessageMethods ObjectiveRefinementMessages { get; protected set; } = null!;

        /// <summary>
        /// Dock operations.
        /// </summary>
        public IDockMethods Docks { get; protected set; } = null!;

        /// <summary>
        /// Signal operations.
        /// </summary>
        public ISignalMethods Signals { get; protected set; } = null!;

        /// <summary>
        /// Event operations.
        /// </summary>
        public IEventMethods Events { get; protected set; } = null!;

        /// <summary>
        /// Request-history operations.
        /// </summary>
        public IRequestHistoryMethods RequestHistory { get; protected set; } = null!;

        /// <summary>
        /// Merge entry operations.
        /// </summary>
        public IMergeEntryMethods MergeEntries { get; protected set; } = null!;

        /// <summary>
        /// Token-usage operations.
        /// </summary>
        public ITokenUsageMethods TokenUsage { get; protected set; } = null!;

        /// <summary>
        /// Tenant operations.
        /// </summary>
        public ITenantMethods Tenants { get; protected set; } = null!;

        /// <summary>
        /// User operations.
        /// </summary>
        public IUserMethods Users { get; protected set; } = null!;

        /// <summary>
        /// Credential operations.
        /// </summary>
        public ICredentialMethods Credentials { get; protected set; } = null!;

        /// <summary>
        /// Prompt template operations.
        /// </summary>
        public IPromptTemplateMethods PromptTemplates { get; protected set; } = null!;

        /// <summary>
        /// Persona operations.
        /// </summary>
        public IPersonaMethods Personas { get; protected set; } = null!;

        /// <summary>
        /// Pipeline operations.
        /// </summary>
        public IPipelineMethods Pipelines { get; protected set; } = null!;

        /// <summary>
        /// Playbook operations.
        /// </summary>
        public IPlaybookMethods Playbooks { get; protected set; } = null!;

        /// <summary>
        /// Workflow-profile operations.
        /// </summary>
        public IWorkflowProfileMethods WorkflowProfiles { get; protected set; } = null!;

        /// <summary>
        /// Project-profile operations.
        /// </summary>
        public IProjectProfileMethods ProjectProfiles { get; protected set; } = null!;

        /// <summary>
        /// Skill operations.
        /// </summary>
        public ISkillMethods Skills { get; protected set; } = null!;

        /// <summary>
        /// Deployment environment operations.
        /// </summary>
        public IDeploymentEnvironmentMethods Environments { get; protected set; } = null!;

        /// <summary>
        /// Structured check-run operations.
        /// </summary>
        public ICheckRunMethods CheckRuns { get; protected set; } = null!;

        /// <summary>
        /// Release operations.
        /// </summary>
        public IReleaseMethods Releases { get; protected set; } = null!;

        /// <summary>
        /// Deployment operations.
        /// </summary>
        public IDeploymentMethods Deployments { get; protected set; } = null!;

        /// <summary>
        /// Durable coordination lease operations (restart-safe, multi-instance-safe mutual exclusion).
        /// </summary>
        public ICoordinationLeaseMethods CoordinationLeases { get; protected set; } = null!;

        /// <summary>
        /// Managed model endpoint (embedding/inference) operations.
        /// </summary>
        public IModelEndpointMethods ModelEndpoints { get; protected set; } = null!;

        /// <summary>
        /// Harbor (host runner) operations.
        /// </summary>
        public IHarborMethods Harbors { get; protected set; } = null!;

        /// <summary>
        /// Durable agent memory operations.
        /// </summary>
        public IMemoryMethods Memories { get; protected set; } = null!;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public DatabaseDriver()
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Initialize the database schema and seed data.
        /// </summary>
        public abstract Task InitializeAsync(CancellationToken token = default);

        /// <summary>
        /// Get the current schema version (the highest applied migration), or 0 when the database has not
        /// been migrated yet.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The current schema version.</returns>
        public abstract Task<int> GetSchemaVersionAsync(CancellationToken token = default);

        /// <summary>
        /// Dispose.
        /// </summary>
        public abstract void Dispose();

        #endregion
    }
}

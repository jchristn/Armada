namespace Armada.Core.Database
{
    using Armada.Core.Database.Interfaces;

    /// <summary>
    /// Abstract database driver providing access to all entity methods. Every entity accessor is wrapped in a
    /// <see cref="DuplicateEntityTranslationProxy"/> when it is assigned, so a provider unique-constraint violation
    /// from any call surfaces as a typed <see cref="Armada.Core.Services.DuplicateEntityException"/>.
    /// </summary>
    public abstract class DatabaseDriver : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Fleet operations.
        /// </summary>
        public IFleetMethods Fleets
        {
            get => _Fleets;
            protected set => _Fleets = DuplicateEntityTranslationProxy.Wrap(value);
        }

        /// <summary>
        /// Vessel operations.
        /// </summary>
        public IVesselMethods Vessels
        {
            get => _Vessels;
            protected set => _Vessels = DuplicateEntityTranslationProxy.Wrap(value);
        }

        /// <summary>
        /// Captain operations.
        /// </summary>
        public ICaptainMethods Captains
        {
            get => _Captains;
            protected set => _Captains = DuplicateEntityTranslationProxy.Wrap(value);
        }

        /// <summary>
        /// Mission operations.
        /// </summary>
        public IMissionMethods Missions
        {
            get => _Missions;
            protected set => _Missions = DuplicateEntityTranslationProxy.Wrap(value);
        }

        /// <summary>
        /// Voyage operations.
        /// </summary>
        public IVoyageMethods Voyages
        {
            get => _Voyages;
            protected set => _Voyages = DuplicateEntityTranslationProxy.Wrap(value);
        }

        /// <summary>
        /// Planning session operations.
        /// </summary>
        public IPlanningSessionMethods PlanningSessions
        {
            get => _PlanningSessions;
            protected set => _PlanningSessions = DuplicateEntityTranslationProxy.Wrap(value);
        }

        /// <summary>
        /// Planning session message operations.
        /// </summary>
        public IPlanningSessionMessageMethods PlanningSessionMessages
        {
            get => _PlanningSessionMessages;
            protected set => _PlanningSessionMessages = DuplicateEntityTranslationProxy.Wrap(value);
        }

        /// <summary>
        /// Background job operations.
        /// </summary>
        public IJobMethods Jobs
        {
            get => _Jobs;
            protected set => _Jobs = DuplicateEntityTranslationProxy.Wrap(value);
        }

        /// <summary>
        /// Objective/backlog operations.
        /// </summary>
        public IObjectiveMethods Objectives
        {
            get => _Objectives;
            protected set => _Objectives = DuplicateEntityTranslationProxy.Wrap(value);
        }

        /// <summary>
        /// Objective refinement session operations.
        /// </summary>
        public IObjectiveRefinementSessionMethods ObjectiveRefinementSessions
        {
            get => _ObjectiveRefinementSessions;
            protected set => _ObjectiveRefinementSessions = DuplicateEntityTranslationProxy.Wrap(value);
        }

        /// <summary>
        /// Objective refinement transcript message operations.
        /// </summary>
        public IObjectiveRefinementMessageMethods ObjectiveRefinementMessages
        {
            get => _ObjectiveRefinementMessages;
            protected set => _ObjectiveRefinementMessages = DuplicateEntityTranslationProxy.Wrap(value);
        }

        /// <summary>
        /// Dock operations.
        /// </summary>
        public IDockMethods Docks
        {
            get => _Docks;
            protected set => _Docks = DuplicateEntityTranslationProxy.Wrap(value);
        }

        /// <summary>
        /// Signal operations.
        /// </summary>
        public ISignalMethods Signals
        {
            get => _Signals;
            protected set => _Signals = DuplicateEntityTranslationProxy.Wrap(value);
        }

        /// <summary>
        /// Event operations.
        /// </summary>
        public IEventMethods Events
        {
            get => _Events;
            protected set => _Events = DuplicateEntityTranslationProxy.Wrap(value);
        }

        /// <summary>
        /// Request-history operations.
        /// </summary>
        public IRequestHistoryMethods RequestHistory
        {
            get => _RequestHistory;
            protected set => _RequestHistory = DuplicateEntityTranslationProxy.Wrap(value);
        }

        /// <summary>
        /// Merge entry operations.
        /// </summary>
        public IMergeEntryMethods MergeEntries
        {
            get => _MergeEntries;
            protected set => _MergeEntries = DuplicateEntityTranslationProxy.Wrap(value);
        }

        /// <summary>
        /// Token-usage operations.
        /// </summary>
        public ITokenUsageMethods TokenUsage
        {
            get => _TokenUsage;
            protected set => _TokenUsage = DuplicateEntityTranslationProxy.Wrap(value);
        }

        /// <summary>
        /// Tenant operations.
        /// </summary>
        public ITenantMethods Tenants
        {
            get => _Tenants;
            protected set => _Tenants = DuplicateEntityTranslationProxy.Wrap(value);
        }

        /// <summary>
        /// User operations.
        /// </summary>
        public IUserMethods Users
        {
            get => _Users;
            protected set => _Users = DuplicateEntityTranslationProxy.Wrap(value);
        }

        /// <summary>
        /// Credential operations.
        /// </summary>
        public ICredentialMethods Credentials
        {
            get => _Credentials;
            protected set => _Credentials = DuplicateEntityTranslationProxy.Wrap(value);
        }

        /// <summary>
        /// Prompt template operations.
        /// </summary>
        public IPromptTemplateMethods PromptTemplates
        {
            get => _PromptTemplates;
            protected set => _PromptTemplates = DuplicateEntityTranslationProxy.Wrap(value);
        }

        /// <summary>
        /// Persona operations.
        /// </summary>
        public IPersonaMethods Personas
        {
            get => _Personas;
            protected set => _Personas = DuplicateEntityTranslationProxy.Wrap(value);
        }

        /// <summary>
        /// Pipeline operations.
        /// </summary>
        public IPipelineMethods Pipelines
        {
            get => _Pipelines;
            protected set => _Pipelines = DuplicateEntityTranslationProxy.Wrap(value);
        }

        /// <summary>
        /// Playbook operations.
        /// </summary>
        public IPlaybookMethods Playbooks
        {
            get => _Playbooks;
            protected set => _Playbooks = DuplicateEntityTranslationProxy.Wrap(value);
        }

        /// <summary>
        /// Workflow-profile operations.
        /// </summary>
        public IWorkflowProfileMethods WorkflowProfiles
        {
            get => _WorkflowProfiles;
            protected set => _WorkflowProfiles = DuplicateEntityTranslationProxy.Wrap(value);
        }

        /// <summary>
        /// Project-profile operations.
        /// </summary>
        public IProjectProfileMethods ProjectProfiles
        {
            get => _ProjectProfiles;
            protected set => _ProjectProfiles = DuplicateEntityTranslationProxy.Wrap(value);
        }

        /// <summary>
        /// Skill operations.
        /// </summary>
        public ISkillMethods Skills
        {
            get => _Skills;
            protected set => _Skills = DuplicateEntityTranslationProxy.Wrap(value);
        }

        /// <summary>
        /// Deployment environment operations.
        /// </summary>
        public IDeploymentEnvironmentMethods Environments
        {
            get => _Environments;
            protected set => _Environments = DuplicateEntityTranslationProxy.Wrap(value);
        }

        /// <summary>
        /// Structured check-run operations.
        /// </summary>
        public ICheckRunMethods CheckRuns
        {
            get => _CheckRuns;
            protected set => _CheckRuns = DuplicateEntityTranslationProxy.Wrap(value);
        }

        /// <summary>
        /// Release operations.
        /// </summary>
        public IReleaseMethods Releases
        {
            get => _Releases;
            protected set => _Releases = DuplicateEntityTranslationProxy.Wrap(value);
        }

        /// <summary>
        /// Deployment operations.
        /// </summary>
        public IDeploymentMethods Deployments
        {
            get => _Deployments;
            protected set => _Deployments = DuplicateEntityTranslationProxy.Wrap(value);
        }

        /// <summary>
        /// Durable coordination lease operations (restart-safe, multi-instance-safe mutual exclusion).
        /// </summary>
        public ICoordinationLeaseMethods CoordinationLeases
        {
            get => _CoordinationLeases;
            protected set => _CoordinationLeases = DuplicateEntityTranslationProxy.Wrap(value);
        }

        /// <summary>
        /// Managed model endpoint (embedding/inference) operations.
        /// </summary>
        public IModelEndpointMethods ModelEndpoints
        {
            get => _ModelEndpoints;
            protected set => _ModelEndpoints = DuplicateEntityTranslationProxy.Wrap(value);
        }

        /// <summary>
        /// Harbor (host runner) operations.
        /// </summary>
        public IHarborMethods Harbors
        {
            get => _Harbors;
            protected set => _Harbors = DuplicateEntityTranslationProxy.Wrap(value);
        }

        /// <summary>
        /// Durable agent memory operations.
        /// </summary>
        public IMemoryMethods Memories
        {
            get => _Memories;
            protected set => _Memories = DuplicateEntityTranslationProxy.Wrap(value);
        }

        /// <summary>
        /// Vessel import batch operations.
        /// </summary>
        public IVesselImportBatchMethods VesselImportBatches
        {
            get => _VesselImportBatches;
            protected set => _VesselImportBatches = DuplicateEntityTranslationProxy.Wrap(value);
        }

        /// <summary>
        /// Vessel import item (candidate) operations.
        /// </summary>
        public IVesselImportItemMethods VesselImportItems
        {
            get => _VesselImportItems;
            protected set => _VesselImportItems = DuplicateEntityTranslationProxy.Wrap(value);
        }

        /// <summary>
        /// Vessel import fleet recommendation methods.
        /// </summary>
        public IVesselImportFleetRecommendationMethods VesselImportFleetRecommendations
        {
            get => _VesselImportFleetRecommendations;
            protected set => _VesselImportFleetRecommendations = DuplicateEntityTranslationProxy.Wrap(value);
        }

        /// <summary>
        /// Ask Armada conversation thread methods.
        /// </summary>
        public IAskThreadMethods AskThreads
        {
            get => _AskThreads;
            protected set => _AskThreads = DuplicateEntityTranslationProxy.Wrap(value);
        }

        /// <summary>
        /// Ask Armada thread message methods.
        /// </summary>
        public IAskMessageMethods AskMessages
        {
            get => _AskMessages;
            protected set => _AskMessages = DuplicateEntityTranslationProxy.Wrap(value);
        }

        /// <summary>
        /// Ask Armada message tool-call methods.
        /// </summary>
        public IAskMessageToolCallMethods AskMessageToolCalls
        {
            get => _AskMessageToolCalls;
            protected set => _AskMessageToolCalls = DuplicateEntityTranslationProxy.Wrap(value);
        }

        /// <summary>
        /// Ask Armada action proposal methods.
        /// </summary>
        public IAskActionProposalMethods AskActionProposals
        {
            get => _AskActionProposals;
            protected set => _AskActionProposals = DuplicateEntityTranslationProxy.Wrap(value);
        }

        /// <summary>
        /// Ask Armada tracked-work methods.
        /// </summary>
        public IAskTrackedWorkMethods AskTrackedWork
        {
            get => _AskTrackedWork;
            protected set => _AskTrackedWork = DuplicateEntityTranslationProxy.Wrap(value);
        }

        /// <summary>
        /// CLI permission request methods.
        /// </summary>
        public ICliPermissionRequestMethods CliPermissionRequests
        {
            get => _CliPermissionRequests;
            protected set => _CliPermissionRequests = DuplicateEntityTranslationProxy.Wrap(value);
        }

        /// <summary>
        /// CLI permission rule methods.
        /// </summary>
        public ICliPermissionRuleMethods CliPermissionRules
        {
            get => _CliPermissionRules;
            protected set => _CliPermissionRules = DuplicateEntityTranslationProxy.Wrap(value);
        }

        /// <summary>
        /// Push notification device methods.
        /// </summary>
        public IPushDeviceMethods PushDevices
        {
            get => _PushDevices;
            protected set => _PushDevices = DuplicateEntityTranslationProxy.Wrap(value);
        }

        /// <summary>
        /// Harbor job record methods (captain launches delegated to a Harbor).
        /// </summary>
        public IHarborJobMethods HarborJobs
        {
            get => _HarborJobs;
            protected set => _HarborJobs = DuplicateEntityTranslationProxy.Wrap(value);
        }

        /// <summary>
        /// Harbor link-health sample methods.
        /// </summary>
        public IHarborLinkSampleMethods HarborLinkSamples
        {
            get => _HarborLinkSamples;
            protected set => _HarborLinkSamples = DuplicateEntityTranslationProxy.Wrap(value);
        }

        /// <summary>
        /// Harbor link event methods.
        /// </summary>
        public IHarborLinkEventMethods HarborLinkEvents
        {
            get => _HarborLinkEvents;
            protected set => _HarborLinkEvents = DuplicateEntityTranslationProxy.Wrap(value);
        }

        /// <summary>
        /// Fleet action definition operations.
        /// </summary>
        public IFleetActionMethods FleetActions
        {
            get => _FleetActions;
            protected set => _FleetActions = DuplicateEntityTranslationProxy.Wrap(value);
        }

        /// <summary>
        /// Fleet action run operations.
        /// </summary>
        public IFleetActionRunMethods FleetActionRuns
        {
            get => _FleetActionRuns;
            protected set => _FleetActionRuns = DuplicateEntityTranslationProxy.Wrap(value);
        }

        /// <summary>
        /// Fleet action run target operations.
        /// </summary>
        public IFleetActionRunTargetMethods FleetActionRunTargets
        {
            get => _FleetActionRunTargets;
            protected set => _FleetActionRunTargets = DuplicateEntityTranslationProxy.Wrap(value);
        }

        /// <summary>
        /// Vessel health row operations.
        /// </summary>
        public IVesselHealthMethods VesselHealth
        {
            get => _VesselHealth;
            protected set => _VesselHealth = DuplicateEntityTranslationProxy.Wrap(value);
        }

        /// <summary>
        /// Vessel health finding operations.
        /// </summary>
        public IVesselHealthFindingMethods VesselHealthFindings
        {
            get => _VesselHealthFindings;
            protected set => _VesselHealthFindings = DuplicateEntityTranslationProxy.Wrap(value);
        }

        /// <summary>
        /// Vessel dependency operations.
        /// </summary>
        public IVesselDependencyMethods VesselDependencies
        {
            get => _VesselDependencies;
            protected set => _VesselDependencies = DuplicateEntityTranslationProxy.Wrap(value);
        }

        /// <summary>
        /// Vessel health override operations.
        /// </summary>
        public IVesselHealthOverrideMethods VesselHealthOverrides
        {
            get => _VesselHealthOverrides;
            protected set => _VesselHealthOverrides = DuplicateEntityTranslationProxy.Wrap(value);
        }

        #endregion

        #region Private-Members

        private IFleetMethods _Fleets = null!;
        private IVesselMethods _Vessels = null!;
        private ICaptainMethods _Captains = null!;
        private IMissionMethods _Missions = null!;
        private IVoyageMethods _Voyages = null!;
        private IPlanningSessionMethods _PlanningSessions = null!;
        private IPlanningSessionMessageMethods _PlanningSessionMessages = null!;
        private IJobMethods _Jobs = null!;
        private IObjectiveMethods _Objectives = null!;
        private IObjectiveRefinementSessionMethods _ObjectiveRefinementSessions = null!;
        private IObjectiveRefinementMessageMethods _ObjectiveRefinementMessages = null!;
        private IDockMethods _Docks = null!;
        private ISignalMethods _Signals = null!;
        private IEventMethods _Events = null!;
        private IRequestHistoryMethods _RequestHistory = null!;
        private IMergeEntryMethods _MergeEntries = null!;
        private ITokenUsageMethods _TokenUsage = null!;
        private ITenantMethods _Tenants = null!;
        private IUserMethods _Users = null!;
        private ICredentialMethods _Credentials = null!;
        private IPromptTemplateMethods _PromptTemplates = null!;
        private IPersonaMethods _Personas = null!;
        private IPipelineMethods _Pipelines = null!;
        private IPlaybookMethods _Playbooks = null!;
        private IWorkflowProfileMethods _WorkflowProfiles = null!;
        private IProjectProfileMethods _ProjectProfiles = null!;
        private ISkillMethods _Skills = null!;
        private IDeploymentEnvironmentMethods _Environments = null!;
        private ICheckRunMethods _CheckRuns = null!;
        private IReleaseMethods _Releases = null!;
        private IDeploymentMethods _Deployments = null!;
        private ICoordinationLeaseMethods _CoordinationLeases = null!;
        private IModelEndpointMethods _ModelEndpoints = null!;
        private IHarborMethods _Harbors = null!;
        private IMemoryMethods _Memories = null!;
        private IVesselImportBatchMethods _VesselImportBatches = null!;
        private IVesselImportItemMethods _VesselImportItems = null!;
        private IVesselImportFleetRecommendationMethods _VesselImportFleetRecommendations = null!;
        private IAskThreadMethods _AskThreads = null!;
        private IAskMessageMethods _AskMessages = null!;
        private IAskMessageToolCallMethods _AskMessageToolCalls = null!;
        private IAskActionProposalMethods _AskActionProposals = null!;
        private IAskTrackedWorkMethods _AskTrackedWork = null!;
        private ICliPermissionRequestMethods _CliPermissionRequests = null!;
        private ICliPermissionRuleMethods _CliPermissionRules = null!;
        private IPushDeviceMethods _PushDevices = null!;
        private IHarborJobMethods _HarborJobs = null!;
        private IHarborLinkSampleMethods _HarborLinkSamples = null!;
        private IHarborLinkEventMethods _HarborLinkEvents = null!;
        private IFleetActionMethods _FleetActions = null!;
        private IFleetActionRunMethods _FleetActionRuns = null!;
        private IFleetActionRunTargetMethods _FleetActionRunTargets = null!;
        private IVesselHealthMethods _VesselHealth = null!;
        private IVesselHealthFindingMethods _VesselHealthFindings = null!;
        private IVesselDependencyMethods _VesselDependencies = null!;
        private IVesselHealthOverrideMethods _VesselHealthOverrides = null!;

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
        /// Get the highest schema version this build defines for the provider (the version
        /// <see cref="InitializeAsync"/> migrates to).
        /// </summary>
        /// <returns>The latest known schema version.</returns>
        public abstract int GetLatestSchemaVersion();

        /// <summary>
        /// Get the number of migrations <see cref="InitializeAsync"/> would apply: the count of defined
        /// migrations newer than the database's current schema version. Zero when the schema is current.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The number of pending migrations.</returns>
        public async Task<int> GetPendingMigrationCountAsync(CancellationToken token = default)
        {
            int current = await GetSchemaVersionAsync(token).ConfigureAwait(false);
            int count = 0;
            foreach (SchemaMigration migration in GetMigrationsForVerification())
            {
                if (migration.Version > current) count++;
            }

            return count;
        }

        /// <summary>
        /// Dispose.
        /// </summary>
        public abstract void Dispose();

        #endregion

        #region Internal-Methods

        /// <summary>
        /// The ordered migrations this driver applies, exposed so tests can verify them.
        /// </summary>
        /// <returns>Ordered migration list.</returns>
        internal abstract IReadOnlyList<SchemaMigration> GetMigrationsForVerification();

        /// <summary>
        /// Re-execute the statements of every defined migration, in order, through the same per-statement path
        /// <see cref="InitializeAsync"/> uses, without recording anything in schema_migrations. Used to verify
        /// that every migration is idempotent (safe to re-run against a schema that already contains it).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        internal abstract Task ReplayMigrationsAsync(CancellationToken token = default);

        #endregion
    }
}

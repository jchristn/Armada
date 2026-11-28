namespace Armada.Core.Services
{
    using System.Text;
    using System.Text.Json;
    using Armada.Core;
    using Armada.Core.BlackBox;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// What the mission services share: the dependencies and state objects the <see cref="MissionService"/> facade
    /// creates once, and each service, so one service can call another. Built by the facade's constructor.
    /// </summary>
    internal sealed class MissionServiceContext
    {
        /// <summary>Shared AdmissionResults.</summary>
        internal MissionAdmissionResultStore AdmissionResults { get; set; } = default!;

        /// <summary>Shared BackgroundLaunches.</summary>
        internal BackgroundLaunchSet BackgroundLaunches { get; set; } = default!;

        /// <summary>Shared CapacityLock.</summary>
        internal SemaphoreSlim CapacityLock { get; set; } = default!;

        /// <summary>Shared Captains.</summary>
        internal ICaptainService Captains { get; set; } = default!;

        /// <summary>Shared CompletionRuns.</summary>
        internal CompletionRunTracker CompletionRuns { get; set; } = default!;

        /// <summary>Shared Database.</summary>
        internal DatabaseDriver Database { get; set; } = default!;

        /// <summary>Shared DefinitionOfDone.</summary>
        internal IDefinitionOfDoneGate? DefinitionOfDone { get; set; } = default!;

        /// <summary>Shared Docks.</summary>
        internal IDockService Docks { get; set; } = default!;

        /// <summary>Shared EndpointBreakers.</summary>
        internal EndpointCircuitBreakers EndpointBreakers { get; set; } = default!;

        /// <summary>Shared Git.</summary>
        internal IGitService? Git { get; set; } = default!;

        /// <summary>Shared Header.</summary>
        internal string Header { get; set; } = default!;

        /// <summary>Shared LaunchBackoff.</summary>
        internal MissionLaunchBackoff LaunchBackoff { get; set; } = default!;

        /// <summary>Shared LeftoverCommitted.</summary>
        internal MissionFlagSet LeftoverCommitted { get; set; } = default!;

        /// <summary>Shared LocalCommands.</summary>
        internal IHostCommandExecutor LocalCommands { get; set; } = default!;

        /// <summary>Shared LocalFiles.</summary>
        internal IDockFileSystem LocalFiles { get; set; } = default!;

        /// <summary>Shared Logging.</summary>
        internal LoggingModule Logging { get; set; } = default!;

        /// <summary>Shared Pass.</summary>
        internal AsyncLocal<DispatchPassCache?> Pass { get; set; } = default!;

        /// <summary>Shared PromptTemplates.</summary>
        internal IPromptTemplateService? PromptTemplates { get; set; } = default!;

        /// <summary>Shared Settings.</summary>
        internal ArmadaSettings Settings { get; set; } = default!;

        /// <summary>Shared UsageSelection.</summary>
        internal UsageSelectionBlockerStore UsageSelection { get; set; } = default!;

        /// <summary>Shared VesselAdmissionLocks.</summary>
        internal VesselAdmissionLockRegistry VesselAdmissionLocks { get; set; } = default!;

        /// <summary>Shared WaitTracker.</summary>
        internal MissionWaitTracker WaitTracker { get; set; } = default!;

        /// <summary>The ArchitectFanOutService.</summary>
        internal ArchitectFanOutService ArchitectFanOutService { get; set; } = null!;

        /// <summary>The BranchDurabilityService.</summary>
        internal BranchDurabilityService BranchDurabilityService { get; set; } = null!;

        /// <summary>The IntegrationBarrierService.</summary>
        internal IntegrationBarrierService IntegrationBarrierService { get; set; } = null!;

        /// <summary>The MissionAdmissionPipeline.</summary>
        internal MissionAdmissionPipeline MissionAdmissionPipeline { get; set; } = null!;

        /// <summary>The MissionCaptainRouter.</summary>
        internal MissionCaptainRouter MissionCaptainRouter { get; set; } = null!;

        /// <summary>The MissionDispatchPass.</summary>
        internal MissionDispatchPass MissionDispatchPass { get; set; } = null!;

        /// <summary>The MissionEscalationService.</summary>
        internal MissionEscalationService MissionEscalationService { get; set; } = null!;

        /// <summary>The MissionFailureCapture.</summary>
        internal MissionFailureCapture MissionFailureCapture { get; set; } = null!;

        /// <summary>The MissionHandoffService.</summary>
        internal MissionHandoffService MissionHandoffService { get; set; } = null!;

        /// <summary>The MissionHierarchyService.</summary>
        internal MissionHierarchyService MissionHierarchyService { get; set; } = null!;

        /// <summary>The MissionInstructionWriter.</summary>
        internal MissionInstructionWriter MissionInstructionWriter { get; set; } = null!;

        /// <summary>The MissionLauncher.</summary>
        internal MissionLauncher MissionLauncher { get; set; } = null!;

        /// <summary>The MissionOutcomeTelemetry.</summary>
        internal MissionOutcomeTelemetry MissionOutcomeTelemetry { get; set; } = null!;

        /// <summary>The MissionOwnershipService.</summary>
        internal MissionOwnershipService MissionOwnershipService { get; set; } = null!;

        /// <summary>The MissionReviewService.</summary>
        internal MissionReviewService MissionReviewService { get; set; } = null!;

        /// <summary>The MissionReworkService.</summary>
        internal MissionReworkService MissionReworkService { get; set; } = null!;

        /// <summary>The MissionScopeGuard.</summary>
        internal MissionScopeGuard MissionScopeGuard { get; set; } = null!;

        /// <summary>The MissionUsageSelector.</summary>
        internal MissionUsageSelector MissionUsageSelector { get; set; } = null!;

        /// <summary>The MissionWaitCoordinator.</summary>
        internal MissionWaitCoordinator MissionWaitCoordinator { get; set; } = null!;

        /// <summary>The OutputRepairService.</summary>
        internal OutputRepairService OutputRepairService { get; set; } = null!;

        /// <summary>The StageCheckRunner.</summary>
        internal StageCheckRunner StageCheckRunner { get; set; } = null!;

        /// <summary>The StageCompletionHandler.</summary>
        internal StageCompletionHandler StageCompletionHandler { get; set; } = null!;

        /// <summary>The WorkerEnvelopeService.</summary>
        internal WorkerEnvelopeService WorkerEnvelopeService { get; set; } = null!;
    }
}

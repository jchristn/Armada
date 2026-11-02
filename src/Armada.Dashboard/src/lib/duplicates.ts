import type {
  Captain,
  Mission,
  DeploymentEnvironment,
  DeploymentEnvironmentUpsertRequest,
  Fleet,
  Objective,
  ObjectiveUpsertRequest,
  Persona,
  Pipeline,
  Playbook,
  PromptTemplate,
  Runbook,
  RunbookParameter,
  RunbookStep,
  RunbookUpsertRequest,
  Vessel,
  WorkflowEnvironmentProfile,
  WorkflowInputReference,
  WorkflowProfile,
} from '../types/models';

export function duplicateDisplayName(name: string): string {
  const trimmed = name.trim();
  return trimmed ? `${trimmed} (Copy)` : 'Copy';
}

export function duplicateFileName(fileName: string): string {
  const trimmed = fileName.trim();
  if (!trimmed) return 'Copy';

  const dotIndex = trimmed.lastIndexOf('.');
  if (dotIndex > 0) {
    return `${trimmed.slice(0, dotIndex)} (Copy)${trimmed.slice(dotIndex)}`;
  }

  return `${trimmed} (Copy)`;
}

function createNestedId(prefix: string): string {
  return `${prefix}_${Date.now()}_${Math.random().toString(36).slice(2, 8)}`;
}

function cloneWorkflowInputReference(input: WorkflowInputReference): WorkflowInputReference {
  return {
    provider: input.provider,
    key: input.key,
    environmentName: input.environmentName ?? null,
    description: input.description ?? null,
  };
}

function cloneWorkflowEnvironmentProfile(environment: WorkflowEnvironmentProfile): WorkflowEnvironmentProfile {
  return {
    environmentName: environment.environmentName,
    deployCommand: environment.deployCommand ?? null,
    rollbackCommand: environment.rollbackCommand ?? null,
    smokeTestCommand: environment.smokeTestCommand ?? null,
    healthCheckCommand: environment.healthCheckCommand ?? null,
    deploymentVerificationCommand: environment.deploymentVerificationCommand ?? null,
    rollbackVerificationCommand: environment.rollbackVerificationCommand ?? null,
  };
}

function cloneRunbookParameter(parameter: RunbookParameter): RunbookParameter {
  return {
    name: parameter.name,
    label: parameter.label ?? '',
    description: parameter.description ?? '',
    defaultValue: parameter.defaultValue ?? '',
    required: parameter.required,
  };
}

function cloneRunbookStep(step: RunbookStep): RunbookStep {
  return {
    id: createNestedId('rbs'),
    title: step.title,
    instructions: step.instructions,
  };
}

export function buildFleetDuplicatePayload(fleet: Fleet): Partial<Fleet> {
  return {
    name: duplicateDisplayName(fleet.name),
    description: fleet.description ?? null,
    defaultPipelineId: fleet.defaultPipelineId ?? null,
  };
}

export function buildVesselDuplicatePayload(vessel: Vessel): Partial<Vessel> {
  return {
    name: duplicateDisplayName(vessel.name),
    fleetId: vessel.fleetId ?? null,
    repoUrl: vessel.repoUrl ?? null,
    localPath: vessel.localPath ?? null,
    workingDirectory: vessel.workingDirectory ?? null,
    defaultBranch: vessel.defaultBranch || 'main',
    projectContext: vessel.projectContext ?? null,
    styleGuide: vessel.styleGuide ?? null,
    enableModelContext: vessel.enableModelContext,
    modelContext: vessel.modelContext ?? null,
    landingMode: vessel.landingMode ?? null,
    branchCleanupPolicy: vessel.branchCleanupPolicy ?? null,
    requirePassingChecksToLand: vessel.requirePassingChecksToLand,
    protectedBranchPatterns: [...(vessel.protectedBranchPatterns || [])],
    releaseBranchPrefix: vessel.releaseBranchPrefix,
    hotfixBranchPrefix: vessel.hotfixBranchPrefix,
    requirePullRequestForProtectedBranches: vessel.requirePullRequestForProtectedBranches,
    requireMergeQueueForReleaseBranches: vessel.requireMergeQueueForReleaseBranches,
    allowConcurrentMissions: vessel.allowConcurrentMissions,
    maxParallelVoyages: vessel.maxParallelVoyages,
    maxParallelMissionsPerVoyage: vessel.maxParallelMissionsPerVoyage,
    defaultPipelineId: vessel.defaultPipelineId ?? null,
  };
}

export function buildCaptainDuplicatePayload(captain: Captain): Partial<Captain> {
  return {
    name: duplicateDisplayName(captain.name),
    runtime: captain.runtime,
    systemInstructions: captain.systemInstructions ?? null,
    model: captain.model ?? null,
    allowedPersonas: captain.allowedPersonas ?? null,
    preferredPersona: captain.preferredPersona ?? null,
    runtimeOptionsJson: captain.runtimeOptionsJson ?? null,
  };
}

export function buildPersonaDuplicatePayload(persona: Persona): Partial<Persona> {
  return {
    name: duplicateDisplayName(persona.name),
    description: persona.description ?? null,
    promptTemplateName: persona.promptTemplateName,
    captainPriorities: (persona.captainPriorities ?? []).map(p => ({ ...p })),
    priorityAvailability: persona.priorityAvailability ?? 'FallThrough',
    priorityExhaustion: persona.priorityExhaustion ?? 'Wait',

  };
}

export function buildPipelineDuplicatePayload(pipeline: Pipeline): Partial<Pipeline> {
  return {
    name: duplicateDisplayName(pipeline.name),
    description: pipeline.description ?? null,
    stages: pipeline.stages
      .slice()
      .sort((left, right) => left.order - right.order)
      .map((stage, index) => ({
        id: createNestedId('pst'),
        pipelineId: null,
        personaName: stage.personaName,
        isOptional: stage.isOptional,
        description: stage.description ?? null,
        requiresReview: stage.requiresReview,
        reviewDenyAction: stage.reviewDenyAction,
        order: index + 1,
      })),
  };
}

export function buildPromptTemplateDuplicatePayload(template: PromptTemplate): {
  name: string;
  description?: string;
  category: string;
  content: string;
  active: boolean;
} {
  return {
    name: duplicateDisplayName(template.name),
    category: template.category,
    content: template.content,
    active: template.active,
    ...(template.description ? { description: template.description } : {}),
  };
}

export function buildPlaybookDuplicatePayload(playbook: Playbook): Partial<Playbook> {
  return {
    fileName: duplicateFileName(playbook.fileName),
    description: playbook.description ?? null,
    content: playbook.content,
    active: playbook.active,
  };
}

export function buildWorkflowProfileDuplicatePayload(profile: WorkflowProfile): Partial<WorkflowProfile> {
  return {
    name: duplicateDisplayName(profile.name),
    description: profile.description ?? null,
    scope: profile.scope,
    fleetId: profile.fleetId ?? null,
    vesselId: profile.vesselId ?? null,
    isDefault: false,
    active: profile.active,
    languageHints: [...(profile.languageHints || [])],
    lintCommand: profile.lintCommand ?? null,
    buildCommand: profile.buildCommand ?? null,
    unitTestCommand: profile.unitTestCommand ?? null,
    abbreviatedUnitTestCommand: profile.abbreviatedUnitTestCommand ?? null,
    integrationTestCommand: profile.integrationTestCommand ?? null,
    e2eTestCommand: profile.e2eTestCommand ?? null,
    migrationCommand: profile.migrationCommand ?? null,
    securityScanCommand: profile.securityScanCommand ?? null,
    performanceCommand: profile.performanceCommand ?? null,
    packageCommand: profile.packageCommand ?? null,
    deploymentVerificationCommand: profile.deploymentVerificationCommand ?? null,
    rollbackVerificationCommand: profile.rollbackVerificationCommand ?? null,
    publishArtifactCommand: profile.publishArtifactCommand ?? null,
    releaseVersioningCommand: profile.releaseVersioningCommand ?? null,
    changelogGenerationCommand: profile.changelogGenerationCommand ?? null,
    requiredSecrets: [...(profile.requiredSecrets || [])],
    requiredInputs: (profile.requiredInputs || []).map(cloneWorkflowInputReference),
    expectedArtifacts: [...(profile.expectedArtifacts || [])],
    environments: (profile.environments || []).map(cloneWorkflowEnvironmentProfile),
  };
}

export function buildEnvironmentDuplicatePayload(environment: DeploymentEnvironment): DeploymentEnvironmentUpsertRequest {
  return {
    vesselId: environment.vesselId ?? null,
    name: duplicateDisplayName(environment.name),
    description: environment.description ?? null,
    kind: environment.kind,
    configurationSource: environment.configurationSource ?? null,
    baseUrl: environment.baseUrl ?? null,
    healthEndpoint: environment.healthEndpoint ?? null,
    accessNotes: environment.accessNotes ?? null,
    deploymentRules: environment.deploymentRules ?? null,
    verificationDefinitions: (environment.verificationDefinitions || []).map((definition) => ({
      id: createNestedId('dvd'),
      name: definition.name,
      method: definition.method,
      path: definition.path,
      requestBody: definition.requestBody ?? null,
      headers: { ...(definition.headers || {}) },
      expectedStatusCode: definition.expectedStatusCode ?? null,
      mustContainText: definition.mustContainText ?? null,
      active: definition.active,
    })),
    rolloutMonitoringWindowMinutes: environment.rolloutMonitoringWindowMinutes,
    rolloutMonitoringIntervalSeconds: environment.rolloutMonitoringIntervalSeconds,
    alertOnRegression: environment.alertOnRegression,
    requiresApproval: environment.requiresApproval,
    isDefault: false,
    active: environment.active,
  };
}

export function buildRunbookDuplicatePayload(runbook: Runbook): RunbookUpsertRequest {
  return {
    fileName: duplicateFileName(runbook.fileName),
    title: duplicateDisplayName(runbook.title),
    description: runbook.description ?? null,
    workflowProfileId: runbook.workflowProfileId ?? null,
    environmentId: runbook.environmentId ?? null,
    environmentName: runbook.environmentName ?? null,
    defaultCheckType: runbook.defaultCheckType ?? null,
    parameters: (runbook.parameters || []).map(cloneRunbookParameter),
    steps: (runbook.steps || []).map(cloneRunbookStep),
    overviewMarkdown: runbook.overviewMarkdown,
    active: runbook.active,
  };
}

export function buildObjectiveDuplicatePayload(objective: Objective): ObjectiveUpsertRequest {
  return {
    title: duplicateDisplayName(objective.title),
    description: objective.description ?? null,
    status: 'Draft',
    kind: objective.kind,
    category: objective.category ?? null,
    priority: objective.priority,
    rank: null,
    backlogState: 'Inbox',
    effort: objective.effort,
    owner: objective.owner ?? null,
    targetVersion: objective.targetVersion ?? null,
    dueUtc: null,
    parentObjectiveId: null,
    blockedByObjectiveIds: [],
    refinementSummary: null,
    suggestedPipelineId: objective.suggestedPipelineId ?? null,
    suggestedPlaybooks: [...(objective.suggestedPlaybooks || [])],
    tags: [...(objective.tags || [])],
    acceptanceCriteria: [...(objective.acceptanceCriteria || [])],
    nonGoals: [...(objective.nonGoals || [])],
    rolloutConstraints: [...(objective.rolloutConstraints || [])],
    evidenceLinks: [...(objective.evidenceLinks || [])],
    fleetIds: [...(objective.fleetIds || [])],
    vesselIds: [...(objective.vesselIds || [])],
    planningSessionIds: [],
    refinementSessionIds: [],
    voyageIds: [],
    missionIds: [],
    checkRunIds: [],
    releaseIds: [],
    deploymentIds: [],
    incidentIds: [],
  };
}

/** Copy reusable mission configuration while excluding execution state and old dependency links. */
export function buildMissionDuplicatePayload(mission: Mission): Partial<Mission> {
  return { title: duplicateDisplayName(mission.title), description: mission.description, vesselId: mission.vesselId,
    mode: mission.mode, priority: mission.priority, persona: mission.persona, requestedCaptainId: mission.requestedCaptainId, requiresReview: mission.requiresReview,
    reviewDenyAction: mission.reviewDenyAction, selectedPlaybooks: mission.selectedPlaybooks?.map(p => ({ ...p })) };
}

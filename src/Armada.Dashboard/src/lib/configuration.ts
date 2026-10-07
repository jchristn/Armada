import type {
  MemoryType,
  ModelEndpointKind,
  ModelProvider,
  PersonaOverride,
  PipelineStage,
  WorkflowEnvironmentProfile,
  WorkflowInputReference,
  WorkflowInputReferenceProvider,
  WorkflowProfile,
} from '../types/models';

/** Pure helpers shared by the Configuration pages (dashboard) and the mobile Configuration hub. */

/** Splits a newline- or comma-separated text field into trimmed, non-empty items. */
export function splitList(value: string): string[] {
  return value
    .split(/\r?\n|,/)
    .map((item) => item.trim())
    .filter(Boolean);
}

/** One item per line (the inverse of splitList for display in a textarea). */
export function joinList(values: string[] | null | undefined): string {
  return (values || []).join('\n');
}

// ---------------------------------------------------------------- Pipelines

/** "Architect -> Worker [review]" summary of a pipeline's stages in order, or '-' when there are none. */
export function formatStages(stages: PipelineStage[]): string {
  if (!stages || stages.length === 0) return '-';
  const sorted = [...stages].sort((a, b) => a.order - b.order);
  return sorted.map(s => `${s.personaName}${s.requiresReview ? ' [review]' : ''}`).join(' -> ');
}

// ---------------------------------------------------------------- Prompt templates

export interface PromptTemplateParameter {
  name: string;
  description: string;
}

export interface PromptTemplateParameterGroup {
  label: string;
  params: PromptTemplateParameter[];
}

/** The placeholders a prompt template can use, grouped as the template editor lists them (English; translate at render). */
export const PROMPT_TEMPLATE_PARAMETER_GROUPS: PromptTemplateParameterGroup[] = [
  {
    label: 'Mission Context',
    params: [
      { name: '{MissionId}', description: 'Mission identifier' },
      { name: '{MissionTitle}', description: 'Mission title' },
      { name: '{MissionDescription}', description: 'Full mission description' },
      { name: '{MissionPersona}', description: 'Persona assigned to this mission' },
      { name: '{VoyageId}', description: 'Parent voyage identifier' },
      { name: '{BranchName}', description: 'Git branch for this mission' },
    ],
  },
  {
    label: 'Vessel Context',
    params: [
      { name: '{VesselId}', description: 'Vessel identifier' },
      { name: '{VesselName}', description: 'Vessel display name' },
      { name: '{DefaultBranch}', description: 'Default branch (e.g. main)' },
      { name: '{ProjectContext}', description: 'User-supplied project description' },
      { name: '{StyleGuide}', description: 'User-supplied style guide' },
      { name: '{ModelContext}', description: 'Agent-accumulated context' },
      { name: '{FleetId}', description: 'Parent fleet identifier' },
    ],
  },
  {
    label: 'Captain Context',
    params: [
      { name: '{CaptainId}', description: 'Captain identifier' },
      { name: '{CaptainName}', description: 'Captain display name' },
      { name: '{CaptainInstructions}', description: 'User-supplied captain instructions' },
    ],
  },
  {
    label: 'Pipeline Context',
    params: [
      { name: '{PersonaPrompt}', description: 'Resolved persona prompt text' },
      { name: '{Diff}', description: 'Diff from the prior pipeline stage' },
      { name: '{PreviousStageOutput}', description: 'Agent output from the prior pipeline stage' },
      { name: '{SelectedPlaybooksMarkdown}', description: 'Rendered content of the selected playbooks' },
      { name: '{ExistingClaudeMd}', description: "Contents of repo's existing CLAUDE.md" },
    ],
  },
  {
    label: 'System',
    params: [
      { name: '{Timestamp}', description: 'Current UTC timestamp' },
    ],
  },
];

/** Prompt template categories, in the order the pages offer them. */
export const PROMPT_TEMPLATE_CATEGORIES = ['mission', 'persona', 'structure', 'commit', 'landing', 'agent', 'import'] as const;

// ---------------------------------------------------------------- Memories

export const MEMORY_TYPES: MemoryType[] = ['Episodic', 'Semantic', 'Procedural'];

// ---------------------------------------------------------------- Model endpoints

export const MODEL_PROVIDERS: ModelProvider[] = ['Ollama', 'OpenAI', 'OpenAICompatible', 'Anthropic', 'Gemini', 'VoyageAI', 'AzureOpenAI', 'VertexAI', 'Bedrock'];
export const MODEL_ENDPOINT_KINDS: ModelEndpointKind[] = ['Embedding', 'Inference'];

/** Humanize the span between the earliest retained probe and now, for the health view. */
export function formatHealthSpan(firstUtc: string | null, now: number = Date.now()): string {
  if (!firstUtc) return '-';
  const ms = now - new Date(firstUtc).getTime();
  if (ms < 0) return '-';
  const minutes = Math.floor(ms / 60000);
  if (minutes < 1) return '<1m';
  if (minutes < 60) return `${minutes}m`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours}h ${minutes % 60}m`;
  const days = Math.floor(hours / 24);
  return `${days}d ${hours % 24}h`;
}

/** Reason a provider/kind combination is invalid (English), or null when valid. Mirrors the server-side guard. */
export function unsupportedEndpointReason(provider: ModelProvider, kind: ModelEndpointKind): string | null {
  if (kind === 'Embedding' && provider === 'Anthropic') return 'Anthropic does not provide an embeddings API. Choose Inference or a different provider.';
  if (kind === 'Inference' && provider === 'VoyageAI') return 'Voyage AI provides embeddings only. Choose Embedding or a different provider.';
  return null;
}

// ---------------------------------------------------------------- Workflow profiles

/** Configured commands of a profile (top-level plus per-environment), as the list's Capabilities column counts them. */
export function countProfileCapabilities(profile: WorkflowProfile): number {
  const commands = [
    profile.lintCommand,
    profile.buildCommand,
    profile.unitTestCommand,
    profile.integrationTestCommand,
    profile.e2eTestCommand,
    profile.packageCommand,
    profile.publishArtifactCommand,
    profile.releaseVersioningCommand,
    profile.changelogGenerationCommand,
  ].filter((value) => !!value).length;
  const environmentCommands = profile.environments.reduce((total, environment) => total
    + (environment.deployCommand ? 1 : 0)
    + (environment.rollbackCommand ? 1 : 0)
    + (environment.smokeTestCommand ? 1 : 0)
    + (environment.healthCheckCommand ? 1 : 0), 0);
  return commands + environmentCommands;
}

export function blankWorkflowEnvironment(): WorkflowEnvironmentProfile {
  return {
    environmentName: 'dev',
    deployCommand: null,
    rollbackCommand: null,
    smokeTestCommand: null,
    healthCheckCommand: null,
    deploymentVerificationCommand: null,
    rollbackVerificationCommand: null,
  };
}

export function blankInputReference(): WorkflowInputReference {
  return {
    provider: 'EnvironmentVariable',
    key: '',
    environmentName: null,
    description: null,
  };
}

/** Required-input providers with their English labels, in the editor's order. */
export const WORKFLOW_INPUT_PROVIDERS: Array<{ value: WorkflowInputReferenceProvider; label: string }> = [
  { value: 'EnvironmentVariable', label: 'Environment Variable' },
  { value: 'FilePath', label: 'File Path' },
  { value: 'DirectoryPath', label: 'Directory Path' },
  { value: 'AwsSecretsManager', label: 'AWS Secrets Manager' },
  { value: 'AzureKeyVaultSecret', label: 'Azure Key Vault' },
  { value: 'HashiCorpVault', label: 'HashiCorp Vault' },
  { value: 'OnePassword', label: '1Password' },
];

export function inputReferencePlaceholder(input: WorkflowInputReference): string {
  switch (input.provider) {
    case 'EnvironmentVariable':
      return 'AWS_PROFILE';
    case 'FilePath':
      return '/path/to/config.json';
    case 'DirectoryPath':
      return '/path/to/config-directory';
    case 'AwsSecretsManager':
      return 'prod/app/database-password';
    case 'AzureKeyVaultSecret':
      return 'kv://armada-prod/database-password';
    case 'HashiCorpVault':
      return 'secret/data/armada/prod/database';
    case 'OnePassword':
      return 'op://Engineering/Armada Prod/database-password';
    default:
      return 'Input reference';
  }
}

// ---------------------------------------------------------------- Project profiles

/** Built-in personas offered for overrides and the persona prompt preview. */
export const KNOWN_PERSONAS = ['Product Manager', 'Architect', 'Worker', 'Test Engineer', 'Judge', 'Usability Engineer'];

export function blankPersonaOverride(): PersonaOverride {
  return { personaName: 'Architect', promptTemplateName: null, additionalInstructions: null, enabled: true };
}

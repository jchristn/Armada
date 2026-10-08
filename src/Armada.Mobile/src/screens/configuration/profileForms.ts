import type {
  Fleet, Pipeline, ProjectProfile, PersonaOverride, ScopeEnum, Vessel, WorkflowEnvironmentProfile, WorkflowInputReference, WorkflowProfile,
} from '@dashboard/types/models';
import { inputReferencePlaceholder, joinList, splitList, WORKFLOW_INPUT_PROVIDERS } from '@dashboard/lib/configuration';
import { resolveCreateScope, type ScopeViewer } from '@dashboard/lib/scoping';
import { bool, str, type FormField, type FormValues } from '../../components/resource/FormSheet';
import type { Translate } from '../../i18n/LocaleContext';
import { profileScopeFields, scopeField, scopeValue } from './common';

type ProfileScope = 'Global' | 'Fleet' | 'Vessel';

function profileScope(value: string): ProfileScope {
  return value === 'Fleet' || value === 'Vessel' ? value : 'Global';
}

// ------------------------------------------------------------------ Workflow profiles

/** Top-level command fields of a workflow profile, in the editor's order, with their English labels. */
export const WORKFLOW_COMMANDS: { key: keyof WorkflowProfile; label: string }[] = [
  { key: 'lintCommand', label: 'Lint Command' },
  { key: 'buildCommand', label: 'Build Command' },
  { key: 'unitTestCommand', label: 'Unit Test Command' },
  { key: 'integrationTestCommand', label: 'Integration Test Command' },
  { key: 'e2eTestCommand', label: 'E2E Test Command' },
  { key: 'migrationCommand', label: 'Migration Command' },
  { key: 'securityScanCommand', label: 'Security Scan Command' },
  { key: 'performanceCommand', label: 'Performance Command' },
  { key: 'packageCommand', label: 'Package Command' },
  { key: 'deploymentVerificationCommand', label: 'Deployment Verification Command' },
  { key: 'rollbackVerificationCommand', label: 'Rollback Verification Command' },
  { key: 'publishArtifactCommand', label: 'Publish Artifact Command' },
  { key: 'releaseVersioningCommand', label: 'Release Versioning Command' },
  { key: 'changelogGenerationCommand', label: 'Changelog Generation Command' },
];

/** Per-environment command fields with their English labels. */
export const ENVIRONMENT_COMMANDS: { key: keyof WorkflowEnvironmentProfile; label: string }[] = [
  { key: 'deployCommand', label: 'Deploy Command' },
  { key: 'rollbackCommand', label: 'Rollback Command' },
  { key: 'smokeTestCommand', label: 'Smoke Test Command' },
  { key: 'healthCheckCommand', label: 'Health Check Command' },
  { key: 'deploymentVerificationCommand', label: 'Deployment Verification Command' },
  { key: 'rollbackVerificationCommand', label: 'Rollback Verification Command' },
];

/** A profile as the create form starts it (the dashboard's defaults), with an optional scope prefill from a link. */
export function blankWorkflowProfile(viewer: ScopeViewer, prefill: { scope?: string; fleetId?: string; vesselId?: string } = {}): WorkflowProfile {
  return {
    id: '', tenantId: null, userId: null, ownershipScope: resolveCreateScope(viewer), name: 'Default Workflow', description: null,
    scope: profileScope(prefill.scope ?? ''), fleetId: prefill.fleetId || null, vesselId: prefill.vesselId || null, isDefault: false, active: true,
    languageHints: [], lintCommand: null, buildCommand: null, unitTestCommand: null, integrationTestCommand: null, e2eTestCommand: null,
    migrationCommand: null, securityScanCommand: null, performanceCommand: null, packageCommand: null, deploymentVerificationCommand: null,
    rollbackVerificationCommand: null, publishArtifactCommand: null, releaseVersioningCommand: null, changelogGenerationCommand: null,
    requiredSecrets: [], requiredInputs: [], expectedArtifacts: [], environments: [], createdUtc: '', lastUpdateUtc: '',
  };
}

export function workflowValues(p: WorkflowProfile): FormValues {
  const v: FormValues = {
    name: p.name, description: p.description || '', scope: p.scope, ownershipScope: p.ownershipScope, fleetId: p.fleetId || '', vesselId: p.vesselId || '',
    isDefault: p.isDefault, active: p.active, languageHints: joinList(p.languageHints), expectedArtifacts: joinList(p.expectedArtifacts),
  };
  for (const c of WORKFLOW_COMMANDS) v[c.key] = (p[c.key] as string | null) || '';
  return v;
}

/** The profile with the edit form applied (commands and lists as typed; workflowPayload normalizes them). */
export function applyWorkflowValues(viewer: ScopeViewer, p: WorkflowProfile, v: FormValues, existing: boolean): WorkflowProfile {
  const next: WorkflowProfile = {
    ...p,
    name: str(v, 'name'),
    description: str(v, 'description'),
    scope: profileScope(str(v, 'scope')),
    ownershipScope: scopeValue(viewer, v, existing ? p.ownershipScope : null, 'ownershipScope'),
    fleetId: str(v, 'fleetId'),
    vesselId: str(v, 'vesselId'),
    isDefault: bool(v, 'isDefault'),
    active: bool(v, 'active'),
    languageHints: splitList(str(v, 'languageHints')),
    expectedArtifacts: splitList(str(v, 'expectedArtifacts')),
  };
  const commands = next as unknown as Record<string, string | null>;
  for (const c of WORKFLOW_COMMANDS) commands[c.key] = str(v, c.key);
  return next;
}

function trimOrNull(value: string | null | undefined): string | null {
  return value?.trim() || null;
}

/** The payload the dashboard's workflow profile editor saves (trimmed, blanks to null, empty rows dropped). */
export function workflowPayload(p: WorkflowProfile): Partial<WorkflowProfile> {
  const out: Partial<WorkflowProfile> = {
    name: p.name.trim(),
    description: trimOrNull(p.description),
    scope: p.scope,
    ownershipScope: p.ownershipScope,
    fleetId: p.scope === 'Fleet' ? p.fleetId || null : null,
    vesselId: p.scope === 'Vessel' ? p.vesselId || null : null,
    isDefault: p.isDefault,
    active: p.active,
    languageHints: p.languageHints,
    requiredInputs: p.requiredInputs
      .map((item) => ({ provider: item.provider, key: item.key.trim(), environmentName: item.environmentName?.trim() || null, description: item.description?.trim() || null }))
      .filter((item) => item.key.length > 0),
    expectedArtifacts: p.expectedArtifacts,
    environments: p.environments.map((e) => ({
      environmentName: e.environmentName.trim(),
      deployCommand: trimOrNull(e.deployCommand),
      rollbackCommand: trimOrNull(e.rollbackCommand),
      smokeTestCommand: trimOrNull(e.smokeTestCommand),
      healthCheckCommand: trimOrNull(e.healthCheckCommand),
      deploymentVerificationCommand: trimOrNull(e.deploymentVerificationCommand),
      rollbackVerificationCommand: trimOrNull(e.rollbackVerificationCommand),
    })).filter((e) => e.environmentName),
  };
  const commands = out as unknown as Record<string, string | null>;
  for (const c of WORKFLOW_COMMANDS) commands[c.key] = trimOrNull(p[c.key] as string | null);
  return out;
}

export function workflowFields(t: Translate, viewer: ScopeViewer, values: FormValues, fleets: Fleet[], vessels: Vessel[]): FormField[] {
  return [
    { kind: 'text', key: 'name', label: t('Name'), required: true },
    { kind: 'multiline', key: 'description', label: t('Description') },
    ...profileScopeFields(t, values, fleets, vessels),
    scopeField(t, viewer, 'ownershipScope'),
    { kind: 'switch', key: 'isDefault', label: t('Default for this scope') },
    { kind: 'switch', key: 'active', label: t('Active') },
    { kind: 'multiline', key: 'languageHints', label: t('Language / Runtime Hints'), placeholder: t('dotnet\nreact\npostgres') },
    { kind: 'multiline', key: 'expectedArtifacts', label: t('Expected Artifacts'), placeholder: t('bin/Release/app.zip\ncoverage/summary.xml') },
    ...WORKFLOW_COMMANDS.map((c) => ({ kind: 'text' as const, key: c.key, label: t(c.label) })),
  ];
}

export function environmentProfileValues(e: WorkflowEnvironmentProfile): FormValues {
  const v: FormValues = { environmentName: e.environmentName };
  for (const c of ENVIRONMENT_COMMANDS) v[c.key] = (e[c.key] as string | null) || '';
  return v;
}

export function environmentProfileFromValues(v: FormValues): WorkflowEnvironmentProfile {
  const e = { environmentName: str(v, 'environmentName') } as WorkflowEnvironmentProfile;
  const commands = e as unknown as Record<string, string | null>;
  for (const c of ENVIRONMENT_COMMANDS) commands[c.key] = str(v, c.key) || null;
  return e;
}

export function environmentProfileFields(t: Translate): FormField[] {
  return [
    { kind: 'text', key: 'environmentName', label: t('Name'), required: true },
    ...ENVIRONMENT_COMMANDS.map((c) => ({ kind: 'text' as const, key: c.key, label: t(c.label) })),
  ];
}

export function inputValues(i: WorkflowInputReference): FormValues {
  return { provider: i.provider, key: i.key, environmentName: i.environmentName || '', description: i.description || '' };
}

export function inputFromValues(v: FormValues): WorkflowInputReference {
  const provider = WORKFLOW_INPUT_PROVIDERS.find((p) => p.value === str(v, 'provider'))?.value ?? 'EnvironmentVariable';
  return { provider, key: str(v, 'key'), environmentName: str(v, 'environmentName') || null, description: str(v, 'description') || null };
}

export function inputFields(t: Translate, values: FormValues, environmentNames: string[]): FormField[] {
  return [
    { kind: 'select', key: 'provider', label: t('Provider'), options: WORKFLOW_INPUT_PROVIDERS.map((p) => ({ value: p.value, label: t(p.label) })) },
    { kind: 'select', key: 'environmentName', label: t('Environment Scope'), options: [{ value: '', label: t('All Environments') }, ...environmentNames.map((n) => ({ value: n, label: n }))] },
    { kind: 'text', key: 'key', label: t('Key / Path'), required: true, placeholder: t(inputReferencePlaceholder(inputFromValues(values))) },
    { kind: 'text', key: 'description', label: t('Description'), placeholder: t('Optional operator note or secret purpose') },
  ];
}

// ------------------------------------------------------------------ Project profiles

export function blankProjectProfile(viewer: ScopeViewer): ProjectProfile {
  return {
    id: '', tenantId: null, userId: null, ownershipScope: resolveCreateScope(viewer), name: 'Default Project Profile', description: null, scope: 'Global',
    fleetId: null, vesselId: null, isDefault: false, active: true, defaultPipelineId: null, workflowProfileId: null, personaOverrides: [], skills: [],
    createdUtc: '', lastUpdateUtc: '',
  };
}

export function projectValues(p: ProjectProfile): FormValues {
  return {
    name: p.name, description: p.description || '', scope: p.scope, ownershipScope: p.ownershipScope, fleetId: p.fleetId || '', vesselId: p.vesselId || '',
    isDefault: p.isDefault, active: p.active, defaultPipelineId: p.defaultPipelineId || '', workflowProfileId: p.workflowProfileId || '', skills: joinList(p.skills),
  };
}

/** The project profile payload (the dashboard's buildPayload / list form), keeping the persona overrides given. */
export function projectPayload(viewer: ScopeViewer, v: FormValues, overrides: PersonaOverride[], existingScope: ScopeEnum | null): Partial<ProjectProfile> {
  const scope = profileScope(str(v, 'scope'));
  return {
    name: str(v, 'name'),
    description: str(v, 'description') || null,
    scope,
    ownershipScope: scopeValue(viewer, v, existingScope, 'ownershipScope'),
    fleetId: scope === 'Fleet' ? (str(v, 'fleetId') || null) : null,
    vesselId: scope === 'Vessel' ? (str(v, 'vesselId') || null) : null,
    isDefault: bool(v, 'isDefault'),
    active: bool(v, 'active'),
    defaultPipelineId: str(v, 'defaultPipelineId') || null,
    workflowProfileId: str(v, 'workflowProfileId') || null,
    personaOverrides: overrides.map((o) => ({ personaName: o.personaName.trim(), promptTemplateName: o.promptTemplateName || null, additionalInstructions: o.additionalInstructions || null, enabled: o.enabled })),
    skills: splitList(str(v, 'skills')),
  };
}

export function projectFields(
  t: Translate, viewer: ScopeViewer, values: FormValues, fleets: Fleet[], vessels: Vessel[], pipelines: Pipeline[], workflowProfiles: WorkflowProfile[],
): FormField[] {
  return [
    { kind: 'text', key: 'name', label: t('Name'), required: true },
    { kind: 'text', key: 'description', label: t('Description') },
    ...profileScopeFields(t, values, fleets, vessels),
    scopeField(t, viewer, 'ownershipScope'),
    { kind: 'select', key: 'defaultPipelineId', label: t('Default Pipeline ID'), options: [{ value: '', label: '-' }, ...pipelines.map((p) => ({ value: p.id, label: `${p.name} (${p.id})` }))] },
    { kind: 'select', key: 'workflowProfileId', label: t('Workflow Profile ID'), options: [{ value: '', label: '-' }, ...workflowProfiles.map((w) => ({ value: w.id, label: `${w.name} (${w.id})` }))] },
    { kind: 'multiline', key: 'skills', label: t('Skills'), hint: t('One skill per line. Attached to this project.'), placeholder: 'dotnet\ntdd' },
    { kind: 'switch', key: 'isDefault', label: t('Default for scope') },
    { kind: 'switch', key: 'active', label: t('Active') },
  ];
}

export function overrideValues(o: PersonaOverride): FormValues {
  return { personaName: o.personaName, promptTemplateName: o.promptTemplateName || '', additionalInstructions: o.additionalInstructions || '', enabled: o.enabled };
}

export function overrideFromValues(v: FormValues): PersonaOverride {
  return { personaName: str(v, 'personaName'), promptTemplateName: str(v, 'promptTemplateName') || null, additionalInstructions: str(v, 'additionalInstructions') || null, enabled: bool(v, 'enabled') };
}

export function overrideFields(t: Translate, personaNames: string[], templateNames: string[]): FormField[] {
  return [
    { kind: 'select', key: 'personaName', label: t('Persona'), required: true, options: personaNames.map((n) => ({ value: n, label: n })) },
    { kind: 'select', key: 'promptTemplateName', label: t('Prompt Template Name'), options: [{ value: '', label: '-' }, ...templateNames.map((n) => ({ value: n, label: n }))] },
    { kind: 'multiline', key: 'additionalInstructions', label: t('Additional Instructions') },
    { kind: 'switch', key: 'enabled', label: t('Enabled') },
  ];
}

import type { Deployment, DeploymentEnvironment, DeploymentUpsertRequest, Release, Vessel, WorkflowProfile } from '@dashboard/types/models';
import { buildEnvironmentOptions } from '@dashboard/lib/deploymentEnvironments';
import { bool, str, strOrNull, type FormField, type FormValues } from '../../components/resource/FormSheet';
import { recordOptions } from '../../resource/lookups';
import type { Translate } from '../../i18n/LocaleContext';

/** Prefill a link can carry into /deployments/new (the dashboard passes it as router state). */
export interface DeploymentPrefill {
  vesselId?: string;
  workflowProfileId?: string;
  environmentId?: string;
  environmentName?: string;
  releaseId?: string;
  missionId?: string;
  voyageId?: string;
  title?: string;
  sourceRef?: string;
  summary?: string;
  notes?: string;
}

export function newDeploymentValues(p: DeploymentPrefill = {}): FormValues {
  return {
    vesselId: p.vesselId ?? '', workflowProfileId: p.workflowProfileId ?? '', environmentId: p.environmentId ?? '', environmentName: p.environmentName ?? '',
    releaseId: p.releaseId ?? '', sourceRef: p.sourceRef ?? '', missionId: p.missionId ?? '', voyageId: p.voyageId ?? '', title: p.title || 'Deployment',
    summary: p.summary ?? '', notes: p.notes ?? '', autoExecute: true,
  };
}

export function deploymentValues(d: Deployment): FormValues {
  return {
    vesselId: d.vesselId || '', workflowProfileId: d.workflowProfileId || '', environmentId: d.environmentId || '', environmentName: d.environmentName || '',
    releaseId: d.releaseId || '', sourceRef: d.sourceRef || '', missionId: d.missionId || '', voyageId: d.voyageId || '', title: d.title || 'Deployment',
    summary: d.summary || '', notes: d.notes || '', autoExecute: true,
  };
}

/** The upsert payload as the dashboard builds it (blank text becomes null). */
export function deploymentPayload(v: FormValues): DeploymentUpsertRequest {
  return {
    vesselId: str(v, 'vesselId') || null,
    workflowProfileId: str(v, 'workflowProfileId') || null,
    environmentId: str(v, 'environmentId') || null,
    environmentName: strOrNull(v, 'environmentName'),
    releaseId: str(v, 'releaseId') || null,
    missionId: strOrNull(v, 'missionId'),
    voyageId: strOrNull(v, 'voyageId'),
    title: strOrNull(v, 'title'),
    sourceRef: strOrNull(v, 'sourceRef'),
    summary: strOrNull(v, 'summary'),
    notes: strOrNull(v, 'notes'),
    autoExecute: bool(v, 'autoExecute'),
  };
}

/**
 * The dashboard's linked-field behavior: choosing an environment fills its name and (when none is chosen) its vessel;
 * choosing a vessel clears an environment of another vessel; choosing a release fills its vessel when none is chosen.
 */
export function linkDeploymentValues(next: FormValues, prev: FormValues, environments: DeploymentEnvironment[], releases: Release[]): FormValues {
  const out = { ...next };
  if (str(next, 'environmentId') !== str(prev, 'environmentId')) {
    const env = environments.find((e) => e.id === str(next, 'environmentId'));
    if (env) {
      out.environmentName = env.name;
      if (!str(out, 'vesselId') && env.vesselId) out.vesselId = env.vesselId;
    }
  }
  if (str(next, 'vesselId') !== str(prev, 'vesselId')) {
    const vesselId = str(next, 'vesselId');
    const env = environments.find((e) => e.id === str(out, 'environmentId'));
    if (vesselId && env && env.vesselId !== vesselId) out.environmentId = '';
  }
  if (str(next, 'releaseId') !== str(prev, 'releaseId')) {
    const release = releases.find((r) => r.id === str(next, 'releaseId'));
    if (release && !str(out, 'vesselId') && release.vesselId) out.vesselId = release.vesselId;
  }
  return out;
}

export function deploymentFields(t: Translate, v: FormValues, data: { vessels: Vessel[]; profiles: WorkflowProfile[]; environments: DeploymentEnvironment[]; releases: Release[] }): FormField[] {
  const vesselId = str(v, 'vesselId');
  const vesselNames = new Map(data.vessels.map((x) => [x.id, x.name]));
  const envs = data.environments.filter((e) => !vesselId || e.vesselId === vesselId);
  const envOptions = buildEnvironmentOptions(envs, vesselNames, !!vesselId, t('No vessel'));
  const releases = data.releases.filter((r) => !vesselId || r.vesselId === vesselId);
  return [
    { kind: 'select', key: 'vesselId', label: t('Vessel'), options: recordOptions(data.vessels, t('Select a vessel')) },
    { kind: 'select', key: 'workflowProfileId', label: t('Workflow Profile'), options: recordOptions(data.profiles, t('Resolved default')) },
    { kind: 'select', key: 'environmentId', label: t('Environment'), options: [{ value: '', label: t('Resolve by environment name') }, ...envOptions.map((o) => ({ value: o.id, label: o.label }))] },
    { kind: 'text', key: 'environmentName', label: t('Environment Name'), placeholder: t('staging, production, customer-a') },
    { kind: 'select', key: 'releaseId', label: t('Release'), options: [{ value: '', label: t('No linked release') }, ...releases.map((r) => ({ value: r.id, label: r.title }))] },
    { kind: 'text', key: 'sourceRef', label: t('Source Ref'), placeholder: t('branch, tag, or commit') },
    { kind: 'text', key: 'missionId', label: t('Mission ID'), placeholder: 'mis_...' },
    { kind: 'text', key: 'voyageId', label: t('Voyage ID'), placeholder: 'voy_...' },
    { kind: 'text', key: 'title', label: t('Title') },
    { kind: 'multiline', key: 'summary', label: t('Summary') },
    { kind: 'multiline', key: 'notes', label: t('Notes') },
    { kind: 'switch', key: 'autoExecute', label: t('Execute immediately when approval is not required') },
  ];
}

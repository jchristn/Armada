import type { Deployment, DeploymentEnvironment, Incident, IncidentSeverity, IncidentStatus, IncidentUpsertRequest, Release, Vessel } from '@dashboard/types/models';
import { INCIDENT_SEVERITIES, INCIDENT_STATUSES, toInputDateTime, toUtcValue } from '@dashboard/lib/deliveryForms';
import { str, strOrNull, type FormField, type FormValues } from '../../components/resource/FormSheet';
import { recordOptions, valueOptions } from '../../resource/lookups';
import type { Translate } from '../../i18n/LocaleContext';

/** Prefill a link can carry into /incidents/new (the dashboard passes it as router state). */
export interface IncidentPrefill {
  title?: string;
  summary?: string;
  status?: string;
  severity?: string;
  vesselId?: string;
  environmentId?: string;
  environmentName?: string;
  deploymentId?: string;
  releaseId?: string;
  missionId?: string;
  voyageId?: string;
  rollbackDeploymentId?: string;
  impact?: string;
}

export const INCIDENT_PREFILL_KEYS: (keyof IncidentPrefill)[] = [
  'title', 'summary', 'status', 'severity', 'vesselId', 'environmentId', 'environmentName', 'deploymentId', 'releaseId', 'missionId', 'voyageId', 'rollbackDeploymentId', 'impact',
];

function pick<T extends string>(value: string | undefined, allowed: readonly T[], fallback: T): T {
  return value && (allowed as readonly string[]).includes(value) ? (value as T) : fallback;
}

export function newIncidentValues(p: IncidentPrefill = {}): FormValues {
  return {
    title: p.title || 'Incident', summary: p.summary ?? '', status: pick(p.status, INCIDENT_STATUSES, 'Open'), severity: pick(p.severity, INCIDENT_SEVERITIES, 'High'),
    vesselId: p.vesselId ?? '', environmentId: p.environmentId ?? '', environmentName: p.environmentName ?? '', deploymentId: p.deploymentId ?? '',
    releaseId: p.releaseId ?? '', missionId: p.missionId ?? '', voyageId: p.voyageId ?? '', rollbackDeploymentId: p.rollbackDeploymentId ?? '',
    impact: p.impact ?? '', rootCause: '', recoveryNotes: '', postmortem: '', detectedUtc: '', mitigatedUtc: '', closedUtc: '',
  };
}

export function incidentValues(i: Incident): FormValues {
  return {
    title: i.title, summary: i.summary || '', status: i.status, severity: i.severity, vesselId: i.vesselId || '', environmentId: i.environmentId || '',
    environmentName: i.environmentName || '', deploymentId: i.deploymentId || '', releaseId: i.releaseId || '', missionId: i.missionId || '',
    voyageId: i.voyageId || '', rollbackDeploymentId: i.rollbackDeploymentId || '', impact: i.impact || '', rootCause: i.rootCause || '',
    recoveryNotes: i.recoveryNotes || '', postmortem: i.postmortem || '', detectedUtc: toInputDateTime(i.detectedUtc),
    mitigatedUtc: toInputDateTime(i.mitigatedUtc), closedUtc: toInputDateTime(i.closedUtc),
  };
}

/** The full upsert payload (the detail page's), times from local `YYYY-MM-DDTHH:mm` to UTC. */
export function incidentPayload(v: FormValues): IncidentUpsertRequest {
  return {
    title: strOrNull(v, 'title'),
    summary: strOrNull(v, 'summary'),
    status: str(v, 'status') as IncidentStatus,
    severity: str(v, 'severity') as IncidentSeverity,
    vesselId: str(v, 'vesselId') || null,
    environmentId: str(v, 'environmentId') || null,
    environmentName: strOrNull(v, 'environmentName'),
    deploymentId: strOrNull(v, 'deploymentId'),
    releaseId: strOrNull(v, 'releaseId'),
    missionId: strOrNull(v, 'missionId'),
    voyageId: strOrNull(v, 'voyageId'),
    rollbackDeploymentId: strOrNull(v, 'rollbackDeploymentId'),
    impact: strOrNull(v, 'impact'),
    rootCause: strOrNull(v, 'rootCause'),
    recoveryNotes: strOrNull(v, 'recoveryNotes'),
    postmortem: strOrNull(v, 'postmortem'),
    detectedUtc: toUtcValue(str(v, 'detectedUtc')),
    mitigatedUtc: toUtcValue(str(v, 'mitigatedUtc')),
    closedUtc: toUtcValue(str(v, 'closedUtc')),
  };
}

/**
 * The detail page's linked fields: an environment fills its name and vessel; a deployment fills the environment,
 * vessel, release, mission, and voyage that are still blank.
 */
export function linkIncidentValues(next: FormValues, prev: FormValues, environments: DeploymentEnvironment[], deployments: Deployment[]): FormValues {
  const out = { ...next };
  if (str(next, 'environmentId') !== str(prev, 'environmentId')) {
    const env = environments.find((e) => e.id === str(next, 'environmentId'));
    if (env) {
      out.environmentName = env.name;
      if (!str(out, 'vesselId') && env.vesselId) out.vesselId = env.vesselId;
    }
  }
  if (str(next, 'deploymentId') !== str(prev, 'deploymentId')) {
    const d = deployments.find((x) => x.id === str(next, 'deploymentId'));
    if (d) {
      if (!str(out, 'environmentId') && d.environmentId) out.environmentId = d.environmentId;
      if (!str(out, 'environmentName') && d.environmentName) out.environmentName = d.environmentName;
      if (!str(out, 'vesselId') && d.vesselId) out.vesselId = d.vesselId;
      if (!str(out, 'releaseId') && d.releaseId) out.releaseId = d.releaseId;
      if (!str(out, 'missionId') && d.missionId) out.missionId = d.missionId;
      if (!str(out, 'voyageId') && d.voyageId) out.voyageId = d.voyageId;
    }
  }
  return out;
}

export interface IncidentReference {
  vessels: Vessel[];
  environments: DeploymentEnvironment[];
  deployments: Deployment[];
  releases: Release[];
}

export function incidentFields(t: Translate, ref: IncidentReference, full: boolean): FormField[] {
  const base: FormField[] = [
    { kind: 'text', key: 'title', label: t('Title'), required: true },
    { kind: 'select', key: 'status', label: t('Status'), options: valueOptions(INCIDENT_STATUSES, t) },
    { kind: 'select', key: 'severity', label: t('Severity'), options: valueOptions(INCIDENT_SEVERITIES, t) },
    { kind: 'select', key: 'vesselId', label: t('Vessel'), options: recordOptions(ref.vessels, t('Select a vessel')) },
    { kind: 'select', key: 'environmentId', label: t('Environment'), options: recordOptions(ref.environments, t('Select an environment')) },
  ];
  const links: FormField[] = [
    { kind: 'select', key: 'deploymentId', label: t('Deployment'), options: [{ value: '', label: t('No linked deployment') }, ...ref.deployments.map((d) => ({ value: d.id, label: d.title }))] },
    { kind: 'select', key: 'releaseId', label: t('Release'), options: [{ value: '', label: t('No linked release') }, ...ref.releases.map((r) => ({ value: r.id, label: r.title }))] },
  ];
  if (!full) {
    return [...base, ...links, { kind: 'multiline', key: 'summary', label: t('Summary') }, { kind: 'multiline', key: 'impact', label: t('Impact') }];
  }
  const dateHint = 'YYYY-MM-DDTHH:mm';
  return [
    ...base,
    { kind: 'text', key: 'environmentName', label: t('Environment Name') },
    ...links,
    { kind: 'text', key: 'missionId', label: t('Mission ID'), placeholder: 'mis_...' },
    { kind: 'text', key: 'voyageId', label: t('Voyage ID'), placeholder: 'voy_...' },
    { kind: 'text', key: 'detectedUtc', label: t('Detected'), placeholder: dateHint },
    { kind: 'text', key: 'mitigatedUtc', label: t('Mitigated'), placeholder: dateHint },
    { kind: 'text', key: 'closedUtc', label: t('Closed'), placeholder: dateHint },
    { kind: 'text', key: 'rollbackDeploymentId', label: t('Rollback Deployment'), placeholder: 'dpl_...' },
    { kind: 'multiline', key: 'summary', label: t('Summary') },
    { kind: 'multiline', key: 'impact', label: t('Impact') },
    { kind: 'multiline', key: 'rootCause', label: t('Root Cause') },
    { kind: 'multiline', key: 'recoveryNotes', label: t('Recovery Notes') },
    { kind: 'multiline', key: 'postmortem', label: t('Postmortem') },
  ];
}

/** The list page's create payload: the short form, environment name from the chosen environment. */
export function incidentCreatePayload(v: FormValues, environments: DeploymentEnvironment[]): IncidentUpsertRequest {
  const environmentId = str(v, 'environmentId');
  return {
    title: strOrNull(v, 'title'),
    summary: strOrNull(v, 'summary'),
    status: str(v, 'status') as IncidentStatus,
    severity: str(v, 'severity') as IncidentSeverity,
    vesselId: str(v, 'vesselId') || null,
    environmentId: environmentId || null,
    environmentName: environmentId ? (environments.find((e) => e.id === environmentId)?.name || null) : null,
    deploymentId: str(v, 'deploymentId') || null,
    releaseId: str(v, 'releaseId') || null,
    impact: strOrNull(v, 'impact'),
  };
}

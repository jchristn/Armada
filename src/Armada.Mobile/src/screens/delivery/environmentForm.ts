import type { DeploymentEnvironment, DeploymentEnvironmentUpsertRequest, DeploymentVerificationDefinition, Vessel } from '@dashboard/types/models';
import { ENVIRONMENT_KINDS, parseHeaderLines, serializeHeaderLines } from '@dashboard/lib/environmentForm';
import { bool, int, str, strOrNull, type FormField, type FormValues } from '../../components/resource/FormSheet';
import { recordOptions, valueOptions } from '../../resource/lookups';
import type { Translate } from '../../i18n/LocaleContext';

/** Form values for a new environment (the dashboard's create defaults), with optional prefill from a link. */
export function newEnvironmentValues(prefill: { vesselId?: string; kind?: string; name?: string } = {}): FormValues {
  const kind = prefill.kind && (ENVIRONMENT_KINDS as string[]).includes(prefill.kind) ? prefill.kind : 'Development';
  return {
    vesselId: prefill.vesselId ?? '', name: prefill.name ?? 'Environment', kind, configurationSource: '', baseUrl: '', healthEndpoint: '',
    description: '', accessNotes: '', deploymentRules: '', rolloutMonitoringWindowMinutes: '60', rolloutMonitoringIntervalSeconds: '300',
    alertOnRegression: true, requiresApproval: false, isDefault: false, active: true,
  };
}

export function environmentValues(e: DeploymentEnvironment): FormValues {
  return {
    vesselId: e.vesselId || '', name: e.name, kind: e.kind, configurationSource: e.configurationSource || '', baseUrl: e.baseUrl || '',
    healthEndpoint: e.healthEndpoint || '', description: e.description || '', accessNotes: e.accessNotes || '', deploymentRules: e.deploymentRules || '',
    rolloutMonitoringWindowMinutes: String(e.rolloutMonitoringWindowMinutes || 60), rolloutMonitoringIntervalSeconds: String(e.rolloutMonitoringIntervalSeconds || 300),
    alertOnRegression: e.alertOnRegression, requiresApproval: e.requiresApproval, isDefault: e.isDefault, active: e.active,
  };
}

/** The upsert payload, as the dashboard builds it (blank text becomes null; monitoring clamps to >= 0 and >= 30). */
export function environmentPayload(v: FormValues, verificationDefinitions: DeploymentVerificationDefinition[]): DeploymentEnvironmentUpsertRequest {
  return {
    vesselId: str(v, 'vesselId') || null,
    name: strOrNull(v, 'name'),
    description: strOrNull(v, 'description'),
    kind: str(v, 'kind') as DeploymentEnvironmentUpsertRequest['kind'],
    configurationSource: strOrNull(v, 'configurationSource'),
    baseUrl: strOrNull(v, 'baseUrl'),
    healthEndpoint: strOrNull(v, 'healthEndpoint'),
    accessNotes: strOrNull(v, 'accessNotes'),
    deploymentRules: strOrNull(v, 'deploymentRules'),
    verificationDefinitions,
    rolloutMonitoringWindowMinutes: Math.max(0, int(v, 'rolloutMonitoringWindowMinutes', 0)),
    rolloutMonitoringIntervalSeconds: Math.max(30, int(v, 'rolloutMonitoringIntervalSeconds', 30)),
    alertOnRegression: bool(v, 'alertOnRegression'),
    requiresApproval: bool(v, 'requiresApproval'),
    isDefault: bool(v, 'isDefault'),
    active: bool(v, 'active'),
  };
}

export function environmentFields(t: Translate, vessels: Vessel[]): FormField[] {
  return [
    { kind: 'select', key: 'vesselId', label: t('Vessel'), options: recordOptions(vessels, t('Select a vessel')) },
    { kind: 'text', key: 'name', label: t('Name'), required: true },
    { kind: 'select', key: 'kind', label: t('Kind'), options: valueOptions(ENVIRONMENT_KINDS) },
    { kind: 'text', key: 'configurationSource', label: t('Configuration Source'), placeholder: t('e.g. Helm values, appsettings.Production.json, Azure slot config') },
    { kind: 'text', key: 'baseUrl', label: t('Base URL'), placeholder: 'https://service.example.com' },
    { kind: 'text', key: 'healthEndpoint', label: t('Health Endpoint'), placeholder: '/health or https://service.example.com/health' },
    { kind: 'multiline', key: 'description', label: t('Description') },
    { kind: 'multiline', key: 'accessNotes', label: t('Access Notes'), placeholder: t('How do operators reach or authenticate to this environment?') },
    { kind: 'multiline', key: 'deploymentRules', label: t('Deployment Rules'), placeholder: t('Document freeze windows, approval policy, maintenance constraints, or rollout notes.') },
    { kind: 'integer', key: 'rolloutMonitoringWindowMinutes', label: t('Rollout Monitoring Window (minutes)') },
    { kind: 'integer', key: 'rolloutMonitoringIntervalSeconds', label: t('Monitoring Interval (seconds)') },
    { kind: 'switch', key: 'alertOnRegression', label: t('Record regression alerts during rollout monitoring') },
    { kind: 'switch', key: 'requiresApproval', label: t('Requires approval') },
    { kind: 'switch', key: 'isDefault', label: t('Default environment for vessel') },
    { kind: 'switch', key: 'active', label: t('Active') },
  ];
}

export function verificationValues(d: DeploymentVerificationDefinition): FormValues {
  return {
    name: d.name, method: d.method, path: d.path, expectedStatusCode: String(d.expectedStatusCode ?? 200), mustContainText: d.mustContainText || '',
    headers: serializeHeaderLines(d.headers), requestBody: d.requestBody || '', active: d.active,
  };
}

/** Applies the verification form to a definition (method upper-cased, status 0 becomes null, as the dashboard does). */
export function applyVerification(d: DeploymentVerificationDefinition, v: FormValues): DeploymentVerificationDefinition {
  return {
    ...d,
    name: str(v, 'name'),
    method: str(v, 'method').toUpperCase(),
    path: str(v, 'path'),
    expectedStatusCode: int(v, 'expectedStatusCode', 0) || null,
    mustContainText: str(v, 'mustContainText') || null,
    headers: parseHeaderLines(str(v, 'headers')),
    requestBody: str(v, 'requestBody') || null,
    active: bool(v, 'active'),
  };
}

export function verificationFields(t: Translate): FormField[] {
  return [
    { kind: 'text', key: 'name', label: t('Name') },
    { kind: 'text', key: 'method', label: t('Method') },
    { kind: 'text', key: 'path', label: t('Path'), placeholder: '/health or /api/status' },
    { kind: 'integer', key: 'expectedStatusCode', label: t('Expected Status') },
    { kind: 'text', key: 'mustContainText', label: t('Must Contain Text') },
    { kind: 'multiline', key: 'headers', label: t('Headers'), placeholder: t('Header-Name: value') },
    { kind: 'multiline', key: 'requestBody', label: t('Request Body') },
    { kind: 'switch', key: 'active', label: t('Active') },
  ];
}

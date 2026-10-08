import type {
  CheckRunType, DeploymentEnvironment, Runbook, RunbookExecutionStartRequest, RunbookParameter, RunbookStep, RunbookUpsertRequest, ScopeEnum, WorkflowProfile,
} from '@dashboard/types/models';
import { RUNBOOK_CHECK_TYPES } from '@dashboard/lib/deliveryForms';
import { canChooseScope, type ScopeViewer } from '@dashboard/lib/scoping';
import { bool, str, strOrNull, type FormField, type FormValues } from '../../components/resource/FormSheet';
import { param } from '../../resource/links';
import { recordOptions, valueOptions } from '../../resource/lookups';
import type { Translate } from '../../i18n/LocaleContext';
import { useAuth } from '../../auth/AuthContext';

/** The scope viewer (role, tenant, user) for scoped-visibility checks, as the dashboard builds it from useAuth(). */
export function useScopeViewer(): ScopeViewer {
  const { isAdmin, isTenantAdmin, user } = useAuth();
  return { isAdmin, isTenantAdmin, tenantId: user?.user?.tenantId, userId: user?.user?.id };
}

/** An execution prefill handed over by an incident, deployment, or environment (query keys exec*). */
export type ExecutionPrefill = Partial<RunbookExecutionStartRequest>;

/** Reads ?execTitle=&execWorkflowProfileId=&execEnvironmentId=&execEnvironmentName=&execCheckType=&execDeploymentId=&execIncidentId=&execNotes=. */
export function executionPrefillFrom(params: Record<string, string | string[] | undefined>): ExecutionPrefill | null {
  const map: [string, keyof RunbookExecutionStartRequest][] = [
    ['execTitle', 'title'], ['execWorkflowProfileId', 'workflowProfileId'], ['execEnvironmentId', 'environmentId'], ['execEnvironmentName', 'environmentName'],
    ['execDeploymentId', 'deploymentId'], ['execIncidentId', 'incidentId'], ['execNotes', 'notes'],
  ];
  const out: ExecutionPrefill = {};
  let any = false;
  for (const [key, field] of map) {
    const value = param(params[key]);
    if (value) { (out as Record<string, string>)[field] = value; any = true; }
  }
  const checkType = param(params.execCheckType);
  if (checkType) { out.checkType = checkType as CheckRunType; any = true; }
  return any ? out : null;
}

/** The exec* query string that carries an execution prefill on to a runbook. */
export function executionPrefillQuery(p: ExecutionPrefill | null): Record<string, string | null | undefined> {
  if (!p) return {};
  return {
    execTitle: p.title, execWorkflowProfileId: p.workflowProfileId, execEnvironmentId: p.environmentId, execEnvironmentName: p.environmentName,
    execCheckType: p.checkType, execDeploymentId: p.deploymentId, execIncidentId: p.incidentId, execNotes: p.notes,
  };
}

export function newRunbookValues(scope: ScopeEnum): FormValues {
  return { fileName: 'RUNBOOK.md', title: 'Runbook', description: '', workflowProfileId: '', environmentId: '', defaultCheckType: '', active: true, scope };
}

export function runbookCreateFields(t: Translate, viewer: ScopeViewer, profiles: WorkflowProfile[], environments: DeploymentEnvironment[]): FormField[] {
  return [
    { kind: 'text', key: 'fileName', label: t('File Name') },
    { kind: 'text', key: 'title', label: t('Title') },
    { kind: 'multiline', key: 'description', label: t('Description') },
    { kind: 'select', key: 'workflowProfileId', label: t('Workflow Profile'), options: recordOptions(profiles, t('No workflow profile')) },
    { kind: 'select', key: 'environmentId', label: t('Environment'), options: recordOptions(environments, t('No environment')) },
    { kind: 'select', key: 'defaultCheckType', label: t('Default Check Type'), options: [{ value: '', label: t('No default check') }, ...valueOptions(RUNBOOK_CHECK_TYPES)] },
    { kind: 'switch', key: 'active', label: t('Active') },
    canChooseScope(viewer)
      ? { kind: 'select', key: 'scope', label: t('Visibility'), options: [
        { value: 'TenantWide', label: t('Tenant-wide (everyone in the tenant can use it)') },
        { value: 'UserSpecific', label: t('Personal (only you can see and edit it)') },
      ] }
      : { kind: 'note', key: 'scope', label: `${t('Visibility')}: ${t('Personal (only you can see and edit it)')}` },
  ];
}

export function runbookCreatePayload(v: FormValues, environments: DeploymentEnvironment[]): RunbookUpsertRequest {
  const environmentId = str(v, 'environmentId');
  return {
    fileName: strOrNull(v, 'fileName'),
    title: strOrNull(v, 'title'),
    description: strOrNull(v, 'description'),
    workflowProfileId: str(v, 'workflowProfileId') || null,
    environmentId: environmentId || null,
    environmentName: environmentId ? (environments.find((e) => e.id === environmentId)?.name || null) : null,
    defaultCheckType: (str(v, 'defaultCheckType') || null) as CheckRunType | null,
    parameters: [],
    steps: [],
    overviewMarkdown: '',
    active: bool(v, 'active'),
    scope: str(v, 'scope') as ScopeEnum,
  };
}

export function runbookValues(r: Runbook): FormValues {
  return {
    fileName: r.fileName, title: r.title, description: r.description || '', workflowProfileId: r.workflowProfileId || '', environmentId: r.environmentId || '',
    environmentName: r.environmentName || '', defaultCheckType: r.defaultCheckType || '', overviewMarkdown: r.overviewMarkdown || '', active: r.active,
  };
}

/** Choosing an environment fills its name (the detail page's behavior). */
export function linkRunbookValues(next: FormValues, prev: FormValues, environments: DeploymentEnvironment[]): FormValues {
  if (str(next, 'environmentId') === str(prev, 'environmentId')) return next;
  const env = environments.find((e) => e.id === str(next, 'environmentId'));
  return env ? { ...next, environmentName: env.name } : next;
}

export function runbookEditFields(t: Translate, profiles: WorkflowProfile[], environments: DeploymentEnvironment[]): FormField[] {
  return [
    { kind: 'text', key: 'fileName', label: t('File Name') },
    { kind: 'text', key: 'title', label: t('Title') },
    { kind: 'multiline', key: 'description', label: t('Description') },
    { kind: 'select', key: 'workflowProfileId', label: t('Workflow Profile'), options: recordOptions(profiles, t('No workflow profile')) },
    { kind: 'select', key: 'environmentId', label: t('Environment'), options: recordOptions(environments, t('No environment')) },
    { kind: 'text', key: 'environmentName', label: t('Environment Name') },
    { kind: 'select', key: 'defaultCheckType', label: t('Default Check Type'), options: [{ value: '', label: t('No default check') }, ...valueOptions(RUNBOOK_CHECK_TYPES)] },
    { kind: 'multiline', key: 'overviewMarkdown', label: t('Overview Markdown') },
    { kind: 'switch', key: 'active', label: t('Active') },
  ];
}

/** The detail page's save payload: the edited fields plus the runbook's parameters and steps. */
export function runbookPayload(v: FormValues, parameters: RunbookParameter[], steps: RunbookStep[]): RunbookUpsertRequest {
  return {
    fileName: strOrNull(v, 'fileName'),
    title: strOrNull(v, 'title'),
    description: strOrNull(v, 'description'),
    workflowProfileId: str(v, 'workflowProfileId') || null,
    environmentId: str(v, 'environmentId') || null,
    environmentName: strOrNull(v, 'environmentName'),
    defaultCheckType: (str(v, 'defaultCheckType') || null) as CheckRunType | null,
    parameters,
    steps,
    overviewMarkdown: str(v, 'overviewMarkdown'),
    active: bool(v, 'active'),
  };
}

export function parameterValues(p: RunbookParameter): FormValues {
  return { name: p.name, label: p.label || '', defaultValue: p.defaultValue || '', description: p.description || '', required: p.required };
}

export function applyParameter(v: FormValues): RunbookParameter {
  return { name: str(v, 'name'), label: str(v, 'label'), defaultValue: str(v, 'defaultValue'), description: str(v, 'description'), required: bool(v, 'required') };
}

export function parameterFields(t: Translate): FormField[] {
  return [
    { kind: 'text', key: 'name', label: t('Name') },
    { kind: 'text', key: 'label', label: t('Label') },
    { kind: 'text', key: 'defaultValue', label: t('Default Value') },
    { kind: 'multiline', key: 'description', label: t('Description') },
    { kind: 'switch', key: 'required', label: t('Required') },
  ];
}

export function stepFields(t: Translate): FormField[] {
  return [
    { kind: 'text', key: 'title', label: t('Title') },
    { kind: 'multiline', key: 'instructions', label: t('Instructions') },
  ];
}

/** Start-execution form values: the prefill first, then the runbook's binding and parameter defaults. */
export function startExecutionValues(r: Runbook, prefill: ExecutionPrefill | null): FormValues {
  const values: FormValues = {
    title: prefill?.title || '',
    workflowProfileId: prefill?.workflowProfileId || r.workflowProfileId || '',
    environmentId: prefill?.environmentId || r.environmentId || '',
    environmentName: prefill?.environmentName || r.environmentName || '',
    checkType: prefill?.checkType || r.defaultCheckType || '',
    notes: prefill?.notes || '',
  };
  for (const p of r.parameters) values[`param:${p.name}`] = prefill?.parameterValues?.[p.name] || p.defaultValue || '';
  return values;
}

export function startExecutionFields(t: Translate, r: Runbook, profiles: WorkflowProfile[], environments: DeploymentEnvironment[]): FormField[] {
  return [
    { kind: 'text', key: 'title', label: t('Title'), placeholder: r.title },
    { kind: 'select', key: 'workflowProfileId', label: t('Workflow Profile'), options: recordOptions(profiles, t('Use runbook binding')) },
    { kind: 'select', key: 'environmentId', label: t('Environment'), options: recordOptions(environments, t('Use runbook binding')) },
    { kind: 'text', key: 'environmentName', label: t('Environment Name') },
    { kind: 'select', key: 'checkType', label: t('Check Type'), options: [{ value: '', label: t('No default check') }, ...valueOptions(RUNBOOK_CHECK_TYPES)] },
    { kind: 'multiline', key: 'notes', label: t('Execution Notes') },
    ...(r.parameters.length > 0 ? [{ kind: 'note' as const, key: 'parametersNote', label: t('Parameters') }] : []),
    ...r.parameters.map((p): FormField => ({ kind: 'text', key: `param:${p.name}`, label: p.required ? `${p.label || p.name} *` : (p.label || p.name), hint: p.description })),
  ];
}

export function startExecutionPayload(r: Runbook, v: FormValues, prefill: ExecutionPrefill | null): RunbookExecutionStartRequest {
  const parameterValues: Record<string, string> = {};
  for (const p of r.parameters) parameterValues[p.name] = str(v, `param:${p.name}`);
  return {
    title: strOrNull(v, 'title'),
    workflowProfileId: str(v, 'workflowProfileId') || null,
    environmentId: str(v, 'environmentId') || null,
    environmentName: strOrNull(v, 'environmentName'),
    checkType: (str(v, 'checkType') || null) as CheckRunType | null,
    parameterValues,
    deploymentId: prefill?.deploymentId || null,
    incidentId: prefill?.incidentId || null,
    notes: strOrNull(v, 'notes'),
  };
}

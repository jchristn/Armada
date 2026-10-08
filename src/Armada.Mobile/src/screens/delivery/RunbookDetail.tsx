import { Stack, useLocalSearchParams, useRouter, type Href } from 'expo-router';
import { useState } from 'react';
import { StyleSheet, View } from 'react-native';
import {
  createRunbook, deleteRunbook, deleteRunbookExecution, getRunbook, getRunbookExecution, listRunbookExecutions, startRunbookExecution, updateRunbook,
  updateRunbookExecution,
} from '@dashboard/api/client';
import type { Runbook, RunbookExecution, RunbookExecutionStatus, RunbookParameter, RunbookStep } from '@dashboard/types/models';
import { buildRunbookDuplicatePayload } from '@dashboard/lib/duplicates';
import { cloneExecution, createDefaultParameter, createDefaultStep } from '@dashboard/lib/deliveryForms';
import { canEdit as canEditScoped, resolveCreateScope, scopeLabel } from '@dashboard/lib/scoping';
import { ActionBar, DetailBody, DetailHeader, DetailPending, Field, FieldCard, JsonSheet, TextBlock } from '../../components/resource/DetailParts';
import { FormSheet, str } from '../../components/resource/FormSheet';
import { ResourceRow, useConfirm } from '../../components/resource/ResourceRow';
import { resourceStyles } from '../../components/resource/styles';
import { AppText } from '../../components/ui/AppText';
import { Banner } from '../../components/ui/Banner';
import { Button } from '../../components/ui/Button';
import { StatusBadge } from '../../components/ui/StatusBadge';
import { SwitchField } from '../../components/ui/SwitchField';
import { TextField } from '../../components/ui/TextField';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { param, prefillQuery } from '../../resource/links';
import { ALL, useNameMap } from '../../resource/lookups';
import { statusBadge } from '../../resource/status';
import { errorText, useLoad, useReloadOnFocus } from '../../resource/useLoad';
import { spacing } from '../../theme/typography';
import { useEnvironments, useWorkflowProfiles } from './deliveryLookups';
import {
  applyParameter, executionPrefillFrom, executionPrefillQuery, linkRunbookValues, newRunbookValues, parameterFields, parameterValues, runbookCreateFields, runbookCreatePayload,
  runbookEditFields, runbookPayload, runbookValues,
  startExecutionFields, startExecutionPayload, startExecutionValues, stepFields, useScopeViewer, type ExecutionPrefill,
} from './runbookForm';

export interface RunbookDetailViewProps {
  id: string;
  /** Execution defaults handed over by an incident, deployment, or environment. */
  prefill?: ExecutionPrefill | null;
  /** The execution to show first (?executionId=). */
  executionId?: string;
  embedded?: boolean;
  onDeleted?: () => void;
  onChanged?: () => void;
}

interface RunbookData {
  runbook: Runbook;
  executions: RunbookExecution[];
}

/**
 * One runbook (the dashboard's /runbooks/:id): binding, overview, parameters, steps, and executions, with Start
 * Execution (prefilled from a handed-over context), execution progress (step completion, step notes, notes, Save
 * Progress, Mark Completed, Cancel Execution, Delete Execution), Run Check, View JSON, Edit, Duplicate, and Delete.
 * Editing follows the runbook's scope (canEdit). Live: reloads on runbook-execution.* events.
 */
export function RunbookDetailView({ id, prefill = null, executionId, embedded, onDeleted, onChanged }: RunbookDetailViewProps) {
  const { t, formatDateTime, formatRelativeTime } = useLocale();
  const { pushToast } = useNotifications();
  const router = useRouter();
  const viewer = useScopeViewer();
  const profiles = useWorkflowProfiles();
  const profileNames = useNameMap(profiles);
  const environments = useEnvironments();
  const { confirm, dialog } = useConfirm('runbook-confirm');
  const [editing, setEditing] = useState(false);
  const [starting, setStarting] = useState(false);
  const [jsonOpen, setJsonOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const [parameter, setParameter] = useState<{ index: number; value: RunbookParameter } | null>(null);
  const [step, setStep] = useState<{ index: number; value: RunbookStep } | null>(null);
  const [selectedId, setSelectedId] = useState(executionId || '');

  const { data, loading, refreshing, error, reload, refresh, setData } = useLoad<RunbookData>(async () => {
    const [runbook, executions] = await Promise.all([getRunbook(id), listRunbookExecutions({ ...ALL, runbookId: id })]);
    return { runbook, executions: executions.objects ?? [] };
  }, [id], { live: ['runbook-execution.'], fallbackError: t('Failed to load runbook.') });
  useReloadOnFocus(reload);

  if (!data) return <DetailPending loading={loading} error={error} onRetry={() => void reload()} />;
  const r = data.runbook;
  const canManage = canEditScoped(viewer, r);
  const selected = data.executions.find((x) => x.id === selectedId) || data.executions[0] || null;
  const go = (href: string) => router.push(href as Href);
  const environmentFor = (envId: string | null | undefined) => (envId ? environments.find((e) => e.id === envId) || null : null);
  const selectedEnvironment = environmentFor(selected?.environmentId) || environmentFor(r.environmentId);
  const effectiveCheckType = selected?.checkType || r.defaultCheckType;
  const canLaunchCheck = !!(selectedEnvironment?.vesselId && effectiveCheckType);

  async function saveRunbook(parameters: RunbookParameter[], steps: RunbookStep[]) {
    const updated = await updateRunbook(r.id, runbookPayload(runbookValues(r), parameters, steps));
    setData({ ...data!, runbook: updated });
    pushToast('success', t('Runbook "{{title}}" saved.', { title: updated.title }));
    onChanged?.();
  }

  function remove() {
    confirm({
      title: t('Delete Runbook'),
      message: t('Delete "{{title}}"? This removes the runbook definition and keeps existing executions only in the event log history.', { title: r.title }),
      confirmLabel: t('Delete'),
      danger: true,
      onConfirm: async () => {
        try {
          await deleteRunbook(r.id);
          pushToast('warning', t('Runbook "{{title}}" deleted.', { title: r.title }));
          if (onDeleted) onDeleted();
          else router.back();
        } catch (err: unknown) {
          pushToast('error', errorText(err, t('Delete failed.')));
        }
      },
    });
  }

  async function duplicate() {
    setBusy(true);
    try {
      const created = await createRunbook(buildRunbookDuplicatePayload(r));
      pushToast('success', t('Runbook "{{title}}" duplicated.', { title: created.title }));
      onChanged?.();
      go(`/runbooks/${created.id}${prefillQuery(executionPrefillQuery(prefill))}`);
    } catch (err: unknown) {
      pushToast('error', errorText(err, t('Duplicate failed.')));
    } finally {
      setBusy(false);
    }
  }

  async function selectExecution(x: RunbookExecution) {
    setSelectedId(x.id);
    try {
      const fresh = await getRunbookExecution(x.id);
      setData({ ...data!, executions: data!.executions.map((e) => (e.id === fresh.id ? fresh : e)) });
    } catch {
      // The listed copy is still shown; the next live reload refreshes it.
    }
  }

  function removeList<T>(items: T[], index: number): T[] {
    return items.filter((_, i) => i !== index);
  }

  const launchCheck = () => {
    if (!selectedEnvironment?.vesselId || !effectiveCheckType) return;
    go(`/delivery${prefillQuery({
      tab: 'checks', run: '1', vesselId: selectedEnvironment.vesselId, workflowProfileId: selected?.workflowProfileId || r.workflowProfileId,
      deploymentId: selected?.deploymentId || prefill?.deploymentId, type: effectiveCheckType,
      environmentName: selected?.environmentName || r.environmentName || selectedEnvironment.name, label: selected?.title || r.title,
    })}`);
  };

  return (
    <DetailBody embedded={embedded} refreshing={refreshing} onRefresh={() => void refresh()} testID="runbook-detail">
      {!embedded ? <Stack.Screen options={{ title: r.title }} /> : null}
      <DetailHeader title={r.title} subtitle={r.id} testID="runbook-title" badges={<StatusBadge label={r.active ? t('Active') : t('Inactive')} tone={r.active ? 'success' : 'cancelled'} />} />
      {prefill ? <Banner tone="info" title={t('An incident or deployment handed off a prefilled runbook execution context. Open a runbook to start the execution with those defaults.')} /> : null}
      <ActionBar>
        <Button label={t('Start Execution')} icon="play" style={resourceStyles.action} onPress={() => setStarting(true)} testID="runbook-start" />
        {canLaunchCheck ? <Button label={t('Run Check')} variant="secondary" style={resourceStyles.action} onPress={launchCheck} /> : null}
        <Button label={t('View JSON')} variant="ghost" style={resourceStyles.action} onPress={() => setJsonOpen(true)} />
        {canManage ? <Button label={t('Edit')} variant="secondary" icon="create-outline" style={resourceStyles.action} onPress={() => setEditing(true)} testID="runbook-edit" /> : null}
        {canManage ? <Button label={t('Duplicate')} variant="ghost" busy={busy} style={resourceStyles.action} onPress={() => void duplicate()} /> : null}
        {canManage ? <Button label={t('Delete')} variant="danger" style={resourceStyles.action} onPress={remove} testID="runbook-delete" /> : null}
      </ActionBar>

      <FieldCard title={t('Overview')}>
        <Field label={t('File Name')} value={r.fileName} mono />
        <Field label={t('Workflow Profile')} value={r.workflowProfileId ? (profileNames.get(r.workflowProfileId) || r.workflowProfileId) : null} />
        <Field label={t('Environment')} value={environmentFor(r.environmentId)?.name || r.environmentName} onPress={r.environmentId ? () => go(`/environments/${r.environmentId}`) : undefined} />
        <Field label={t('Default Check')} value={r.defaultCheckType} />
        <Field label={t('Parameters')} value={r.parameters.length} />
        <Field label={t('Steps')} value={r.steps.length} />
        <Field label={t('Executions')} value={data.executions.length} />
        <Field label={t('Visibility')} value={t(scopeLabel(r.scope))} />
        <Field label={t('Created')} value={formatDateTime(r.createdUtc)} />
        <Field label={t('Last Updated')} value={formatRelativeTime(r.lastUpdateUtc)} />
        <Field label={t('Playbook ID')} value={r.playbookId} mono onPress={() => go(`/playbooks/${r.playbookId}`)} />
      </FieldCard>
      <TextBlock title={t('Description')} text={r.description} />
      <TextBlock title={t('Overview Markdown')} text={r.overviewMarkdown} />

      <FieldCard title={t('Parameters')}>
        {r.parameters.length === 0 ? <AppText muted style={resourceStyles.pad}>{t('No parameters defined.')}</AppText> : r.parameters.map((p, index) => (
          <ResourceRow
            key={`${p.name}:${index}`}
            title={p.label || p.name}
            subtitle={[p.name, p.defaultValue, p.description].filter(Boolean).join(' \u2022 ')}
            meta={p.required ? t('Required') : null}
            onPress={canManage ? () => setParameter({ index, value: p }) : undefined}
            actions={canManage ? [{ key: 'remove', label: t('Remove'), icon: 'trash-outline', tone: 'danger', onPress: () => void saveRunbook(removeList(r.parameters, index), r.steps).catch((err: unknown) => pushToast('error', errorText(err, t('Save failed.')))) }] : []}
          />
        ))}
      </FieldCard>
      {canManage ? <Button label={t('Add Parameter')} variant="secondary" icon="add" style={resourceStyles.create} onPress={() => setParameter({ index: -1, value: createDefaultParameter() })} testID="runbook-add-parameter" /> : null}

      <FieldCard title={t('Steps')} testID="runbook-steps">
        {r.steps.length === 0 ? <AppText muted style={resourceStyles.pad}>{t('No steps defined yet.')}</AppText> : r.steps.map((s, index) => (
          <ResourceRow
            key={s.id}
            title={`${index + 1}. ${s.title}`}
            subtitle={s.instructions}
            onPress={canManage ? () => setStep({ index, value: s }) : undefined}
            actions={canManage ? [{ key: 'remove', label: t('Remove'), icon: 'trash-outline', tone: 'danger', onPress: () => void saveRunbook(r.parameters, removeList(r.steps, index)).catch((err: unknown) => pushToast('error', errorText(err, t('Save failed.')))) }] : []}
          />
        ))}
      </FieldCard>
      {canManage ? <Button label={t('Add Step')} variant="secondary" icon="add" style={resourceStyles.create} onPress={() => setStep({ index: -1, value: createDefaultStep() })} testID="runbook-add-step" /> : null}

      <FieldCard title={t('Executions')} testID="runbook-executions">
        {data.executions.length === 0 ? <AppText muted style={resourceStyles.pad}>{t('No executions recorded yet.')}</AppText> : data.executions.map((x) => (
          <ResourceRow
            key={x.id}
            testID={`runbook-execution-${x.id}`}
            title={x.title}
            subtitle={`${x.environmentName || t('No environment')} \u2022 ${x.checkType || t('No check type')} \u2022 ${x.completedStepIds.length}/${r.steps.length} ${t('steps')}`}
            badge={statusBadge(t, x.status)}
            selected={selected?.id === x.id}
            onPress={() => void selectExecution(x)}
          />
        ))}
      </FieldCard>
      {selected ? (
        <ExecutionProgress
          key={`${selected.id}:${selected.lastUpdateUtc}`}
          execution={selected}
          steps={r.steps}
          onSaved={(updated) => { setData({ ...data, executions: data.executions.map((e) => (e.id === updated.id ? updated : e)) }); onChanged?.(); }}
          onDeleted={(deletedId) => { setData({ ...data, executions: data.executions.filter((e) => e.id !== deletedId) }); setSelectedId(''); onChanged?.(); }}
        />
      ) : null}

      <FormSheet
        testID="runbook-form"
        open={editing}
        title={t('Runbook')}
        initial={runbookValues(r)}
        fields={() => runbookEditFields(t, profiles, environments)}
        onChange={(next, prev) => linkRunbookValues(next, prev, environments)}
        submitLabel={t('Save Runbook')}
        onClose={() => setEditing(false)}
        onSubmit={async (values) => {
          const updated = await updateRunbook(r.id, runbookPayload(values, r.parameters, r.steps));
          setData({ ...data, runbook: updated });
          setEditing(false);
          pushToast('success', t('Runbook "{{title}}" saved.', { title: updated.title }));
          onChanged?.();
        }}
      />
      <FormSheet
        testID="runbook-parameter-form"
        open={parameter !== null}
        title={t('Parameters')}
        initial={parameter ? parameterValues(parameter.value) : {}}
        fields={() => parameterFields(t)}
        submitLabel={t('Save Runbook')}
        onClose={() => setParameter(null)}
        onSubmit={async (values) => {
          if (!parameter) return;
          const next = applyParameter(values);
          const parameters = parameter.index < 0 ? [...r.parameters, next] : r.parameters.map((p, i) => (i === parameter.index ? next : p));
          await saveRunbook(parameters, r.steps);
          setParameter(null);
        }}
      />
      <FormSheet
        testID="runbook-step-form"
        open={step !== null}
        title={t('Steps')}
        initial={step ? { title: step.value.title, instructions: step.value.instructions } : {}}
        fields={() => stepFields(t)}
        submitLabel={t('Save Runbook')}
        onClose={() => setStep(null)}
        onSubmit={async (values) => {
          if (!step) return;
          const next: RunbookStep = { ...step.value, title: str(values, 'title'), instructions: str(values, 'instructions') };
          const steps = step.index < 0 ? [...r.steps, next] : r.steps.map((s, i) => (i === step.index ? next : s));
          await saveRunbook(r.parameters, steps);
          setStep(null);
        }}
      />
      <FormSheet
        testID="runbook-start-form"
        open={starting}
        title={t('Start Execution')}
        initial={startExecutionValues(r, prefill)}
        fields={() => startExecutionFields(t, r, profiles, environments)}
        onChange={(next, prev) => linkRunbookValues(next, prev, environments)}
        submitLabel={t('Start Execution')}
        onClose={() => setStarting(false)}
        onSubmit={async (values) => {
          const execution = await startRunbookExecution(r.id, startExecutionPayload(r, values, prefill));
          const refreshed = await listRunbookExecutions({ ...ALL, runbookId: r.id });
          setData({ ...data, executions: refreshed.objects ?? [] });
          setSelectedId(execution.id);
          setStarting(false);
          pushToast('success', t('Execution "{{title}}" started.', { title: execution.title }));
          onChanged?.();
        }}
      />
      <JsonSheet open={jsonOpen} title={r.title} data={r} onClose={() => setJsonOpen(false)} />
      {dialog}
    </DetailBody>
  );
}

/** Progress of one execution: step completion and notes, execution notes, and the save / complete / cancel / delete actions. */
function ExecutionProgress({ execution, steps, onSaved, onDeleted }: {
  execution: RunbookExecution;
  steps: RunbookStep[];
  onSaved: (updated: RunbookExecution) => void;
  onDeleted: (id: string) => void;
}) {
  const { t, formatDateTime, formatRelativeTime } = useLocale();
  const { pushToast } = useNotifications();
  const router = useRouter();
  const { confirm, dialog } = useConfirm('runbook-execution-confirm');
  const [draft, setDraft] = useState<RunbookExecution>(() => cloneExecution(execution));
  const [saving, setSaving] = useState(false);

  async function save(statusOverride?: RunbookExecutionStatus) {
    setSaving(true);
    try {
      const updated = await updateRunbookExecution(draft.id, {
        status: statusOverride || draft.status,
        completedStepIds: draft.completedStepIds,
        stepNotes: draft.stepNotes,
        notes: draft.notes || null,
      });
      pushToast('success', t('Execution "{{title}}" updated.', { title: updated.title }));
      onSaved(updated);
    } catch (err: unknown) {
      pushToast('error', errorText(err, t('Failed to update execution.')));
    } finally {
      setSaving(false);
    }
  }

  function remove() {
    confirm({
      title: t('Delete Execution'),
      message: t('Delete execution "{{title}}"?', { title: draft.title }),
      confirmLabel: t('Delete'),
      danger: true,
      onConfirm: async () => {
        try {
          await deleteRunbookExecution(draft.id);
          pushToast('warning', t('Execution "{{title}}" deleted.', { title: draft.title }));
          onDeleted(draft.id);
        } catch (err: unknown) {
          pushToast('error', errorText(err, t('Delete failed.')));
        }
      },
    });
  }

  const toggle = (stepId: string, checked: boolean) => setDraft((d) => ({
    ...d,
    completedStepIds: checked ? Array.from(new Set([...d.completedStepIds, stepId])) : d.completedStepIds.filter((v) => v !== stepId),
  }));

  return (
    <>
      <FieldCard title={t('Execution Progress')} testID="runbook-progress">
        <Field label={t('Status')} value={<StatusBadge {...statusBadge(t, draft.status)} />} />
        <Field label={t('Environment')} value={draft.environmentName} />
        <Field label={t('Check Type')} value={draft.checkType} />
        <Field label={t('Deployment')} value={draft.deploymentId} mono onPress={draft.deploymentId ? () => router.push(`/deployments/${draft.deploymentId}` as Href) : undefined} />
        <Field label={t('Incident')} value={draft.incidentId} mono onPress={draft.incidentId ? () => router.push(`/incidents/${draft.incidentId}` as Href) : undefined} />
        <Field label={t('Started')} value={formatDateTime(draft.startedUtc)} />
        <Field label={t('Completed')} value={draft.completedUtc ? formatDateTime(draft.completedUtc) : null} />
        <Field label={t('Last Updated')} value={formatRelativeTime(draft.lastUpdateUtc)} />
        <View style={styles.pad}>
          {steps.map((s, index) => (
            <View key={s.id}>
              <SwitchField label={`${index + 1}. ${s.title}`} hint={s.instructions} value={draft.completedStepIds.includes(s.id)} onChange={(v) => toggle(s.id, v)} testID={`runbook-step-done-${index}`} />
              <TextField label={t('Step Notes')} value={draft.stepNotes[s.id] || ''} onChangeText={(v) => setDraft((d) => ({ ...d, stepNotes: { ...d.stepNotes, [s.id]: v } }))} multiline />
            </View>
          ))}
          <TextField label={t('Execution Notes')} value={draft.notes || ''} onChangeText={(v) => setDraft((d) => ({ ...d, notes: v }))} multiline testID="runbook-execution-notes" />
        </View>
      </FieldCard>
      <ActionBar>
        <Button label={saving ? t('Saving...') : t('Save Progress')} busy={saving} style={resourceStyles.action} onPress={() => void save()} testID="runbook-save-progress" />
        <Button label={t('Mark Completed')} variant="secondary" disabled={saving} style={resourceStyles.action} onPress={() => void save('Completed')} testID="runbook-mark-completed" />
        <Button label={t('Cancel Execution')} variant="secondary" disabled={saving} style={resourceStyles.action} onPress={() => void save('Cancelled')} />
        <Button label={t('Delete Execution')} variant="danger" disabled={saving} style={resourceStyles.action} onPress={remove} />
      </ActionBar>
      {dialog}
    </>
  );
}

/** The /runbooks/:id route (?executionId= picks the execution; ?exec...= carries an execution prefill). */
export function RunbookDetailRoute() {
  const params = useLocalSearchParams<Record<string, string>>();
  if (params.id === 'new') return <RunbookCreateScreen prefill={executionPrefillFrom(params)} />;
  return <RunbookDetailView id={params.id} prefill={executionPrefillFrom(params)} executionId={param(params.executionId) || undefined} />;
}

/** /runbooks/new: the create form (the dashboard's create mode); saving opens the runbook with the prefill carried on. */
function RunbookCreateScreen({ prefill }: { prefill: ExecutionPrefill | null }) {
  const { t } = useLocale();
  const router = useRouter();
  const { pushToast } = useNotifications();
  const viewer = useScopeViewer();
  const profiles = useWorkflowProfiles();
  const environments = useEnvironments();
  return (
    <DetailBody testID="runbook-create">
      <Stack.Screen options={{ title: t('Create Runbook') }} />
      <FormSheet
        testID="runbook-form"
        open
        title={t('Create Runbook')}
        initial={newRunbookValues(resolveCreateScope(viewer))}
        fields={() => runbookCreateFields(t, viewer, profiles, environments)}
        submitLabel={t('Create Runbook')}
        onClose={() => router.back()}
        onSubmit={async (values) => {
          const created = await createRunbook(runbookCreatePayload(values, environments));
          pushToast('success', t('Runbook "{{title}}" created.', { title: created.title }));
          router.replace(`/runbooks/${created.id}${prefillQuery(executionPrefillQuery(prefill))}` as Href);
        }}
      />
    </DetailBody>
  );
}

const styles = StyleSheet.create({
  pad: { padding: spacing.md },
});

import { useLocalSearchParams, useRouter, type Href } from 'expo-router';
import { useMemo, useState } from 'react';
import { createRunbook, deleteRunbook, listRunbookExecutions, listRunbooks } from '@dashboard/api/client';
import type { Runbook, RunbookExecution } from '@dashboard/types/models';
import { buildRunbookDuplicatePayload } from '@dashboard/lib/duplicates';
import { canEdit as canEditScoped, resolveCreateScope, scopeLabel } from '@dashboard/lib/scoping';
import { useAuth } from '../../auth/AuthContext';
import { JsonSheet } from '../../components/resource/DetailParts';
import { FormSheet } from '../../components/resource/FormSheet';
import { MasterDetail, useSelection } from '../../components/resource/Hub';
import { ResourceList, StatRow } from '../../components/resource/ResourceList';
import { ResourceRow, useConfirm } from '../../components/resource/ResourceRow';
import { resourceStyles } from '../../components/resource/styles';
import { Banner } from '../../components/ui/Banner';
import { Button } from '../../components/ui/Button';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { prefillQuery } from '../../resource/links';
import { ALL } from '../../resource/lookups';
import { errorText, useLoad, useReloadOnFocus } from '../../resource/useLoad';
import { useEnvironments, useWorkflowProfiles } from './deliveryLookups';
import { RunbookDetailView } from './RunbookDetail';
import { executionPrefillFrom, executionPrefillQuery, newRunbookValues, runbookCreateFields, runbookCreatePayload, useScopeViewer } from './runbookForm';

interface RunbookData {
  runbooks: Runbook[];
  executions: RunbookExecution[];
}

/**
 * Delivery > Runbooks: runbooks with summary counts, search, state filter, execution counts, create (everyone;
 * visibility chosen by admins), and row actions (open, duplicate, view JSON, delete when the scope allows it). An
 * execution prefill handed over by an incident, deployment, or environment (?exec...=) is carried into the runbook
 * that is opened. Live: reloads on runbook-execution.* events.
 */
export function RunbooksTab() {
  const { t, formatRelativeTime } = useLocale();
  const { pushToast } = useNotifications();
  const router = useRouter();
  const params = useLocalSearchParams<Record<string, string>>();
  const prefill = executionPrefillFrom(params);
  const carry = prefillQuery(executionPrefillQuery(prefill));
  const { isAdmin, isTenantAdmin } = useAuth();
  const canManage = isAdmin || isTenantAdmin;
  const viewer = useScopeViewer();
  const profiles = useWorkflowProfiles();
  const environments = useEnvironments();
  const { confirm, dialog } = useConfirm('runbook-confirm');
  const selection = useSelection((id) => `/runbooks/${id}${carry}`);

  const { data, loading, refreshing, error, reload, refresh } = useLoad<RunbookData>(async () => {
    const [runbooks, executions] = await Promise.all([listRunbooks(ALL), listRunbookExecutions(ALL)]);
    return { runbooks: runbooks.objects ?? [], executions: executions.objects ?? [] };
  }, [], { live: ['runbook-execution.'], fallbackError: t('Failed to load runbooks.') });
  useReloadOnFocus(reload);
  const runbooks = useMemo(() => data?.runbooks ?? [], [data]);
  const executions = useMemo(() => data?.executions ?? [], [data]);
  const counts = useMemo(() => {
    const map = new Map<string, { total: number; running: number }>();
    for (const x of executions) {
      const c = map.get(x.runbookId) || { total: 0, running: 0 };
      c.total += 1;
      if (x.status === 'Running') c.running += 1;
      map.set(x.runbookId, c);
    }
    return map;
  }, [executions]);

  const [search, setSearch] = useState('');
  const [state, setState] = useState('all');
  const [creating, setCreating] = useState(false);
  const [json, setJson] = useState<Runbook | null>(null);

  const filtered = useMemo(() => {
    const term = search.trim().toLowerCase();
    return runbooks.filter((r) => (!term || [r.title, r.fileName, r.description, r.environmentName, r.id].some((v) => (v ?? '').toLowerCase().includes(term)))
      && (state === 'all' || (state === 'active' ? r.active : !r.active)));
  }, [runbooks, search, state]);

  function remove(r: Runbook) {
    confirm({
      title: t('Delete Runbook'),
      message: t('Delete "{{title}}"? This removes the runbook definition but does not touch deployments, incidents, or completed check runs.', { title: r.title }),
      confirmLabel: t('Delete'),
      danger: true,
      onConfirm: async () => {
        try {
          await deleteRunbook(r.id);
          pushToast('warning', t('Runbook "{{title}}" deleted.', { title: r.title }));
          if (selection.selected === r.id) selection.clear();
          await reload();
        } catch (err: unknown) {
          pushToast('error', errorText(err, t('Delete failed.')));
        }
      },
    });
  }

  async function duplicate(r: Runbook) {
    try {
      const created = await createRunbook(buildRunbookDuplicatePayload(r));
      pushToast('success', t('Runbook "{{title}}" duplicated.', { title: created.title }));
      await reload();
      router.push(`/runbooks/${created.id}${carry}` as Href);
    } catch (err: unknown) {
      pushToast('error', errorText(err, t('Duplicate failed.')));
    }
  }

  const envName = (r: Runbook) => (r.environmentId ? (environments.find((e) => e.id === r.environmentId)?.name || r.environmentName || r.environmentId) : (r.environmentName || t('No environment')));
  const list = (
    <ResourceList
      testID="runbooks"
      items={filtered}
      keyOf={(r) => r.id}
      loading={loading}
      error={error}
      onRetry={() => void reload()}
      refreshing={refreshing}
      onRefresh={() => void refresh()}
      search={{ value: search, onChange: setSearch, placeholder: t('Search by title, file name, description, environment, or ID...') }}
      filters={[
        { key: 'state', label: t('State'), value: state, onChange: setState, options: [{ value: 'all', label: t('All states') }, { value: 'active', label: t('Active') }, { value: 'inactive', label: t('Inactive') }] },
      ]}
      header={(
        <>
          {prefill ? <Banner tone="info" title={t('An incident or deployment handed off a prefilled runbook execution context. Open a runbook to start the execution with those defaults.')} testID="runbooks-prefill" /> : null}
          <StatRow stats={[
            { label: t('Total Runbooks'), value: runbooks.length },
            { label: t('Active'), value: runbooks.filter((r) => r.active).length },
            { label: t('Executions'), value: executions.length },
            { label: t('Running'), value: executions.filter((x) => x.status === 'Running').length },
          ]} />
          <Button label={t('Create Runbook')} icon="add" onPress={() => setCreating(true)} style={resourceStyles.create} testID="runbooks-create" />
        </>
      )}
      emptyTitle={t('No runbooks match the current filters.')}
      emptyMessage={canManage ? t('Create a runbook to guide release, deploy, rollback, migration, or incident work step by step.') : t('Ask a tenant administrator to create and manage runbooks.')}
      renderItem={(r) => {
        const c = counts.get(r.id) || { total: 0, running: 0 };
        return (
          <ResourceRow
            testID={`runbook-row-${r.id}`}
            title={r.title}
            subtitle={[envName(r), `${r.steps.length} ${t('steps')} \u2022 ${r.parameters.length} ${t('parameters')}`, `${c.total} ${t('total')} \u2022 ${c.running} ${t('running')}`, t(scopeLabel(r.scope))].join(' \u2022 ')}
            badge={{ label: r.active ? t('Active') : t('Inactive'), tone: r.active ? 'success' : 'cancelled' }}
            meta={formatRelativeTime(r.lastUpdateUtc)}
            selected={selection.selected === r.id}
            onPress={() => selection.open(r.id)}
            actions={[
              { key: 'duplicate', label: t('Duplicate'), icon: 'copy-outline', onPress: () => void duplicate(r) },
              { key: 'json', label: t('View JSON'), icon: 'code-outline', onPress: () => setJson(r) },
              ...(canEditScoped(viewer, r) ? [{ key: 'delete', label: t('Delete'), icon: 'trash-outline' as const, tone: 'danger' as const, onPress: () => remove(r) }] : []),
            ]}
          />
        );
      }}
    />
  );

  return (
    <>
      <MasterDetail
        list={list}
        detail={selection.selected ? <RunbookDetailView key={selection.selected} id={selection.selected} prefill={prefill} embedded onDeleted={() => { selection.clear(); void reload(); }} onChanged={() => void reload()} /> : null}
        onBack={selection.clear}
      />
      <FormSheet
        testID="runbook-form"
        open={creating}
        title={t('Create Runbook')}
        initial={newRunbookValues(resolveCreateScope(viewer))}
        fields={() => runbookCreateFields(t, viewer, profiles, environments)}
        submitLabel={t('Create Runbook')}
        onClose={() => setCreating(false)}
        onSubmit={async (values) => {
          const created = await createRunbook(runbookCreatePayload(values, environments));
          pushToast('success', t('Runbook "{{title}}" created.', { title: created.title }));
          setCreating(false);
          await reload();
        }}
      />
      <JsonSheet open={json !== null} title={json?.title ?? ''} data={json} onClose={() => setJson(null)} />
      {dialog}
    </>
  );
}

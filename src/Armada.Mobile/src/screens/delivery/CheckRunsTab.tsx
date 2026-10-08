import { useLocalSearchParams, useRouter, type Href } from 'expo-router';
import { useMemo, useState } from 'react';
import { listCheckRuns } from '@dashboard/api/client';
import type { CheckRun, CheckRunRequest } from '@dashboard/types/models';
import { ALL_CHECK_TYPES, summarizeRunParsing } from '@dashboard/lib/deliveryForms';
import { buildCheckRunComparisonMap, formatCheckRunComparisonScope, formatCheckRunComparisonSummary } from '@dashboard/lib/checkRunComparison';
import { JsonSheet } from '../../components/resource/DetailParts';
import { MasterDetail, useSelection } from '../../components/resource/Hub';
import { ResourceList, StatRow } from '../../components/resource/ResourceList';
import { ResourceRow } from '../../components/resource/ResourceRow';
import { resourceStyles } from '../../components/resource/styles';
import { Button } from '../../components/ui/Button';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { ALL, useNameMap, useVessels, valueOptions } from '../../resource/lookups';
import { statusBadge } from '../../resource/status';
import { useLoad, useReloadOnFocus } from '../../resource/useLoad';
import { CheckRunDetailView } from './CheckRunDetail';
import { checkPrefillFrom, draftReleaseLink } from './checkLinks';
import { useWorkflowProfiles } from './deliveryLookups';
import { RunCheckSheet } from './RunCheckSheet';

/**
 * Delivery > Checks: structured check runs with summary counts, vessel / status / source / type filters, parsed
 * results and the comparison to the previous comparable run, Run Check (prefilled from ?run=1&... links), and row
 * actions (open, Draft Release, view JSON). Live: reloads on check-run.* events.
 */
export function CheckRunsTab() {
  const { t, formatRelativeTime } = useLocale();
  const { pushToast } = useNotifications();
  const router = useRouter();
  const params = useLocalSearchParams<Record<string, string>>();
  const vessels = useVessels();
  const vesselNames = useNameMap(vessels);
  const profiles = useWorkflowProfiles();
  const selection = useSelection((id) => `/checks/${id}`);

  const { data, loading, refreshing, error, reload, refresh } = useLoad(async () => (await listCheckRuns(ALL)).objects ?? [], [], { live: ['check-run.'], fallbackError: t('Failed to load check runs.') });
  useReloadOnFocus(reload);
  const runs = useMemo(() => data ?? [], [data]);
  const comparisons = useMemo(() => buildCheckRunComparisonMap(runs), [runs]);

  const [vessel, setVessel] = useState('all');
  const [status, setStatus] = useState('all');
  const [source, setSource] = useState('all');
  const [type, setType] = useState('all');
  const [json, setJson] = useState<CheckRun | null>(null);

  // A Run Check link (?run=1&...) opens the sheet once with its prefill, like the dashboard's router state.
  const linkPrefill = checkPrefillFrom(params);
  const linkKey = linkPrefill ? JSON.stringify(linkPrefill) : '';
  const [consumedKey, setConsumedKey] = useState(linkKey);
  const [sheet, setSheet] = useState<{ prefill: Partial<CheckRunRequest> | null } | null>(linkPrefill ? { prefill: linkPrefill } : null);
  if (linkKey && linkKey !== consumedKey) {
    setConsumedKey(linkKey);
    setSheet({ prefill: linkPrefill });
  }

  const filtered = useMemo(() => runs.filter((r) => (vessel === 'all' || r.vesselId === vessel)
    && (status === 'all' || r.status === status)
    && (source === 'all' || r.source === source)
    && (type === 'all' || r.type === type)), [runs, vessel, status, source, type]);

  function subtitle(run: CheckRun): string {
    const comparison = comparisons.get(run.id);
    return [
      run.label ? run.type : null,
      summarizeRunParsing(run) || null,
      comparison ? `vs ${formatCheckRunComparisonScope(comparison.scope)}: ${formatCheckRunComparisonSummary(comparison)}` : null,
      run.vesselId ? (vesselNames.get(run.vesselId) || run.vesselId) : null,
      run.environmentName,
      run.source === 'External' ? `${run.source}${run.providerName ? ` / ${run.providerName}` : ''}` : null,
      run.durationMs != null ? `${Math.round(run.durationMs)} ms` : null,
    ].filter(Boolean).join(' \u2022 ');
  }

  const list = (
    <ResourceList
      testID="checks"
      items={filtered}
      keyOf={(r) => r.id}
      loading={loading}
      error={error}
      onRetry={() => void reload()}
      refreshing={refreshing}
      onRefresh={() => void refresh()}
      filters={[
        { key: 'vessel', label: t('Vessel'), value: vessel, onChange: setVessel, options: [{ value: 'all', label: t('All vessels') }, ...vessels.map((v) => ({ value: v.id, label: v.name }))] },
        { key: 'status', label: t('Status'), value: status, onChange: setStatus, options: [{ value: 'all', label: t('All statuses') }, ...valueOptions(['Passed', 'Failed', 'Running', 'Pending', 'Canceled'], t)] },
        { key: 'source', label: t('Source'), value: source, onChange: setSource, options: [{ value: 'all', label: t('All sources') }, ...valueOptions(['Armada', 'External'], t)] },
        { key: 'type', label: t('Type'), value: type, onChange: setType, options: [{ value: 'all', label: t('All check types') }, ...valueOptions(ALL_CHECK_TYPES)] },
      ]}
      header={(
        <>
          <StatRow stats={[
            { label: t('Total Runs'), value: runs.length },
            { label: t('Passed'), value: runs.filter((r) => r.status === 'Passed').length, tone: 'success' },
            { label: t('Failed'), value: runs.filter((r) => r.status === 'Failed').length, tone: 'danger' },
            { label: t('Running'), value: runs.filter((r) => r.status === 'Running').length },
          ]} />
          <Button label={t('Run Check')} icon="play" onPress={() => setSheet({ prefill: null })} style={resourceStyles.create} testID="checks-run" />
        </>
      )}
      emptyTitle={t('No check runs match the current filters.')}
      emptyMessage={t('Run a structured check to capture build, test, or deploy evidence for a vessel.')}
      renderItem={(run) => (
        <ResourceRow
          testID={`check-row-${run.id}`}
          title={run.label || run.type}
          subtitle={subtitle(run)}
          badge={statusBadge(t, run.status)}
          meta={formatRelativeTime(run.createdUtc)}
          selected={selection.selected === run.id}
          onPress={() => selection.open(run.id)}
          actions={[
            { key: 'release', label: t('Draft Release'), icon: 'pricetag-outline', onPress: () => router.push(draftReleaseLink(run) as Href) },
            { key: 'json', label: t('View JSON'), icon: 'code-outline', onPress: () => setJson(run) },
          ]}
        />
      )}
    />
  );

  return (
    <>
      <MasterDetail
        list={list}
        detail={selection.selected ? <CheckRunDetailView key={selection.selected} id={selection.selected} embedded onDeleted={() => { selection.clear(); void reload(); }} onChanged={() => void reload()} /> : null}
      />
      <RunCheckSheet
        open={sheet !== null}
        prefill={sheet?.prefill ?? null}
        vessels={vessels}
        profiles={profiles}
        onClose={() => setSheet(null)}
        onRan={(run) => {
          pushToast(run.status === 'Passed' ? 'success' : run.status === 'Failed' ? 'warning' : 'info', t('Check run "{{id}}" completed with status {{status}}.', { id: run.id, status: run.status }));
          setSheet(null);
          void reload();
          router.push(`/checks/${run.id}` as Href);
        }}
      />
      <JsonSheet open={json !== null} title={json?.label || json?.id || ''} data={json} onClose={() => setJson(null)} />
    </>
  );
}

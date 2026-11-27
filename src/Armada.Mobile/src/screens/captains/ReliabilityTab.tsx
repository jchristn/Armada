import { useCallback, useRef, useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { getGateReport, getReliabilitySummary } from '@dashboard/api/client';
import { formatDurationMs, formatRate, summarizeKinds, windowStartUtc } from '@dashboard/lib/reliability';
import type { GateReport, ReliabilitySort, ReliabilitySummary, ReliabilitySummaryRow } from '@dashboard/types/reliability';
import { PagedList } from '../../components/app/PagedList';
import { AppText, ListRow, SearchField, Section, SegmentedControl, SelectField, StatusBadge, SwitchRow } from '../../components/ui';
import { KeyValueRow } from '../../components/ui/KeyValueRow';
import { usePagedList } from '../../data/usePagedList';
import type { PageResult } from '../../data/pagedList';
import { useQuery } from '../../data/useQuery';
import { useLocale } from '../../i18n/LocaleContext';
import { spacing } from '../../theme/typography';

/** Days the summary covers (the dashboard's choices; the server allows at most 90). */
export const RELIABILITY_WINDOWS = [7, 14, 30, 90];

/** Sort choices, in the dashboard's column order, with the direction each starts in. */
export const RELIABILITY_SORTS: { value: ReliabilitySort; label: string; descending: boolean }[] = [
  { value: 'Attempts', label: 'Attempts', descending: true },
  { value: 'CompletionRate', label: 'Completion', descending: false },
  { value: 'Failures', label: 'Failed', descending: true },
  { value: 'Repairs', label: 'Repairs', descending: true },
  { value: 'Escalations', label: 'Escalations', descending: true },
  { value: 'UnclassifiedRate', label: 'Unclassified', descending: true },
  { value: 'Model', label: 'Model', descending: false },
  { value: 'Persona', label: 'Persona', descending: false },
];

/** Pure: the summary as one page for the endless list. */
export function summaryPage(summary: ReliabilitySummary): PageResult<ReliabilitySummaryRow> {
  return { objects: summary.rows, totalRecords: summary.totalRecords, totalPages: summary.totalPages, pageNumber: summary.pageNumber };
}

/** Pure: a row's stable key (model and persona). */
export function reliabilityRowKey(r: Pick<ReliabilitySummaryRow, 'model' | 'persona'>): string {
  return `${r.model}|${r.persona}`;
}

/** Pure: the badge tone for a completion rate (no attempts reads as neutral). */
export function completionTone(rate: number | null | undefined, attempts: number): 'success' | 'warning' | 'error' | 'pending' {
  if (!attempts || rate === null || rate === undefined) return 'pending';
  if (rate >= 0.9) return 'success';
  return rate >= 0.6 ? 'warning' : 'error';
}

/**
 * The Reliability tab of the Captains hub (the dashboard's CaptainsHub `?tab=reliability`): completion, failure
 * kinds, repairs, escalations and the Unclassified rate per model and persona over a recent window, from recorded
 * stage outcomes (GET /api/v1/reliability/summary). The Admiral filters, sorts and pages. Administrators also see the
 * latest acceptance gate report.
 */
export function ReliabilityTab() {
  const { t, formatDateTime } = useLocale();
  const [days, setDays] = useState(14);
  const [includeEvaluation, setIncludeEvaluation] = useState(false);
  const [sort, setSort] = useState<ReliabilitySort>('Attempts');
  const [model, setModel] = useState('');
  const [persona, setPersona] = useState('');
  const [totals, setTotals] = useState<ReliabilitySummary | null>(null);
  const descending = RELIABILITY_SORTS.find(s => s.value === sort)?.descending ?? true;
  const totalsRef = useRef(setTotals);

  const fetchPage = useCallback(async (pageNumber: number, pageSize: number) => {
    const summary = await getReliabilitySummary({
      model: model.trim() || undefined, persona: persona.trim() || undefined, includeEvaluation,
      fromUtc: windowStartUtc(days), sort, descending, pageNumber, pageSize,
    });
    if (pageNumber <= 1) totalsRef.current(summary);
    return summaryPage(summary);
  }, [model, persona, includeEvaluation, days, sort, descending]);

  const list = usePagedList(fetchPage, reliabilityRowKey, [model, persona, includeEvaluation, days, sort], 50, t('Failed to load the reliability summary.'));
  const gate = useQuery<GateReport | null>(() => getGateReport('latest').catch(() => null), [], t('Failed to load the gate report.'));

  const sum = totals?.totals;
  const header = (
    <View style={styles.header}>
      {gate.data ? <GateReportSection report={gate.data} formatDateTime={formatDateTime} /> : null}
      <SegmentedControl
        label={t('Window')}
        value={String(days)}
        onChange={(v) => setDays(Number(v))}
        options={RELIABILITY_WINDOWS.map(d => ({ value: String(d), label: t('{{count}} days', { count: d }), testID: `reliability-window-${d}` }))}
      />
      <SearchField value={model} onChangeText={setModel} placeholder={t('Model')} clearLabel={t('Clear')} testID="reliability-model" />
      <SearchField value={persona} onChangeText={setPersona} placeholder={t('Persona')} clearLabel={t('Clear')} testID="reliability-persona" />
      <SelectField
        label={t('Sort')}
        value={sort}
        onChange={(v) => setSort(v as ReliabilitySort)}
        options={RELIABILITY_SORTS.map(s => ({ value: s.value, label: t(s.label) }))}
        placeholder={t('Attempts')}
        closeLabel={t('Close')}
        testID="reliability-sort"
      />
      <SwitchRow label={t('Include evaluations')} hint={t('Include captain evaluation and battery runs, which are left out by default.')} value={includeEvaluation} onChange={setIncludeEvaluation} testID="reliability-include-evaluation" />
      {sum ? (
        <AppText variant="caption" muted style={styles.pad} testID="reliability-totals">
          {t('{{attempts}} attempts, {{completion}} completed, {{repairs}} repairs, {{escalations}} escalations, {{unclassified}} unclassified', {
            attempts: sum.attempts.toLocaleString(), completion: formatRate(sum.completionRate, sum.attempts),
            repairs: sum.repairs.toLocaleString(), escalations: sum.escalations.toLocaleString(), unclassified: formatRate(sum.unclassifiedRate, sum.attempts),
          })}
          {totals?.truncated ? ` ${t('(partial)')}` : ''}
        </AppText>
      ) : null}
    </View>
  );

  return (
    <View style={styles.fill} testID="reliability">
      <PagedList
        state={list}
        header={header}
        keyExtractor={reliabilityRowKey}
        emptyTitle={t('No stage outcomes match the current filters.')}
        loadingLabel={t('Loading...')}
        testID="reliability-list"
        renderItem={({ item }) => {
          const completion = formatRate(item.completionRate, item.attempts);
          const kinds = summarizeKinds(item.failureKinds);
          const facts = [
            item.persona || '-',
            t('{{count}} attempts', { count: item.attempts }),
            item.failed > 0 ? `${t('Failed')} ${item.failed}` : null,
            item.repairs > 0 ? `${t('Repairs')} ${item.repairs}` : null,
            item.escalations > 0 ? `${t('Escalations')} ${item.escalations}` : null,
            item.unclassified > 0 ? `${t('Unclassified')} ${formatRate(item.unclassifiedRate, item.attempts)}` : null,
            item.medianDurationMs ? `${t('Median duration')} ${formatDurationMs(item.medianDurationMs)}` : null,
            kinds || null,
          ].filter((f): f is string => !!f);
          return (
            <ListRow
              title={item.model || '-'}
              subtitle={facts.join(' · ')}
              accessory={<StatusBadge label={completion} tone={completionTone(item.completionRate, item.attempts)} />}
              accessibilityValue={[`${t('Completion')} ${completion}`]}
              testID={`reliability-row-${reliabilityRowKey(item)}`}
            />
          );
        }}
      />
    </View>
  );
}

function GateReportSection({ report, formatDateTime }: { report: GateReport; formatDateTime: (value: string | null | undefined) => string }) {
  const { t } = useLocale();
  const facts = [
    t('Finished {{time}}', { time: formatDateTime(report.finishedUtc) }),
    report.armadaVersion ? t('Armada {{version}}', { version: report.armadaVersion }) : null,
    report.pipelines.length > 0 ? t('{{count}} pipelines', { count: report.pipelines.length }) : null,
  ].filter(Boolean).join(' · ');
  return (
    <Section title={t('Latest gate report')} footer={facts}>
      <View testID="gate-report">
        <KeyValueRow label={t('Status')}>
          <StatusBadge label={report.passed ? t('Passed') : t('Failed')} tone={report.passed ? 'success' : 'error'} />
        </KeyValueRow>
        {report.measures.map(m => (
          <KeyValueRow key={m.id} label={m.name} value={`${m.value} (${m.threshold}) · ${t(m.status)}`} testID={`gate-measure-${m.id}`} />
        ))}
      </View>
    </Section>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  header: { paddingTop: spacing.sm, gap: spacing.sm },
  pad: { paddingHorizontal: spacing.lg },
});

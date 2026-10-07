import { Stack, useRouter, type Href } from 'expo-router';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { ActivityIndicator, FlatList, Pressable, RefreshControl, StyleSheet, View } from 'react-native';
import { getVessel, getVesselBranches, getVesselCommitActivity, getVesselCommits } from '@dashboard/api/client';
import type { GitChangedFile, Vessel, VesselCommit, VesselCommitActivity } from '@dashboard/types/models';
import {
  appendCommits,
  beforeForDay,
  browserUtcOffsetMinutes,
  formatIsoDay,
  groupCommitsByDay,
  parseIsoDate,
  rangeForYear,
  selectableYears,
  todayIsoDate,
} from '@dashboard/lib/vesselHistory';
import { errorMessage } from '../../../build/useLiveResource';
import { AppText, Button, EmptyState, ErrorState, Icon, IconButton, LoadingState, Section, StatusBadge, TextField } from '../../../components/ui';
import { SelectField, type SelectOption } from '../../../components/ui/SelectSheet';
import { useLocale } from '../../../i18n/LocaleContext';
import { useTheme } from '../../../theme/ThemeContext';
import { radius, spacing, typography } from '../../../theme/typography';
import { vesselLinks } from '../vesselLinks';
import { CommitHeatmap } from './CommitHeatmap';

/** Commits requested per page (the dashboard's HISTORY_PAGE_SIZE). */
export const HISTORY_PAGE_SIZE = 50;

const DAY_HEADER_FORMAT: Intl.DateTimeFormatOptions = { weekday: 'long', year: 'numeric', month: 'long', day: 'numeric' };

type Row = { kind: 'day'; key: string; date: string } | { kind: 'commit'; key: string; commit: VesselCommit; date: string };

const KIND_TONE: Record<string, 'success' | 'failed' | 'running' | 'warning'> = {
  Added: 'success', Deleted: 'failed', Modified: 'running', Renamed: 'warning', Copied: 'warning',
};

function FileRow({ file }: { file: GitChangedFile }) {
  const { t } = useLocale();
  const hasSource = (file.kind === 'Renamed' || file.kind === 'Copied') && !!file.oldPath;
  return (
    <View style={styles.file}>
      <StatusBadge label={t(file.kind)} tone={KIND_TONE[file.kind] ?? 'info'} />
      <AppText selectable style={[typography.mono, styles.filePath]}>{hasSource ? `${file.oldPath} -> ${file.path}` : file.path}</AppText>
      {file.isBinary ? <AppText variant="caption" muted>{t('binary')}</AppText> : (
        <AppText variant="caption">
          {file.addedLines !== null ? <AppText variant="caption" color="success">{`+${file.addedLines} `}</AppText> : null}
          {file.deletedLines !== null ? <AppText variant="caption" color="danger">{`-${file.deletedLines}`}</AppText> : null}
        </AppText>
      )}
    </View>
  );
}

function CommitRow({ commit, expanded, onToggle, selectedDay }: { commit: VesselCommit; expanded: boolean; onToggle: () => void; selectedDay: boolean }) {
  const { t, formatDateTime, formatRelativeTime } = useLocale();
  const { colors } = useTheme();
  const shortSha = commit.shortSha || commit.sha.slice(0, 7);
  return (
    <View style={[styles.commit, { backgroundColor: colors.surface, borderColor: selectedDay ? colors.primary : colors.border }]} testID="vessel-history-commit">
      <Pressable
        accessibilityRole="button"
        accessibilityState={{ expanded }}
        accessibilityLabel={`${commit.subject || t('(no message)')}, ${commit.authorName}, ${formatRelativeTime(commit.committedUtc)}`}
        onPress={onToggle}
        style={styles.commitHead}
        testID={`vessel-history-commit-${shortSha}`}
      >
        <Icon name={expanded ? 'chevron-down' : 'chevron-forward'} size={16} color="textMuted" />
        <AppText variant="label" style={styles.flex}>{commit.subject || t('(no message)')}</AppText>
        {commit.isMerge ? <StatusBadge label={t('Merge')} tone="warning" /> : null}
      </Pressable>
      <AppText variant="caption" muted>
        {`${commit.authorName} \u00B7 ${formatRelativeTime(commit.committedUtc)} (${formatDateTime(commit.committedUtc)})`}
      </AppText>
      <AppText variant="caption">
        <AppText variant="caption" selectable style={typography.mono}>{shortSha}</AppText>
        {'  '}
        <AppText variant="caption" color="success">{`+${commit.addedLines}`}</AppText>
        {' '}
        <AppText variant="caption" color="danger">{`-${commit.deletedLines}`}</AppText>
        {'  '}
        {t('{count, plural, one {# file} other {# files}}', { count: commit.filesChanged })}
      </AppText>
      {expanded ? (
        <View style={styles.detail}>
          <AppText variant="caption" muted selectable style={typography.mono}>{commit.sha}</AppText>
          {commit.body ? <AppText selectable style={[typography.mono, styles.body]}>{commit.body}</AppText> : null}
          {commit.authorName !== commit.committerName && commit.committerName ? (
            <AppText variant="caption" muted>{t('Committed by {{name}}', { name: commit.committerName })}</AppText>
          ) : null}
          {commit.files.length > 0
            ? <View accessibilityLabel={t('Changed files')}>{commit.files.map((file) => <FileRow key={`${file.oldPath ?? ''}>${file.path}`} file={file} />)}</View>
            : <AppText variant="caption" muted>{t('No file changes.')}</AppText>}
          {commit.filesTruncated ? (
            <AppText variant="caption" muted>{t('Showing {{shown}} of {{total}} changed files.', { shown: commit.files.length, total: commit.filesChanged })}</AppText>
          ) : null}
        </View>
      ) : null}
    </View>
  );
}

/**
 * View History (the dashboard's VesselHistory): the commit-activity heatmap of a branch (range: last year or a
 * calendar year, with previous / next) and the day-grouped commit timeline with endless scroll. Choosing a heatmap day
 * or "Jump to date" restarts the timeline at that day; "Latest" returns to the newest commits.
 */
export function VesselHistoryScreen({ id }: { id: string }) {
  const { t, locale } = useLocale();
  const { colors } = useTheme();
  const router = useRouter();
  const today = useMemo(() => todayIsoDate(), []);
  const utcOffsetMinutes = useMemo(() => browserUtcOffsetMinutes(), []);
  const listRef = useRef<FlatList<Row> | null>(null);
  const headerHeight = useRef(0);
  const scrollOnLoad = useRef(false);

  const [vessel, setVessel] = useState<Vessel | null>(null);
  const [vesselError, setVesselError] = useState('');
  const [vesselKey, setVesselKey] = useState(0);
  const [branches, setBranches] = useState<string[]>([]);
  const [branch, setBranch] = useState<string | null>(null);

  const [year, setYear] = useState<number | null>(null);
  const [activity, setActivity] = useState<VesselCommitActivity | null>(null);
  const [activityLoading, setActivityLoading] = useState(false);
  const [activityError, setActivityError] = useState('');
  const [activityKey, setActivityKey] = useState(0);
  const [firstCommitUtc, setFirstCommitUtc] = useState<string | null>(null);

  const [selectedDate, setSelectedDate] = useState<string | null>(null);
  const [jumpValue, setJumpValue] = useState('');

  const [commits, setCommits] = useState<VesselCommit[]>([]);
  const [nextCursor, setNextCursor] = useState<string | null>(null);
  const [listLoading, setListLoading] = useState(false);
  const [listLoaded, setListLoaded] = useState(false);
  const [listError, setListError] = useState('');
  const [repoError, setRepoError] = useState('');
  const [listKey, setListKey] = useState(0);
  const [loadingMore, setLoadingMore] = useState(false);
  const [loadMoreError, setLoadMoreError] = useState('');
  const [expanded, setExpanded] = useState<Set<string>>(() => new Set());
  const [refreshing, setRefreshing] = useState(false);

  const generation = useRef(0);
  const cursorRef = useRef<string | null>(null);
  const loadingMoreRef = useRef(false);

  const range = useMemo(() => rangeForYear(year, today), [year, today]);
  const years = useMemo(() => selectableYears(firstCommitUtc, today), [firstCommitUtc, today]);
  const yearOptions = useMemo<(number | null)[]>(() => [null, ...years], [years]);
  const yearIndex = Math.max(0, yearOptions.indexOf(year));

  // Vessel and its branches (the default branch first when the repository lists none).
  useEffect(() => {
    let cancelled = false;
    Promise.all([getVessel(id), getVesselBranches(id).catch(() => null)])
      .then(([loaded, branchResult]) => {
        if (cancelled) return;
        const defaultBranch = loaded.defaultBranch || branchResult?.defaultBranch || 'main';
        const names = (branchResult?.branches ?? []).map((b) => b.name).filter((name) => !!name);
        if (!names.includes(defaultBranch)) names.unshift(defaultBranch);
        setVessel(loaded);
        setVesselError('');
        setBranches(names);
        setBranch((current) => current ?? defaultBranch);
      })
      .catch((e: unknown) => { if (!cancelled) setVesselError(errorMessage(e) || t('Failed to load vessel.')); });
    return () => { cancelled = true; };
  }, [id, vesselKey, t]);

  // Heatmap.
  useEffect(() => {
    if (!branch) return undefined;
    let cancelled = false;
    // eslint-disable-next-line react-hooks/set-state-in-effect -- a fetch keyed on branch and range; it sets loading state first
    setActivityLoading(true);
    setActivityError('');
    getVesselCommitActivity(id, { branch, from: range.from, to: range.to, utcOffsetMinutes })
      .then((result) => {
        if (cancelled) return;
        setActivity(result);
        if (!result.error) setFirstCommitUtc(result.firstCommitUtc ?? null);
      })
      .catch((e: unknown) => {
        if (cancelled) return;
        setActivity(null);
        setActivityError(errorMessage(e) || t('Failed to load commit activity.'));
      })
      .finally(() => { if (!cancelled) setActivityLoading(false); });
    return () => { cancelled = true; };
  }, [id, branch, range.from, range.to, utcOffsetMinutes, activityKey, t]);

  // First page of the timeline; each query gets a generation so a slow page from an older one is dropped.
  useEffect(() => {
    if (!branch) return;
    generation.current += 1;
    const gen = generation.current;
    cursorRef.current = null;
    loadingMoreRef.current = false;
    // eslint-disable-next-line react-hooks/set-state-in-effect -- a fetch keyed on branch and date; it resets the list first
    setCommits([]);
    setNextCursor(null);
    setListLoaded(false);
    setListError('');
    setRepoError('');
    setLoadMoreError('');
    setLoadingMore(false);
    setListLoading(true);
    getVesselCommits(id, { branch, before: selectedDate ? beforeForDay(selectedDate) : null, limit: HISTORY_PAGE_SIZE })
      .then((page) => {
        if (gen !== generation.current) return;
        if (page.error) { setRepoError(page.error); return; }
        cursorRef.current = page.nextCursor ?? null;
        setCommits(page.commits ?? []);
        setNextCursor(page.nextCursor ?? null);
        setListLoaded(true);
        if (scrollOnLoad.current) {
          scrollOnLoad.current = false;
          listRef.current?.scrollToOffset({ offset: headerHeight.current, animated: true });
        }
      })
      .catch((e: unknown) => { if (gen === generation.current) setListError(errorMessage(e) || t('Failed to load commits.')); })
      .finally(() => { if (gen === generation.current) setListLoading(false); });
  }, [id, branch, selectedDate, listKey, t]);

  const loadMore = useCallback(() => {
    const cursor = cursorRef.current;
    if (!cursor || loadingMoreRef.current) return;
    const gen = generation.current;
    loadingMoreRef.current = true;
    setLoadingMore(true);
    setLoadMoreError('');
    getVesselCommits(id, { cursor, limit: HISTORY_PAGE_SIZE })
      .then((page) => {
        if (gen !== generation.current) return;
        if (page.error) { setLoadMoreError(page.error); return; }
        cursorRef.current = page.nextCursor ?? null;
        setCommits((prev) => appendCommits(prev, page.commits ?? []));
        setNextCursor(page.nextCursor ?? null);
      })
      .catch((e: unknown) => { if (gen === generation.current) setLoadMoreError(errorMessage(e) || t('Failed to load more commits.')); })
      .finally(() => {
        if (gen !== generation.current) return;
        loadingMoreRef.current = false;
        setLoadingMore(false);
      });
  }, [id, t]);

  function selectDate(date: string) {
    scrollOnLoad.current = true;
    setSelectedDate(date);
    setJumpValue(date);
    if (date < range.from || date > range.to) {
      const lastYear = rangeForYear(null, today);
      setYear(date >= lastYear.from ? null : Number(date.slice(0, 4)));
    }
  }

  function jump() {
    if (!parseIsoDate(jumpValue)) return;
    selectDate(jumpValue > today ? today : jumpValue);
  }

  function resetToLatest() {
    scrollOnLoad.current = true;
    setSelectedDate(null);
    setJumpValue('');
  }

  function changeBranch(next: string) {
    setActivity(null);
    setFirstCommitUtc(null);
    setBranch(next);
  }

  async function refresh() {
    setRefreshing(true);
    setActivityKey((k) => k + 1);
    setListKey((k) => k + 1);
    setRefreshing(false);
  }

  const rows = useMemo<Row[]>(() => {
    const out: Row[] = [];
    for (const group of groupCommitsByDay(commits)) {
      out.push({ kind: 'day', key: `day-${group.date}`, date: group.date });
      for (const commit of group.commits) out.push({ kind: 'commit', key: commit.sha, commit, date: group.date });
    }
    return out;
  }, [commits]);

  const screenTitle = <Stack.Screen options={{ title: t('View History') }} />;
  if (!vessel) {
    if (vesselError) {
      return <>{screenTitle}<ErrorState title={t('Vessel not found.')} message={vesselError} retryLabel={t('Retry')} onRetry={() => setVesselKey((k) => k + 1)} /></>;
    }
    return <>{screenTitle}<LoadingState label={t('Loading...')} /></>;
  }

  const anyRepoError = activity?.error || repoError;
  const rangeLabel = year === null ? t('the last year') : String(year);
  const selectedLabel = selectedDate ? formatIsoDay(locale, selectedDate, { year: 'numeric', month: 'long', day: 'numeric' }) : '';
  const branchOptions: SelectOption<string>[] = branches.map((name) => ({ value: name, label: name }));
  const yearSelectOptions: SelectOption<string>[] = [{ value: '', label: t('Last year') }, ...years.map((y) => ({ value: String(y), label: String(y) }))];

  const header = (
    <View onLayout={(e) => { headerHeight.current = e.nativeEvent.layout.height; }}>
      <AppText muted style={styles.pad}>{t('Commit activity and history for {{name}}.', { name: vessel.name })}</AppText>
      <Section>
        <View style={styles.toolbar}>
          <SelectField label={t('Branch')} value={branch ?? ''} options={branchOptions} onChange={changeBranch} closeLabel={t('Close')} testID="vessel-history-branch" />
          <AppText variant="label">{t('Range')}</AppText>
          <View style={styles.yearRow} accessibilityLabel={t('Heatmap range')}>
            <IconButton icon="chevron-back" label={t('Previous year')} onPress={() => setYear(yearOptions[yearIndex + 1] ?? null)} testID="vessel-history-prev-year" />
            <View style={styles.flex}>
              <SelectField
                label={t('Year')}
                value={year === null ? '' : String(year)}
                options={yearSelectOptions}
                onChange={(v) => setYear(v ? Number(v) : null)}
                closeLabel={t('Close')}
                testID="vessel-history-year"
              />
            </View>
            <IconButton icon="chevron-forward" label={t('Next year')} onPress={() => { if (yearIndex > 0) setYear(yearOptions[yearIndex - 1] ?? null); }} testID="vessel-history-next-year" />
          </View>
          <TextField
            label={t('Jump to date')}
            value={jumpValue}
            onChangeText={setJumpValue}
            placeholder="yyyy-mm-dd"
            autoCapitalize="none"
            autoCorrect={false}
            keyboardType="numbers-and-punctuation"
            returnKeyType="go"
            onSubmitEditing={jump}
            testID="vessel-history-jump"
          />
          <View style={styles.buttons}>
            <Button label={t('Go')} onPress={jump} disabled={!parseIsoDate(jumpValue)} testID="vessel-history-go" />
            <Button label={t('Latest')} variant="secondary" onPress={resetToLatest} disabled={!selectedDate} testID="vessel-history-latest" />
          </View>
        </View>
      </Section>
      {anyRepoError ? (
        <EmptyState
          title={t('History is not available')}
          message={`${t('The repository for this vessel could not be read. It may not be cloned yet, or the branch may not exist.')}\n${anyRepoError}`}
        />
      ) : (
        <Section>
          <View style={[styles.heat, activityLoading && activity ? styles.stale : null]}>
            {activityError ? (
              <ErrorState title={t('Could not load')} message={activityError} retryLabel={t('Retry')} onRetry={() => setActivityKey((k) => k + 1)} />
            ) : activity ? (
              <CommitHeatmap activity={activity} selectedDate={selectedDate} onSelectDate={selectDate} rangeLabel={rangeLabel} />
            ) : (
              <View style={styles.center}><ActivityIndicator color={colors.primary} /><AppText muted>{t('Loading activity...')}</AppText></View>
            )}
          </View>
        </Section>
      )}
      {selectedDate && !anyRepoError ? (
        <View style={[styles.banner, { borderColor: colors.primary, backgroundColor: colors.surface }]} testID="vessel-history-jump-banner">
          <AppText style={styles.flex}>{t('Showing commits on or before {{date}}.', { date: selectedLabel })}</AppText>
          <Button label={t('Latest')} variant="ghost" onPress={resetToLatest} style={styles.noMargin} />
        </View>
      ) : null}
    </View>
  );

  let empty = null;
  if (!anyRepoError) {
    if (listError) empty = <ErrorState title={t('Could not load')} message={listError} retryLabel={t('Retry')} onRetry={() => setListKey((k) => k + 1)} />;
    else if (listLoading) empty = <LoadingState label={t('Loading commits...')} />;
    else if (listLoaded) {
      empty = (
        <EmptyState
          title={selectedDate ? t('No commits on or before this date') : t('No commits yet')}
          message={selectedDate ? t('Pick a later date or return to the latest commits.') : t('This branch has no commits.')}
        />
      );
    }
  }

  const footer = rows.length === 0 ? null : (
    <View style={styles.footer}>
      {loadMoreError ? <AppText color="danger" accessibilityRole="alert">{loadMoreError}</AppText> : null}
      {nextCursor ? (
        loadingMore
          ? <ActivityIndicator color={colors.primary} accessibilityLabel={t('Loading...')} />
          : <Button label={t('Load more')} variant="secondary" onPress={loadMore} testID="vessel-history-load-more" />
      ) : listLoaded ? <AppText muted testID="vessel-history-end">{t('End of history')}</AppText> : null}
    </View>
  );

  return (
    <View style={[styles.fill, { backgroundColor: colors.background }]} testID="vessel-history">
      <Stack.Screen
        options={{
          title: t('View History'),
          headerRight: () => <IconButton icon="boat-outline" label={t('Back To Vessel')} onPress={() => router.push(vesselLinks.detail(vessel.id) as Href)} />,
        }}
      />
      <FlatList
        ref={listRef}
        data={anyRepoError || listLoading ? [] : rows}
        keyExtractor={(row) => row.key}
        ListHeaderComponent={header}
        ListEmptyComponent={empty}
        ListFooterComponent={footer}
        contentContainerStyle={styles.content}
        onEndReached={() => { if (!loadMoreError) loadMore(); }}
        onEndReachedThreshold={0.6}
        refreshControl={<RefreshControl refreshing={refreshing} onRefresh={() => void refresh()} tintColor={colors.primary} />}
        renderItem={({ item }) => item.kind === 'day' ? (
          <AppText
            variant="subheading"
            accessibilityRole="header"
            color={item.date === selectedDate ? 'primary' : 'textMuted'}
            style={styles.dayHeader}
            testID="vessel-history-day"
          >
            {formatIsoDay(locale, item.date, DAY_HEADER_FORMAT)}
          </AppText>
        ) : (
          <CommitRow
            commit={item.commit}
            selectedDay={item.date === selectedDate}
            expanded={expanded.has(item.commit.sha)}
            onToggle={() => setExpanded((prev) => {
              const next = new Set(prev);
              if (next.has(item.commit.sha)) next.delete(item.commit.sha);
              else next.add(item.commit.sha);
              return next;
            })}
          />
        )}
      />
    </View>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  flex: { flex: 1 },
  content: { paddingVertical: spacing.lg, flexGrow: 1, width: '100%', maxWidth: 820, alignSelf: 'center' },
  pad: { paddingHorizontal: spacing.lg, marginBottom: spacing.md },
  toolbar: { padding: spacing.md },
  yearRow: { flexDirection: 'row', alignItems: 'flex-end', gap: spacing.xs },
  buttons: { flexDirection: 'row', gap: spacing.sm },
  heat: { padding: spacing.md },
  stale: { opacity: 0.5 },
  center: { alignItems: 'center', gap: spacing.sm, padding: spacing.lg },
  banner: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm, borderWidth: 1, borderRadius: radius.md, marginHorizontal: spacing.md, marginBottom: spacing.md, paddingLeft: spacing.md },
  noMargin: { marginBottom: 0 },
  dayHeader: { marginHorizontal: spacing.lg, marginTop: spacing.md, marginBottom: spacing.sm },
  commit: { marginHorizontal: spacing.md, marginBottom: spacing.sm, padding: spacing.md, borderRadius: radius.md, borderWidth: StyleSheet.hairlineWidth, gap: 2 },
  commitHead: { flexDirection: 'row', alignItems: 'center', gap: spacing.xs, minHeight: 32 },
  detail: { marginTop: spacing.sm, gap: spacing.xs },
  body: { fontSize: 12, lineHeight: 17 },
  file: { flexDirection: 'row', flexWrap: 'wrap', alignItems: 'center', gap: spacing.sm, paddingVertical: 2 },
  filePath: { fontSize: 12, flexShrink: 1 },
  footer: { alignItems: 'center', padding: spacing.lg, gap: spacing.sm },
});

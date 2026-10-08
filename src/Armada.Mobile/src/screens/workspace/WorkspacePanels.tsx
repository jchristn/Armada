import { useEffect, useState } from 'react';
import { Pressable, ScrollView, StyleSheet, TextInput, View } from 'react-native';
import { execWorkspaceCommand, getWorkspaceChanges, getWorkspaceDiff, searchWorkspace } from '@dashboard/api/client';
import type { WorkspaceChangesResult, WorkspaceSearchResult } from '@dashboard/types/models';
import { parseUnifiedDiff, type UnifiedDiffLineKind } from '@dashboard/lib/unifiedDiff';
import { ActionRow } from '../../build/fields';
import { errorMessage } from '../../build/useLiveResource';
import { AppText, Banner, Button, EmptyState, LoadingState, SearchField, StatusBadge } from '../../components/ui';
import { useLocale } from '../../i18n/LocaleContext';
import { useTheme } from '../../theme/ThemeContext';
import type { Palette } from '../../theme/palette';
import { MIN_TOUCH, radius, spacing, typography } from '../../theme/typography';

/** Palette color of a diff line (the parser's classification; an added "+++ x" line inside a hunk is still an addition). */
function diffColor(kind: UnifiedDiffLineKind): keyof Palette {
  switch (kind) {
    case 'add': return 'success';
    case 'del': return 'danger';
    case 'hunkHeader': return 'info';
    case 'fileHeader':
    case 'meta': return 'textMuted';
    default: return 'text';
  }
}

/** Unified diff text with line colors; long lines wrap. */
export function DiffText({ diff, testID }: { diff: string; testID?: string }) {
  const { colors } = useTheme();
  const lines = parseUnifiedDiff(diff).lines;
  return (
    <View style={[styles.code, { backgroundColor: colors.background, borderColor: colors.border }]} testID={testID}>
      {lines.map((line, i) => (
        <AppText key={i} selectable color={diffColor(line.kind)} style={typography.mono}>{line.text || ' '}</AppText>
      ))}
    </View>
  );
}

/** Review diff (the dashboard's WorkspaceDiff): the working tree against HEAD, optionally for one path. */
export function DiffPanel({ vesselId, path, onClearPath }: { vesselId: string; path?: string | null; onClearPath?: () => void }) {
  const { t } = useLocale();
  const [diff, setDiff] = useState<string | null>(null);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);
  const [loaded, setLoaded] = useState(false);

  async function load() {
    setLoading(true);
    try {
      const result = await getWorkspaceDiff(vesselId, path ?? undefined);
      if (result.error) { setError(result.error); setDiff(null); } else { setDiff(result.diff); setError(''); }
      setLoaded(true);
    } catch (e) {
      setError(errorMessage(e) || t('Failed to load diff.'));
    } finally {
      setLoading(false);
    }
  }

  // A path chosen from Changes loads at once; the whole-tree diff loads on request, like the dashboard.
  // eslint-disable-next-line react-hooks/set-state-in-effect -- a fetch keyed on the path; it sets loading state first
  useEffect(() => { if (path) void load(); }, [path]); // eslint-disable-line react-hooks/exhaustive-deps

  return (
    <View style={styles.pad} testID="workspace-diff">
      {path ? (
        <View style={styles.rowWrap}>
          <AppText variant="caption" style={[typography.mono, styles.flex]}>{path}</AppText>
          {onClearPath ? <Button label={t('All changes')} variant="ghost" onPress={onClearPath} /> : null}
        </View>
      ) : null}
      <Button label={loading ? t('Loading...') : loaded ? t('Refresh') : t('Load diff')} busy={loading} variant="secondary" onPress={() => void load()} testID="workspace-diff-load" />
      {error ? <Banner tone="danger" title={error} /> : null}
      {loaded && !error && (diff ?? '').trim().length === 0 ? <AppText muted>{t('No tracked changes against HEAD.')}</AppText> : null}
      {diff && diff.trim().length > 0 ? <DiffText diff={diff} testID="workspace-diff-text" /> : null}
    </View>
  );
}

/** Changed files (git status) of the working tree; a file opens its diff or the file itself. */
export function ChangesPanel({ vesselId, onOpenDiff, onOpenFile }: { vesselId: string; onOpenDiff: (path: string) => void; onOpenFile: (path: string) => void }) {
  const { t } = useLocale();
  const { colors } = useTheme();
  const [data, setData] = useState<WorkspaceChangesResult | null>(null);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(true);

  async function load() {
    setLoading(true);
    try {
      setData(await getWorkspaceChanges(vesselId));
      setError('');
    } catch (e) {
      setError(errorMessage(e));
    } finally {
      setLoading(false);
    }
  }
  // eslint-disable-next-line react-hooks/set-state-in-effect -- a fetch keyed on the vessel; it sets loading state first
  useEffect(() => { void load(); }, [vesselId]); // eslint-disable-line react-hooks/exhaustive-deps

  return (
    <View testID="workspace-changes">
      <View style={styles.pad}>
        <Button label={t('Refresh')} variant="ghost" icon="refresh" onPress={() => void load()} />
        {loading && !data ? <LoadingState label={t('Loading...')} /> : null}
        {error ? <Banner tone="danger" title={error} /> : null}
        {data?.error ? <Banner tone="warning" title={data.error} /> : null}
        {data ? (
          <AppText variant="caption" muted>
            {`${data.branchName || t('No branch')} - ${data.isDirty ? t('Dirty working tree') : t('Clean working tree')} - ${t('{{count}} ahead', { count: data.commitsAhead })} / ${t('{{count}} behind', { count: data.commitsBehind })}`}
          </AppText>
        ) : null}
      </View>
      {data && data.changes.length === 0 ? <EmptyState icon="checkmark-done-outline" title={t('No changes')} /> : null}
      {data?.changes.map((c) => (
        <View key={`${c.status}:${c.path}`} style={[styles.listRow, { borderBottomColor: colors.border }]} testID={`workspace-change-${c.path}`}>
          <View style={styles.rowWrap}>
            <StatusBadge label={c.status} tone={c.status.toLowerCase().includes('delete') ? 'failed' : c.status.toLowerCase().includes('add') || c.status.toLowerCase().includes('untracked') ? 'success' : 'warning'} />
            <AppText variant="caption" style={[typography.mono, styles.flex]}>{c.originalPath ? `${c.originalPath} → ${c.path}` : c.path}</AppText>
          </View>
          <ActionRow>
            <Button label={t('Diff')} variant="ghost" onPress={() => onOpenDiff(c.path)} testID={`workspace-change-diff-${c.path}`} />
            <Button label={t('Open')} variant="ghost" onPress={() => onOpenFile(c.path)} />
          </ActionRow>
        </View>
      ))}
    </View>
  );
}

/** Content search across the working tree; a match opens the file. */
export function SearchPanel({ vesselId, onOpenFile }: { vesselId: string; onOpenFile: (path: string) => void }) {
  const { t } = useLocale();
  const { colors } = useTheme();
  const [query, setQuery] = useState('');
  const [result, setResult] = useState<WorkspaceSearchResult | null>(null);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);

  async function run() {
    const q = query.trim();
    if (!q) return;
    setBusy(true);
    try {
      setResult(await searchWorkspace(vesselId, q));
      setError('');
    } catch (e) {
      setError(errorMessage(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <View testID="workspace-search">
      <SearchField value={query} onChangeText={setQuery} placeholder={t('Search file contents')} clearLabel={t('Clear')} testID="workspace-search-input" />
      <View style={styles.pad}>
        <Button label={busy ? t('Searching...') : t('Search')} busy={busy} disabled={!query.trim()} onPress={() => void run()} testID="workspace-search-run" />
        {error ? <Banner tone="danger" title={error} /> : null}
        {result ? (
          <AppText variant="caption" muted>
            {`${t('{{count}} matches', { count: result.totalMatches })}${result.truncated ? ` - ${t('Showing the first {{count}}', { count: result.matches.length })}` : ''}`}
          </AppText>
        ) : null}
      </View>
      {result?.matches.map((m, i) => (
        <Pressable
          key={`${m.path}:${m.lineNumber}:${i}`}
          accessibilityRole="button"
          accessibilityLabel={`${m.path}:${m.lineNumber} ${m.preview}`}
          onPress={() => onOpenFile(m.path)}
          style={({ pressed }) => [styles.listRow, { borderBottomColor: colors.border, backgroundColor: pressed ? colors.background : colors.surface }]}
        >
          <AppText variant="caption" color="primary" style={typography.mono}>{`${m.path}:${m.lineNumber}`}</AppText>
          <AppText variant="caption" style={typography.mono} numberOfLines={2}>{m.preview}</AppText>
        </Pressable>
      ))}
    </View>
  );
}

interface TerminalLine { kind: 'command' | 'stdout' | 'stderr' | 'meta'; text: string }

/**
 * The workspace terminal (the dashboard's WorkspaceTerminal): one non-interactive command at a time in the vessel's
 * working tree, bounded by the server timeout and limited to tenant administrators by the server. Earlier commands
 * are offered as chips (the dashboard's arrow-key history).
 */
export function TerminalPanel({ vesselId }: { vesselId: string }) {
  const { t } = useLocale();
  const { colors } = useTheme();
  const [command, setCommand] = useState('');
  const [lines, setLines] = useState<TerminalLine[]>([]);
  const [busy, setBusy] = useState(false);
  const [history, setHistory] = useState<string[]>([]);

  async function run() {
    const cmd = command.trim();
    if (!cmd || busy) return;
    setCommand('');
    setHistory((h) => [cmd, ...h.filter((x) => x !== cmd)].slice(0, 50));
    setLines((l) => [...l, { kind: 'command', text: `$ ${cmd}` }]);
    setBusy(true);
    try {
      const result = await execWorkspaceCommand(vesselId, cmd);
      const next: TerminalLine[] = [];
      if (result.stdout) next.push({ kind: 'stdout', text: result.stdout.replace(/\n+$/, '') });
      if (result.stderr) next.push({ kind: 'stderr', text: result.stderr.replace(/\n+$/, '') });
      next.push({ kind: 'meta', text: `${result.timedOut ? t('timed out') : t('exit {{code}}', { code: result.exitCode })} · ${Math.round(result.durationMs)}ms` });
      setLines((l) => [...l, ...next]);
    } catch (e) {
      setLines((l) => [...l, { kind: 'stderr', text: errorMessage(e) }]);
    } finally {
      setBusy(false);
    }
  }

  const color = (kind: TerminalLine['kind']): keyof Palette => (kind === 'command' ? 'info' : kind === 'stderr' ? 'danger' : kind === 'meta' ? 'textMuted' : 'text');

  return (
    <View style={styles.pad} testID="workspace-terminal">
      <View style={[styles.code, styles.terminal, { backgroundColor: colors.background, borderColor: colors.border }]} accessibilityLiveRegion="polite">
        {lines.length === 0
          ? <AppText muted>{t('Run a command in the vessel working tree (e.g. git status, ls, npm test).')}</AppText>
          : lines.map((line, i) => <AppText key={i} selectable color={color(line.kind)} style={typography.mono}>{line.text}</AppText>)}
      </View>
      <View style={[styles.input, { borderColor: colors.border, backgroundColor: colors.surface }]}>
        <AppText style={typography.mono}>$</AppText>
        <TextInput
          value={command}
          onChangeText={setCommand}
          onSubmitEditing={() => void run()}
          placeholder={t('Enter a command...')}
          placeholderTextColor={colors.textMuted}
          accessibilityLabel={t('Command')}
          autoCapitalize="none"
          autoCorrect={false}
          editable={!busy}
          returnKeyType="go"
          style={[typography.mono, styles.flex, { color: colors.text }]}
          testID="workspace-terminal-input"
        />
      </View>
      <ActionRow>
        <Button label={busy ? t('Running...') : t('Run')} busy={busy} disabled={!command.trim()} onPress={() => void run()} testID="workspace-terminal-run" />
        {lines.length > 0 ? <Button label={t('Clear')} variant="ghost" onPress={() => setLines([])} /> : null}
      </ActionRow>
      {history.length > 0 ? (
        <ScrollView horizontal showsHorizontalScrollIndicator={false} contentContainerStyle={styles.chips} accessibilityLabel={t('Previous commands')}>
          {history.map((h) => (
            <Pressable key={h} accessibilityRole="button" accessibilityLabel={h} onPress={() => setCommand(h)} style={[styles.chip, { borderColor: colors.border }]}>
              <AppText variant="caption" style={typography.mono}>{h}</AppText>
            </Pressable>
          ))}
        </ScrollView>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  pad: { paddingHorizontal: spacing.lg, gap: spacing.sm },
  rowWrap: { flexDirection: 'row', alignItems: 'center', flexWrap: 'wrap', gap: spacing.sm },
  code: { borderWidth: StyleSheet.hairlineWidth, borderRadius: radius.md, padding: spacing.md },
  terminal: { minHeight: 160 },
  listRow: { paddingHorizontal: spacing.lg, paddingVertical: spacing.sm, gap: 2, minHeight: MIN_TOUCH, borderBottomWidth: StyleSheet.hairlineWidth },
  input: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm, borderWidth: 1, borderRadius: radius.md, paddingHorizontal: spacing.md, minHeight: MIN_TOUCH },
  chips: { gap: spacing.sm, paddingBottom: spacing.sm },
  chip: { borderWidth: 1, borderRadius: radius.pill, paddingHorizontal: spacing.md, paddingVertical: spacing.xs, minHeight: 36, justifyContent: 'center' },
});

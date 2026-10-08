import { Stack, useRouter, type Href } from 'expo-router';
import { useCallback, useEffect, useMemo, useState } from 'react';
import { Pressable, RefreshControl, ScrollView, StyleSheet, TextInput, View } from 'react-native';
import {
  createWorkspaceDirectory,
  deleteWorkspaceEntry,
  getVesselReadiness,
  getWorkspaceFile,
  getWorkspaceStatus,
  getWorkspaceTree,
  listVessels,
  renameWorkspaceEntry,
  saveWorkspaceFile,
} from '@dashboard/api/client';
import type { Vessel, VesselReadinessResult, WorkspaceFileResponse, WorkspaceStatusResult, WorkspaceTreeEntry } from '@dashboard/types/models';
import { readinessLabel, readinessTone, readinessBranchSummary, readinessDriftSummary } from '@dashboard/lib/readiness';
import {
  buildWorkspaceContextSnippet,
  buildWorkspaceDispatchDraft,
  buildWorkspacePlanningDraft,
  createDraftFile,
  getWorkspaceName,
  getWorkspaceParentPath,
  isWorkspacePathInScope,
  normalizeWorkspacePath,
  pruneWorkspaceFileMap,
  pruneWorkspaceStringMap,
  remapWorkspaceFileMap,
  remapWorkspaceScopedPath,
  remapWorkspaceStringMap,
} from '@dashboard/lib/workspace';
import { HubTabBar, type HubTab } from '../../build/HubTabs';
import { ActionRow, InfoRow } from '../../build/fields';
import { errorMessage } from '../../build/useLiveResource';
import { AppText, Banner, BottomSheet, Button, ConfirmDialog, EmptyState, Icon, LoadingState, StatusBadge, SwipeRow } from '../../components/ui';
import { Disclosure } from '../../components/ui/Disclosure';
import { SelectField } from '../../components/ui/SelectSheet';
import { useLocale } from '../../i18n/LocaleContext';
import { useLayout } from '../../navigation/useLayout';
import { useNotifications } from '../../notifications/NotificationContext';
import { useTheme } from '../../theme/ThemeContext';
import { MIN_TOUCH, radius, spacing, typography } from '../../theme/typography';
import { dispatchHref } from '../operations/w24/dispatchLink';
import { PathPromptSheet } from './PathPromptSheet';
import { rememberWorkspaceVessel } from './recentVessels';
import { WorkspaceContextSheet } from './WorkspaceContextSheet';
import { ChangesPanel, DiffPanel, SearchPanel, TerminalPanel } from './WorkspacePanels';

export type WorkspacePanel = 'files' | 'search' | 'changes' | 'diff' | 'terminal';
export const WORKSPACE_PANELS: WorkspacePanel[] = ['files', 'search', 'changes', 'diff', 'terminal'];

type Prompt = { kind: 'file' | 'folder' | 'rename'; initial: string; target?: string } | null;

/**
 * A vessel's workspace (the dashboard's Workspace page, /workspace/:vesselId[/:panel]): status, readiness, a
 * drill-down file browser with open, edit and save (with conflict detection through the content hash), new file and
 * folder, rename or move, delete with confirmation, entry metadata, multi-select for scoped Plan / Dispatch / context,
 * plus Search, Changes, Diff, and Terminal panels. Tablets show the file list and the editor side by side.
 */
export function WorkspaceScreen({ vesselId, initialPanel = 'files' }: { vesselId: string; initialPanel?: WorkspacePanel }) {
  const { t, formatDateTime } = useLocale();
  const { colors } = useTheme();
  const { isTablet } = useLayout();
  const { pushToast } = useNotifications();
  const router = useRouter();
  const [panel, setPanel] = useState<WorkspacePanel>(initialPanel);
  const [vessels, setVessels] = useState<Vessel[]>([]);
  const [status, setStatus] = useState<WorkspaceStatusResult | null>(null);
  const [readiness, setReadiness] = useState<VesselReadinessResult | null>(null);
  const [entriesByDir, setEntriesByDir] = useState<Record<string, WorkspaceTreeEntry[]>>({});
  const [currentDir, setCurrentDir] = useState('');
  const [loadingDir, setLoadingDir] = useState(false);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState('');
  const [openPaths, setOpenPaths] = useState<string[]>([]);
  const [activePath, setActivePath] = useState<string | null>(null);
  const [files, setFiles] = useState<Record<string, WorkspaceFileResponse>>({});
  const [drafts, setDrafts] = useState<Record<string, string>>({});
  const [selected, setSelected] = useState<string[]>([]);
  const [prompt, setPrompt] = useState<Prompt>(null);
  const [confirmDelete, setConfirmDelete] = useState<string | null>(null);
  const [metadata, setMetadata] = useState<WorkspaceTreeEntry | null>(null);
  const [contextOpen, setContextOpen] = useState(false);
  const [diffPath, setDiffPath] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  const vessel = useMemo(() => vessels.find((v) => v.id === vesselId) ?? null, [vessels, vesselId]);
  const activeFile = activePath ? files[activePath] ?? null : null;
  const activeDraft = activePath ? drafts[activePath] ?? activeFile?.content ?? '' : '';
  const isDirty = useCallback((path: string) => !!files[path] && (drafts[path] ?? files[path].content) !== files[path].content, [files, drafts]);
  const actionable = useMemo(() => {
    const sel = selected.map(normalizeWorkspacePath).filter(Boolean);
    return sel.length > 0 ? sel : activePath ? [activePath] : [];
  }, [selected, activePath]);
  const overlapping = useMemo(() => (status?.activeMissions ?? []).filter((m) => m.scopedFiles.some((p) => actionable.includes(normalizeWorkspacePath(p)))), [status, actionable]);

  const loadDir = useCallback(async (dir: string) => {
    const tree = await getWorkspaceTree(vesselId, dir || undefined);
    setEntriesByDir((cur) => ({ ...cur, [dir]: tree?.entries ?? [] }));
  }, [vesselId]);

  const refresh = useCallback(async (dirs?: string[]) => {
    try {
      const next = await getWorkspaceStatus(vesselId);
      setStatus(next);
      const toLoad = Array.from(new Set(['', ...(dirs ?? [])]));
      for (const dir of toLoad) {
        try { await loadDir(dir); } catch (e) { if (dir === '') throw e; }
      }
      setError('');
    } catch (e) {
      setError(errorMessage(e) || t('Failed to load Workspace.'));
    }
  }, [vesselId, loadDir, t]);

  useEffect(() => {
    let cancelled = false;
    void rememberWorkspaceVessel(vesselId);
    listVessels({ pageSize: 9999 }).then((r) => { if (!cancelled) setVessels(r?.objects ?? []); }).catch((e: unknown) => { if (!cancelled) setError(errorMessage(e)); });
    getVesselReadiness(vesselId).then((r) => { if (!cancelled) setReadiness(r ?? null); }).catch(() => { if (!cancelled) setReadiness(null); });
    // eslint-disable-next-line react-hooks/set-state-in-effect -- the first load of the vessel's workspace; status and tree are set from the fetch
    void refresh().finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, [vesselId, refresh]);

  async function enterDir(dir: string) {
    const path = normalizeWorkspacePath(dir);
    setCurrentDir(path);
    if (entriesByDir[path]) return;
    setLoadingDir(true);
    try { await loadDir(path); setError(''); } catch (e) { setError(errorMessage(e) || t('Failed to load directory.')); } finally { setLoadingDir(false); }
  }

  async function openFile(path: string) {
    const p = normalizeWorkspacePath(path);
    if (!files[p]) {
      try {
        const file = await getWorkspaceFile(vesselId, p);
        setFiles((cur) => ({ ...cur, [p]: file }));
        setDrafts((cur) => ({ ...cur, [p]: cur[p] ?? file.content }));
      } catch (e) {
        setError(errorMessage(e) || t('Failed to open file.'));
        return;
      }
    }
    setOpenPaths((cur) => (cur.includes(p) ? cur : [...cur, p]));
    setActivePath(p);
    setPanel('files');
    setError('');
  }

  function closeFile(path: string) {
    setOpenPaths((cur) => {
      const next = cur.filter((x) => x !== path);
      if (activePath === path) setActivePath(next[next.length - 1] ?? null);
      return next;
    });
  }

  async function save() {
    if (!activePath || !activeFile?.isEditable) return;
    const content = drafts[activePath] ?? activeFile.content;
    setSaving(true);
    try {
      const result = await saveWorkspaceFile(vesselId, { path: activePath, content, expectedHash: activeFile.contentHash || null });
      setFiles((cur) => ({
        ...cur,
        [activePath]: { ...(cur[activePath] ?? createDraftFile(activePath)), path: activePath, name: getWorkspaceName(activePath), content, contentHash: result.contentHash, sizeBytes: result.sizeBytes, lastWriteUtc: result.lastWriteUtc, isEditable: true, isBinary: false, isLarge: false, previewTruncated: false },
      }));
      setDrafts((cur) => ({ ...cur, [activePath]: content }));
      pushToast('success', t('Saved {{path}}', { path: activePath }));
      await refresh([currentDir, getWorkspaceParentPath(activePath)]);
    } catch (e) {
      const message = errorMessage(e) || t('Save failed.');
      setError(message);
      pushToast('warning', message);
    } finally {
      setSaving(false);
    }
  }

  async function submitPrompt(value: string) {
    const current = prompt;
    setPrompt(null);
    if (!current) return;
    const path = normalizeWorkspacePath(value);
    try {
      if (current.kind === 'file') {
        setFiles((cur) => ({ ...cur, [path]: createDraftFile(path) }));
        setDrafts((cur) => ({ ...cur, [path]: '' }));
        setOpenPaths((cur) => (cur.includes(path) ? cur : [...cur, path]));
        setActivePath(path);
        setSelected((cur) => (cur.includes(path) ? cur : [...cur, path]));
      } else if (current.kind === 'folder') {
        await createWorkspaceDirectory(vesselId, { path });
        pushToast('success', t('Created folder {{path}}', { path }));
        await refresh([currentDir, getWorkspaceParentPath(path)]);
      } else if (current.target && path !== current.target) {
        const source = current.target;
        const result = await renameWorkspaceEntry(vesselId, { path: source, newPath: path });
        const next = normalizeWorkspacePath(result.newPath || path);
        setOpenPaths((cur) => cur.map((p) => remapWorkspaceScopedPath(p, source, next)));
        setSelected((cur) => cur.map((p) => remapWorkspaceScopedPath(p, source, next)));
        setFiles((cur) => remapWorkspaceFileMap(cur, source, next));
        setDrafts((cur) => remapWorkspaceStringMap(cur, source, next));
        if (activePath && isWorkspacePathInScope(activePath, source)) setActivePath(remapWorkspaceScopedPath(activePath, source, next));
        const dir = isWorkspacePathInScope(currentDir, source) ? remapWorkspaceScopedPath(currentDir, source, next) : currentDir;
        setCurrentDir(dir);
        setEntriesByDir({});
        pushToast('success', t('Renamed {{path}}', { path: result.path }));
        await refresh([dir, getWorkspaceParentPath(next)]);
      }
    } catch (e) {
      setError(errorMessage(e) || (current.kind === 'folder' ? t('Failed to create folder.') : t('Rename failed.')));
    }
  }

  async function remove(path: string) {
    setConfirmDelete(null);
    try {
      await deleteWorkspaceEntry(vesselId, path);
      setOpenPaths((cur) => {
        const next = cur.filter((p) => !isWorkspacePathInScope(p, path));
        if (activePath && isWorkspacePathInScope(activePath, path)) setActivePath(next[next.length - 1] ?? null);
        return next;
      });
      setSelected((cur) => cur.filter((p) => !isWorkspacePathInScope(p, path)));
      setFiles((cur) => pruneWorkspaceFileMap(cur, path));
      setDrafts((cur) => pruneWorkspaceStringMap(cur, path));
      const dir = isWorkspacePathInScope(currentDir, path) ? getWorkspaceParentPath(path) : currentDir;
      setCurrentDir(dir);
      setEntriesByDir({});
      pushToast('warning', t('Deleted {{path}}', { path }));
      await refresh([dir]);
    } catch (e) {
      setError(errorMessage(e) || t('Delete failed.'));
    }
  }

  async function buildSnippet(): Promise<string | null> {
    if (actionable.length === 0) return null;
    const list = await Promise.all(actionable.slice(0, 6).map(async (path) => {
      const cached = files[path];
      if (cached && !cached.isBinary && !cached.isLarge) return { path, content: drafts[path] ?? cached.content };
      const response = await getWorkspaceFile(vesselId, path);
      return response.isBinary || response.isLarge ? null : { path, content: response.content };
    }));
    const usable = list.filter((f): f is { path: string; content: string } => !!f && !!f.content);
    return usable.length ? buildWorkspaceContextSnippet(usable) : null;
  }

  function plan() {
    if (!vessel || actionable.length === 0) return;
    const draft = buildWorkspacePlanningDraft(vessel, actionable);
    router.push({ pathname: '/planning', params: { from: 'workspace', vesselId: vessel.id, fleetId: vessel.fleetId ?? '', title: draft.title, prompt: draft.prompt } } as unknown as Href);
  }
  function dispatch() {
    if (!vessel || actionable.length === 0) return;
    const draft = buildWorkspaceDispatchDraft(vessel, actionable);
    router.push(dispatchHref('workspace', { vesselId: vessel.id, voyageTitle: draft.title, prompt: draft.prompt }) as Href);
  }
  function runCheck() {
    if (!vessel) return;
    router.push({
      pathname: '/delivery',
      params: { tab: 'checks', vesselId: vessel.id, branch: status?.branchName || vessel.defaultBranch || '', label: actionable.length > 0 ? `${vessel.name}: ${actionable[0]}` : vessel.name },
    } as unknown as Href);
  }
  const toggleSelected = (path: string) => setSelected((cur) => (cur.includes(path) ? cur.filter((p) => p !== path) : [...cur, path]));
  const suggestedParent = activePath ? getWorkspaceParentPath(activePath) : currentDir;

  const panels: HubTab<WorkspacePanel>[] = [
    { key: 'files', label: t('Files') },
    { key: 'search', label: t('Search') },
    { key: 'changes', label: t('Changes') },
    { key: 'diff', label: t('Diff') },
    { key: 'terminal', label: t('Terminal') },
  ];

  const entries = entriesByDir[currentDir] ?? [];
  const crumbs = currentDir ? currentDir.split('/') : [];
  const fileList = (
    <View testID="workspace-files">
      <ActionRow>
        <Button label={t('New File')} variant="secondary" icon="document-outline" onPress={() => setPrompt({ kind: 'file', initial: suggestedParent ? `${suggestedParent}/new-file.txt` : 'new-file.txt' })} testID="workspace-new-file" />
        <Button label={t('New Folder')} variant="secondary" icon="folder-outline" onPress={() => setPrompt({ kind: 'folder', initial: suggestedParent ? `${suggestedParent}/new-folder` : 'new-folder' })} testID="workspace-new-folder" />
      </ActionRow>
      <View style={[styles.crumbs, styles.pad]} accessibilityLabel={t('Folder')}>
        <AppText variant="caption" color="primary" accessibilityRole="link" onPress={() => void enterDir('')}>{vessel?.name || t('Root')}</AppText>
        {crumbs.map((part, i) => (
          <AppText key={`${part}-${i}`} variant="caption" color={i === crumbs.length - 1 ? 'text' : 'primary'} accessibilityRole="link" onPress={() => void enterDir(crumbs.slice(0, i + 1).join('/'))}>
            {` / ${part}`}
          </AppText>
        ))}
      </View>
      {currentDir ? (
        <Pressable accessibilityRole="button" accessibilityLabel={t('Up one folder')} onPress={() => void enterDir(getWorkspaceParentPath(currentDir))} style={[styles.entry, { borderBottomColor: colors.border }]} testID="workspace-up">
          <Icon name="arrow-up" color="textMuted" />
          <AppText style={styles.flex}>..</AppText>
        </Pressable>
      ) : null}
      {loadingDir ? <LoadingState label={t('Loading...')} /> : null}
      {!loadingDir && entries.length === 0 && !loading ? <AppText muted style={styles.pad}>{t('This folder is empty.')}</AppText> : null}
      {entries.map((entry) => {
        const isSel = selected.includes(entry.relativePath);
        const actions = [
          { key: 'rename', label: t('Rename'), icon: 'create-outline' as const, onPress: () => setPrompt({ kind: 'rename', initial: entry.relativePath, target: entry.relativePath }) },
          { key: 'info', label: t('Info'), icon: 'information-circle-outline' as const, onPress: () => setMetadata(entry) },
          { key: 'delete', label: t('Delete'), icon: 'trash-outline' as const, tone: 'danger' as const, onPress: () => setConfirmDelete(entry.relativePath) },
        ];
        return (
          <SwipeRow key={entry.relativePath} actions={actions} testID={`workspace-entry-${entry.relativePath}`}>
            <Pressable
              accessibilityRole="button"
              accessibilityLabel={`${entry.isDirectory ? t('Folder') : t('File')} ${entry.name}`}
              accessibilityHint={t('Long press to select')}
              accessibilityState={{ selected: isSel }}
              onPress={() => (entry.isDirectory ? void enterDir(entry.relativePath) : void openFile(entry.relativePath))}
              onLongPress={() => toggleSelected(entry.relativePath)}
              style={({ pressed }) => [styles.entry, { borderBottomColor: colors.border, backgroundColor: isSel ? colors.surfaceRaised : pressed ? colors.background : colors.surface }, activePath === entry.relativePath ? { borderLeftWidth: 3, borderLeftColor: colors.primary } : null]}
              testID={`workspace-open-${entry.name}`}
            >
              <Icon name={isSel ? 'checkbox' : entry.isDirectory ? 'folder-outline' : 'document-text-outline'} color={isSel ? 'primary' : 'textMuted'} />
              <AppText style={styles.flex} numberOfLines={1}>{entry.name}</AppText>
              {!entry.isDirectory && isDirty(entry.relativePath) ? <AppText color="warning">*</AppText> : null}
              {entry.isDirectory ? <Icon name="chevron-forward" size={18} color="textMuted" /> : null}
            </Pressable>
          </SwipeRow>
        );
      })}
    </View>
  );

  const editor = activePath && activeFile ? (
    <View testID="workspace-editor">
      {openPaths.length > 0 ? (
        <ScrollView horizontal showsHorizontalScrollIndicator={false} contentContainerStyle={styles.tabs}>
          {openPaths.map((p) => (
            <View key={p} style={[styles.tab, { borderColor: p === activePath ? colors.primary : colors.border }]}>
              <AppText variant="caption" accessibilityRole="button" onPress={() => setActivePath(p)}>{`${getWorkspaceName(p)}${isDirty(p) ? ' *' : ''}`}</AppText>
              <Pressable accessibilityRole="button" accessibilityLabel={t('Close {{name}}', { name: getWorkspaceName(p) })} onPress={() => closeFile(p)} hitSlop={8}>
                <Icon name="close" size={16} color="textMuted" />
              </Pressable>
            </View>
          ))}
        </ScrollView>
      ) : null}
      <AppText variant="caption" muted selectable style={[styles.pad, typography.mono]}>{activeFile.path}</AppText>
      <ActionRow>
        {!isTablet ? <Button label={t('Files')} variant="ghost" icon="chevron-back" onPress={() => setActivePath(null)} testID="workspace-back-to-files" /> : null}
        <Button label={t('Save')} busy={saving} disabled={!activeFile.isEditable || !isDirty(activePath)} onPress={() => void save()} testID="workspace-save" />
        <Button label={t('Rename')} variant="secondary" onPress={() => setPrompt({ kind: 'rename', initial: activePath, target: activePath })} />
        <Button label={t('Delete')} variant="danger" onPress={() => setConfirmDelete(activePath)} testID="workspace-delete" />
      </ActionRow>
      {activeFile.isEditable ? (
        <TextInput
          value={activeDraft}
          onChangeText={(v) => setDrafts((cur) => ({ ...cur, [activePath]: v }))}
          multiline
          scrollEnabled={false}
          autoCapitalize="none"
          autoCorrect={false}
          spellCheck={false}
          textAlignVertical="top"
          accessibilityLabel={activeFile.path}
          style={[typography.mono, styles.editor, { color: colors.text, backgroundColor: colors.surface, borderColor: colors.control }]}
          testID="workspace-editor-input"
        />
      ) : (
        <View>
          <Banner
            tone="warning"
            title={activeFile.isBinary
              ? t('This file is binary and cannot be edited in Workspace.')
              : activeFile.isLarge ? t('This file is too large for editing in Workspace. A preview is shown below.') : t('This file is read-only in Workspace.')}
          />
          <View style={[styles.editor, { borderColor: colors.border, backgroundColor: colors.surface }]}>
            <AppText selectable style={typography.mono}>{activeFile.content}</AppText>
          </View>
        </View>
      )}
    </View>
  ) : (
    <EmptyState icon="document-text-outline" title={t('Open a file to begin')} message={t('Browse the vessel tree, then select a file to start editing, planning, or dispatching scoped work.')} />
  );

  let filesBody;
  if (isTablet) {
    filesBody = (
      <View style={styles.split}>
        <View style={[styles.master, { borderRightColor: colors.border }]}>{fileList}</View>
        <View style={styles.flex}>{editor}</View>
      </View>
    );
  } else {
    filesBody = activePath && activeFile ? editor : fileList;
  }

  const readinessState = readinessTone(readiness);
  return (
    <View style={[styles.flex, { backgroundColor: colors.background }]} testID="workspace">
      <Stack.Screen options={{ title: vessel?.name || t('Workspace') }} />
      <ScrollView
        keyboardShouldPersistTaps="handled"
        contentContainerStyle={styles.content}
        refreshControl={<RefreshControl refreshing={refreshing} tintColor={colors.primary} onRefresh={async () => { setRefreshing(true); await refresh([currentDir]); setRefreshing(false); }} />}
      >
        <View style={[styles.pills, styles.pad]}>
          <StatusBadge label={status?.branchName || t('No branch')} tone="info" />
          <StatusBadge label={status?.isDirty ? t('Dirty working tree') : t('Clean working tree')} tone={status?.isDirty ? 'warning' : 'success'} />
          <StatusBadge label={`${status?.activeMissionCount ?? 0} ${t('active mission(s)')}`} tone="running" />
          <StatusBadge label={`${status?.commitsAhead ?? 0} ${t('ahead')} / ${status?.commitsBehind ?? 0} ${t('behind')}`} tone="pending" />
        </View>
        {vessels.length > 1 ? (
          <View style={styles.pad}>
            <SelectField
              label={t('Switch vessel')}
              value={vesselId}
              options={vessels.map((v) => ({ value: v.id, label: v.name }))}
              onChange={(id) => router.replace(`/workspace/${encodeURIComponent(id)}` as Href)}
              closeLabel={t('Close')}
              testID="workspace-switch"
            />
          </View>
        ) : null}
        <ActionRow>
          <Button label={t('Refresh')} variant="ghost" icon="refresh" onPress={() => void refresh([currentDir])} />
          <Button label={t('Context')} variant="secondary" disabled={!vessel} onPress={() => setContextOpen(true)} testID="workspace-context-open" />
          <Button label={t('Plan')} variant="secondary" disabled={actionable.length === 0} onPress={plan} />
          <Button label={t('Dispatch')} variant="secondary" disabled={actionable.length === 0} onPress={dispatch} />
          <Button label={t('Run Check')} variant="secondary" onPress={runCheck} />
        </ActionRow>
        {error ? <Banner tone="danger" title={error} /> : null}
        {status && !loading && !status.hasWorkingDirectory ? <Banner tone="warning" title={status.error || t('This vessel does not have a usable working directory.')} /> : null}
        {loading && !status ? <LoadingState label={t('Loading Workspace...')} /> : null}
        <View style={styles.pad}>
          <Disclosure title={`${t('Readiness')}: ${t(readinessLabel(readiness))}`} testID="workspace-readiness">
            {readiness ? (
              <View style={styles.gap}>
                <StatusBadge label={t(readinessLabel(readiness))} tone={readinessState === 'ready' ? 'success' : readinessState === 'error' ? 'failed' : 'warning'} />
                {readinessBranchSummary(readiness) ? <AppText variant="caption">{readinessBranchSummary(readiness)}</AppText> : null}
                {readinessDriftSummary(readiness) ? <AppText variant="caption">{readinessDriftSummary(readiness)}</AppText> : null}
                <AppText variant="caption" muted>{t('{{done}} of {{total}} setup steps done', { done: readiness.setupChecklistSatisfiedCount, total: readiness.setupChecklistTotalCount })}</AppText>
                {readiness.issues.map((issue) => (
                  <AppText key={`${issue.code}-${issue.relatedValue ?? ''}`} variant="caption" color={issue.severity === 'Error' ? 'danger' : 'warning'}>{`${issue.title}: ${issue.message}`}</AppText>
                ))}
              </View>
            ) : <AppText variant="caption" muted>{t('Readiness data is not available for this vessel yet.')}</AppText>}
          </Disclosure>
        </View>
        {selected.length > 0 ? (
          <View style={[styles.selection, { borderColor: colors.primary, backgroundColor: colors.surface }]} testID="workspace-selection">
            <AppText variant="label">{t('Selection')}</AppText>
            {selected.map((p) => <AppText key={p} variant="caption" style={typography.mono}>{p}</AppText>)}
            {overlapping.length > 0 ? (
              <View>
                <AppText variant="label" color="warning">{t('Active mission overlap')}</AppText>
                {overlapping.map((m) => (
                  <AppText key={m.missionId} variant="caption" color="primary" accessibilityRole="link" onPress={() => router.push(`/missions/${encodeURIComponent(m.missionId)}` as Href)}>{`${m.title} (${m.status})`}</AppText>
                ))}
              </View>
            ) : null}
            <Button label={t('Clear selection')} variant="ghost" onPress={() => setSelected([])} />
          </View>
        ) : null}
        <View style={styles.tabBar}>
          <HubTabBar tabs={panels} value={panel} onChange={(p) => { setPanel(p); if (p !== 'diff') setDiffPath(null); }} label={t('Workspace panels')} />
        </View>
        {panel === 'files' ? filesBody : null}
        {panel === 'search' ? <SearchPanel vesselId={vesselId} onOpenFile={(p) => void openFile(p)} /> : null}
        {panel === 'changes' ? <ChangesPanel vesselId={vesselId} onOpenDiff={(p) => { setDiffPath(p); setPanel('diff'); }} onOpenFile={(p) => void openFile(p)} /> : null}
        {panel === 'diff' ? <DiffPanel vesselId={vesselId} path={diffPath} onClearPath={() => setDiffPath(null)} /> : null}
        {panel === 'terminal' ? <TerminalPanel vesselId={vesselId} /> : null}
      </ScrollView>

      {prompt ? (
        <PathPromptSheet
          title={prompt.kind === 'file' ? t('New File') : prompt.kind === 'folder' ? t('New Folder') : t('Rename')}
          label={prompt.kind === 'file' ? t('New file path') : prompt.kind === 'folder' ? t('New folder path') : t('Rename or move path')}
          initialValue={prompt.initial}
          confirmLabel={prompt.kind === 'rename' ? t('Rename') : t('Create')}
          onSubmit={(v) => void submitPrompt(v)}
          onClose={() => setPrompt(null)}
          testID="workspace-prompt"
        />
      ) : null}
      <ConfirmDialog
        open={confirmDelete !== null}
        title={t('Delete')}
        message={confirmDelete ? t('Delete {{path}}?', { path: confirmDelete }) : ''}
        confirmLabel={t('Delete')}
        cancelLabel={t('Cancel')}
        danger
        onConfirm={() => { if (confirmDelete) void remove(confirmDelete); }}
        onCancel={() => setConfirmDelete(null)}
        testID="workspace-delete-confirm"
      />
      {metadata ? (
        <BottomSheet open title={t('Entry Metadata')} onClose={() => setMetadata(null)} closeLabel={t('Close')} testID="workspace-metadata">
          <InfoRow label={t('Name')} value={metadata.name} />
          <InfoRow label={t('Path')} value={metadata.relativePath} mono />
          <InfoRow label={t('Type')} value={metadata.isDirectory ? t('Folder') : t('File')} />
          <InfoRow label={t('Editable')} value={metadata.isEditable ? t('Yes') : t('No')} />
          <InfoRow label={t('Size')} value={typeof metadata.sizeBytes === 'number' ? t('{{count}} bytes', { count: metadata.sizeBytes }) : t('Directory')} />
          <InfoRow label={t('Modified')} value={formatDateTime(metadata.lastWriteUtc)} />
          {!metadata.isDirectory && files[metadata.relativePath] ? <InfoRow label={t('Language')} value={files[metadata.relativePath].language} /> : null}
          <ActionRow>
            <Button label={selected.includes(metadata.relativePath) ? t('Unselect') : t('Select')} variant="secondary" onPress={() => { toggleSelected(metadata.relativePath); setMetadata(null); }} />
          </ActionRow>
        </BottomSheet>
      ) : null}
      {contextOpen && vessel ? (
        <WorkspaceContextSheet
          vessel={vessel}
          canAppend={actionable.length > 0}
          buildSnippet={buildSnippet}
          onSaved={(updated) => { setVessels((cur) => cur.map((v) => (v.id === updated.id ? updated : v))); setContextOpen(false); }}
          onClose={() => setContextOpen(false)}
        />
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  content: { paddingVertical: spacing.md, flexGrow: 1 },
  pad: { paddingHorizontal: spacing.lg },
  gap: { gap: spacing.xs },
  pills: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.xs, marginBottom: spacing.md },
  crumbs: { flexDirection: 'row', flexWrap: 'wrap', marginBottom: spacing.sm },
  entry: { flexDirection: 'row', alignItems: 'center', gap: spacing.md, paddingHorizontal: spacing.lg, minHeight: MIN_TOUCH, borderBottomWidth: StyleSheet.hairlineWidth },
  tabs: { gap: spacing.sm, paddingHorizontal: spacing.lg, paddingBottom: spacing.sm },
  tab: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm, borderWidth: 1, borderRadius: radius.pill, paddingHorizontal: spacing.md, minHeight: 36 },
  editor: { marginHorizontal: spacing.md, borderWidth: StyleSheet.hairlineWidth, borderRadius: radius.md, padding: spacing.md, minHeight: 240 },
  selection: { borderWidth: 1, borderRadius: radius.md, padding: spacing.md, marginHorizontal: spacing.md, marginVertical: spacing.sm, gap: spacing.xs },
  tabBar: { marginVertical: spacing.md },
  split: { flexDirection: 'row', alignItems: 'flex-start' },
  master: { width: 360, borderRightWidth: StyleSheet.hairlineWidth },
});

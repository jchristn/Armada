import { useEffect, useMemo, useRef, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import {
  createWorkspaceDirectory,
  deleteWorkspaceEntry,
  getVesselReadiness,
  getWorkspaceFile,
  getWorkspaceStatus,
  listAllWorkspaceActiveMissions,
  listWorkspaceDirectory,
  listVessels,
  openWorkspaceTerminal,
  renameWorkspaceEntry,
  saveWorkspaceFile,
  updateVesselContext,
  isApiStatus,
} from '../api/client';
import { listAll } from '../api/paging';
import type {
  Vessel,
  WorkspaceActiveMission,
  WorkspaceFileResponse,
  WorkspaceSaveResult,
  WorkspaceStatusResult,
  WorkspaceTreeEntry,
  VesselReadinessResult,
} from '../types/models';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';
import { copyToClipboard } from '../components/shared/CopyButton';
import WorkspaceReadinessStrip from '../components/workspace/WorkspaceReadinessStrip';
import type { AskNavigationState } from '../lib/askNavigation';
import WorkspaceTree from '../components/workspace/WorkspaceTree';
import WorkspaceVesselPicker from '../components/workspace/WorkspaceVesselPicker';
import WorkspaceDiff from '../components/workspace/WorkspaceDiff';
import {
  buildWorkspaceAskDraft,
  buildWorkspaceContextSnippet,
  buildWorkspaceDispatchDraft,
  buildWorkspacePlanningDraft,
  collapseWorkspacePathMap,
  createDraftFile,
  getExpandedWorkspacePaths,
  getWorkspaceName,
  getWorkspaceParentPath,
  isWorkspacePathInScope,
  normalizeWorkspacePath,
  pruneWorkspaceEntryDirectoryMap,
  pruneWorkspaceFileMap,
  pruneWorkspacePathMap,
  pruneWorkspaceStringMap,
  remapWorkspaceEntryDirectoryMap,
  remapWorkspaceFileMap,
  remapWorkspacePathMap,
  remapWorkspaceScopedPath,
  remapWorkspaceStringMap,
  sortWorkspacePathsByDepth,
} from '../lib/workspace';
import { PLANNING_PATH } from '../lib/navTargets';
import { useDeleteProgress } from '../lib/useDeleteProgress';

interface PersistedWorkspaceState {
  expandedPaths: Record<string, boolean>;
  recentFiles: string[];
}

interface WorkspacePlanningState {
  fromWorkspace: true;
  vesselId: string;
  fleetId?: string;
  title?: string;
  initialPrompt: string;
}

interface WorkspaceDispatchState {
  fromWorkspace: true;
  vesselId: string;
  prompt: string;
  voyageTitle?: string;
}

interface WorkspaceCheckState {
  prefill: Partial<import('../types/models').CheckRunRequest>;
}

const LAST_VESSEL_KEY = 'armada_workspace_last_vessel';
const RECENT_VESSELS_KEY = 'armada_workspace_recent_vessels';

function getPersistedWorkspaceKey(vesselId: string) {
  return `armada_workspace_state_${vesselId}`;
}

type ContextField = 'projectContext' | 'styleGuide' | 'modelContext';

export default function Workspace() {
  const { vesselId } = useParams<{ vesselId?: string }>();
  const navigate = useNavigate();
  const { t, formatDateTime } = useLocale();
  const deletion = useDeleteProgress();
  const { pushToast } = useNotifications();

  const [vessels, setVessels] = useState<Vessel[]>([]);
  const [statusByVesselId, setStatusByVesselId] = useState<Record<string, WorkspaceStatusResult | undefined>>({});

  const [status, setStatus] = useState<WorkspaceStatusResult | null>(null);
  const [activeMissions, setActiveMissions] = useState<WorkspaceActiveMission[]>([]);
  const [readiness, setReadiness] = useState<VesselReadinessResult | null>(null);
  const [entriesByDirectory, setEntriesByDirectory] = useState<Record<string, WorkspaceTreeEntry[]>>({});
  const [expandedPaths, setExpandedPaths] = useState<Record<string, boolean>>({ '': true });
  const [loadingDirectories, setLoadingDirectories] = useState<string[]>([]);
  const [tabs, setTabs] = useState<string[]>([]);
  const [activePath, setActivePath] = useState<string | null>(null);
  const [filesByPath, setFilesByPath] = useState<Record<string, WorkspaceFileResponse>>({});
  const [draftsByPath, setDraftsByPath] = useState<Record<string, string>>({});
  const [recentFiles, setRecentFiles] = useState<string[]>([]);
  const [selectedPaths, setSelectedPaths] = useState<string[]>([]);
  const [loadingWorkspace, setLoadingWorkspace] = useState(false);
  const [loadingReadiness, setLoadingReadiness] = useState(false);
  const [openingTerminal, setOpeningTerminal] = useState(false);
  const [workspaceError, setWorkspaceError] = useState('');
  const [showContextModal, setShowContextModal] = useState(false);
  const [metadataEntry, setMetadataEntry] = useState<WorkspaceTreeEntry | null>(null);
  const [contextDrafts, setContextDrafts] = useState<Record<ContextField, string>>({
    projectContext: '',
    styleGuide: '',
    modelContext: '',
  });
  const [savingContext, setSavingContext] = useState(false);
  const editorRef = useRef<HTMLTextAreaElement | null>(null);
  const editorLineNumberRef = useRef<HTMLDivElement | null>(null);
  const readonlyLineNumberRef = useRef<HTMLDivElement | null>(null);
  const restoreExpandedPathsRef = useRef<string[]>([]);
  const workspaceStateHydratedRef = useRef(false);

  const currentVessel = useMemo(
    () => vessels.find((item) => item.id === vesselId) || null,
    [vesselId, vessels],
  );
  const activeFile = activePath ? filesByPath[activePath] || null : null;
  const activeDraft = activePath ? (draftsByPath[activePath] ?? activeFile?.content ?? '') : '';
  const activeLineNumbers = useMemo(() => {
    const source = activePath ? (draftsByPath[activePath] ?? activeFile?.content ?? '') : '';
    const lineCount = Math.max(1, source.split('\n').length);
    return Array.from({ length: lineCount }, (_, index) => index + 1);
  }, [activeFile?.content, activePath, draftsByPath]);
  const actionablePaths = useMemo(() => {
    const normalizedSelection = selectedPaths.map(normalizeWorkspacePath).filter(Boolean);
    if (normalizedSelection.length > 0) return normalizedSelection;
    return activePath ? [normalizeWorkspacePath(activePath)] : [];
  }, [activePath, selectedPaths]);
  const overlappingMissions = useMemo(() => {
    if (!activeMissions.length || actionablePaths.length === 0) return [];
    return activeMissions.filter((mission) =>
      (mission.scopedFiles ?? []).some((path) => actionablePaths.includes(normalizeWorkspacePath(path))),
    );
  }, [actionablePaths, activeMissions]);
  const showSelectionPane = selectedPaths.length > 0;

  useEffect(() => {
    if (!showContextModal || !currentVessel) return;
    setContextDrafts({
      projectContext: currentVessel.projectContext || '',
      styleGuide: currentVessel.styleGuide || '',
      modelContext: currentVessel.modelContext || '',
    });
  }, [currentVessel, showContextModal]);

  useEffect(() => {
    let cancelled = false;
    listAll(listVessels)
      .then((result) => {
        if (cancelled) return;
        setVessels(result.objects);
      })
      .catch((error: unknown) => {
        if (cancelled) return;
        setWorkspaceError(error instanceof Error ? error.message : t('Failed to load vessels.'));
      });

    return () => {
      cancelled = true;
    };
  }, [t]);

  useEffect(() => {
    if (vesselId) return;
    const recentIds = readRecentVesselIds();
    const previewIds = [...recentIds, ...vessels.map((vessel) => vessel.id)]
      .filter((id, index, array) => array.indexOf(id) === index)
      .slice(0, 12);

    if (previewIds.length === 0) return;

    let cancelled = false;
    Promise.all(previewIds.map(async (id) => {
      try {
        return [id, await getWorkspaceStatus(id)] as const;
      } catch {
        return [id, undefined] as const;
      }
    })).then((results) => {
      if (cancelled) return;
      const next = Object.fromEntries(results);
      setStatusByVesselId((current) => ({ ...current, ...next }));
    });

    return () => {
      cancelled = true;
    };
  }, [vesselId, vessels]);

  useEffect(() => {
    if (!vesselId) {
      setStatus(null);
      setActiveMissions([]);
      setReadiness(null);
      setEntriesByDirectory({});
      setTabs([]);
      setActivePath(null);
      setFilesByPath({});
      setDraftsByPath({});
      setRecentFiles([]);
      setSelectedPaths([]);
      setExpandedPaths({ '': true });
      setLoadingWorkspace(false);
      restoreExpandedPathsRef.current = [];
      workspaceStateHydratedRef.current = false;
      return;
    }

    const persisted = readPersistedState(vesselId);
    workspaceStateHydratedRef.current = false;
    restoreExpandedPathsRef.current = getExpandedWorkspacePaths({ '': true, ...(persisted?.expandedPaths || {}) })
      .filter((path) => path !== '');
    setTabs([]);
    setActivePath(null);
    setExpandedPaths({ '': true });
    setSelectedPaths([]);
    setRecentFiles(persisted?.recentFiles || []);
    setFilesByPath({});
    setDraftsByPath({});
    setEntriesByDirectory({});
    setLoadingDirectories([]);
    setWorkspaceError('');
  }, [vesselId]);

  useEffect(() => {
    if (!vesselId) {
      setReadiness(null);
      setLoadingReadiness(false);
      return;
    }

    let cancelled = false;
    setLoadingReadiness(true);
    getVesselReadiness(vesselId)
      .then((value) => {
        if (!cancelled) setReadiness(value);
      })
      .catch(() => {
        if (!cancelled) setReadiness(null);
      })
      .finally(() => {
        if (!cancelled) setLoadingReadiness(false);
      });

    return () => {
      cancelled = true;
    };
  }, [vesselId]);

  useEffect(() => {
    if (!vesselId) return;
    if (!workspaceStateHydratedRef.current) return;
    persistWorkspaceState(vesselId, { expandedPaths, recentFiles });
  }, [expandedPaths, recentFiles, vesselId]);

  useEffect(() => {
    if (!vesselId) return;
    rememberVessel(vesselId);
    void refreshWorkspace(restoreExpandedPathsRef.current);
  }, [vesselId]);

  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (!activePath || !activeFile?.isEditable) return;
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 's') {
        event.preventDefault();
        void handleSaveActiveFile();
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [activeFile?.isEditable, activePath, draftsByPath, filesByPath]);

  async function refreshWorkspace(expandedPathOverrides?: string[]) {
    if (!vesselId) return;
    setLoadingWorkspace(true);
    let rebuilt = false;
    try {
      await rebuildWorkspaceState(expandedPathOverrides ?? getExpandedWorkspacePaths(expandedPaths).filter((path) => path !== ''));
      rebuilt = true;
    } catch (error: unknown) {
      setWorkspaceError(error instanceof Error ? error.message : t('Failed to load Workspace.'));
    } finally {
      if (rebuilt) {
        workspaceStateHydratedRef.current = true;
      }
      setLoadingWorkspace(false);
    }
  }

  async function rebuildWorkspaceState(pathsToRestore: string[]) {
    if (!vesselId) return;
    const nextStatus = await getWorkspaceStatus(vesselId);
    // The status carries only activeMissionCount; the missions (with scoped files) are paged explicitly.
    const nextActiveMissions = (nextStatus?.activeMissionCount ?? 0) > 0
      ? await listAllWorkspaceActiveMissions(vesselId).catch(() => [] as WorkspaceActiveMission[])
      : [];
    // Each directory is read in explicit pages of 1000; the first page is never treated as the whole directory.
    const rootTree = await listWorkspaceDirectory(vesselId, undefined);
    const nextEntries: Record<string, WorkspaceTreeEntry[]> = { '': rootTree.entries };
    const nextExpanded: Record<string, boolean> = { '': true };
    const normalizedPaths = sortWorkspacePathsByDepth(pathsToRestore);

    for (const path of normalizedPaths) {
      const normalizedPath = normalizeWorkspacePath(path);
      if (!normalizedPath) continue;

      const parentPath = getWorkspaceParentPath(normalizedPath);
      if (parentPath && !nextExpanded[parentPath]) continue;

      try {
        const tree = await listWorkspaceDirectory(vesselId, normalizedPath);
        nextEntries[normalizedPath] = tree.entries;
        nextExpanded[normalizedPath] = true;
      } catch {
        // Skip directories that cannot be restored.
      }
    }

    setStatus(nextStatus);
    setActiveMissions(nextActiveMissions);
    setStatusByVesselId((current) => ({ ...current, [vesselId]: nextStatus }));
    setEntriesByDirectory(nextEntries);
    setExpandedPaths(nextExpanded);
    setLoadingDirectories([]);
    setWorkspaceError('');
  }

  async function refreshExpandedDirectories(paths: string[]) {
    if (!vesselId) return;
    const uniquePaths = paths
      .map(normalizeWorkspacePath)
      .filter((path, index, array) => array.indexOf(path) === index);

    const results = await Promise.all(uniquePaths.map(async (path) => {
      try {
        const tree = await listWorkspaceDirectory(vesselId, path || undefined);
        return [path, tree.entries] as const;
      } catch {
        return [path, [] as WorkspaceTreeEntry[]] as const;
      }
    }));

    setEntriesByDirectory((current) => ({
      ...current,
      ...Object.fromEntries(results),
    }));
  }

  async function loadDirectory(path: string): Promise<boolean> {
    if (!vesselId) return false;
    const normalizedPath = normalizeWorkspacePath(path);
    if (loadingDirectories.includes(normalizedPath)) return false;

    try {
      setLoadingDirectories((current) => [...current, normalizedPath]);
      const tree = await listWorkspaceDirectory(vesselId, normalizedPath || undefined);
      setEntriesByDirectory((current) => ({ ...current, [normalizedPath]: tree.entries }));
      setWorkspaceError('');
      return true;
    } catch (error: unknown) {
      const message = error instanceof Error ? error.message : t('Failed to load directory.');
      if (isApiStatus(error, 404)) {
        setExpandedPaths((current) => {
          const next = { ...current };
          delete next[normalizedPath];
          return next;
        });
        setEntriesByDirectory((current) => {
          const next = { ...current };
          delete next[normalizedPath];
          return next;
        });
      } else {
        setWorkspaceError(message);
      }
      return false;
    } finally {
      setLoadingDirectories((current) => current.filter((value) => value !== normalizedPath));
    }
  }

  async function handleToggleDirectory(path: string) {
    const normalizedPath = normalizeWorkspacePath(path);
    if (expandedPaths[normalizedPath]) {
      setExpandedPaths((current) => collapseWorkspacePathMap(current, normalizedPath));
      setWorkspaceError('');
      return;
    }

    if (Object.prototype.hasOwnProperty.call(entriesByDirectory, normalizedPath)) {
      setExpandedPaths((current) => ({ ...current, [normalizedPath]: true }));
      setWorkspaceError('');
      return;
    }

    const canExpand = normalizedPath === ''
      ? true
      : await loadDirectory(normalizedPath);

    if (!canExpand) return;

    setExpandedPaths((current) => ({ ...current, [normalizedPath]: true }));
    setWorkspaceError('');
  }

  async function openFile(path: string) {
    if (!vesselId) return;
    const normalizedPath = normalizeWorkspacePath(path);

    if (!filesByPath[normalizedPath]) {
      try {
        const file = await getWorkspaceFile(vesselId, normalizedPath);
        setFilesByPath((current) => ({ ...current, [normalizedPath]: file }));
        setDraftsByPath((current) => ({ ...current, [normalizedPath]: current[normalizedPath] ?? file.content }));
        setWorkspaceError('');
      } catch (error: unknown) {
        setWorkspaceError(error instanceof Error ? error.message : t('Failed to open file.'));
        return;
      }
    }

    setTabs((current) => current.includes(normalizedPath) ? current : [...current, normalizedPath]);
    setActivePath(normalizedPath);
    setRecentFiles((current) => [normalizedPath, ...current.filter((item) => item !== normalizedPath)].slice(0, 12));
    setWorkspaceError('');
    rememberVessel(vesselId);
  }

  function closeTab(path: string) {
    const normalizedPath = normalizeWorkspacePath(path);
    setTabs((current) => {
      const next = current.filter((item) => item !== normalizedPath);
      if (activePath === normalizedPath) {
        setActivePath(next[next.length - 1] || null);
      }
      return next;
    });
  }

  function updateDraft(path: string, value: string) {
    const normalizedPath = normalizeWorkspacePath(path);
    setDraftsByPath((current) => ({ ...current, [normalizedPath]: value }));
  }

  async function handleSaveActiveFile() {
    if (!vesselId || !activePath || !activeFile?.isEditable) return;
    const content = draftsByPath[activePath] ?? activeFile.content;

    try {
      const result = await saveWorkspaceFile(vesselId, {
        path: activePath,
        content,
        expectedHash: activeFile.contentHash || null,
      });
      applySaveResult(activePath, content, result);
      setWorkspaceError('');
      pushToast('success', t('Saved {{path}}', { path: activePath }));
      await refreshWorkspace();
    } catch (error: unknown) {
      const message = error instanceof Error ? error.message : t('Save failed.');
      setWorkspaceError(message);
      pushToast('warning', message);
    }
  }

  function applySaveResult(path: string, content: string, result: WorkspaceSaveResult) {
    setFilesByPath((current) => ({
      ...current,
      [path]: {
        ...(current[path] || createDraftFile(path)),
        path,
        name: getWorkspaceName(path),
        content,
        contentHash: result.contentHash,
        sizeBytes: result.sizeBytes,
        lastWriteUtc: result.lastWriteUtc,
        isEditable: true,
        isBinary: false,
        isLarge: false,
        previewTruncated: false,
      },
    }));
    setDraftsByPath((current) => ({ ...current, [path]: content }));
  }

  async function handleCreateFile() {
    const suggestedParent = activePath ? getWorkspaceParentPath(activePath) : '';
    const initialValue = suggestedParent ? `${suggestedParent}/new-file.txt` : 'new-file.txt';
    const value = window.prompt(t('New file path'), initialValue);
    if (!value) return;

    const normalizedPath = normalizeWorkspacePath(value);
    const draft = createDraftFile(normalizedPath);
    setFilesByPath((current) => ({ ...current, [normalizedPath]: draft }));
    setDraftsByPath((current) => ({ ...current, [normalizedPath]: '' }));
    setTabs((current) => current.includes(normalizedPath) ? current : [...current, normalizedPath]);
    setActivePath(normalizedPath);
    setSelectedPaths((current) => current.includes(normalizedPath) ? current : [...current, normalizedPath]);

    const parent = getWorkspaceParentPath(normalizedPath);
    setExpandedPaths((current) => ({ ...current, [parent]: true }));
    await refreshExpandedDirectories([parent || '', ...Object.keys(expandedPaths).filter((path) => expandedPaths[path])]);
  }

  async function handleCreateDirectory() {
    if (!vesselId) return;
    const suggestedParent = activePath ? getWorkspaceParentPath(activePath) : '';
    const initialValue = suggestedParent ? `${suggestedParent}/new-folder` : 'new-folder';
    const value = window.prompt(t('New folder path'), initialValue);
    if (!value) return;

    try {
      await createWorkspaceDirectory(vesselId, { path: normalizeWorkspacePath(value) });
      setWorkspaceError('');
      pushToast('success', t('Created folder {{path}}', { path: normalizeWorkspacePath(value) }));
      await refreshWorkspace();
    } catch (error: unknown) {
      setWorkspaceError(error instanceof Error ? error.message : t('Failed to create folder.'));
    }
  }

  async function handleRenameEntry() {
    const targetPath = activePath || selectedPaths[0];
    if (!targetPath) return;
    await handleRenamePath(targetPath);
  }

  async function handleRenamePath(targetPath: string) {
    if (!vesselId) return;
    const normalizedTargetPath = normalizeWorkspacePath(targetPath);
    const value = window.prompt(t('Rename or move path'), normalizedTargetPath);
    if (!value || normalizeWorkspacePath(value) === normalizedTargetPath) return;

    try {
      const result = await renameWorkspaceEntry(vesselId, {
        path: normalizedTargetPath,
        newPath: normalizeWorkspacePath(value),
      });
      const nextPath = normalizeWorkspacePath(result.newPath || value);
      const nextExpandedPaths = remapWorkspacePathMap(expandedPaths, normalizedTargetPath, nextPath);
      setExpandedPaths(nextExpandedPaths);
      setEntriesByDirectory((current) => remapWorkspaceEntryDirectoryMap(current, normalizedTargetPath, nextPath));
      setTabs((current) => current.map((item) => remapWorkspaceScopedPath(item, normalizedTargetPath, nextPath)));
      setSelectedPaths((current) => current.map((item) => remapWorkspaceScopedPath(item, normalizedTargetPath, nextPath)));
      setRecentFiles((current) => current.map((item) => remapWorkspaceScopedPath(item, normalizedTargetPath, nextPath)));
      setFilesByPath((current) => remapWorkspaceFileMap(current, normalizedTargetPath, nextPath));
      setDraftsByPath((current) => remapWorkspaceStringMap(current, normalizedTargetPath, nextPath));
      if (activePath && isWorkspacePathInScope(activePath, normalizedTargetPath)) {
        setActivePath(remapWorkspaceScopedPath(activePath, normalizedTargetPath, nextPath));
      }
      if (metadataEntry && isWorkspacePathInScope(metadataEntry.relativePath, normalizedTargetPath)) {
        const remappedPath = remapWorkspaceScopedPath(metadataEntry.relativePath, normalizedTargetPath, nextPath);
        setMetadataEntry({
          ...metadataEntry,
          name: getWorkspaceName(remappedPath),
          relativePath: remappedPath,
        });
      }
      setWorkspaceError('');
      pushToast('success', t('Renamed {{path}}', { path: result.path }));
      await refreshWorkspace(getExpandedWorkspacePaths(nextExpandedPaths).filter((path) => path !== ''));
    } catch (error: unknown) {
      setWorkspaceError(error instanceof Error ? error.message : t('Rename failed.'));
    }
  }

  async function handleDeleteEntry() {
    const targetPath = activePath || selectedPaths[0];
    if (!targetPath) return;
    await handleDeletePath(targetPath);
  }

  async function handleDeletePath(targetPath: string) {
    if (!vesselId) return;
    const normalizedTargetPath = normalizeWorkspacePath(targetPath);
    if (!window.confirm(t('Delete {{path}}?', { path: normalizedTargetPath }))) return;

    const done = await deletion.run({
      kind: 'workspaceEntry',
      action: () => deleteWorkspaceEntry(vesselId, normalizedTargetPath),
      errorMessage: (error) => (error instanceof Error ? error.message : t('Delete failed.')),
    });
    if (!done) return;
    const nextExpandedPaths = pruneWorkspacePathMap(expandedPaths, normalizedTargetPath);
    setExpandedPaths(nextExpandedPaths);
    setEntriesByDirectory((current) => pruneWorkspaceEntryDirectoryMap(current, normalizedTargetPath));
    setTabs((current) => {
      const next = current.filter((item) => !isWorkspacePathInScope(item, normalizedTargetPath));
      if (activePath && isWorkspacePathInScope(activePath, normalizedTargetPath)) {
        setActivePath(next[next.length - 1] || null);
      }
      return next;
    });
    setSelectedPaths((current) => current.filter((item) => !isWorkspacePathInScope(item, normalizedTargetPath)));
    setRecentFiles((current) => current.filter((item) => !isWorkspacePathInScope(item, normalizedTargetPath)));
    setFilesByPath((current) => pruneWorkspaceFileMap(current, normalizedTargetPath));
    setDraftsByPath((current) => pruneWorkspaceStringMap(current, normalizedTargetPath));
    if (metadataEntry && isWorkspacePathInScope(metadataEntry.relativePath, normalizedTargetPath)) {
      setMetadataEntry(null);
    }
    setWorkspaceError('');
    pushToast('warning', t('Deleted {{path}}', { path: normalizedTargetPath }));
    try {
      await refreshWorkspace(getExpandedWorkspacePaths(nextExpandedPaths).filter((path) => path !== ''));
    } catch (error: unknown) {
      setWorkspaceError(error instanceof Error ? error.message : t('Delete failed.'));
    }
  }

  function openContextModal() {
    if (!currentVessel) return;
    setContextDrafts({
      projectContext: currentVessel.projectContext || '',
      styleGuide: currentVessel.styleGuide || '',
      modelContext: currentVessel.modelContext || '',
    });
    setShowContextModal(true);
  }

  async function buildSelectionContextSnippet(): Promise<string | null> {
    if (!currentVessel || actionablePaths.length === 0) return null;

    const files = await Promise.all(actionablePaths.slice(0, 6).map(async (path) => {
      const cached = filesByPath[path];
      if (cached && !cached.isBinary && !cached.isLarge) {
        return { path, content: draftsByPath[path] ?? cached.content };
      }

      const response = await getWorkspaceFile(currentVessel.id, path);
      if (response.isBinary || response.isLarge) return null;
      return { path, content: response.content };
    }));

    const usableFiles = files.filter((file): file is { path: string; content: string } => !!file && !!file.content);
    if (usableFiles.length === 0) return null;
    return buildWorkspaceContextSnippet(usableFiles);
  }

  async function appendSelectionToContextDraft(target: ContextField) {
    try {
      const snippet = await buildSelectionContextSnippet();
      if (!snippet) {
        pushToast('warning', t('No text files were available for context curation.'));
        return;
      }

      setContextDrafts((current) => {
        const existing = current[target].trim();
        return {
          ...current,
          [target]: existing ? `${existing}\n\n${snippet}` : snippet,
        };
      });
      setWorkspaceError('');
    } catch (error: unknown) {
      setWorkspaceError(error instanceof Error ? error.message : t('Failed to build context from selection.'));
    }
  }

  async function handleSaveContextModal() {
    if (!currentVessel) return;

    try {
      setSavingContext(true);
      const updated = await updateVesselContext(currentVessel.id, {
        projectContext: contextDrafts.projectContext,
        styleGuide: contextDrafts.styleGuide,
        modelContext: contextDrafts.modelContext,
      });
      setVessels((current) => current.map((item) => item.id === updated.id ? updated : item));
      setWorkspaceError('');
      setShowContextModal(false);
      pushToast('success', t('Saved vessel context.'));
    } catch (error: unknown) {
      setWorkspaceError(error instanceof Error ? error.message : t('Failed to save vessel context.'));
    } finally {
      setSavingContext(false);
    }
  }

  function handlePlanSelection() {
    if (!currentVessel || actionablePaths.length === 0) return;
    const draft = buildWorkspacePlanningDraft(currentVessel, actionablePaths);
    const state: WorkspacePlanningState = {
      fromWorkspace: true,
      vesselId: currentVessel.id,
      fleetId: currentVessel.fleetId || undefined,
      title: draft.title,
      initialPrompt: draft.prompt,
    };
    navigate(PLANNING_PATH, { state });
  }

  function handleDispatchSelection() {
    if (!currentVessel || actionablePaths.length === 0) return;
    const draft = buildWorkspaceDispatchDraft(currentVessel, actionablePaths);
    const state: WorkspaceDispatchState = {
      fromWorkspace: true,
      vesselId: currentVessel.id,
      prompt: draft.prompt,
      voyageTitle: draft.title,
    };
    navigate('/dispatch', { state });
  }

  async function handleOpenTerminal() {
    if (!currentVessel || openingTerminal) return;
    setOpeningTerminal(true);
    let path: string | null = readiness?.checkoutPath ?? null;
    let reason = '';
    try {
      const result = await openWorkspaceTerminal(currentVessel.id);
      if (result.opened) {
        pushToast('success', result.hostName
          ? t('Opened a terminal at {{path}} on {{host}}.', { path: result.path || '', host: result.hostName })
          : t('Opened a terminal at {{path}}.', { path: result.path || '' }));
        return;
      }
      path = result.path || path;
      reason = result.message || '';
    } catch (error: unknown) {
      reason = error instanceof Error && error.message ? error.message : t('Could not open a terminal.');
    } finally {
      setOpeningTerminal(false);
    }

    // No terminal could be opened from here: hand over the cd command instead.
    if (!path) {
      pushToast('warning', reason || t('This vessel has no checkout to open a terminal in.'));
      return;
    }
    try {
      await copyToClipboard(`cd "${path}"`);
      pushToast('info', `${reason ? reason + ' ' : ''}${t('Copied a cd command for {{path}} to the clipboard; paste it in a terminal.', { path })}`);
    } catch {
      pushToast('warning', `${reason ? reason + ' ' : ''}${t('The checkout is at {{path}}.', { path })}`);
    }
  }

  function handleAskArmada() {
    if (!currentVessel) return;
    const draft = buildWorkspaceAskDraft(currentVessel, actionablePaths, readiness?.checkoutPath, readiness?.harborName);
    const state: AskNavigationState = { draft };
    navigate('/ask', { state });
  }

  function handleRunCheck() {
    if (!currentVessel) return;
    const state: WorkspaceCheckState = {
      prefill: {
        vesselId: currentVessel.id,
        branchName: status?.branchName || currentVessel.defaultBranch || null,
        label: actionablePaths.length > 0 ? `${currentVessel.name}: ${actionablePaths[0]}` : currentVessel.name,
      },
    };
    navigate('/checks', { state });
  }

  function handleOpenHomeVessel(nextVesselId: string) {
    navigate(`/workspace/${nextVesselId}`);
  }

  function isDirty(path: string): boolean {
    const file = filesByPath[path];
    if (!file) return false;
    return (draftsByPath[path] ?? file.content) !== file.content;
  }

  function toggleSelectedPath(path: string) {
    const normalizedPath = normalizeWorkspacePath(path);
    setSelectedPaths((current) =>
      current.includes(normalizedPath)
        ? current.filter((item) => item !== normalizedPath)
        : [...current, normalizedPath],
    );
  }

  function syncLineNumberScroll(ref: { current: HTMLDivElement | null }, top: number) {
    if (!ref.current) return;
    ref.current.scrollTop = top;
  }

  const recentVesselIds = readRecentVesselIds();
  const hasUnsavedChanges = activePath ? isDirty(activePath) : false;

  if (!vesselId) {
    return (
      <WorkspaceVesselPicker
        vessels={vessels}
        recentVesselIds={recentVesselIds}
        statusByVesselId={statusByVesselId}
        onOpen={handleOpenHomeVessel}
      />
    );
  }

  return (
    <div className="workspace-page">
      {deletion.dialog}
      <div className="breadcrumbs">
        <Link to="/workspace" title={t('Back to the Workspace vessel picker.')}>{t('Workspace')}</Link>
        <span className="bc-sep">&gt;</span>
        <span>{currentVessel?.name || vesselId}</span>
      </div>

      <div className="workspace-header">
        <div className="workspace-header-body">
          <h2>{currentVessel?.name || t('Workspace')}</h2>
          <div className="workspace-header-row">
            <div className="workspace-header-status">
              <span className="workspace-status-pill" title={t('Branch currently checked out in the vessel working directory.')}>{status?.branchName || t('No branch')}</span>
              <span className="workspace-status-pill" title={status?.isDirty ? t('The working directory has uncommitted changes. Review them in Review Diff below.') : t('The working directory has no uncommitted changes.')}>{status?.isDirty ? t('Dirty working tree') : t('Clean working tree')}</span>
              <span className="workspace-status-pill" title={t('Missions currently running on this vessel. Selecting a file one of them is changing shows a warning in the Selection pane.')}>{status?.activeMissionCount || 0} {t('active mission(s)')}</span>
              <span className="workspace-status-pill" title={t('Commits the checked-out branch has that its remote tracking branch lacks (ahead), and commits it is missing (behind).')}>{status?.commitsAhead ?? 0} {t('ahead')} / {status?.commitsBehind ?? 0} {t('behind')}</span>
            </div>

            <div className="workspace-header-actions">
              <div className="workspace-header-toolbar">
                <button type="button" className="btn btn-sm" onClick={() => void refreshWorkspace()} title={t('Reload the file tree and git status from disk. Unsaved edits in open tabs are kept.')}>{t('Refresh')}</button>
                <button type="button" className="btn btn-sm btn-primary" onClick={() => void handleSaveActiveFile()} disabled={!activePath || !activeFile?.isEditable || !hasUnsavedChanges} title={t('Write the active tab to disk in the working directory. Refused if the file changed on disk since you opened it.')}>
                  {t('Save')}
                </button>
                <button type="button" className="btn btn-sm" onClick={handleRunCheck} title={t('Go to Checks with this vessel and its current branch filled in, to run a build, test or other check.')}>{t('Run Check')}</button>
                <button type="button" className="btn btn-sm" onClick={handlePlanSelection} disabled={actionablePaths.length === 0} title={t('Start a planning chat with a captain, pre-filled with the selected files (or the active tab), to work out the change before dispatching.')}>{t('Plan')}</button>
                <button type="button" className="btn btn-sm" onClick={handleDispatchSelection} disabled={actionablePaths.length === 0} title={t('Go to Dispatch with a mission draft scoped to the selected files (or the active tab). Nothing runs until you submit it there.')}>{t('Dispatch')}</button>
                <button type="button" className="btn btn-sm" onClick={() => void handleOpenTerminal()} disabled={openingTerminal} title={t('Open a terminal window at this vessel\'s checkout, on the Harbor that holds it or on the Admiral host. Where that is not possible, a cd command for the checkout is copied instead.')}>{openingTerminal ? t('Opening...') : t('Open in Terminal')}</button>
                <button type="button" className="btn btn-sm" onClick={handleAskArmada} title={t('Start an Ask Armada conversation about this vessel, with its checkout and the selected files (or the active tab) filled in for you to finish and send.')}>{t('Ask Armada')}</button>
                <button type="button" className="btn btn-sm" onClick={openContextModal} title={t('Edit this vessel Project Context, Style Guide and Model Context, which are added to every captain prompt for it.')}>{t('Context')}</button>
              </div>

              <select aria-label={t('Switch vessel')}
                className="workspace-header-select"
                value={vesselId}
                onChange={(event) => navigate(`/workspace/${event.target.value}`)}
                title={t('Open a different vessel in Workspace. Unsaved edits in the open tabs are discarded, so save first.')}
              >
                {vessels.map((vessel) => (
                  <option key={vessel.id} value={vessel.id}>{vessel.name}</option>
                ))}
              </select>
            </div>
          </div>
        </div>
      </div>

      {workspaceError && (
        <div className="alert alert-error" style={{ marginBottom: '1rem' }}>
          {workspaceError}
        </div>
      )}

      {status && !loadingWorkspace && !status.hasWorkingDirectory && (
        <div className="alert alert-warning" style={{ marginBottom: '1rem' }}>
          {status?.error || t('This vessel does not have a usable working directory.')}
        </div>
      )}

      <WorkspaceReadinessStrip readiness={readiness} loading={loadingReadiness} />

      <div className={`workspace-shell${showSelectionPane ? ' has-selection-pane' : ''}`}>
        <aside className="workspace-pane workspace-pane-left">
          <div className="workspace-pane-header">
            <h3 title={t('Files and folders in the vessel working directory. Check files to select them for Plan, Dispatch and Context.')}>{t('Files')}</h3>
            <div className="workspace-mini-actions">
              <button type="button" className="btn btn-sm" onClick={() => void handleCreateFile()} title={t('Create an empty file. You are asked for its path relative to the repository root, e.g. docs/notes.md.')}>{t('New File')}</button>
              <button type="button" className="btn btn-sm" onClick={() => void handleCreateDirectory()} title={t('Create a folder. You are asked for its path relative to the repository root, e.g. docs/guides.')}>{t('New Folder')}</button>
            </div>
          </div>

          <WorkspaceTree
            entriesByDirectory={entriesByDirectory}
            expandedPaths={expandedPaths}
            activePath={activePath}
            selectedPaths={selectedPaths}
            loadingDirectories={loadingDirectories}
            onToggleDirectory={(path) => void handleToggleDirectory(path)}
            onOpenFile={(path) => void openFile(path)}
            onToggleSelect={toggleSelectedPath}
            onRenameEntry={(entry) => void handleRenamePath(entry.relativePath)}
            onDeleteEntry={(entry) => void handleDeletePath(entry.relativePath)}
            onViewMetadata={setMetadataEntry}
          />
        </aside>

        <section className="workspace-pane workspace-pane-center">
          {tabs.length > 0 && (
            <div className="workspace-tabs">
              {tabs.map((path) => (
                <div key={path} className={`workspace-tab${activePath === path ? ' active' : ''}`}>
                  <button type="button" onClick={() => setActivePath(path)} title={isDirty(path) ? t('{{path}} (unsaved changes)', { path }) : path}>
                    {getWorkspaceName(path)}
                    {isDirty(path) && <span className="workspace-tab-dirty">*</span>}
                  </button>
                  <button type="button" className="workspace-tab-close" onClick={() => closeTab(path)} title={t('Close the {{name}} tab. Unsaved edits are not written to disk.', { name: getWorkspaceName(path) })} aria-label={t('Close {{name}}', { name: getWorkspaceName(path) })}>x</button>
                </div>
              ))}
            </div>
          )}

          <div className="workspace-editor-shell">
            {activePath && activeFile ? (
              <>
                <div className="workspace-editor-header">
                  <div className="workspace-editor-path" title={activeFile.path}>{activeFile.path}</div>
                  <div className="workspace-mini-actions workspace-editor-actions">
                    <button
                      type="button"
                      className="btn btn-sm btn-primary"
                      onClick={() => void handleSaveActiveFile()}
                      disabled={!activeFile.isEditable || !hasUnsavedChanges}
                      title={t('Write this file to disk in the working directory. Refused if the file changed on disk since you opened it.')}
                    >
                      {t('Save')}
                    </button>
                    <button type="button" className="btn btn-sm" onClick={() => void handleRenameEntry()} title={t('Rename or move this file. Enter a new path relative to the repository root; it moves on disk immediately.')}>{t('Rename')}</button>
                    <button type="button" className="btn btn-sm btn-danger" onClick={() => void handleDeleteEntry()} title={t('Permanently delete this file from the working directory after you confirm. Commit first if you may need it back.')}>{t('Delete')}</button>
                  </div>
                </div>

                {activeFile.isEditable ? (
                  <div className="workspace-editor-surface">
                    <div ref={editorLineNumberRef} className="workspace-editor-gutter" aria-hidden="true">
                      {activeLineNumbers.map((line) => (
                        <div key={line} className="workspace-editor-line-number">{line}</div>
                      ))}
                    </div>
                    <textarea
                      ref={editorRef}
                      className="workspace-editor-input"
                      aria-label={activeFile.path}
                      value={activeDraft}
                      onChange={(event) => updateDraft(activeFile.path, event.target.value)}
                      onScroll={(event) => syncLineNumberScroll(editorLineNumberRef, event.currentTarget.scrollTop)}
                      spellCheck={false}
                      wrap="off"
                      title={t('Editing {{path}}. Changes stay in this tab until you click Save.', { path: activeFile.path })}
                    />
                  </div>
                ) : (
                  <div className="workspace-editor-readonly">
                    <div className="alert alert-warning" style={{ marginBottom: '1rem' }}>
                      {activeFile.isBinary
                        ? t('This file is binary and cannot be edited in Workspace.')
                        : activeFile.isLarge
                          ? t('This file is too large for editing in Workspace. A preview is shown below.')
                          : t('This file is read-only in Workspace.')}
                    </div>
                    <div className="workspace-editor-surface workspace-editor-surface-readonly">
                      <div ref={readonlyLineNumberRef} className="workspace-editor-gutter" aria-hidden="true">
                        {activeLineNumbers.map((line) => (
                          <div key={line} className="workspace-editor-line-number">{line}</div>
                        ))}
                      </div>
                      <div
                        className="workspace-editor-readonly-scroll"
                        onScroll={(event) => syncLineNumberScroll(readonlyLineNumberRef, event.currentTarget.scrollTop)}
                      >
                        <pre className="workspace-editor-readonly-pre">{activeFile.content}</pre>
                      </div>
                    </div>
                  </div>
                )}
              </>
            ) : (
              <div className="workspace-empty-state">
                <h3>{t('Open a file to begin')}</h3>
                <p className="text-muted">
                  {t('Browse the vessel tree, then select a file to start editing, planning, or dispatching scoped work.')}
                </p>
              </div>
            )}
          </div>

        </section>

        {showSelectionPane && (
        <aside className="workspace-pane workspace-pane-right">
          <div className="workspace-pane-header">
            <h3 title={t('Files that Plan, Dispatch and Append Selection act on: the checked files, or the active tab when none are checked.')}>{t('Selection')}</h3>
          </div>

          <div className="workspace-action-rail">
            <div className="workspace-selection-list">
              {actionablePaths.length > 0 ? actionablePaths.map((path) => (
                <div key={path} className="workspace-selection-item">{path}</div>
              )) : <div className="text-dim">{t('Select files in the tree or use the active tab.')}</div>}
            </div>

            {overlappingMissions.length > 0 && (
              <div className="workspace-mission-warning">
                <strong title={t('These running missions are scoped to files you selected. Changing those files now may conflict with what the captains are doing.')}>{t('Active mission overlap')}</strong>
                {overlappingMissions.map((mission) => (
                  <div key={mission.missionId} className="workspace-mission-warning-item">
                    <span>{mission.title}</span>
                    <span className="text-dim" title={t('Current status of the overlapping mission.')}>{mission.status}</span>
                  </div>
                ))}
              </div>
            )}
          </div>
        </aside>
        )}
      </div>

      {loadingWorkspace && !status && (
        <div className="modal-overlay workspace-loading-overlay" role="presentation">
          <div className="modal workspace-loading-modal" role="status" aria-live="polite" aria-modal="true">
            <h3>{t('Loading Workspace...')}</h3>
            <div className="progress-bar" aria-hidden="true" style={{ marginTop: '0.85rem', marginBottom: '0.75rem' }}>
              <div className="progress-fill progress-fill-indeterminate" />
            </div>
            <p className="text-muted">
              {t('Armada is loading the vessel working directory, repository status, and file tree.')}
            </p>
          </div>
        </div>
      )}

      {metadataEntry && (
        <div className="modal-overlay" onClick={() => setMetadataEntry(null)}>
          <div
            className="modal modal-vessel-edit workspace-metadata-modal"
            onClick={(event) => event.stopPropagation()}
          >
            <h3>{t('Entry Metadata')}</h3>
            <div className="workspace-metadata-grid">
              <div className="workspace-metadata-row">
                <span className="workspace-metadata-label">{t('Name')}</span>
                <span className="workspace-metadata-value">{metadataEntry.name}</span>
              </div>
              <div className="workspace-metadata-row">
                <span className="workspace-metadata-label">{t('Path')}</span>
                <span className="workspace-metadata-value mono">{metadataEntry.relativePath}</span>
              </div>
              <div className="workspace-metadata-row">
                <span className="workspace-metadata-label">{t('Type')}</span>
                <span className="workspace-metadata-value">{metadataEntry.isDirectory ? t('Folder') : t('File')}</span>
              </div>
              <div className="workspace-metadata-row">
                <span className="workspace-metadata-label">{t('Editable')}</span>
                <span className="workspace-metadata-value">{metadataEntry.isEditable ? t('Yes') : t('No')}</span>
              </div>
              <div className="workspace-metadata-row">
                <span className="workspace-metadata-label">{t('Size')}</span>
                <span className="workspace-metadata-value">
                  {typeof metadataEntry.sizeBytes === 'number' ? `${metadataEntry.sizeBytes} bytes` : t('Directory')}
                </span>
              </div>
              <div className="workspace-metadata-row">
                <span className="workspace-metadata-label">{t('Modified')}</span>
                <span className="workspace-metadata-value">{formatDateTime(metadataEntry.lastWriteUtc)}</span>
              </div>
              {!metadataEntry.isDirectory && filesByPath[metadataEntry.relativePath] && (
                <div className="workspace-metadata-row">
                  <span className="workspace-metadata-label">{t('Language')}</span>
                  <span className="workspace-metadata-value">{filesByPath[metadataEntry.relativePath].language}</span>
                </div>
              )}
            </div>
            <div className="modal-actions">
              <button type="button" className="btn" onClick={() => setMetadataEntry(null)} title={t('Close the metadata view.')}>{t('Close')}</button>
            </div>
          </div>
        </div>
      )}

      {showContextModal && currentVessel && (
        <div className="modal-overlay" onClick={() => !savingContext && setShowContextModal(false)}>
          <form
            className="modal modal-vessel-edit workspace-context-modal"
            onClick={(event) => event.stopPropagation()}
            onSubmit={(event) => {
              event.preventDefault();
              void handleSaveContextModal();
            }}
          >
            <h3 title={t('Text Armada adds to every captain prompt for this vessel. Saving here updates the vessel, the same as Edit Vessel.')}>{t('Vessel Context')}</h3>
            <p className="text-dim workspace-context-modal-intro">
              {t('Edit the vessel context fields directly, or append the current Workspace selection into any field.')}
            </p>

            <div className="workspace-context-section">
              <div className="workspace-context-section-header">
                <strong title={t('What this project is: purpose, architecture, key files and dependencies.')}>{t('Project Context')}</strong>
                <button type="button" className="btn btn-sm" title={t('Append the contents of up to six selected text files to this field as a snippet. Nothing is saved until you click Save Context.')} onClick={() => void appendSelectionToContextDraft('projectContext')} disabled={actionablePaths.length === 0 || savingContext}>
                  {t('Append Selection')}
                </button>
              </div>
              <textarea
                className="workspace-context-textarea"
                aria-label={t('Project Context')}
                value={contextDrafts.projectContext}
                onChange={(event) => setContextDrafts((current) => ({ ...current, projectContext: event.target.value }))}
                spellCheck={false}
              />
            </div>

            <div className="workspace-context-section">
              <div className="workspace-context-section-header">
                <strong title={t('Coding rules captains must follow here: naming, patterns, language restrictions, preferred libraries.')}>{t('Style Guide')}</strong>
                <button type="button" className="btn btn-sm" title={t('Append the contents of up to six selected text files to this field as a snippet. Nothing is saved until you click Save Context.')} onClick={() => void appendSelectionToContextDraft('styleGuide')} disabled={actionablePaths.length === 0 || savingContext}>
                  {t('Append Selection')}
                </button>
              </div>
              <textarea
                className="workspace-context-textarea"
                aria-label={t('Style Guide')}
                value={contextDrafts.styleGuide}
                onChange={(event) => setContextDrafts((current) => ({ ...current, styleGuide: event.target.value }))}
                spellCheck={false}
              />
            </div>

            <div className="workspace-context-section">
              <div className="workspace-context-section-header">
                <strong title={t('Notes captains have accumulated about this repo during missions, fed to future missions.')}>{t('Model Context')}</strong>
                <button type="button" className="btn btn-sm" title={t('Append the contents of up to six selected text files to this field as a snippet. Nothing is saved until you click Save Context.')} onClick={() => void appendSelectionToContextDraft('modelContext')} disabled={actionablePaths.length === 0 || savingContext}>
                  {t('Append Selection')}
                </button>
              </div>
              <textarea
                className="workspace-context-textarea"
                aria-label={t('Model Context')}
                value={contextDrafts.modelContext}
                onChange={(event) => setContextDrafts((current) => ({ ...current, modelContext: event.target.value }))}
                spellCheck={false}
              />
            </div>

            <div className="modal-actions">
              <button type="button" className="btn" onClick={() => setShowContextModal(false)} disabled={savingContext} title={t('Close without saving changes to the context fields.')}>{t('Cancel')}</button>
              <button type="submit" className="btn btn-primary" disabled={savingContext} title={t('Save all three context fields to the vessel. Missions dispatched from now on use the new text.')}>{savingContext ? t('Saving...') : t('Save Context')}</button>
            </div>
          </form>
        </div>
      )}

      {currentVessel && <WorkspaceDiff vesselId={currentVessel.id} />}
    </div>
  );
}

function readPersistedState(vesselId: string): PersistedWorkspaceState | null {
  try {
    const raw = localStorage.getItem(getPersistedWorkspaceKey(vesselId));
    if (!raw) return null;
    return JSON.parse(raw) as PersistedWorkspaceState;
  } catch {
    return null;
  }
}

function persistWorkspaceState(vesselId: string, state: PersistedWorkspaceState) {
  try {
    localStorage.setItem(getPersistedWorkspaceKey(vesselId), JSON.stringify(state));
  } catch {
    // ignore persistence failures
  }
}

function readRecentVesselIds(): string[] {
  try {
    const raw = localStorage.getItem(RECENT_VESSELS_KEY);
    if (!raw) return [];
    const parsed = JSON.parse(raw);
    return Array.isArray(parsed) ? parsed.filter((item): item is string => typeof item === 'string') : [];
  } catch {
    return [];
  }
}

function rememberVessel(vesselId: string) {
  try {
    localStorage.setItem(LAST_VESSEL_KEY, vesselId);
    const next = [vesselId, ...readRecentVesselIds().filter((item) => item !== vesselId)].slice(0, 8);
    localStorage.setItem(RECENT_VESSELS_KEY, JSON.stringify(next));
  } catch {
    // ignore persistence failures
  }
}

import AsyncStorage from '@react-native-async-storage/async-storage';
import { act, fireEvent, screen, waitFor } from '@testing-library/react-native';
import { Slot } from 'expo-router';
import { renderRouter } from 'expo-router/testing-library';
import { Text } from 'react-native';
import * as client from '@dashboard/api/client';
import type { Vessel, WorkspaceFileResponse, WorkspaceTreeEntry } from '@dashboard/types/models';
import WorkspacePanelRoute from '../app/(app)/(work)/workspace/[vesselId]/[panel]';
import WorkspaceRoute from '../app/(app)/(work)/workspace/[vesselId]/index';
import { WorkspaceTab } from '../screens/workspace/WorkspaceTab';
import { RECENT_WORKSPACE_VESSELS_KEY, readRecentWorkspaceVessels } from '../screens/workspace/recentVessels';
import { BuildProviders, page } from '../test/buildFixtures';

jest.mock('@dashboard/api/client', () => require('../test/buildClientMock').buildClientMockFactory());

const api = client as jest.Mocked<typeof client>;

const VESSEL = { id: 'vsl_1', name: 'demo-api', workingDirectory: '/code/api', defaultBranch: 'main', fleetId: null } as unknown as Vessel;

function entry(relativePath: string, isDirectory = false): WorkspaceTreeEntry {
  return { name: relativePath.split('/').pop() ?? relativePath, relativePath, isDirectory, isEditable: !isDirectory, sizeBytes: isDirectory ? null : 10, lastWriteUtc: '2026-10-07T10:00:00Z' };
}

function file(path: string, content: string, over: Partial<WorkspaceFileResponse> = {}): WorkspaceFileResponse {
  return { vesselId: 'vsl_1', path, name: path.split('/').pop() ?? path, content, contentHash: 'h1', isEditable: true, isBinary: false, isLarge: false, previewTruncated: false, sizeBytes: content.length, lastWriteUtc: '', language: 'markdown', ...over };
}

const ROUTES = {
  _layout: () => <BuildProviders><Slot /></BuildProviders>,
  '(work)/vessels/index': () => <WorkspaceTab />,
  '(work)/workspace/[vesselId]/index': WorkspaceRoute,
  '(work)/workspace/[vesselId]/[panel]': WorkspacePanelRoute,
  '(work)/planning/index': () => <Text>Planning screen</Text>,
};

describe('workspace', () => {
  beforeEach(async () => {
    await AsyncStorage.clear();
    api.listVessels.mockResolvedValue(page([VESSEL]));
    api.getWorkspaceStatus.mockResolvedValue({ vesselId: 'vsl_1', hasWorkingDirectory: true, branchName: 'main', isDirty: false, commitsAhead: 0, commitsBehind: 0, activeMissionCount: 1, activeMissions: [{ missionId: 'msn_1', title: 'Fix docs', status: 'InProgress', scopedFiles: ['README.md'] }] });
    api.getVesselReadiness.mockResolvedValue(null as never);
    api.getWorkspaceTree.mockImplementation(async (_id: string, path?: string) => ({
      vesselId: 'vsl_1', rootPath: '/code/api', currentPath: path ?? '', parentPath: null,
      entries: path === 'src' ? [entry('src/app.ts')] : [entry('src', true), entry('README.md')],
    }));
  });

  it('picks a vessel from the Workspace tab and remembers it', async () => {
    await renderRouter(ROUTES, { initialUrl: '/vessels' });
    await waitFor(() => expect(screen.getByTestId('workspace-open-demo-api')).toBeTruthy());
    expect(screen.getByText('Workspace ready')).toBeTruthy();
    await act(async () => { await fireEvent.press(screen.getByTestId('workspace-open-demo-api')); });
    await waitFor(() => expect(screen.getByTestId('workspace')).toBeTruthy());
    await waitFor(async () => expect(await readRecentWorkspaceVessels()).toEqual(['vsl_1']));
    expect(RECENT_WORKSPACE_VESSELS_KEY).toBe('armada.workspace.recentVessels');
  });

  it('browses folders, edits and saves a file with its content hash', async () => {
    api.getWorkspaceFile.mockResolvedValue(file('README.md', '# Demo'));
    api.saveWorkspaceFile.mockResolvedValue({ path: 'README.md', contentHash: 'h2', sizeBytes: 12, lastWriteUtc: '', created: false });
    await renderRouter(ROUTES, { initialUrl: '/workspace/vsl_1' });
    await waitFor(() => expect(screen.getByTestId('workspace-open-src')).toBeTruthy());
    await act(async () => { await fireEvent.press(screen.getByTestId('workspace-open-src')); });
    await waitFor(() => expect(screen.getByTestId('workspace-open-app.ts')).toBeTruthy());
    expect(api.getWorkspaceTree).toHaveBeenCalledWith('vsl_1', 'src');
    await act(async () => { await fireEvent.press(screen.getByTestId('workspace-up')); });

    await act(async () => { await fireEvent.press(screen.getByTestId('workspace-open-README.md')); });
    await waitFor(() => expect(screen.getByTestId('workspace-editor-input')).toBeTruthy());
    expect(screen.getByTestId('workspace-save')).toBeDisabled();
    await act(async () => { await fireEvent.changeText(screen.getByTestId('workspace-editor-input'), '# Demo\nmore'); });
    await act(async () => { await fireEvent.press(screen.getByTestId('workspace-save')); });
    expect(api.saveWorkspaceFile).toHaveBeenCalledWith('vsl_1', { path: 'README.md', content: '# Demo\nmore', expectedHash: 'h1' });
    await waitFor(() => expect(screen.getByTestId('workspace-save')).toBeDisabled());
  });

  it('creates a folder, renames, and deletes with confirmation', async () => {
    api.createWorkspaceDirectory.mockResolvedValue({ path: 'docs', status: 'Created' });
    api.renameWorkspaceEntry.mockResolvedValue({ path: 'README.md', newPath: 'README2.md', status: 'Renamed' });
    api.deleteWorkspaceEntry.mockResolvedValue({ path: 'src', status: 'Deleted' });
    await renderRouter(ROUTES, { initialUrl: '/workspace/vsl_1' });
    await waitFor(() => expect(screen.getByTestId('workspace-open-src')).toBeTruthy());

    await act(async () => { await fireEvent.press(screen.getByTestId('workspace-new-folder')); });
    await act(async () => { await fireEvent.changeText(screen.getByTestId('workspace-prompt-input'), '/docs/'); });
    await act(async () => { await fireEvent.press(screen.getByTestId('workspace-prompt-submit')); });
    expect(api.createWorkspaceDirectory).toHaveBeenCalledWith('vsl_1', { path: 'docs' });

    await act(async () => { await fireEvent.press(screen.getByTestId('workspace-entry-README.md-rename')); });
    await act(async () => { await fireEvent.changeText(screen.getByTestId('workspace-prompt-input'), 'README2.md'); });
    await act(async () => { await fireEvent.press(screen.getByTestId('workspace-prompt-submit')); });
    expect(api.renameWorkspaceEntry).toHaveBeenCalledWith('vsl_1', { path: 'README.md', newPath: 'README2.md' });

    await act(async () => { await fireEvent.press(screen.getByTestId('workspace-entry-src-delete')); });
    expect(api.deleteWorkspaceEntry).not.toHaveBeenCalled();
    await act(async () => { await fireEvent.press(screen.getByTestId('workspace-delete-confirm-confirm')); });
    expect(api.deleteWorkspaceEntry).toHaveBeenCalledWith('vsl_1', 'src');
  });

  it('warns about active mission overlap for selected files and plans the selection', async () => {
    await renderRouter(ROUTES, { initialUrl: '/workspace/vsl_1' });
    await waitFor(() => expect(screen.getByTestId('workspace-open-README.md')).toBeTruthy());
    await act(async () => { await fireEvent(screen.getByTestId('workspace-open-README.md'), 'longPress'); });
    expect(screen.getByTestId('workspace-selection')).toBeTruthy();
    expect(screen.getByText('Active mission overlap')).toBeTruthy();
    expect(screen.getByText('Fix docs (InProgress)')).toBeTruthy();
    await act(async () => { await fireEvent.press(screen.getByText('Plan')); });
    await waitFor(() => expect(screen.getByText('Planning screen')).toBeTruthy());
  });

  it('opens on a panel: search, changes with a per-file diff, and terminal', async () => {
    api.searchWorkspace.mockResolvedValue({ query: 'demo', totalMatches: 1, truncated: false, matches: [{ path: 'README.md', lineNumber: 1, preview: '# Demo' }] });
    await renderRouter(ROUTES, { initialUrl: '/workspace/vsl_1/search' });
    await waitFor(() => expect(screen.getByTestId('workspace-search')).toBeTruthy());
    await act(async () => { await fireEvent.changeText(screen.getByTestId('workspace-search-input'), 'demo'); });
    await act(async () => { await fireEvent.press(screen.getByTestId('workspace-search-run')); });
    expect(api.searchWorkspace).toHaveBeenCalledWith('vsl_1', 'demo');
    expect(screen.getByText('README.md:1')).toBeTruthy();

    api.getWorkspaceChanges.mockResolvedValue({ branchName: 'main', isDirty: true, commitsAhead: 0, commitsBehind: 0, changes: [{ path: 'README.md', status: 'Modified' }] });
    api.getWorkspaceDiff.mockResolvedValue({ path: 'README.md', diff: 'diff --git a/README.md b/README.md\n@@ -1 +1 @@\n-# Demo\n+# Demo 2\n', error: null });
    await act(async () => { await fireEvent.press(screen.getByTestId('hub-tab-changes')); });
    await waitFor(() => expect(screen.getByTestId('workspace-change-README.md')).toBeTruthy());
    await act(async () => { await fireEvent.press(screen.getByTestId('workspace-change-diff-README.md')); });
    await waitFor(() => expect(api.getWorkspaceDiff).toHaveBeenCalledWith('vsl_1', 'README.md'));
    await waitFor(() => expect(screen.getByText('+# Demo 2')).toBeTruthy());

    api.execWorkspaceCommand.mockResolvedValue({ command: 'git status', workingDirectory: '/code/api', exitCode: 0, stdout: 'clean\n', stderr: '', timedOut: false, durationMs: 12 });
    await act(async () => { await fireEvent.press(screen.getByTestId('hub-tab-terminal')); });
    await act(async () => { await fireEvent.changeText(screen.getByTestId('workspace-terminal-input'), 'git status'); });
    await act(async () => { await fireEvent.press(screen.getByTestId('workspace-terminal-run')); });
    expect(api.execWorkspaceCommand).toHaveBeenCalledWith('vsl_1', 'git status');
    await waitFor(() => expect(screen.getByText('clean')).toBeTruthy());
    expect(screen.getByText(/exit 0/)).toBeTruthy();
  });
});

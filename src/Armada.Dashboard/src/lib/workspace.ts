/**
 * Pure Workspace logic shared by the dashboard (pages/Workspace.tsx) and the mobile app: path normalization, the
 * scoped planning and dispatch drafts, the context snippet, and the path-scope helpers that keep open files,
 * selections, and the expanded tree in step after a rename or delete.
 */
import type { Vessel, WorkspaceFileResponse, WorkspaceTreeEntry } from '../types/models';

export function normalizeWorkspacePath(path?: string | null): string {
  return (path || '').replace(/\\/g, '/').replace(/^\/+|\/+$/g, '');
}

export function getWorkspaceName(path: string): string {
  const normalized = normalizeWorkspacePath(path);
  if (!normalized) return '';
  const parts = normalized.split('/');
  return parts[parts.length - 1] || normalized;
}

export function getWorkspaceParentPath(path: string): string {
  const normalized = normalizeWorkspacePath(path);
  if (!normalized.includes('/')) return '';
  return normalized.substring(0, normalized.lastIndexOf('/'));
}

export function inferWorkspaceLanguage(path: string): string {
  const normalized = normalizeWorkspacePath(path).toLowerCase();
  if (normalized.endsWith('.cs')) return 'csharp';
  if (normalized.endsWith('.csproj') || normalized.endsWith('.xml')) return 'xml';
  if (normalized.endsWith('.md')) return 'markdown';
  if (normalized.endsWith('.json')) return 'json';
  if (normalized.endsWith('.ts') || normalized.endsWith('.tsx')) return 'typescript';
  if (normalized.endsWith('.js') || normalized.endsWith('.jsx')) return 'javascript';
  if (normalized.endsWith('.css')) return 'css';
  if (normalized.endsWith('.html')) return 'html';
  if (normalized.endsWith('.yml') || normalized.endsWith('.yaml')) return 'yaml';
  if (normalized.endsWith('.ps1')) return 'powershell';
  if (normalized.endsWith('.bat')) return 'bat';
  if (normalized.endsWith('.sql')) return 'sql';
  return 'plaintext';
}

export function buildScopedFileDirective(paths: string[]): string {
  const normalized = paths.map(normalizeWorkspacePath).filter(Boolean);
  if (!normalized.length) return '';
  return `Touch only ${normalized.join(', ')}`;
}

export function buildWorkspacePlanningDraft(vessel: Vessel, paths: string[]): { title: string; prompt: string } {
  const directive = buildScopedFileDirective(paths);
  const primary = getWorkspaceName(paths[0] || vessel.name);
  const title = `Plan ${primary}`;
  const prompt = [
    directive,
    '',
    `Help me plan the changes needed in vessel "${vessel.name}".`,
    'Review the selected files, identify likely dependencies, and outline a concrete implementation approach before dispatch.',
    'Call out risks, affected areas, and any follow-up files that may need to be touched if the current scope is too narrow.',
  ].filter(Boolean).join('\n');

  return { title, prompt };
}

export function buildWorkspaceDispatchDraft(vessel: Vessel, paths: string[]): { title: string; prompt: string } {
  const directive = buildScopedFileDirective(paths);
  const primary = getWorkspaceName(paths[0] || vessel.name);
  const title = `Workspace: ${primary}`;
  const prompt = [
    directive,
    '',
    `Implement the requested change in vessel "${vessel.name}".`,
    'Use the selected files as the primary scope. If adjacent files are required, expand carefully and explain why.',
    'Update tests and documentation when the change requires it.',
  ].filter(Boolean).join('\n');

  return { title, prompt };
}

export function buildWorkspaceContextSnippet(files: Array<Pick<WorkspaceFileResponse, 'path' | 'content'>>): string {
  const sections = files.map((file) => [
    `### ${file.path}`,
    '```text',
    file.content.trim(),
    '```',
  ].join('\n'));

  return [
    '## Workspace Selection',
    '',
    ...sections,
  ].join('\n');
}

/** An empty, editable file the user is about to create (not saved yet). */
export function createDraftFile(path: string): WorkspaceFileResponse {
  const normalizedPath = normalizeWorkspacePath(path);
  return {
    vesselId: '',
    path: normalizedPath,
    name: getWorkspaceName(normalizedPath),
    content: '',
    contentHash: '',
    isEditable: true,
    isBinary: false,
    isLarge: false,
    previewTruncated: false,
    sizeBytes: 0,
    lastWriteUtc: new Date().toISOString(),
    language: inferWorkspaceLanguage(normalizedPath),
  };
}

export function getExpandedWorkspacePaths(expandedPaths: Record<string, boolean>) {
  return Object.keys(expandedPaths).filter((path) => expandedPaths[path] || path === '');
}

export function sortWorkspacePathsByDepth(paths: string[]) {
  return paths
    .map(normalizeWorkspacePath)
    .filter((path, index, array) => !!path && array.indexOf(path) === index)
    .sort((left, right) => {
      const depthDifference = getWorkspacePathDepth(left) - getWorkspacePathDepth(right);
      return depthDifference !== 0 ? depthDifference : left.localeCompare(right);
    });
}

export function getWorkspacePathDepth(path: string) {
  if (!path) return 0;
  return path.split('/').length;
}

export function isWorkspacePathInScope(candidatePath: string, scopePath: string) {
  return candidatePath === scopePath || candidatePath.startsWith(`${scopePath}/`);
}

export function remapWorkspaceScopedPath(candidatePath: string, sourcePath: string, nextPath: string) {
  if (candidatePath === sourcePath) return nextPath;
  if (candidatePath.startsWith(`${sourcePath}/`)) {
    return `${nextPath}${candidatePath.slice(sourcePath.length)}`;
  }

  return candidatePath;
}

export function remapWorkspacePathMap(pathMap: Record<string, boolean>, sourcePath: string, nextPath: string) {
  return Object.fromEntries(
    Object.entries(pathMap).map(([path, expanded]) => [remapWorkspaceScopedPath(path, sourcePath, nextPath), expanded]),
  );
}

export function pruneWorkspacePathMap(pathMap: Record<string, boolean>, targetPath: string) {
  return Object.fromEntries(
    Object.entries(pathMap).filter(([path]) => !isWorkspacePathInScope(path, targetPath)),
  );
}

export function collapseWorkspacePathMap(pathMap: Record<string, boolean>, targetPath: string) {
  return Object.fromEntries(
    Object.entries(pathMap).filter(([path]) => path === '' || !isWorkspacePathInScope(path, targetPath)),
  );
}

export function remapWorkspaceFileMap<T extends WorkspaceFileResponse>(fileMap: Record<string, T>, sourcePath: string, nextPath: string) {
  return Object.fromEntries(
    Object.entries(fileMap).map(([path, value]) => {
      const remappedPath = remapWorkspaceScopedPath(path, sourcePath, nextPath);
      return [
        remappedPath,
        {
          ...value,
          path: remappedPath,
          name: getWorkspaceName(remappedPath),
        },
      ];
    }),
  );
}

export function pruneWorkspaceFileMap<T>(fileMap: Record<string, T>, targetPath: string) {
  return Object.fromEntries(
    Object.entries(fileMap).filter(([path]) => !isWorkspacePathInScope(path, targetPath)),
  );
}

export function remapWorkspaceStringMap(valueMap: Record<string, string>, sourcePath: string, nextPath: string) {
  return Object.fromEntries(
    Object.entries(valueMap).map(([path, value]) => [remapWorkspaceScopedPath(path, sourcePath, nextPath), value]),
  );
}

export function pruneWorkspaceStringMap(valueMap: Record<string, string>, targetPath: string) {
  return Object.fromEntries(
    Object.entries(valueMap).filter(([path]) => !isWorkspacePathInScope(path, targetPath)),
  );
}

export function remapWorkspaceEntryDirectoryMap(
  directoryMap: Record<string, WorkspaceTreeEntry[]>,
  sourcePath: string,
  nextPath: string,
) {
  return Object.fromEntries(
    Object.entries(directoryMap).map(([directoryPath, entries]) => [
      remapWorkspaceScopedPath(directoryPath, sourcePath, nextPath),
      entries.map((entry) => {
        if (!isWorkspacePathInScope(entry.relativePath, sourcePath)) {
          return entry;
        }

        const remappedPath = remapWorkspaceScopedPath(entry.relativePath, sourcePath, nextPath);
        return {
          ...entry,
          name: getWorkspaceName(remappedPath),
          relativePath: remappedPath,
        };
      }),
    ]),
  );
}

export function pruneWorkspaceEntryDirectoryMap(
  directoryMap: Record<string, WorkspaceTreeEntry[]>,
  targetPath: string,
) {
  return Object.fromEntries(
    Object.entries(directoryMap)
      .filter(([directoryPath]) => !isWorkspacePathInScope(directoryPath, targetPath))
      .map(([directoryPath, entries]) => [
        directoryPath,
        entries.filter((entry) => !isWorkspacePathInScope(entry.relativePath, targetPath)),
      ]),
  );
}

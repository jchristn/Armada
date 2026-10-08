import { describe, expect, it } from 'vitest';
import type { WorkspaceTreeEntry } from '../types/models';
import {
  collapseWorkspacePathMap,
  createDraftFile,
  getExpandedWorkspacePaths,
  isWorkspacePathInScope,
  pruneWorkspaceEntryDirectoryMap,
  pruneWorkspacePathMap,
  remapWorkspaceEntryDirectoryMap,
  remapWorkspaceScopedPath,
  remapWorkspaceStringMap,
  sortWorkspacePathsByDepth,
} from './workspace';

function entry(relativePath: string, isDirectory = false): WorkspaceTreeEntry {
  return { name: relativePath.split('/').pop() ?? relativePath, relativePath, isDirectory, isEditable: !isDirectory, sizeBytes: null, lastWriteUtc: '' };
}

describe('workspace path scope helpers', () => {
  it('scopes and remaps paths below a renamed folder', () => {
    expect(isWorkspacePathInScope('src/a.ts', 'src')).toBe(true);
    expect(isWorkspacePathInScope('srcx/a.ts', 'src')).toBe(false);
    expect(remapWorkspaceScopedPath('src/a.ts', 'src', 'lib')).toBe('lib/a.ts');
    expect(remapWorkspaceScopedPath('docs/a.md', 'src', 'lib')).toBe('docs/a.md');
    expect(remapWorkspaceStringMap({ 'src/a.ts': 'x' }, 'src', 'lib')).toEqual({ 'lib/a.ts': 'x' });
  });

  it('prunes and collapses expanded folders', () => {
    const expanded = { '': true, src: true, 'src/x': true, docs: true };
    expect(pruneWorkspacePathMap(expanded, 'src')).toEqual({ '': true, docs: true });
    expect(collapseWorkspacePathMap(expanded, '')).toEqual({ '': true, docs: true, src: true, 'src/x': true });
    expect(getExpandedWorkspacePaths({ '': false, a: true, b: false })).toEqual(['', 'a']);
    expect(sortWorkspacePathsByDepth(['a/b/c', '/a/', 'z', 'a/b', 'a'])).toEqual(['a', 'z', 'a/b', 'a/b/c']);
  });

  it('remaps and prunes tree entries', () => {
    const tree = { '': [entry('src', true), entry('README.md')], src: [entry('src/a.ts')] };
    const renamed = remapWorkspaceEntryDirectoryMap(tree, 'src', 'lib');
    expect(Object.keys(renamed)).toEqual(['', 'lib']);
    expect(renamed[''][0]).toMatchObject({ name: 'lib', relativePath: 'lib' });
    expect(renamed.lib[0]).toMatchObject({ name: 'a.ts', relativePath: 'lib/a.ts' });
    expect(pruneWorkspaceEntryDirectoryMap(tree, 'src')).toEqual({ '': [entry('README.md')] });
  });

  it('creates an editable draft file', () => {
    expect(createDraftFile('/docs/new.md')).toMatchObject({ path: 'docs/new.md', name: 'new.md', content: '', isEditable: true, language: 'markdown' });
  });
});

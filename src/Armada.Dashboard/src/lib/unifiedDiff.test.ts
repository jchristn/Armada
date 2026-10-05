import { describe, expect, it } from 'vitest';
import { parseUnifiedDiff, unquoteGitPath } from './unifiedDiff';

describe('parseUnifiedDiff', () => {
  it('counts an added line whose content begins with "++ " as an addition, not a header', () => {
    const diff = [
      'diff --git a/notes.txt b/notes.txt',
      'index 1111111..2222222 100644',
      '--- a/notes.txt',
      '+++ b/notes.txt',
      '@@ -1,3 +1,4 @@',
      ' keep',
      '+++ looks like a header',
      '--- also looks like a header',
      '+plain add',
      ' tail',
      '',
    ].join('\n');
    // Old side: keep, "-- also ...", tail (3). New side: keep, "++ looks ...", "plain add", tail (4).
    const parsed = parseUnifiedDiff(diff);
    expect(parsed.files).toHaveLength(1);
    expect(parsed.files[0]).toMatchObject({ path: 'notes.txt', kind: 'Modified', additions: 2, deletions: 1, isBinary: false });
    const kinds = parsed.lines.map((l) => l.kind);
    expect(kinds.slice(0, 11)).toEqual(['fileHeader', 'meta', 'meta', 'meta', 'hunkHeader', 'context', 'add', 'del', 'add', 'context', 'other']);
    expect(parsed.lines[6]).toMatchObject({ kind: 'add', newNumber: 2 });
    expect(parsed.lines[7]).toMatchObject({ kind: 'del', oldNumber: 2 });
  });

  it('records section ranges so one file can be sliced out of the raw text', () => {
    const diff = [
      'diff --git a/a.txt b/a.txt',
      '--- a/a.txt',
      '+++ b/a.txt',
      '@@ -1 +1 @@',
      '-old',
      '+new',
      'diff --git a/b.txt b/b.txt',
      '--- a/b.txt',
      '+++ b/b.txt',
      '@@ -0,0 +1 @@',
      '+diff --git a/fake b/fake',
    ].join('\n');
    const parsed = parseUnifiedDiff(diff);
    expect(parsed.files.map((f) => [f.path, f.startLine, f.endLine, f.additions, f.deletions])).toEqual([
      ['a.txt', 0, 6, 1, 1],
      ['b.txt', 6, 11, 1, 0],
    ]);
    // The added line that reads like a file header is content inside the hunk.
    expect(parsed.lines[10].kind).toBe('add');
  });

  it('reports a rename with both paths', () => {
    const diff = [
      'diff --git a/old/name.ts b/new/name.ts',
      'similarity index 100%',
      'rename from old/name.ts',
      'rename to new/name.ts',
    ].join('\n');
    const [file] = parseUnifiedDiff(diff).files;
    expect(file).toMatchObject({ kind: 'Renamed', oldPath: 'old/name.ts', newPath: 'new/name.ts', path: 'new/name.ts', additions: 0, deletions: 0 });
  });

  it('reports a deletion by its old path', () => {
    const diff = [
      'diff --git a/gone.txt b/gone.txt',
      'deleted file mode 100644',
      'index 1111111..0000000',
      '--- a/gone.txt',
      '+++ /dev/null',
      '@@ -1,2 +0,0 @@',
      '-line one',
      '-line two',
    ].join('\n');
    const [file] = parseUnifiedDiff(diff).files;
    expect(file).toMatchObject({ kind: 'Deleted', oldPath: 'gone.txt', newPath: null, path: 'gone.txt', additions: 0, deletions: 2 });
  });

  it('decodes C-quoted paths', () => {
    const diff = [
      'diff --git "a/caf\\303\\251 menu.txt" "b/caf\\303\\251 menu.txt"',
      'new file mode 100644',
      '--- /dev/null',
      '+++ "b/caf\\303\\251 menu.txt"',
      '@@ -0,0 +1 @@',
      '+hello',
    ].join('\n');
    const [file] = parseUnifiedDiff(diff).files;
    expect(file).toMatchObject({ kind: 'Added', path: 'caf\u00e9 menu.txt', oldPath: null, additions: 1 });
    expect(unquoteGitPath('"tab\\there"')).toBe('tab\there');
    expect(unquoteGitPath('plain.txt')).toBe('plain.txt');
  });

  it('flags binary sections', () => {
    const diff = [
      'diff --git a/img.png b/img.png',
      'index 1111111..2222222 100644',
      'Binary files a/img.png and b/img.png differ',
    ].join('\n');
    expect(parseUnifiedDiff(diff).files[0]).toMatchObject({ path: 'img.png', isBinary: true });
  });

  it('returns nothing for empty input', () => {
    expect(parseUnifiedDiff('')).toEqual({ files: [], lines: [] });
    expect(parseUnifiedDiff(null)).toEqual({ files: [], lines: [] });
  });
});

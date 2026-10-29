import * as assert from 'assert';
import * as path from 'path';
import { describe, it } from 'node:test';
import * as vscode from 'vscode';
import { BlackBoxDecorationProvider, CONFLICT_BADGE, HOTSPOT_BADGE, relativeInside } from '../../src/decorations/blackBoxDecorationProvider';
import { makeStore, standardMock } from '../fakes/storeHelpers';
import { ThemeColor, Uri } from '../fakes/vscode';

const FOLDER = path.resolve('/work/alpha');

async function provider(): Promise<BlackBoxDecorationProvider> {
  const store = makeStore(standardMock(), { folders: [FOLDER], remotes: { [FOLDER]: ['https://github.com/org/alpha'] } });
  await store.refresh();
  return new BlackBoxDecorationProvider(store);
}

function decorate(p: BlackBoxDecorationProvider, uri: Uri): vscode.FileDecoration | undefined {
  return p.provideFileDecoration(uri as unknown as vscode.Uri) as vscode.FileDecoration | undefined;
}

describe('BlackBoxDecorationProvider', () => {
  it('decorates a conflicted file with the mission and date', async () => {
    const p = await provider();
    const decoration = decorate(p, Uri.file(path.join(FOLDER, 'src', 'b.ts')));
    assert.ok(decoration);
    assert.strictEqual(decoration.badge, CONFLICT_BADGE);
    assert.strictEqual(decoration.tooltip, 'conflicted in msn_x landing, 2026-10-28');
    assert.strictEqual((decoration.color as unknown as ThemeColor).id, 'gitDecoration.conflictingResourceForeground');
    assert.strictEqual(decoration.propagate, false);
  });

  it('mentions other open entries that also conflicted on the file', async () => {
    const p = await provider();
    const decoration = decorate(p, Uri.file(path.join(FOLDER, 'src', 'a.ts')));
    assert.strictEqual(decoration?.tooltip, 'conflicted in msn_x landing, 2026-10-28 (and 1 more open entry)');
  });

  it('decorates a hotspot directory', async () => {
    const p = await provider();
    const decoration = decorate(p, Uri.file(path.join(FOLDER, 'src')));
    assert.strictEqual(decoration?.badge, HOTSPOT_BADGE);
    assert.strictEqual(decoration?.tooltip, 'Armada hotspot: 3 conflicts in black box entries');
  });

  it('leaves other files, other folders and non-file URIs alone', async () => {
    const p = await provider();
    assert.strictEqual(decorate(p, Uri.file(path.join(FOLDER, 'README.md'))), undefined);
    assert.strictEqual(decorate(p, Uri.file(path.resolve('/work/other/src/a.ts'))), undefined);
    assert.strictEqual(decorate(p, Uri.file(FOLDER)), undefined);
    assert.strictEqual(decorate(p, Uri.parse('untitled:Untitled-1')), undefined);
  });

  it('returns nothing when not connected', async () => {
    const store = makeStore(undefined);
    await store.refresh();
    const p = new BlackBoxDecorationProvider(store);
    assert.strictEqual(decorate(p, Uri.file(path.join(FOLDER, 'src', 'a.ts'))), undefined);
  });

  it('fires a change event when the store refreshes', async () => {
    const store = makeStore(standardMock(), { folders: [FOLDER], remotes: { [FOLDER]: ['https://github.com/org/alpha'] } });
    const p = new BlackBoxDecorationProvider(store);
    let fired = 0;
    p.onDidChangeFileDecorations(() => fired++);
    await store.refresh();
    assert.ok(fired >= 1);
    p.dispose();
  });

  it('computes repository-relative paths', () => {
    assert.strictEqual(relativeInside(FOLDER, path.join(FOLDER, 'a', 'b.ts')), 'a/b.ts');
    assert.strictEqual(relativeInside(FOLDER, FOLDER), null);
    assert.strictEqual(relativeInside(FOLDER, path.resolve('/work/alphabet/x')), null);
  });
});

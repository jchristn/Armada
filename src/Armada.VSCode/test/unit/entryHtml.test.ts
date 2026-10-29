import * as assert from 'assert';
import { describe, it } from 'node:test';
import { escapeHtml, isEntryViewMessage, renderEntryHtml } from '../../src/views/entryHtml';
import { fullEntry, summary } from '../fakes/fixtures';

describe('renderEntryHtml', () => {
  it('renders the summary sections, recovery buttons and the nonce', () => {
    const html = renderEntryHtml(fullEntry(), [{ Body: 'checked the merge', AuthorKind: 'Operator', CreatedUtc: '2026-10-28T11:00:00Z' }], 'NONCE123', 'vscode-resource:');
    for (const heading of ['Summary', 'Where', 'How', 'Status', 'Why', 'Conflict', 'Recovery', 'Notes']) {
      assert.ok(html.includes(`<h2>${heading}</h2>`), heading);
    }
    assert.ok(html.includes('The landing merge conflicted with msn_y.'));
    assert.ok(html.includes('data-action="runRecovery" data-path="rp2"'));
    assert.ok(html.includes('data loss risk'));
    assert.ok(html.includes('<script nonce="NONCE123">'));
    assert.ok(html.includes("script-src 'nonce-NONCE123'"));
    assert.ok(html.includes('checked the merge'));
    assert.ok(html.includes('data-action="resolve"'));
    assert.ok(html.includes('Ask Armada about this failure'));
  });

  it('escapes server text', () => {
    const html = renderEntryHtml(fullEntry({ MissionTitle: '<img src=x onerror=alert(1)>', Record: { Analysis: { Summary: '"quoted" & <b>' } } }), [], 'n', 'c');
    assert.ok(!html.includes('<img src=x'));
    assert.ok(html.includes('&lt;img src=x onerror=alert(1)&gt;'));
    assert.ok(html.includes('&quot;quoted&quot; &amp; &lt;b&gt;'));
  });

  it('handles an entry without a record and hides Resolve when resolved', () => {
    const html = renderEntryHtml({ ...summary({ Resolution: 'Resolved' }), Record: null }, [], 'n', 'c');
    assert.ok(html.includes('No analysis summary yet'));
    assert.ok(html.includes('No recovery paths recorded.'));
    assert.ok(!html.includes('data-action="resolve"'));
    assert.ok(!html.includes('<h2>Conflict</h2>'));
  });

  it('contains no em-dashes', () => {
    assert.ok(!renderEntryHtml(fullEntry(), [], 'n', 'c').includes('—'));
  });

  it('validates webview messages', () => {
    assert.ok(isEntryViewMessage({ action: 'copyFixThis' }));
    assert.ok(isEntryViewMessage({ action: 'runRecovery', pathId: 'rp1' }));
    assert.ok(!isEntryViewMessage({ action: 'deleteEverything' }));
    assert.ok(!isEntryViewMessage({ action: 'runRecovery', pathId: 5 }));
    assert.ok(!isEntryViewMessage(null));
    assert.strictEqual(escapeHtml(`<'&">`), '&lt;&#39;&amp;&quot;&gt;');
  });
});

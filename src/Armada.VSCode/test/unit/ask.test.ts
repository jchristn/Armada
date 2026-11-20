import * as assert from 'assert';
import { describe, it } from 'node:test';
import { conversationReducer, initialConversation } from '@dashboard/lib/askConversation';
import { buildChatView } from '../../src/surfaces/ask/askViewModel';
import { renderMarkdown } from '../../src/surfaces/ask/markdown';

describe('ask markdown', () => {
  it('renders the common Markdown the captain writes', () => {
    const html = renderMarkdown('## Plan\n\n1. Fix **this**\n2. Run `npm test`\n\n```ts\nconst a = 1 < 2;\n```\n\n> quoted\n\n| a | b |\n|---|---|\n| 1 | 2 |\n\nSee [docs](https://example.com) or https://armada.dev.');
    assert.ok(html.includes('<h4>Plan</h4>'));
    assert.ok(html.includes('<ol><li>Fix <strong>this</strong></li><li>Run <code>npm test</code></li></ol>'));
    assert.ok(html.includes('<pre><code class="language-ts">const a = 1 &lt; 2;</code></pre>'));
    assert.ok(html.includes('<blockquote><p>quoted</p></blockquote>'));
    assert.ok(html.includes('<th>a</th>') && html.includes('<td>2</td>'));
    assert.ok(html.includes('<a href="https://example.com">docs</a>'));
    assert.ok(html.includes('<a href="https://armada.dev.">https://armada.dev.</a>') || html.includes('<a href="https://armada.dev">'));
  });

  it('never lets the text inject markup or script links', () => {
    const html = renderMarkdown('<img src=x onerror=alert(1)> [x](javascript:alert(1)) <script>bad()</script>');
    assert.ok(!html.includes('<img'));
    assert.ok(!html.includes('<script'));
    assert.ok(!html.includes('href="javascript'));
  });

  it('terminates on unusual input', () => {
    assert.strictEqual(typeof renderMarkdown('```not a fence because of trailing text here'), 'string');
    assert.strictEqual(renderMarkdown(''), '');
    assert.ok(renderMarkdown('---').includes('<hr>'));
  });
});

describe('ask chat view model', () => {
  it('shows messages, the streaming reply and pending decisions from the dashboard conversation state', () => {
    let state = initialConversation('ath_1');
    state = conversationReducer(state, { type: 'optimisticUser', message: { id: 'local-1', threadId: 'ath_1', sequence: 1, role: 'User', kind: 'Text', contentText: 'Hello **there**', isLocal: true } });
    state = conversationReducer(state, { type: 'confirmUser', localId: 'local-1', messageId: 'msg_1', turnId: 'trn_1' });
    state = conversationReducer(state, { type: 'event', event: { type: 'ask.chunk', threadId: 'ath_1', turnId: 'trn_1', delta: 'Working on it' } });
    const view = buildChatView(state, true, null);
    assert.strictEqual(view.messages.length, 1);
    assert.strictEqual(view.messages[0].role, 'user');
    assert.ok(view.messages[0].html.includes('<strong>there</strong>'));
    assert.ok(view.turnActive);
    assert.ok(view.streaming?.html.includes('Working on it'));
    state = conversationReducer(state, { type: 'proposal', proposal: { id: 'prp_1', threadId: 'ath_1', messageId: 'msg_2', toolName: 'dispatch', summaryText: 'Dispatch a mission', source: 'Captain', status: 'Pending' } });
    state = conversationReducer(state, { type: 'event', event: { type: 'ask.message', threadId: 'ath_1', message: { id: 'msg_2', threadId: 'ath_1', sequence: 2, role: 'Assistant', kind: 'ActionProposal', contentText: 'I can do that', proposalId: 'prp_1' } } });
    const card = buildChatView(state, true, null).messages.find((m) => m.id === 'msg_2')?.card;
    assert.deepStrictEqual(card && { kind: card.kind, title: card.title, pending: card.pending }, { kind: 'proposal', title: 'Dispatch a mission', pending: true });
    assert.strictEqual(buildChatView(initialConversation(), false, 'boom').error, 'boom');
  });
});

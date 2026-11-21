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

describe('ask reply telemetry', () => {
  it('formats the metrics of a reply on one line and in detail, and totals the conversation', async () => {
    const { formatMetrics } = await import('../../src/surfaces/ask/askViewModel');
    const metrics = { timeToFirstTokenMs: 1234, timeToFirstTextMs: 1500, streamingMs: 8300, totalMs: 9900, promptTokens: 1234, completionTokens: 567, totalTokens: 1801, tokensPerSecond: 58.27, cachedTokens: 200, costUsd: 0.0123, toolCallCount: 3, toolTimeMs: 1200 };
    assert.strictEqual(formatMetrics(metrics, null, false), 'TTFT 1.23s · first text 1.50s · streaming 8.30s · total 9.90s · 1,234 in · 567 out · 200 cached · 58.3 tok/s · cost $0.012 · tools 3 (1.20s)');
    assert.match(formatMetrics(metrics, null, true), /^Time to first token: 1\.23s\nTime to first text: 1\.50s\n/);
    assert.strictEqual(formatMetrics({ ...metrics, tokensEstimated: true, timeToFirstTextMs: null, cachedTokens: 0, costUsd: null, toolCallCount: 0 } as never, null, false), 'TTFT 1.23s · streaming 8.30s · total 9.90s · ~1,234 in · ~567 out · 58.3 tok/s');
    assert.strictEqual(formatMetrics(null, null, false), '');
    assert.strictEqual(formatMetrics(null, 450, false), 'total 450 ms');
    let state = initialConversation('ath_1');
    state = conversationReducer(state, { type: 'loaded', threadId: 'ath_1', detail: { thread: { id: 'ath_1', title: 'T', captainId: null, autoApprove: false, pinned: false, archived: false } } as never, hasMore: false, messages: [
      { id: 'm1', threadId: 'ath_1', sequence: 1, role: 'User', kind: 'Text', contentText: 'hi' },
      { id: 'm2', threadId: 'ath_1', sequence: 2, role: 'Assistant', kind: 'Text', contentText: 'hello', metrics: metrics as never },
      { id: 'm3', threadId: 'ath_1', sequence: 3, role: 'Assistant', kind: 'Text', contentText: 'again', metrics: { ...metrics, costUsd: 0.01 } as never },
    ] });
    const view = buildChatView(state, true, null);
    assert.deepStrictEqual(view.totals, { replies: 2, promptTokens: 2468, completionTokens: 1134, cachedTokens: 400, costUsd: 0.0223, estimated: false });
    assert.strictEqual(view.messages[0].metrics, '', 'user messages carry no telemetry');
    assert.match(view.messages[1].metrics, /^TTFT 1\.23s/);
  });
});

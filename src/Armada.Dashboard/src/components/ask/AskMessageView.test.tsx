import { fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import AskMessageView from './AskMessageView';
import AskMessageList from './AskMessageList';
import { translateTemplate } from '../../i18n/runtime';
import type { AskMessage } from '../../types/models';

const localeValue = {
  t: (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', text, null, params),
  locale: 'en',
  formatDateTime: (v: string | null | undefined) => v ?? '',
  formatRelativeTime: (v: string | null | undefined) => (v ? 'recently' : ''),
};
vi.mock('../../context/LocaleContext', () => ({ useLocale: () => localeValue }));

function reply(overrides: Partial<AskMessage> = {}): AskMessage {
  return {
    id: 'amg_1',
    threadId: 'ath_1',
    sequence: 2,
    role: 'Assistant',
    kind: 'Text',
    contentText: 'All quiet.',
    captainId: 'cpt_1',
    durationMs: 2500,
    createdUtc: '2026-10-08T12:00:00Z',
    toolCalls: [{ id: 'tc1', callId: 'c1', toolName: 'armada_status', ok: true, resultText: '{}', elapsedMs: 250 }],
    ...overrides,
  };
}

const handlers = { proposal: null, proposalBusy: false, onApprove: () => undefined, onReject: () => undefined };

function popoverRows(): string[][] {
  const dialog = screen.getByRole('dialog', { name: 'Turn statistics' });
  return Array.from(dialog.querySelectorAll('.chat-metrics-row')).map((row) => [
    row.querySelector('.chat-metrics-row-label')?.textContent ?? '',
    row.querySelector('.chat-metrics-row-value')?.textContent ?? '',
  ]);
}

describe('AskMessageView turn statistics', () => {
  it('a captain reply with recorded telemetry opens the full statistics popover, like a Planning reply', () => {
    render(
      <AskMessageView
        {...handlers}
        captainName="Ada"
        message={reply({
          metrics: {
            timeToFirstTokenMs: 400,
            timeToFirstTextMs: 1000,
            streamingMs: 2100,
            totalMs: 2500,
            promptTokens: 1050,
            completionTokens: 120,
            totalTokens: 1170,
            tokensPerSecond: 57.14,
            cachedTokens: 900,
            costUsd: 0.0123,
            tokensEstimated: false,
            toolCallCount: 1,
            toolTimeMs: 250,
          },
        })}
      />,
    );

    const button = screen.getByRole('button', { name: 'Turn statistics' });
    expect(button.getAttribute('aria-expanded')).toBe('false');
    fireEvent.click(button);
    expect(button.getAttribute('aria-expanded')).toBe('true');
    expect(popoverRows()).toEqual([
      ['time to first token', '400ms'],
      ['time to first text', '1.00s'],
      ['streaming', '2.10s'],
      ['tokens/sec', '57.1'],
      ['output tokens', '120'],
      ['input tokens', '1050'],
      ['cached tokens', '900'],
      ['cost', '$0.0123'],
      ['total', '2.50s'],
      ['tool calls', '1'],
      ['tool time', '250ms'],
    ]);

    fireEvent.keyDown(document, { key: 'Escape' });
    expect(screen.queryByRole('dialog', { name: 'Turn statistics' })).toBeNull();
  });

  it('an older reply without telemetry falls back to its total and tool calls', () => {
    render(<AskMessageView {...handlers} captainName="Ada" message={reply()} />);
    fireEvent.click(screen.getByRole('button', { name: 'Turn statistics' }));
    expect(popoverRows()).toEqual([
      ['total', '2.50s'],
      ['tool calls', '1'],
      ['tool time', '250ms'],
    ]);
  });

  it('a reply with nothing to show has no statistics button, and a user message never has one', () => {
    const { container } = render(<AskMessageView {...handlers} captainName="Ada" message={reply({ durationMs: null, toolCalls: null })} />);
    expect(within(container).queryByRole('button', { name: 'Turn statistics' })).toBeNull();
    render(<AskMessageView {...handlers} message={reply({ id: 'amg_u', role: 'User', durationMs: null, toolCalls: null })} />);
    expect(screen.queryByRole('button', { name: 'Turn statistics' })).toBeNull();
  });
});

const OUTCOME = [
  'Voyage "Run tests" finished (1 of 1 missions done).',
  '',
  '**Outcome** (took 3m 12s)',
  '- Mission "Run Test.Automated" on DocConverter: complete, landed. Took 3m 05s.',
  '  - UnitTest check "Test.Automated" passed (exit code 0, 2m 40s): 412 passed, 0 failed of 412 tests.',
  '  - Captain\'s final message: "All 412 tests passed."',
].join('\n');

describe('AskMessageView work reports and outcome milestones', () => {
  it('renders a WorkReport as a captain reply with a Report tag, tool chips, statistics, thinking, and Markdown', () => {
    const { container } = render(
      <AskMessageView
        {...handlers}
        captainName="Ada"
        message={reply({ kind: 'WorkReport', trackedWorkId: 'atw_1', thinkingText: 'Checking the run.', contentText: 'The voyage **finished**: all 412 tests passed.' })}
      />,
    );
    const article = container.querySelector('article')!;
    expect(article.classList.contains('ask-msg-assistant')).toBe(true);
    expect(article.classList.contains('ask-msg-report')).toBe(true);
    const head = container.querySelector('.ask-bubble-head')!;
    expect(within(head as HTMLElement).getByText('Ada')).toBeInTheDocument();
    expect(within(head as HTMLElement).getByText('Report')).toHaveClass('ask-report-tag');
    expect(screen.getByRole('button', { name: 'Turn statistics' })).toBeInTheDocument();
    expect(screen.getByText('Thinking')).toBeInTheDocument();
    expect(screen.getByText('finished').tagName).toBe('STRONG');
    expect(container.querySelectorAll('.chat-tools .chat-tool')).toHaveLength(1);
  });

  it('a plain captain reply has no Report tag', () => {
    const { container } = render(<AskMessageView {...handlers} captainName="Ada" message={reply()} />);
    expect(container.querySelector('.ask-report-tag')).toBeNull();
    expect(container.querySelector('article')!.classList.contains('ask-msg-report')).toBe(false);
    expect(screen.queryByText('Report')).toBeNull();
  });

  it('a multi-line outcome milestone renders its outcome lines and nested list', () => {
    const { container } = render(<AskMessageView {...handlers} message={reply({ kind: 'WorkUpdate', toolCalls: null, durationMs: null, contentText: OUTCOME })} />);
    expect(screen.getByText('Progress update')).toBeInTheDocument();
    expect(screen.getByText('Outcome').tagName).toBe('STRONG');
    const outer = container.querySelector('.ask-milestone-body ul')!;
    expect(outer).not.toBeNull();
    const nested = outer.querySelector('li ul')!;
    expect(nested).not.toBeNull();
    const nestedItems = Array.from(nested.querySelectorAll(':scope > li')).map((li) => li.textContent);
    expect(nestedItems).toEqual([
      'UnitTest check "Test.Automated" passed (exit code 0, 2m 40s): 412 passed, 0 failed of 412 tests.',
      'Captain\'s final message: "All 412 tests passed."',
    ]);
    expect(outer.querySelector(':scope > li')!.textContent).toContain('Mission "Run Test.Automated" on DocConverter: complete, landed. Took 3m 05s.');
  });
});

describe('AskMessageList with work reports', () => {
  const listProps = {
    proposals: {},
    snapshots: {},
    hasMore: false,
    loadingOlder: false,
    onLoadOlder: () => undefined,
    streaming: null,
    turnActive: false,
    waitingText: '',
    captainName: 'Ada',
    captainNames: {},
    busyProposalId: null,
    onApprove: () => undefined,
    onReject: () => undefined,
    highlightedWorkId: null,
    emptyState: null,
    turnError: null,
  };
  const work = { id: 'atw_1', threadId: 'ath_1', entityType: 'Voyage', entityId: 'vyg_1', title: 'Run tests', status: 'Complete', state: 'Finished' };

  it('keeps the live card on the message that started the work, not on the report', () => {
    const messages: AskMessage[] = [
      reply({ id: 'amg_r', sequence: 3, kind: 'ActionResult', toolCalls: null, durationMs: null, trackedWorkId: 'atw_1', contentText: 'Voyage started.' }),
      reply({ id: 'amg_u', sequence: 4, kind: 'WorkUpdate', toolCalls: null, durationMs: null, trackedWorkId: 'atw_1', contentText: OUTCOME }),
      reply({ id: 'amg_w', sequence: 5, kind: 'WorkReport', trackedWorkId: 'atw_1', contentText: 'All 412 tests passed.' }),
    ];
    const { container } = render(<MemoryRouter><AskMessageList {...listProps} messages={messages} trackedWork={[work]} /></MemoryRouter>);
    const card = container.querySelector('#ask-work-atw_1')!;
    expect(card).not.toBeNull();
    expect(card.closest('article')!.getAttribute('data-sequence')).toBe('3');
    expect(container.querySelector('article[data-sequence="5"] #ask-work-atw_1')).toBeNull();
    expect(within(container.querySelector('article[data-sequence="5"]') as HTMLElement).getByText('Report')).toBeInTheDocument();
  });

  it('an older transcript without reports renders as before, and an unknown kind falls back to assistant text', () => {
    const messages: AskMessage[] = [
      reply({ id: 'amg_q', sequence: 1, role: 'User', toolCalls: null, durationMs: null, contentText: 'How are the tests?' }),
      reply({ id: 'amg_a', sequence: 2, contentText: 'Starting a run.' }),
      reply({ id: 'amg_u', sequence: 3, kind: 'WorkUpdate', toolCalls: null, durationMs: null, contentText: 'Voyage "Run tests" finished.' }),
      reply({ id: 'amg_x', sequence: 4, kind: 'SomethingNew', toolCalls: null, durationMs: null, contentText: 'From a newer server.' }),
    ];
    const { container } = render(<MemoryRouter><AskMessageList {...listProps} messages={messages} trackedWork={[]} /></MemoryRouter>);
    expect(screen.getByText('How are the tests?')).toBeInTheDocument();
    expect(screen.getByText('Starting a run.')).toBeInTheDocument();
    expect(screen.getByText('Voyage "Run tests" finished.')).toBeInTheDocument();
    expect(screen.getByText('From a newer server.')).toBeInTheDocument();
    expect(container.querySelector('article[data-sequence="4"]')!.classList.contains('ask-msg-assistant')).toBe(true);
    expect(container.querySelector('.ask-report-tag')).toBeNull();
  });
});

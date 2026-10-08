import { fireEvent, render, screen, within } from '@testing-library/react';
import AskMessageView from './AskMessageView';
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

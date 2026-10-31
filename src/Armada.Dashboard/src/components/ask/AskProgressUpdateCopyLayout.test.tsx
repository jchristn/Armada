import { readFileSync } from 'node:fs';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import AskMessageView from './AskMessageView';
import { translateTemplate } from '../../i18n/runtime';
import type { AskMessage } from '../../types/models';

// Guards the copy control on Ask Armada Progress Update cards. It used to share the header row's free space with
// the "Show live card" link (both carried margin-left: auto), so it floated near the middle of the card. It is now
// pinned to the card's top-right corner, the card is its positioning context, and the header reserves its width.

const localeValue = {
  t: (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', text, null, params),
  locale: 'en',
  formatDateTime: (v: string | null | undefined) => v ?? '',
  formatRelativeTime: (v: string | null | undefined) => (v ? 'recently' : ''),
};
vi.mock('../../context/LocaleContext', () => ({ useLocale: () => localeValue }));

const css = readFileSync('./src/App.css', 'utf8');

function rule(selector: string): string {
  const start = css.indexOf(`\n${selector} {`);
  if (start < 0) throw new Error(`Missing CSS rule: ${selector}`);
  return css.slice(start + selector.length + 3, css.indexOf('}', start));
}

function update(overrides: Partial<AskMessage> = {}): AskMessage {
  return {
    id: 'amg_u',
    threadId: 'ath_1',
    sequence: 4,
    role: 'Assistant',
    kind: 'WorkUpdate',
    contentText: 'Mission "Fix the build" finished.',
    createdUtc: '2026-10-08T12:00:00Z',
    ...overrides,
  } as AskMessage;
}

const COPY_NAME = 'Copy this response to the clipboard.';
const handlers = { proposal: null, proposalBusy: false, onApprove: () => undefined, onReject: () => undefined };

describe('Ask Progress Update copy control placement', () => {
  it('renders the copy control last in the header, after the Show live card link, so tab order matches its top-right position', () => {
    const onShowWork = vi.fn();
    const { container } = render(<AskMessageView {...handlers} message={update()} onShowWork={onShowWork} />);
    const head = container.querySelector('.ask-msg-milestone .ask-milestone-head')!;
    const children = Array.from(head.children);
    const copy = screen.getByRole('button', { name: COPY_NAME });
    expect(copy).toHaveClass('copy-btn', 'ask-msg-copy');
    expect(children[children.length - 1]).toBe(copy);
    expect(children.indexOf(screen.getByRole('button', { name: 'Show live card' }))).toBeLessThan(children.indexOf(copy));

    fireEvent.click(screen.getByRole('button', { name: 'Show live card' }));
    expect(onShowWork).toHaveBeenCalledTimes(1);
  });

  it('keeps the copy control (and its label) when there is no live card link, and omits it for an empty update', () => {
    const { container, rerender } = render(<AskMessageView {...handlers} message={update()} />);
    const head = container.querySelector('.ask-milestone-head')!;
    expect(head.lastElementChild).toBe(screen.getByRole('button', { name: COPY_NAME }));
    expect(head.lastElementChild).toHaveAttribute('title', COPY_NAME);

    rerender(<AskMessageView {...handlers} message={update({ contentText: '   ' })} />);
    expect(screen.queryByRole('button', { name: COPY_NAME })).toBeNull();
  });

  it('still copies the update text and shows copied feedback', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
    render(<AskMessageView {...handlers} message={update()} onShowWork={() => undefined} />);
    const copy = screen.getByRole('button', { name: COPY_NAME });
    copy.focus();
    expect(copy).toHaveFocus();
    fireEvent.click(copy);
    expect(writeText).toHaveBeenCalledWith('Mission "Fix the build" finished.');
    await waitFor(() => expect(copy).toHaveClass('copied'));
    expect(copy).toHaveAttribute('aria-label', COPY_NAME);
  });

  it('pins the copy control to the card corner and reserves header space for it', () => {
    expect(rule('.ask-msg-milestone')).toMatch(/border-left: 3px solid/);
    expect(css).toMatch(/\n\.ask-msg-milestone \{ position: relative; \}/);

    const pinned = rule('.ask-msg-milestone .ask-msg-copy');
    expect(pinned).toMatch(/position: absolute;/);
    expect(pinned).toMatch(/top: 0\.5rem;/);
    expect(pinned).toMatch(/right: 0\.5rem;/);
    expect(pinned).toMatch(/margin: 0;/);

    // The copy-btn is 20px square; the header reserves that width on the right and that height, and the control's
    // top matches the card's top padding so it sits on the header row instead of over the message body.
    expect(css).toMatch(/\n\.ask-milestone-head \{ min-height: 20px; padding-right: calc\(20px \+ 0\.5rem\); \}/);
    expect(rule('.ask-msg-milestone')).toMatch(/padding: 0\.5rem 0\.75rem;/);
    expect(css).toMatch(/\.copy-btn,\n\.btn-copy \{[^}]*width: 20px;/);
  });
});

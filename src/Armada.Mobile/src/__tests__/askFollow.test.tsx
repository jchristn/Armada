import { act, fireEvent, render, screen } from '@testing-library/react-native';
import { createRef, type ReactNode } from 'react';
import { AccessibilityInfo, Text } from 'react-native';
import type { AskMessage } from '@dashboard/types/models';
import { MessageList, type MessageListHandle } from '../components/ask/MessageList';
import { classifyMessagesChange, STICK_THRESHOLD, TranscriptFollow } from '../components/ask/transcriptFollow';
import { LocaleProvider } from '../i18n/LocaleContext';
import { ThemeProvider } from '../theme/ThemeContext';

const info = AccessibilityInfo as jest.Mocked<typeof AccessibilityInfo>;
const { FlatList } = jest.requireActual<typeof import('react-native')>('react-native');
const NOW = '2026-10-07T12:00:00Z';

function message(id: string, over: Partial<AskMessage> = {}): AskMessage {
  return { id, threadId: 'thr_1', sequence: 1, role: 'Assistant', kind: 'Text', contentText: `Text of ${id}`, createdUtc: NOW, ...over };
}

function Providers({ children }: { children: ReactNode }) {
  return (
    <ThemeProvider>
      <LocaleProvider serverUrl={null} bundledCatalog={() => ({ defaultLocale: 'en', supportedLocales: [], locales: {} })}>{children}</LocaleProvider>
    </ThemeProvider>
  );
}

function List({ messages, listRef }: { messages: AskMessage[]; listRef?: React.Ref<MessageListHandle> }) {
  return (
    <Providers>
      <MessageList
        ref={listRef}
        messages={messages}
        proposals={{}}
        trackedWork={[]}
        snapshots={{}}
        hasMore={false}
        loadingOlder={false}
        onLoadOlder={() => undefined}
        streaming={null}
        turnActive={false}
        waitingText=""
        captainName="Captain"
        captainNames={{}}
        busyProposalId={null}
        onApprove={() => undefined}
        onReject={() => undefined}
        highlightedWorkId={null}
        emptyState={<Text>Empty</Text>}
        turnError={null}
        cliPermissions={{}}
        onCliDecided={() => undefined}
        cliResolution={null}
      />
    </Providers>
  );
}

const transcript = () => screen.getByTestId('ask-transcript');
const scrollEvent = (offset: number, contentHeight: number, viewportHeight = 300) => ({
  nativeEvent: { contentOffset: { x: 0, y: offset }, contentSize: { width: 390, height: contentHeight }, layoutMeasurement: { width: 390, height: viewportHeight } },
});

async function layout(height: number) {
  await fireEvent(transcript(), 'layout', { nativeEvent: { layout: { x: 0, y: 0, width: 390, height } } });
}
async function contentSize(height: number) {
  await fireEvent(transcript(), 'contentSizeChange', 390, height);
}
/** The reader drags to an offset and lets go (no fling). */
async function drag(offset: number, contentHeight: number) {
  await fireEvent(transcript(), 'scrollBeginDrag', scrollEvent(offset, contentHeight));
  await fireEvent.scroll(transcript(), scrollEvent(offset, contentHeight));
  await fireEvent(transcript(), 'scrollEndDrag', scrollEvent(offset, contentHeight));
}

let scrollToOffset: jest.SpyInstance;
let scrollToIndex: jest.SpyInstance;

beforeEach(() => {
  jest.clearAllMocks();
  info.isReduceMotionEnabled.mockResolvedValue(false);
  scrollToOffset = jest.spyOn(FlatList.prototype, 'scrollToOffset').mockImplementation(() => undefined);
  scrollToIndex = jest.spyOn(FlatList.prototype, 'scrollToIndex').mockImplementation(() => undefined);
});

afterEach(() => {
  scrollToOffset.mockRestore();
  scrollToIndex.mockRestore();
});

describe('TranscriptFollow', () => {
  function follower() {
    const changes: boolean[] = [];
    const follow = new TranscriptFollow((value) => changes.push(value));
    follow.layoutChanged(300);
    follow.contentSizeChanged(1000);
    return { follow, changes };
  }

  it('content growth while following scrolls to an explicit end offset from the reported sizes', () => {
    const { follow } = follower();
    expect(follow.contentSizeChanged(1600)).toEqual({ offset: 1300, animated: false });
    // The keyboard opening shrinks the viewport: the end moves down by as much.
    expect(follow.layoutChanged(200)).toEqual({ offset: 1400, animated: false });
    // Shorter than the viewport: the end is the top.
    expect(follow.contentSizeChanged(100)).toEqual({ offset: 0, animated: false });
  });

  it('scroll events the reader did not cause never stop following', () => {
    const { follow, changes } = follower();
    follow.scrolled({ offset: 700, contentHeight: 1000, viewportHeight: 300 });
    // The content grew by a long reply; the native view reports the new size with the old offset before the end is
    // scrolled to: far from the end, but not the reader's doing.
    follow.contentSizeChanged(2000);
    expect(follow.scrolled({ offset: 700, contentHeight: 2000, viewportHeight: 300 })).toEqual({ offset: 1700, animated: false });
    // A native event that has not caught up with the new content size: the end JavaScript computed is the target.
    expect(follow.scrolled({ offset: 0, contentHeight: 1000, viewportHeight: 300 })).toEqual({ offset: 1700, animated: false });
    expect(follow.following).toBe(true);
    expect(changes).toEqual([]);
  });

  it('a drift off the end while following (the list keeping a row in place) is put back', () => {
    const { follow } = follower();
    follow.scrolled({ offset: 700, contentHeight: 1000, viewportHeight: 300 });
    expect(follow.scrolled({ offset: 760, contentHeight: 1000, viewportHeight: 300 })).toEqual({ offset: 700, animated: false });
    expect(follow.scrolled({ offset: 700, contentHeight: 1000, viewportHeight: 300 })).toBeNull();
  });

  it('a drag up stops following; dragging back to the end follows again', () => {
    const { follow, changes } = follower();
    follow.dragBegan();
    follow.scrolled({ offset: 300, contentHeight: 1000, viewportHeight: 300 });
    follow.dragEnded({ offset: 300, contentHeight: 1000, viewportHeight: 300 });
    expect(follow.following).toBe(false);
    expect(follow.contentSizeChanged(1400)).toBeNull();
    follow.dragBegan();
    follow.dragEnded({ offset: 1400 - 300 - (STICK_THRESHOLD - 20), contentHeight: 1400, viewportHeight: 300 });
    expect(follow.following).toBe(true);
    expect(changes).toEqual([false, true]);
  });

  it('a fling decides where it comes to rest; the end of our own animated scroll does not', () => {
    const { follow } = follower();
    follow.dragBegan();
    follow.dragEnded({ offset: 650, contentHeight: 1000, viewportHeight: 300 });
    follow.momentumBegan();
    follow.momentumEnded({ offset: 100, contentHeight: 1000, viewportHeight: 300 });
    expect(follow.following).toBe(false);
    follow.toBottom(true);
    // iOS reports the end of a programmatic animated scroll as a momentum end; it is not the reader's.
    follow.momentumEnded({ offset: 100, contentHeight: 1000, viewportHeight: 300 });
    expect(follow.following).toBe(true);
  });

  it('a momentum scroll that no drag started (Android reports our own animated scrolls so) is not the reader', () => {
    const { follow } = follower();
    follow.toBottom(true);
    follow.momentumBegan();
    follow.scrolled({ offset: 20, contentHeight: 1000, viewportHeight: 300 });
    follow.momentumEnded({ offset: 40, contentHeight: 1000, viewportHeight: 300 });
    expect(follow.following).toBe(true);
    // A fling right after the reader's drag is theirs.
    follow.dragBegan();
    follow.dragEnded({ offset: 650, contentHeight: 1000, viewportHeight: 300 });
    follow.momentumBegan();
    follow.scrolled({ offset: 200, contentHeight: 1000, viewportHeight: 300 });
    expect(follow.following).toBe(false);
  });

  it('a screen reader page up (a move up with no drag, at known sizes) stops following', () => {
    const { follow } = follower();
    follow.scrolled({ offset: 700, contentHeight: 1000, viewportHeight: 300 });
    expect(follow.scrolled({ offset: 420, contentHeight: 1000, viewportHeight: 300 })).toBeNull();
    expect(follow.following).toBe(false);
  });

  it('a jump to a work card stops following until the reader scrolls back to the end themselves', () => {
    const { follow } = follower();
    follow.jumped();
    expect(follow.following).toBe(false);
    // Events at the end that the reader did not cause (the jump's own animation starting there) do not resume it.
    follow.scrolled({ offset: 700, contentHeight: 1000, viewportHeight: 300 });
    expect(follow.following).toBe(false);
    expect(follow.contentSizeChanged(1200)).toBeNull();
    follow.dragBegan();
    follow.dragEnded({ offset: 900, contentHeight: 1200, viewportHeight: 300 });
    expect(follow.following).toBe(true);
  });

  it('once the native view has the content, its own end is the target (Android measures its viewport itself)', () => {
    const { follow } = follower();
    follow.layoutChanged(276);
    follow.contentSizeChanged(778);
    // onLayout said 276 high; the native view is 300 high, so its end is 478, not 502.
    expect(follow.scrolled({ offset: 22, contentHeight: 778, viewportHeight: 300 })).toEqual({ offset: 478, animated: false });
    expect(follow.scrolled({ offset: 478, contentHeight: 778, viewportHeight: 300 })).toBeNull();
    expect(follow.unsettledScroll()).toBeNull();
    expect(follow.following).toBe(true);
  });

  it('scrolling to the newest message animates only from further up', () => {
    const { follow } = follower();
    expect(follow.toBottom(true)).toEqual({ offset: 700, animated: false });
    follow.jumped();
    expect(follow.toBottom(true)).toEqual({ offset: 700, animated: true });
  });

  it('the end stays unsettled until a scroll event shows the native view there (Android clamps an early scroll)', () => {
    const { follow } = follower();
    expect(follow.contentSizeChanged(1600)).toEqual({ offset: 1300, animated: false });
    expect(follow.unsettledScroll()).toEqual({ offset: 1300, animated: false });
    // The scroll ran before the grown content mounted and stopped at the old end.
    follow.scrolled({ offset: 700, contentHeight: 1000, viewportHeight: 300 });
    expect(follow.unsettledScroll()).toEqual({ offset: 1300, animated: false });
    follow.scrolled({ offset: 1300, contentHeight: 1600, viewportHeight: 300 });
    expect(follow.unsettledScroll()).toBeNull();
    // Not following: nothing to repeat.
    follow.contentSizeChanged(1800);
    follow.jumped();
    expect(follow.unsettledScroll()).toBeNull();
  });

  it('classifies a page of messages as prepended, replaced, or the same', () => {
    expect(classifyMessagesChange('m3', ['m1', 'm2', 'm3', 'm4'])).toBe('prepended');
    expect(classifyMessagesChange('m3', ['m3', 'm4', 'm5'])).toBe('same');
    expect(classifyMessagesChange('m3', ['x1', 'x2'])).toBe('replaced');
    expect(classifyMessagesChange(null, ['m1'])).toBe('replaced');
    expect(classifyMessagesChange('m1', [])).toBe('replaced');
  });
});

describe('MessageList following the newest message', () => {
  const page = [message('m1', { role: 'User', contentText: 'first' }), message('m2'), message('m3')];

  it('content growth while following scrolls to the end, at an offset computed from the new content size', async () => {
    await render(<List messages={page} />);
    await layout(300);
    scrollToOffset.mockClear();
    await contentSize(1600);
    expect(scrollToOffset).toHaveBeenLastCalledWith({ offset: 1300, animated: false });
    // The keyboard opening (commit 52532175): the viewport shrinks and the end stays in view.
    await layout(200);
    expect(scrollToOffset).toHaveBeenLastCalledWith({ offset: 1400, animated: false });
  });

  it('repeats the follow scroll each frame until the native view reports the end, then stops', async () => {
    jest.useFakeTimers();
    try {
      await render(<List messages={page} />);
      await layout(300);
      await contentSize(1600);
      scrollToOffset.mockClear();
      // The native view has not mounted the grown content yet: its scroll stopped at the old end.
      await fireEvent.scroll(transcript(), scrollEvent(700, 1000));
      await act(async () => { jest.advanceTimersByTime(16); });
      expect(scrollToOffset).toHaveBeenLastCalledWith({ offset: 1300, animated: false });
      await fireEvent.scroll(transcript(), scrollEvent(1300, 1600));
      scrollToOffset.mockClear();
      await act(async () => { jest.advanceTimersByTime(1000); });
      expect(scrollToOffset).not.toHaveBeenCalled();
    } finally {
      jest.useRealTimers();
    }
  });

  it('"New messages" scrolls with animation that a repeat left from an earlier follow scroll does not cut short', async () => {
    jest.useFakeTimers();
    try {
      const { rerender } = await render(<List messages={page} />);
      await layout(300);
      // A follow scroll that schedules a repeat (the native view has not reported the end yet)...
      await contentSize(1000);
      // ...then, before that repeat runs, the reader drags up and a new message arrives.
      await drag(200, 1000);
      await rerender(<List messages={[...page, message('m4')]} />);
      await contentSize(1350);
      scrollToOffset.mockClear();

      await fireEvent.press(screen.getByTestId('ask-new-messages'));
      expect(scrollToOffset).toHaveBeenLastCalledWith({ offset: 1050, animated: true });
      await act(async () => { jest.advanceTimersByTime(1000); });
      expect(scrollToOffset).toHaveBeenCalledTimes(1);
    } finally {
      jest.useRealTimers();
    }
  });

  it('a scroll event caused by content growth does not stop following', async () => {
    await render(<List messages={page} />);
    await layout(300);
    await contentSize(1000);
    await fireEvent.scroll(transcript(), scrollEvent(700, 1000));
    await contentSize(2400);
    // The native view reports the grown content before the end has been scrolled to: still following.
    await fireEvent.scroll(transcript(), scrollEvent(700, 2400));
    scrollToOffset.mockClear();
    await contentSize(2600);
    expect(scrollToOffset).toHaveBeenLastCalledWith({ offset: 2300, animated: false });
    expect(screen.queryByTestId('ask-new-messages')).toBeNull();
  });

  it('a drag up stops following and keeps the reader in place; a new message offers "New messages", announced once', async () => {
    const { rerender } = await render(<List messages={page} />);
    await layout(300);
    await contentSize(1000);
    await drag(200, 1000);
    scrollToOffset.mockClear();
    await contentSize(1300);
    await layout(250);
    expect(scrollToOffset).not.toHaveBeenCalled();
    expect(screen.queryByTestId('ask-new-messages')).toBeNull();

    await rerender(<List messages={[...page, message('m4')]} />);
    const pill = screen.getByTestId('ask-new-messages');
    expect(pill.props.accessibilityRole).toBe('button');
    expect(pill.props.accessibilityLabel).toBe('New messages');
    expect(pill.props.accessibilityHint).toBe('Scrolls to the newest message');
    expect(AccessibilityInfo.announceForAccessibilityWithOptions).toHaveBeenCalledWith('New messages', { queue: true });
    await rerender(<List messages={[...page, message('m4'), message('m5')]} />);
    expect(AccessibilityInfo.announceForAccessibilityWithOptions).toHaveBeenCalledTimes(1);
    expect(scrollToOffset).not.toHaveBeenCalled();

    await fireEvent.press(screen.getByTestId('ask-new-messages'));
    expect(scrollToOffset).toHaveBeenLastCalledWith({ offset: 1050, animated: true });
    expect(screen.queryByTestId('ask-new-messages')).toBeNull();
    await contentSize(1500);
    expect(scrollToOffset).toHaveBeenLastCalledWith({ offset: 1250, animated: false });
  });

  it('dragging back to the end follows again and hides "New messages"', async () => {
    const { rerender } = await render(<List messages={page} />);
    await layout(300);
    await contentSize(1000);
    await drag(100, 1000);
    await rerender(<List messages={[...page, message('m4')]} />);
    expect(screen.getByTestId('ask-new-messages')).toBeTruthy();
    await drag(1300 - 300, 1300);
    expect(screen.queryByTestId('ask-new-messages')).toBeNull();
    scrollToOffset.mockClear();
    await contentSize(1500);
    expect(scrollToOffset).toHaveBeenLastCalledWith({ offset: 1200, animated: false });
  });

  it('older messages loaded above keep the reader in place and keep maintainVisibleContentPosition', async () => {
    const { rerender } = await render(<List messages={page} />);
    expect(transcript().props.maintainVisibleContentPosition).toEqual({ minIndexForVisible: 0 });
    await layout(300);
    await contentSize(250);
    scrollToOffset.mockClear();
    // A short transcript (at the top and the end at once) loads its earlier page: the place is kept, not the end.
    await rerender(<List messages={[message('m0a'), message('m0b'), ...page]} />);
    await contentSize(900);
    expect(scrollToOffset).not.toHaveBeenCalled();
    expect(screen.queryByTestId('ask-new-messages')).toBeNull();
  });

  it('another conversation starts at its newest message even after the reader scrolled up in the last one', async () => {
    const { rerender } = await render(<List messages={page} />);
    await layout(300);
    await contentSize(1000);
    await drag(100, 1000);
    await rerender(<List messages={[message('x1'), message('x2')]} />);
    scrollToOffset.mockClear();
    await contentSize(800);
    expect(scrollToOffset).toHaveBeenLastCalledWith({ offset: 500, animated: false });
  });

  it('a jump to a work card stops following until the reader scrolls back to the end', async () => {
    const listRef = createRef<MessageListHandle>();
    const withWork = [message('m1', { role: 'User' }), message('m2', { kind: 'ActionResult', trackedWorkId: 'vyg_1' }), message('m3')];
    await render(<List messages={withWork} listRef={listRef} />);
    await layout(300);
    await contentSize(1000);
    await act(async () => { expect(listRef.current?.scrollToWork('vyg_1')).toBe(true); });
    expect(scrollToIndex).toHaveBeenCalledWith({ index: 1, viewPosition: 0, animated: true });
    scrollToOffset.mockClear();
    // The jump's own scroll events, starting at the end, do not resume following.
    await fireEvent.scroll(transcript(), scrollEvent(700, 1000));
    await contentSize(1400);
    expect(scrollToOffset).not.toHaveBeenCalled();
    await drag(1100, 1400);
    await contentSize(1600);
    expect(scrollToOffset).toHaveBeenLastCalledWith({ offset: 1300, animated: false });
  });

  it('under Reduce Motion the jumps are not animated', async () => {
    info.isReduceMotionEnabled.mockResolvedValue(true);
    const listRef = createRef<MessageListHandle>();
    const { rerender } = await render(<List messages={page} listRef={listRef} />);
    await act(async () => { await Promise.resolve(); });
    await layout(300);
    await contentSize(1000);
    await drag(100, 1000);
    await rerender(<List messages={[...page, message('m4')]} listRef={listRef} />);
    await fireEvent.press(screen.getByTestId('ask-new-messages'));
    expect(scrollToOffset).toHaveBeenLastCalledWith({ offset: 700, animated: false });
    await act(async () => { listRef.current?.scrollToBottom(); });
    expect(scrollToOffset).toHaveBeenLastCalledWith({ offset: 700, animated: false });
  });
});

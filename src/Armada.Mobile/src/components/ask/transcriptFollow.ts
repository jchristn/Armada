/** How close to the end (dp) still counts as reading the newest message. */
export const STICK_THRESHOLD = 140;

/** One scroll event's numbers, as the native scroll view reports them. */
export interface ScrollMetrics {
  offset: number;
  contentHeight: number;
  viewportHeight: number;
}

/** A scroll the transcript should make now, or null for none. */
export interface FollowScroll {
  offset: number;
  animated: boolean;
}

/**
 * Whether the Ask transcript follows its newest message, and where to scroll to keep doing so.
 *
 * Following is a decision the reader makes with their own scroll gestures (a drag, and the fling after it): ending
 * one within STICK_THRESHOLD of the end follows, ending one further up stops. Scroll events the reader did not cause
 * (content growing, the native list keeping the first visible row in place, our own scrolls, which Android also
 * reports as momentum) never stop it, with one exception: a move up and away from the end while nothing changes size
 * and no finger is down is a screen reader's scroll (VoiceOver and TalkBack page without drag events), and stops it.
 * While following, any such event that finds the list off the end puts the end back in view.
 *
 * Scrolls go to an explicit offset computed from the sizes JavaScript was told about (onContentSizeChange,
 * onLayout), not to scrollToEnd: on the new architecture the layout event reaches JavaScript before the native view
 * has mounted the new content, and a scrollToEnd command that runs first measures the old content and scrolls
 * nowhere. On iOS an explicit offset stays right when the content catches up; Android clamps it to the content it
 * has, so until a scroll event shows the native view at its end with that content, the end is unsettled and
 * unsettledScroll() asks for the scroll again (MessageList repeats it on the next frames).
 */
export class TranscriptFollow {
  private _following = true;
  private _dragging = false;
  private _momentum = false;
  private _flingPossible = false;
  private _locked = false;
  private _contentHeight = 0;
  private _viewportHeight = 0;
  private _lastEvent: ScrollMetrics | null = null;
  private _settled = true;
  private readonly _onChange: (following: boolean) => void;

  /** onChange runs whenever following starts or stops. */
  constructor(onChange: (following: boolean) => void = () => undefined) {
    this._onChange = onChange;
  }

  /** True while the transcript keeps its newest message in view. */
  get following(): boolean {
    return this._following;
  }

  /** The offset that shows the end of the content, from the sizes last reported to JavaScript. */
  get endOffset(): number {
    return Math.max(0, this._contentHeight - this._viewportHeight);
  }

  /** The content grew or shrank (a message, a streamed chunk, the waiting line). */
  contentSizeChanged(height: number): FollowScroll | null {
    this._contentHeight = height;
    return this.followScroll();
  }

  /** The viewport changed (the keyboard opened or closed, rotation, a split pane resized). */
  layoutChanged(height: number): FollowScroll | null {
    this._viewportHeight = height;
    return this.followScroll();
  }

  /** While following, the scroll to repeat until the native view is seen at the end; null once it is. */
  unsettledScroll(): FollowScroll | null {
    return this._following && !this._settled ? { offset: this.endOffset, animated: false } : null;
  }

  /** The reader put a finger down on the transcript. */
  dragBegan(): void {
    this._dragging = true;
    this._momentum = false;
    this._flingPossible = false;
    this._locked = false;
  }

  /** The reader lifted their finger; a fling may follow (momentumBegan). */
  dragEnded(metrics: ScrollMetrics): void {
    this._dragging = false;
    this._flingPossible = true;
    this.readerScrolled(metrics);
  }

  /**
   * A momentum scroll started: the reader's fling when it follows their drag. Android also reports our own animated
   * scrolls (to the newest message, to a work card) as momentum; those are not the reader's.
   */
  momentumBegan(): void {
    this._momentum = this._flingPossible;
    this._flingPossible = false;
  }

  /** A scroll came to rest: the reader's fling, or one of our own animated scrolls (iOS reports both). */
  momentumEnded(metrics: ScrollMetrics): void {
    if (!this._momentum) return;
    this._momentum = false;
    this.readerScrolled(metrics);
  }

  /** Any scroll event. Returns a scroll to make when the list is off the end while following. */
  scrolled(metrics: ScrollMetrics): FollowScroll | null {
    const previous = this._lastEvent;
    this._lastEvent = metrics;
    if (this._dragging || this._momentum) {
      this.readerScrolled(metrics);
      return null;
    }
    // Where the end is: the native view's own end once it has the content JavaScript was told about (it measures its
    // viewport itself; Android's onLayout height can differ from it), the end JavaScript computed while the native
    // view has not mounted that content yet.
    const caughtUp = metrics.contentHeight >= this._contentHeight - 1;
    const end = caughtUp ? Math.max(0, metrics.contentHeight - metrics.viewportHeight) : this.endOffset;
    const distance = end - metrics.offset;
    if (caughtUp && Math.abs(distance) <= 1) this._settled = true;
    if (this._following) {
      // A move up and away from the end, with nothing changing size: a screen reader's scroll.
      const steady = !!previous && Math.abs(previous.contentHeight - metrics.contentHeight) <= 1 && Math.abs(previous.viewportHeight - metrics.viewportHeight) <= 1;
      if (steady && previous && metrics.offset < previous.offset - 1 && distance > STICK_THRESHOLD) {
        this.setFollowing(false);
        return null;
      }
      // Off the end by something the reader did not do (the native list keeping a row in place as content above it
      // changed size, an earlier animated scroll still running): put the end back in view.
      return Math.abs(distance) > 1 ? { offset: end, animated: false } : null;
    }
    if (!this._locked && caughtUp && distance <= STICK_THRESHOLD) this.setFollowing(true);
    return null;
  }

  /**
   * Scroll to the newest message and follow it (the reader sent a message, ran a quick action, or asked to). Animated
   * only from further up: at the end already there is nothing to show, and on Android a running animation would keep
   * pulling the list back to this end while the content grows past it.
   */
  toBottom(animated: boolean): FollowScroll {
    const wasFollowing = this._following;
    this._locked = false;
    this._flingPossible = false;
    this._momentum = false;
    this.setFollowing(true);
    if (wasFollowing) this._settled = false;
    return { offset: this.endOffset, animated: animated && !wasFollowing };
  }

  /** A jump to a work card: stop following until the reader scrolls back to the end themselves. */
  jumped(): void {
    this._locked = true;
    this._flingPossible = false;
    this._momentum = false;
    this.setFollowing(false);
  }

  /** Older messages were loaded above: keep the reader's place instead of jumping to the end. */
  prepended(): void {
    this.setFollowing(false);
  }

  /** A different conversation (or the first page) replaced the messages: start at its newest message. */
  reset(): void {
    this._locked = false;
    this.setFollowing(true);
  }

  private followScroll(): FollowScroll | null {
    if (!this._following) return null;
    this._settled = false;
    return { offset: this.endOffset, animated: false };
  }

  private readerScrolled(metrics: ScrollMetrics): void {
    const distance = metrics.contentHeight - metrics.offset - metrics.viewportHeight;
    this.setFollowing(distance < STICK_THRESHOLD);
  }

  private setFollowing(value: boolean): void {
    if (this._following === value) return;
    this._following = value;
    this._onChange(value);
  }
}

/** How a new page of messages relates to the previous one. */
export type MessagesChange = 'prepended' | 'replaced' | 'same';

/**
 * Compares the first message ID before and after an update: 'prepended' when the old first message is still there
 * further down (older messages were loaded above it), 'replaced' when it is gone (another conversation, or the first
 * load), 'same' otherwise (messages appended or updated).
 */
export function classifyMessagesChange(previousFirstId: string | null, ids: readonly string[]): MessagesChange {
  const firstId = ids.length > 0 ? ids[0] : null;
  if (firstId === previousFirstId) return 'same';
  if (previousFirstId === null) return 'replaced';
  return ids.indexOf(previousFirstId) > 0 ? 'prepended' : 'replaced';
}

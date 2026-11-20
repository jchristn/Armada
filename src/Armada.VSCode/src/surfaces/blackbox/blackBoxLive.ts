/*
 * Black box live updates: a subscriber of the shared event bus (transport/eventBus.ts). Black box events (blackbox.*)
 * and polling ticks reload the store after a short debounce; a capture for the open repository's vessel raises a
 * notification. No `vscode` import, so it is unit tested with a fake bus.
 */

import { BLACKBOX_EVENT_PREFIX } from '@dashboard/lib/blackbox';
import { ArmadaEventMessage, Subscription } from '../../transport/eventBus';

/** The capture event that raises a notification. */
export const BLACKBOX_CAPTURED_EVENT = 'blackbox.captured';

/** Delay that coalesces a burst of events (captured, then analyzed) into one reload. */
export const REFRESH_DEBOUNCE_MS = 750;

/** The fields of a black box event payload the extension reads (WebSocketSurface, camelCase). */
export interface BlackBoxEventData {
  entryId?: string | null;
  missionId?: string | null;
  voyageId?: string | null;
  vesselId?: string | null;
  phase?: string | null;
  step?: string | null;
}

/** The part of the event bus this subscriber uses. */
export interface EventSource {
  subscribe(listener: (event: ArmadaEventMessage) => void): Subscription;
  onPoll(listener: () => void): Subscription;
}

/** Host hooks. */
export interface BlackBoxLiveOptions {
  /** Reloads the store. */
  refresh(): void;
  /** Called for a capture, after the vessel filter; shows the notification. */
  onCaptured?(data: BlackBoxEventData): void;
  /** Vessel ids matched to the open workspace; a capture for another vessel is not announced. */
  matchedVesselIds?(): string[];
  log?(line: string): void;
  setTimer?: (fn: () => void, ms: number) => unknown;
  clearTimer?: (handle: unknown) => void;
}

/** Subscribes the black box surfaces to the event bus. */
export class BlackBoxLive {
  private readonly _Options: BlackBoxLiveOptions;
  private readonly _SetTimer: (fn: () => void, ms: number) => unknown;
  private readonly _ClearTimer: (handle: unknown) => void;
  private readonly _Subscriptions: Subscription[];
  private _Timer: unknown;

  /**
   * @throws Error when source or options are missing.
   */
  public constructor(source: EventSource, options: BlackBoxLiveOptions) {
    if (!source) throw new Error('source is required');
    if (!options) throw new Error('options are required');
    this._Options = options;
    this._SetTimer = options.setTimer ?? ((fn, ms) => setTimeout(fn, ms));
    this._ClearTimer = options.clearTimer ?? ((h) => clearTimeout(h as ReturnType<typeof setTimeout>));
    this._Subscriptions = [
      source.subscribe((event) => this.onEvent(event)),
      source.onPoll(() => this.scheduleRefresh()),
    ];
  }

  /** The payload of a black box event, or undefined for any other event. Exposed for tests. */
  public static blackBoxData(event: ArmadaEventMessage): BlackBoxEventData | undefined {
    if (!event.type.startsWith(BLACKBOX_EVENT_PREFIX)) return undefined;
    return event.data && typeof event.data === 'object' ? (event.data as BlackBoxEventData) : {};
  }

  public dispose(): void {
    for (const s of this._Subscriptions) s.dispose();
    if (this._Timer !== undefined) this._ClearTimer(this._Timer);
    this._Timer = undefined;
  }

  private onEvent(event: ArmadaEventMessage): void {
    const data = BlackBoxLive.blackBoxData(event);
    if (!data) return;
    this._Options.log?.(`[live] ${event.type} ${data.entryId ?? ''} ${data.missionId ?? ''}`.trim());
    this.scheduleRefresh();
    if (event.type !== BLACKBOX_CAPTURED_EVENT || !this._Options.onCaptured) return;
    const matched = this._Options.matchedVesselIds?.() ?? [];
    if (matched.length && data.vesselId && !matched.includes(data.vesselId)) return;
    this._Options.onCaptured(data);
  }

  private scheduleRefresh(): void {
    if (this._Timer !== undefined) this._ClearTimer(this._Timer);
    this._Timer = this._SetTimer(() => {
      this._Timer = undefined;
      this._Options.refresh();
    }, REFRESH_DEBOUNCE_MS);
  }
}

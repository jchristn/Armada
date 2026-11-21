/*
 * Live operational state for the native Armada views (VSCODE_EXTENSION_PLAN.md 5, domains V1): missions, voyages,
 * captains, vessels and fleets, the merge queue and Needs You. Each domain loads through the shared dashboard client
 * and reloads when the Admiral's live events touch it (mission.*, voyage.*, captain.*, merge.*), on the event bus's
 * polling ticks, and when the connection changes. No `vscode` import, so it is unit tested with a stubbed fetch.
 */

import { enumerateVoyageProgress, getInbox, listCaptains, listFleets, listMergeQueue, listMissionSummaries, listVessels } from '@dashboard/api/client';
import type { Captain, Fleet, InboxItem, MergeEntry, MissionSummary, Vessel, VoyageProgress } from '@dashboard/types/models';
import { describeError } from '../../transport/errors';

/** The domains the store loads. */
export type OpsDomain = 'missions' | 'voyages' | 'captains' | 'fleet' | 'merge' | 'inbox';

/** Every domain, in load order. */
export const OPS_DOMAINS: OpsDomain[] = ['missions', 'voyages', 'captains', 'fleet', 'merge', 'inbox'];

/** Load state of one domain. */
export type OpsLoadState = 'disconnected' | 'loading' | 'ready' | 'error';

/** Mission statuses that are still moving. */
export const ACTIVE_MISSION_STATUSES = ['Assigned', 'InProgress', 'WorkProduced', 'PullRequestOpen', 'Testing', 'Verifying'];

/** Mission statuses waiting to start. */
export const QUEUED_MISSION_STATUSES = ['Pending', 'Blocked'];

/** Mission statuses that wait on a person. */
export const REVIEW_MISSION_STATUSES = ['Review'];

/** Mission statuses that are finished. */
export const FINISHED_MISSION_STATUSES = ['Complete', 'Failed', 'LandingFailed', 'Cancelled'];

/** Which domains an Admiral event type touches. */
export function domainsForEvent(type: string): OpsDomain[] {
  const t = type.toLowerCase();
  if (t.startsWith('mission.')) return ['missions', 'voyages', 'inbox'];
  if (t.startsWith('voyage.')) return ['voyages', 'missions'];
  if (t.startsWith('captain.')) return ['captains', 'inbox'];
  if (t.startsWith('merge')) return ['merge', 'missions'];
  if (t.startsWith('vessel.') || t.startsWith('fleet.')) return ['fleet'];
  if (t.startsWith('cli-permission') || t.startsWith('deployment.') || t.startsWith('model-alert') || t.startsWith('inbox')) return ['inbox'];
  return [];
}

type Listener = (domain: OpsDomain) => void;

/** Host hooks. */
export interface OpsStoreDeps {
  /** True while connected. */
  isConnected(): boolean;
  /** Diagnostics (never given a secret). */
  log?(line: string): void;
  /** Delay before a burst of events reloads a domain. Default 400 ms. */
  debounceMs?: number;
}

/** Live operational state. */
export class OpsStore {
  private readonly _Deps: OpsStoreDeps;
  private readonly _Listeners = new Set<Listener>();
  private readonly _State = new Map<OpsDomain, OpsLoadState>();
  private readonly _Errors = new Map<OpsDomain, string>();
  private readonly _Timers = new Map<OpsDomain, ReturnType<typeof setTimeout>>();
  private readonly _InFlight = new Map<OpsDomain, Promise<void>>();
  private readonly _Again = new Set<OpsDomain>();

  /** Mission summaries, newest first (the most recent page). */
  public missions: MissionSummary[] = [];
  /** Total missions on the server. */
  public missionTotal = 0;
  /** Voyages with progress, newest first. */
  public voyages: VoyageProgress[] = [];
  public captains: Captain[] = [];
  public vessels: Vessel[] = [];
  public fleets: Fleet[] = [];
  public mergeQueue: MergeEntry[] = [];
  public inbox: InboxItem[] = [];

  /**
   * @throws Error when deps is missing.
   */
  public constructor(deps: OpsStoreDeps) {
    if (!deps) throw new Error('deps is required');
    this._Deps = deps;
    for (const d of OPS_DOMAINS) this._State.set(d, 'disconnected');
  }

  /** Subscribes to domain changes. */
  public onDidChange(listener: Listener): { dispose(): void } {
    this._Listeners.add(listener);
    return { dispose: () => this._Listeners.delete(listener) };
  }

  /** Load state of a domain. */
  public state(domain: OpsDomain): OpsLoadState {
    return this._State.get(domain) ?? 'disconnected';
  }

  /** The last load error of a domain, if any. */
  public error(domain: OpsDomain): string | undefined {
    return this._Errors.get(domain);
  }

  /** Missions in the given statuses. */
  public missionsIn(statuses: string[]): MissionSummary[] {
    return this.missions.filter((m) => statuses.includes(m.status));
  }

  /** A vessel's name, or its id. */
  public vesselName(id: string | null | undefined): string {
    if (!id) return '';
    return this.vessels.find((v) => v.id === id)?.name ?? id;
  }

  /** A captain's name, or its id. */
  public captainName(id: string | null | undefined): string {
    if (!id) return '';
    return this.captains.find((c) => c.id === id)?.name ?? id;
  }

  /** Missions of a voyage, from the loaded page. */
  public missionsOfVoyage(voyageId: string): MissionSummary[] {
    return this.missions.filter((m) => m.voyageId === voyageId);
  }

  /** Reloads every domain (or clears them when disconnected). */
  public async refreshAll(): Promise<void> {
    await Promise.all(OPS_DOMAINS.map((d) => this.refresh(d)));
  }

  /** Reloads the domains an Admiral event touches, after a short debounce. */
  public onEvent(type: string): void {
    for (const domain of domainsForEvent(type)) this.schedule(domain);
  }

  /** Reloads a domain after the debounce. */
  public schedule(domain: OpsDomain): void {
    if (this._Timers.has(domain)) return;
    this._Timers.set(domain, setTimeout(() => {
      this._Timers.delete(domain);
      void this.refresh(domain);
    }, this._Deps.debounceMs ?? 400));
  }

  /** Reloads one domain now. Concurrent calls coalesce; never throws. */
  public refresh(domain: OpsDomain): Promise<void> {
    const running = this._InFlight.get(domain);
    if (running) {
      this._Again.add(domain);
      return running;
    }
    const run = this.load(domain).finally(() => {
      this._InFlight.delete(domain);
      if (this._Again.delete(domain)) void this.refresh(domain);
    });
    this._InFlight.set(domain, run);
    return run;
  }

  /** Drops every timer. */
  public dispose(): void {
    for (const timer of this._Timers.values()) clearTimeout(timer);
    this._Timers.clear();
    this._Listeners.clear();
  }

  private async load(domain: OpsDomain): Promise<void> {
    if (!this._Deps.isConnected()) {
      this.clear(domain);
      this._State.set(domain, 'disconnected');
      this._Errors.delete(domain);
      this.fire(domain);
      return;
    }
    if (this._State.get(domain) !== 'ready') {
      this._State.set(domain, 'loading');
      this.fire(domain);
    }
    try {
      switch (domain) {
        case 'missions': {
          // The newest 50 are enough for the status bar, notifications and titles; the dashboard pages list the rest.
          const page = await listMissionSummaries({ pageNumber: 1, pageSize: 50 });
          this.missions = sortNewest(page.objects ?? [], (m) => m.lastUpdateUtc ?? m.createdUtc);
          this.missionTotal = page.totalRecords ?? this.missions.length;
          break;
        }
        case 'voyages': {
          const page = await enumerateVoyageProgress({ pageNumber: 1, pageSize: 100 });
          this.voyages = sortNewest(page.objects ?? [], (v) => v.voyage.lastUpdateUtc ?? v.voyage.createdUtc);
          break;
        }
        case 'captains':
          this.captains = ((await listCaptains({ pageNumber: 1, pageSize: 500 })).objects ?? []).sort((a, b) => a.name.localeCompare(b.name));
          break;
        case 'fleet': {
          const [vessels, fleets] = await Promise.all([listVessels({ pageNumber: 1, pageSize: 1000 }), listFleets({ pageNumber: 1, pageSize: 500 })]);
          this.vessels = (vessels.objects ?? []).sort((a, b) => a.name.localeCompare(b.name));
          this.fleets = (fleets.objects ?? []).sort((a, b) => a.name.localeCompare(b.name));
          break;
        }
        case 'merge':
          this.mergeQueue = sortNewest((await listMergeQueue({ pageNumber: 1, pageSize: 200 })).objects ?? [], (e) => e.lastUpdateUtc ?? e.createdUtc);
          break;
        case 'inbox':
          this.inbox = (await getInbox()) ?? [];
          break;
      }
      this._State.set(domain, 'ready');
      this._Errors.delete(domain);
    } catch (err) {
      this._State.set(domain, 'error');
      this._Errors.set(domain, describeError(err));
      this._Deps.log?.(`[ops] ${domain} failed to load: ${describeError(err)}`);
    }
    this.fire(domain);
  }

  private clear(domain: OpsDomain): void {
    switch (domain) {
      case 'missions': this.missions = []; this.missionTotal = 0; break;
      case 'voyages': this.voyages = []; break;
      case 'captains': this.captains = []; break;
      case 'fleet': this.vessels = []; this.fleets = []; break;
      case 'merge': this.mergeQueue = []; break;
      case 'inbox': this.inbox = []; break;
    }
  }

  private fire(domain: OpsDomain): void {
    for (const listener of this._Listeners) {
      try {
        listener(domain);
      } catch {
        /* one listener must not break the others */
      }
    }
  }
}

function sortNewest<T>(items: T[], stamp: (item: T) => string | null | undefined): T[] {
  return [...items].sort((a, b) => (Date.parse(stamp(b) ?? '') || 0) - (Date.parse(stamp(a) ?? '') || 0));
}

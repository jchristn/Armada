import { ArmadaBlackBoxClient, describeError } from '../api/blackbox';
import { BlackBoxConflict, BlackBoxEntrySummary, BlackBoxHotspot, Vessel } from '../api/types';
import { directoryPath, filePath, isOpen } from '../util/format';
import { readGitRemoteUrls, sameRepository } from '../util/repoUrl';

/** A workspace folder whose git remote matches a vessel. */
export interface FolderMatch {
  folderPath: string;
  vessel: Vessel;
}

/** Why a file is decorated as conflicted. */
export interface ConflictInfo {
  missionId: string;
  entryId: string;
  createdUtc: string;
  count: number;
}

/** Loading state of the store. */
export type StoreStatus = 'disconnected' | 'loading' | 'ready' | 'error';

/** Settings the store reads on every refresh. */
export interface StoreSettings {
  filterToWorkspaceVessel: boolean;
  includeResolved: boolean;
  maxEntries: number;
  decorationsEnabled: boolean;
  hotspotLimit: number;
}

/** Host dependencies, injected so the store is testable without VS Code. */
export interface StoreHost {
  getClient(): ArmadaBlackBoxClient | undefined;
  getWorkspaceFolderPaths(): string[];
  getSettings(): StoreSettings;
  readRemotes?(folderPath: string): string[];
}

/** Maximum open entries per vessel whose Conflict section is read for file decorations. */
export const MAX_DECORATION_ENTRIES = 25;

type Listener = () => void;

/** Caches vessels, entries, conflicted files and hotspots, and notifies listeners when they change. */
export class BlackBoxStore {
  private readonly _Host: StoreHost;
  private readonly _Listeners = new Set<Listener>();
  private _Sequence = 0;
  private _VesselsLoaded = false;

  public status: StoreStatus = 'disconnected';
  public error: string | undefined;
  public vessels: Vessel[] = [];
  public matches: FolderMatch[] = [];
  public entries: BlackBoxEntrySummary[] = [];
  public filtered = false;
  /** True when filtering is on, the workspace has folders, and none matched a vessel. */
  public unmatchedWorkspace = false;
  public openCount = 0;
  /** folder path -> repository-relative file path -> conflict. */
  public conflictedFiles = new Map<string, Map<string, ConflictInfo>>();
  /** folder path -> repository-relative directory -> hotspot. */
  public hotspots = new Map<string, Map<string, BlackBoxHotspot>>();
  public lastRefreshUtc: string | undefined;

  /**
   * @throws Error when host is missing.
   */
  public constructor(host: StoreHost) {
    if (!host) throw new Error('host is required');
    this._Host = host;
  }

  /** Subscribes to changes; returns an unsubscribe function. */
  public onDidChange(listener: Listener): { dispose(): void } {
    this._Listeners.add(listener);
    return { dispose: () => this._Listeners.delete(listener) };
  }

  /** Forces the vessel list (and so the repository match) to reload on the next refresh. */
  public invalidateVessels(): void {
    this._VesselsLoaded = false;
  }

  /** The vessel ids matched to workspace folders. */
  public matchedVesselIds(): string[] {
    return Array.from(new Set(this.matches.map((m) => m.vessel.Id)));
  }

  /** Reloads everything. Never throws; failures set status to 'error'. */
  public async refresh(): Promise<void> {
    const sequence = ++this._Sequence;
    const client = this._Host.getClient();
    if (!client) {
      this.reset('disconnected');
      this.emit();
      return;
    }
    const settings = this._Host.getSettings();
    this.status = 'loading';
    this.emit();

    try {
      if (!this._VesselsLoaded) {
        const vessels = await client.listAllVessels();
        if (sequence !== this._Sequence) return;
        this.vessels = vessels;
        this._VesselsLoaded = true;
      }
      const matches = this.computeMatches();

      const useFilter = settings.filterToWorkspaceVessel && matches.length > 0;
      const vesselIds = useFilter ? Array.from(new Set(matches.map((m) => m.vessel.Id))) : [undefined];
      const entries: BlackBoxEntrySummary[] = [];
      let openCount = 0;
      for (const vesselId of vesselIds) {
        const page = await client.enumerateEntries({
          VesselId: vesselId,
          IsCurrent: true,
          Resolution: settings.includeResolved ? undefined : 'Open',
          PageNumber: 1,
          PageSize: settings.maxEntries,
        });
        entries.push(...page.Objects);
        if (!settings.includeResolved) openCount += page.TotalRecords ?? page.Objects.length;
      }
      if (settings.includeResolved) openCount = entries.filter(isOpen).length;
      entries.sort((a, b) => Date.parse(b.CreatedUtc) - Date.parse(a.CreatedUtc));

      const conflicted = new Map<string, Map<string, ConflictInfo>>();
      const hotspots = new Map<string, Map<string, BlackBoxHotspot>>();
      if (settings.decorationsEnabled) {
        for (const match of matches) {
          conflicted.set(match.folderPath, await this.loadConflicts(client, entries, match.vessel.Id));
          hotspots.set(match.folderPath, await this.loadHotspots(client, match.vessel.Id, settings.hotspotLimit));
        }
      }
      if (sequence !== this._Sequence) return;

      this.matches = matches;
      this.entries = entries;
      this.filtered = useFilter;
      this.unmatchedWorkspace = settings.filterToWorkspaceVessel && matches.length === 0 && this._Host.getWorkspaceFolderPaths().length > 0;
      this.openCount = openCount;
      this.conflictedFiles = conflicted;
      this.hotspots = hotspots;
      this.status = 'ready';
      this.error = undefined;
      this.lastRefreshUtc = new Date().toISOString();
    } catch (err) {
      if (sequence !== this._Sequence) return;
      this.reset('error');
      this.error = describeError(err);
    }
    this.emit();
  }

  /** Matches workspace folders to vessels by git remote URL. */
  public computeMatches(): FolderMatch[] {
    const readRemotes = this._Host.readRemotes ?? readGitRemoteUrls;
    const matches: FolderMatch[] = [];
    for (const folderPath of this._Host.getWorkspaceFolderPaths()) {
      const remotes = readRemotes(folderPath);
      const vessel = this.vessels.find((v) => remotes.some((r) => sameRepository(r, v.RepoUrl)));
      if (vessel) matches.push({ folderPath, vessel });
    }
    return matches;
  }

  private async loadConflicts(client: ArmadaBlackBoxClient, entries: BlackBoxEntrySummary[], vesselId: string): Promise<Map<string, ConflictInfo>> {
    const result = new Map<string, ConflictInfo>();
    const candidates = entries.filter((e) => e.VesselId === vesselId && isOpen(e)).slice(0, MAX_DECORATION_ENTRIES);
    for (const entry of candidates) {
      let conflict: BlackBoxConflict | undefined;
      try {
        conflict = await client.getSection<BlackBoxConflict>(entry.Id, 'Conflict');
      } catch {
        continue; // a missing section or object must not hide the other entries' decorations
      }
      for (const ref of conflict?.ConflictedFiles ?? []) {
        const path = filePath(ref);
        if (!path) continue;
        const existing = result.get(path);
        const info: ConflictInfo = {
          missionId: entry.MissionId || entry.VoyageId || entry.Id,
          entryId: entry.Id,
          createdUtc: entry.CreatedUtc,
          count: (existing?.count ?? 0) + 1,
        };
        if (!existing || Date.parse(entry.CreatedUtc) > Date.parse(existing.createdUtc)) result.set(path, info);
        else existing.count = info.count;
      }
    }
    return result;
  }

  private async loadHotspots(client: ArmadaBlackBoxClient, vesselId: string, limit: number): Promise<Map<string, BlackBoxHotspot>> {
    const result = new Map<string, BlackBoxHotspot>();
    if (limit <= 0) return result;
    try {
      const page = await client.listHotspots(vesselId, limit);
      for (const hotspot of page.Objects.slice(0, limit)) {
        const dir = directoryPath(hotspot.Directory);
        if (dir) result.set(dir, hotspot);
      }
    } catch {
      // hotspots are decoration only; an older server without the route still shows entries
    }
    return result;
  }

  private reset(status: StoreStatus): void {
    this.status = status;
    this.error = undefined;
    this.matches = [];
    this.entries = [];
    this.filtered = false;
    this.unmatchedWorkspace = false;
    this.openCount = 0;
    this.conflictedFiles = new Map();
    this.hotspots = new Map();
    if (status === 'disconnected') {
      this.vessels = [];
      this._VesselsLoaded = false;
    }
  }

  private emit(): void {
    for (const listener of Array.from(this._Listeners)) {
      try {
        listener();
      } catch {
        // a failing listener must not break the others
      }
    }
  }
}

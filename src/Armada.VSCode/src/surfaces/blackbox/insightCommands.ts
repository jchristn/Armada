import * as vscode from 'vscode';
import { diffBlackBoxEntries, enumerateBlackBoxResilienceGaps, getBlackBoxTimeline, listMissionBlackBoxEntries, previewMissionOverlap } from '@dashboard/api/client';
import type { BlackBoxAttemptDiff, BlackBoxEntrySummary, BlackBoxResilienceGap, BlackBoxTimelinePage, MissionOverlapPreview } from '@dashboard/types/blackbox';
import type { EnumerationResult, Vessel } from '@dashboard/types/models';
import { describeError, needsConnection } from '../../transport/errors';
import { listAllVessels } from '../../transport/hostClient';
import { entryLabel, formatAge } from '../../util/format';
import { BlackBoxStore } from './blackBoxStore';
import { CommandConnection } from './blackBoxCommands';
import { renderDiffMarkdown, renderGapsMarkdown, renderOverlapMarkdown, renderTimelineMarkdown } from './insightMarkdown';

/** Timeline events per page. */
export const TIMELINE_PAGE_SIZE = 100;

/** Resilience gaps per page (the server caps it at 50). */
export const GAPS_PAGE_SIZE = 25;

/** Navigation buttons offered after a timeline page. */
export const TIMELINE_NEXT = 'Next Page';
export const TIMELINE_PREVIOUS = 'Previous Page';

const ENTER_OTHER_LABEL = '$(edit) Enter another entry id (bbx_...)...';

/** A vessel to preview against: a full vessel, or only the id the caller passed. */
export type VesselRef = Pick<Vessel, 'id'> & Partial<Pick<Vessel, 'name' | 'repoUrl'>>;

/** Outcome of "Preview Mission Overlap", for callers and tests. */
export type OverlapOutcome = 'shown' | 'clean' | 'noVessel' | 'cancelled' | 'failed';

/** Dependencies of the insight commands. */
export interface InsightCommandDeps {
  /**
   * The active connection.
   * @throws NotConnectedError when there is none.
   */
  requireConnection(): CommandConnection;
  store: BlackBoxStore;
  /** Resolves a command argument (tree node, entry, id) to an entry, prompting when needed (BlackBoxCommands.pickEntry). */
  pickEntry(arg: unknown, title: string): Promise<BlackBoxEntrySummary | undefined>;
}

/** Black box forensics and prevention commands (timeline, attempt diff, overlap preview). Never throw. */
export class InsightCommands {
  private readonly _Deps: InsightCommandDeps;

  /**
   * @throws Error when deps is missing.
   */
  public constructor(deps: InsightCommandDeps) {
    if (!deps) throw new Error('deps is required');
    this._Deps = deps;
  }

  /** Armada: Show Black Box Timeline. Opens one Markdown page per timeline page; offers next and previous pages. */
  public async showTimeline(arg?: unknown): Promise<BlackBoxTimelinePage[]> {
    const shown: BlackBoxTimelinePage[] = [];
    await this.guard(async () => {
      const entry = await this._Deps.pickEntry(arg, 'Show Black Box Timeline');
      if (!entry) return;
      this._Deps.requireConnection();
      let pageNumber = 1;
      for (let guard = 0; guard < 1000; guard++) {
        const page = await getBlackBoxTimeline(entry.id, { order: 'CreatedAscending', pageNumber, pageSize: TIMELINE_PAGE_SIZE });
        shown.push(page);
        await this.openMarkdown(renderTimelineMarkdown(entry, page));
        const buttons: string[] = [];
        if (page.pageNumber > 1) buttons.push(TIMELINE_PREVIOUS);
        if (page.pageNumber < page.totalPages) buttons.push(TIMELINE_NEXT);
        if (!buttons.length) return;
        const choice = await vscode.window.showInformationMessage(`Armada: timeline page ${page.pageNumber} of ${page.totalPages} for ${entryLabel(entry)}.`, ...buttons);
        if (choice === TIMELINE_NEXT) pageNumber = page.pageNumber + 1;
        else if (choice === TIMELINE_PREVIOUS) pageNumber = page.pageNumber - 1;
        else return;
      }
    }, undefined);
    return shown;
  }

  /** Armada: Show Resilience Gaps. Opens one Markdown page per page of gaps (filtered to the open repository's vessel when
   * exactly one matches); offers next and previous pages. */
  public async showResilienceGaps(): Promise<Array<EnumerationResult<BlackBoxResilienceGap>>> {
    const shown: Array<EnumerationResult<BlackBoxResilienceGap>> = [];
    await this.guard(async () => {
      this._Deps.requireConnection();
      const vessels = Array.from(new Map(this._Deps.store.matches.map((m) => [m.vessel.id, m.vessel])).values());
      const vesselId = vessels.length === 1 ? vessels[0].id : undefined;
      let pageNumber = 1;
      for (let guard = 0; guard < 1000; guard++) {
        const page = await enumerateBlackBoxResilienceGaps({ vesselId, pageNumber, pageSize: GAPS_PAGE_SIZE });
        shown.push(page);
        await this.openMarkdown(renderGapsMarkdown(page));
        const buttons: string[] = [];
        if (page.pageNumber > 1) buttons.push(TIMELINE_PREVIOUS);
        if (page.pageNumber < page.totalPages) buttons.push(TIMELINE_NEXT);
        if (!buttons.length) return;
        const choice = await vscode.window.showInformationMessage(`Armada: resilience gaps page ${page.pageNumber} of ${page.totalPages}.`, ...buttons);
        if (choice === TIMELINE_NEXT) pageNumber = page.pageNumber + 1;
        else if (choice === TIMELINE_PREVIOUS) pageNumber = page.pageNumber - 1;
        else return;
      }
    }, undefined);
    return shown;
  }

  /** Armada: Compare Black Box Attempts. Picks an entry and another attempt (or any entry id) and opens the diff. */
  public async compareAttempts(arg?: unknown, otherId?: string): Promise<BlackBoxAttemptDiff | undefined> {
    return this.guard(async () => {
      const entry = await this._Deps.pickEntry(arg, 'Compare Black Box Attempts');
      if (!entry) return undefined;
      this._Deps.requireConnection();
      let other = otherId?.trim() || undefined;
      let otherLabel = other ?? '';
      if (!other) {
        let attempts: BlackBoxEntrySummary[] = [];
        if (entry.missionId) {
          try {
            attempts = (await listMissionBlackBoxEntries(entry.missionId)).objects.filter((e) => e.id !== entry.id);
          } catch {
            attempts = [];
          }
        }
        type Item = vscode.QuickPickItem & { entry?: BlackBoxEntrySummary };
        const items: Item[] = [
          ...attempts.map((e) => ({
            label: `Attempt ${e.attempt ?? '?'}`,
            description: [e.failureKind, e.step, formatAge(e.createdUtc)].filter(Boolean).join(', '),
            detail: e.id,
            entry: e,
          })),
          { label: ENTER_OTHER_LABEL, alwaysShow: true },
        ];
        const picked = attempts.length
          ? await vscode.window.showQuickPick(items, { title: `Compare ${entryLabel(entry)} with`, placeHolder: 'Pick another attempt of this mission, or enter an entry id' })
          : { label: ENTER_OTHER_LABEL } as Item;
        if (!picked) return undefined;
        if (picked.entry) {
          other = picked.entry.id;
          otherLabel = `${picked.entry.id}`;
        } else {
          const typed = await vscode.window.showInputBox({
            title: `Compare ${entryLabel(entry)} with`,
            prompt: attempts.length ? 'Black box entry id (bbx_...)' : 'This mission has no other attempts. Enter any black box entry id (bbx_...) to compare with.',
            ignoreFocusOut: true,
            validateInput: (v) => (!v.trim() ? 'Enter an entry id' : v.trim() === entry.id ? 'Pick a different entry' : undefined),
          });
          if (!typed || !typed.trim()) return undefined;
          other = typed.trim();
          otherLabel = other;
        }
      }
      const diff = await diffBlackBoxEntries(entry.id, other);
      await this.openMarkdown(renderDiffMarkdown(diff, entry.id, otherLabel));
      return diff;
    }, undefined);
  }

  /**
   * Armada: Preview Mission Overlap. Resolves the open repository's vessel (as the Failures view does, by remote URL),
   * asks for a title and brief, and shows the overlap and brief lint warnings. Nothing is dispatched.
   */
  public async previewOverlap(arg?: unknown): Promise<OverlapOutcome> {
    return this.guard<OverlapOutcome>(async () => {
      this._Deps.requireConnection();
      const vessel = await this.pickVessel(typeof arg === 'string' ? arg : undefined);
      if (vessel === null) return 'noVessel';
      if (!vessel) return 'cancelled';
      const title = await vscode.window.showInputBox({ title: `Preview Mission Overlap: ${vessel.name ?? vessel.id}`, prompt: 'Mission title (optional)', ignoreFocusOut: true });
      if (title === undefined) return 'cancelled';
      const brief = await vscode.window.showInputBox({
        title: `Preview Mission Overlap: ${vessel.name ?? vessel.id}`,
        prompt: 'Mission brief: what the mission should do (name the files or directories it will touch)',
        ignoreFocusOut: true,
        validateInput: (v) => (!v.trim() && !title.trim() ? 'Enter a brief' : undefined),
      });
      if (brief === undefined) return 'cancelled';
      if (!brief.trim() && !title.trim()) return 'cancelled';
      const preview: MissionOverlapPreview = await previewMissionOverlap(vessel.id, { title: title.trim() || null, description: brief.trim() || null });
      await this.openMarkdown(renderOverlapMarkdown(preview, vessel.name ?? vessel.id, title.trim()));
      const overlaps = preview.warningCount ?? preview.warnings?.length ?? 0;
      const lint = preview.briefLint?.findingCount ?? preview.briefLint?.findings?.length ?? 0;
      if (!overlaps && !lint) {
        void vscode.window.showInformationMessage(`Armada: no overlap or brief lint warnings for this mission on ${vessel.name ?? vessel.id}.`);
        return 'clean';
      }
      void vscode.window.showWarningMessage(`Armada: ${overlaps} overlap warning${overlaps === 1 ? '' : 's'} and ${lint} brief lint warning${lint === 1 ? '' : 's'} on ${vessel.name ?? vessel.id}.`);
      return 'shown';
    }, 'failed');
  }

  /**
   * The vessel to preview against: an explicit id, the single matched workspace vessel, a pick among several matches,
   * or a pick among every vessel when none matches. Null when the server has no vessels; undefined when cancelled.
   */
  private async pickVessel(vesselId?: string): Promise<VesselRef | null | undefined> {
    const store = this._Deps.store;
    if (vesselId && vesselId.trim()) {
      const id = vesselId.trim();
      return store.vessels.find((v) => v.id === id) ?? { id };
    }
    if (!store.matches.length) await store.refresh();
    const matched = Array.from(new Map(store.matches.map((m) => [m.vessel.id, m.vessel])).values());
    if (matched.length === 1) return matched[0];
    let candidates = matched;
    let placeHolder = 'Several vessels match the open folders; pick one';
    if (!candidates.length) {
      candidates = store.vessels.length ? store.vessels : await listAllVessels(this._Deps.requireConnection().tenant);
      placeHolder = 'No vessel matches the open repository (by git remote URL); pick one';
    }
    if (!candidates.length) {
      void vscode.window.showWarningMessage('Armada: no vessels are registered on this Armada server, so there is nothing to compare the mission with.');
      return null;
    }
    const picked = await vscode.window.showQuickPick(
      candidates.map((v) => ({ label: v.name ?? v.id, description: v.id, detail: v.repoUrl ?? undefined, vessel: v })),
      { title: 'Armada: Preview Mission Overlap', placeHolder, matchOnDescription: true, matchOnDetail: true },
    );
    return picked?.vessel;
  }

  private async openMarkdown(content: string): Promise<void> {
    const doc = await vscode.workspace.openTextDocument({ language: 'markdown', content });
    await vscode.window.showTextDocument(doc, { preview: false });
  }

  private async guard<T>(fn: () => Promise<T>, fallback: T): Promise<T> {
    try {
      return await fn();
    } catch (err) {
      const message = describeError(err);
      if (needsConnection(err)) {
        void vscode.window.showErrorMessage(`Armada: ${message}`, 'Connect').then((choice) => {
          if (choice === 'Connect') void vscode.commands.executeCommand('armada.connect');
        });
      } else {
        void vscode.window.showErrorMessage(`Armada: ${message}`);
      }
      return fallback;
    }
  }
}

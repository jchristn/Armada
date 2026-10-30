import * as vscode from 'vscode';
import { ArmadaApiError, ArmadaBlackBoxClient, describeError } from '../api/blackbox';
import { BlackBoxAttemptDiff, BlackBoxEntrySummary, BlackBoxResilienceGap, BlackBoxTimelinePage, EnumerationResult, MissionOverlapPreview, Vessel } from '../api/types';
import { BlackBoxStore } from '../state/blackBoxStore';
import { entryLabel, formatAge } from '../util/format';
import { renderDiffMarkdown, renderGapsMarkdown, renderOverlapMarkdown, renderTimelineMarkdown } from '../views/insightMarkdown';

/** Timeline events per page. */
export const TIMELINE_PAGE_SIZE = 100;

/** Resilience gaps per page (the server caps it at 50). */
export const GAPS_PAGE_SIZE = 25;

/** Navigation buttons offered after a timeline page. */
export const TIMELINE_NEXT = 'Next Page';
export const TIMELINE_PREVIOUS = 'Previous Page';

const ENTER_OTHER_LABEL = '$(edit) Enter another entry id (bbx_...)...';

/** Outcome of "Preview Mission Overlap", for callers and tests. */
export type OverlapOutcome = 'shown' | 'clean' | 'noVessel' | 'cancelled' | 'failed';

/** Dependencies of the insight commands. */
export interface InsightCommandDeps {
  getClient(): ArmadaBlackBoxClient;
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
      const client = this._Deps.getClient();
      let pageNumber = 1;
      for (let guard = 0; guard < 1000; guard++) {
        const page = await client.getTimeline(entry.Id, pageNumber, TIMELINE_PAGE_SIZE);
        shown.push(page);
        await this.openMarkdown(renderTimelineMarkdown(entry, page));
        const buttons: string[] = [];
        if (page.PageNumber > 1) buttons.push(TIMELINE_PREVIOUS);
        if (page.PageNumber < page.TotalPages) buttons.push(TIMELINE_NEXT);
        if (!buttons.length) return;
        const choice = await vscode.window.showInformationMessage(`Armada: timeline page ${page.PageNumber} of ${page.TotalPages} for ${entryLabel(entry)}.`, ...buttons);
        if (choice === TIMELINE_NEXT) pageNumber = page.PageNumber + 1;
        else if (choice === TIMELINE_PREVIOUS) pageNumber = page.PageNumber - 1;
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
      const client = this._Deps.getClient();
      const vessels = Array.from(new Map(this._Deps.store.matches.map((m) => [m.vessel.Id, m.vessel])).values());
      const vesselId = vessels.length === 1 ? vessels[0].Id : undefined;
      let pageNumber = 1;
      for (let guard = 0; guard < 1000; guard++) {
        const page = await client.getResilienceGaps({ vesselId }, pageNumber, GAPS_PAGE_SIZE);
        shown.push(page);
        await this.openMarkdown(renderGapsMarkdown(page));
        const buttons: string[] = [];
        if (page.PageNumber > 1) buttons.push(TIMELINE_PREVIOUS);
        if (page.PageNumber < page.TotalPages) buttons.push(TIMELINE_NEXT);
        if (!buttons.length) return;
        const choice = await vscode.window.showInformationMessage(`Armada: resilience gaps page ${page.PageNumber} of ${page.TotalPages}.`, ...buttons);
        if (choice === TIMELINE_NEXT) pageNumber = page.PageNumber + 1;
        else if (choice === TIMELINE_PREVIOUS) pageNumber = page.PageNumber - 1;
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
      const client = this._Deps.getClient();
      let other = otherId?.trim() || undefined;
      let otherLabel = other ?? '';
      if (!other) {
        let attempts: BlackBoxEntrySummary[] = [];
        if (entry.MissionId) {
          try {
            attempts = (await client.listMissionEntries(entry.MissionId)).Objects.filter((e) => e.Id !== entry.Id);
          } catch {
            attempts = [];
          }
        }
        type Item = vscode.QuickPickItem & { entry?: BlackBoxEntrySummary };
        const items: Item[] = [
          ...attempts.map((e) => ({
            label: `Attempt ${e.Attempt ?? '?'}`,
            description: [e.FailureKind, e.Step, formatAge(e.CreatedUtc)].filter(Boolean).join(', '),
            detail: e.Id,
            entry: e,
          })),
          { label: ENTER_OTHER_LABEL, alwaysShow: true },
        ];
        const picked = attempts.length
          ? await vscode.window.showQuickPick(items, { title: `Compare ${entryLabel(entry)} with`, placeHolder: 'Pick another attempt of this mission, or enter an entry id' })
          : { label: ENTER_OTHER_LABEL } as Item;
        if (!picked) return undefined;
        if (picked.entry) {
          other = picked.entry.Id;
          otherLabel = `${picked.entry.Id}`;
        } else {
          const typed = await vscode.window.showInputBox({
            title: `Compare ${entryLabel(entry)} with`,
            prompt: attempts.length ? 'Black box entry id (bbx_...)' : 'This mission has no other attempts. Enter any black box entry id (bbx_...) to compare with.',
            ignoreFocusOut: true,
            validateInput: (v) => (!v.trim() ? 'Enter an entry id' : v.trim() === entry.Id ? 'Pick a different entry' : undefined),
          });
          if (!typed || !typed.trim()) return undefined;
          other = typed.trim();
          otherLabel = other;
        }
      }
      const diff = await client.diffEntries(entry.Id, other);
      await this.openMarkdown(renderDiffMarkdown(diff, entry.Id, otherLabel));
      return diff;
    }, undefined);
  }

  /**
   * Armada: Preview Mission Overlap. Resolves the open repository's vessel (as the Failures view does, by remote URL),
   * asks for a title and brief, and shows the overlap and brief lint warnings. Nothing is dispatched.
   */
  public async previewOverlap(arg?: unknown): Promise<OverlapOutcome> {
    return this.guard<OverlapOutcome>(async () => {
      const vessel = await this.pickVessel(typeof arg === 'string' ? arg : undefined);
      if (vessel === null) return 'noVessel';
      if (!vessel) return 'cancelled';
      const title = await vscode.window.showInputBox({ title: `Preview Mission Overlap: ${vessel.Name ?? vessel.Id}`, prompt: 'Mission title (optional)', ignoreFocusOut: true });
      if (title === undefined) return 'cancelled';
      const brief = await vscode.window.showInputBox({
        title: `Preview Mission Overlap: ${vessel.Name ?? vessel.Id}`,
        prompt: 'Mission brief: what the mission should do (name the files or directories it will touch)',
        ignoreFocusOut: true,
        validateInput: (v) => (!v.trim() && !title.trim() ? 'Enter a brief' : undefined),
      });
      if (brief === undefined) return 'cancelled';
      if (!brief.trim() && !title.trim()) return 'cancelled';
      const preview: MissionOverlapPreview = await this._Deps.getClient().previewOverlap(vessel.Id, { Title: title, Description: brief });
      await this.openMarkdown(renderOverlapMarkdown(preview, vessel.Name ?? vessel.Id, title.trim()));
      const overlaps = preview.WarningCount ?? preview.Warnings?.length ?? 0;
      const lint = preview.BriefLint?.FindingCount ?? preview.BriefLint?.Findings?.length ?? 0;
      if (!overlaps && !lint) {
        void vscode.window.showInformationMessage(`Armada: no overlap or brief lint warnings for this mission on ${vessel.Name ?? vessel.Id}.`);
        return 'clean';
      }
      void vscode.window.showWarningMessage(`Armada: ${overlaps} overlap warning${overlaps === 1 ? '' : 's'} and ${lint} brief lint warning${lint === 1 ? '' : 's'} on ${vessel.Name ?? vessel.Id}.`);
      return 'shown';
    }, 'failed');
  }

  /**
   * The vessel to preview against: an explicit id, the single matched workspace vessel, a pick among several matches,
   * or a pick among every vessel when none matches. Null when the server has no vessels; undefined when cancelled.
   */
  private async pickVessel(vesselId?: string): Promise<Vessel | null | undefined> {
    const store = this._Deps.store;
    if (vesselId && vesselId.trim()) {
      const id = vesselId.trim();
      return store.vessels.find((v) => v.Id === id) ?? { Id: id };
    }
    if (!store.matches.length) await store.refresh();
    const matched = Array.from(new Map(store.matches.map((m) => [m.vessel.Id, m.vessel])).values());
    if (matched.length === 1) return matched[0];
    let candidates = matched;
    let placeHolder = 'Several vessels match the open folders; pick one';
    if (!candidates.length) {
      candidates = store.vessels.length ? store.vessels : await this._Deps.getClient().listAllVessels();
      placeHolder = 'No vessel matches the open repository (by git remote URL); pick one';
    }
    if (!candidates.length) {
      void vscode.window.showWarningMessage('Armada: no vessels are registered on this Armada server, so there is nothing to compare the mission with.');
      return null;
    }
    const picked = await vscode.window.showQuickPick(
      candidates.map((v) => ({ label: v.Name ?? v.Id, description: v.Id, detail: v.RepoUrl ?? undefined, vessel: v })),
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
      if (err instanceof ArmadaApiError && (err.kind === 'notConfigured' || err.kind === 'unauthorized')) {
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

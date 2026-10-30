import * as vscode from 'vscode';
import { ArmadaApiError, ArmadaBlackBoxClient, askArmadaPrompt, dashboardAskUrl, describeError } from '../api/blackbox';
import { BlackBoxEntrySummary, BlackBoxRecoveryPath, BlackBoxRecoveryResult, BlackBoxResolution, RESOLUTION_CHOICES } from '../api/types';
import { ArmadaSettings } from '../config';
import { changeBrief, rankedChanges } from '../views/entryHtml';
import { BlackBoxStore } from '../state/blackBoxStore';
import { entryKind, entryLabel, entrySubjectId, formatAge } from '../util/format';

/** The chat command feature-detected by "Send FIX THIS to Chat". */
export const CHAT_OPEN_COMMAND = 'workbench.action.chat.open';

/** Outcome of a recovery run, for callers and tests. */
export type RecoveryOutcome = 'ran' | 'ranWithErrors' | 'cancelled' | 'noPaths' | 'noToken' | 'refusedDataLoss' | 'conflict' | 'failed';

/** Outcome of "Draft Armada Fix Mission". */
export type DraftOutcome = 'chat' | 'copied' | 'noChange' | 'cancelled' | 'failed';

/** Outcome of "Send FIX THIS to Chat". */
export type ChatOutcome = 'chatWithAttachment' | 'chatWithText' | 'copied' | 'cancelled' | 'failed';

/** Dependencies of the command handlers. */
export interface CommandDeps {
  getClient(): ArmadaBlackBoxClient;
  store: BlackBoxStore;
  getSettings(): ArmadaSettings;
  /** Folder for temporary brief files handed to chat. */
  storageUri: vscode.Uri | undefined;
  /** Opens the entry view. */
  openEntryView?(entry: BlackBoxEntrySummary): Promise<void>;
}

/** Text of the data loss acknowledgement checkbox. */
export const DATA_LOSS_ACK_LABEL = 'I understand this recovery path can permanently lose work (for example deleting a branch or reclaiming a dock).';

const ENTER_ID_LABEL = '$(edit) Enter a mission, voyage or entry id...';

/** Command logic for the black box. Every public method shows its own errors and never throws. */
export class BlackBoxCommands {
  private readonly _Deps: CommandDeps;

  /**
   * @throws Error when deps is missing.
   */
  public constructor(deps: CommandDeps) {
    if (!deps) throw new Error('deps is required');
    this._Deps = deps;
  }

  /** Armada: Open Black Box for Mission... */
  public async openBlackBox(arg?: unknown): Promise<BlackBoxEntrySummary | undefined> {
    return this.guard(async () => {
      const entry = await this.pickEntry(arg, 'Open Black Box for Mission');
      if (!entry) return undefined;
      if (this._Deps.openEntryView) await this._Deps.openEntryView(entry);
      return entry;
    }, undefined);
  }

  /** Armada: Copy FIX THIS. Returns the brief that was copied. */
  public async copyFixThis(arg?: unknown): Promise<string | undefined> {
    return this.guard(async () => {
      const entry = await this.pickEntry(arg, 'Copy FIX THIS');
      if (!entry) return undefined;
      const brief = await this.brief(entry);
      await vscode.env.clipboard.writeText(brief);
      void vscode.window.showInformationMessage(`Armada: FIX THIS brief for ${entrySubjectId(entry)} copied. Paste it into your coding agent and say "FIX THIS".`);
      return brief;
    }, undefined);
  }

  /** Armada: Open FIX THIS in Editor (untitled Markdown, plus a preview). */
  public async openFixThisInEditor(arg?: unknown): Promise<vscode.TextDocument | undefined> {
    return this.guard(async () => {
      const entry = await this.pickEntry(arg, 'Open FIX THIS in Editor');
      if (!entry) return undefined;
      const brief = await this.brief(entry);
      const doc = await vscode.workspace.openTextDocument({ language: 'markdown', content: brief });
      await vscode.window.showTextDocument(doc, { preview: false });
      if (this._Deps.getSettings().previewFixThis) {
        try {
          await vscode.commands.executeCommand('markdown.showPreviewToSide', doc.uri);
        } catch {
          // the Markdown extension may be disabled; the editor already shows the brief
        }
      }
      return doc;
    }, undefined);
  }

  /** Armada: Send FIX THIS to Chat. Uses the editor chat when the host has one, otherwise copies. */
  public async sendFixThisToChat(arg?: unknown): Promise<ChatOutcome> {
    return this.guard<ChatOutcome>(async () => {
      const entry = await this.pickEntry(arg, 'Send FIX THIS to Chat');
      if (!entry) return 'cancelled';
      const brief = await this.brief(entry);
      await vscode.env.clipboard.writeText(brief);

      const available = await vscode.commands.getCommands(true);
      if (!available.includes(CHAT_OPEN_COMMAND)) {
        void vscode.window.showInformationMessage('Armada: this editor has no chat. The FIX THIS brief was copied to the clipboard; paste it into your coding agent.');
        return 'copied';
      }

      const fileUri = await this.writeBriefFile(entry, brief);
      if (fileUri) {
        try {
          await vscode.commands.executeCommand(CHAT_OPEN_COMMAND, { query: 'FIX THIS', isPartialQuery: true, attachFiles: [fileUri] });
          return 'chatWithAttachment';
        } catch {
          // older chat builds reject attachFiles; fall through to the text form
        }
      }
      try {
        await vscode.commands.executeCommand(CHAT_OPEN_COMMAND, { query: `FIX THIS\n\n${brief}`, isPartialQuery: true });
        return 'chatWithText';
      } catch {
        void vscode.window.showInformationMessage('Armada: could not open the chat. The FIX THIS brief was copied to the clipboard.');
        return 'copied';
      }
    }, 'failed');
  }

  /**
   * Armada: Draft Armada Fix Mission. Copies the top proposed Armada change of the entry's resilience section as a
   * mission brief and, when the host has a chat, opens it there; otherwise the brief stays on the clipboard.
   */
  public async draftArmadaFixMission(arg?: unknown): Promise<DraftOutcome> {
    return this.guard<DraftOutcome>(async () => {
      const summary = await this.pickEntry(arg, 'Draft Armada Fix Mission');
      if (!summary) return 'cancelled';
      const entry = await this._Deps.getClient().getEntry(summary.Id);
      const resilience = entry.Record?.Analysis?.Resilience ?? null;
      const top = rankedChanges(resilience)[0];
      if (!top) {
        void vscode.window.showInformationMessage(`Armada: ${resilience?.NoChangeReason ?? `entry ${entry.Id} proposes no Armada change.`}`);
        return 'noChange';
      }
      const brief = changeBrief(top);
      await vscode.env.clipboard.writeText(brief);
      const available = await vscode.commands.getCommands(true);
      if (available.includes(CHAT_OPEN_COMMAND)) {
        try {
          await vscode.commands.executeCommand(CHAT_OPEN_COMMAND, { query: `Draft this Armada fix mission on the Armada vessel:\n\n${brief}`, isPartialQuery: true });
          return 'chat';
        } catch {
          // fall through to the copied message
        }
      }
      void vscode.window.showInformationMessage(`Armada: the Armada fix mission brief "${top.Title}" was copied. Dispatch it on the Armada vessel.`);
      return 'copied';
    }, 'failed');
  }

  /** Armada: Run Recovery Path... (dry run, plan, acknowledgement when needed, modal confirm, run). */
  public async runRecoveryPath(arg?: unknown, pathId?: string): Promise<RecoveryOutcome> {
    return this.guard<RecoveryOutcome>(async () => {
      const summary = await this.pickEntry(arg, 'Run Recovery Path');
      if (!summary) return 'cancelled';
      const client = this._Deps.getClient();
      const entry = await client.getEntry(summary.Id);
      const paths = entry.Record?.Analysis?.RecoveryPaths ?? [];
      if (!paths.length) {
        void vscode.window.showInformationMessage(`Armada: entry ${entry.Id} has no recovery paths.`);
        return 'noPaths';
      }

      let path: BlackBoxRecoveryPath | undefined = pathId ? paths.find((p) => p.Id === pathId) : undefined;
      if (!path) {
        const picked = await vscode.window.showQuickPick(
          paths.map((p, i) => ({
            label: `${i + 1}. ${p.Title}`,
            description: [p.Id, p.Risk ? `risk ${p.Risk}` : '', p.DataLossRisk ? 'DATA LOSS RISK' : ''].filter(Boolean).join(', '),
            detail: p.ExpectedOutcome,
            path: p,
          })),
          { title: `Recovery paths for ${entryLabel(entry)}`, placeHolder: 'Pick a recovery path to dry run (nothing runs until you confirm)', ignoreFocusOut: true },
        );
        if (!picked) return 'cancelled';
        path = picked.path;
      }

      const dry = await client.dryRunRecovery(entry.Id, path.Id);
      if (!dry.ConfirmToken) {
        void vscode.window.showErrorMessage(`Armada: the dry run for ${path.Id} returned no confirm token, so nothing was run.${dry.Message ? ` ${dry.Message}` : ''}`);
        return 'noToken';
      }

      const dataLoss = path.DataLossRisk === true || (dry.Plan ?? []).some((s) => s.DataLossRisk === true);
      if (dataLoss) {
        const ack = await vscode.window.showQuickPick([{ label: DATA_LOSS_ACK_LABEL, picked: false }], {
          title: `"${path.Title}" is marked dataLossRisk`,
          placeHolder: 'Tick the acknowledgement and press Enter to continue, or Escape to refuse',
          canPickMany: true,
          ignoreFocusOut: true,
        });
        if (!ack || ack.length === 0) {
          void vscode.window.showWarningMessage(`Armada: refused to run "${path.Title}" because it can lose data and the risk was not acknowledged.`);
          return 'refusedDataLoss';
        }
      }

      const choice = await vscode.window.showWarningMessage(
        `Run recovery path "${path.Title}" for ${entrySubjectId(entry)}?`,
        { modal: true, detail: describePlan(path, dry, dataLoss) },
        'Run',
      );
      if (choice !== 'Run') return 'cancelled';

      let result: BlackBoxRecoveryResult;
      try {
        result = await client.runRecovery(entry.Id, path.Id, dry.ConfirmToken, dataLoss);
      } catch (err) {
        if (err instanceof ArmadaApiError && err.kind === 'conflict') {
          void vscode.window.showWarningMessage(`Armada: the confirm token expired or the mission changed since the dry run. Run the recovery path again to get a new dry run.${err.serverMessage ? ` (${err.serverMessage})` : ''}`);
          return 'conflict';
        }
        throw err;
      }

      const failedSteps = (result.StepResults ?? []).filter((s) => s.Succeeded === false);
      void this._Deps.store.refresh();
      if (result.Succeeded === false || failedSteps.length > 0) {
        void vscode.window.showWarningMessage(`Armada: recovery path "${path.Title}" finished with errors. ${failedSteps.map((s) => s.Message ?? s.Tool ?? '').filter(Boolean).join('; ') || result.Message || ''}`.trim());
        return 'ranWithErrors';
      }
      void vscode.window.showInformationMessage(`Armada: recovery path "${path.Title}" ran. ${result.Message ?? path.ExpectedOutcome ?? ''}`.trim());
      return 'ran';
    }, 'failed');
  }

  /** Armada: Resolve Black Box Entry. */
  public async resolveEntry(arg?: unknown): Promise<BlackBoxResolution | undefined> {
    return this.guard(async () => {
      const entry = await this.pickEntry(arg, 'Resolve Black Box Entry');
      if (!entry) return undefined;
      const resolution = await vscode.window.showQuickPick(RESOLUTION_CHOICES.map((r) => ({ label: r })), { title: `Resolve ${entryLabel(entry)}`, placeHolder: 'Resolution', ignoreFocusOut: true });
      if (!resolution) return undefined;
      const note = await vscode.window.showInputBox({ title: 'Resolution note (optional)', prompt: 'What fixed it?', ignoreFocusOut: true, validateInput: (v) => (v.length > 8000 ? 'Limited to 8000 characters' : undefined) });
      if (note === undefined) return undefined;
      const link = await vscode.window.showInputBox({ title: 'Resolution link (optional)', prompt: 'Mission id, commit, pull request or issue URL', ignoreFocusOut: true });
      if (link === undefined) return undefined;
      const value = resolution.label as BlackBoxResolution;
      await this._Deps.getClient().resolve(entry.Id, { Resolution: value, Note: note, Link: link });
      void vscode.window.showInformationMessage(`Armada: ${entry.Id} resolved as ${value}.`);
      void this._Deps.store.refresh();
      return value;
    }, undefined);
  }

  /** Armada: Add Note. */
  public async addNote(arg?: unknown): Promise<boolean> {
    return this.guard(async () => {
      const entry = await this.pickEntry(arg, 'Add Note');
      if (!entry) return false;
      const body = await vscode.window.showInputBox({
        title: `Note on ${entryLabel(entry)}`,
        prompt: 'Markdown, up to 8000 characters',
        ignoreFocusOut: true,
        validateInput: (v) => (!v.trim() ? 'A note cannot be empty' : v.length > 8000 ? 'Limited to 8000 characters' : undefined),
      });
      if (!body || !body.trim()) return false;
      await this._Deps.getClient().addNote(entry.Id, body);
      void vscode.window.showInformationMessage(`Armada: note added to ${entry.Id}.`);
      return true;
    }, false);
  }

  /** Ask Armada about this failure: opens the dashboard Ask page with the prompt pre-filled (?prompt=); copies the prompt
   * when the dashboard cannot be pre-filled or the browser cannot be opened. */
  public async askArmada(arg?: unknown): Promise<{ url: string; prefilled: boolean } | undefined> {
    return this.guard(async () => {
      const entry = await this.pickEntry(arg, 'Ask Armada about this failure');
      if (!entry) return undefined;
      const settings = this._Deps.getSettings();
      if (!settings.dashboardUrl) throw new ArmadaApiError('notConfigured', 'No dashboard URL.');
      const prompt = askArmadaPrompt(entrySubjectId(entry));
      const target = dashboardAskUrl(settings.dashboardUrl, prompt);
      if (!target.prefilled) await vscode.env.clipboard.writeText(prompt);
      const opened = await vscode.env.openExternal(vscode.Uri.parse(target.url));
      if (!opened && target.prefilled) await vscode.env.clipboard.writeText(prompt);
      if (!target.prefilled || !opened) {
        void vscode.window.showInformationMessage(opened
          ? 'Armada: the Ask Armada prompt was copied. Paste it into the new conversation.'
          : `Armada: could not open the dashboard Ask page. The Ask Armada prompt was copied to the clipboard.`);
      }
      return target;
    }, undefined);
  }

  /**
   * Resolves a command argument (tree node, entry, id) to an entry, or shows a quick pick of recent failures with an
   * option to paste an id. Returns undefined when the user cancels.
   */
  public async pickEntry(arg: unknown, title: string): Promise<BlackBoxEntrySummary | undefined> {
    const fromArg = entryFromArg(arg);
    if (fromArg) return fromArg;
    const client = this._Deps.getClient();
    if (typeof arg === 'string' && arg.trim()) return client.getEntryForAnyId(arg);

    let recent = this._Deps.store.entries;
    if (!recent.length) {
      try {
        recent = (await client.enumerateEntries({ IsCurrent: true, PageSize: 25 })).Objects;
      } catch {
        recent = [];
      }
    }
    type Item = vscode.QuickPickItem & { entry?: BlackBoxEntrySummary };
    const items: Item[] = [
      { label: ENTER_ID_LABEL, alwaysShow: true },
      ...recent.map((e) => ({
        label: entryLabel(e),
        description: [entryKind(e), e.Step, formatAge(e.CreatedUtc)].filter(Boolean).join(', '),
        detail: [e.VesselName ?? e.VesselId, e.Id, e.Resolution && e.Resolution !== 'Open' ? e.Resolution : ''].filter(Boolean).join('  '),
        entry: e,
      })),
    ];
    const picked = await vscode.window.showQuickPick(items, { title: `Armada: ${title}`, placeHolder: 'Pick a recent failure, or enter an id', matchOnDescription: true, matchOnDetail: true });
    if (!picked) return undefined;
    if (picked.entry) return picked.entry;
    const id = await vscode.window.showInputBox({
      title: `Armada: ${title}`,
      prompt: 'Mission id (msn_...), voyage id (vyg_...) or black box entry id (bbx_...)',
      ignoreFocusOut: true,
      validateInput: (v) => (v.trim() ? undefined : 'Enter an id'),
    });
    if (!id || !id.trim()) return undefined;
    return client.getEntryForAnyId(id);
  }

  private brief(entry: BlackBoxEntrySummary): Promise<string> {
    return this._Deps.getClient().getBrief(entry.Id);
  }

  private async writeBriefFile(entry: BlackBoxEntrySummary, brief: string): Promise<vscode.Uri | undefined> {
    const base = this._Deps.storageUri;
    if (!base) return undefined;
    try {
      const dir = vscode.Uri.joinPath(base, 'briefs');
      await vscode.workspace.fs.createDirectory(dir);
      const file = vscode.Uri.joinPath(dir, `FIX-THIS-${entry.Id.replace(/[^A-Za-z0-9_-]/g, '_')}.md`);
      await vscode.workspace.fs.writeFile(file, Buffer.from(brief, 'utf8'));
      return file;
    } catch {
      return undefined;
    }
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

/** Extracts an entry from a tree node, an entry object, or a webview argument. */
export function entryFromArg(arg: unknown): BlackBoxEntrySummary | undefined {
  if (!arg || typeof arg !== 'object') return undefined;
  const node = arg as { kind?: unknown; entry?: unknown; Id?: unknown; CreatedUtc?: unknown };
  if (node.kind === 'entry' && node.entry && typeof node.entry === 'object' && typeof (node.entry as { Id?: unknown }).Id === 'string') {
    return node.entry as BlackBoxEntrySummary;
  }
  if (typeof node.Id === 'string' && node.Id) return arg as BlackBoxEntrySummary;
  return undefined;
}

/** Describes what a dry run says will happen. */
export function describePlan(path: BlackBoxRecoveryPath, dry: BlackBoxRecoveryResult, dataLoss: boolean): string {
  const lines: string[] = [];
  if (dry.Message) lines.push(dry.Message, '');
  const steps = dry.Plan ?? [];
  if (steps.length) {
    lines.push('Will run:');
    steps.forEach((s, i) => {
      const args = s.Action?.Arguments && Object.keys(s.Action.Arguments).length ? ` ${JSON.stringify(s.Action.Arguments)}` : '';
      const call = s.Action ? `${s.Action.Tool}${args}` : (s.McpCommand ?? s.CliCommand ?? 'step');
      lines.push(`${i + 1}. ${s.Description ? `${s.Description} (${call})` : call}${s.DataLossRisk ? ' [data loss risk]' : ''}`);
    });
  } else if (path.Action) {
    lines.push(`Will run: ${path.Action.Tool}${path.Action.Arguments ? ` ${JSON.stringify(path.Action.Arguments)}` : ''}`);
  }
  const outcome = path.ExpectedOutcome;
  if (outcome) lines.push('', `Expected outcome: ${outcome}`);
  if (dataLoss) lines.push('', 'WARNING: this path is marked dataLossRisk. You acknowledged that work can be lost.');
  if (dry.ConfirmTokenExpiresUtc) lines.push('', `The confirmation expires at ${dry.ConfirmTokenExpiresUtc}.`);
  return lines.join('\n');
}

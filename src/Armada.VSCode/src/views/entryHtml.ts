import { BlackBoxEntry, BlackBoxNote, BlackBoxRecord } from '../api/types';
import { directoryPath, entryKind, entryLabel, filePath, formatDate, isOpen } from '../util/format';

/** Actions the entry view can post back to the extension. */
export type EntryViewAction = 'copyFixThis' | 'openFixThis' | 'sendToChat' | 'runRecovery' | 'resolve' | 'addNote' | 'askArmada' | 'refresh';

/** Message posted by the webview. */
export interface EntryViewMessage {
  action: EntryViewAction;
  pathId?: string;
}

/** Escapes text for HTML. */
export function escapeHtml(value: unknown): string {
  return String(value ?? '')
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}

/**
 * Renders the entry summary page: Summary, Where, How, Status, Why, Conflict, What would have prevented it, What
 * Armada could change, Recovery, Attempts and Notes. Pure; all values are escaped. The nonce gates the inline script
 * under the page's Content-Security-Policy.
 */
export function renderEntryHtml(entry: BlackBoxEntry, notes: BlackBoxNote[], nonce: string, cspSource: string): string {
  const record: BlackBoxRecord = entry.Record ?? {};
  const analysis = record.Analysis ?? {};
  const site = record.Site ?? {};
  const mech = record.Mechanism ?? {};
  const cls = record.Classification ?? {};
  const conflict = record.Conflict ?? {};
  const sections: string[] = [];

  sections.push(section('Summary', [
    analysis.Summary ? `<p>${escapeHtml(analysis.Summary)}</p>` : '<p class="muted">No analysis summary yet (the entry may still be enriching).</p>',
    table([
      ['Entry', entry.Id],
      ['Mission', entry.MissionId],
      ['Voyage', entry.VoyageId],
      ['Vessel', entry.VesselName || entry.VesselId],
      ['Kind', entryKind(entry)],
      ['Attempt', entry.Attempt],
      ['Triage', analysis.Triage?.Verdict ? `${analysis.Triage.Verdict}${analysis.Triage.Confidence !== undefined ? ` (${analysis.Triage.Confidence})` : ''}` : entry.Triage],
      ['Resolution', entry.Resolution ?? 'Open'],
      ['Captured', entry.CreatedUtc],
      ['Capture mode', entry.CaptureMode ?? record.CaptureMode],
    ]),
  ]));

  sections.push(section('Where', [table([
    ['Phase', site.Phase ?? entry.Phase],
    ['Step', site.Step ?? entry.Step],
    ['Host', site.Host],
    ['Component', site.Component],
    ['Method', site.Method],
    ['Source', site.SourceFile ? `${site.SourceFile}${site.SourceLine ? `:${site.SourceLine}` : ''}` : undefined],
    ['Armada', [site.ArmadaVersion ?? entry.ArmadaVersion, site.ArmadaCommit ?? entry.ArmadaCommit].filter(Boolean).join(' ')],
  ])]));

  const how: string[] = [table([
    ['Exception', mech.ExceptionType],
    ['Message', mech.ExceptionMessage],
    ['Exit code', mech.ExitCode],
    ['Runtime failure', mech.RuntimeFailureKind],
    ['Failing command', mech.FailingCommand],
    ['Command exit code', mech.CommandExitCode],
    ['Git operation', mech.GitOperation],
    ['Reason', cls.RawReason],
  ])];
  if (mech.CommandOutputTail) how.push(`<details><summary>Command output (tail)</summary><pre>${escapeHtml(mech.CommandOutputTail)}</pre></details>`);
  if (mech.GitStderrTail) how.push(`<details><summary>Git stderr (tail)</summary><pre>${escapeHtml(mech.GitStderrTail)}</pre></details>`);
  sections.push(section('How', how));

  sections.push(section('Status', [table([
    ['Before', record.Status?.Before ?? entry.StatusBefore],
    ['After', record.Status?.After ?? entry.StatusAfter],
    ['Changed', record.Status?.ChangedUtc],
  ])]));

  const why = analysis.Why ?? [];
  sections.push(section('Why', [why.length
    ? `<ul>${why.map((w) => `<li>${escapeHtml(w.Statement)}${w.RuleId ? ` <code>${escapeHtml(w.RuleId)}</code>` : ''}${w.Confidence !== undefined ? ` <span class="muted">(${escapeHtml(w.Confidence)})</span>` : ''}</li>`).join('')}</ul>`
    : `<p class="muted">${escapeHtml(cls.SuspectedCause ? `Suspected cause: ${cls.SuspectedCause}` : 'No findings recorded.')}</p>`]));

  if (conflict.IsConflict || (conflict.ConflictedFiles?.length ?? 0) > 0 || (conflict.ConflictingMissions?.length ?? 0) > 0) {
    const files = (conflict.ConflictedFiles ?? []).map(filePath).filter((p): p is string => !!p);
    const dirs = (conflict.ConflictedDirectories ?? []).map((d) => ({ path: directoryPath(d), count: d.FileCount }));
    const missions = conflict.ConflictingMissions ?? [];
    const parts: string[] = [];
    if (missions.length) parts.push(`<h3>Conflicting missions</h3><ul>${missions.map((m) => `<li><code>${escapeHtml(m.MissionId ?? (m.IsOutsideArmada ? 'outside Armada' : ''))}</code> ${escapeHtml(m.Title ?? '')}${m.CommitHash ? ` <code>${escapeHtml(m.CommitHash.slice(0, 12))}</code>` : ''}${m.LandedUtc ? ` <span class="muted">landed ${escapeHtml(formatDate(m.LandedUtc))}</span>` : ''}${(m.OverlappingFiles ?? []).length ? `<br><span class="muted">overlapping: ${escapeHtml((m.OverlappingFiles ?? []).join(', '))}${(m.OverlappingFileTotal ?? 0) > (m.OverlappingFiles ?? []).length ? ` and ${escapeHtml((m.OverlappingFileTotal ?? 0) - (m.OverlappingFiles ?? []).length)} more` : ''}</span>` : ''}</li>`).join('')}</ul>`);
    if (files.length) parts.push(`<h3>Files (${conflict.ConflictedFileTotal ?? files.length})</h3><ul>${files.map((f) => `<li><code>${escapeHtml(f)}</code></li>`).join('')}</ul>`);
    if (dirs.length) parts.push(`<h3>Directories</h3><ul>${dirs.map((d) => `<li><code>${escapeHtml(d.path ?? '')}</code>${d.count !== undefined ? ` (${escapeHtml(d.count)})` : ''}</li>`).join('')}</ul>`);
    sections.push(section('Conflict', parts));
  }

  const recs = analysis.ConfigRecommendations ?? [];
  if (recs.length) {
    sections.push(section('What would have prevented it', [`<ul>${recs.map((r) => `<li><strong>${escapeHtml(r.Setting)}</strong>${r.Scope ? ` (${escapeHtml(r.Scope)})` : ''}: ${escapeHtml(r.CurrentValue ?? '?')} to ${escapeHtml(r.SuggestedValue ?? '?')}. ${escapeHtml(r.Rationale ?? '')}${r.HowToApply ? `<br><span class="muted">${escapeHtml(r.HowToApply)}</span>` : ''}</li>`).join('')}</ul>`]));
  }

  const improvements = analysis.ArmadaImprovements ?? [];
  if (improvements.length) {
    sections.push(section('What Armada could change', [`<ul>${improvements.map((i) => `<li>${i.Area ? `<strong>${escapeHtml(i.Area)}</strong>: ` : ''}${escapeHtml(i.Hypothesis)}${i.Severity ? ` <span class="muted">(${escapeHtml(i.Severity)})</span>` : ''}</li>`).join('')}</ul>`]));
  }

  const paths = analysis.RecoveryPaths ?? [];
  sections.push(section('Recovery', [paths.length
    ? `<ol>${paths.map((p) => `<li><strong>${escapeHtml(p.Title)}</strong> <code>${escapeHtml(p.Id)}</code>${p.Risk ? ` <span class="muted">risk ${escapeHtml(p.Risk)}</span>` : ''}${p.DataLossRisk ? ' <span class="danger">data loss risk</span>' : ''}${p.ExpectedOutcome ? `<br>${escapeHtml(p.ExpectedOutcome)}` : ''}${(p.Steps ?? []).length ? `<ul>${(p.Steps ?? []).map((s) => `<li>${escapeHtml(s.Description ?? s.CliCommand ?? s.McpCommand ?? s.RestCommand ?? '')}</li>`).join('')}</ul>` : ''}<br><button data-action="runRecovery" data-path="${escapeHtml(p.Id)}">Dry run ${escapeHtml(p.Id)}</button></li>`).join('')}</ol>`
    : '<p class="muted">No recovery paths recorded.</p>']));

  const attempts = record.Attempts ?? [];
  if (attempts.length) {
    sections.push(section('Attempts', [`<ul>${attempts.map((a) => `<li>Attempt ${escapeHtml(a.Attempt ?? '?')}: ${escapeHtml(a.Kind ?? '')} ${escapeHtml(a.ReasonExcerpt ?? '')}${a.Runtime ? ` <span class="muted">${escapeHtml(a.Runtime)}${a.Model ? ` ${escapeHtml(a.Model)}` : ''}</span>` : ''}</li>`).join('')}</ul>`]));
  }

  sections.push(section('Notes', [notes.length
    ? `<ul>${notes.map((n) => `<li><span class="muted">${escapeHtml(n.AuthorKind ?? 'Operator')} ${escapeHtml(formatDate(n.CreatedUtc))}</span><br>${escapeHtml(n.Body)}</li>`).join('')}</ul>`
    : '<p class="muted">No notes.</p>']));

  const open = isOpen(entry);
  return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src ${cspSource} 'unsafe-inline'; script-src 'nonce-${nonce}';">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<title>${escapeHtml(entryLabel(entry))}</title>
<style>
body { font-family: var(--vscode-font-family); color: var(--vscode-foreground); padding: 0 16px 24px; line-height: 1.45; }
h1 { font-size: 1.3em; } h2 { font-size: 1.1em; border-bottom: 1px solid var(--vscode-panel-border); padding-bottom: 4px; margin-top: 20px; }
h3 { font-size: 1em; margin-bottom: 4px; }
table { border-collapse: collapse; } td { padding: 2px 12px 2px 0; vertical-align: top; } td:first-child { color: var(--vscode-descriptionForeground); white-space: nowrap; }
pre { background: var(--vscode-textCodeBlock-background); padding: 8px; overflow-x: auto; max-height: 320px; }
.muted { color: var(--vscode-descriptionForeground); } .danger { color: var(--vscode-errorForeground); font-weight: 600; }
.toolbar { position: sticky; top: 0; background: var(--vscode-editor-background); padding: 8px 0; display: flex; flex-wrap: wrap; gap: 6px; }
button { background: var(--vscode-button-background); color: var(--vscode-button-foreground); border: none; padding: 4px 10px; cursor: pointer; }
button.secondary { background: var(--vscode-button-secondaryBackground); color: var(--vscode-button-secondaryForeground); }
</style>
</head>
<body>
<h1>${escapeHtml(entryLabel(entry))}</h1>
<div class="toolbar">
<button data-action="copyFixThis">Copy FIX THIS</button>
<button data-action="openFixThis" class="secondary">Open FIX THIS in Editor</button>
<button data-action="sendToChat" class="secondary">Send FIX THIS to Chat</button>
<button data-action="runRecovery" class="secondary">Run Recovery Path...</button>
${open ? '<button data-action="resolve" class="secondary">Resolve</button>' : ''}
<button data-action="addNote" class="secondary">Add Note</button>
<button data-action="askArmada" class="secondary">Ask Armada about this failure</button>
<button data-action="refresh" class="secondary">Refresh</button>
</div>
${sections.join('\n')}
<script nonce="${nonce}">
const vscode = acquireVsCodeApi();
document.addEventListener('click', (event) => {
  const target = event.target instanceof Element ? event.target.closest('button[data-action]') : null;
  if (!target) return;
  vscode.postMessage({ action: target.getAttribute('data-action'), pathId: target.getAttribute('data-path') || undefined });
});
</script>
</body>
</html>`;
}

function section(title: string, body: string[]): string {
  return `<section><h2>${escapeHtml(title)}</h2>${body.join('')}</section>`;
}

function table(rows: Array<[string, unknown]>): string {
  const visible = rows.filter(([, v]) => v !== undefined && v !== null && v !== '');
  if (!visible.length) return '<p class="muted">Not recorded.</p>';
  return `<table>${visible.map(([k, v]) => `<tr><td>${escapeHtml(k)}</td><td>${escapeHtml(v)}</td></tr>`).join('')}</table>`;
}

/** True when a webview message is well formed. */
export function isEntryViewMessage(value: unknown): value is EntryViewMessage {
  if (!value || typeof value !== 'object') return false;
  const action = (value as { action?: unknown }).action;
  const allowed: EntryViewAction[] = ['copyFixThis', 'openFixThis', 'sendToChat', 'runRecovery', 'resolve', 'addNote', 'askArmada', 'refresh'];
  if (typeof action !== 'string' || !allowed.includes(action as EntryViewAction)) return false;
  const pathId = (value as { pathId?: unknown }).pathId;
  return pathId === undefined || pathId === null || typeof pathId === 'string';
}

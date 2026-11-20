import type { BlackBoxDoctorPage, BlackBoxEntry, BlackBoxNote, BlackBoxProposedChange, BlackBoxRecord, BlackBoxRecoveryPath, BlackBoxResilience } from '@dashboard/types/blackbox';
import { dashboardPageUrl } from '../../transport/hostClient';
import { directoryPath, entryKind, entryLabel, filePath, formatDate, isOpen } from '../../util/format';

/** Actions the entry view can post back to the extension. */
export type EntryViewAction = 'copyFixThis' | 'openFixThis' | 'sendToChat' | 'runRecovery' | 'resolve' | 'addNote' | 'askArmada' | 'timeline' | 'compareAttempts' | 'draftFix' | 'refresh' | 'openRoute';

/** Optional extras for the entry page. */
export interface EntryHtmlOptions {
  /** Dashboard base URL, for runbook and example entry links. Empty hides the links. */
  dashboardUrl?: string;
  /** The vessel's configuration doctor (recurring recommendations); omitted or failed hides the section. */
  doctor?: BlackBoxDoctorPage | null;
}

/** Message posted by the webview. */
export interface EntryViewMessage {
  action: EntryViewAction;
  pathId?: string;
  /** For openRoute: the dashboard route a link points to ("/missions/msn_x"), opened natively where possible. */
  route?: string;
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
 * Renders the entry summary page: Summary, Armada resilience (right after the summary), Where, How, Status, Why, Conflict, What would have prevented it, What
 * Armada could change, Recovery, Attempts and Notes. Pure; all values are escaped. The nonce gates the inline script
 * under the page's Content-Security-Policy.
 */
export function renderEntryHtml(entry: BlackBoxEntry, notes: BlackBoxNote[], nonce: string, cspSource: string, options: EntryHtmlOptions = {}): string {
  const dashboard = (options.dashboardUrl ?? '').replace(/\/+$/, '');
  const record: Partial<BlackBoxRecord> = entry.record ?? {};
  const analysis = record.analysis ?? {};
  const site = record.site ?? {};
  const mech = record.mechanism ?? {};
  const cls = record.classification ?? {};
  const conflict = record.conflict ?? {};
  const sections: string[] = [];

  sections.push(section('Summary', [
    analysis.summary ? `<p>${escapeHtml(analysis.summary)}</p>` : '<p class="muted">No analysis summary yet (the entry may still be enriching).</p>',
    table([
      ['Entry', entry.id],
      ['Mission', entry.missionId],
      ['Voyage', entry.voyageId],
      ['Vessel', entry.vesselName || entry.vesselId],
      ['Kind', entryKind(entry)],
      ['Attempt', entry.attempt],
      ['Triage', analysis.triage?.verdict ? `${analysis.triage.verdict}${analysis.triage.confidence !== undefined ? ` (${analysis.triage.confidence})` : ''}` : entry.triage],
      ['Resolution', entry.resolution ?? 'Open'],
      ['Captured', entry.createdUtc],
      ['Capture mode', entry.captureMode ?? record.captureMode],
    ]),
  ]));

  const resilience = analysis.resilience ?? null;
  if (resilience) sections.push(section('Armada resilience', [renderResilience(resilience, entry, record)]));

  sections.push(section('Where', [table([
    ['Phase', site.phase ?? entry.phase],
    ['Step', site.step ?? entry.step],
    ['Host', site.host],
    ['Component', site.component],
    ['Method', site.method],
    ['Source', site.sourceFile ? `${site.sourceFile}${site.sourceLine ? `:${site.sourceLine}` : ''}` : undefined],
    ['Armada', [site.armadaVersion ?? entry.armadaVersion, site.armadaCommit ?? entry.armadaCommit].filter(Boolean).join(' ')],
  ])]));

  const how: string[] = [table([
    ['Exception', mech.exceptionType],
    ['Message', mech.exceptionMessage],
    ['Exit code', mech.exitCode],
    ['Runtime failure', mech.runtimeFailureKind],
    ['Failing command', mech.failingCommand],
    ['Command exit code', mech.commandExitCode],
    ['Git operation', mech.gitOperation],
    ['Reason', cls.rawReason],
  ])];
  if (mech.commandOutputTail) how.push(`<details><summary>Command output (tail)</summary><pre>${escapeHtml(mech.commandOutputTail)}</pre></details>`);
  if (mech.gitStderrTail) how.push(`<details><summary>Git stderr (tail)</summary><pre>${escapeHtml(mech.gitStderrTail)}</pre></details>`);
  sections.push(section('How', how));

  sections.push(section('Status', [table([
    ['Before', record.status?.before ?? entry.statusBefore],
    ['After', record.status?.after ?? entry.statusAfter],
    ['Changed', record.status?.changedUtc],
  ])]));

  const why = analysis.why ?? [];
  sections.push(section('Why', [why.length
    ? `<ul>${why.map((w) => `<li>${escapeHtml(w.statement)}${w.ruleId ? ` <code>${escapeHtml(w.ruleId)}</code>` : ''}${w.confidence !== undefined ? ` <span class="muted">(${escapeHtml(w.confidence)})</span>` : ''}</li>`).join('')}</ul>`
    : `<p class="muted">${escapeHtml(cls.suspectedCause ? `Suspected cause: ${cls.suspectedCause}` : 'No findings recorded.')}</p>`]));

  if (conflict.isConflict || (conflict.conflictedFiles?.length ?? 0) > 0 || (conflict.conflictingMissions?.length ?? 0) > 0) {
    const files = (conflict.conflictedFiles ?? []).map(filePath).filter((p): p is string => !!p);
    const dirs = (conflict.conflictedDirectories ?? []).map((d) => ({ path: directoryPath(d), count: d.fileCount }));
    const missions = conflict.conflictingMissions ?? [];
    const parts: string[] = [];
    if (missions.length) parts.push(`<h3>Conflicting missions</h3><ul>${missions.map((m) => `<li><code>${escapeHtml(m.missionId ?? (m.isOutsideArmada ? 'outside Armada' : ''))}</code> ${escapeHtml(m.title ?? '')}${m.commitHash ? ` <code>${escapeHtml(m.commitHash.slice(0, 12))}</code>` : ''}${m.landedUtc ? ` <span class="muted">landed ${escapeHtml(formatDate(m.landedUtc))}</span>` : ''}${(m.overlappingFiles ?? []).length ? `<br><span class="muted">overlapping: ${escapeHtml((m.overlappingFiles ?? []).join(', '))}${(m.overlappingFileTotal ?? 0) > (m.overlappingFiles ?? []).length ? ` and ${escapeHtml((m.overlappingFileTotal ?? 0) - (m.overlappingFiles ?? []).length)} more` : ''}</span>` : ''}</li>`).join('')}</ul>`);
    if (files.length) parts.push(`<h3>Files (${conflict.conflictedFileTotal ?? files.length})</h3><ul>${files.map((f) => `<li><code>${escapeHtml(f)}</code></li>`).join('')}</ul>`);
    if (dirs.length) parts.push(`<h3>Directories</h3><ul>${dirs.map((d) => `<li><code>${escapeHtml(d.path ?? '')}</code>${d.count !== undefined ? ` (${escapeHtml(d.count)})` : ''}</li>`).join('')}</ul>`);
    sections.push(section('Conflict', parts));
  }

  const recs = analysis.configRecommendations ?? [];
  if (recs.length) {
    sections.push(section('What would have prevented it', [`<ul>${recs.map((r) => `<li><strong>${escapeHtml(r.setting)}</strong>${r.scope ? ` (${escapeHtml(r.scope)})` : ''}: ${escapeHtml(r.currentValue ?? '?')} to ${escapeHtml(r.suggestedValue ?? '?')}. ${escapeHtml(r.rationale ?? '')}${r.howToApply ? `<br><span class="muted">${escapeHtml(r.howToApply)}</span>` : ''}</li>`).join('')}</ul>`]));
  }

  const improvements = analysis.armadaImprovements ?? [];
  if (improvements.length) {
    sections.push(section('What Armada could change', [`<ul>${improvements.map((i) => `<li>${i.area ? `<strong>${escapeHtml(i.area)}</strong>: ` : ''}${escapeHtml(i.hypothesis)}${i.severity ? ` <span class="muted">(${escapeHtml(i.severity)})</span>` : ''}</li>`).join('')}</ul>`]));
  }

  const paths = analysis.recoveryPaths ?? [];
  sections.push(section('Recovery', [paths.length
    ? `<ol>${paths.map((p) => `<li><strong>${escapeHtml(p.title)}</strong> <code>${escapeHtml(p.id)}</code>${p.risk ? ` <span class="muted">risk ${escapeHtml(p.risk)}</span>` : ''}${p.dataLossRisk ? ' <span class="danger">data loss risk</span>' : ''}${p.expectedOutcome ? `<br>${escapeHtml(p.expectedOutcome)}` : ''}${recoveryExtras(p, dashboard)}${(p.steps ?? []).length ? `<ul>${(p.steps ?? []).map((s) => `<li>${escapeHtml(s.description ?? s.cliCommand ?? s.mcpCommand ?? s.restCommand ?? '')}</li>`).join('')}</ul>` : ''}<br><button data-action="runRecovery" data-path="${escapeHtml(p.id)}" title="Dry run first; nothing runs until you confirm">Dry Run ${escapeHtml(p.id)}</button></li>`).join('')}</ol>`
    : '<p class="muted">No recovery paths recorded.</p>']));

  const attempts = record.attempts ?? [];
  if (attempts.length) {
    sections.push(section('Attempts', [`<ul>${attempts.map((a) => `<li>Attempt ${escapeHtml(a.attempt ?? '?')}: ${escapeHtml(a.kind ?? '')} ${escapeHtml(a.reasonExcerpt ?? '')}${a.runtime ? ` <span class="muted">${escapeHtml(a.runtime)}${a.model ? ` ${escapeHtml(a.model)}` : ''}</span>` : ''}</li>`).join('')}</ul>`]));
  }

  const doctor = options.doctor;
  if (doctor && doctor.objects.length) {
    const rows = doctor.objects.map((f) => {
      const example = dashboard
        ? `<a href="${escapeHtml(dashboardPageUrl(dashboard, `/blackbox/${encodeURIComponent(f.exampleEntryId)}`))}">${escapeHtml(f.exampleEntryId)}</a>`
        : `<code>${escapeHtml(f.exampleEntryId)}</code>`;
      return `<li><strong>${escapeHtml(f.setting)}</strong>: ${escapeHtml(f.currentValue ?? '?')} to ${escapeHtml(f.suggestedValue ?? '?')}, recommended ${escapeHtml(f.count)} time${f.count === 1 ? '' : 's'}${f.ruleId ? ` by <code>${escapeHtml(f.ruleId)}</code>` : ''}${f.lastSeenUtc ? ` <span class="muted">last ${escapeHtml(formatDate(f.lastSeenUtc))}</span>` : ''}<br><span class="muted">Example ${example}${f.applicable ? ' (can be applied from that entry)' : ''}</span></li>`;
    }).join('');
    const more = doctor.totalRecords > doctor.objects.length ? `<p class="muted">${escapeHtml(doctor.totalRecords - doctor.objects.length)} more recurring recommendations on this vessel.</p>` : '';
    sections.push(section(`Recurring on this vessel (last ${doctor.windowDays ?? 30} days)`, [`<ul>${rows}</ul>`, more]));
  }

  sections.push(section('Notes', [notes.length
    ? `<ul>${notes.map((n) => `<li><span class="muted">${escapeHtml(n.authorKind ?? 'Operator')} ${escapeHtml(formatDate(n.createdUtc))}</span><br>${escapeHtml(n.body)}</li>`).join('')}</ul>`
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
body { font-family: var(--vscode-font-family); font-size: var(--vscode-font-size); color: var(--vscode-foreground); padding: 0 16px 24px; line-height: 1.45; overflow-wrap: anywhere; }
h1 { font-size: 1.3em; margin: 16px 0 4px; } h2 { font-size: 1.1em; border-bottom: 1px solid var(--vscode-panel-border); padding-bottom: 4px; margin-top: 20px; }
h3 { font-size: 1em; margin-bottom: 4px; }
table { border-collapse: collapse; max-width: 100%; } td { padding: 2px 12px 2px 0; vertical-align: top; } td:first-child { color: var(--vscode-descriptionForeground); white-space: nowrap; }
li { margin-bottom: 6px; }
code { font-family: var(--vscode-editor-font-family); }
pre { background: var(--vscode-textCodeBlock-background); padding: 8px; overflow-x: auto; max-height: 320px; overflow-wrap: normal; white-space: pre; }
summary { cursor: pointer; }
.badge { font-size: 0.75em; padding: 1px 6px; border-radius: 8px; background: var(--vscode-inputValidation-errorBackground); color: var(--vscode-foreground); border: 1px solid var(--vscode-errorForeground); vertical-align: middle; white-space: nowrap; }
.muted { color: var(--vscode-descriptionForeground); } .danger { color: var(--vscode-errorForeground); font-weight: 600; }
.toolbar { position: sticky; top: 0; z-index: 1; background: var(--vscode-editor-background); padding: 8px 0; border-bottom: 1px solid var(--vscode-panel-border); display: flex; flex-wrap: wrap; gap: 6px; }
button { font-family: inherit; font-size: inherit; line-height: 1.4; background: var(--vscode-button-background); color: var(--vscode-button-foreground); border: 1px solid var(--vscode-button-border, transparent); border-radius: 2px; padding: 4px 10px; cursor: pointer; }
button:hover { background: var(--vscode-button-hoverBackground); }
button:focus-visible { outline: 1px solid var(--vscode-focusBorder); outline-offset: 2px; }
button.secondary { background: var(--vscode-button-secondaryBackground); color: var(--vscode-button-secondaryForeground); }
button.secondary:hover { background: var(--vscode-button-secondaryHoverBackground); }
li > button { margin-top: 4px; }
</style>
</head>
<body>
<h1>${escapeHtml(entryLabel(entry))}${suspectedDefect(entry) ? ` <span class="badge">${escapeHtml(analysis.triage?.suspected ? 'Suspected Armada defect (prior only)' : 'Suspected Armada defect')}</span>` : ''}</h1>
<div class="toolbar" role="toolbar" aria-label="Black box actions">
<button data-action="copyFixThis">Copy FIX THIS</button>
<button data-action="openFixThis" class="secondary">Open FIX THIS in Editor</button>
<button data-action="sendToChat" class="secondary">Send FIX THIS to Chat</button>
<button data-action="runRecovery" class="secondary">Run Recovery Path...</button>
${open ? '<button data-action="resolve" class="secondary">Resolve</button>' : ''}
<button data-action="addNote" class="secondary">Add Note</button>
<button data-action="askArmada" class="secondary">Ask Armada About This Failure</button>
${(resilience?.proposedChanges ?? []).length ? '<button data-action="draftFix" class="secondary">Draft Armada Fix Mission</button>' : ''}
<button data-action="timeline" class="secondary">Timeline</button>
<button data-action="compareAttempts" class="secondary">Compare Attempts</button>
<button data-action="refresh" class="secondary">Refresh</button>
</div>
${sections.join('\n')}
<script nonce="${nonce}">
const vscode = acquireVsCodeApi();
document.addEventListener('click', (event) => {
  const link = event.target instanceof Element ? event.target.closest('a[href]') : null;
  const href = link ? link.getAttribute('href') || '' : '';
  const at = href.indexOf('/dashboard/');
  if (link && at >= 0) {
    event.preventDefault();
    vscode.postMessage({ action: 'openRoute', route: href.slice(at + '/dashboard'.length) });
    return;
  }
  const target = event.target instanceof Element ? event.target.closest('button[data-action]') : null;
  if (!target) return;
  vscode.postMessage({ action: target.getAttribute('data-action'), pathId: target.getAttribute('data-path') || undefined });
});
</script>
</body>
</html>`;
}

/** The page shown when an entry cannot be loaded: the entry label, the error and a Retry button. */
export function renderEntryErrorHtml(label: string, message: string, nonce: string): string {
  return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; script-src 'nonce-${nonce}';">
<title>${escapeHtml(label)}</title>
<style>
body { font-family: var(--vscode-font-family); font-size: var(--vscode-font-size); color: var(--vscode-foreground); padding: 16px; line-height: 1.45; overflow-wrap: anywhere; }
h1 { font-size: 1.3em; margin: 0 0 8px; }
.error { color: var(--vscode-errorForeground); }
button { font-family: inherit; font-size: inherit; background: var(--vscode-button-background); color: var(--vscode-button-foreground); border: 1px solid var(--vscode-button-border, transparent); border-radius: 2px; padding: 4px 10px; cursor: pointer; }
button:hover { background: var(--vscode-button-hoverBackground); }
button:focus-visible { outline: 1px solid var(--vscode-focusBorder); outline-offset: 2px; }
</style>
</head>
<body>
<h1>${escapeHtml(label)}</h1>
<p class="error" role="alert">Could not load this black box entry. ${escapeHtml(message)}</p>
<button data-action="refresh">Retry</button>
<script nonce="${nonce}">
const vscode = acquireVsCodeApi();
document.querySelector('button[data-action="refresh"]').addEventListener('click', () => vscode.postMessage({ action: 'refresh' }));
</script>
</body>
</html>`;
}

/** True when the entry is triaged ArmadaDefect (the "Suspected Armada defect" badge). */
export function suspectedDefect(entry: BlackBoxEntry): boolean {
  return (entry.record?.analysis?.triage?.verdict ?? entry.triage) === 'ArmadaDefect';
}

/** Proposed Armada changes, most impactful first. */
export function rankedChanges(resilience: BlackBoxResilience | null | undefined): BlackBoxProposedChange[] {
  return [...(resilience?.proposedChanges ?? [])].sort((a, b) => a.rank - b.rank);
}

/** The mission brief of a change (the server's MissionBrief, or one built from the title, change and test). */
export function changeBrief(change: BlackBoxProposedChange): string {
  if (change.missionBrief) return change.missionBrief;
  return [change.title, change.change, change.acceptanceTest ? `Acceptance test: ${change.acceptanceTest}` : ''].filter(Boolean).join('\n\n');
}

/**
 * The Armada resilience section: in handoff and landing scope the self-healable verdict and what Armada could have
 * done, the detection gap and invariant, severity and recurrence, recoveries already attempted, code pointers at the
 * build commit, proposed Armada changes with acceptance tests and the regression test; outside the scope the short
 * reason no Armada change is suggested.
 */
export function renderResilience(res: BlackBoxResilience, entry: BlackBoxEntry, record: Partial<BlackBoxRecord>): string {
  const changes = rankedChanges(res);
  if (!res.inScope && !changes.length) {
    return `<p class="muted">${escapeHtml(res.noChangeReason || 'No Armada change suggested: the failure is outside Armada\'s handoff and landing.')}</p>`;
  }
  const build = [res.armadaVersion ?? record.site?.armadaVersion ?? entry.armadaVersion, (res.armadaCommit ?? record.site?.armadaCommit ?? entry.armadaCommit) ? `commit ${res.armadaCommit ?? record.site?.armadaCommit ?? entry.armadaCommit}` : ''].filter(Boolean).join(' ');
  const occurrences = res.occurrences30Days ?? 1;
  const parts: string[] = [];
  if (res.inScope) parts.push('<p class="muted">Armada\'s handoff and landing could have been more resilient here: look for the Armada fix (with a test) first.</p>');
  parts.push(table([
    ['Scope', res.inScope ? `Handoff and landing${res.scopeReason ? ` (${res.scopeReason})` : ''}` : 'Outside handoff and landing'],
    ['Self-healable', res.selfHealable ?? 'Unknown'],
    ['Armada could have', res.selfHealingProposal ?? undefined],
    ['Detection gap', res.detectionGap ? `${res.detectionGapKind ? `${res.detectionGapKind}: ` : ''}${res.detectionGap}` : undefined],
    ['Invariant', res.invariant ?? undefined],
    ['Severity', res.severity ? `${res.severity} (${occurrences} in 30 days${(res.armadaVersionCount ?? 0) > 1 ? `, ${res.armadaVersionCount} Armada versions` : ''})` : undefined],
    ['Armada already attempted', (res.attemptedRecoveries ?? []).join('; ') || undefined],
    ['Regression test', res.regressionTest ? `${res.regressionTest.suite}: ${res.regressionTest.description}` : undefined],
  ]));
  const pointers = res.codePointers ?? [];
  if (pointers.length) parts.push(`<h3>Code pointers${build ? ` (Armada ${escapeHtml(build)})` : ''}</h3><ul>${pointers.map((p) => `<li><code>${escapeHtml(p)}</code></li>`).join('')}</ul>`);
  if (changes.length) {
    parts.push(`<h3>Proposed Armada changes</h3><ol>${changes.map((c) => `<li><strong>${escapeHtml(c.title)}</strong>${c.severity ? ` <span class="muted">(${escapeHtml(c.severity)})</span>` : ''}<br>${escapeHtml(c.change)}${c.acceptanceTest ? `<br><span class="muted">Acceptance test: ${escapeHtml(c.acceptanceTest)}</span>` : ''}</li>`).join('')}</ol>`);
  }
  return parts.join('');
}

/** Success rate and runbook link of a recovery path. */
export function recoveryExtras(p: BlackBoxRecoveryPath, dashboard: string): string {
  const parts: string[] = [];
  if (typeof p.successRate === 'number') parts.push(`<span class="muted">recovered ${escapeHtml(Math.round(p.successRate * 100))}% of ${escapeHtml(p.trackedRuns ?? 0)} tracked runs</span>`);
  if (p.runbookId) {
    const name = escapeHtml(p.runbookTitle || p.runbookId);
    parts.push(dashboard
      ? `Runbook: <a href="${escapeHtml(dashboardPageUrl(dashboard, `/runbooks/${encodeURIComponent(p.runbookId)}`))}">${name}</a>`
      : `Runbook: ${name} <code>${escapeHtml(p.runbookId)}</code>`);
  }
  return parts.length ? `<br>${parts.join(' ')}` : '';
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
  const allowed: EntryViewAction[] = ['copyFixThis', 'openFixThis', 'sendToChat', 'runRecovery', 'resolve', 'addNote', 'askArmada', 'timeline', 'compareAttempts', 'draftFix', 'refresh', 'openRoute'];
  if (typeof action !== 'string' || !allowed.includes(action as EntryViewAction)) return false;
  const route = (value as { route?: unknown }).route;
  if (action === 'openRoute' && (typeof route !== 'string' || !route.startsWith('/'))) return false;
  const pathId = (value as { pathId?: unknown }).pathId;
  return pathId === undefined || pathId === null || typeof pathId === 'string';
}

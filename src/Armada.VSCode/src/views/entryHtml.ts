import { Routes } from '../api/blackbox';
import { BlackBoxDoctorPage, BlackBoxEntry, BlackBoxNote, BlackBoxProposedChange, BlackBoxRecord, BlackBoxRecoveryPath, BlackBoxResilience } from '../api/types';
import { directoryPath, entryKind, entryLabel, filePath, formatDate, isOpen } from '../util/format';

/** Actions the entry view can post back to the extension. */
export type EntryViewAction = 'copyFixThis' | 'openFixThis' | 'sendToChat' | 'runRecovery' | 'resolve' | 'addNote' | 'askArmada' | 'timeline' | 'compareAttempts' | 'draftFix' | 'refresh';

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

  const resilience = analysis.Resilience ?? null;
  if (resilience) sections.push(section('Armada resilience', [renderResilience(resilience, entry, record)]));

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
    ? `<ol>${paths.map((p) => `<li><strong>${escapeHtml(p.Title)}</strong> <code>${escapeHtml(p.Id)}</code>${p.Risk ? ` <span class="muted">risk ${escapeHtml(p.Risk)}</span>` : ''}${p.DataLossRisk ? ' <span class="danger">data loss risk</span>' : ''}${p.ExpectedOutcome ? `<br>${escapeHtml(p.ExpectedOutcome)}` : ''}${recoveryExtras(p, dashboard)}${(p.Steps ?? []).length ? `<ul>${(p.Steps ?? []).map((s) => `<li>${escapeHtml(s.Description ?? s.CliCommand ?? s.McpCommand ?? s.RestCommand ?? '')}</li>`).join('')}</ul>` : ''}<br><button data-action="runRecovery" data-path="${escapeHtml(p.Id)}">Dry run ${escapeHtml(p.Id)}</button></li>`).join('')}</ol>`
    : '<p class="muted">No recovery paths recorded.</p>']));

  const attempts = record.Attempts ?? [];
  if (attempts.length) {
    sections.push(section('Attempts', [`<ul>${attempts.map((a) => `<li>Attempt ${escapeHtml(a.Attempt ?? '?')}: ${escapeHtml(a.Kind ?? '')} ${escapeHtml(a.ReasonExcerpt ?? '')}${a.Runtime ? ` <span class="muted">${escapeHtml(a.Runtime)}${a.Model ? ` ${escapeHtml(a.Model)}` : ''}</span>` : ''}</li>`).join('')}</ul>`]));
  }

  const doctor = options.doctor;
  if (doctor && doctor.Objects.length) {
    const rows = doctor.Objects.map((f) => {
      const example = dashboard
        ? `<a href="${escapeHtml(dashboard + Routes.dashboardEntry(f.ExampleEntryId))}">${escapeHtml(f.ExampleEntryId)}</a>`
        : `<code>${escapeHtml(f.ExampleEntryId)}</code>`;
      return `<li><strong>${escapeHtml(f.Setting)}</strong>: ${escapeHtml(f.CurrentValue ?? '?')} to ${escapeHtml(f.SuggestedValue ?? '?')}, recommended ${escapeHtml(f.Count)} time${f.Count === 1 ? '' : 's'}${f.RuleId ? ` by <code>${escapeHtml(f.RuleId)}</code>` : ''}${f.LastSeenUtc ? ` <span class="muted">last ${escapeHtml(formatDate(f.LastSeenUtc))}</span>` : ''}<br><span class="muted">Example ${example}${f.Applicable ? ' (can be applied from that entry)' : ''}</span></li>`;
    }).join('');
    const more = doctor.TotalRecords > doctor.Objects.length ? `<p class="muted">${escapeHtml(doctor.TotalRecords - doctor.Objects.length)} more recurring recommendations on this vessel.</p>` : '';
    sections.push(section(`Recurring on this vessel (last ${doctor.WindowDays ?? 30} days)`, [`<ul>${rows}</ul>`, more]));
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
.badge { font-size: 0.75em; padding: 1px 6px; border-radius: 8px; background: var(--vscode-inputValidation-errorBackground); color: var(--vscode-foreground); border: 1px solid var(--vscode-errorForeground); vertical-align: middle; }
.muted { color: var(--vscode-descriptionForeground); } .danger { color: var(--vscode-errorForeground); font-weight: 600; }
.toolbar { position: sticky; top: 0; background: var(--vscode-editor-background); padding: 8px 0; display: flex; flex-wrap: wrap; gap: 6px; }
button { background: var(--vscode-button-background); color: var(--vscode-button-foreground); border: none; padding: 4px 10px; cursor: pointer; }
button.secondary { background: var(--vscode-button-secondaryBackground); color: var(--vscode-button-secondaryForeground); }
</style>
</head>
<body>
<h1>${escapeHtml(entryLabel(entry))}${suspectedDefect(entry) ? ` <span class="badge">${escapeHtml(analysis.Triage?.Suspected ? 'Suspected Armada defect (prior only)' : 'Suspected Armada defect')}</span>` : ''}</h1>
<div class="toolbar">
<button data-action="copyFixThis">Copy FIX THIS</button>
<button data-action="openFixThis" class="secondary">Open FIX THIS in Editor</button>
<button data-action="sendToChat" class="secondary">Send FIX THIS to Chat</button>
<button data-action="runRecovery" class="secondary">Run Recovery Path...</button>
${open ? '<button data-action="resolve" class="secondary">Resolve</button>' : ''}
<button data-action="addNote" class="secondary">Add Note</button>
<button data-action="askArmada" class="secondary">Ask Armada about this failure</button>
${(resilience?.ProposedChanges ?? []).length ? '<button data-action="draftFix" class="secondary">Draft Armada Fix Mission</button>' : ''}
<button data-action="timeline" class="secondary">Timeline</button>
<button data-action="compareAttempts" class="secondary">Compare Attempts</button>
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

/** True when the entry is triaged ArmadaDefect (the "Suspected Armada defect" badge). */
export function suspectedDefect(entry: BlackBoxEntry): boolean {
  return (entry.Record?.Analysis?.Triage?.Verdict ?? entry.Triage) === 'ArmadaDefect';
}

/** Proposed Armada changes, most impactful first. */
export function rankedChanges(resilience: BlackBoxResilience | null | undefined): BlackBoxProposedChange[] {
  return [...(resilience?.ProposedChanges ?? [])].sort((a, b) => a.Rank - b.Rank);
}

/** The mission brief of a change (the server's MissionBrief, or one built from the title, change and test). */
export function changeBrief(change: BlackBoxProposedChange): string {
  if (change.MissionBrief) return change.MissionBrief;
  return [change.Title, change.Change, change.AcceptanceTest ? `Acceptance test: ${change.AcceptanceTest}` : ''].filter(Boolean).join('\n\n');
}

/**
 * The Armada resilience section: in handoff and landing scope the self-healable verdict and what Armada could have
 * done, the detection gap and invariant, severity and recurrence, recoveries already attempted, code pointers at the
 * build commit, proposed Armada changes with acceptance tests and the regression test; outside the scope the short
 * reason no Armada change is suggested.
 */
export function renderResilience(res: BlackBoxResilience, entry: BlackBoxEntry, record: BlackBoxRecord): string {
  const changes = rankedChanges(res);
  if (!res.InScope && !changes.length) {
    return `<p class="muted">${escapeHtml(res.NoChangeReason || 'No Armada change suggested: the failure is outside Armada\'s handoff and landing.')}</p>`;
  }
  const build = [res.ArmadaVersion ?? record.Site?.ArmadaVersion ?? entry.ArmadaVersion, (res.ArmadaCommit ?? record.Site?.ArmadaCommit ?? entry.ArmadaCommit) ? `commit ${res.ArmadaCommit ?? record.Site?.ArmadaCommit ?? entry.ArmadaCommit}` : ''].filter(Boolean).join(' ');
  const occurrences = res.Occurrences30Days ?? 1;
  const parts: string[] = [];
  if (res.InScope) parts.push('<p class="muted">Armada\'s handoff and landing could have been more resilient here: look for the Armada fix (with a test) first.</p>');
  parts.push(table([
    ['Scope', res.InScope ? `Handoff and landing${res.ScopeReason ? ` (${res.ScopeReason})` : ''}` : 'Outside handoff and landing'],
    ['Self-healable', res.SelfHealable ?? 'Unknown'],
    ['Armada could have', res.SelfHealingProposal ?? undefined],
    ['Detection gap', res.DetectionGap ? `${res.DetectionGapKind ? `${res.DetectionGapKind}: ` : ''}${res.DetectionGap}` : undefined],
    ['Invariant', res.Invariant ?? undefined],
    ['Severity', res.Severity ? `${res.Severity} (${occurrences} in 30 days${(res.ArmadaVersionCount ?? 0) > 1 ? `, ${res.ArmadaVersionCount} Armada versions` : ''})` : undefined],
    ['Armada already attempted', (res.AttemptedRecoveries ?? []).join('; ') || undefined],
    ['Regression test', res.RegressionTest ? `${res.RegressionTest.Suite}: ${res.RegressionTest.Description}` : undefined],
  ]));
  const pointers = res.CodePointers ?? [];
  if (pointers.length) parts.push(`<h3>Code pointers${build ? ` (Armada ${escapeHtml(build)})` : ''}</h3><ul>${pointers.map((p) => `<li><code>${escapeHtml(p)}</code></li>`).join('')}</ul>`);
  if (changes.length) {
    parts.push(`<h3>Proposed Armada changes</h3><ol>${changes.map((c) => `<li><strong>${escapeHtml(c.Title)}</strong>${c.Severity ? ` <span class="muted">(${escapeHtml(c.Severity)})</span>` : ''}<br>${escapeHtml(c.Change)}${c.AcceptanceTest ? `<br><span class="muted">Acceptance test: ${escapeHtml(c.AcceptanceTest)}</span>` : ''}</li>`).join('')}</ol>`);
  }
  return parts.join('');
}

/** Success rate and runbook link of a recovery path. */
export function recoveryExtras(p: BlackBoxRecoveryPath, dashboard: string): string {
  const parts: string[] = [];
  if (typeof p.SuccessRate === 'number') parts.push(`<span class="muted">recovered ${escapeHtml(Math.round(p.SuccessRate * 100))}% of ${escapeHtml(p.TrackedRuns ?? 0)} tracked runs</span>`);
  if (p.RunbookId) {
    const name = escapeHtml(p.RunbookTitle || p.RunbookId);
    parts.push(dashboard
      ? `Runbook: <a href="${escapeHtml(dashboard + Routes.dashboardRunbook(p.RunbookId))}">${name}</a>`
      : `Runbook: ${name} <code>${escapeHtml(p.RunbookId)}</code>`);
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
  const allowed: EntryViewAction[] = ['copyFixThis', 'openFixThis', 'sendToChat', 'runRecovery', 'resolve', 'addNote', 'askArmada', 'timeline', 'compareAttempts', 'draftFix', 'refresh'];
  if (typeof action !== 'string' || !allowed.includes(action as EntryViewAction)) return false;
  const pathId = (value as { pathId?: unknown }).pathId;
  return pathId === undefined || pathId === null || typeof pathId === 'string';
}

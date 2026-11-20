/*
 * Markdown renderings of the black box insight results (timeline, attempt diff, overlap preview, resilience gaps). Pure: no `vscode`
 * import, every server value escaped for Markdown tables, so they are unit tested directly.
 */

import type {
  BlackBoxAttemptDiff,
  BlackBoxEntrySummary,
  BlackBoxResilienceGap,
  BlackBoxTimelinePage,
  BriefLintFinding,
  MissionOverlapPreview,
  MissionOverlapWarning,
} from '@dashboard/types/blackbox';
import type { EnumerationResult } from '@dashboard/types/models';
import { entryLabel, formatDate } from '../../util/format';

/** Escapes a value for a Markdown table cell (pipes, newlines, HTML brackets and backticks). */
export function cell(value: unknown): string {
  const text = value === undefined || value === null || value === '' ? '-' : String(value);
  return text
    .replace(/\\/g, '\\\\')
    .replace(/\|/g, '\\|')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/`/g, '\\`')
    .replace(/\r?\n/g, ' ');
}

/** A Markdown code span (backticks inside are replaced, since a code span cannot escape them). */
export function code(value: unknown): string {
  return '`' + String(value ?? '').replace(/`/g, "'").replace(/\r?\n/g, ' ') + '`';
}

/** Escapes inline Markdown text outside tables. */
export function inline(value: unknown): string {
  return String(value ?? '').replace(/([\\`*_[\]<>#|])/g, '\\$1').replace(/\r?\n/g, ' ');
}

/** The combined timeline page as a Markdown table, with the page position and source counts. */
export function renderTimelineMarkdown(entry: BlackBoxEntrySummary, page: BlackBoxTimelinePage): string {
  const lines: string[] = [`# Black box timeline: ${inline(entryLabel(entry))}`, ''];
  lines.push(`Entry ${code(entry.id)}. Page ${page.pageNumber} of ${Math.max(1, page.totalPages)}, ${page.totalRecords} events in all.`);
  if (page.windowStartUtc || page.windowEndUtc) lines.push(`Window: ${inline(formatDate(page.windowStartUtc))} to ${inline(formatDate(page.windowEndUtc))}.`);
  if (page.truncated) lines.push('', '> Some sources were capped (200 items each, 1000 in all); older or newer events may be missing.');
  lines.push('');
  if (!page.objects.length) {
    lines.push('_No timeline events were recorded for this entry._');
  } else {
    lines.push('| Time | Source | Kind | Message | Id |', '|---|---|---|---|---|');
    for (const item of page.objects) {
      lines.push(`| ${cell(formatDate(item.timestampUtc))} | ${cell(item.source)} | ${cell(item.kind)} | ${cell(item.message)} | ${cell(item.entityId)} |`);
    }
  }
  const sources = page.sources ?? [];
  if (sources.length) {
    lines.push('', '## Sources', '', '| Source | Events | Capped |', '|---|---|---|');
    for (const s of sources) lines.push(`| ${cell(s.source)} | ${cell(s.count)} | ${s.truncated ? 'yes' : 'no'} |`);
  }
  return lines.join('\n') + '\n';
}

/** One page of Armada resilience gaps as a Markdown table, most severe first, with the top proposed change of each. */
export function renderGapsMarkdown(page: EnumerationResult<BlackBoxResilienceGap>): string {
  const lines: string[] = ['# Armada resilience gaps', ''];
  lines.push('Failure modes in Armada\'s own handoff and landing that the black box triaged as Armada defects, grouped by fingerprint.');
  lines.push(`Page ${page.pageNumber} of ${Math.max(1, page.totalPages)}, ${page.totalRecords} failure modes in all.`, '');
  if (!page.objects.length) {
    lines.push('_No Armada resilience gaps were recorded._');
    return lines.join('\n') + '\n';
  }
  lines.push('| Severity | Where | Occurrences | Self-healable | Top proposed change | Versions | Newest entry |', '|---|---|---|---|---|---|---|');
  for (const g of page.objects) {
    const where = `${g.phase ?? '?'} / ${g.step ?? '?'}${g.failureKind ? ` (${g.failureKind})` : ''}`;
    const healable = `${g.selfHealable ?? 'Unknown'}${g.suspected ? ' (suspected)' : ''}`;
    lines.push(`| ${cell(g.severity)} | ${cell(where)} | ${cell(`${g.occurrences} (${g.openCount ?? 0} open)`)} | ${cell(healable)} | ${cell(g.topProposedChange?.title)} | ${cell((g.armadaVersions ?? []).join(', '))} | ${cell(g.latestEntryId)} |`);
  }
  const details = page.objects.filter((g) => g.topProposedChange || g.detectionGap);
  if (details.length) {
    lines.push('', '## Proposed Armada changes');
    for (const g of details) {
      lines.push('', `### ${inline(g.topProposedChange?.title ?? g.fingerprint)}`, '');
      lines.push(`Fingerprint ${code(g.fingerprint)}, newest entry ${code(g.latestEntryId ?? '?')}${g.latestMissionId ? ` (mission ${code(g.latestMissionId)})` : ''}.`);
      if (g.detectionGap) lines.push('', `Detection gap: ${inline(g.detectionGap)}`);
      if (g.topProposedChange) {
        lines.push('', inline(g.topProposedChange.change));
        if (g.topProposedChange.acceptanceTest) lines.push('', `Acceptance test: ${inline(g.topProposedChange.acceptanceTest)}`);
      }
    }
  }
  return lines.join('\n') + '\n';
}

/** The attempt diff as a side-by-side Markdown table plus the files only each side changed. */
export function renderDiffMarkdown(diff: BlackBoxAttemptDiff, left: string, right: string): string {
  const leftHead = `${left}${diff.attempt ? ` (attempt ${diff.attempt})` : ''}`;
  const rightHead = `${right}${diff.otherAttempt ? ` (attempt ${diff.otherAttempt})` : ''}`;
  const lines: string[] = [`# Black box attempt diff`, ''];
  lines.push(`${code(diff.entryId)} compared with ${code(diff.otherEntryId)}.`);
  lines.push(`${diff.sameMission ? 'Same mission' : 'Different missions'}; ${diff.sameFingerprint ? 'same failure fingerprint' : 'different failure fingerprints'}.`, '');
  const diffs = diff.differences ?? [];
  if (!diffs.length) {
    lines.push('_No recorded values differ between the two entries._');
  } else {
    lines.push(`## Differences (${diff.differenceCount ?? diffs.length})`, '');
    lines.push(`| Section | Field | ${cell(leftHead)} | ${cell(rightHead)} |`, '|---|---|---|---|');
    for (const d of diffs) lines.push(`| ${cell(d.section)} | ${cell(d.field)} | ${cell(d.value)} | ${cell(d.otherValue)} |`);
    if ((diff.differenceCount ?? diffs.length) > diffs.length) lines.push('', `_${(diff.differenceCount ?? 0) - diffs.length} more differences are not shown._`);
  }
  lines.push('', '## Changed files', '');
  if (diff.changedFilesKnown === false) {
    lines.push('_The changed files are not known for one or both entries._');
  } else {
    lines.push(`${diff.filesInBothCount ?? 0} files changed in both.`, '');
    lines.push(...fileList(`Only in ${leftHead}`, diff.filesOnlyInEntry ?? [], diff.filesOnlyInEntryCount));
    lines.push(...fileList(`Only in ${rightHead}`, diff.filesOnlyInOther ?? [], diff.filesOnlyInOtherCount));
  }
  return lines.join('\n') + '\n';
}

/** The overlap preview and brief lint findings for a mission brief. */
export function renderOverlapMarkdown(preview: MissionOverlapPreview, vesselLabel: string, title: string): string {
  const warnings = preview.warnings ?? [];
  const lint = preview.briefLint?.findings ?? [];
  const lines: string[] = [`# Mission overlap preview: ${inline(vesselLabel)}`, ''];
  if (title) lines.push(`Mission: ${inline(title)}`, '');
  lines.push(`Compared with ${preview.activeMissionsCompared ?? 0} active missions${preview.activeMissionsTruncated ? ' (the newest 200 only)' : ''}. ${preview.likelyFileCount ?? 0} likely files (${preview.scopedFileCount ?? 0} scoped, ${preview.briefPathCount ?? 0} named in the brief).`);
  if (preview.trackedFilesKnown === false) lines.push('', '> The vessel has no local repository on the Admiral, so brief paths could not be checked.');
  if (preview.serializeOverlappingMissions) lines.push('', '> This vessel serializes overlapping missions: dispatch will chain the mission after the overlapping one.');
  if (preview.suggestedDependsOnMissionId) lines.push('', `Suggested dependency: ${code(preview.suggestedDependsOnMissionId)}.`);
  lines.push('', `## Overlap warnings (${preview.warningCount ?? warnings.length})`, '');
  if (!warnings.length) lines.push('_No overlaps with active missions, reservations or hotspots._');
  else for (const w of warnings) lines.push(`- ${overlapLine(w)}`);
  lines.push('', `## Brief lint warnings (${preview.briefLint?.findingCount ?? lint.length})`, '');
  if (!lint.length) lines.push(preview.briefLint ? '_The brief is clean._' : '_The server returned no lint result._');
  else for (const f of lint) lines.push(`- ${lintLine(f)}`);
  const likely = preview.likelyFiles ?? [];
  if (likely.length) {
    lines.push('', '## Likely files', '');
    for (const f of likely) lines.push(`- ${code(f)}`);
  }
  return lines.join('\n') + '\n';
}

/** One overlap warning as one line. */
export function overlapLine(w: MissionOverlapWarning): string {
  const parts: string[] = [`**${inline(w.kind)}**: ${inline(w.message)}`];
  if (w.overlappingMissionId) parts.push(`(${code(w.overlappingMissionId)}${w.overlappingMissionStatus ? ` ${inline(w.overlappingMissionStatus)}` : ''})`);
  if (w.directory) parts.push(`in ${code(w.directory)}`);
  const files = w.files ?? [];
  if (files.length) parts.push(`files: ${files.slice(0, 10).map((f) => `${code(f)}`).join(', ')}${(w.fileCount ?? files.length) > Math.min(10, files.length) ? ` and ${(w.fileCount ?? files.length) - Math.min(10, files.length)} more` : ''}`);
  if (w.serialized) parts.push('(chained after it)');
  return parts.join(' ');
}

/** One brief lint finding as one line. */
export function lintLine(f: BriefLintFinding): string {
  const parts: string[] = [`**${inline(f.ruleId)}**: ${inline(f.message)}`];
  if (f.excerpt) parts.push(`("${inline(f.excerpt)}")`);
  if (f.suggestion) parts.push(`Suggestion: ${inline(f.suggestion)}`);
  return parts.join(' ');
}

function fileList(heading: string, files: string[], total: number | undefined): string[] {
  const count = total ?? files.length;
  const lines = [`### ${inline(heading)} (${count})`, ''];
  if (!files.length) lines.push('_None._');
  else for (const f of files) lines.push(`- ${code(f)}`);
  if (count > files.length) lines.push(`- _and ${count - files.length} more_`);
  lines.push('');
  return lines;
}

/*
 * Markdown renderings of the black box insight results (timeline, attempt diff, overlap preview, resilience gaps). Pure: no `vscode`
 * import, every server value escaped for Markdown tables, so they are unit tested directly.
 */

import {
  BlackBoxAttemptDiff,
  BlackBoxEntrySummary,
  BlackBoxResilienceGap,
  BlackBoxTimelinePage,
  EnumerationResult,
  BriefLintFinding,
  MissionOverlapPreview,
  MissionOverlapWarning,
} from '../api/types';
import { entryLabel, formatDate } from '../util/format';

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
  lines.push(`Entry ${code(entry.Id)}. Page ${page.PageNumber} of ${Math.max(1, page.TotalPages)}, ${page.TotalRecords} events in all.`);
  if (page.WindowStartUtc || page.WindowEndUtc) lines.push(`Window: ${inline(formatDate(page.WindowStartUtc))} to ${inline(formatDate(page.WindowEndUtc))}.`);
  if (page.Truncated) lines.push('', '> Some sources were capped (200 items each, 1000 in all); older or newer events may be missing.');
  lines.push('');
  if (!page.Objects.length) {
    lines.push('_No timeline events were recorded for this entry._');
  } else {
    lines.push('| Time | Source | Kind | Message | Id |', '|---|---|---|---|---|');
    for (const item of page.Objects) {
      lines.push(`| ${cell(formatDate(item.TimestampUtc))} | ${cell(item.Source)} | ${cell(item.Kind)} | ${cell(item.Message)} | ${cell(item.EntityId)} |`);
    }
  }
  const sources = page.Sources ?? [];
  if (sources.length) {
    lines.push('', '## Sources', '', '| Source | Events | Capped |', '|---|---|---|');
    for (const s of sources) lines.push(`| ${cell(s.Source)} | ${cell(s.Count)} | ${s.Truncated ? 'yes' : 'no'} |`);
  }
  return lines.join('\n') + '\n';
}

/** One page of Armada resilience gaps as a Markdown table, most severe first, with the top proposed change of each. */
export function renderGapsMarkdown(page: EnumerationResult<BlackBoxResilienceGap>): string {
  const lines: string[] = ['# Armada resilience gaps', ''];
  lines.push('Failure modes in Armada\'s own handoff and landing that the black box triaged as Armada defects, grouped by fingerprint.');
  lines.push(`Page ${page.PageNumber} of ${Math.max(1, page.TotalPages)}, ${page.TotalRecords} failure modes in all.`, '');
  if (!page.Objects.length) {
    lines.push('_No Armada resilience gaps were recorded._');
    return lines.join('\n') + '\n';
  }
  lines.push('| Severity | Where | Occurrences | Self-healable | Top proposed change | Versions | Newest entry |', '|---|---|---|---|---|---|---|');
  for (const g of page.Objects) {
    const where = `${g.Phase ?? '?'} / ${g.Step ?? '?'}${g.FailureKind ? ` (${g.FailureKind})` : ''}`;
    const healable = `${g.SelfHealable ?? 'Unknown'}${g.Suspected ? ' (suspected)' : ''}`;
    lines.push(`| ${cell(g.Severity)} | ${cell(where)} | ${cell(`${g.Occurrences} (${g.OpenCount ?? 0} open)`)} | ${cell(healable)} | ${cell(g.TopProposedChange?.Title)} | ${cell((g.ArmadaVersions ?? []).join(', '))} | ${cell(g.LatestEntryId)} |`);
  }
  const details = page.Objects.filter((g) => g.TopProposedChange || g.DetectionGap);
  if (details.length) {
    lines.push('', '## Proposed Armada changes');
    for (const g of details) {
      lines.push('', `### ${inline(g.TopProposedChange?.Title ?? g.Fingerprint)}`, '');
      lines.push(`Fingerprint ${code(g.Fingerprint)}, newest entry ${code(g.LatestEntryId ?? '?')}${g.LatestMissionId ? ` (mission ${code(g.LatestMissionId)})` : ''}.`);
      if (g.DetectionGap) lines.push('', `Detection gap: ${inline(g.DetectionGap)}`);
      if (g.TopProposedChange) {
        lines.push('', inline(g.TopProposedChange.Change));
        if (g.TopProposedChange.AcceptanceTest) lines.push('', `Acceptance test: ${inline(g.TopProposedChange.AcceptanceTest)}`);
      }
    }
  }
  return lines.join('\n') + '\n';
}

/** The attempt diff as a side-by-side Markdown table plus the files only each side changed. */
export function renderDiffMarkdown(diff: BlackBoxAttemptDiff, left: string, right: string): string {
  const leftHead = `${left}${diff.Attempt ? ` (attempt ${diff.Attempt})` : ''}`;
  const rightHead = `${right}${diff.OtherAttempt ? ` (attempt ${diff.OtherAttempt})` : ''}`;
  const lines: string[] = [`# Black box attempt diff`, ''];
  lines.push(`${code(diff.EntryId)} compared with ${code(diff.OtherEntryId)}.`);
  lines.push(`${diff.SameMission ? 'Same mission' : 'Different missions'}; ${diff.SameFingerprint ? 'same failure fingerprint' : 'different failure fingerprints'}.`, '');
  const diffs = diff.Differences ?? [];
  if (!diffs.length) {
    lines.push('_No recorded values differ between the two entries._');
  } else {
    lines.push(`## Differences (${diff.DifferenceCount ?? diffs.length})`, '');
    lines.push(`| Section | Field | ${cell(leftHead)} | ${cell(rightHead)} |`, '|---|---|---|---|');
    for (const d of diffs) lines.push(`| ${cell(d.Section)} | ${cell(d.Field)} | ${cell(d.Value)} | ${cell(d.OtherValue)} |`);
    if ((diff.DifferenceCount ?? diffs.length) > diffs.length) lines.push('', `_${(diff.DifferenceCount ?? 0) - diffs.length} more differences are not shown._`);
  }
  lines.push('', '## Changed files', '');
  if (diff.ChangedFilesKnown === false) {
    lines.push('_The changed files are not known for one or both entries._');
  } else {
    lines.push(`${diff.FilesInBothCount ?? 0} files changed in both.`, '');
    lines.push(...fileList(`Only in ${leftHead}`, diff.FilesOnlyInEntry ?? [], diff.FilesOnlyInEntryCount));
    lines.push(...fileList(`Only in ${rightHead}`, diff.FilesOnlyInOther ?? [], diff.FilesOnlyInOtherCount));
  }
  return lines.join('\n') + '\n';
}

/** The overlap preview and brief lint findings for a mission brief. */
export function renderOverlapMarkdown(preview: MissionOverlapPreview, vesselLabel: string, title: string): string {
  const warnings = preview.Warnings ?? [];
  const lint = preview.BriefLint?.Findings ?? [];
  const lines: string[] = [`# Mission overlap preview: ${inline(vesselLabel)}`, ''];
  if (title) lines.push(`Mission: ${inline(title)}`, '');
  lines.push(`Compared with ${preview.ActiveMissionsCompared ?? 0} active missions${preview.ActiveMissionsTruncated ? ' (the newest 200 only)' : ''}. ${preview.LikelyFileCount ?? 0} likely files (${preview.ScopedFileCount ?? 0} scoped, ${preview.BriefPathCount ?? 0} named in the brief).`);
  if (preview.TrackedFilesKnown === false) lines.push('', '> The vessel has no local repository on the Admiral, so brief paths could not be checked.');
  if (preview.SerializeOverlappingMissions) lines.push('', '> This vessel serializes overlapping missions: dispatch will chain the mission after the overlapping one.');
  if (preview.SuggestedDependsOnMissionId) lines.push('', `Suggested dependency: ${code(preview.SuggestedDependsOnMissionId)}.`);
  lines.push('', `## Overlap warnings (${preview.WarningCount ?? warnings.length})`, '');
  if (!warnings.length) lines.push('_No overlaps with active missions, reservations or hotspots._');
  else for (const w of warnings) lines.push(`- ${overlapLine(w)}`);
  lines.push('', `## Brief lint warnings (${preview.BriefLint?.FindingCount ?? lint.length})`, '');
  if (!lint.length) lines.push(preview.BriefLint ? '_The brief is clean._' : '_The server returned no lint result._');
  else for (const f of lint) lines.push(`- ${lintLine(f)}`);
  const likely = preview.LikelyFiles ?? [];
  if (likely.length) {
    lines.push('', '## Likely files', '');
    for (const f of likely) lines.push(`- ${code(f)}`);
  }
  return lines.join('\n') + '\n';
}

/** One overlap warning as one line. */
export function overlapLine(w: MissionOverlapWarning): string {
  const parts: string[] = [`**${inline(w.Kind)}**: ${inline(w.Message)}`];
  if (w.OverlappingMissionId) parts.push(`(${code(w.OverlappingMissionId)}${w.OverlappingMissionStatus ? ` ${inline(w.OverlappingMissionStatus)}` : ''})`);
  if (w.Directory) parts.push(`in ${code(w.Directory)}`);
  const files = w.Files ?? [];
  if (files.length) parts.push(`files: ${files.slice(0, 10).map((f) => `${code(f)}`).join(', ')}${(w.FileCount ?? files.length) > Math.min(10, files.length) ? ` and ${(w.FileCount ?? files.length) - Math.min(10, files.length)} more` : ''}`);
  if (w.Serialized) parts.push('(chained after it)');
  return parts.join(' ');
}

/** One brief lint finding as one line. */
export function lintLine(f: BriefLintFinding): string {
  const parts: string[] = [`**${inline(f.RuleId)}**: ${inline(f.Message)}`];
  if (f.Excerpt) parts.push(`("${inline(f.Excerpt)}")`);
  if (f.Suggestion) parts.push(`Suggestion: ${inline(f.Suggestion)}`);
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

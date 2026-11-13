import { enumerateFleetActionRunTargets, getFleetActionRunTarget } from '../api/client';
import type { FleetActionRun, FleetActionRunTargetSummary, FleetActionTargetStatus } from '../types/models';
import { MISSION_MODE_LABELS, TARGET_STATUS_META, reasonLabel, type Translate } from './fleetActionLabels';

/** Targets per page in the run Report view. */
export const REPORT_PAGE_SIZE = 10;

/** Targets per request when downloading the whole report. */
export const REPORT_DOWNLOAD_PAGE_SIZE = 50;

/** One vessel's section of a run report. */
export interface ReportEntry {
  id: string;
  vesselId: string;
  vesselName: string;
  status: FleetActionTargetStatus;
  skipReason: string | null;
  failureReason: string | null;
  outputText: string | null;
  outputTruncated: boolean;
}

/** One page of report entries and the run's total target count. */
export interface ReportPage {
  entries: ReportEntry[];
  totalRecords: number;
  totalPages: number;
}

async function toEntries(runId: string, rows: FleetActionRunTargetSummary[]): Promise<ReportEntry[]> {
  return await Promise.all(rows.map(async (row) => {
    let outputText = row.outputText;
    // A server that ignores IncludeOutput sends summaries only; read those targets one at a time instead.
    if (outputText === undefined && row.outputLength > 0) {
      try {
        outputText = (await getFleetActionRunTarget(runId, row.id)).outputText;
      } catch {
        outputText = null;
      }
    }
    return {
      id: row.id,
      vesselId: row.vesselId,
      vesselName: row.vesselName,
      status: row.status,
      skipReason: row.skipReason,
      failureReason: row.failureReason,
      outputText: outputText ?? null,
      outputTruncated: row.outputTruncated,
    };
  }));
}

/** Read one page of a run's targets with their output text (the captain's final report for Mission runs). */
export async function fetchReportPage(runId: string, pageNumber: number, pageSize = REPORT_PAGE_SIZE): Promise<ReportPage> {
  const page = await enumerateFleetActionRunTargets(runId, { pageNumber, pageSize, includeOutput: true });
  return {
    entries: await toEntries(runId, page.objects || []),
    totalRecords: page.totalRecords || 0,
    totalPages: Math.max(1, page.totalPages || 1),
  };
}

/** Read every target of a run with output, paging until the run's target count (or the last page) is reached. */
export async function fetchAllReportEntries(runId: string, targetCount: number, pageSize = REPORT_DOWNLOAD_PAGE_SIZE): Promise<ReportEntry[]> {
  const entries: ReportEntry[] = [];
  const maxPages = Math.max(1, Math.ceil(Math.max(0, targetCount) / pageSize));
  for (let pageNumber = 1; pageNumber <= maxPages; pageNumber++) {
    const page = await fetchReportPage(runId, pageNumber, pageSize);
    entries.push(...page.entries);
    if (page.entries.length === 0 || pageNumber >= page.totalPages || entries.length >= targetCount) break;
  }
  return targetCount > 0 ? entries.slice(0, targetCount) : entries;
}

/** One Markdown document holding every vessel's report, with vessel name and status headings. */
export function buildRunReportMarkdown(t: Translate, run: Pick<FleetActionRun, 'id' | 'actionName' | 'kind' | 'missionMode' | 'targetCount' | 'createdUtc'>, entries: ReportEntry[]): string {
  const lines: string[] = [`# ${run.actionName}`, ''];
  lines.push(`- ${t('Run')}: ${run.id}`);
  if (run.kind === 'Mission' && run.missionMode) lines.push(`- ${t('Mission mode')}: ${t(MISSION_MODE_LABELS[run.missionMode])}`);
  lines.push(`- ${t('Targets')}: ${run.targetCount}`);
  lines.push(`- ${t('Created')}: ${run.createdUtc}`);
  for (const entry of entries) {
    const status = t(TARGET_STATUS_META[entry.status]?.label ?? entry.status);
    lines.push('', `## ${entry.vesselName} (${status})`, '');
    if (entry.outputText && entry.outputText.trim()) {
      if (entry.outputTruncated) lines.push(`_${t('Only the end of this report was kept.')}_`, '');
      lines.push(entry.outputText.trim());
    } else {
      const reason = reasonLabel(t, entry.skipReason, entry.failureReason);
      lines.push(`_${reason || t('No report.')}_`);
    }
  }
  return `${lines.join('\n')}\n`;
}

/** File name for a downloaded run report. */
export function reportFileName(runId: string): string {
  return `fleet-action-report-${runId.replace(/[^A-Za-z0-9_-]/g, '_')}.md`;
}

import { BlackBoxDirectoryCount, BlackBoxEntrySummary } from '../api/types';

/** Short relative age ("just now", "5m", "3h", "2d") of an ISO timestamp, relative to now. */
export function formatAge(iso: string | null | undefined, now: Date = new Date()): string {
  if (!iso) return '';
  const time = Date.parse(iso);
  if (Number.isNaN(time)) return '';
  const seconds = Math.max(0, Math.floor((now.getTime() - time) / 1000));
  if (seconds < 60) return 'just now';
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return `${minutes}m ago`;
  const hours = Math.floor(minutes / 60);
  if (hours < 48) return `${hours}h ago`;
  return `${Math.floor(hours / 24)}d ago`;
}

/** The date part (YYYY-MM-DD, UTC) of an ISO timestamp, or the input when it does not parse. */
export function formatDate(iso: string | null | undefined): string {
  if (!iso) return '';
  const time = Date.parse(iso);
  if (Number.isNaN(time)) return iso;
  return new Date(time).toISOString().slice(0, 10);
}

/** The failure kind to show for an entry. */
export function entryKind(entry: BlackBoxEntrySummary): string {
  return entry.FailureKind || entry.Phase || 'Failure';
}

/** The subject id an entry is about (mission id, else voyage id, else the entry id). */
export function entrySubjectId(entry: BlackBoxEntrySummary): string {
  return entry.MissionId || entry.VoyageId || entry.Id;
}

/** A readable label for an entry. */
export function entryLabel(entry: BlackBoxEntrySummary): string {
  const subject = entrySubjectId(entry);
  return entry.MissionTitle ? `${entry.MissionTitle} (${subject})` : subject;
}

/** True when the entry is still open (no resolution, or Open). */
export function isOpen(entry: BlackBoxEntrySummary): boolean {
  return !entry.Resolution || entry.Resolution === 'Open';
}

/** Normalizes a repository-relative file path from a record section. */
export function filePath(ref: string | null | undefined): string | null {
  if (!ref) return null;
  const value = normalizeRelPath(ref);
  return value || null;
}

/** Normalizes a directory count row. */
export function directoryPath(dir: BlackBoxDirectoryCount | string | null | undefined): string | null {
  if (!dir) return null;
  if (typeof dir === 'string') return normalizeRelPath(dir);
  return dir.Directory ? normalizeRelPath(dir.Directory) : null;
}

/** Repository-relative path with forward slashes and no leading `./` or `/`. */
export function normalizeRelPath(value: string): string {
  return value.replace(/\\/g, '/').replace(/^\.\//, '').replace(/^\/+/, '').replace(/\/+$/, '');
}

/**
 * Incremental transcript loading for planning and refinement sessions. A session detail carries `messageCount`, not
 * the transcript; the transcript is read one page at a time from its paged enumeration, newest page first, and older
 * pages load on request. Messages are kept oldest first and merged by id, keeping the newer copy (the WebSocket can
 * deliver a message before or after the page that also holds it).
 */
import type { EnumerationResult, PagedQuery } from '../types/models';
import { upsertIfNewer } from './liveMerge';

/** Transcript messages read per page. */
export const TRANSCRIPT_PAGE_SIZE = 100;

interface TranscriptMessage {
  id: string;
  sequence: number;
  lastUpdateUtc: string;
}

/** Reads one transcript page. */
export type TranscriptPageReader<M> = (query: PagedQuery) => Promise<EnumerationResult<M> | null | undefined>;

/** Merge messages into a transcript by id (newer copy wins), oldest first. */
export function mergeTranscript<M extends TranscriptMessage>(current: M[], incoming: M[]): M[] {
  let merged = [...current];
  for (const message of incoming) merged = upsertIfNewer(merged, message);
  return merged.sort((a, b) => a.sequence - b.sequence);
}

/** The newest page of a transcript, oldest first, and the transcript's total length. */
export async function loadNewestTranscript<M extends TranscriptMessage>(read: TranscriptPageReader<M>): Promise<{ messages: M[]; total: number }> {
  const page = await read({ pageNumber: 1, pageSize: TRANSCRIPT_PAGE_SIZE, order: 'CreatedDescending' });
  const messages = mergeTranscript<M>([], page?.objects ?? []);
  return { messages, total: Math.max(page?.totalRecords ?? 0, messages.length) };
}

/**
 * The next older page for a transcript that has `loaded` messages, merged in, and the total. Pages count back from
 * the newest; a message that arrived since the first page shifts the window by one, which the id merge absorbs.
 */
export async function loadOlderTranscript<M extends TranscriptMessage>(read: TranscriptPageReader<M>, current: M[]): Promise<{ messages: M[]; total: number }> {
  const pageNumber = Math.floor(current.length / TRANSCRIPT_PAGE_SIZE) + 1;
  const page = await read({ pageNumber, pageSize: TRANSCRIPT_PAGE_SIZE, order: 'CreatedDescending' });
  const messages = mergeTranscript(current, page?.objects ?? []);
  return { messages, total: Math.max(page?.totalRecords ?? 0, messages.length) };
}

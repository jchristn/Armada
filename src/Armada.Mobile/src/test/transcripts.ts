/**
 * Serves planning and refinement transcripts from the session details a test configured. The app reads a session's
 * detail and its newest transcript page together (lib/sessionViews); the detail carries counts only, so tests that
 * describe a session by its detail (with messages) get the transcript page from that same detail: the page reader
 * awaits the detail call made alongside it.
 */
import type { EnumerationResult } from '@dashboard/types/models';

interface Sequenced {
  sequence: number;
}

function page<T>(objects: T[]): EnumerationResult<T> {
  return { success: true, pageNumber: 1, pageSize: Math.max(objects.length, 1), totalPages: objects.length > 0 ? 1 : 0, totalRecords: objects.length, objects, totalMs: 1 };
}

function fromDetail(detailMock: jest.Mock) {
  return async () => {
    const results = detailMock.mock.results;
    const latest = results.length > 0 ? await (results[results.length - 1].value as Promise<{ messages?: Sequenced[] } | undefined>) : undefined;
    const messages = [...(latest?.messages ?? [])].sort((a, b) => b.sequence - a.sequence);
    return page(messages);
  };
}

/** Route the transcript page readers to the configured session details. */
export function serveTranscriptsFromDetails(api: Record<string, jest.Mock>): void {
  api.listPlanningSessionMessages.mockImplementation(fromDetail(api.getPlanningSession));
  api.listObjectiveRefinementMessages.mockImplementation(fromDetail(api.getObjectiveRefinementSession));
}

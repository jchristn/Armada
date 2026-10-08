import { act, fireEvent, screen, waitFor, within } from '@testing-library/react-native';
import { Text } from 'react-native';
import * as client from '@dashboard/api/client';
import type { HistoricalTimelineEntry, Job, RequestHistoryEntry, RequestHistoryRecord, TokenUsageSummaryResult } from '@dashboard/types/models';
import { buildHistoryCsv, buildHistoryMarkdown, buildHistoryTimelineQuery, escapeCsvValue } from '@dashboard/lib/historyExport';
import {
  buildRequestHistoryDeleteQuery, buildRequestHistoryQuery, buildRequestHistorySummaryQuery, defaultRequestHistoryFilters,
  getActivityRangeWindow, hasActiveRequestFilters, normalizeSummaryBuckets,
} from '@dashboard/lib/requestHistory';
import { computeYTicks, formatTokens } from '@dashboard/lib/tokenUsage';
import { isJobTerminal } from '@dashboard/lib/jobs';
import MoreLayout from '../app/(app)/(more)/_layout';
import ActivityRoute from '../app/(app)/(more)/activity';
import JobsRoute from '../app/(app)/(more)/jobs';
import RequestRoute from '../app/(app)/(more)/requests/[id]';
import { replayHref } from '../screens/activity/RequestDetail';
import { usageSeries } from '../screens/activity/TokenUsageTab';
import { emit, page, renderW4Routes, resetW4 } from '../test/w4';
import { rowActionTarget } from '../test/a11y';

jest.mock('@dashboard/api/client', () => require('../test/w4Client').autoMockClient());
jest.mock('expo-sharing', () => ({ isAvailableAsync: jest.fn(async () => true), shareAsync: jest.fn(async () => undefined) }));
jest.mock('expo-file-system', () => {
  const written: Record<string, string> = {};
  class File {
    uri: string;
    exists = false;
    constructor(_dir: unknown, name: string) { this.uri = `file:///cache/${name}`; }
    create() { /* in memory */ }
    delete() { /* in memory */ }
    write(content: string) { written[this.uri] = content; }
  }
  return { File, Paths: { cache: {} }, __written: written };
});

const mockWindow = { width: 390, height: 844 };
jest.mock('react-native/Libraries/Utilities/useWindowDimensions', () => ({
  __esModule: true,
  default: () => ({ width: mockWindow.width, height: mockWindow.height, scale: 2, fontScale: 1 }),
}));

const api = client as jest.Mocked<typeof client>;

function hist(over: Partial<HistoricalTimelineEntry> = {}): HistoricalTimelineEntry {
  return {
    id: 'hst_1', sourceType: 'Mission', sourceId: 'msn_1', entityType: 'mission', entityId: 'msn_1', objectiveId: null, vesselId: 'vsl_1',
    environmentId: null, deploymentId: null, incidentId: null, missionId: 'msn_1', voyageId: null, actorId: null, actorDisplay: 'admin@armada',
    title: 'Mission completed', description: 'Fix login', status: 'Complete', severity: 'Success', route: '/missions/msn_1',
    occurredUtc: '2026-10-07T10:00:00Z', metadataJson: '{"a":1}', ...over,
  };
}

function req(over: Partial<RequestHistoryEntry> = {}): RequestHistoryEntry {
  return {
    id: 'req_1', tenantId: 'ten_1', userId: 'usr_1', credentialId: null, principalDisplay: 'admin@armada', authMethod: 'Bearer', method: 'POST',
    route: '/api/v1/missions', routeTemplate: '/api/v1/missions', queryString: null, statusCode: 201, durationMs: 12.5, requestSizeBytes: 100,
    responseSizeBytes: 2048, requestContentType: 'application/json', responseContentType: 'application/json', isSuccess: true, clientIp: null,
    correlationId: null, createdUtc: '2026-10-07T10:00:00Z', ...over,
  };
}

const RECORD: RequestHistoryRecord = {
  entry: req(),
  detail: {
    requestHistoryId: 'req_1', pathParamsJson: null, queryParamsJson: '{"q":"1"}', requestHeadersJson: '{"X-Token":"[redacted]"}', responseHeadersJson: null,
    requestBodyText: '{"title":"x"}', responseBodyText: '{"id":"msn_1"}', requestBodyTruncated: true, responseBodyTruncated: false,
  },
};

const SUMMARY = { totalCount: 4, successCount: 3, failureCount: 1, successRate: 75, averageDurationMs: 10, fromUtc: null, toUtc: null, bucketMinutes: 15, buckets: [] };

const ROUTES = {
  '(more)/_layout': MoreLayout,
  '(more)/more': () => null,
  '(more)/activity': ActivityRoute,
  '(more)/requests/[id]': RequestRoute,
  '(more)/jobs': JobsRoute,
  '(more)/events/[id]': () => <Text>Event screen</Text>,
  '(more)/api-explorer/index': () => <Text>API Explorer</Text>,
  '(work)/missions/[id]': () => <Text>Mission screen</Text>,
};

beforeEach(async () => {
  await resetW4();
  jest.clearAllMocks();
  mockWindow.width = 390;
  api.listVessels.mockResolvedValue(page([{ id: 'vsl_1', name: 'armada' }]) as never);
  api.listObjectives.mockResolvedValue(page([]) as never);
  api.listCaptains.mockResolvedValue(page([]) as never);
  api.listUsers.mockResolvedValue(page([]) as never);
  api.enumerateHistoryTimeline.mockResolvedValue(page([hist(), hist({ id: 'hst_2', sourceType: 'Request', sourceId: 'req_9', title: 'POST /api/v1/x', severity: 'Error', status: '500', route: null, metadataJson: null })]) as never);
  api.listRequestHistory.mockResolvedValue(page([req(), req({ id: 'req_2', method: 'DELETE', statusCode: 404, isSuccess: false })], { totalPages: 1 }) as never);
  api.getRequestHistorySummary.mockResolvedValue(SUMMARY as never);
});

describe('shared activity logic', () => {
  it('builds the History timeline query from the filters', () => {
    const q = buildHistoryTimelineQuery({ objectiveId: 'all', text: 'boom', actor: '', vesselId: 'vsl_1', sourceType: 'Mission', postmortemOnly: false, showReadRequests: false }, 100, 3);
    expect(q).toEqual({ pageNumber: 3, pageSize: 100, objectiveId: null, text: 'boom', actor: null, vesselId: 'vsl_1', sourceTypes: ['Mission'], postmortemOnly: undefined, excludeReadRequests: true });
  });

  it('exports CSV with escaping and Markdown with the active filters', () => {
    expect(escapeCsvValue('a,"b"')).toBe('"a,""b"""');
    const csv = buildHistoryCsv([hist({ description: 'x, y' })]);
    expect(csv.split('\r\n')[1]).toContain('"x, y"');
    const md = buildHistoryMarkdown({ text: 'boom', excludeReadRequests: true }, [hist()], '2026-10-07T00:00:00Z');
    expect(md).toContain('Filters: text=`boom`, excludeReadRequests=`true`');
    expect(md).toContain('## Mission completed');
  });

  it('builds request history queries, delete query, and summary buckets', () => {
    const f = { ...defaultRequestHistoryFilters(Date.parse('2026-10-07T12:00:00Z')), method: 'POST', statusCode: '500', isSuccess: 'false' as const };
    expect(hasActiveRequestFilters(f)).toBe(true);
    expect(hasActiveRequestFilters(defaultRequestHistoryFilters())).toBe(false);
    expect(buildRequestHistoryQuery(f, 2, 25)).toMatchObject({ pageNumber: 2, pageSize: 25, method: 'POST', statusCode: 500, isSuccess: false });
    const del = buildRequestHistoryDeleteQuery(f);
    expect(del.pageNumber).toBeUndefined();
    expect(del.pageSize).toBeUndefined();
    const now = new Date('2026-10-07T12:07:00Z');
    expect(buildRequestHistorySummaryQuery(f, 'lastHour', now)).toMatchObject({ bucketMinutes: 1, method: 'POST' });
    const w = getActivityRangeWindow('lastHour', now);
    const buckets = normalizeSummaryBuckets({ ...SUMMARY, buckets: [{ bucketStartUtc: new Date(w.startMs).toISOString(), bucketEndUtc: '', totalCount: 5, successCount: 4, failureCount: 1, averageDurationMs: 2 }] }, 'lastHour', now);
    expect(buckets).toHaveLength(60);
    expect(buckets[0].totalCount).toBe(5);
    expect(buckets[59].totalCount).toBe(0);
  });

  it('formats tokens, ticks, jobs, and replay links', () => {
    expect(formatTokens(1500)).toBe('1.5K');
    expect(formatTokens(2_000_000)).toBe('2M');
    expect(formatTokens(12)).toBe('12');
    expect(computeYTicks(90)).toEqual([0, 50, 100]);
    expect(isJobTerminal('Succeeded')).toBe(true);
    expect(isJobTerminal('Running')).toBe(false);
    expect(replayHref('req 1')).toBe('/api-explorer?replay=req%201');
  });

  it('splits token usage by model or by type', () => {
    const data = {
      buckets: [{ bucketStartUtc: 'a', bucketEndUtc: 'b', inputTokens: 1, outputTokens: 2, cachedTokens: 3, totalTokens: 6, models: [{ model: 'm1', totalTokens: 6 }] }],
      byModel: [{ model: 'm1', totalTokens: 6 }, { model: 'm2', totalTokens: 0 }],
    } as unknown as TokenUsageSummaryResult;
    expect(usageSeries(data, 'byType', (s) => s).values).toEqual([[1, 2, 3]]);
    const byModel = usageSeries(data, 'total', (s) => s);
    expect(byModel.series.map((s) => s.key)).toEqual(['m1', 'm2']);
    expect(byModel.values).toEqual([[6, 0]]);
  });
});

describe('Activity > All Activity', () => {
  it('is the default source: entries with counts; a row opens its route', async () => {
    const h = await renderW4Routes(ROUTES, '/activity');
    await waitFor(() => expect(screen.getByText('Mission completed')).toBeTruthy());
    expect(screen.getByTestId('activity-history')).toBeTruthy();
    expect(screen.getByLabelText('Visible Entries: 2')).toBeTruthy();
    expect(screen.getByLabelText('Errors: 1')).toBeTruthy();
    expect(api.enumerateHistoryTimeline).toHaveBeenCalledWith(expect.objectContaining({ pageNumber: 1, pageSize: 100, excludeReadRequests: true }));
    await fireEvent.press(screen.getByTestId('history-row-0'));
    await waitFor(() => expect(h.getPathname()).toBe('/missions/msn_1'));
  });

  it('takes filters from the link, reloads live, and pages through the server', async () => {
    api.enumerateHistoryTimeline.mockResolvedValue(page([hist()], { totalPages: 2 }) as never);
    const h = await renderW4Routes(ROUTES, '/activity?source=history&vesselId=vsl_1&postmortemOnly=true');
    await waitFor(() => expect(screen.getByText('Mission completed')).toBeTruthy());
    expect(api.enumerateHistoryTimeline).toHaveBeenCalledWith(expect.objectContaining({ vesselId: 'vsl_1', postmortemOnly: true }));
    api.enumerateHistoryTimeline.mockResolvedValueOnce(page([hist({ id: 'hst_3', title: 'Older entry' })], { totalPages: 2, pageNumber: 2 }) as never);
    await fireEvent(screen.getByTestId('history-row-0'), 'endReached');
    await waitFor(() => expect(screen.getByText('Older entry')).toBeTruthy());
    expect(api.enumerateHistoryTimeline).toHaveBeenLastCalledWith(expect.objectContaining({ pageNumber: 2 }));
    const calls = api.enumerateHistoryTimeline.mock.calls.length;
    await emit(h, { type: 'mission.changed', data: {} });
    await waitFor(() => expect(api.enumerateHistoryTimeline.mock.calls.length).toBe(calls + 1));
  });

  it('deletes request rows only, after confirmation', async () => {
    api.deleteRequestHistoryEntry.mockResolvedValue(undefined as never);
    await renderW4Routes(ROUTES, '/activity');
    await waitFor(() => expect(screen.getByText('POST /api/v1/x')).toBeTruthy());
    expect(screen.queryByTestId('history-row-0-swipe-delete')).toBeNull();
    await fireEvent(rowActionTarget(screen.getByTestId('history-row-1-swipe'), 'delete'), 'accessibilityAction', { nativeEvent: { actionName: 'delete' } });
    await waitFor(() => expect(screen.getByText('Delete this request-history entry? This cannot be undone.')).toBeTruthy());
    await act(async () => { await fireEvent.press(screen.getByTestId('history-confirm-confirm')); });
    expect(api.deleteRequestHistoryEntry).toHaveBeenCalledWith('req_9');
  });

  it('exports the current view through the share sheet', async () => {
    const sharing = jest.requireMock('expo-sharing') as { shareAsync: jest.Mock };
    await renderW4Routes(ROUTES, '/activity');
    await waitFor(() => expect(screen.getByText('Mission completed')).toBeTruthy());
    await act(async () => { await fireEvent.press(screen.getByTestId('history-export-json')); });
    await waitFor(() => expect(sharing.shareAsync).toHaveBeenCalledWith(expect.stringMatching(/armada-history-.*\.json$/), expect.objectContaining({ mimeType: 'application/json' })));
    expect(api.enumerateHistoryTimeline).toHaveBeenLastCalledWith(expect.objectContaining({ pageSize: 5000 }));
  });
});

describe('Activity > API Requests', () => {
  it('shows the summary and list; a row opens the request route on phones', async () => {
    const h = await renderW4Routes(ROUTES, '/activity?source=requests');
    await waitFor(() => expect(screen.getByText('POST /api/v1/missions')).toBeTruthy());
    expect(screen.getByLabelText('Success Rate: 75.0%')).toBeTruthy();
    expect(screen.getByLabelText('Failures: 1')).toBeTruthy();
    await fireEvent.press(screen.getByTestId('request-row-req_1'));
    await waitFor(() => expect(h.getPathname()).toBe('/requests/req_1'));
  });

  it('long press selects rows for Delete Selected; Delete Visible Range deletes by filter', async () => {
    api.deleteRequestHistoryEntries.mockResolvedValue({} as never);
    api.deleteRequestHistoryByFilter.mockResolvedValue({} as never);
    await renderW4Routes(ROUTES, '/activity?source=requests');
    await waitFor(() => expect(screen.getByText('POST /api/v1/missions')).toBeTruthy());
    await fireEvent(screen.getByTestId('request-row-req_1'), 'longPress');
    await fireEvent.press(screen.getByTestId('request-row-req_2'));
    await fireEvent.press(screen.getByTestId('requests-delete-selected'));
    await waitFor(() => expect(screen.getByText('Delete 2 selected request-history entries?')).toBeTruthy());
    await act(async () => { await fireEvent.press(screen.getByTestId('requests-confirm-confirm')); });
    expect(api.deleteRequestHistoryEntries).toHaveBeenCalledWith(['req_1', 'req_2']);

    await waitFor(() => expect(screen.getByTestId('requests-delete-filtered')).toBeTruthy());
    await fireEvent.press(screen.getByTestId('requests-delete-filtered'));
    await act(async () => { await fireEvent.press(screen.getByTestId('requests-confirm-confirm')); });
    const query = api.deleteRequestHistoryByFilter.mock.calls[0][0];
    expect(query.pageNumber).toBeUndefined();
    expect(query.fromUtc).toBeTruthy();
  });

  it('filters in a sheet; tenant only for admins, user for tenant admins', async () => {
    await renderW4Routes(ROUTES, '/activity?source=requests', 'tenantAdmin');
    await waitFor(() => expect(screen.getByText('POST /api/v1/missions')).toBeTruthy());
    await fireEvent.press(screen.getByTestId('requests-filters'));
    await waitFor(() => expect(screen.getByTestId('requests-filter-form-route')).toBeTruthy());
    expect(screen.queryByTestId('requests-filter-form-tenantId')).toBeNull();
    expect(screen.getByTestId('requests-filter-form-userId')).toBeTruthy();
    await fireEvent.changeText(screen.getByTestId('requests-filter-form-route'), '/api/v1/fleets');
    await act(async () => { await fireEvent.press(screen.getByTestId('requests-filter-form-submit')); });
    await waitFor(() => expect(api.listRequestHistory).toHaveBeenLastCalledWith(expect.objectContaining({ route: '/api/v1/fleets', pageNumber: 1 })));
    expect(api.getRequestHistorySummary).toHaveBeenLastCalledWith(expect.objectContaining({ route: '/api/v1/fleets' }));
  });
});

describe('tablets', () => {
  it('show the selected request beside the list', async () => {
    mockWindow.width = 1180;
    api.getRequestHistoryEntry.mockResolvedValue(RECORD);
    const h = await renderW4Routes(ROUTES, '/activity?source=requests');
    await waitFor(() => expect(screen.getByText('POST /api/v1/missions')).toBeTruthy());
    expect(screen.getByText('Select an item to see its details.')).toBeTruthy();
    await fireEvent.press(screen.getByTestId('request-row-req_1'));
    await waitFor(() => expect(screen.getByTestId('request-title')).toHaveTextContent('POST /api/v1/missions'));
    expect(screen.getByTestId('split-view')).toBeTruthy();
    expect(h.getPathname()).toBe('/activity');
  });
});

describe('request detail', () => {
  it('shows the stored request and replays it in API Explorer', async () => {
    api.getRequestHistoryEntry.mockResolvedValue(RECORD);
    const h = await renderW4Routes(ROUTES, '/requests/req_1');
    await waitFor(() => expect(screen.getByTestId('request-title')).toHaveTextContent('POST /api/v1/missions'));
    expect(screen.getByText('Request Body (Stored body was truncated)')).toBeTruthy();
    expect(screen.getByText('{"title":"x"}')).toBeTruthy();
    await fireEvent.press(screen.getByTestId('request-replay'));
    await waitFor(() => expect(h.getPathname()).toBe('/api-explorer'));
    expect(h.getSearchParams()).toMatchObject({ replay: 'req_1' });
  });

  it('deletes after confirmation', async () => {
    api.getRequestHistoryEntry.mockResolvedValue(RECORD);
    api.deleteRequestHistoryEntry.mockResolvedValue(undefined as never);
    const h = await renderW4Routes(ROUTES, '/requests/req_1');
    await waitFor(() => expect(screen.getByTestId('request-delete')).toBeTruthy());
    await fireEvent.press(screen.getByTestId('request-delete'));
    await act(async () => { await fireEvent.press(screen.getByTestId('request-confirm-confirm')); });
    expect(api.deleteRequestHistoryEntry).toHaveBeenCalledWith('req_1');
    await waitFor(() => expect(h.getPathname()).toBe('/activity'));
  });
});

describe('Activity > Events, Signals, Token Usage', () => {
  it('Events embeds the Operations list; phones open /events/:id', async () => {
    api.listEvents.mockResolvedValue(page([{ id: 'evt_1', tenantId: null, eventType: 'mission.completed', entityType: 'mission', entityId: 'msn_1', captainId: null, missionId: 'msn_1', vesselId: null, voyageId: null, message: 'done', payload: null, createdUtc: '2026-10-07T10:00:00Z' }]) as never);
    const h = await renderW4Routes(ROUTES, '/activity?source=events');
    await waitFor(() => expect(screen.getByTestId('event-row-evt_1')).toBeTruthy());
    await fireEvent.press(screen.getByTestId('event-row-evt_1'));
    await waitFor(() => expect(h.getPathname()).toBe('/events/evt_1'));
  });

  it('Signals embeds the Operations list', async () => {
    api.listSignals.mockResolvedValue(page([{ id: 'sig_1', tenantId: null, type: 'Mail', payload: 'hello', fromCaptainId: null, toCaptainId: null, read: false, createdUtc: '2026-10-07T10:00:00Z' }]) as never);
    await renderW4Routes(ROUTES, '/activity?source=signals');
    await waitFor(() => expect(screen.getByTestId('signal-row-sig_1')).toBeTruthy());
  });

  it('Token Usage shows totals and the by-model breakdown', async () => {
    api.getTokenUsage.mockResolvedValue({
      fromUtc: null, toUtc: null, bucketMinutes: 15, recordCount: 4, estimatedCount: 1, inputTokens: 1000, outputTokens: 500, cachedTokens: 2000, totalTokens: 3500,
      buckets: [{ bucketStartUtc: '2026-10-07T10:00:00Z', bucketEndUtc: '2026-10-07T10:15:00Z', inputTokens: 1000, outputTokens: 500, cachedTokens: 2000, totalTokens: 3500, models: [{ model: 'opus', inputTokens: 1000, outputTokens: 500, cachedTokens: 2000, totalTokens: 3500 }] }],
      byModel: [{ model: 'opus', inputTokens: 1000, outputTokens: 500, cachedTokens: 2000, totalTokens: 3500 }],
    } as never);
    await renderW4Routes(ROUTES, '/activity?source=tokens');
    await waitFor(() => expect(screen.getByLabelText('Total: 3.5K')).toBeTruthy());
    expect(screen.getByText('1 of 4 records estimated')).toBeTruthy();
    expect(within(screen.getByTestId('tokens-by-model')).getByText('opus')).toBeTruthy();
    expect(api.getTokenUsage).toHaveBeenCalledWith(expect.objectContaining({ bucketMinutes: 15 }));
    await fireEvent.press(screen.getByTestId('tokens-range-week'));
    await waitFor(() => expect(api.getTokenUsage).toHaveBeenLastCalledWith(expect.objectContaining({ bucketMinutes: 120 })));
  });
});

describe('Jobs', () => {
  const JOBS: Job[] = [
    { id: 'job_1', tenantId: null, userId: null, name: 'Import vessels', kind: 'VesselImport', status: 'Succeeded', progress: 100, resultJson: '{"ok":true}', errorReason: null, createdUtc: '2026-10-07T10:00:00Z', startedUtc: null, completedUtc: null, lastUpdateUtc: '2026-10-07T10:00:00Z' },
    { id: 'job_2', tenantId: null, userId: null, name: 'Health scan', kind: 'HealthEvaluation', status: 'Succeeded', progress: 40, resultJson: null, errorReason: null, createdUtc: '2026-10-07T10:00:00Z', startedUtc: null, completedUtc: null, lastUpdateUtc: '2026-10-07T10:00:00Z' },
  ];

  it('lists jobs and opens one with its full record', async () => {
    api.listJobs.mockResolvedValue(page(JOBS) as never);
    api.getJob.mockResolvedValue(JOBS[0]);
    await renderW4Routes(ROUTES, '/jobs');
    await waitFor(() => expect(screen.getByText('Import vessels')).toBeTruthy());
    expect(screen.queryByTestId('job-row-job_1-swipe')).toBeNull();
    await fireEvent.press(screen.getByTestId('job-row-job_1'));
    await waitFor(() => expect(api.getJob).toHaveBeenCalledWith('job_1'));
    await waitFor(() => expect(screen.getByText('{"ok":true}')).toBeTruthy());
  });

  it('cancels an unfinished job', async () => {
    api.listJobs.mockResolvedValue(page([{ ...JOBS[1], status: 'Running' }]) as never);
    api.cancelJob.mockResolvedValue({ ...JOBS[1], status: 'Cancelled' });
    await renderW4Routes(ROUTES, '/jobs');
    await waitFor(() => expect(screen.getByText('Health scan')).toBeTruthy());
    await act(async () => { await fireEvent(rowActionTarget(screen.getByTestId('job-row-job_2-swipe'), 'cancel'), 'accessibilityAction', { nativeEvent: { actionName: 'cancel' } }); });
    expect(api.cancelJob).toHaveBeenCalledWith('job_2');
  });
});

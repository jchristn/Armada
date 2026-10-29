import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  ApiError,
  diffBlackBoxEntries,
  enumerateBlackBoxNearbySuccesses,
  enumerateBlackBoxRecoveryStats,
  enumerateBlackBoxTimeline,
  enumerateVesselBlackBoxDoctor,
  enumerateVesselReservations,
  getBlackBoxRecoveryStats,
  getBlackBoxTimeline,
  getVesselBlackBoxDoctor,
  getVoyageBlackBoxCascade,
  lintMissionBrief,
  listBlackBoxNearbySuccesses,
  listVesselReservations,
  previewMissionOverlap,
} from './client';
import { onlyCallArgs } from '../test/mockCalls';

function jsonResponse(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
}

function stub(response: Response) {
  const fetchMock = vi.fn().mockResolvedValue(response);
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

function body(init: RequestInit): Record<string, unknown> {
  return JSON.parse(String(init.body ?? '{}')) as Record<string, unknown>;
}

describe('black box insight client', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('previews overlaps with a PascalCase body and camelizes the preview', async () => {
    const fetchMock = stub(jsonResponse(200, {
      VesselId: 'vsl_1', SerializeOverlappingMissions: true, LikelyFiles: ['src/a.ts'], LikelyFileCount: 1, WarningCount: 1,
      Warnings: [{ Kind: 'ActiveMission', OverlappingMissionId: 'msn_2', Files: ['src/a.ts'], FileCount: 1, Message: 'Shares 1 file' }],
      SuggestedDependsOnMissionId: 'msn_2', BriefLint: { Findings: [{ RuleId: 'BRIEF-DEP-001', Severity: 'Warning', Message: 'Dependency' }], FindingCount: 1, Clean: false },
    }));
    const preview = await previewMissionOverlap('vsl 1', { title: 'T', description: 'Touch only src/a.ts', dependsOnMissionId: null });
    const [url, init] = onlyCallArgs(fetchMock);
    expect(url).toMatch(/\/api\/v1\/vessels\/vsl%201\/overlap-preview$/);
    expect(init.method).toBe('POST');
    expect(body(init)).toEqual({ Title: 'T', Description: 'Touch only src/a.ts' });
    expect(preview.warnings[0].overlappingMissionId).toBe('msn_2');
    expect(preview.briefLint?.findings[0].ruleId).toBe('BRIEF-DEP-001');
    expect(preview.suggestedDependsOnMissionId).toBe('msn_2');
  });

  it('lints a brief and sends only the fields given', async () => {
    const fetchMock = stub(jsonResponse(200, { Findings: [], FindingCount: 0, Clean: true, PathsChecked: false }));
    const result = await lintMissionBrief({ title: 'T', description: 'D', vesselId: 'vsl_1', dependsOnMissionId: 'msn_0' });
    const [url, init] = onlyCallArgs(fetchMock);
    expect(url).toMatch(/\/api\/v1\/missions\/lint-brief$/);
    expect(body(init)).toEqual({ Title: 'T', Description: 'D', VesselId: 'vsl_1', DependsOnMissionId: 'msn_0' });
    expect(result.clean).toBe(true);
  });

  it('pages reservations with GET query and POST body', async () => {
    let fetchMock = stub(jsonResponse(200, { Objects: [{ Directory: 'src', MissionId: 'msn_1' }], TotalRecords: 1, Threshold: 3 }));
    const pageResult = await listVesselReservations('vsl_1', { pageNumber: 2, pageSize: 5 });
    expect(onlyCallArgs(fetchMock)[0]).toMatch(/\/api\/v1\/vessels\/vsl_1\/reservations\?pageNumber=2&pageSize=5$/);
    expect(pageResult.threshold).toBe(3);
    expect(pageResult.objects[0].directory).toBe('src');
    fetchMock = stub(jsonResponse(200, { Objects: [] }));
    await enumerateVesselReservations('vsl_1', { pageNumber: 3 });
    const [url, init] = onlyCallArgs(fetchMock);
    expect(url).toMatch(/\/reservations\/enumerate$/);
    expect(body(init)).toEqual({ PageNumber: 3 });
  });

  it('reads the doctor with a window and paging', async () => {
    let fetchMock = stub(jsonResponse(200, { Objects: [{ Key: 'k', Setting: 'LandingMode', Count: 6, Applicable: true, ExampleEntryId: 'bbx_1' }], WindowDays: 7 }));
    const doctor = await getVesselBlackBoxDoctor('vsl_1', { windowDays: 7, pageNumber: 1, pageSize: 10 });
    expect(onlyCallArgs(fetchMock)[0]).toMatch(/\/vessels\/vsl_1\/blackbox-doctor\?windowDays=7&pageNumber=1&pageSize=10$/);
    expect(doctor.objects[0].applicable).toBe(true);
    fetchMock = stub(jsonResponse(200, { Objects: [] }));
    await enumerateVesselBlackBoxDoctor('vsl_1', { windowDays: 90 });
    expect(body(onlyCallArgs(fetchMock)[1])).toEqual({ WindowDays: 90 });
  });

  it('reads recovery statistics with filters', async () => {
    let fetchMock = stub(jsonResponse(200, { Objects: [{ RuleId: 'BBX-LAND-001', PathId: 'p', Runs: 4, SuccessRate: 0.75 }] }));
    const stats = await getBlackBoxRecoveryStats({ ruleId: 'BBX-LAND-001', vesselId: 'vsl_1' });
    expect(onlyCallArgs(fetchMock)[0]).toMatch(/\/api\/v1\/blackbox\/recovery-stats\?ruleId=BBX-LAND-001&vesselId=vsl_1$/);
    expect(stats.objects[0].successRate).toBe(0.75);
    fetchMock = stub(jsonResponse(200, { Objects: [] }));
    await enumerateBlackBoxRecoveryStats({ ruleId: 'R', pageSize: 50 });
    expect(body(onlyCallArgs(fetchMock)[1])).toEqual({ PageSize: 50, RuleId: 'R' });
  });

  it('diffs two entries and lists nearby successes', async () => {
    let fetchMock = stub(jsonResponse(200, { EntryId: 'bbx_2', OtherEntryId: 'bbx_1', Differences: [{ Section: 'Subject', Field: 'Model', Value: 'b', OtherValue: 'a' }] }));
    const diff = await diffBlackBoxEntries('bbx_2', 'bbx_1');
    expect(onlyCallArgs(fetchMock)[0]).toMatch(/\/api\/v1\/blackbox\/bbx_2\/diff\/bbx_1$/);
    expect(diff.differences[0].otherValue).toBe('a');
    fetchMock = stub(jsonResponse(200, { Objects: [{ SuccessId: 'bbs_1', MissionId: 'msn_9', OverlappingFiles: ['a'] }] }));
    const near = await listBlackBoxNearbySuccesses('bbx_2', { pageNumber: 1, pageSize: 10 });
    expect(onlyCallArgs(fetchMock)[0]).toMatch(/\/blackbox\/bbx_2\/nearby-successes\?pageNumber=1&pageSize=10$/);
    expect(near.objects[0].missionId).toBe('msn_9');
    fetchMock = stub(jsonResponse(200, { Objects: [] }));
    await enumerateBlackBoxNearbySuccesses('bbx_2');
    expect(onlyCallArgs(fetchMock)[0]).toMatch(/\/nearby-successes\/enumerate$/);
  });

  it('reads the timeline in the requested order', async () => {
    let fetchMock = stub(jsonResponse(200, { EntryId: 'bbx_1', Objects: [{ Source: 'Mission', Kind: 'created', Message: 'Created' }], Sources: [{ Source: 'Mission', Count: 1, Truncated: false }] }));
    const timeline = await getBlackBoxTimeline('bbx_1', { order: 'CreatedDescending', pageNumber: 2, pageSize: 25 });
    expect(onlyCallArgs(fetchMock)[0]).toMatch(/\/blackbox\/bbx_1\/timeline\?order=CreatedDescending&pageNumber=2&pageSize=25$/);
    expect(timeline.objects[0].source).toBe('Mission');
    expect(timeline.sources[0].count).toBe(1);
    fetchMock = stub(jsonResponse(200, { Objects: [] }));
    await enumerateBlackBoxTimeline('bbx_1', { order: 'CreatedAscending' });
    expect(body(onlyCallArgs(fetchMock)[1])).toEqual({ Order: 'CreatedAscending' });
  });

  it('reads a voyage cascade graph and surfaces 404 as ApiError', async () => {
    let fetchMock = stub(jsonResponse(200, { VoyageId: 'vyg_1', Nodes: [{ MissionId: 'msn_1', Status: 'Failed', IsRoot: true }], Edges: [{ FromMissionId: 'msn_1', ToMissionId: 'msn_2', Kind: 'CancelledBecause' }] }));
    const graph = await getVoyageBlackBoxCascade('vyg_1');
    expect(onlyCallArgs(fetchMock)[0]).toMatch(/\/api\/v1\/voyages\/vyg_1\/blackbox\/cascade$/);
    expect(graph.nodes[0].isRoot).toBe(true);
    expect(graph.edges[0].kind).toBe('CancelledBecause');
    fetchMock = stub(jsonResponse(404, { Error: 'NotFound', Message: 'Voyage not found' }));
    await expect(getVoyageBlackBoxCascade('vyg_x')).rejects.toBeInstanceOf(ApiError);
  });
});

import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import BlackBoxTimeline, { timelineEntityHref } from './BlackBoxTimeline';
import BlackBoxAttemptDiff, { defaultDiffTarget } from './BlackBoxAttemptDiff';
import BlackBoxNearbySuccesses, { formatHoursFromFailure } from './BlackBoxNearbySuccesses';
import VesselReservationsCard from './VesselReservationsCard';
import VesselBlackBoxDoctorCard from './VesselBlackBoxDoctorCard';
import BlackBoxRecoveryStatsTab, { recoveryRatePercent } from './BlackBoxRecoveryStatsTab';
import {
  applyBlackBoxRecommendation,
  diffBlackBoxEntries,
  getBlackBoxRecoveryStats,
  getBlackBoxTimeline,
  getVesselBlackBoxDoctor,
  listBlackBoxNearbySuccesses,
  listVesselReservations,
} from '../../api/client';
import { translateTemplate } from '../../i18n/runtime';
import type { BlackBoxAttemptDiff as Diff, BlackBoxDoctorPage, BlackBoxTimelineItem, BlackBoxTimelinePage, VesselReservationPage } from '../../types/blackbox';
import { blackBoxAttemptsFixture, page } from '../../test/fixtures/blackbox';

vi.mock('../../api/client', () => ({
  applyBlackBoxRecommendation: vi.fn(),
  runBlackBoxRecovery: vi.fn(),
  diffBlackBoxEntries: vi.fn(),
  getBlackBoxRecoveryStats: vi.fn(),
  getBlackBoxTimeline: vi.fn(),
  getVesselBlackBoxDoctor: vi.fn(),
  listBlackBoxNearbySuccesses: vi.fn(),
  listVesselReservations: vi.fn(),
}));
const localeValue = {
  t: (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', text, null, params),
  locale: 'en',
  formatDateTime: (v: string | null | undefined) => v ?? '',
  formatRelativeTime: (v: string | null | undefined) => v ?? '',
};
vi.mock('../../context/LocaleContext', () => ({ useLocale: () => localeValue }));

function renderIn(node: React.ReactNode) {
  return render(<MemoryRouter>{node}</MemoryRouter>);
}

function timelinePage(overrides: Partial<BlackBoxTimelinePage> = {}): BlackBoxTimelinePage {
  return {
    ...page<BlackBoxTimelineItem>([
      { timestampUtc: '2026-10-28T10:00:00Z', source: 'Mission', kind: 'created', message: 'Mission created', entityId: 'msn_1' },
      { timestampUtc: '2026-10-28T10:05:00Z', source: 'MergeEntry', kind: 'queued', message: 'Queued for landing', entityId: 'mrg_1' },
    ], { totalRecords: 30, totalPages: 2, pageSize: 25 }),
    entryId: 'bbx_1', sources: [{ source: 'Mission', count: 1, truncated: false }, { source: 'Captain', count: 200, truncated: true }], truncated: false,
    ...overrides,
  };
}

beforeEach(() => {
  vi.clearAllMocks();
  localStorage.clear();
});

describe('BlackBoxTimeline', () => {
  it('lists the merged timeline with sources, entity links and cut sources, and pages', async () => {
    vi.mocked(getBlackBoxTimeline).mockResolvedValue(timelinePage());
    renderIn(<BlackBoxTimeline entryId="bbx_1" />);
    expect(await screen.findByText('Mission created')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'mrg_1' })).toHaveAttribute('href', '/merge-queue/mrg_1');
    expect(screen.getByText('Merge entry')).toBeInTheDocument();
    expect(screen.getByText(/Some sources were cut to their newest items: Captain/)).toBeInTheDocument();
    expect(getBlackBoxTimeline).toHaveBeenCalledWith('bbx_1', { order: 'CreatedAscending', pageNumber: 1, pageSize: 25 });
    fireEvent.click(screen.getByRole('button', { name: /next/i }));
    await waitFor(() => expect(getBlackBoxTimeline).toHaveBeenLastCalledWith('bbx_1', { order: 'CreatedAscending', pageNumber: 2, pageSize: 25 }));
  });

  it('switches to newest first and starts again at page 1', async () => {
    vi.mocked(getBlackBoxTimeline).mockResolvedValue(timelinePage());
    renderIn(<BlackBoxTimeline entryId="bbx_1" />);
    await screen.findByText('Mission created');
    fireEvent.change(screen.getByLabelText('Timeline order'), { target: { value: 'CreatedDescending' } });
    await waitFor(() => expect(getBlackBoxTimeline).toHaveBeenLastCalledWith('bbx_1', { order: 'CreatedDescending', pageNumber: 1, pageSize: 25 }));
  });

  it('shows the empty and error states', async () => {
    vi.mocked(getBlackBoxTimeline).mockResolvedValueOnce(timelinePage({ ...page<BlackBoxTimelineItem>([]), sources: [], truncated: false }));
    const view = renderIn(<BlackBoxTimeline entryId="bbx_1" />);
    expect(await screen.findByText('No timeline items')).toBeInTheDocument();
    view.unmount();
    vi.mocked(getBlackBoxTimeline).mockRejectedValueOnce(new Error('boom'));
    renderIn(<BlackBoxTimeline entryId="bbx_2" />);
    expect(await screen.findByText('The timeline could not be loaded: boom')).toBeInTheDocument();
  });

  it('maps entity ids to pages', () => {
    expect(timelineEntityHref('msn_1')).toBe('/missions/msn_1');
    expect(timelineEntityHref('dck_1')).toBe('/docks/dck_1');
    expect(timelineEntityHref('cpt_1')).toBe('/captains/cpt_1');
    expect(timelineEntityHref('hbr_1')).toBeNull();
    expect(timelineEntityHref(null)).toBeNull();
  });
});

const DIFF: Diff = {
  entryId: 'bbx_2', otherEntryId: 'bbx_1', sameMission: true, attempt: 2, otherAttempt: 1, sameFingerprint: true,
  differences: [
    { section: 'Subject', field: 'Model', value: 'opus', otherValue: 'sonnet' },
    { section: 'Workspace', field: 'StartCommit', value: 'bbb', otherValue: 'aaa' },
  ],
  differenceCount: 2, filesOnlyInEntry: ['src/new.ts'], filesOnlyInEntryCount: 1, filesOnlyInOther: [], filesOnlyInOtherCount: 0, filesInBothCount: 4, changedFilesKnown: true,
};

describe('BlackBoxAttemptDiff', () => {
  it('compares with the previous attempt by default and renders the differences side by side', async () => {
    const attempts = blackBoxAttemptsFixture();
    vi.mocked(diffBlackBoxEntries).mockResolvedValue({ ...DIFF, entryId: attempts[1].id, otherEntryId: attempts[0].id });
    renderIn(<BlackBoxAttemptDiff entryId={attempts[1].id} attempts={attempts} />);
    await waitFor(() => expect(diffBlackBoxEntries).toHaveBeenCalledWith(attempts[1].id, attempts[0].id));
    const table = await screen.findByRole('table', { name: 'Differences' });
    const row = within(table).getByText('Model').closest('tr')!;
    expect(within(row).getByText('opus')).toHaveClass('blackbox-diff-new');
    expect(within(row).getByText('sonnet')).toHaveClass('blackbox-diff-old');
    expect(screen.getByText('Same failure fingerprint')).toBeInTheDocument();
    expect(screen.getByText('src/new.ts')).toBeInTheDocument();
    expect(screen.getByText('4')).toBeInTheDocument();
  });

  it('compares with a typed entry id', async () => {
    vi.mocked(diffBlackBoxEntries).mockResolvedValue({ ...DIFF, sameMission: false, sameFingerprint: false, differences: [], differenceCount: 0, changedFilesKnown: false });
    renderIn(<BlackBoxAttemptDiff entryId="bbx_2" attempts={[]} />);
    expect(screen.getByText('Nothing to compare yet')).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText('Entry id to compare with'), { target: { value: 'bbx_9' } });
    fireEvent.click(screen.getByRole('button', { name: 'Compare' }));
    await waitFor(() => expect(diffBlackBoxEntries).toHaveBeenCalledWith('bbx_2', 'bbx_9'));
    expect(await screen.findByText('These entries belong to different missions.')).toBeInTheDocument();
    expect(screen.getByText('No recorded values differ between these entries.')).toBeInTheDocument();
    expect(screen.getByText('Changed files are not known for one of the entries.')).toBeInTheDocument();
  });

  it('refuses to compare an entry with itself and shows errors', async () => {
    vi.mocked(diffBlackBoxEntries).mockRejectedValue(new Error('Entry not found'));
    renderIn(<BlackBoxAttemptDiff entryId="bbx_2" attempts={[]} />);
    fireEvent.change(screen.getByLabelText('Entry id to compare with'), { target: { value: 'bbx_2' } });
    fireEvent.click(screen.getByRole('button', { name: 'Compare' }));
    expect(await screen.findByText(/cannot be compared with itself/)).toBeInTheDocument();
    expect(diffBlackBoxEntries).not.toHaveBeenCalled();
    fireEvent.change(screen.getByLabelText('Entry id to compare with'), { target: { value: 'bbx_404' } });
    fireEvent.click(screen.getByRole('button', { name: 'Compare' }));
    expect(await screen.findByText('The comparison could not be loaded: Entry not found')).toBeInTheDocument();
  });

  it('picks the default target', () => {
    const attempts = blackBoxAttemptsFixture();
    expect(defaultDiffTarget(attempts[0].id, attempts)).toBe(attempts[1].id);
    expect(defaultDiffTarget('bbx_none', [])).toBe('');
  });
});

describe('BlackBoxNearbySuccesses', () => {
  it('lists landings near the failure linked to their missions', async () => {
    vi.mocked(listBlackBoxNearbySuccesses).mockResolvedValue(page([
      { successId: 'bbs_1', missionId: 'msn_9', missionTitle: 'Fix parser', landedUtc: '2026-10-28T09:00:00Z', hoursFromFailure: -2.5, targetCommit: 'abcdef1234567', overlappingFiles: ['src/a.ts'], overlappingFileCount: 1 },
    ]));
    renderIn(<BlackBoxNearbySuccesses entryId="bbx_1" />);
    expect(await screen.findByRole('link', { name: 'Fix parser' })).toHaveAttribute('href', '/missions/msn_9');
    expect(screen.getByText('2.5 hours before')).toBeInTheDocument();
    expect(screen.getByText('abcdef1234')).toBeInTheDocument();
    expect(listBlackBoxNearbySuccesses).toHaveBeenCalledWith('bbx_1', { pageNumber: 1, pageSize: 10 });
  });

  it('shows the empty and error states', async () => {
    vi.mocked(listBlackBoxNearbySuccesses).mockResolvedValueOnce(page([]));
    const view = renderIn(<BlackBoxNearbySuccesses entryId="bbx_1" />);
    expect(await screen.findByText('No nearby successes')).toBeInTheDocument();
    view.unmount();
    vi.mocked(listBlackBoxNearbySuccesses).mockRejectedValueOnce(new Error('down'));
    renderIn(<BlackBoxNearbySuccesses entryId="bbx_2" />);
    expect(await screen.findByText('Nearby successes could not be loaded: down')).toBeInTheDocument();
  });

  it('formats the distance from the failure', () => {
    const t = localeValue.t;
    expect(formatHoursFromFailure(0.5, t)).toBe('30 minutes after');
    expect(formatHoursFromFailure(-0.25, t)).toBe('15 minutes before');
    expect(formatHoursFromFailure(3, t)).toBe('3 hours after');
  });
});

function reservations(objects: VesselReservationPage['objects'], overrides: Partial<VesselReservationPage> = {}): VesselReservationPage {
  return { ...page(objects), vesselId: 'vsl_1', threshold: 3, hotDirectoryCount: 2, activeMissionsScanned: 4, scanTruncated: false, ...overrides };
}

describe('VesselReservationsCard', () => {
  it('lists reservations and the serialize state', async () => {
    vi.mocked(listVesselReservations).mockResolvedValue(reservations([
      { vesselId: 'vsl_1', directory: 'src/landing', missionId: 'msn_3', missionTitle: 'Landing fix', missionStatus: 'InProgress', hotspotEntryCount: 5, fileCount: 2, sinceUtc: '2026-10-28T08:00:00Z' },
    ], { scanTruncated: true, activeMissionsScanned: 200 }));
    renderIn(<VesselReservationsCard vesselId="vsl_1" serializeOverlappingMissions />);
    expect(await screen.findByText('src/landing')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Landing fix' })).toHaveAttribute('href', '/missions/msn_3');
    expect(screen.getByText(/at least 3 black box entries/)).toBeInTheDocument();
    expect(screen.getByText('Overlapping missions are chained.')).toBeInTheDocument();
    expect(screen.getByText('Only the newest 200 active missions were scanned.')).toBeInTheDocument();
  });

  it('shows reservations off, empty and error', async () => {
    vi.mocked(listVesselReservations).mockResolvedValueOnce(reservations([], { threshold: 0 }));
    const view = renderIn(<VesselReservationsCard vesselId="vsl_1" />);
    expect(await screen.findByText('No reservations')).toBeInTheDocument();
    expect(screen.getByText(/Reservations are off/)).toBeInTheDocument();
    expect(screen.getByText('Overlapping missions are warned only.')).toBeInTheDocument();
    view.unmount();
    vi.mocked(listVesselReservations).mockRejectedValueOnce(new Error('nope'));
    renderIn(<VesselReservationsCard vesselId="vsl_2" />);
    expect(await screen.findByText('Reservations could not be loaded: nope')).toBeInTheDocument();
  });
});

function doctor(objects: BlackBoxDoctorPage['objects']): BlackBoxDoctorPage {
  return { ...page(objects), vesselId: 'vsl_1', windowDays: 30, entriesInWindow: 12, entriesScanned: 12, scanTruncated: false };
}

describe('VesselBlackBoxDoctorCard', () => {
  const FINDINGS: BlackBoxDoctorPage['objects'] = [
    { key: 'a', ruleId: 'BBX-LAND-001', recommendationId: 'rec_1', setting: 'LandingMode', currentValue: 'LocalMerge', suggestedValue: 'PullRequest', rationale: 'Conflicts on main', count: 6, firstSeenUtc: '2026-10-01T00:00:00Z', lastSeenUtc: '2026-10-28T00:00:00Z', exampleEntryId: 'bbx_7', exampleMissionId: 'msn_7', applicable: true },
    { key: 'b', ruleId: 'BBX-SCOPE-002', recommendationId: 'rec_2', setting: 'ProtectedPathPatterns', count: 2, firstSeenUtc: '2026-10-02T00:00:00Z', lastSeenUtc: '2026-10-20T00:00:00Z', exampleEntryId: 'bbx_8', applicable: false },
  ];

  it('lists recurring recommendations with example links, and Apply only where there is an action', async () => {
    vi.mocked(getVesselBlackBoxDoctor).mockResolvedValue(doctor(FINDINGS));
    vi.mocked(applyBlackBoxRecommendation).mockResolvedValue({ dryRun: true, applied: false, steps: [], confirmToken: 'tok', expiresUtc: null, message: 'Would set LandingMode' } as never);
    renderIn(<VesselBlackBoxDoctorCard vesselId="vsl_1" />);
    expect(await screen.findByText('LandingMode')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'bbx_7' })).toHaveAttribute('href', '/blackbox/bbx_7');
    expect(screen.getByRole('link', { name: 'msn_7' })).toHaveAttribute('href', '/missions/msn_7');
    expect(screen.getByText('12 of 12 entries in the window were read.')).toBeInTheDocument();
    const applyButtons = screen.getAllByRole('button', { name: 'Apply...' });
    expect(applyButtons).toHaveLength(1);
    fireEvent.click(applyButtons[0]);
    await waitFor(() => expect(applyBlackBoxRecommendation).toHaveBeenCalledWith('bbx_7', 'rec_1', expect.objectContaining({ dryRun: true })));
  });

  it('re-queries with another window', async () => {
    vi.mocked(getVesselBlackBoxDoctor).mockResolvedValue(doctor(FINDINGS));
    renderIn(<VesselBlackBoxDoctorCard vesselId="vsl_1" />);
    await screen.findByText('LandingMode');
    fireEvent.change(screen.getByLabelText('Doctor window'), { target: { value: '90' } });
    await waitFor(() => expect(getVesselBlackBoxDoctor).toHaveBeenLastCalledWith('vsl_1', { windowDays: 90, pageNumber: 1, pageSize: 10 }));
  });

  it('shows the empty and error states', async () => {
    vi.mocked(getVesselBlackBoxDoctor).mockResolvedValueOnce(doctor([]));
    const view = renderIn(<VesselBlackBoxDoctorCard vesselId="vsl_1" />);
    expect(await screen.findByText('No recurring recommendations')).toBeInTheDocument();
    view.unmount();
    vi.mocked(getVesselBlackBoxDoctor).mockRejectedValueOnce(new Error('window out of range'));
    renderIn(<VesselBlackBoxDoctorCard vesselId="vsl_2" />);
    expect(await screen.findByText('The doctor could not be loaded: window out of range')).toBeInTheDocument();
  });
});

describe('BlackBoxRecoveryStatsTab', () => {
  it('lists rates per rule and path, and filters by rule', async () => {
    vi.mocked(getBlackBoxRecoveryStats).mockResolvedValue(page([
      { ruleId: 'BBX-LAND-001', pathId: 'bbx-land-001.pull-request', runs: 8, succeeded: 6, failed: 2, pending: 0, expired: 0, successRate: 0.75, lastRunUtc: '2026-10-28T00:00:00Z' },
      { ruleId: 'BBX-DOCK-003', pathId: 'bbx-dock-003.repair', runs: 1, succeeded: 0, failed: 0, pending: 1, expired: 0, successRate: null },
    ]));
    renderIn(<BlackBoxRecoveryStatsTab vessels={[{ id: 'vsl_1', name: 'Core' } as never]} />);
    expect(await screen.findByText('bbx-land-001.pull-request')).toBeInTheDocument();
    expect(screen.getByLabelText('75% success')).toBeInTheDocument();
    expect(screen.getByTitle('No run of this path has settled yet.')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'BBX-LAND-001' }));
    await waitFor(() => expect(getBlackBoxRecoveryStats).toHaveBeenLastCalledWith({ vesselId: undefined, ruleId: 'BBX-LAND-001', pageNumber: 1, pageSize: 25 }));
    fireEvent.change(screen.getByLabelText('Vessel'), { target: { value: 'vsl_1' } });
    await waitFor(() => expect(getBlackBoxRecoveryStats).toHaveBeenLastCalledWith({ vesselId: 'vsl_1', ruleId: 'BBX-LAND-001', pageNumber: 1, pageSize: 25 }));
    fireEvent.click(screen.getByRole('button', { name: 'Clear' }));
    await waitFor(() => expect(getBlackBoxRecoveryStats).toHaveBeenLastCalledWith({ vesselId: 'vsl_1', ruleId: undefined, pageNumber: 1, pageSize: 25 }));
  });

  it('shows the empty and error states', async () => {
    vi.mocked(getBlackBoxRecoveryStats).mockResolvedValueOnce(page([]));
    const view = renderIn(<BlackBoxRecoveryStatsTab vessels={[]} />);
    expect(await screen.findByText('No recovery runs yet')).toBeInTheDocument();
    view.unmount();
    vi.mocked(getBlackBoxRecoveryStats).mockRejectedValueOnce(new Error('forbidden'));
    await act(async () => { renderIn(<BlackBoxRecoveryStatsTab vessels={[]} />); });
    expect(await screen.findByText('Recovery statistics could not be loaded: forbidden')).toBeInTheDocument();
  });

  it('turns a rate into a percent', () => {
    expect(recoveryRatePercent(0.666)).toBe(67);
    expect(recoveryRatePercent(null)).toBeNull();
    expect(recoveryRatePercent(1.4)).toBe(100);
  });
});

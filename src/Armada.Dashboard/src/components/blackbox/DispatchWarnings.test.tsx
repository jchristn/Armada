import { act, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import DispatchPreview, { DispatchWarningList, DispatchWarningsBanner } from './DispatchWarnings';
import { lintMissionBrief, previewMissionOverlap } from '../../api/client';
import { translateTemplate } from '../../i18n/runtime';
import { dispatchWarningsFrom, dispatchWarningsFromState } from '../../lib/dispatchPreview';
import type { MissionOverlapPreview, MissionOverlapWarning } from '../../types/blackbox';

vi.mock('../../api/client', () => ({ previewMissionOverlap: vi.fn(), lintMissionBrief: vi.fn() }));
const localeValue = {
  t: (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', text, null, params),
  locale: 'en',
  formatDateTime: (v: string | null | undefined) => v ?? '',
};
vi.mock('../../context/LocaleContext', () => ({ useLocale: () => localeValue }));

const ACTIVE: MissionOverlapWarning = {
  kind: 'ActiveMission', overlappingMissionId: 'msn_2', overlappingMissionTitle: 'Refactor parser', overlappingMissionStatus: 'InProgress',
  files: ['src/a.ts', 'src/b.ts', 'src/c.ts', 'src/d.ts', 'src/e.ts', 'src/f.ts'], fileCount: 8, message: 'Shares 8 files with msn_2', serialized: true,
};
const HOTSPOT: MissionOverlapWarning = { kind: 'Hotspot', directory: 'src/landing', hotspotEntryCount: 4, message: 'src/landing is a conflict hotspot' };

function preview(overrides: Partial<MissionOverlapPreview> = {}): MissionOverlapPreview {
  return {
    vesselId: 'vsl_1', serializeOverlappingMissions: false, likelyFiles: ['src/a.ts'], likelyFileCount: 1, scopedFileCount: 1, briefPathCount: 0,
    trackedFilesKnown: true, warnings: [], warningCount: 0, activeMissionsCompared: 3, activeMissionsTruncated: false, briefLint: { findings: [], findingCount: 0, clean: true, pathsChecked: true },
    ...overrides,
  };
}

function renderIn(node: React.ReactNode) {
  return render(<MemoryRouter>{node}</MemoryRouter>);
}

describe('DispatchWarningList', () => {
  it('renders overlap warnings with kind, chained marker, mission link and a cut file list', () => {
    renderIn(<DispatchWarningList overlapWarnings={[ACTIVE, HOTSPOT]} overlapTotal={7} suggestedDependsOnMissionId="msn_2" />);
    expect(screen.getByText('Overlap warnings')).toBeInTheDocument();
    expect(screen.getByText('(2 of 7)')).toBeInTheDocument();
    expect(screen.getByText('Active mission')).toBeInTheDocument();
    expect(screen.getByText('Conflict hotspot')).toBeInTheDocument();
    expect(screen.getByText('Chained')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Refactor parser' })).toHaveAttribute('href', '/missions/msn_2');
    expect(screen.getByText(/and 3 more/)).toBeInTheDocument();
    expect(screen.getByText(/src\/landing · 4 black box entries/)).toBeInTheDocument();
    expect(screen.getByText(/Consider making this mission depend on msn_2/)).toBeInTheDocument();
  });

  it('renders brief lint findings with rule id, excerpt and suggestion', () => {
    renderIn(<DispatchWarningList briefLintWarnings={[{ ruleId: 'BRIEF-NOOP-001', severity: 'Warning', message: 'Verification only', excerpt: 'verify the build', suggestion: 'Dispatch it as Audit' }]} />);
    expect(screen.getByText('Brief warnings')).toBeInTheDocument();
    expect(screen.getByText('BRIEF-NOOP-001')).toBeInTheDocument();
    expect(screen.getByText('verify the build')).toBeInTheDocument();
    expect(screen.getByText(/Dispatch it as Audit/)).toBeInTheDocument();
    expect(screen.queryByText('Overlap warnings')).not.toBeInTheDocument();
  });

  it('renders nothing without warnings', () => {
    const { container } = renderIn(<DispatchWarningList overlapWarnings={[]} briefLintWarnings={null} />);
    expect(container).toBeEmptyDOMElement();
    const banner = renderIn(<DispatchWarningsBanner overlapWarnings={[]} briefLintWarnings={[]} />);
    expect(banner.container).toBeEmptyDOMElement();
  });

  it('says the vessel serializes when it chains automatically', () => {
    renderIn(<DispatchWarningList overlapWarnings={[ACTIVE]} suggestedDependsOnMissionId="msn_2" serializeOverlappingMissions />);
    expect(screen.getByText(/dispatch will chain this mission after msn_2/)).toBeInTheDocument();
  });
});

describe('dispatch warnings helpers', () => {
  it('reads warnings from a create response and from navigation state', () => {
    expect(dispatchWarningsFrom({ overlapWarnings: null, briefLintWarnings: [] })).toBeNull();
    expect(dispatchWarningsFrom(undefined)).toBeNull();
    const w = dispatchWarningsFrom({ overlapWarnings: [HOTSPOT] });
    expect(w?.overlapWarnings).toHaveLength(1);
    expect(w?.briefLintWarnings).toEqual([]);
    expect(dispatchWarningsFromState({ dispatchWarnings: w })?.overlapWarnings[0].kind).toBe('Hotspot');
    expect(dispatchWarningsFromState(null)).toBeNull();
    expect(dispatchWarningsFromState('x')).toBeNull();
  });
});

describe('DispatchPreview', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.mocked(previewMissionOverlap).mockReset();
    vi.mocked(lintMissionBrief).mockReset();
  });
  afterEach(() => vi.useRealTimers());

  it('stays idle and sends nothing while the brief is empty', async () => {
    const { container } = renderIn(<DispatchPreview vesselId="vsl_1" title="" description="  " />);
    await act(async () => { vi.advanceTimersByTime(2000); });
    expect(container).toBeEmptyDOMElement();
    expect(previewMissionOverlap).not.toHaveBeenCalled();
    expect(lintMissionBrief).not.toHaveBeenCalled();
  });

  it('debounces typing and previews the latest brief against the vessel', async () => {
    vi.mocked(previewMissionOverlap).mockResolvedValue(preview({ warnings: [ACTIVE], warningCount: 1, suggestedDependsOnMissionId: 'msn_2' }));
    const view = renderIn(<DispatchPreview vesselId="vsl_1" title="T" description="a" />);
    view.rerender(<MemoryRouter><DispatchPreview vesselId="vsl_1" title="T" description="ab" /></MemoryRouter>);
    expect(screen.getByText('Checking for overlaps...')).toBeInTheDocument();
    await act(async () => { vi.advanceTimersByTime(700); });
    await act(async () => { await Promise.resolve(); });
    expect(previewMissionOverlap).toHaveBeenCalledTimes(1);
    expect(vi.mocked(previewMissionOverlap).mock.calls[0]).toEqual(['vsl_1', { title: 'T', description: 'ab', dependsOnMissionId: undefined, excludeMissionId: undefined }]);
    expect(screen.getByText('Shares 8 files with msn_2')).toBeInTheDocument();
    expect(screen.getByText(/1 likely files compared with 3 active missions/)).toBeInTheDocument();
  });

  it('lints only when no vessel is chosen, and reports a clean brief', async () => {
    vi.mocked(lintMissionBrief).mockResolvedValue({ findings: [], findingCount: 0, clean: true, pathsChecked: false });
    renderIn(<DispatchPreview vesselId="" title="T" description="Add retries to src/a.ts" />);
    await act(async () => { vi.advanceTimersByTime(700); });
    await act(async () => { await Promise.resolve(); });
    expect(previewMissionOverlap).not.toHaveBeenCalled();
    expect(lintMissionBrief).toHaveBeenCalledTimes(1);
    expect(screen.getByText('No brief warnings found. Choose a vessel to check for overlaps too.')).toBeInTheDocument();
  });

  it('reports no warnings for a clean preview', async () => {
    vi.mocked(previewMissionOverlap).mockResolvedValue(preview());
    renderIn(<DispatchPreview vesselId="vsl_1" title="T" description="D" />);
    await act(async () => { vi.advanceTimersByTime(700); });
    await act(async () => { await Promise.resolve(); });
    expect(screen.getByText('No overlaps or brief warnings found.')).toBeInTheDocument();
  });

  it('shows the error when the preview fails', async () => {
    vi.mocked(previewMissionOverlap).mockRejectedValue(new Error('Vessel not found'));
    renderIn(<DispatchPreview vesselId="vsl_x" title="T" description="D" />);
    await act(async () => { vi.advanceTimersByTime(700); });
    await act(async () => { await Promise.resolve(); });
    expect(screen.getByRole('alert')).toHaveTextContent('The preview is not available: Vessel not found');
  });

  it('sends nothing while disabled', async () => {
    renderIn(<DispatchPreview vesselId="vsl_1" title="T" description="D" enabled={false} />);
    await act(async () => { vi.advanceTimersByTime(2000); });
    expect(previewMissionOverlap).not.toHaveBeenCalled();
  });
});

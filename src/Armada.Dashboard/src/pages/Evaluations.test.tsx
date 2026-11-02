import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import catalogSource from '../../../Armada.Server/wwwroot/i18n/armada.json?raw';
import listSource from './Evaluations.tsx?raw';
import detailSource from './EvaluationDetail.tsx?raw';
import Evaluations, { EVALUATIONS_PAGE_SIZE } from './Evaluations';
import EvaluationDetail from './EvaluationDetail';
import { getModelProbeRun, listCaptains, listModelProbeRuns } from '../api/client';
import { translateTemplate } from '../i18n/runtime';
import type { I18nCatalog } from '../i18n/runtime';
import type { Captain, EnumerationResult, ModelProbeRun, ModelProbeRunWithBattery } from '../types/models';

vi.mock('../api/client', () => ({
  listModelProbeRuns: vi.fn(),
  listCaptains: vi.fn(),
  getModelProbeRun: vi.fn(),
}));

const localeValue = {
  t: (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', text, null, params),
  locale: 'en',
  formatDateTime: (v: string | null | undefined) => v ?? '',
  formatRelativeTime: (v: string | null | undefined) => v ?? '',
};

vi.mock('../context/LocaleContext', () => ({
  useLocale: () => localeValue,
}));

function run(overrides: Partial<ModelProbeRun> = {}): ModelProbeRun {
  return {
    id: 'mpr_1', targetKind: 'Captain', captainId: 'cpt_1', captainName: 'qwen-one', model: 'qwen3.8:27b', depth: 'Quick',
    status: 'Completed', isEvaluation: true, probeSetVersion: 'quick-1', tokenBudget: 1000, tokensUsed: 120,
    passedCount: 4, failedCount: 1, couldNotRunCount: 1, createdUtc: '2026-10-26T00:00:00Z', startedUtc: '2026-10-26T00:00:01Z',
    lastUpdateUtc: '2026-10-26T00:00:00Z', results: [],
    ...overrides,
  };
}

function page<T>(objects: T[], totalPages = 1): EnumerationResult<T> {
  return { success: true, pageNumber: 1, pageSize: objects.length, totalPages, totalRecords: objects.length, objects, totalMs: 1 } as EnumerationResult<T>;
}

function LocationProbe() {
  const location = useLocation();
  return <span data-testid="location">{location.pathname}{location.search}</span>;
}

function renderList(path = '/evaluations') {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/evaluations" element={<Evaluations />} />
        <Route path="/evaluations/:id" element={<span>detail page</span>} />
      </Routes>
      <LocationProbe />
    </MemoryRouter>,
  );
}

function renderDetail(id = 'mpr_1') {
  return render(
    <MemoryRouter initialEntries={[`/evaluations/${id}`]}>
      <Routes>
        <Route path="/evaluations/:id" element={<EvaluationDetail pollMs={5} />} />
      </Routes>
    </MemoryRouter>,
  );
}

describe('Evaluations list', () => {
  beforeEach(() => {
    vi.mocked(listModelProbeRuns).mockReset().mockResolvedValue(page([
      run(),
      run({ id: 'mpr_2', targetKind: 'ModelEndpoint', captainId: null, captainName: null, modelEndpointId: 'mep_1', model: 'llama', depth: 'Full', status: 'Running', passedCount: 2, failedCount: 0, couldNotRunCount: 3 }),
    ]));
    vi.mocked(listCaptains).mockReset().mockResolvedValue(page([
      { id: 'cpt_1', name: 'qwen-one' } as Captain,
      { id: 'cpt_2', name: 'claude-two' } as Captain,
    ]));
  });

  it('lists runs with started, target, model, depth, status and counts, each linking to its results page', async () => {
    renderList();
    const table = await screen.findByRole('table', { name: 'Evaluations' });
    expect(listModelProbeRuns).toHaveBeenCalledWith({ captainId: null, pageNumber: 1, pageSize: EVALUATIONS_PAGE_SIZE });
    const headers = within(table).getAllByRole('columnheader').map((h) => h.textContent);
    expect(headers).toEqual(['Started', 'Captain / endpoint', 'Model', 'Depth', 'Status', 'Passed', 'Failed', 'Could not run', 'View results']);
    const rows = within(table).getAllByRole('row').slice(1);
    expect(rows).toHaveLength(2);
    expect(within(rows[0]).getByRole('link', { name: '2026-10-26T00:00:01Z' })).toHaveAttribute('href', '/evaluations/mpr_1');
    expect(within(rows[0]).getByRole('link', { name: 'qwen-one' })).toHaveAttribute('href', '/captains/cpt_1');
    expect(rows[0]).toHaveTextContent('qwen3.8:27b');
    expect(rows[0]).toHaveTextContent('Quick');
    expect(rows[1]).toHaveTextContent('mep_1');
    expect(rows[1]).toHaveTextContent('Full');
    expect(within(rows[0]).getByRole('link', { name: 'View results' })).toHaveAttribute('href', '/evaluations/mpr_1');
    expect(within(rows[1]).getByRole('link', { name: 'View results' })).toHaveAttribute('href', '/evaluations/mpr_2');
    expect(within(rows[1]).getAllByRole('cell').slice(5, 8).map((c) => c.textContent)).toEqual(['2', '0', '3']);
  });

  it('filters by the captainId in the URL and changes the filter from the captain select', async () => {
    renderList('/evaluations?captainId=cpt_1');
    await screen.findByRole('table', { name: 'Evaluations' });
    expect(listModelProbeRuns).toHaveBeenLastCalledWith({ captainId: 'cpt_1', pageNumber: 1, pageSize: EVALUATIONS_PAGE_SIZE });
    const select = screen.getByLabelText('Captain');
    await waitFor(() => expect(within(select).getByRole('option', { name: 'claude-two' })).toBeInTheDocument());
    expect(select).toHaveValue('cpt_1');
    await userEvent.selectOptions(select, 'cpt_2');
    await waitFor(() => expect(listModelProbeRuns).toHaveBeenLastCalledWith({ captainId: 'cpt_2', pageNumber: 1, pageSize: EVALUATIONS_PAGE_SIZE }));
    expect(screen.getByTestId('location')).toHaveTextContent('/evaluations?captainId=cpt_2');
    await userEvent.selectOptions(select, '');
    await waitFor(() => expect(listModelProbeRuns).toHaveBeenLastCalledWith({ captainId: null, pageNumber: 1, pageSize: EVALUATIONS_PAGE_SIZE }));
  });

  it('keeps an unknown captain id selectable when the captain list fails', async () => {
    vi.mocked(listCaptains).mockRejectedValue(new Error('forbidden'));
    renderList('/evaluations?captainId=cpt_gone');
    await screen.findByRole('table', { name: 'Evaluations' });
    expect(screen.getByLabelText('Captain')).toHaveValue('cpt_gone');
  });

  it('shows the empty state and a load error', async () => {
    vi.mocked(listModelProbeRuns).mockResolvedValueOnce(page([]));
    const view = renderList();
    expect(await screen.findByText(/No evaluations yet/)).toBeInTheDocument();
    view.unmount();
    vi.mocked(listModelProbeRuns).mockRejectedValueOnce(new Error('List unavailable'));
    renderList();
    expect(await screen.findByRole('alert')).toHaveTextContent('List unavailable');
    expect(screen.queryByText(/No evaluations yet/)).not.toBeInTheDocument();
  });

  it('pages through runs', async () => {
    vi.mocked(listModelProbeRuns).mockResolvedValue(page([run()], 3));
    renderList();
    await screen.findByText('Page 1 of 3');
    await userEvent.click(screen.getByRole('button', { name: 'Next' }));
    await waitFor(() => expect(listModelProbeRuns).toHaveBeenLastCalledWith({ captainId: null, pageNumber: 2, pageSize: EVALUATIONS_PAGE_SIZE }));
    expect(await screen.findByText('Page 2 of 3')).toBeInTheDocument();
  });
});

const FULL: ModelProbeRunWithBattery = run({
  depth: 'Full', batteryRunId: 'bat_1',
  results: [{ id: 'r1', runId: 'mpr_1', probe: 'AsciiFormat', outcome: 'Failed', summary: '2 em dashes', attempts: 1, tokensUsed: 10, durationMs: 5, createdUtc: '' }],
  batteryProgress: { status: 'Completed', totalTasks: 2, passedCount: 1, failedCount: 1, couldNotRunCount: 0, pendingEvaluationCount: 0 },
  report: {
    runId: 'mpr_1', complete: true, batteryTokensUsed: 10, packs: ['csharp'], personas: ['Worker'], evaluatorCaptainName: 'claude-premium',
    evaluatorExplicit: false, warnings: [], notes: [], probes: [], createdUtc: '',
    personaOutcomes: [{ persona: 'Worker', totals: { key: 'w', label: 'Worker', passed: 1, failed: 1, couldNotRun: 0, pendingEvaluation: 0, graded: 2 }, tasks: [] }],
    narrative: { status: 'Accepted', strengths: [{ text: 'Follows the output contract', key: 'k', passed: 1, total: 1 }], weaknesses: [], observedProblems: [], subjectiveGrades: [] },
  },
});

describe('Evaluation detail', () => {
  beforeEach(() => {
    vi.mocked(getModelProbeRun).mockReset();
  });

  it('shows probe results, the Full report and battery results with mission, voyage and captain links', async () => {
    vi.mocked(getModelProbeRun).mockResolvedValue({
      ...FULL,
      batteryResults: [
        { id: 'btr_1', runId: 'bat_1', packId: 'csharp', packVersion: 1, taskId: 't1', taskTitle: 'Fix the parser', persona: 'Worker', attempt: 1, outcome: 'Passed', summary: 'tests pass', missionId: 'msn_1', voyageId: 'vyg_1', durationMs: 1, createdUtc: '' },
        { id: 'btr_2', runId: 'bat_1', packId: 'csharp', packVersion: 1, taskId: 't2', attempt: 1, outcome: 'PendingEvaluation', durationMs: 1, createdUtc: '' },
      ],
    });
    renderDetail();
    expect(await screen.findByRole('heading', { name: 'Evaluation mpr_1' })).toBeInTheDocument();
    expect(getModelProbeRun).toHaveBeenCalledWith('mpr_1', true, true, 1);
    expect(screen.getByTestId('evaluation-summary')).toHaveTextContent('4 passed, 1 failed, 1 could not run');
    for (const link of screen.getAllByRole('link', { name: 'qwen-one' })) expect(link).toHaveAttribute('href', '/captains/cpt_1');
    expect(screen.getByRole('link', { name: 'Captains' })).toHaveAttribute('href', '/captains');
    expect(screen.getByRole('link', { name: 'All evaluations of this captain' })).toHaveAttribute('href', '/evaluations?captainId=cpt_1');
    expect(screen.getByRole('link', { name: 'Evaluations' })).toHaveAttribute('href', '/evaluations?captainId=cpt_1');
    const probes = screen.getByRole('table', { name: 'Probe results' });
    expect(within(probes).getAllByRole('columnheader').map(header => header.textContent)).toEqual(['Probe type', 'Pass/fail', 'Details']);
    const probeRow = within(probes).getAllByRole('row')[1];
    expect(within(probeRow).getByRole('rowheader')).toHaveTextContent('ASCII and format');
    expect(within(probeRow).getAllByRole('cell')[0]).toHaveTextContent('Failed');
    expect(within(probeRow).getAllByRole('cell')[1]).toHaveTextContent('2 em dashes');
    expect(screen.getByTestId('model-evaluation-report')).toHaveTextContent('Follows the output contract');
    expect(screen.getByTestId('evaluation-battery-summary')).toHaveTextContent('1 passed, 1 failed, 0 could not run, 0 pending of 2 tasks');
    const table = screen.getByRole('table', { name: 'Battery results' });
    const rows = within(table).getAllByRole('row').slice(1);
    expect(within(rows[0]).getByRole('link', { name: 'msn_1' })).toHaveAttribute('href', '/missions/msn_1');
    expect(within(rows[0]).getByRole('link', { name: 'vyg_1' })).toHaveAttribute('href', '/voyages/vyg_1');
    expect(rows[0]).toHaveTextContent('Fix the parser');
    expect(rows[1]).toHaveTextContent('Pending evaluation');
    expect(within(rows[1]).queryByRole('link')).not.toBeInTheDocument();
  });

  it('shows passed, failed and could-not-run probes with summaries and reasons in three columns', async () => {
    vi.mocked(getModelProbeRun).mockResolvedValue({ ...FULL, results: [
      { ...FULL.results![0], id: 'pass', outcome: 'Passed', summary: 'Contract followed', reason: null },
      { ...FULL.results![0], id: 'fail', outcome: 'Failed', summary: 'Invalid response', reason: 'Expected JSON' },
      { ...FULL.results![0], id: 'skip', outcome: 'CouldNotRun', summary: null, reason: 'Runtime unavailable' },
    ] });
    renderDetail();
    const table = await screen.findByRole('table', { name: 'Probe results' });
    const rows = within(table).getAllByRole('row').slice(1);
    expect(rows).toHaveLength(3);
    for (const row of rows) {
      expect(within(row).getAllByRole('rowheader')).toHaveLength(1);
      expect(within(row).getAllByRole('cell')).toHaveLength(2);
    }
    expect(within(rows[0]).getAllByRole('cell')[0]).toHaveTextContent('Passed');
    expect(within(rows[1]).getAllByRole('cell')[1]).toHaveTextContent('Invalid response');
    expect(within(rows[1]).getAllByRole('cell')[1]).toHaveTextContent('Expected JSON');
    expect(within(rows[2]).getAllByRole('cell')[0]).toHaveTextContent('Could not run');
    expect(within(rows[2]).getAllByRole('cell')[1]).toHaveTextContent('Runtime unavailable');
  });

  it('renders a Quick run without the Full sections and a model endpoint target without a captain link', async () => {
    vi.mocked(getModelProbeRun).mockResolvedValue(run({ targetKind: 'ModelEndpoint', captainId: null, captainName: null, modelEndpointId: 'mep_1' }));
    renderDetail();
    expect(await screen.findByText('mep_1')).toBeInTheDocument();
    expect(screen.getByText('No probe results were recorded.')).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Report' })).not.toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Battery results' })).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Evaluations' })).toHaveAttribute('href', '/evaluations');
  });

  it('keeps the run visible when the battery results cannot be read', async () => {
    vi.mocked(getModelProbeRun).mockResolvedValue({ ...FULL, batteryResultsError: 'Battery run not found.' });
    renderDetail();
    expect(await screen.findByText('Battery run not found.')).toBeInTheDocument();
    expect(screen.getByText('ASCII and format')).toBeInTheDocument();
  });

  it('refreshes a running evaluation and continues refreshing the completed results', async () => {
    vi.mocked(getModelProbeRun)
      .mockResolvedValueOnce(run({ depth: 'Full', status: 'Running', passedCount: 0, failedCount: 0, couldNotRunCount: 0 }))
      .mockResolvedValue(FULL);
    renderDetail();
    expect(await screen.findByText('The battery has not started yet.')).toBeInTheDocument();
    expect(screen.getByText('The report is written when the battery and grading finish.')).toBeInTheDocument();
    await waitFor(() => expect(screen.getByTestId('model-evaluation-report')).toBeInTheDocument());
    const calls = vi.mocked(getModelProbeRun).mock.calls.length;
    await new Promise((resolve) => setTimeout(resolve, 30));
    expect(vi.mocked(getModelProbeRun).mock.calls.length).toBeGreaterThan(calls);
  });

  it('pages results with boundaries and renders safe collapsible output and formatted details', async () => {
    const user = userEvent.setup();
    const result = { id: 'task_1', runId: 'bat_1', packId: 'pack', packVersion: 1, taskId: 'task', attempt: 1, outcome: 'Passed', durationMs: 1, createdUtc: '', output: '<b>raw output</b>', details: { score: 7 } };
    vi.mocked(getModelProbeRun).mockImplementation(async (_id, _details, _results, number) => ({
      ...FULL, batteryResults: number === 2 ? [] : [result], batteryResultsPage: page([], 2),
    }));
    renderDetail();
    expect(await screen.findByText('Page 1 of 2')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Previous' })).toBeDisabled();
    expect(screen.getByText(/"score": 7/).textContent).toBe('{\n  "score": 7\n}');
    const output = screen.getByText('Output');
    expect(output.parentElement).not.toHaveAttribute('open');
    await user.click(output);
    expect(output.parentElement).toHaveAttribute('open');
    expect(screen.getByText('<b>raw output</b>').tagName).toBe('PRE');
    await user.click(output);
    expect(output.parentElement).not.toHaveAttribute('open');
    await user.click(screen.getByRole('button', { name: 'Next' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Next' })).toBeDisabled());
    expect(screen.getByText('Page 2 of 2')).toBeInTheDocument();
    expect(screen.queryByText('Output')).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Previous' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Previous' })).toBeDisabled());
    expect(getModelProbeRun).toHaveBeenLastCalledWith('mpr_1', true, true, 1);
  });

  it('disables both controls for empty results and omits empty task details and output', async () => {
    vi.mocked(getModelProbeRun).mockResolvedValue({ ...FULL, batteryResults: [], batteryResultsPage: page([], 0) });
    const view = renderDetail();
    await screen.findByText('Page 1 of 1');
    expect(screen.getByRole('button', { name: 'Previous' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Next' })).toBeDisabled();
    expect(screen.queryByRole('table', { name: 'Battery results' })).not.toBeInTheDocument();
    view.unmount();
    vi.mocked(getModelProbeRun).mockResolvedValue({
      ...FULL, batteryResultsPage: page([], 1),
      batteryResults: [{ id: 'empty', runId: 'bat_1', packId: 'pack', packVersion: 1, taskId: 'task', attempt: 1, outcome: 'Passed', durationMs: 0, createdUtc: '', details: {}, output: '' }],
    });
    renderDetail();
    await screen.findByRole('table', { name: 'Battery results' });
    expect(screen.queryByText('Output')).not.toBeInTheDocument();
    expect(document.querySelector('td pre')).toBeNull();
    expect(screen.getByRole('button', { name: 'Next' })).toBeDisabled();
  });

  it('preserves the selected page while polling and ignores an old page response', async () => {
    const user = userEvent.setup();
    let resolveOld!: (value: ModelProbeRunWithBattery) => void;
    const oldResponse = new Promise<ModelProbeRunWithBattery>(resolve => { resolveOld = resolve; });
    let firstPageCalls = 0;
    let secondPageCalls = 0;
    vi.mocked(getModelProbeRun).mockImplementation(async (_id, _details, _results, number) => {
      if (number === 1) {
        firstPageCalls++;
        if (firstPageCalls > 1) return oldResponse;
      } else {
        secondPageCalls++;
      }
      return { ...FULL, status: 'Running', batteryResultsPage: page([], 3) };
    });
    renderDetail();
    await screen.findByText('Page 1 of 3');
    await waitFor(() => expect(firstPageCalls).toBe(2));
    await user.click(screen.getByRole('button', { name: 'Next' }));
    await waitFor(() => expect(secondPageCalls).toBeGreaterThan(1));
    await act(async () => { resolveOld({ ...FULL, batteryResultsError: 'stale response' }); });
    expect(screen.queryByText('stale response')).not.toBeInTheDocument();
    expect(screen.getByText('Page 2 of 3')).toBeInTheDocument();
    expect(getModelProbeRun).toHaveBeenLastCalledWith('mpr_1', true, true, 2);
  });

  it('retries after a transient refresh failure and retains the last results', async () => {
    vi.mocked(getModelProbeRun).mockResolvedValueOnce(FULL)
      .mockRejectedValueOnce(new Error('Temporary connection error'))
      .mockResolvedValue({ ...FULL, passedCount: 9 });
    renderDetail();
    await screen.findByText('Temporary connection error');
    expect(screen.getByTestId('model-evaluation-report')).toBeInTheDocument();
    await waitFor(() => expect(screen.getByTestId('evaluation-summary')).toHaveTextContent('9 passed'));
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('supports a persisted refresh interval, disabling refresh and manually reloading', async () => {
    localStorage.setItem('armada_autorefresh_evaluation-detail', '30');
    vi.mocked(getModelProbeRun).mockResolvedValue(FULL);
    const view = render(<MemoryRouter initialEntries={['/evaluations/mpr_1']}>
      <Routes><Route path="/evaluations/:id" element={<EvaluationDetail />} /></Routes>
    </MemoryRouter>);
    await screen.findByTestId('evaluation-summary');
    expect(screen.getByRole('combobox', { name: 'Auto-refresh interval' })).toHaveValue('30');
    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Auto-refresh interval' }), '0');
    expect(localStorage.getItem('armada_autorefresh_evaluation-detail')).toBe('0');
    const calls = vi.mocked(getModelProbeRun).mock.calls.length;
    vi.mocked(getModelProbeRun).mockResolvedValue({ ...FULL, passedCount: 8 });
    await userEvent.click(screen.getByRole('button', { name: 'Refresh' }));
    await waitFor(() => expect(screen.getByTestId('evaluation-summary')).toHaveTextContent('8 passed'));
    expect(getModelProbeRun).toHaveBeenCalledTimes(calls + 1);
    view.unmount();
    localStorage.clear();
  });

  it('shows a load error for an unknown run', async () => {
    vi.mocked(getModelProbeRun).mockRejectedValue(new Error('Model probe run not found.'));
    renderDetail('mpr_missing');
    expect(await screen.findByRole('alert')).toHaveTextContent('Model probe run not found.');
  });
});

describe('Evaluations i18n catalog', () => {
  const S = "'((?:[^'\\\\]|\\\\.)*)'";
  const call = new RegExp(`\\bt\\(\\s*${S}`, 'g');
  const literal = new RegExp(`^\\s*\\w+:\\s*${S},?$`, 'gm');
  const header = /\[((?:'[^']*',?\s*)+)\]\.map\(\(label\)/g;
  const keys = new Set<string>();
  for (const source of [listSource, detailSource]) {
    for (const m of source.matchAll(call)) keys.add(m[1].replace(/\\'/g, "'"));
    for (const m of source.matchAll(literal)) keys.add(m[1]);
    for (const m of source.matchAll(header)) for (const k of m[1].matchAll(/'([^']*)'/g)) keys.add(k[1]);
  }
  for (const key of ['Evaluations', 'Quick', 'Full', 'View results', 'Evaluation started.']) keys.add(key);
  const catalog = JSON.parse(catalogSource) as I18nCatalog;

  it('finds the strings, including table headers', () => {
    expect(keys.has('Captain / endpoint')).toBe(true);
    expect(keys.has('Pending evaluation')).toBe(true);
    expect(keys.has('Mission')).toBe(true);
  });

  it('has a translation for every string in every non-English locale', () => {
    const missing: string[] = [];
    for (const meta of catalog.supportedLocales) {
      if (meta.code === catalog.defaultLocale) continue;
      const pack = catalog.locales[meta.code] ?? {};
      for (const key of keys) {
        if (!pack.phrases?.[key] && !pack.terms?.[key] && !pack.sections?.[key]) missing.push(`${meta.code}: ${key}`);
      }
    }
    expect(missing).toEqual([]);
  });

  it('keeps placeholders intact', () => {
    const bad: string[] = [];
    for (const meta of catalog.supportedLocales) {
      if (meta.code === catalog.defaultLocale) continue;
      const pack = catalog.locales[meta.code] ?? {};
      for (const key of keys) {
        const value = pack.phrases?.[key] ?? pack.terms?.[key] ?? pack.sections?.[key];
        if (!value) continue;
        for (const placeholder of key.match(/\{\{\w+\}\}/g) ?? []) if (!value.includes(placeholder)) bad.push(`${meta.code}: ${key}`);
      }
    }
    expect(bad).toEqual([]);
  });
});

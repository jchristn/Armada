import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import catalogSource from '../../../Armada.Server/wwwroot/i18n/armada.json?raw';
import componentSource from './ModelProbeEvaluate.tsx?raw';
import captainDetailSource from '../pages/CaptainDetail.tsx?raw';
import endpointsSource from '../pages/Endpoints.tsx?raw';
import ModelProbeEvaluate from './ModelProbeEvaluate';
import { cancelModelProbeRun, evaluateCaptain, evaluateModelEndpoint, getModelProbeRun } from '../api/client';
import { translateTemplate } from '../i18n/runtime';
import type { I18nCatalog } from '../i18n/runtime';
import type { ModelProbeRun } from '../types/models';

vi.mock('../api/client', () => ({
  cancelModelProbeRun: vi.fn(),
  evaluateCaptain: vi.fn(),
  evaluateModelEndpoint: vi.fn(),
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
    id: 'mpr_1', targetKind: 'Captain', captainId: 'cpt_1', depth: 'Quick', status: 'Queued', isEvaluation: true,
    probeSetVersion: 'quick-1', tokenBudget: 1000, tokensUsed: 0, passedCount: 0, failedCount: 0, couldNotRunCount: 0,
    createdUtc: '2026-10-26T00:00:00Z', lastUpdateUtc: '2026-10-26T00:00:00Z', results: [],
    ...overrides,
  };
}

const COMPLETED = run({
  status: 'Completed', passedCount: 4, failedCount: 1, couldNotRunCount: 1,
  results: [
    { id: 'r1', runId: 'mpr_1', probe: 'AsciiFormat', outcome: 'Failed', summary: '2 em dashes', attempts: 1, tokensUsed: 10, durationMs: 5, createdUtc: '' },
    { id: 'r2', runId: 'mpr_1', probe: 'ServedContext', outcome: 'CouldNotRun', reason: 'vendor CLI hides the served context', attempts: 0, tokensUsed: 0, durationMs: 0, createdUtc: '' },
  ],
});

const FULL = run({
  depth: 'Full', status: 'Completed', passedCount: 5, failedCount: 1,
  report: {
    runId: 'mpr_1', complete: true, batteryTokensUsed: 1000, packs: ['csharp'], personas: ['Worker'],
    evaluatorCaptainId: 'cpt_eval', evaluatorCaptainName: 'claude-premium', evaluatorExplicit: false,
    warnings: ['The evaluator is of the same model family (qwen) as the candidate.'], notes: [], probes: [],
    personaOutcomes: [{
      persona: 'Worker',
      totals: { key: 'persona:Worker', label: 'Worker', passed: 3, failed: 3, couldNotRun: 1, pendingEvaluation: 0, graded: 6 },
      tasks: [],
    }],
    narrative: {
      status: 'Accepted',
      strengths: [{ key: 'task:csharp/add-member', text: 'Strong: single-file Worker edits (3 of 3).', passed: 3, total: 3 }],
      weaknesses: [{ key: 'task:csharp/two-place', text: 'Weak: coordinated two-place edits (0 of 3).', passed: 0, total: 3 }],
      observedProblems: ['Forgets to register the test.'], howToWork: 'One objective per mission.', subjectiveGrades: [],
    },
    createdUtc: '2026-10-30T00:00:00Z',
  },
});

describe('ModelProbeEvaluate', () => {
  beforeEach(() => {
    vi.mocked(evaluateCaptain).mockReset();
    vi.mocked(evaluateModelEndpoint).mockReset();
    vi.mocked(getModelProbeRun).mockReset();
    vi.mocked(cancelModelProbeRun).mockReset();
  });

  it('starts a Quick run, disables the button while polling, and shows the finished counts and probes', async () => {
    vi.mocked(evaluateCaptain).mockResolvedValue(run());
    vi.mocked(getModelProbeRun).mockResolvedValueOnce(run({ status: 'Running', passedCount: 2 })).mockResolvedValue(COMPLETED);
    render(<ModelProbeEvaluate target="captain" targetId="cpt_1" pollMs={5} />);

    await userEvent.click(screen.getByRole('button', { name: 'Evaluate' }));
    expect(evaluateCaptain).toHaveBeenCalledWith('cpt_1', 'Quick');
    expect(screen.getByRole('button', { name: 'Evaluating...' })).toBeDisabled();

    await waitFor(() => expect(screen.getByTestId('model-probe-counts')).toHaveTextContent('4 passed, 1 failed, 1 could not run'));
    expect(screen.getByTestId('model-probe-counts')).toHaveTextContent('Completed');
    expect(getModelProbeRun).toHaveBeenCalledWith('mpr_1');
    expect(screen.getByRole('button', { name: 'Evaluate' })).toBeEnabled();
    expect(screen.getByText('ASCII and format')).toBeInTheDocument();
    expect(screen.getByText('2 em dashes', { exact: false })).toBeInTheDocument();
    expect(screen.getByText('Could not run')).toBeInTheDocument();
    expect(screen.getByText('(vendor CLI hides the served context)', { exact: false })).toBeInTheDocument();
  });

  it('evaluates a model endpoint and stops polling when the run fails', async () => {
    vi.mocked(evaluateModelEndpoint).mockResolvedValue(run({ targetKind: 'ModelEndpoint', modelEndpointId: 'mep_1' }));
    vi.mocked(getModelProbeRun).mockResolvedValue(run({ status: 'Failed', failureReason: 'Evaluation budget exhausted.' }));
    render(<ModelProbeEvaluate target="modelEndpoint" targetId="mep_1" pollMs={5} />);

    await userEvent.click(screen.getByRole('button', { name: 'Evaluate' }));
    expect(evaluateModelEndpoint).toHaveBeenCalledWith('mep_1', 'Quick');
    expect(await screen.findByText('Evaluation budget exhausted.')).toBeInTheDocument();
    expect(screen.getByTestId('model-probe-counts')).toHaveTextContent('0 passed, 0 failed, 0 could not run');
    const calls = vi.mocked(getModelProbeRun).mock.calls.length;
    await new Promise((resolve) => setTimeout(resolve, 30));
    expect(vi.mocked(getModelProbeRun).mock.calls.length).toBe(calls);
  });

  it('hands a start error to the page error display', async () => {
    vi.mocked(evaluateCaptain).mockRejectedValue(new Error('Only an admin can evaluate a captain.'));
    const onError = vi.fn();
    render(<ModelProbeEvaluate target="captain" targetId="cpt_1" onError={onError} pollMs={5} />);
    await userEvent.click(screen.getByRole('button', { name: 'Evaluate' }));
    await waitFor(() => expect(onError).toHaveBeenCalledWith('Only an admin can evaluate a captain.'));
    expect(screen.getByRole('button', { name: 'Evaluate' })).toBeEnabled();
    expect(screen.queryByTestId('model-probe-counts')).toBeNull();
  });

  it('starts a Full Evaluate and shows the report: counts per persona, evaluator, warnings and the accepted narrative', async () => {
    vi.mocked(evaluateCaptain).mockResolvedValue(run({ depth: 'Full' }));
    vi.mocked(getModelProbeRun).mockResolvedValue(FULL);
    render(<ModelProbeEvaluate target="captain" targetId="cpt_1" pollMs={5} />);

    await userEvent.click(screen.getByRole('button', { name: 'Full Evaluate' }));
    expect(evaluateCaptain).toHaveBeenCalledWith('cpt_1', 'Full');
    const report = await screen.findByTestId('model-evaluation-report');
    expect(report).toHaveTextContent('Report complete');
    expect(report).toHaveTextContent('Evaluator: claude-premium');
    expect(report).toHaveTextContent('Worker: 3 of 6 passed, 0 pending, 1 could not run');
    expect(report).toHaveTextContent('The evaluator is of the same model family');
    expect(report).toHaveTextContent('Strong: single-file Worker edits (3 of 3).');
    expect(report).toHaveTextContent('Weak: coordinated two-place edits (0 of 3).');
    expect(report).toHaveTextContent('Forgets to register the test.');
    expect(report).toHaveTextContent('One objective per mission.');
  });

  it('shows an incomplete report and the narrative rejection reason', async () => {
    vi.mocked(evaluateCaptain).mockResolvedValue(run({ depth: 'Full' }));
    vi.mocked(getModelProbeRun).mockResolvedValue(run({
      depth: 'Full', status: 'Completed',
      report: { ...FULL.report!, complete: false, incompleteReason: 'The fixture battery did not finish: budget spent.', narrative: { ...FULL.report!.narrative, status: 'Rejected', reason: 'A strength cites 2 of 3 but the count is 3 of 3.' } },
    }));
    render(<ModelProbeEvaluate target="captain" targetId="cpt_1" pollMs={5} />);
    await userEvent.click(screen.getByRole('button', { name: 'Full Evaluate' }));
    const report = await screen.findByTestId('model-evaluation-report');
    expect(report).toHaveTextContent('Incomplete: The fixture battery did not finish: budget spent.');
    expect(report).toHaveTextContent('Narrative rejected: A strength cites 2 of 3 but the count is 3 of 3.');
    expect(report).not.toHaveTextContent('Strong: single-file');
  });

  it('cancels a running evaluation and stops polling', async () => {
    vi.mocked(evaluateCaptain).mockResolvedValue(run({ depth: 'Full', status: 'Running' }));
    vi.mocked(getModelProbeRun).mockResolvedValue(run({ depth: 'Full', status: 'Running' }));
    vi.mocked(cancelModelProbeRun).mockResolvedValue(run({
      depth: 'Full', status: 'Cancelled', failureReason: 'Cancelled by an administrator.',
      report: { ...FULL.report!, complete: false, incompleteReason: 'Cancelled by an administrator.', narrative: { ...FULL.report!.narrative, status: 'Skipped', reason: 'The run was stopped.' } },
    }));
    render(<ModelProbeEvaluate target="captain" targetId="cpt_1" pollMs={5} />);
    await userEvent.click(screen.getByRole('button', { name: 'Full Evaluate' }));
    await userEvent.click(await screen.findByRole('button', { name: 'Cancel' }));
    expect(cancelModelProbeRun).toHaveBeenCalledWith('mpr_1');
    const report = await screen.findByTestId('model-evaluation-report');
    expect(report).toHaveTextContent('Incomplete: Cancelled by an administrator.');
    expect(report).toHaveTextContent('Subjective grading skipped: The run was stopped.');
    expect(screen.queryByRole('button', { name: 'Cancel' })).toBeNull();
    const calls = vi.mocked(getModelProbeRun).mock.calls.length;
    await new Promise((resolve) => setTimeout(resolve, 30));
    expect(vi.mocked(getModelProbeRun).mock.calls.length).toBe(calls);
  });

  it('shows a poll error inline when no page error display is given', async () => {
    vi.mocked(evaluateCaptain).mockResolvedValue(run());
    vi.mocked(getModelProbeRun).mockRejectedValue(new Error('Model probe run not found.'));
    render(<ModelProbeEvaluate target="captain" targetId="cpt_1" pollMs={5} />);
    await userEvent.click(screen.getByRole('button', { name: 'Evaluate' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Model probe run not found.');
    expect(screen.getByRole('button', { name: 'Evaluate' })).toBeEnabled();
  });
});

describe('ModelProbeEvaluate i18n catalog', () => {
  const S = "'((?:[^'\\\\]|\\\\.)*)'";
  const call = new RegExp(`\\bt\\(\\s*${S}`, 'g');
  const literal = new RegExp(`^\\s*\\w+:\\s*${S},?$`, 'gm');
  const keys = new Set<string>();
  for (const m of componentSource.matchAll(call)) keys.add(m[1].replace(/\\'/g, "'"));
  for (const m of componentSource.matchAll(literal)) keys.add(m[1]);
  const pageKeys: Array<{ source: string; keys: string[] }> = [
    { source: captainDetailSource, keys: ['Evaluation', 'Probe this captain through its own runtime: served context, output contract, ASCII and format, tool calling, speed and determinism.'] },
    { source: endpointsSource, keys: ['Evaluate', 'Evaluation', 'Probe this model endpoint: served context, output contract, ASCII and format, tool calling, speed and determinism.', "Open this endpoint\\'s details to run the Quick probes against it as an evaluation."] },
  ];
  for (const page of pageKeys) for (const key of page.keys) keys.add(key.replace(/\\'/g, "'"));
  const catalog = JSON.parse(catalogSource) as I18nCatalog;

  it('finds the strings', () => {
    expect(keys.has('{{passed}} passed, {{failed}} failed, {{couldNotRun}} could not run')).toBe(true);
    expect(keys.has('Could not run')).toBe(true);
    expect(keys.has('Served context')).toBe(true);
    for (const page of pageKeys) for (const key of page.keys) expect(page.source).toContain(`'${key}'`);
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
    for (const [code, pack] of Object.entries(catalog.locales)) {
      for (const key of keys) {
        const value = pack.phrases?.[key];
        if (!value) continue;
        const ph = (text: string) => (text.match(/\{\{[\w.]+\}\}/g) ?? []).sort().join(',');
        if (ph(key) !== ph(value)) bad.push(`${code}: ${key}`);
      }
    }
    expect(bad).toEqual([]);
  });
});

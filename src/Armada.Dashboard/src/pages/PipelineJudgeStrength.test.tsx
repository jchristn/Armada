import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import Pipelines from './Pipelines';
import PipelineDetail from './PipelineDetail';
import { createPipeline, getPipeline, listPersonas, listPipelines, listVessels, updatePipeline } from '../api/client';
import { translateTemplate } from '../i18n/runtime';
import type { Pipeline } from '../types/models';

vi.mock('../api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/client')>();
  return {
    ...actual,
    listPipelines: vi.fn(), listPersonas: vi.fn(), listVessels: vi.fn(), createPipeline: vi.fn(), updatePipeline: vi.fn(), getPipeline: vi.fn(),
  };
});

const localeValue = {
  t: (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', text, null, params),
  locale: 'en',
  formatDateTime: (v: string | null | undefined) => v ?? '',
  formatRelativeTime: (v: string | null | undefined) => v ?? '',
};
vi.mock('../context/LocaleContext', () => ({ useLocale: () => localeValue }));
vi.mock('../context/NotificationContext', () => ({ useNotifications: () => ({ pushToast: vi.fn() }) }));
vi.mock('../context/AuthContext', () => ({ useAuth: () => ({ isTenantAdmin: true, isAdmin: true, user: { user: { id: 'usr_1', tenantId: 'default' } } }) }));
vi.mock('../components/shared/UserScopeFilter', () => ({ default: () => null }));

function page<T>(objects: T[]) {
  return { success: true, pageNumber: 1, pageSize: 1000, totalPages: 1, totalRecords: objects.length, totalMs: 1, objects };
}

const reference: Pipeline = {
  id: 'ppl_ref', tenantId: null, userId: null, scope: 'TenantWide', name: 'Reference-PA', description: 'Opus Architect, Qwen Worker, GPT Judge',
  stages: [
    { id: 'stg_1', pipelineId: 'ppl_ref', order: 1, personaName: 'Architect', isOptional: false, description: null, requiresReview: false, reviewDenyAction: 'RetryStage' },
    { id: 'stg_2', pipelineId: 'ppl_ref', order: 2, personaName: 'Judge', isOptional: false, description: null, requiresReview: false, reviewDenyAction: 'RetryStage' },
  ] as Pipeline['stages'],
  isBuiltIn: true, active: false, judgeStrength: 'ProceedAndFlag', createdUtc: '2026-11-01T00:00:00Z', lastUpdateUtc: '2026-11-01T00:00:00Z',
};

beforeEach(() => {
  vi.mocked(listPipelines).mockReset().mockResolvedValue(page([reference]) as never);
  vi.mocked(listPersonas).mockReset().mockResolvedValue(page([{ name: 'Architect' }, { name: 'Judge' }]) as never);
  vi.mocked(listVessels).mockReset().mockResolvedValue(page([]) as never);
  vi.mocked(createPipeline).mockReset().mockResolvedValue({ ...reference, id: 'ppl_new', name: 'Mine', isBuiltIn: false });
  vi.mocked(updatePipeline).mockReset().mockResolvedValue(reference);
  vi.mocked(getPipeline).mockReset().mockResolvedValue({ ...reference, isBuiltIn: false, judgeStrength: null });
});

describe('Pipeline Judge strength rule', () => {
  it('creates from a built-in template, copying its stages and Judge rule, and defaults new pipelines to Wait', async () => {
    render(<MemoryRouter><Pipelines /></MemoryRouter>);
    await screen.findAllByText('Reference-PA');
    fireEvent.click(screen.getByRole('button', { name: /\+ Pipeline/ }));
    const judge = screen.getByLabelText('Judge strength rule') as HTMLSelectElement;
    expect(judge.value).toBe('Wait');
    fireEvent.change(screen.getByLabelText('Create from template'), { target: { value: 'Reference-PA' } });
    expect((screen.getByLabelText('Judge strength rule') as HTMLSelectElement).value).toBe('ProceedAndFlag');
    const form = screen.getByRole('heading', { name: 'Create Pipeline' }).closest('form')!;
    fireEvent.change(within(form).getAllByRole('textbox')[0], { target: { value: 'Mine' } });
    fireEvent.submit(form);
    await waitFor(() => expect(createPipeline).toHaveBeenCalledTimes(1));
    const payload = vi.mocked(createPipeline).mock.calls[0][0];
    expect(payload.judgeStrength).toBe('ProceedAndFlag');
    expect(payload.description).toBe('Opus Architect, Qwen Worker, GPT Judge');
    expect(payload.stages?.map(s => s.personaName)).toEqual(['Architect', 'Judge']);
  });

  it('shows a stored pipeline without a rule as Off and saves a changed rule', async () => {
    render(
      <MemoryRouter initialEntries={['/pipelines/Reference-PA']}>
        <Routes><Route path="/pipelines/:name" element={<PipelineDetail />} /></Routes>
      </MemoryRouter>,
    );
    const label = await screen.findByText('Judge strength rule');
    expect(label.closest('.detail-field')).toHaveTextContent('Off');
    fireEvent.click(screen.getByRole('button', { name: 'Edit stages' }));
    fireEvent.change(screen.getByLabelText('Judge strength rule'), { target: { value: 'Wait' } });
    fireEvent.submit(screen.getByRole('heading', { name: 'Edit Pipeline' }).closest('form')!);
    await waitFor(() => expect(updatePipeline).toHaveBeenCalledTimes(1));
    expect(vi.mocked(updatePipeline).mock.calls[0][1].judgeStrength).toBe('Wait');
  });
});

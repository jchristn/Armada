import { fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import Deployments from './Deployments';
import { listDeployments, listEnvironments, listReleases, listVessels, listWorkflowProfiles } from '../api/client';
import { translateTemplate } from '../i18n/runtime';

vi.mock('../api/client', () => ({
  createDeployment: vi.fn(),
  deleteDeployment: vi.fn(),
  listDeployments: vi.fn(),
  listEnvironments: vi.fn(),
  listReleases: vi.fn(),
  listVessels: vi.fn(),
  listWorkflowProfiles: vi.fn(),
  updateDeployment: vi.fn(),
}));

const localeValue = {
  t: (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', text, null, params),
  locale: 'en',
  formatDateTime: (v: string | null | undefined) => v ?? '',
  formatRelativeTime: (v: string | null | undefined) => v ?? '',
};
vi.mock('../context/LocaleContext', () => ({ useLocale: () => localeValue }));
vi.mock('../context/NotificationContext', () => ({ useNotifications: () => ({ pushToast: vi.fn() }) }));
vi.mock('../context/AuthContext', () => ({ useAuth: () => ({ isAdmin: true, isTenantAdmin: true }) }));

function page<T>(objects: T[]) {
  return { success: true, pageNumber: 1, pageSize: 9999, totalPages: 1, totalRecords: objects.length, totalMs: 1, objects };
}

function env(id: string, vesselId: string, name: string) {
  return { id, tenantId: null, userId: null, vesselId, name, description: null };
}

describe('Create Deployment environment picker (F19)', () => {
  beforeEach(() => {
    vi.mocked(listDeployments).mockResolvedValue(page([]) as never);
    vi.mocked(listReleases).mockResolvedValue(page([]) as never);
    vi.mocked(listWorkflowProfiles).mockResolvedValue(page([]) as never);
    vi.mocked(listVessels).mockResolvedValue(page([
      { id: 'vsl_b', name: 'billing' },
      { id: 'vsl_a', name: 'api' },
    ]) as never);
    vi.mocked(listEnvironments).mockResolvedValue(page([
      env('env_b_dev', 'vsl_b', 'Development'),
      env('env_a_dev', 'vsl_a', 'Development'),
      env('env_a_prod', 'vsl_a', 'Production'),
    ]) as never);
  });

  it('labels environments "vessel / environment" until a vessel is chosen, then filters by it', async () => {
    render(<MemoryRouter><Deployments /></MemoryRouter>);

    fireEvent.click((await screen.findAllByRole('button', { name: '+ Deployment' }))[0]);
    const environmentSelect = screen.getByLabelText('Environment') as HTMLSelectElement;
    const labels = () => within(environmentSelect).getAllByRole('option').map((option) => option.textContent).slice(1);

    expect(labels()).toEqual(['api / Development', 'api / Production', 'billing / Development']);

    fireEvent.change(screen.getByLabelText('Vessel'), { target: { value: 'vsl_b' } });
    expect(labels()).toEqual(['Development']);

    // Picking a vessel drops an environment that belongs to another vessel.
    fireEvent.change(screen.getByLabelText('Vessel'), { target: { value: '' } });
    fireEvent.change(environmentSelect, { target: { value: 'env_a_prod' } });
    expect((screen.getByLabelText('Vessel') as HTMLSelectElement).value).toBe('vsl_a');
    fireEvent.change(screen.getByLabelText('Vessel'), { target: { value: 'vsl_b' } });
    expect(environmentSelect.value).toBe('');
  });
});

import { render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import Voyages from './Voyages';
import { listVoyages } from '../api/client';
import { translateTemplate } from '../i18n/runtime';
import type { Voyage } from '../types/models';

vi.mock('../api/client', () => ({
  listVoyages: vi.fn(),
  cancelVoyage: vi.fn(),
  purgeVoyage: vi.fn(),
  getVoyageStatus: vi.fn(),
}));

const localeValue = {
  t: (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', text, null, params),
  locale: 'en',
  formatDateTime: (v: string | null | undefined) => v ?? '',
  formatRelativeTime: (v: string | null | undefined) => v ?? '',
};
vi.mock('../context/LocaleContext', () => ({ useLocale: () => localeValue }));
vi.mock('../context/NotificationContext', () => ({ useNotifications: () => ({ pushToast: vi.fn() }) }));
vi.mock('../context/AuthContext', () => ({ useAuth: () => ({ isTenantAdmin: true, isAdmin: true, user: { id: 'usr_1' } }) }));
vi.mock('../components/shared/UserScopeFilter', () => ({ default: () => null }));

function voyage(id: string, title: string, landingMode: string | null): Voyage {
  return {
    id, tenantId: 'default', title, description: null, status: 'InProgress',
    createdUtc: '2026-10-04T00:00:00Z', completedUtc: null, lastUpdateUtc: '2026-10-04T00:00:00Z',
    autoPush: true, autoCreatePullRequests: true, autoMergePullRequests: null, landingMode,
  };
}

describe('Voyages landing mode column', () => {
  beforeEach(() => {
    vi.mocked(listVoyages).mockResolvedValue({
      success: true, pageNumber: 1, pageSize: 25, totalPages: 1, totalRecords: 2, totalMs: 1,
      objects: [voyage('vyg_1', 'Gateway hardening', 'MergeAndPush'), voyage('vyg_2', 'Docs sweep', null)],
    } as never);
  });

  it('shows Landing Mode (Default when inherited) and drops the Auto Push and Auto Create PRs columns', async () => {
    render(<MemoryRouter><Voyages /></MemoryRouter>);
    const row = (await screen.findByText('Gateway hardening')).closest('tr')!;
    expect(within(row).getByText('MergeAndPush')).toBeInTheDocument();
    const inherited = screen.getByText('Docs sweep').closest('tr')!;
    expect(within(inherited).getByText('Default')).toBeInTheDocument();
    expect(screen.getByRole('columnheader', { name: 'Landing Mode' })).toBeInTheDocument();
    expect(screen.queryByRole('columnheader', { name: 'Auto Push' })).toBeNull();
    expect(screen.queryByRole('columnheader', { name: 'Auto Create PRs' })).toBeNull();
  });
});

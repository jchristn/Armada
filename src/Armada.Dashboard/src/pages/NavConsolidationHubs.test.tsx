import type { ReactElement } from 'react';
import { render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import DispatchHub from './DispatchHub';
import Activity from './Activity';
import ServerHub from './ServerHub';
import MissionsHub from './MissionsHub';
import { translateTemplate } from '../i18n/runtime';

const localeValue = { t: (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', text, null, params) };
vi.mock('../context/LocaleContext', () => ({ useLocale: () => localeValue }));
const authValue = { isAdmin: true, isTenantAdmin: false };
vi.mock('../context/AuthContext', () => ({ useAuth: () => authValue }));

vi.mock('./Dispatch', () => ({ default: () => <div>dispatch panel</div> }));
vi.mock('./Planning', () => ({ default: () => <div>planning panel</div> }));
vi.mock('./Objectives', () => ({ default: () => <div>backlog panel</div> }));
vi.mock('./History', () => ({ default: () => <div>history panel</div> }));
vi.mock('./RequestHistory', () => ({ default: () => <div>requests panel</div> }));
vi.mock('./Events', () => ({ default: () => <div>events panel</div> }));
vi.mock('./Signals', () => ({ default: () => <div>signals panel</div> }));
vi.mock('./TokenUsage', () => ({ default: () => <div>tokens panel</div> }));
vi.mock('./Missions', () => ({ default: () => <div>missions panel</div> }));
vi.mock('./Voyages', () => ({ default: () => <div>voyages panel</div> }));
vi.mock('./MergeQueue', () => ({ default: () => <div>merge queue panel</div> }));
vi.mock('./BlackBox', () => ({ default: () => <div>black box panel</div> }));
vi.mock('./Jobs', () => ({ default: () => <div>jobs panel</div> }));
vi.mock('./Server', () => ({ default: () => <div>server panel</div> }));
vi.mock('./Storage', () => ({ default: () => <div>storage panel</div> }));
vi.mock('./CliPermissions', () => ({ default: () => <div>cli permissions panel</div> }));
vi.mock('./Doctor', () => ({ default: () => <div>diagnostics panel</div> }));
vi.mock('./admin/Tenants', () => ({ default: () => <div>tenants panel</div> }));
vi.mock('./admin/Users', () => ({ default: () => <div>users panel</div> }));
vi.mock('./admin/Credentials', () => ({ default: () => <div>credentials panel</div> }));

function renderAt(path: string, node: ReactElement) {
  return render(<MemoryRouter initialEntries={[path]}>{node}</MemoryRouter>);
}

function tabNames(label: string): string[] {
  return within(screen.getByRole('tablist', { name: label })).getAllByRole('tab').map((tab) => tab.textContent ?? '');
}

beforeEach(() => {
  authValue.isAdmin = true;
});

describe('v1.2.0 hub tabs', () => {
  it('Dispatch hosts Planning between Dispatch and Backlog, deep-linked by ?tab=planning', async () => {
    renderAt('/dispatch?tab=planning&session=psn_1', <DispatchHub />);
    expect(tabNames('Dispatch sections')).toEqual(['Dispatch', 'Planning', 'Backlog']);
    expect(await screen.findByText('planning panel')).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Planning' })).toHaveAttribute('aria-selected', 'true');
  });

  it('Missions hosts Voyages, the Merge Queue, and the Black Box, deep-linked by ?tab=blackbox', async () => {
    renderAt('/missions?tab=blackbox&bbtab=fingerprints', <MissionsHub />);
    expect(tabNames('Mission sections')).toEqual(['Missions', 'Voyages', 'Merge Queue', 'Black Box']);
    expect(await screen.findByText('black box panel')).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Black Box' })).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByRole('tab', { name: 'Black Box' })).toHaveAttribute('title', 'Failure records for missions and voyages: where, how and why each failed, and how to recover');
  });

  it('Activity hosts Jobs, deep-linked by ?source=jobs', async () => {
    renderAt('/activity?source=jobs', <Activity />);
    expect(tabNames('Activity sources')).toContain('Jobs');
    expect(await screen.findByText('jobs panel')).toBeInTheDocument();
  });

  it('Settings hosts Storage and CLI Tool Permissions as tabs', async () => {
    renderAt('/server?tab=cli-permissions&request=cpr_1', <ServerHub />);
    expect(tabNames('Settings sections')).toEqual(['Server', 'Storage', 'CLI Tool Permissions', 'Diagnostics', 'Tenants', 'Users', 'Credentials']);
    expect(await screen.findByText('cli permissions panel')).toBeInTheDocument();
  });

  it('Settings opens Storage from ?tab=storage for global admins', async () => {
    renderAt('/server?tab=storage', <ServerHub />);
    expect(await screen.findByText('storage panel')).toBeInTheDocument();
  });

  it('CLI Tool Permissions is visible to every user; Storage stays global-admin only', async () => {
    authValue.isAdmin = false;
    renderAt('/server?tab=storage', <ServerHub />);
    expect(tabNames('Settings sections')).toEqual(['Server', 'CLI Tool Permissions', 'Diagnostics']);
    // An unknown or hidden tab falls back to the default Server tab.
    expect(await screen.findByText('server panel')).toBeInTheDocument();
  });
});

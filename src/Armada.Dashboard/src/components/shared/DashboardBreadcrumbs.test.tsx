import { StrictMode, Suspense, lazy, useState, type ReactNode } from 'react';
import { fireEvent, render, screen, within } from '@testing-library/react';
import { Link, MemoryRouter, Route, Routes, useSearchParams } from 'react-router-dom';
import Breadcrumbs from './Breadcrumbs';
import DashboardBreadcrumbs, { DashboardBreadcrumbProvider } from './DashboardBreadcrumbs';
import PageHeader from './PageHeader';
import Tabs from './Tabs';
import NotFound from '../../pages/NotFound';
import { dashboardBreadcrumbRoutes, dashboardBreadcrumbs, dashboardHubs, normalizeDashboardBreadcrumbs } from '../../lib/dashboardNavigation';

vi.mock('../../context/LocaleContext', () => ({ useLocale: () => ({ t: (text: string) => text }) }));

function at(path: string, children: ReactNode) {
  return render(<MemoryRouter basename="/dashboard" initialEntries={[`/dashboard${path}`]}>
    <DashboardBreadcrumbProvider><DashboardBreadcrumbs />{children}</DashboardBreadcrumbProvider>
  </MemoryRouter>);
}
function trail() {
  return screen.getByRole('navigation', { name: 'Breadcrumbs' });
}
function labels() {
  return [...trail().querySelectorAll('li')].map(li => li.lastElementChild?.textContent);
}

describe('Global dashboard breadcrumb placement', () => {
  it.each(['/', ...dashboardBreadcrumbRoutes.map(path => path.replace(/:\w+\??/g, 'example'))])('renders a shared trail before the page at %s, even without page markup', path => {
    at(path, <PageHeader title="Loading or error state" />);
    expect(screen.getAllByRole('navigation', { name: 'Breadcrumbs' })).toHaveLength(1);
    expect(trail().querySelector('ol')).not.toBeNull();
    expect(trail().querySelectorAll('[aria-current="page"]')).toHaveLength(1);
    expect(labels()[0]).toBe('Dashboard');
    expect(labels()).not.toContain('Page not found');
    for (const link of within(trail()).queryAllByRole('link')) expect(link.getAttribute('href')).toMatch(/^\/dashboard(?:\/|\?|$)/);
  });

  it('places direct breadcrumbs and PageHeader breadcrumbs in the same single global slot', () => {
    const view = at('/captains/cpt_1', <PageHeader title="Alpha" breadcrumb={[{ label: 'Captains', to: '/captains' }, { label: 'Alpha' }]} />);
    expect(labels()).toEqual(['Dashboard', 'Captains', 'Alpha']);
    view.rerender(<MemoryRouter basename="/dashboard" initialEntries={['/dashboard/captains/cpt_1']}>
      <DashboardBreadcrumbProvider><DashboardBreadcrumbs /><Breadcrumbs items={[{ label: 'Captains', to: '/captains' }, { label: 'Alpha' }]} /></DashboardBreadcrumbProvider>
    </MemoryRouter>);
    expect(screen.getAllByRole('navigation', { name: 'Breadcrumbs' })).toHaveLength(1);
    expect(labels()).toEqual(['Dashboard', 'Captains', 'Alpha']);
  });

  it('keeps captain ancestry and names on evaluation pages, then clears them on navigation', () => {
    at('/captains/cpt_1', <>
      <Link to="/evaluations/run_1">Results</Link><Link to="/vessels/missing">Missing vessel</Link>
      <Routes>
        <Route path="/captains/:id" element={<PageHeader title="Alpha" breadcrumb={[{ label: 'Captains', to: '/captains' }, { label: 'Alpha' }]} />} />
        <Route path="/evaluations/:id" element={<PageHeader title="Result" breadcrumb={[
          { label: 'Captains', to: '/captains' }, { label: 'Alpha', to: '/captains/cpt_1' },
          { label: 'Evaluations', to: '/evaluations?captainId=cpt_1' }, { label: 'run_1' },
        ]} />} />
        <Route path="/vessels/:id" element={<p>Vessel not found</p>} />
      </Routes>
    </>);
    fireEvent.click(screen.getByRole('link', { name: 'Results' }));
    expect(labels()).toEqual(['Dashboard', 'Captains', 'Alpha', 'Evaluations', 'run_1']);
    expect(within(trail()).getByRole('link', { name: 'Alpha' })).toHaveAttribute('href', '/dashboard/captains/cpt_1');
    expect(within(trail()).getByRole('link', { name: 'Evaluations' })).toHaveAttribute('href', '/dashboard/evaluations?captainId=cpt_1');
    fireEvent.click(screen.getByRole('link', { name: 'Missing vessel' }));
    expect(labels()).toEqual(['Dashboard', 'Vessels', 'missing']);
    expect(within(trail()).queryByText('Alpha')).not.toBeInTheDocument();
  });

  it('clears an override when only query parameters change', () => {
    function ListPage() {
      const [params] = useSearchParams();
      return <PageHeader title="Evaluations" breadcrumb={params.get('captainId') ? [
        { label: 'Captains', to: '/captains' }, { label: 'Alpha', to: '/captains/cpt_1' }, { label: 'Evaluations' },
      ] : undefined} />;
    }
    at('/evaluations?captainId=cpt_1', <>
      <Link to="/evaluations">All evaluations</Link>
      <Routes><Route path="/evaluations" element={<ListPage />} /></Routes>
    </>);
    expect(labels()).toEqual(['Dashboard', 'Captains', 'Alpha', 'Evaluations']);
    fireEvent.click(screen.getByRole('link', { name: 'All evaluations' }));
    expect(labels()).toEqual(['Dashboard', 'Captains', 'Evaluations']);
  });

  it('keeps navigation visible while a page chunk is loading', () => {
    const LoadingPage = lazy(() => new Promise<{ default: () => ReactNode }>(() => {}));
    at('/captains/cpt_1', <Suspense fallback={<p>Loading page...</p>}><LoadingPage /></Suspense>);
    expect(screen.getByText('Loading page...')).toBeInTheDocument();
    expect(labels()).toEqual(['Dashboard', 'Captains', 'cpt_1']);
  });

  it('reflects the actual visible tab when an unavailable admin tab was bookmarked', () => {
    at('/server?tab=tenants', <Tabs defaultTabKey="server" tabs={[
      { key: 'server', label: 'Server', render: () => <p>Server panel</p> },
      { key: 'tenants', label: 'Tenants', hidden: true, render: () => <p>Forbidden panel</p> },
    ]} />);
    expect(labels()).toEqual(['Dashboard', 'Settings', 'Server']);
    expect(screen.queryByText('Forbidden panel')).not.toBeInTheDocument();
  });

  it('lets an embedded detail supply its name and restores the hub trail after it closes', () => {
    function WorkspacePanel() {
      const [open, setOpen] = useState(true);
      return <><button onClick={() => setOpen(false)}>Close detail</button>{open && <PageHeader title="Repository" breadcrumb={[
        { label: 'Workspace', to: '/workspace' }, { label: 'Repository' },
      ]} />}</>;
    }
    at('/vessels?tab=workspace', <Tabs defaultTabKey="workspace" tabs={[
      { key: 'workspace', label: 'Workspace', render: () => <WorkspacePanel /> },
    ]} />);
    expect(labels()).toEqual(['Dashboard', 'Vessels', 'Workspace', 'Repository']);
    fireEvent.click(screen.getByRole('button', { name: 'Close detail' }));
    expect(labels()).toEqual(['Dashboard', 'Vessels', 'Workspace']);
  });

  it('supports repeated page renders and StrictMode without duplicate trails or update loops', () => {
    function Page({ name }: { name: string }) {
      return <PageHeader title={name} breadcrumb={[{ label: 'Vessels', to: '/vessels' }, { label: name }]} />;
    }
    const tree = (name: string) => <StrictMode><MemoryRouter initialEntries={['/vessels/vsl_1']}>
      <DashboardBreadcrumbProvider><DashboardBreadcrumbs /><Page name={name} /></DashboardBreadcrumbProvider>
    </MemoryRouter></StrictMode>;
    const view = render(tree('First'));
    view.rerender(tree('Updated'));
    expect(labels()).toEqual(['Dashboard', 'Vessels', 'Updated']);
    expect(screen.getAllByRole('navigation', { name: 'Breadcrumbs' })).toHaveLength(1);
  });

  it('shows recovery navigation for unknown URLs instead of an empty outlet', () => {
    at('/retired-page', <Routes><Route path="*" element={<NotFound />} /></Routes>);
    expect(labels()).toEqual(['Dashboard', 'Page not found']);
    expect(screen.getByRole('heading', { name: 'Page not found' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Return to Dashboard' })).toHaveAttribute('href', '/dashboard');
  });
});

describe('Shared breadcrumb ancestry', () => {
  it.each(dashboardHubs.flatMap(hub => hub.tabs.map(tab => [hub.path, hub.param, tab.key, hub.label, tab.label] as const)))('%s?%s=%s uses its owning hub', (path, param, key, hub, tab) => {
    const items = dashboardBreadcrumbs(path, `?${param}=${key}`);
    expect(items.map(item => item.label)).toEqual(tab === hub ? ['Dashboard', hub] : ['Dashboard', hub, tab]);
  });

  it.each([
    ['/fleets', ['Dashboard', 'Vessels', 'Fleets']],
    ['/docks', ['Dashboard', 'Captains', 'Docks']],
    ['/voyages', ['Dashboard', 'Missions', 'Voyages']],
    ['/merge-queue', ['Dashboard', 'Missions', 'Merge Queue']],
    ['/personas', ['Dashboard', 'Configuration', 'Personas']],
    ['/deployments', ['Dashboard', 'Delivery', 'Deployments']],
    ['/events', ['Dashboard', 'Activity', 'Events']],
  ])('%s detail breadcrumbs link directly to their hub', (to, expected) => {
    const items = normalizeDashboardBreadcrumbs([{ label: (expected as string[])[(expected as string[]).length - 1], to: to as string }, { label: 'Name' }]);
    expect(items.map(item => item.label)).toEqual([...(expected as string[]), 'Name']);
    expect(items[items.length - 2]?.to).toContain('?');
  });

  it('uses canonical collection labels and accepts an entity link as the first ancestor', () => {
    expect(normalizeDashboardBreadcrumbs([{ label: 'Prompt Templates', to: '/prompt-templates' }, { label: 'Template' }]).map(item => item.label))
      .toEqual(['Dashboard', 'Configuration', 'Prompts', 'Template']);
    expect(normalizeDashboardBreadcrumbs([{ label: 'Alpha', to: '/captains/cpt_1' }, { label: 'Evaluations' }]).map(item => item.label))
      .toEqual(['Dashboard', 'Captains', 'Alpha', 'Evaluations']);
  });

  it('keeps vessel ancestry on onboarding and history, including before data loads', () => {
    const items = dashboardBreadcrumbs('/vessels/vsl_1/history');
    expect(items.map(item => item.label)).toEqual(['Dashboard', 'Vessels', 'vsl_1', 'History']);
    expect(items[2].to).toBe('/vessels/vsl_1');
  });
});

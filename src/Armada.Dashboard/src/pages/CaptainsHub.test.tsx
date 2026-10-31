import { act, fireEvent, render, screen } from '@testing-library/react';
import { createMemoryRouter, Navigate, RouterProvider } from 'react-router-dom';
import CaptainsHub from './CaptainsHub';

vi.mock('../context/LocaleContext', () => ({
  useLocale: () => ({ t: (text: string) => text }),
}));

vi.mock('./Captains', () => ({ default: () => <div>Captains panel</div> }));
vi.mock('./Docks', () => ({ default: () => <div>Docks panel</div> }));
vi.mock('./BatteryPacks', () => ({ default: () => <div>Battery packs panel</div> }));

/** Mirrors the App.tsx routes for /captains and the legacy /battery redirect. */
function renderAt(entry: string) {
  const router = createMemoryRouter([
    { path: '/captains', element: <CaptainsHub /> },
    { path: '/battery', element: <Navigate to="/captains?tab=battery" replace /> },
  ], { initialEntries: [entry] });
  render(<RouterProvider router={router} />);
  return router;
}

function selected(): string | null {
  return screen.getByRole('tab', { selected: true }).textContent;
}

describe('CaptainsHub Battery Packs tab', () => {
  it('lists Captains, Docks and Battery Packs, with Captains as the default tab', async () => {
    renderAt('/captains');
    expect(screen.getAllByRole('tab').map(tab => tab.textContent)).toEqual(['Captains', 'Docks', 'Battery Packs']);
    expect(await screen.findByText('Captains panel')).toBeInTheDocument();
    expect(selected()).toBe('Captains');
  });

  it('a direct link to ?tab=battery opens the Battery Packs tab', async () => {
    renderAt('/captains?tab=battery');
    expect(await screen.findByText('Battery packs panel')).toBeInTheDocument();
    expect(selected()).toBe('Battery Packs');
    expect(screen.queryByText('Captains panel')).toBeNull();
  });

  it('redirects the legacy /battery route to the Battery Packs tab, replacing the history entry', async () => {
    const router = renderAt('/battery');
    expect(await screen.findByText('Battery packs panel')).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/captains');
    expect(router.state.location.search).toBe('?tab=battery');
    expect(router.state.historyAction).toBe('REPLACE');
  });

  it('switches tabs through the URL so back and forward restore each tab', async () => {
    const router = renderAt('/captains');
    expect(await screen.findByText('Captains panel')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('tab', { name: 'Battery Packs' }));
    expect(await screen.findByText('Battery packs panel')).toBeInTheDocument();
    expect(router.state.location.search).toBe('?tab=battery');
    await act(async () => { await router.navigate(-1); });
    expect(await screen.findByText('Captains panel')).toBeInTheDocument();
    expect(selected()).toBe('Captains');
    await act(async () => { await router.navigate(1); });
    expect(await screen.findByText('Battery packs panel')).toBeInTheDocument();
    expect(selected()).toBe('Battery Packs');
  });

  it('falls back to the Captains tab for an unknown tab key', async () => {
    renderAt('/captains?tab=nope');
    expect(await screen.findByText('Captains panel')).toBeInTheDocument();
  });
});

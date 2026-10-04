import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import VesselsHub, { VESSEL_HEALTH_PATH } from './VesselsHub';
import { navSections } from '../components/navConfig';

vi.mock('../context/LocaleContext', () => ({
  useLocale: () => ({ t: (text: string) => text }),
}));

vi.mock('./Vessels', () => ({ default: () => <div>Vessels panel</div> }));
vi.mock('./Fleets', () => ({ default: () => <div>Fleets panel</div> }));
vi.mock('./Workspace', () => ({ default: () => <div>Workspace panel</div> }));
vi.mock('./VesselHealth', () => ({ default: () => <div>Health panel</div> }));

function Probe() {
  const location = useLocation();
  return <div data-testid="location">{location.pathname}{location.search}</div>;
}

function renderHub(entry: string) {
  return render(
    <MemoryRouter initialEntries={[entry]}>
      <Routes>
        <Route path="/vessels" element={<><VesselsHub /><Probe /></>} />
        <Route path="/vessels/health" element={<><VesselsHub /><Probe /></>} />
      </Routes>
    </MemoryRouter>,
  );
}

describe('VesselsHub Health tab', () => {
  it('deep link /vessels/health opens the Health tab', async () => {
    renderHub('/vessels/health?overall=Fail');
    expect(await screen.findByText('Health panel')).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Health' })).toHaveAttribute('aria-selected', 'true');
  });

  it('lists Health between Vessels and Fleets', () => {
    renderHub('/vessels');
    const names = screen.getAllByRole('tab').map((tab) => tab.textContent);
    expect(names).toEqual(['Vessels', 'Health', 'Fleets', 'Workspace']);
  });

  it('redirects /vessels?tab=health to the /vessels/health route', async () => {
    renderHub('/vessels?tab=health&overall=Warn');
    await waitFor(() => expect(screen.getByTestId('location').textContent).toBe('/vessels/health?overall=Warn'));
  });

  it('switching to another tab from /vessels/health returns to /vessels', async () => {
    renderHub('/vessels/health?tab=fleets&overall=Fail');
    await waitFor(() => expect(screen.getByTestId('location').textContent).toBe('/vessels?tab=fleets'));
  });

  it('the BUILD nav section highlights Vessels on the health route', () => {
    expect(VESSEL_HEALTH_PATH).toBe('/vessels/health');
    const section = navSections.find((s) => s.items.some((i) => i.to === '/vessels'));
    expect(section).toBeDefined();
    expect(section!.matchers.some((m) => VESSEL_HEALTH_PATH.startsWith(m))).toBe(true);
  });
});

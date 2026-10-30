import { readFileSync } from 'node:fs';
import { cleanup, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { BlackBoxRedirect, CliPermissionsRedirect, PlanningRedirect } from './LegacyNavRedirects';
import { blackBoxHref, cliPermissionsHref, planningHref } from '../lib/navTargets';

function Probe() {
  const location = useLocation();
  return (
    <>
      <div data-testid="location">{location.pathname + location.search}</div>
      <div data-testid="state">{JSON.stringify(location.state ?? null)}</div>
    </>
  );
}

function renderAt(entry: string | { pathname: string; search?: string; state?: unknown }) {
  cleanup();
  render(
    <MemoryRouter initialEntries={[entry]}>
      <Routes>
        <Route path="/cli-permissions" element={<CliPermissionsRedirect />} />
        <Route path="/planning" element={<PlanningRedirect />} />
        <Route path="/planning/:id" element={<PlanningRedirect />} />
        <Route path="/server" element={<Probe />} />
        <Route path="/dispatch" element={<Probe />} />
        <Route path="/blackbox" element={<BlackBoxRedirect />} />
        <Route path="/missions" element={<Probe />} />
      </Routes>
    </MemoryRouter>,
  );
  return screen.getByTestId('location').textContent;
}

describe('legacy nav redirects (v1.2.0 consolidation)', () => {
  it('/cli-permissions lands on the Settings tab', () => {
    expect(renderAt('/cli-permissions')).toBe('/server?tab=cli-permissions');
  });

  it('/cli-permissions keeps the request deep link and maps the old tab to section', () => {
    expect(renderAt('/cli-permissions?request=cpr_1')).toBe('/server?tab=cli-permissions&request=cpr_1');
    expect(renderAt('/cli-permissions?tab=rules&request=cpr_2')).toBe('/server?tab=cli-permissions&section=rules&request=cpr_2');
  });

  it('/planning lands on the Dispatch Planning tab and keeps the router state of handoffs', () => {
    expect(renderAt({ pathname: '/planning', state: { fromWorkspace: true, vesselId: 'vsl_1' } })).toBe('/dispatch?tab=planning');
    expect(screen.getByTestId('state').textContent).toBe(JSON.stringify({ fromWorkspace: true, vesselId: 'vsl_1' }));
  });

  it('/planning/:id keeps the session id and other query parameters', () => {
    expect(renderAt('/planning/psn_42')).toBe('/dispatch?tab=planning&session=psn_42');
    expect(renderAt('/planning/psn_7?from=workspace')).toBe('/dispatch?tab=planning&session=psn_7&from=workspace');
  });

  it('/blackbox lands on the Missions Black Box tab; the old tab becomes bbtab and filters are kept', () => {
    expect(renderAt('/blackbox')).toBe('/missions?tab=blackbox');
    expect(renderAt('/blackbox?tab=entries&fingerprint=fp_1')).toBe('/missions?tab=blackbox&bbtab=entries&fingerprint=fp_1');
    expect(renderAt('/blackbox?vesselId=vsl_1&triage=ArmadaDefect')).toBe('/missions?tab=blackbox&vesselId=vsl_1&triage=ArmadaDefect');
  });

  it('link helpers build the new tab URLs', () => {
    expect(cliPermissionsHref()).toBe('/server?tab=cli-permissions');
    expect(cliPermissionsHref({ requestId: 'cpr_1', section: 'rules' })).toBe('/server?tab=cli-permissions&section=rules&request=cpr_1');
    expect(planningHref()).toBe('/dispatch?tab=planning');
    expect(planningHref('psn_1')).toBe('/dispatch?tab=planning&session=psn_1');
    expect(blackBoxHref()).toBe('/missions?tab=blackbox');
    expect(blackBoxHref('entries', { path: 'src/a b', vesselId: null, tab: 'x' })).toBe('/missions?tab=blackbox&bbtab=entries&path=src%2Fa+b');
  });

  it('App.tsx redirects every folded route instead of rendering the old pages (no 404s)', () => {
    const app = readFileSync('./src/App.tsx', 'utf8');
    expect(app).toContain('<Route path="jobs" element={<Navigate to="/activity?source=jobs" replace />} />');
    expect(app).toContain('<Route path="storage" element={<Navigate to="/server?tab=storage" replace />} />');
    expect(app).toContain('<Route path="cli-permissions" element={<CliPermissionsRedirect />} />');
    expect(app).toContain('<Route path="planning" element={<PlanningRedirect />} />');
    expect(app).toContain('<Route path="planning/:id" element={<PlanningRedirect />} />');
    expect(app).toContain('<Route path="blackbox" element={<BlackBoxRedirect />} />');
    expect(app).toContain('<Route path="blackbox/:id" element={<BlackBoxEntry />} />');
    for (const page of ['Jobs', 'Storage', 'Planning', 'CliPermissions', 'BlackBox']) {
      expect(app).not.toContain(`import('./pages/${page}')`);
    }
  });
});

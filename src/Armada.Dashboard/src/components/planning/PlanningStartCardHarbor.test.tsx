import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import PlanningStartCard from './PlanningStartCard';
import type { Captain, Harbor, Vessel } from '../../types/models';

vi.mock('../shared/PlaybookSelector', () => ({ default: () => null }));

const t = (value: string, vars?: Record<string, string | number>) => value.replace(/\{\{(\w+)\}\}/g, (_m, k: string) => String(vars?.[k] ?? ''));
const captain = { id: 'cpt_1', name: 'Planner', runtime: 'ClaudeCode', supportsPlanningSessions: true, state: 'Idle' } as unknown as Captain;
const vessel = { id: 'vsl_1', name: 'gateway', fleetId: null, preferredHarborId: 'hbr_laptop' } as unknown as Vessel;
const harbors = [
  { id: 'hbr_laptop', name: 'laptop', connectionStatus: 'Connected' },
  { id: 'hbr_desk', name: 'desk', connectionStatus: 'Disconnected' },
] as unknown as Harbor[];

function renderCard(onHarborChange = vi.fn(), withHarbors = true) {
  render(
    <MemoryRouter>
      <PlanningStartCard
        t={t} loading={false} captains={[captain]} fleets={[]} vessels={[vessel]} pipelines={[]} title="" captainId="cpt_1" fleetId=""
        vesselId="vsl_1" pipelineId="" availableVessels={[vessel]} selectedCaptain={captain} selectedPlaybooks={[]} creating={false}
        canStartSession onTitleChange={() => undefined} onCaptainChange={() => undefined} onFleetChange={() => undefined}
        onVesselChange={() => undefined} onPipelineChange={() => undefined} onSelectedPlaybooksChange={() => undefined} onStart={() => undefined}
        harbors={withHarbors ? harbors : []} harborId="" onHarborChange={onHarborChange}
      />
    </MemoryRouter>,
  );
  return onHarborChange;
}

describe('PlanningStartCard Run on', () => {
  it('offers Automatic (naming the vessel\'s preferred Harbor) and each Harbor, with offline ones disabled', () => {
    const onHarborChange = renderCard();
    const select = screen.getByRole('combobox', { name: 'Run on' }) as HTMLSelectElement;
    expect(Array.from(select.options).map((o) => o.textContent)).toEqual(['Automatic (preferred: laptop)', 'Harbor: laptop', 'Harbor: desk (disconnected)']);
    expect(select.options[2].disabled).toBe(true);
    fireEvent.change(select, { target: { value: 'hbr_laptop' } });
    expect(onHarborChange).toHaveBeenCalledWith('hbr_laptop');
    expect(screen.getByRole('link', { name: 'Manage Harbors' })).toHaveAttribute('href', '/configuration?tab=harbors');
    expect(screen.getByRole('link', { name: 'Manage vessels' })).toHaveAttribute('href', '/vessels');
  });

  it('hides the picker when there are no Harbors', () => {
    renderCard(vi.fn(), false);
    expect(screen.queryByRole('combobox', { name: 'Run on' })).toBeNull();
  });
});

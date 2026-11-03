import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import CaptainPicker from './CaptainPicker';
import CaptainTierBadge from './CaptainTierBadge';
import FallbackTierSelect from './FallbackTierSelect';
import type { Captain } from '../../types/models';

vi.mock('../../context/LocaleContext', () => ({
  useLocale: () => ({ t: (text: string, vars?: Record<string, string | number>) => (vars ? text.replace(/\{\{(\w+)\}\}/g, (_, k) => String(vars[k])) : text) }),
}));

function captain(id: string, name: string, tier?: string | null): Captain {
  return {
    id,
    tenantId: null,
    name,
    runtime: 'Codex',
    supportsPlanningSessions: true,
    planningSessionSupportReason: null,
    systemInstructions: null,
    model: null,
    tier: tier ?? null,
    allowedPersonas: null,
    preferredPersona: null,
    state: 'Idle',
    processId: null,
  } as Captain;
}

describe('CaptainTierBadge', () => {
  it('renders the tier text when a tier is set', () => {
    render(<CaptainTierBadge tier="Premium" />);
    expect(screen.getByText('Premium')).toBeInTheDocument();
  });

  it('renders nothing when no tier is set', () => {
    const { container } = render(<CaptainTierBadge tier={null} />);
    expect(container.firstChild).toBeNull();
  });
});

describe('CaptainPicker', () => {
  it('disables inactive captains even when the caller allows all runtime states', () => {
    render(<CaptainPicker captains={[{ ...captain('cpt_a', 'Disabled'), active: false }, captain('cpt_b', 'Enabled')]} value={null} onChange={() => undefined} isOptionDisabled={() => false} />);
    expect(screen.getByRole('option', { name: /Disabled/ })).toBeDisabled();
    expect(screen.getByRole('option', { name: /Enabled/ })).not.toBeDisabled();
  });
  it('offers an auto option plus a labeled option per captain, and reports selection', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(
      <CaptainPicker
        captains={[captain('cpt_a', 'Alpha', 'Premium'), captain('cpt_b', 'Beta', 'Economy')]}
        value={null}
        onChange={onChange}
      />,
    );

    expect(screen.getByText('Auto (default routing)')).toBeInTheDocument();
    expect(screen.getByRole('option', { name: /Alpha/ })).toBeInTheDocument();

    await user.selectOptions(screen.getByRole('combobox'), 'cpt_b');
    expect(onChange).toHaveBeenCalledWith('cpt_b');
  });

  it('reports null when the auto option is chosen', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(<CaptainPicker captains={[captain('cpt_a', 'Alpha')]} value="cpt_a" onChange={onChange} />);
    await user.selectOptions(screen.getByRole('combobox'), '');
    expect(onChange).toHaveBeenCalledWith(null);
  });

  it('renders only the auto option when there are no captains', () => {
    render(<CaptainPicker captains={[]} value={null} onChange={() => undefined} />);
    expect(screen.getAllByRole('option')).toHaveLength(1);
  });
});

describe('FallbackTierSelect', () => {
  it('renders the tier options and reports the selection', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(<FallbackTierSelect value={null} onChange={onChange} />);
    expect(screen.getByRole('option', { name: 'Economy' })).toBeInTheDocument();
    await user.selectOptions(screen.getByRole('combobox'), 'Premium');
    expect(onChange).toHaveBeenCalledWith('Premium');
  });

  it('reports null for the auto option', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(<FallbackTierSelect value="Standard" onChange={onChange} />);
    await user.selectOptions(screen.getByRole('combobox'), '');
    expect(onChange).toHaveBeenCalledWith(null);
  });
});

import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import AssignmentBlockerCard from './AssignmentBlockerCard';
import type { MissionAssignmentBlocker } from '../../types/models';

vi.mock('../../context/LocaleContext', () => ({ useLocale: () => ({ t: (s: string) => s, formatDateTime: (s: string) => s }) }));
const base: MissionAssignmentBlocker = { reason: 'VoyageMissionLimit', summary: 'Two running, limit two.', untilUtc: null, dependsOnMissionId: null, blockingMissionIds: [], captains: [], computedUtc: '' };
it('shows a compact next-step link to the real vessel edit form', () => {
  render(<MemoryRouter><AssignmentBlockerCard blocker={{ ...base, advice: [{ text: 'Raise the limit from 2 to 3.', action: 'EditVessel', targetId: 'vsl_one' }] }} /></MemoryRouter>);
  expect(screen.getByRole('region', { name: 'Next steps' })).toHaveTextContent('Raise the limit from 2 to 3.');
  expect(screen.getByRole('link', { name: 'Edit vessel limits' })).toHaveAttribute('href', '/vessels/vsl_one?edit=1');
  expect(screen.getByRole('link')).toHaveClass('btn', 'btn-sm');
});
it('retains the remedy for a reader without a configuration action', () => {
  render(<MemoryRouter><AssignmentBlockerCard blocker={{ ...base, advice: [{ text: 'Ask an admin to review capacity.', action: null }] }} /></MemoryRouter>);
  expect(screen.getByText('Ask an admin to review capacity.')).toBeInTheDocument();
  expect(screen.queryByRole('link')).not.toBeInTheDocument();
});

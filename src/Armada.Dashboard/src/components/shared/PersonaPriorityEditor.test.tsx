import { useState } from 'react';
import { fireEvent, render, screen, within } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import PersonaPriorityEditor from './PersonaPriorityEditor';
import { enumerateModelEndpoints, listModelEndpoints } from '../../api/client';
import { asPage } from '../../test/asPage';

vi.mock('../../api/client', () => ({ listModelEndpoints: vi.fn(), enumerateModelEndpoints: vi.fn() }));
beforeEach(() => vi.mocked(enumerateModelEndpoints).mockImplementation((async () => asPage(await listModelEndpoints())) as never));
beforeEach(() => vi.mocked(listModelEndpoints).mockResolvedValue([
  { id: 'mep_one', name: 'First API', kind: 'Inference', provider: 'XAI', model: 'same-model' },
  { id: 'mep_two', name: 'Second API', kind: 'Inference', provider: 'XAI', model: 'same-model' },
  { id: 'mep_embed', name: 'Embeddings only', kind: 'Embedding', provider: 'XAI' },
] as never));
import { personaRoutingForm, validatePersonaPriorities, type PersonaRoutingForm } from '../../lib/personaPriorities';

vi.mock('../../context/LocaleContext', () => ({ useLocale: () => ({ t: (text: string, params?: Record<string, unknown>) => text.replace('{{number}}', String(params?.number ?? '')) }) }));

function Harness({ initial = personaRoutingForm() }: { initial?: PersonaRoutingForm }) {
  const [value, setValue] = useState(initial);
  return <><PersonaPriorityEditor value={value} onChange={setValue} /><output data-testid="payload">{JSON.stringify(value)}</output></>;
}

function payload(): PersonaRoutingForm { return JSON.parse(screen.getByTestId('payload').textContent!); }

describe('Persona priority editor', () => {
  it('adds a specific inference endpoint as a first-class target and edits its identity', async () => {
    render(<Harness />);
    await screen.findByRole('option', { name: 'API Endpoint: First API (XAI / same-model)' });
    expect(screen.queryByRole('option', { name: /Embeddings only/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('option', { name: 'API Endpoint' })).not.toBeInTheDocument();
    fireEvent.change(screen.getByLabelText('Add priority'), { target: { value: 'endpoint:mep_one' } });
    expect(payload().captainPriorities[0]).toEqual({ runtime: 'ApiEndpoint', endpoint: 'mep_one' });
    fireEvent.change(screen.getByLabelText('Runtime'), { target: { value: 'endpoint:mep_two' } });
    expect(payload().captainPriorities[0].endpoint).toBe('mep_two');
    fireEvent.change(screen.getByLabelText('Runtime'), { target: { value: 'Codex' } });
    expect(payload().captainPriorities[0]).toMatchObject({ runtime: 'Codex', endpoint: null });
  });

  it('preserves an existing API endpoint while options load, including a removed endpoint', async () => {
    render(<Harness initial={personaRoutingForm({ captainPriorities: [{ runtime: 'ApiEndpoint', endpoint: 'mep_removed' }] })} />);
    expect(screen.getByLabelText('Runtime')).toHaveValue('endpoint:mep_removed');
    await screen.findAllByRole('option', { name: /First API/ });
    expect(screen.getByLabelText('Endpoint')).toHaveValue('mep_removed');
    expect(validatePersonaPriorities(payload().captainPriorities)).toBeNull();
  });

  it('adds, edits, reorders, and removes structured classes', () => {
    render(<Harness />);
    fireEvent.change(screen.getByLabelText('Add priority'), { target: { value: 'Codex' } });
    fireEvent.change(screen.getByLabelText('Add priority'), { target: { value: 'Codex' } });
    const rows = screen.getAllByRole('group').slice(1);
    fireEvent.change(within(rows[1]).getByLabelText('Runtime'), { target: { value: 'Mux' } });
    fireEvent.change(within(rows[1]).getByLabelText('Model'), { target: { value: 'gpt-oss:120b' } });
    fireEvent.change(within(rows[1]).getByLabelText('Endpoint'), { target: { value: 'local' } });
    fireEvent.change(within(rows[1]).getByLabelText('Minimum tier'), { target: { value: 'Economy' } });
    fireEvent.click(within(rows[1]).getByRole('button', { name: 'Move up' }));
    expect(payload().captainPriorities[0]).toMatchObject({ runtime: 'Mux', endpoint: 'local', model: 'gpt-oss:120b', minimumTier: 'Economy' });
    fireEvent.click(screen.getAllByRole('button', { name: 'Remove priority' })[1]);
    expect(payload().captainPriorities).toHaveLength(1);
    fireEvent.click(screen.getByRole('button', { name: 'Remove priority' }));
    expect(payload().captainPriorities).toEqual([]);
  });

  it('adds the selected runtime', () => {
    render(<Harness />);
    fireEvent.change(screen.getByLabelText('Add priority'), { target: { value: 'Mux' } });
    expect(payload().captainPriorities[0].runtime).toBe('Mux');
    expect(screen.getByLabelText('Endpoint')).not.toBeDisabled();
  });

  it('adds a runtime the moment it is chosen, so a choice is never left unsaved', () => {
    render(<Harness />);
    expect(screen.getByLabelText('Add priority')).toHaveValue('');
    fireEvent.change(screen.getByLabelText('Add priority'), { target: { value: 'ClaudeCode' } });
    expect(payload().captainPriorities).toEqual([{ runtime: 'ClaudeCode' }]);
    expect(screen.getByLabelText('Add priority')).toHaveValue('');
    expect(screen.queryByRole('button', { name: 'Add priority' })).toBeNull();
  });

  it('moves to a chosen position and supports keyboard reordering', () => {
    render(<Harness initial={personaRoutingForm({ captainPriorities: [{ runtime: 'ClaudeCode' }, { runtime: 'Codex' }, { runtime: 'Mux' }] })} />);
    fireEvent.change(screen.getAllByLabelText('Position')[2], { target: { value: '0' } });
    expect(payload().captainPriorities.map(p => p.runtime)).toEqual(['Mux', 'ClaudeCode', 'Codex']);
    fireEvent.keyDown(screen.getAllByRole('button', { name: 'Drag to reorder' })[0], { key: 'ArrowDown', altKey: true });
    expect(payload().captainPriorities.map(p => p.runtime)).toEqual(['ClaudeCode', 'Mux', 'Codex']);
    fireEvent.keyDown(screen.getAllByRole('button', { name: 'Drag to reorder' })[0], { key: 'ArrowUp', altKey: true });
    expect(payload().captainPriorities[0].runtime).toBe('ClaudeCode');
  });

  it('drags matching constraints together', () => {
    render(<Harness initial={personaRoutingForm({ captainPriorities: [{ runtime: 'Codex' }, { runtime: 'Mux', model: 'gpt-oss:120b', endpoint: 'local' }] })} />);
    const transfer = { setData: vi.fn(), effectAllowed: '', dropEffect: '' };
    fireEvent.dragStart(screen.getAllByRole('button', { name: 'Drag to reorder' })[1], { dataTransfer: transfer });
    const first = screen.getByRole('group', { name: 'Priority 1' });
    fireEvent.dragOver(first, { dataTransfer: transfer });
    expect(first).toHaveClass('persona-priority-drop-target');
    fireEvent.drop(first, { dataTransfer: transfer });
    expect(payload().captainPriorities[0]).toMatchObject({ runtime: 'Mux', model: 'gpt-oss:120b', endpoint: 'local' });
    expect(first).not.toHaveClass('persona-priority-drop-target');
  });

  it('clears endpoint constraints when switching away from Mux', () => {
    render(<Harness initial={personaRoutingForm({ captainPriorities: [{ runtime: 'Mux', endpoint: 'local' }] })} />);
    fireEvent.change(screen.getByLabelText('Runtime'), { target: { value: 'Codex' } });
    expect(screen.getByLabelText('Endpoint')).toBeDisabled();
    expect(payload().captainPriorities[0].endpoint).toBeNull();
  });

  it('configures busy-class and exhausted-list policies independently', () => {
    render(<Harness />);
    fireEvent.change(screen.getByLabelText('When all priorities are unavailable'), { target: { value: 'UseEligibleCaptain' } });
    fireEvent.change(screen.getByLabelText('When a priority is unavailable'), { target: { value: 'WaitForFirstPriority' } });
    expect(screen.getByLabelText('When all priorities are unavailable')).toBeDisabled();
    expect(payload().priorityAvailability).toBe('WaitForFirstPriority');
    fireEvent.change(screen.getByLabelText('When a priority is unavailable'), { target: { value: 'FallThrough' } });
    expect(screen.getByLabelText('When all priorities are unavailable')).not.toBeDisabled();
    expect(payload().priorityExhaustion).toBe('UseEligibleCaptain');
  });

  it('keeps endpoint and model constraints on separate entries', () => {
    expect(validatePersonaPriorities([{ runtime: 'Codex' }, { runtime: 'Mux', endpoint: 'local', model: 'gpt-oss:120b' }])).toBeNull();
    expect(validatePersonaPriorities([{}])).toBeTruthy();
    expect(validatePersonaPriorities([{ runtime: 'Codex', endpoint: 'local' }])).toBeTruthy();
    expect(validatePersonaPriorities([{ runtime: 'unknown' }])).toBeTruthy();
    expect(validatePersonaPriorities([{ minimumTier: 'unknown' }])).toBeTruthy();
    expect(validatePersonaPriorities([{ model: ' ' }])).toBeTruthy();
  });

  it('copies routing lists instead of mutating loaded personas', () => {
    const original = { captainPriorities: [{ runtime: 'Mux', endpoint: 'local' }] };
    const draft = personaRoutingForm(original);
    draft.captainPriorities[0].endpoint = 'other';
    expect(original.captainPriorities[0].endpoint).toBe('local');
  });
});

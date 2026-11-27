import { expect, it } from 'vitest';
import type { Persona } from '../types/models';
import { seedStepAssignments, captainAssignmentOverrides, buildDispatchVoyageRequest } from './dispatchRequest';

it('sends a read-only mode on every mission and omits the Implementation default', () => {
  const draft = { vesselId: 'vsl_1', prompt: 'Audit the parser', priority: 100, voyageTitle: '', objectiveId: '', pipeline: '', selectedPlaybooks: [], stepAssignments: {}, multiTaskTitle: 'Multi' };
  expect(buildDispatchVoyageRequest({ ...draft, mode: 'Audit' }).missions[0].mode).toBe('Audit');
  expect('mode' in buildDispatchVoyageRequest({ ...draft, mode: 'Implementation' }).missions[0]).toBe(false);
  expect('mode' in buildDispatchVoyageRequest(draft).missions[0]).toBe(false);
});

it('keeps personas with priorities on automatic routing despite a stored legacy default', () => {
  const persona = { name: 'Architect', defaultCaptainId: 'cpt_legacy', captainPriorities: [{ runtime: 'Mux' }] } as Persona;
  expect(seedStepAssignments({}, ['Architect'], [persona]).Architect.captainId).toBeNull();
  expect(seedStepAssignments({ Architect: { captainId: 'cpt_explicit', fallbackTier: null } }, ['Architect'], [persona]).Architect.captainId).toBe('cpt_explicit');
});

it('still prefills the legacy default for an empty hierarchy', () => {
  const persona = { name: 'Architect', defaultCaptainId: 'cpt_legacy', captainPriorities: [] } as unknown as Persona;
  expect(seedStepAssignments({}, ['Architect'], [persona]).Architect.captainId).toBe('cpt_legacy');
});

it('preserves strict captain selection only for an explicit captain', () => {
  expect(captainAssignmentOverrides({ Worker: { captainId: 'cpt_deepseek', fallbackTier: 'Standard', strictCaptain: true } }))
    .toEqual([{ persona: 'Worker', captainId: 'cpt_deepseek', fallbackTier: 'Standard', strictCaptain: true }]);
  expect(captainAssignmentOverrides({ Worker: { captainId: null, fallbackTier: 'Standard', strictCaptain: true } }))
    .toEqual([{ persona: 'Worker', captainId: null, fallbackTier: 'Standard' }]);
});

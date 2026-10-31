import { describe, expect, it } from 'vitest';
import { buildVesselPayload, buildWorkerEnvelope, emptyVesselForm, findLandingMode, getLandingModes, vesselToForm } from './vesselForm';
import type { Vessel } from '../types/models';

const identity = (text: string) => text;

describe('getLandingModes', () => {
  const modes = getLandingModes(identity);

  it('lists the modes in the UI order with Merge and Push after Local Merge', () => {
    expect(modes.map((m) => m.value)).toEqual(['', 'LocalMerge', 'MergeAndPush', 'PullRequest', 'MergeQueue', 'None']);
  });

  it('says Local Merge does not push', () => {
    const local = findLandingMode(modes, 'LocalMerge');
    expect(local.label).toBe('Local Merge -- into your working directory, no push');
    expect(local.short).toBe('local, no push');
    expect(local.description).toContain('Nothing is pushed.');
    expect(local.description).not.toMatch(/pushes/);
  });

  it('offers Merge and Push with its own labels', () => {
    const push = findLandingMode(modes, 'MergeAndPush');
    expect(push.value).toBe('MergeAndPush');
    expect(push.label).toBe('Merge and Push -- verify and push to the remote');
    expect(push.short).toBe('local + push');
    expect(push.description).toBe("Integrates and verifies the mission in its dock, then pushes the verified commit to the remote target branch. A dirty working directory does not block publication.");
  });

  it('translates every label through t', () => {
    const tagged = getLandingModes((text) => `[${text}]`);
    expect(findLandingMode(tagged, 'MergeAndPush').label).toBe('[Merge and Push -- verify and push to the remote]');
    expect(findLandingMode(tagged, 'MergeAndPush').short).toBe('[local + push]');
  });
});

describe('serializeOverlappingMissions', () => {
  it('is off for a new vessel and sent on create', () => {
    expect(emptyVesselForm.serializeOverlappingMissions).toBe(false);
    expect(buildVesselPayload({ ...emptyVesselForm, name: 'v' }, null).serializeOverlappingMissions).toBe(false);
  });

  it('round-trips through the form and the payload', () => {
    const stored = { id: 'vsl_1', name: 'Core', defaultBranch: 'main', enableModelContext: true, allowConcurrentMissions: true, serializeOverlappingMissions: true } as unknown as Vessel;
    const form = vesselToForm(stored);
    expect(form.serializeOverlappingMissions).toBe(true);
    expect(buildVesselPayload({ ...form, serializeOverlappingMissions: false }, stored).serializeOverlappingMissions).toBe(false);
  });

  it('treats a vessel from an older server (no field) as off', () => {
    const legacy = { id: 'vsl_2', name: 'Old', defaultBranch: 'main', enableModelContext: true, allowConcurrentMissions: false } as unknown as Vessel;
    expect(vesselToForm(legacy).serializeOverlappingMissions).toBe(false);
  });
});

describe('workerEnvelope', () => {
  it('is null for a new vessel and when every field is blank', () => {
    expect(buildVesselPayload({ ...emptyVesselForm, name: 'v' }, null).workerEnvelope).toBeNull();
    expect(buildWorkerEnvelope({ ...emptyVesselForm, envelopeMaxFiles: '  ', envelopeMaxSteps: '', envelopeMinServedContextTokens: '' })).toBeNull();
  });

  it('clears a stored envelope when the form is blanked on edit', () => {
    const stored = { id: 'vsl_1', name: 'Core', defaultBranch: 'main', enableModelContext: true, allowConcurrentMissions: false, workerEnvelope: { maxFiles: 5, requiredTools: ['mcp'] } } as unknown as Vessel;
    const form = { ...vesselToForm(stored), envelopeMaxFiles: '', envelopeRequireMcp: false };
    expect(buildVesselPayload(form, stored).workerEnvelope).toBeNull();
  });

  it('builds the payload from populated fields', () => {
    const form = { ...emptyVesselForm, name: 'v', envelopeMaxFiles: '8', envelopeMaxSteps: '40', envelopeMinServedContextTokens: '64000', envelopeRequireTools: true, envelopeRequireMcp: true };
    expect(buildVesselPayload(form, null).workerEnvelope).toEqual({ maxFiles: 8, maxSteps: 40, minServedContextTokens: 64000, requiredTools: ['tools', 'mcp'] });
  });

  it('sends a tools-only envelope with null counts', () => {
    expect(buildWorkerEnvelope({ ...emptyVesselForm, envelopeRequireMcp: true })).toEqual({ maxFiles: null, maxSteps: null, minServedContextTokens: null, requiredTools: ['mcp'] });
  });

  it('round-trips a stored envelope through the form and the payload', () => {
    const stored = { id: 'vsl_1', name: 'Core', defaultBranch: 'main', enableModelContext: true, allowConcurrentMissions: false, workerEnvelope: { maxFiles: 3, maxSteps: null, minServedContextTokens: 128000, requiredTools: ['tools'] } } as unknown as Vessel;
    const form = vesselToForm(stored);
    expect(form.envelopeMaxFiles).toBe('3');
    expect(form.envelopeMaxSteps).toBe('');
    expect(form.envelopeMinServedContextTokens).toBe('128000');
    expect(form.envelopeRequireTools).toBe(true);
    expect(form.envelopeRequireMcp).toBe(false);
    expect(buildVesselPayload(form, stored).workerEnvelope).toEqual({ maxFiles: 3, maxSteps: null, minServedContextTokens: 128000, requiredTools: ['tools'] });
  });

  it('reads a vessel without the field as no envelope', () => {
    const stored = { id: 'vsl_1', name: 'Core', defaultBranch: 'main', enableModelContext: true, allowConcurrentMissions: false } as unknown as Vessel;
    expect(buildVesselPayload(vesselToForm(stored), stored).workerEnvelope).toBeNull();
  });
});

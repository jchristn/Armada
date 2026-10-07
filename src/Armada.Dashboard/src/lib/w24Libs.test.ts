import { describe, expect, it } from 'vitest';
import type { Persona, Pipeline } from '../types/models';
import {
  ALL_STEPS_PERSONA,
  buildDispatchVoyageRequest,
  captainAssignmentOverrides,
  dispatchPrefillNotice,
  effectiveStepPersonas,
  hasDispatchPrefill,
  parseDispatchPriority,
  pipelineStepPersonas,
  seedStepAssignments,
} from './dispatchRequest';
import { parseDockGitAnchors } from './dockAnchors';
import { formatEventPayload } from './eventPayload';
import { buildEnqueueMergeRequest, emptyEnqueueMergeForm } from './mergeQueueForm';
import { formatInputProvider, readinessLabel, readinessTone } from './readiness';
import { SIGNAL_TYPES, buildSendSignalRequest, formatSignalPayload, signalListFilters } from './signals';
import type { VesselReadinessResult } from '../types/models';

const pipeline = {
  id: 'ppl_1',
  name: 'Reviewed',
  stages: [
    { id: 's2', pipelineId: 'ppl_1', order: 2, personaName: 'Judge', isOptional: false, description: null, requiresReview: false, reviewDenyAction: 'RetryStage' },
    { id: 's1', pipelineId: 'ppl_1', order: 1, personaName: 'Worker', isOptional: false, description: null, requiresReview: false, reviewDenyAction: 'RetryStage' },
    { id: 's3', pipelineId: 'ppl_1', order: 3, personaName: 'Worker', isOptional: false, description: null, requiresReview: false, reviewDenyAction: 'RetryStage' },
  ],
} as unknown as Pipeline;

describe('dispatchRequest', () => {
  it('lists distinct step personas in stage order, or the wildcard step', () => {
    expect(pipelineStepPersonas(pipeline)).toEqual(['Worker', 'Judge']);
    expect(pipelineStepPersonas(null)).toEqual([]);
    expect(effectiveStepPersonas([])).toEqual([ALL_STEPS_PERSONA]);
    expect(effectiveStepPersonas(['Worker'])).toEqual(['Worker']);
  });

  it('seeds new steps from persona defaults and keeps existing choices', () => {
    const personas = [{ name: 'Worker', defaultCaptainId: 'cpt_w' }, { name: 'Judge' }] as unknown as Persona[];
    const seeded = seedStepAssignments({ Judge: { captainId: 'cpt_j', fallbackTier: 'Premium' } }, ['Worker', 'Judge'], personas);
    expect(seeded).toEqual({
      Worker: { captainId: 'cpt_w', fallbackTier: null },
      Judge: { captainId: 'cpt_j', fallbackTier: 'Premium' },
    });
  });

  it('describes prefills', () => {
    expect(hasDispatchPrefill({ fromVessel: true, vesselId: 'v' })).toBe(true);
    expect(hasDispatchPrefill(null)).toBe(false);
    expect(dispatchPrefillNotice({ fromVessel: true })).toBeNull();
    expect(dispatchPrefillNotice({ fromObjective: true, fromPlanning: true })).toContain('backlog item');
    expect(dispatchPrefillNotice({ fromIncident: true })).toContain('incident');
  });

  it('builds the createVoyage request', () => {
    const request = buildDispatchVoyageRequest({
      vesselId: 'vsl_1',
      prompt: '  Fix the login bug  ',
      priority: 50,
      voyageTitle: '',
      objectiveId: '',
      pipeline: '',
      selectedPlaybooks: [],
      stepAssignments: { '*': { captainId: null, fallbackTier: null } },
      multiTaskTitle: 'Multi',
    });
    expect(request).toEqual({
      title: 'Fix the login bug',
      vesselId: 'vsl_1',
      missions: [{ vesselId: 'vsl_1', title: 'Fix the login bug', description: 'Fix the login bug', priority: 50 }],
    });
    const full = buildDispatchVoyageRequest({
      vesselId: 'vsl_1', prompt: 'x'.repeat(100), priority: 100, voyageTitle: ' Named ', objectiveId: 'obj_1', pipeline: 'Reviewed',
      selectedPlaybooks: [{ playbookId: 'pbk_1', deliveryMode: 'InlineFullContent' }],
      stepAssignments: { Worker: { captainId: 'cpt_1', fallbackTier: null }, Judge: { captainId: null, fallbackTier: 'Standard' } },
      multiTaskTitle: 'Multi',
    });
    expect(full.title).toBe('Named');
    expect(full.missions[0].title).toHaveLength(80);
    expect(full.objectiveId).toBe('obj_1');
    expect(full.pipeline).toBe('Reviewed');
    expect(full.selectedPlaybooks).toHaveLength(1);
    expect(full.captainAssignments).toEqual([
      { persona: 'Worker', captainId: 'cpt_1', fallbackTier: null },
      { persona: 'Judge', captainId: null, fallbackTier: 'Standard' },
    ]);
    expect(captainAssignmentOverrides({ x: { captainId: null, fallbackTier: null } })).toEqual([]);
  });

  it('parses priority like the page', () => {
    expect(parseDispatchPriority('42')).toBe(42);
    expect(parseDispatchPriority('')).toBe(100);
    expect(parseDispatchPriority('0')).toBe(100);
  });
});

describe('signals', () => {
  it('builds list filters', () => {
    expect(signalListFilters({ type: '', toCaptainId: '', unreadOnly: false, userId: '' })).toEqual({});
    expect(signalListFilters({ type: 'Mail', toCaptainId: 'cpt_1', unreadOnly: true, userId: 'usr_1' }))
      .toEqual({ type: 'Mail', toCaptainId: 'cpt_1', unreadOnly: 'true', userId: 'usr_1' });
  });

  it('builds the send request', () => {
    expect(buildSendSignalRequest({ type: '', payload: '', toCaptainId: '' })).toEqual({ type: 'Nudge', payload: undefined, toCaptainId: undefined });
    expect(buildSendSignalRequest({ type: 'Mail', payload: 'hi', toCaptainId: 'cpt_1' })).toEqual({ type: 'Mail', payload: 'hi', toCaptainId: 'cpt_1' });
    expect(SIGNAL_TYPES[0]).toBe('Nudge');
  });

  it('formats payloads', () => {
    expect(formatSignalPayload(null, '(empty)')).toEqual({ isJson: false, formatted: '(empty)' });
    expect(formatSignalPayload('{"a":1}', '')).toEqual({ isJson: true, formatted: '{\n  "a": 1\n}' });
    expect(formatSignalPayload('plain', '')).toEqual({ isJson: false, formatted: 'plain' });
  });
});

describe('docks, events, merge queue, readiness', () => {
  it('parses dock anchors', () => {
    expect(parseDockGitAnchors(null)).toBeNull();
    expect(parseDockGitAnchors('nope')).toBeNull();
    expect(parseDockGitAnchors('{"startCommit":"abc"}')).toEqual({ startCommit: 'abc' });
  });

  it('formats event payloads', () => {
    expect(formatEventPayload(null)).toBeNull();
    expect(formatEventPayload('{"a":1}')).toBe('{\n  "a": 1\n}');
    expect(formatEventPayload('raw')).toBe('raw');
    expect(formatEventPayload({ b: 2 })).toBe('{\n  "b": 2\n}');
  });

  it('builds enqueue requests', () => {
    expect(emptyEnqueueMergeForm()).toEqual({ branchName: '', targetBranch: 'main', missionId: '', vesselId: '', testCommand: '', priority: 0 });
    expect(buildEnqueueMergeRequest({ ...emptyEnqueueMergeForm(), branchName: 'feat', targetBranch: '' }))
      .toEqual({ branchName: 'feat', targetBranch: 'main', missionId: undefined, vesselId: undefined, testCommand: undefined, priority: 0 });
  });

  it('labels readiness', () => {
    expect(readinessTone(null)).toBe('warning');
    expect(readinessLabel(null)).toBe('Unknown');
    const r = (errorCount: number, warningCount: number) => ({ errorCount, warningCount } as unknown as VesselReadinessResult);
    expect(readinessTone(r(1, 0))).toBe('error');
    expect(readinessLabel(r(0, 2))).toBe('Needs Attention');
    expect(readinessLabel(r(0, 0))).toBe('Ready');
    expect(formatInputProvider('OnePassword')).toBe('1Password');
    expect(formatInputProvider(null)).toBe('Input');
  });
});

import { describe, expect, it } from 'vitest';
import { isMultiSlotRuntime, planSetupCaptains } from './setupWizard';

describe('planSetupCaptains', () => {
  it('creates that many single-slot captains for a CLI runtime', () => {
    expect(planSetupCaptains(' Setup Captain ', 'ClaudeCode', 3)).toEqual([
      { name: 'Setup Captain', maxConcurrency: 1 },
      { name: 'Setup Captain 2', maxConcurrency: 1 },
      { name: 'Setup Captain 3', maxConcurrency: 1 },
    ]);
  });

  it('creates one multi-slot captain for an endpoint runtime', () => {
    expect(planSetupCaptains('Endpoint', 'ApiEndpoint', 4)).toEqual([{ name: 'Endpoint', maxConcurrency: 4 }]);
    expect(planSetupCaptains('Mux', 'Mux', 2)).toEqual([{ name: 'Mux', maxConcurrency: 2 }]);
    expect(isMultiSlotRuntime('Codex')).toBe(false);
  });

  it('clamps the count to 1..16', () => {
    expect(planSetupCaptains('A', 'Codex', 0)).toHaveLength(1);
    expect(planSetupCaptains('A', 'Codex', Number.NaN)).toHaveLength(1);
    expect(planSetupCaptains('A', 'Codex', 40)).toHaveLength(16);
    expect(planSetupCaptains('A', 'Mux', 40)[0].maxConcurrency).toBe(16);
  });
});

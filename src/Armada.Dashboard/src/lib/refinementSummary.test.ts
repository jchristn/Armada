import { describe, expect, it } from 'vitest';
import { refinementSummaryFromEvent } from './refinementSummary';

describe('refinementSummaryFromEvent', () => {
  it('reads the draft nested under summary, as the Admiral broadcasts it', () => {
    const draft = refinementSummaryFromEvent({
      sessionId: 'ors_1',
      messageId: 'orm_1',
      summary: {
        sessionId: 'ors_1', messageId: 'orm_1', summary: 'Retry with backoff.',
        acceptanceCriteria: ['Retries 3 times'], nonGoals: [], rolloutConstraints: ['Behind a flag'],
        suggestedPipelineId: null, method: 'runtime-json',
      },
    });
    expect(draft).not.toBeNull();
    expect(draft!.sessionId).toBe('ors_1');
    expect(draft!.messageId).toBe('orm_1');
    expect(draft!.summary).toBe('Retry with backoff.');
    expect(draft!.acceptanceCriteria).toEqual(['Retries 3 times']);
    expect(draft!.rolloutConstraints).toEqual(['Behind a flag']);
    expect(draft!.method).toBe('runtime-json');
  });

  it('always returns arrays for the list fields, so rendering them cannot throw', () => {
    const draft = refinementSummaryFromEvent({ sessionId: 'ors_1', messageId: null, summary: { summary: 'Plan', method: 'assistant-fallback' } });
    expect(draft!.acceptanceCriteria).toEqual([]);
    expect(draft!.nonGoals).toEqual([]);
    expect(draft!.rolloutConstraints).toEqual([]);
    expect(() => draft!.acceptanceCriteria.map((x) => x)).not.toThrow();
  });

  it('rejects events without a draft or a session', () => {
    expect(refinementSummaryFromEvent(undefined)).toBeNull();
    expect(refinementSummaryFromEvent({ sessionId: 'ors_1' })).toBeNull();
    expect(refinementSummaryFromEvent({ summary: { summary: 'x' } })).toBeNull();
  });
});

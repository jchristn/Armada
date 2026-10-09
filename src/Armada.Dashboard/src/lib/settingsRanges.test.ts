import { describe, expect, it } from 'vitest';
import { ASK_DEFAULTS, ASK_FIELDS, ASK_NUMBER_FIELDS, ASK_RANGES, askDraftFrom, askSettingsPayload, validateAskDraft, type AskDraft } from './settingsRanges';

const draft = (overrides: Partial<AskDraft> = {}): AskDraft => ({ ...askDraftFrom(ASK_DEFAULTS), ...overrides });

describe('Ask settings ranges', () => {
  it('mirror the server clamps and defaults', () => {
    expect(ASK_RANGES.historyTurns).toMatchObject({ min: 2, max: 200 });
    expect(ASK_RANGES.proposalExpiryMinutes).toMatchObject({ min: 1, max: 1440 });
    expect(ASK_RANGES.trackerIntervalSeconds).toMatchObject({ min: 2, max: 300 });
    expect(ASK_RANGES.narrationTimeoutSeconds).toMatchObject({ min: 10, max: 600 });
    expect(ASK_RANGES.turnTimeoutMinutes).toMatchObject({ min: 1, max: 120 });
    expect(ASK_DEFAULTS).toEqual({
      historyTurns: 20,
      proposalExpiryMinutes: 60,
      trackerIntervalSeconds: 5,
      narrateMilestones: true,
      reportResultsOnCompletion: true,
      captainAutoApprove: false,
      narrationTimeoutSeconds: 60,
      turnTimeoutMinutes: 15,
    });
  });

  it('edits every field but captainAutoApprove', () => {
    expect(ASK_FIELDS).not.toContain('captainAutoApprove');
    expect([...ASK_FIELDS].sort()).toEqual([...ASK_NUMBER_FIELDS, 'narrateMilestones', 'reportResultsOnCompletion'].sort());
  });

  it('accepts each field at its bounds', () => {
    expect(validateAskDraft(draft())).toEqual({});
    for (const field of ASK_NUMBER_FIELDS) {
      expect(validateAskDraft(draft({ [field]: String(ASK_RANGES[field].min) }))).toEqual({});
      expect(validateAskDraft(draft({ [field]: String(ASK_RANGES[field].max) }))).toEqual({});
    }
  });

  it('rejects below, above, fractional, and empty values', () => {
    for (const field of ASK_NUMBER_FIELDS) {
      const range = ASK_RANGES[field];
      for (const bad of [String(range.min - 1), String(range.max + 1), '2.5', '', 'abc']) {
        expect(validateAskDraft(draft({ [field]: bad }))[field]).toBe('range');
      }
    }
  });

  it('builds the full payload and carries captainAutoApprove over from the server', () => {
    const fromServer = { ...ASK_DEFAULTS, captainAutoApprove: true, historyTurns: 40 };
    const edited = { ...askDraftFrom(fromServer), reportResultsOnCompletion: false, turnTimeoutMinutes: '30' };
    expect(askSettingsPayload(edited, fromServer)).toEqual({ ...fromServer, reportResultsOnCompletion: false, turnTimeoutMinutes: 30 });
  });

  it('uses the defaults when an older server sends no Ask group', () => {
    expect(askDraftFrom(undefined)).toEqual(askDraftFrom(ASK_DEFAULTS));
    expect(askSettingsPayload(askDraftFrom(null), undefined)).toEqual(ASK_DEFAULTS);
  });
});

import { describe, expect, it } from 'vitest';
import { STATUS_DESCRIPTIONS, statusDescription } from './statusDescriptions';

describe('statusDescription', () => {
  it('looks statuses up case-insensitively', () => {
    expect(statusDescription('LandingFailed')).toBe(STATUS_DESCRIPTIONS.landingfailed);
    expect(statusDescription('REVIEW')).toContain('ready for review');
  });

  it('returns an empty string for unknown or missing statuses', () => {
    expect(statusDescription('NoSuchStatus')).toBe('');
    expect(statusDescription(null)).toBe('');
    expect(statusDescription(undefined)).toBe('');
  });
});

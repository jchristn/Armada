import { describe, expect, it } from 'vitest';
import { usageWindowLabel } from './usageWindowLabel';
import { translateTemplate } from '../i18n/runtime';

const t = (message: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', message, null, params);

describe('usage window labels', () => {
  it.each([
    ['seven_day', null, 'Weekly limit'],
    ['five_hour', null, '5-hour limit'],
    ['codex:primary', 300, '5-hour limit'],
    ['codex:secondary', 10080, 'Weekly limit'],
    ['codex:primary', 1440, 'Daily limit'],
    ['codex:primary', 120, '2-hour limit'],
    ['codex:secondary', 2880, '2-day limit'],
    ['codex:primary', 90, '90-minute limit'],
    ['codex:primary', null, 'Primary limit'],
    ['codex:secondary', null, 'Secondary limit'],
    ['codex:primary', -1, 'Primary limit'],
    ['quota_REQUESTS', null, 'Quota requests'],
    ['custom_bucket:extra', null, 'Custom bucket extra'],
    ['', null, 'Usage limit'],
  ])('renders %s with its reported duration (%s)', (name, windowMinutes, expected) => {
    expect(usageWindowLabel({ name, windowMinutes }, t)).toBe(expected);
  });

  it('retains model scope once so a family limit is not mistaken for the whole account', () => {
    expect(usageWindowLabel({ name: 'seven_day_sonnet', model: 'sonnet' }, t)).toBe('Weekly limit (Sonnet)');
    expect(usageWindowLabel({ name: 'seven_day_opus' }, t)).toBe('Weekly limit (Opus)');
  });
});

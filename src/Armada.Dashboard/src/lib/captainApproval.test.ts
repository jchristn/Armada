import { describe, expect, it } from 'vitest';
import { applyAutoApprove, autoApproveFromCaptain, supportsAutoApproveSwitch } from './captainApproval';

describe('captain auto-approve switch', () => {
  it('defaults to true', () => {
    expect(autoApproveFromCaptain(null)).toBe(true);
    expect(autoApproveFromCaptain({ runtimeOptionsJson: null })).toBe(true);
    expect(autoApproveFromCaptain({ runtimeOptionsJson: '{"endpoint":"x"}' })).toBe(true);
    expect(autoApproveFromCaptain({ runtimeOptionsJson: 'not json' })).toBe(true);
  });

  it('reads and writes false while keeping other keys', () => {
    const json = applyAutoApprove('{"endpoint":"local"}', false);
    expect(JSON.parse(json!)).toEqual({ endpoint: 'local', autoApprove: false });
    expect(autoApproveFromCaptain({ runtimeOptionsJson: json })).toBe(false);
    expect(applyAutoApprove(json, true)).toBe('{"endpoint":"local"}');
    expect(applyAutoApprove(null, true)).toBeNull();
  });

  it('applies to CLI runtimes only', () => {
    expect(supportsAutoApproveSwitch('ClaudeCode')).toBe(true);
    expect(supportsAutoApproveSwitch('ApiEndpoint')).toBe(false);
  });
});

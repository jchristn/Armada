import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import ReliabilitySettings, { RELIABILITY_LIMITS, reliabilityPayload, toReliabilityDraft, validateReliabilityDraft } from './ReliabilitySettings';
import { updateSettings } from '../../api/client';
import { translateTemplate } from '../../i18n/runtime';

vi.mock('../../api/client', () => ({ updateSettings: vi.fn() }));
const localeValue = {
  t: (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', text, null, params),
  locale: 'en',
};
vi.mock('../../context/LocaleContext', () => ({ useLocale: () => localeValue }));

describe('ReliabilitySettings', () => {
  beforeEach(() => { vi.mocked(updateSettings).mockReset().mockResolvedValue({}); });

  it('fills defaults for settings the server did not send', () => {
    const d = toReliabilityDraft({});
    expect(d.timeoutScaleReferenceTokensPerSecond).toBe('30');
    expect(d.promptVariantCompactMaxContextTokens).toBe('32768');
    expect(d.mergeQueueBatchSize).toBe('1');
    expect(d.rankByOutcomes).toBe(false);
    expect(d.suggestTiers).toBe(false);
    expect(d.minimumSamples).toBe('20');
    expect(validateReliabilityDraft(d)).toEqual({});
  });

  it('rejects values outside the server limits', () => {
    const d = toReliabilityDraft({});
    for (const [field, { min, max }] of Object.entries(RELIABILITY_LIMITS)) {
      expect(validateReliabilityDraft({ ...d, [field]: String(max + 1) })).toHaveProperty(field);
      expect(validateReliabilityDraft({ ...d, [field]: String(min - 1) })).toHaveProperty(field);
      expect(validateReliabilityDraft({ ...d, [field]: '1.5' })).toHaveProperty(field);
    }
  });

  it('saves the section with RoutingData sent whole', async () => {
    const onSaved = vi.fn();
    const notify = vi.fn();
    render(<ReliabilitySettings settings={{ maxEscalations: 2, routingData: { rankByOutcomes: true, suggestTiers: false, minimumSamples: 20, halfLifeDays: 14 } }} locked={false} onSaved={onSaved} notify={notify} />);
    const save = screen.getByRole('button', { name: 'Save Reliability Settings' });
    expect(save).toBeDisabled();
    fireEvent.change(screen.getByLabelText('Maximum escalations'), { target: { value: '9' } });
    expect(save).toBeDisabled();
    expect(screen.getByText('Must be a whole number from 0 to 5.')).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText('Maximum escalations'), { target: { value: '4' } });
    fireEvent.click(screen.getByLabelText('Suggest model profile tiers'));
    fireEvent.click(save);
    await waitFor(() => expect(updateSettings).toHaveBeenCalled());
    const body = vi.mocked(updateSettings).mock.calls[0][0];
    expect(body).toMatchObject({ maxEscalations: 4, routingData: { rankByOutcomes: true, suggestTiers: true, minimumSamples: 20, halfLifeDays: 14 } });
    expect(onSaved).toHaveBeenCalled();
    expect(notify).toHaveBeenCalledWith('success', 'Reliability settings saved and applied.');
  });

  it('builds a payload with every 1.5.0 setting', () => {
    const body = reliabilityPayload(toReliabilityDraft({}));
    expect(Object.keys(body).sort()).toEqual([
      'escalateOnOpenEndpointBreaker', 'judgeReworkRounds', 'maxEscalations', 'mergeQueueBatchSize', 'mergeQueueLeaseSeconds',
      'mergeQueueRequeueLimit', 'promptVariantCompactMaxContextTokens', 'promptVariantLeanMaxContextTokens', 'routingData',
      'timeoutScaleReferenceTokensPerSecond', 'unclassifiedResumeLimit',
    ]);
  });
});

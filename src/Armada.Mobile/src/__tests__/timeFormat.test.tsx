import { render } from '@testing-library/react-native';
import { LocaleProvider, normalizeUtc, useLocale, type LocaleState } from '../i18n/LocaleContext';

let locale: LocaleState | null = null;
function Probe() {
  locale = useLocale();
  return null;
}

describe('server timestamps on Hermes', () => {
  it('trims .NET fractional seconds to milliseconds', () => {
    expect(normalizeUtc('2026-10-07T21:13:43.5922721Z')).toBe('2026-10-07T21:13:43.592Z');
    expect(normalizeUtc('2026-10-07T21:13:43.59Z')).toBe('2026-10-07T21:13:43.59Z');
    expect(normalizeUtc('2026-10-07T21:13:43Z')).toBe('2026-10-07T21:13:43Z');
    expect(normalizeUtc(null)).toBeNull();
  });

  it('never shows the raw ISO text when relative formatting is unavailable', async () => {
    await render(<LocaleProvider serverUrl={null} bundledCatalog={() => ({ defaultLocale: 'en', supportedLocales: [], locales: {} })}><Probe /></LocaleProvider>);
    const utc = '2026-10-07T21:13:43.592272Z';
    expect(locale!.formatRelativeTime(utc)).not.toContain('T21:13');
    const original = Intl.RelativeTimeFormat;
    Object.defineProperty(Intl, 'RelativeTimeFormat', { value: undefined, configurable: true, writable: true });
    try {
      expect(locale!.formatRelativeTime(utc)).not.toContain('T21:13');
    } finally {
      Object.defineProperty(Intl, 'RelativeTimeFormat', { value: original, configurable: true, writable: true });
    }
  });
});

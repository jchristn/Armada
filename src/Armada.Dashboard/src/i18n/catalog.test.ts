import { describe, expect, it, vi, afterEach } from 'vitest';
import { fetchCatalog, pickInitialLocale } from './catalog';

describe('pickInitialLocale', () => {
  it('a stored choice wins over device preferences', () => {
    expect(pickInitialLocale('de', ['fr-FR'], null)).toBe('de');
  });

  it('falls back to the first preferred locale, normalized by alias', () => {
    expect(pickInitialLocale(null, ['zh-TW', 'en-US'], null)).toBe('zh-Hant');
  });

  it('falls back to the default when there are no preferences', () => {
    expect(pickInitialLocale(null, [], null)).toBe('en');
  });
});

describe('fetchCatalog', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('resolves null instead of rejecting when the catalog is missing', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('', { status: 404 })));
    await expect(fetchCatalog('http://x/dashboard/i18n/armada.json')).resolves.toBeNull();
  });
});

describe('date parsing and relative time', () => {
  it('parses seven fractional digits (server timestamps) to the millisecond', async () => {
    const { parseUtcMs } = await import('./catalog');
    expect(parseUtcMs('2026-10-07T21:18:12.9270491Z')).toBe(Date.parse('2026-10-07T21:18:12.927Z'));
    expect(parseUtcMs('2026-10-07T21:18:12Z')).toBe(Date.parse('2026-10-07T21:18:12Z'));
  });

  it('falls back to English when the engine has no Intl.RelativeTimeFormat (Hermes)', async () => {
    const { formatRelativeFromUtc } = await import('./catalog');
    const original = Intl.RelativeTimeFormat;
    const fiveMinutesAgo = new Date(Date.now() - 5 * 60 * 1000).toISOString();
    try {
      (Intl as unknown as { RelativeTimeFormat: unknown }).RelativeTimeFormat = undefined;
      expect(formatRelativeFromUtc('en', fiveMinutesAgo)).toBe('5 minutes ago');
    } finally {
      (Intl as unknown as { RelativeTimeFormat: unknown }).RelativeTimeFormat = original;
    }
    expect(formatRelativeFromUtc('en', fiveMinutesAgo)).toBe('5 minutes ago');
  });
});

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

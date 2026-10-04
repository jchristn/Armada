import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import LanguageSelector from './LanguageSelector';
import { LocaleProvider } from '../../context/LocaleContext';
import { DEFAULT_LOCALES, localeOptionLabel } from '../../i18n/runtime';
import type { I18nCatalog } from '../../i18n/runtime';
import catalogSource from '../../../../Armada.Server/wwwroot/i18n/armada.json?raw';

describe('LanguageSelector beta labels (decision D4)', () => {
  it('labels every non-English locale beta in its own language and leaves English unlabeled', () => {
    const labels = DEFAULT_LOCALES.map(localeOptionLabel);
    expect(labels[0]).toBe('English');
    const nonEnglish = DEFAULT_LOCALES.filter((l) => l.code !== 'en');
    expect(nonEnglish).toHaveLength(8);
    for (const meta of nonEnglish) {
      expect(meta.beta).toBe(true);
      expect(localeOptionLabel(meta)).toBe(`${meta.nativeLabel} (${meta.betaLabel})`);
    }
    expect(localeOptionLabel(DEFAULT_LOCALES.find((l) => l.code === 'de')!)).toBe('Deutsch (Beta)');
    expect(localeOptionLabel(DEFAULT_LOCALES.find((l) => l.code === 'ja')!)).toBe('日本語 (ベータ版)');
  });

  it('falls back to the English word when a beta locale has no label', () => {
    expect(localeOptionLabel({ code: 'xx', label: 'X', nativeLabel: 'Xx', dir: 'ltr', beta: true })).toBe('Xx (beta)');
  });

  it('renders the beta suffix in the picker options', () => {
    render(<LocaleProvider><LanguageSelector /></LocaleProvider>);
    const options = screen.getAllByRole('option').map((o) => o.textContent);
    expect(options).toContain('English');
    expect(options).toContain('Français (bêta)');
    expect(options.filter((o) => o?.includes('('))).toHaveLength(8);
  });

  it('marks the same eight locales beta in the shipped catalog', () => {
    const catalog = JSON.parse(catalogSource) as I18nCatalog;
    for (const meta of catalog.supportedLocales) {
      const fallback = DEFAULT_LOCALES.find((l) => l.code === meta.code)!;
      expect(!!meta.beta).toBe(meta.code !== 'en');
      expect(meta.betaLabel).toBe(fallback.betaLabel);
    }
  });
});

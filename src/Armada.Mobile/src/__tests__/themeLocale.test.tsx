import AsyncStorage from '@react-native-async-storage/async-storage';
import { act, render, screen, userEvent, waitFor, within } from '@testing-library/react-native';
import type { I18nCatalog } from '@dashboard/i18n/catalog';
import { LocalePicker } from '../components/app/LocalePicker';
import { ThemePicker } from '../components/app/ThemePicker';
import { LocaleProvider, loadBundledCatalog, useLocale, type LocaleState } from '../i18n/LocaleContext';
import { PREF_KEYS } from '../storage/prefs';
import { ThemeProvider, useTheme, type ThemeState } from '../theme/ThemeContext';
import { palettes, resolveTheme, type ThemeName } from '../theme/palette';

const mockScheme: { value: 'light' | 'dark' } = { value: 'light' };
jest.mock('react-native/Libraries/Utilities/useColorScheme', () => ({ __esModule: true, default: () => mockScheme.value }));

/** WCAG relative luminance contrast ratio between two #rrggbb colors. */
function contrast(a: string, b: string): number {
  const lum = (hex: string) => {
    const [r, g, bl] = [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16) / 255)
      .map((c) => (c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4));
    return 0.2126 * r + 0.7152 * g + 0.0722 * bl;
  };
  const [hi, lo] = [lum(a), lum(b)].sort((x, y) => y - x);
  return (hi + 0.05) / (lo + 0.05);
}

const CATALOG: I18nCatalog = {
  defaultLocale: 'en',
  supportedLocales: [
    { code: 'en', label: 'English', nativeLabel: 'English', dir: 'ltr' },
    { code: 'de', label: 'German', nativeLabel: 'Deutsch', dir: 'ltr', beta: true, betaLabel: 'Beta' },
  ],
  locales: { de: { phrases: { 'Language': 'Sprache', 'Signing in as {{email}} to {{tenant}}': 'Anmeldung als {{email}} bei {{tenant}}' } } },
};

let theme: ThemeState | null = null;
let locale: LocaleState | null = null;
function Probe() {
  theme = useTheme();
  locale = useLocale();
  return null;
}

beforeEach(async () => {
  await AsyncStorage.clear();
  jest.restoreAllMocks();
});

describe('themes', () => {
  it('system follows the device; explicit choices do not', () => {
    expect(resolveTheme('system', 'dark')).toBe('dark');
    expect(resolveTheme('system', 'light')).toBe('light');
    expect(resolveTheme('highContrast', 'light')).toBe('highContrast');
  });

  it.each(['light', 'dark', 'highContrast'] as ThemeName[])('%s meets contrast targets', (name) => {
    const p = palettes[name];
    const min = name === 'highContrast' ? 7 : 4.5;
    for (const bg of [p.background, p.surface]) {
      expect(contrast(p.text, bg)).toBeGreaterThanOrEqual(7);
      expect(contrast(p.textMuted, bg)).toBeGreaterThanOrEqual(min);
      expect(contrast(p.primary, bg)).toBeGreaterThanOrEqual(min);
      expect(contrast(p.danger, bg)).toBeGreaterThanOrEqual(min);
    }
    expect(contrast(p.primaryText, p.primary)).toBeGreaterThanOrEqual(4.5);
    expect(contrast(p.badgeText, p.badge)).toBeGreaterThanOrEqual(4.5);
  });

  it('system follows a dark device', async () => {
    mockScheme.value = 'dark';
    await render(<ThemeProvider><LocaleProvider serverUrl={null}><Probe /></LocaleProvider></ThemeProvider>);
    expect(theme!.preference).toBe('system');
    expect(theme!.name).toBe('dark');
    expect(theme!.dark).toBe(true);
    mockScheme.value = 'light';
  });

  it('the picker switches and persists the theme', async () => {
    mockScheme.value = 'light';
    const user = userEvent.setup();
    await render(<ThemeProvider><LocaleProvider serverUrl={null}><ThemePicker /><Probe /></LocaleProvider></ThemeProvider>);
    expect(theme!.name).toBe('light');
    await user.press(screen.getByTestId('theme-highContrast'));
    expect(theme!.name).toBe('highContrast');
    expect(theme!.colors.background).toBe('#000000');
    await waitFor(async () => expect(await AsyncStorage.getItem(PREF_KEYS.theme)).toBe(JSON.stringify('highContrast')));
  });

  it('a stored preference is restored', async () => {
    await AsyncStorage.setItem(PREF_KEYS.theme, JSON.stringify('dark'));
    await render(<ThemeProvider><LocaleProvider serverUrl={null}><Probe /></LocaleProvider></ThemeProvider>);
    await waitFor(() => expect(theme!.name).toBe('dark'));
  });
});

describe('locale', () => {
  it('the bundled catalog ships all nine dashboard languages', () => {
    const bundled = loadBundledCatalog();
    expect(bundled.supportedLocales.map((l) => l.code).sort()).toEqual(['de', 'en', 'es', 'fr', 'it', 'ja', 'yue-Hant', 'zh-Hans', 'zh-Hant']);
  });

  it('English needs no catalog; choosing another language loads the bundled one and translates', async () => {
    const bundled = jest.fn(() => CATALOG);
    await render(<ThemeProvider><LocaleProvider serverUrl={null} bundledCatalog={bundled}><LocalePicker /><Probe /></LocaleProvider></ThemeProvider>);
    expect(locale!.locale).toBe('en');
    expect(bundled).not.toHaveBeenCalled();
    await act(async () => { locale!.setLocale('de-DE'); });
    expect(locale!.locale).toBe('de');
    expect(bundled).toHaveBeenCalledTimes(1);
    expect(locale!.catalogSource).toBe('bundled');
    expect(locale!.t('Signing in as {{email}} to {{tenant}}', { email: 'a@b', tenant: 'T' })).toBe('Anmeldung als a@b bei T');
    expect(await screen.findByText('Sprache')).toBeTruthy();
    await waitFor(async () => expect(await AsyncStorage.getItem(PREF_KEYS.locale)).toBe(JSON.stringify('de')));
  });

  it('the picker lists every supported locale with beta labels', async () => {
    const user = userEvent.setup();
    await render(<ThemeProvider><LocaleProvider serverUrl={null} bundledCatalog={() => CATALOG}><LocalePicker /><Probe /></LocaleProvider></ThemeProvider>);
    await act(async () => { locale!.setLocale('de'); });
    await user.press(screen.getByTestId('locale-picker'));
    expect(within(await screen.findByTestId('locale-picker-sheet')).getByText('Deutsch (Beta)')).toBeTruthy();
    await user.press(screen.getByTestId('locale-picker-en'));
    expect(locale!.locale).toBe('en');
  });

  it('after sign-in the server catalog replaces the bundled one', async () => {
    const server: I18nCatalog = { ...CATALOG, locales: { de: { phrases: { 'Language': 'Sprache (Server)' } } } };
    const fetchMock = jest.fn(async () => ({ ok: true, status: 200, json: async () => server }));
    globalThis.fetch = fetchMock as unknown as typeof fetch;
    await AsyncStorage.setItem(PREF_KEYS.locale, JSON.stringify('de'));
    await render(<ThemeProvider><LocaleProvider serverUrl="http://10.0.2.2:44010" bundledCatalog={() => CATALOG}><Probe /></LocaleProvider></ThemeProvider>);
    await waitFor(() => expect(locale!.catalogSource).toBe('server'));
    expect(fetchMock).toHaveBeenCalledWith('http://10.0.2.2:44010/dashboard/i18n/armada.json', undefined);
    expect(locale!.t('Language')).toBe('Sprache (Server)');
  });

  it('a stored language is restored', async () => {
    await AsyncStorage.setItem(PREF_KEYS.locale, JSON.stringify('de'));
    await render(<ThemeProvider><LocaleProvider serverUrl={null} bundledCatalog={() => CATALOG}><Probe /></LocaleProvider></ThemeProvider>);
    await waitFor(() => expect(locale!.locale).toBe('de'));
  });
});

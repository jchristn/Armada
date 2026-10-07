import { render, type RenderOptions } from '@testing-library/react-native';
import type { ReactElement, ReactNode } from 'react';
import { AuthProvider } from '../auth/AuthContext';
import { LocaleProvider } from '../i18n/LocaleContext';
import { ThemeProvider } from '../theme/ThemeContext';
import type { I18nCatalog } from '@dashboard/i18n/catalog';

const EMPTY_CATALOG: I18nCatalog = { defaultLocale: 'en', supportedLocales: [], locales: {} };

/** Theme + locale (no server catalog, tiny bundled catalog) + auth, as the app nests them. */
export function AppProviders({ children }: { children: ReactNode }) {
  return (
    <ThemeProvider>
      <AuthProvider>
        <LocaleProvider serverUrl={null} bundledCatalog={() => EMPTY_CATALOG}>{children}</LocaleProvider>
      </AuthProvider>
    </ThemeProvider>
  );
}

export function renderWithProviders(ui: ReactElement, options?: Omit<RenderOptions, 'wrapper'>) {
  return render(ui, { wrapper: AppProviders, ...options });
}

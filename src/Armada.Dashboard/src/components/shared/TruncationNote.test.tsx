import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import TruncationNote from './TruncationNote';
import { translateTemplate } from '../../i18n/runtime';

// Stable across renders (components keep `t` in effect dependencies).
const localeValue = {
  t: (text: string, params?: Record<string, string | number>) => translateTemplate('en', text, null, params),
  locale: 'en',
  formatDateTime: (v: string | null | undefined) => v ?? '',
  formatRelativeTime: (v: string | null | undefined) => v ?? '',
};
vi.mock('../../context/LocaleContext', () => ({ useLocale: () => localeValue }));

describe('TruncationNote', () => {
  it('says how many of the total a first page holds', () => {
    render(<TruncationNote shown={1000} total={1500} />);
    expect(screen.getByTestId('truncation-note')).toHaveTextContent('Showing 1000 of 1500');
  });

  it('renders nothing when every row is shown', () => {
    const { container } = render(<TruncationNote shown={3} total={3} />);
    expect(container).toBeEmptyDOMElement();
  });
});

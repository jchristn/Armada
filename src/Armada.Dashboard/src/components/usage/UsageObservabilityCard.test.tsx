import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import UsageObservabilityCard from './UsageObservabilityCard';

vi.mock('../../context/LocaleContext', async () => {
  const { translateTemplate } = await import('../../i18n/runtime');
  const value = { locale: 'en', t: (text: string) => translateTemplate('en', text, null) };
  return { useLocale: () => value };
});

describe('UsageObservabilityCard', () => {
  it('links the usage and captain selection dashboards and the trace and metric stores', () => {
    render(<UsageObservabilityCard />);
    const usage = screen.getByRole('link', { name: /Usage operations/ });
    const selection = screen.getByRole('link', { name: /Captain Selection/ });
    expect(usage.getAttribute('href')).toMatch(/:3001\/d\/armada-usage$/);
    expect(selection.getAttribute('href')).toMatch(/:3001\/d\/armada-captain-selection$/);
    for (const link of [usage, selection, screen.getByRole('link', { name: 'Prometheus' }), screen.getByRole('link', { name: 'Tempo' })]) {
      expect(link.getAttribute('target')).toBe('_blank');
      expect(link.getAttribute('rel')).toContain('noopener');
    }
  });
});

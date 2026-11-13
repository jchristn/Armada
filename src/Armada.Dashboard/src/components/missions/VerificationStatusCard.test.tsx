import { render, screen } from '@testing-library/react';
import VerificationStatusCard from './VerificationStatusCard';
vi.mock('../../context/LocaleContext', () => ({ useLocale: () => ({ t: (s: string) => s }) }));
it('explains verification and repair waiting without implying an agent is running', () => {
  render(<VerificationStatusCard />);
  expect(screen.getByRole('status')).toHaveTextContent('Verification does not reserve a captain execution slot');
  expect(screen.getByRole('status')).toHaveTextContent('any repair waits for available capacity');
});

it('has a translated explanation in every supported non-English locale', async () => {
  const { default: source } = await import('../../../../Armada.Server/wwwroot/i18n/armada.json?raw');
  const catalog = JSON.parse(source);
  const key = 'Checks may be queued or running. Verification does not reserve a captain execution slot; any repair waits for available capacity.';
  for (const pack of Object.values(catalog.locales) as { phrases: Record<string, string> }[]) {
    expect(pack.phrases[key]).toBeTruthy();
    expect(pack.phrases[key]).not.toBe(key);
  }
});

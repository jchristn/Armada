import { Alert, Linking, type AlertButton } from 'react-native';
import { confirmOpenExternalUrl, externalHost, externalUrl, openExternalUrl } from '../lib/externalLinks';

const t = (text: string, params?: Record<string, string | number | null | undefined>) =>
  text.replace(/\{\{(\w+)\}\}/g, (_m, key: string) => String(params?.[key] ?? ''));

describe('external links from server data', () => {
  let open: jest.SpyInstance;
  let alert: jest.SpyInstance;

  beforeEach(() => {
    open = jest.spyOn(Linking, 'openURL').mockResolvedValue(true);
    alert = jest.spyOn(Alert, 'alert').mockImplementation(() => undefined);
  });

  afterEach(() => {
    open.mockRestore();
    alert.mockRestore();
  });

  it.each([
    'https://github.com/acme/repo/pull/12',
    'http://git.lan:3000/acme/repo/pulls/4',
    'HTTPS://GITHUB.COM/x',
  ])('opens http(s) URL %s', async (url) => {
    expect(externalUrl(url)).toBe(url);
    expect(await openExternalUrl(url)).toBe(true);
    expect(open).toHaveBeenCalledWith(url);
  });

  it.each([
    ['sms:+15550100?body=pay'],
    ['tel:+15550100'],
    ['shortcuts://run-shortcut?name=x'],
    ['javascript:alert(1)'],
    ['mailto:a@b.example'],
    ['armada://missions/msn_1'],
    ['https://github.com@evil.example/x'],
    ['https://evil.example\\@github.com/'],
    ['https://exa mple.com'],
    ['https://example.com/a\nb'],
    ['https://'],
    [''],
    [null],
    [42],
  ])('refuses %#', async (url) => {
    expect(externalUrl(url)).toBeNull();
    expect(await openExternalUrl(url)).toBe(false);
    expect(confirmOpenExternalUrl(url, t)).toBe(false);
    expect(open).not.toHaveBeenCalled();
    expect(alert).not.toHaveBeenCalled();
  });

  it('names the host without the port', () => {
    expect(externalHost('https://GitHub.com:443/x')).toBe('github.com');
    expect(externalHost('http://[::1]:8080/x')).toBe('[::1]');
  });

  it('the confirmation names the destination host and opens only on Open', async () => {
    expect(confirmOpenExternalUrl('https://evil.example/login', t)).toBe(true);
    expect(open).not.toHaveBeenCalled();
    expect(alert.mock.calls[0][0]).toBe('Open evil.example?');
    const buttons = alert.mock.calls[0][2] as AlertButton[];
    buttons.find((b) => b.text === 'Cancel')?.onPress?.();
    expect(open).not.toHaveBeenCalled();
    buttons.find((b) => b.text === 'Open')!.onPress!();
    await Promise.resolve();
    expect(open).toHaveBeenCalledWith('https://evil.example/login');
  });
});

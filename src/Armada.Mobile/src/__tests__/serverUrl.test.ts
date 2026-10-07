import { isLocalNetworkHost, normalizeServerUrl, serverHost, urlSecurityLevel } from '../profiles/serverUrl';

describe('server URL rules', () => {
  it('adds https to a bare host and strips trailing slashes', () => {
    expect(normalizeServerUrl(' armada.example.com/ ')).toEqual({ url: 'https://armada.example.com', error: null });
    expect(normalizeServerUrl('http://10.0.2.2:44010///').url).toBe('http://10.0.2.2:44010');
  });

  it('keeps a path prefix (relayed servers) and lowercases the host', () => {
    expect(normalizeServerUrl('https://Armada.Example.com/relay/abc').url).toBe('https://armada.example.com/relay/abc');
  });

  it('rejects other schemes, credentials, queries, and empty input', () => {
    expect(normalizeServerUrl('ftp://x').error).toBe('scheme');
    expect(normalizeServerUrl('https://user:pw@x.com').error).toBe('credentials');
    expect(normalizeServerUrl('https://x.com/?a=1').error).toBe('queryOrFragment');
    expect(normalizeServerUrl('   ').error).toBe('empty');
    expect(normalizeServerUrl('https://bad host').error).toBe('host');
  });

  it('classifies plain HTTP as LAN or public', () => {
    expect(urlSecurityLevel('https://armada.example.com')).toBe('secure');
    expect(urlSecurityLevel('http://192.168.1.20:7890')).toBe('lan');
    expect(urlSecurityLevel('http://10.0.2.2:44010')).toBe('lan');
    expect(urlSecurityLevel('http://admiral.local')).toBe('lan');
    expect(urlSecurityLevel('http://armada.example.com')).toBe('public');
    expect(urlSecurityLevel('http://172.32.0.1')).toBe('public');
  });

  it('reads hosts, including IPv6 literals', () => {
    expect(serverHost('http://[::1]:7890')).toBe('::1');
    expect(isLocalNetworkHost('::1')).toBe(true);
    expect(isLocalNetworkHost('8.8.8.8')).toBe(false);
  });
});

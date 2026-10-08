/**
 * The iOS App Transport Security settings let the app reach a self-hosted Admiral over plain HTTP on any host.
 * iOS ignores NSAllowsArbitraryLoads when another NSAllows* key is present, so it must be the only one.
 */
const appConfig = require('../../app.config');

type ExpoConfigLike = { ios?: { infoPlist?: { NSAppTransportSecurity?: Record<string, unknown> } } };

function resolveConfig(): ExpoConfigLike {
  const exported = appConfig.default ?? appConfig;
  return typeof exported === 'function' ? exported({ config: {} }) : exported;
}

describe('app config', () => {
  it('allows plain HTTP to any host on iOS with NSAllowsArbitraryLoads as the only ATS key', () => {
    const ats = resolveConfig().ios?.infoPlist?.NSAppTransportSecurity;
    expect(ats).toEqual({ NSAllowsArbitraryLoads: true });
  });
});

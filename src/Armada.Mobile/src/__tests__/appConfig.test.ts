/**
 * The iOS App Transport Security settings let the app reach a self-hosted Admiral over plain HTTP on any host.
 * iOS ignores NSAllowsArbitraryLoads when another NSAllows* key is present, so it must be the only one.
 */
const appConfig = require('../../app.config');

type PluginEntry = string | [string, Record<string, unknown>];
type ExpoConfigLike = {
  ios?: { infoPlist?: { NSAppTransportSecurity?: Record<string, unknown>; NSFaceIDUsageDescription?: string } };
  android?: { permissions?: string[] };
  plugins?: PluginEntry[];
};

function pluginOptions(config: ExpoConfigLike, name: string): Record<string, unknown> | null {
  const entry = (config.plugins ?? []).find((p) => (typeof p === 'string' ? p : p[0]) === name);
  if (!entry) return null;
  return typeof entry === 'string' ? {} : entry[1];
}

function resolveConfig(): ExpoConfigLike {
  const exported = appConfig.default ?? appConfig;
  return typeof exported === 'function' ? exported({ config: {} }) : exported;
}

describe('app config', () => {
  it('allows plain HTTP to any host on iOS with NSAllowsArbitraryLoads as the only ATS key', () => {
    const ats = resolveConfig().ios?.infoPlist?.NSAppTransportSecurity;
    expect(ats).toEqual({ NSAllowsArbitraryLoads: true });
  });

  it('declares Face ID for saved passwords (secure store) and the app lock, and biometrics on Android', () => {
    const config = resolveConfig();
    const usage = config.ios?.infoPlist?.NSFaceIDUsageDescription;
    expect(usage).toBeTruthy();
    expect(pluginOptions(config, 'expo-secure-store')).toEqual({ faceIDPermission: usage });
    expect(pluginOptions(config, 'expo-local-authentication')).toEqual({ faceIDPermission: usage });
    expect(config.android?.permissions).toContain('USE_BIOMETRIC');
  });

  it('keeps app content out of the Android Recents thumbnail without blocking screenshots', () => {
    expect(resolveConfig().plugins).toContain('./plugins/withAndroidRecentsPrivacy');
    const { addRecentsPrivacy } = require('../../plugins/withAndroidRecentsPrivacy');
    const template = [
      'import android.os.Build',
      'class MainActivity : ReactActivity() {',
      '  override fun onCreate(savedInstanceState: Bundle?) {',
      '    super.onCreate(null)',
      '  }',
      '}',
    ].join('\n');
    const patched: string = addRecentsPrivacy(template);
    expect(patched).toMatch(/super\.onCreate\(null\)\n.*\n\s*if \(Build\.VERSION\.SDK_INT >= Build\.VERSION_CODES\.TIRAMISU\) \{\n\s*setRecentsScreenshotEnabled\(false\)/);
    expect(patched).not.toContain('FLAG_SECURE');
    expect(addRecentsPrivacy(patched)).toBe(patched);
    expect(() => addRecentsPrivacy('class MainActivity {}')).toThrow(/template changed/);
  });
});

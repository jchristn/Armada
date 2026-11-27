/**
 * The iOS App Transport Security settings let the app reach a self-hosted Admiral over plain HTTP on any host.
 * iOS ignores NSAllowsArbitraryLoads when another NSAllows* key is present, so it must be the only one.
 */
const appConfig = require('../../app.config');

type PluginEntry = string | [string, Record<string, unknown>];
type ExpoConfigLike = {
  version?: string;
  owner?: string;
  ios?: {
    bundleIdentifier?: string;
    appleTeamId?: string;
    buildNumber?: string;
    config?: { usesNonExemptEncryption?: boolean };
    infoPlist?: { NSAppTransportSecurity?: Record<string, unknown>; NSFaceIDUsageDescription?: string };
  };
  android?: { package?: string; versionCode?: number; permissions?: string[]; blockedPermissions?: string[] };
  plugins?: PluginEntry[];
  extra?: { releaseVersion?: string; appVariant?: string; eas?: { projectId?: string } };
  mods?: Record<string, unknown>;
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

// Loads the config afresh with the given environment variables (the config reads them when it loads).
function resolveWithEnv(env: Record<string, string>): ExpoConfigLike {
  const saved: Record<string, string | undefined> = {};
  for (const key of Object.keys(env)) {
    saved[key] = process.env[key];
    process.env[key] = env[key];
  }
  try {
    let resolved: ExpoConfigLike = {};
    jest.isolateModules(() => {
      const fresh = require('../../app.config');
      const exported = fresh.default ?? fresh;
      resolved = typeof exported === 'function' ? exported({ config: {} }) : exported;
    });
    return resolved;
  } finally {
    for (const key of Object.keys(env)) {
      if (saved[key] === undefined) delete process.env[key];
      else process.env[key] = saved[key];
    }
  }
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

  it('uses the family store identity, version 1.5.0, and remote build numbers', () => {
    const config = resolveConfig();
    expect(config.ios?.bundleIdentifier).toBe('ai.latticetech.armada');
    expect(config.android?.package).toBe('ai.latticetech.armada');
    expect(config.version).toBe('1.5.0');
    expect(config.extra?.releaseVersion).toBe('1.5.0');
    expect(config.ios?.buildNumber).toBeUndefined();
    expect(config.android?.versionCode).toBeUndefined();
    expect(config.ios?.config?.usesNonExemptEncryption).toBe(false);
    expect(config.ios?.appleTeamId).toBeUndefined();
  });

  it('keeps the advertising id permission out of the Android manifest', () => {
    const config = resolveConfig();
    expect(config.android?.blockedPermissions).toContain('com.google.android.gms.permission.AD_ID');
    expect(config.android?.permissions ?? []).not.toContain('com.google.android.gms.permission.AD_ID');
  });

  it('reads the Expo owner, project id, and variant from the environment', () => {
    const config = resolveWithEnv({ ARMADA_MOBILE_EAS_OWNER: 'example-owner', ARMADA_MOBILE_EAS_PROJECT_ID: 'example-project', APP_VARIANT: 'preview' });
    expect(config.owner).toBe('example-owner');
    expect(config.extra?.eas?.projectId).toBe('example-project');
    expect(config.extra?.appVariant).toBe('preview');
  });

  it('builds a personal-team device build under <id>.dev without push or associated domains', () => {
    const config = resolveWithEnv({ ARMADA_MOBILE_LOCAL_TEAM_ID: 'PERSONALTEAM' });
    expect(config.ios?.bundleIdentifier).toBe('ai.latticetech.armada.dev');
    expect(config.ios?.appleTeamId).toBe('PERSONALTEAM');
    expect(config.android?.package).toBe('ai.latticetech.armada');
    expect(config.mods).toBeDefined();
  });

  it('follows the family eas.json standard', () => {
    const eas = require('../../eas.json');
    expect(eas.cli.appVersionSource).toBe('remote');
    expect(Object.keys(eas.build).sort()).toEqual(['development', 'development-device', 'preview', 'production']);
    expect(eas.build.preview.distribution).toBe('store');
    expect(eas.build.production.distribution).toBe('store');
    expect(eas.build.preview.autoIncrement).toBe(true);
    expect(eas.build.production.autoIncrement).toBe(true);
    for (const name of ['development', 'preview', 'production']) expect(eas.build[name].env.APP_VARIANT).toBe(name);
    for (const name of ['preview', 'production']) {
      expect(eas.submit[name].android.track).toBe('internal');
      expect(eas.submit[name].android.releaseStatus).toBe('draft');
      expect(eas.submit[name].ios.appleId).toBeUndefined();
    }
  });
});

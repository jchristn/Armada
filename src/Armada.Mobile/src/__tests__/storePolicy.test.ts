/**
 * Store review policy: the app offers no purchase path outside the stores (Apple 3.1.1 and 3.1.3(b)) and ships no
 * in-app purchase, advertising, or tracking library. Armada is free and signs in to an Admiral the user runs.
 */
import { readdirSync, readFileSync, statSync } from 'fs';
import { join } from 'path';

const ROOT = join(__dirname, '..');

function sourceFiles(dir: string): string[] {
  const out: string[] = [];
  for (const name of readdirSync(dir)) {
    const full = join(dir, name);
    if (statSync(full).isDirectory()) {
      if (name === '__tests__') continue;
      out.push(...sourceFiles(full));
    } else if (/\.(ts|tsx)$/.test(name)) {
      out.push(full);
    }
  }
  return out;
}

// Words that only appear when an app sells something or points to a place to pay.
const PURCHASE_PATTERNS: RegExp[] = [
  /promo ?code/i,
  /coupon/i,
  /\bpricing\b/i,
  /\bbuy (now|a |the )/i,
  /\bupgrade to (pro|premium|paid)/i,
  /\bper month\b/i,
  /\$\d+(\.\d{2})?\s*\/\s*(mo|month|year)/i,
  /checkout\.stripe|stripe\.com|paypal\.com/i,
];

const FORBIDDEN_PACKAGES = [
  'react-native-iap',
  'expo-in-app-purchases',
  'react-native-purchases',
  'expo-tracking-transparency',
  'expo-ads-admob',
  'react-native-google-mobile-ads',
  '@react-native-firebase/analytics',
  'react-native-fbsdk-next',
];

describe('store policy', () => {
  it('shows no outside purchase path in the app source', () => {
    const hits: string[] = [];
    for (const file of sourceFiles(ROOT)) {
      const text = readFileSync(file, 'utf8');
      for (const pattern of PURCHASE_PATTERNS) {
        if (pattern.test(text)) hits.push(file.slice(ROOT.length + 1) + ': ' + pattern.source);
      }
    }
    expect(hits).toEqual([]);
  });

  it('ships no purchase, advertising, or tracking library', () => {
    const pkg = JSON.parse(readFileSync(join(ROOT, '..', 'package.json'), 'utf8'));
    const deps = Object.keys({ ...(pkg.dependencies ?? {}), ...(pkg.devDependencies ?? {}) });
    expect(deps.filter((name) => FORBIDDEN_PACKAGES.includes(name))).toEqual([]);
  });
});

/*
 * Finds the local Admiral the way the Armada CLI and TUI do: the data directory is ARMADA_DATA_DIR or ~/.armada, and
 * its settings.json (ArmadaSettings, written in camelCase) holds `admiralPort` (default 7890) and `apiKey`, the local
 * API key the Admiral generates on first start and accepts as X-Api-Key. Reading it needs no prompt. No `vscode` import.
 */

import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';

/** Environment variable that moves the Armada data directory (Armada.Core Constants.DataDirectoryEnvVar). */
export const DATA_DIRECTORY_ENV = 'ARMADA_DATA_DIR';

/** Default Admiral REST port (Constants.DefaultAdmiralPort). */
export const DEFAULT_ADMIRAL_PORT = 7890;

/** What the local settings file says about the Admiral. */
export interface LocalAdmiral {
  /** The settings file that was read. */
  settingsPath: string;
  port: number;
  /** The local API key, or null when the file has none yet (the Admiral writes one on first start). */
  apiKey: string | null;
  /** http://127.0.0.1:<port>, as the CLI uses. */
  baseUrl: string;
}

/** Why no local Admiral was found. */
export interface LocalAdmiralMissing {
  settingsPath: string;
  reason: string;
}

/** Inputs, injectable for tests. */
export interface DiscoveryEnvironment {
  env?: Record<string, string | undefined>;
  homeDir?: string;
  readFile?: (file: string) => string;
}

/** The Armada data directory: ARMADA_DATA_DIR when set, else ~/.armada. */
export function armadaDataDirectory(environment: DiscoveryEnvironment = {}): string {
  const env = environment.env ?? process.env;
  const override = env[DATA_DIRECTORY_ENV];
  if (override && override.trim()) return override.trim();
  return path.join(environment.homeDir ?? os.homedir(), '.armada');
}

/** Reads the local Admiral's port and API key. Never throws. */
export function discoverLocalAdmiral(environment: DiscoveryEnvironment = {}): LocalAdmiral | LocalAdmiralMissing {
  const settingsPath = path.join(armadaDataDirectory(environment), 'settings.json');
  const read = environment.readFile ?? ((file: string) => fs.readFileSync(file, 'utf8'));
  let text: string;
  try {
    text = read(settingsPath);
  } catch {
    return { settingsPath, reason: `No Armada settings at ${settingsPath}. Install and start Armada, or connect to a remote Admiral.` };
  }
  let parsed: unknown;
  try {
    parsed = JSON.parse(text.replace(/^﻿/, ''));
  } catch {
    return { settingsPath, reason: `${settingsPath} is not valid JSON.` };
  }
  if (!parsed || typeof parsed !== 'object') return { settingsPath, reason: `${settingsPath} is not a settings object.` };
  const settings = parsed as Record<string, unknown>;
  const rawPort = property(settings, 'admiralPort');
  const port = typeof rawPort === 'number' && Number.isInteger(rawPort) && rawPort >= 1 && rawPort <= 65535 ? rawPort : DEFAULT_ADMIRAL_PORT;
  const rawKey = property(settings, 'apiKey');
  const apiKey = typeof rawKey === 'string' && rawKey.trim() ? rawKey.trim() : null;
  return { settingsPath, port, apiKey, baseUrl: `http://127.0.0.1:${port}` };
}

/** True when a discovery result found the settings file. */
export function isLocalAdmiral(value: LocalAdmiral | LocalAdmiralMissing): value is LocalAdmiral {
  return (value as LocalAdmiral).baseUrl !== undefined;
}

/** True for http://localhost, 127.x.x.x or [::1] on the port. */
export function isLoopbackUrl(url: string, port?: number): boolean {
  let parsed: URL;
  try {
    parsed = new URL(url);
  } catch {
    return false;
  }
  const host = parsed.hostname.replace(/^\[|\]$/g, '').toLowerCase();
  const loopback = host === 'localhost' || host === '::1' || /^127(\.\d{1,3}){3}$/.test(host);
  if (!loopback) return false;
  if (port === undefined) return true;
  const actual = parsed.port ? Number(parsed.port) : parsed.protocol === 'https:' ? 443 : 80;
  return actual === port;
}

// ArmadaSettings is read case-insensitively by the Admiral, so a hand-edited file may use PascalCase.
function property(settings: Record<string, unknown>, name: string): unknown {
  if (name in settings) return settings[name];
  const lower = name.toLowerCase();
  const key = Object.keys(settings).find((k) => k.toLowerCase() === lower);
  return key ? settings[key] : undefined;
}

import * as fs from 'fs';
import * as path from 'path';

/**
 * Normalizes a git remote URL for comparison, the same way Armada matches vessels on import: scheme, credentials,
 * port, host case, a trailing slash and a trailing `.git` are ignored, so HTTPS and SSH forms of one repository match.
 * Returns an empty string for an empty input. Local paths normalize to their forward-slash, lower-cased form.
 */
export function normalizeRepoUrl(raw: string | null | undefined): string {
  if (!raw) return '';
  let value = raw.trim();
  if (!value) return '';

  // scp-like SSH: git@github.com:org/repo.git
  const scp = /^(?:[^@/\s]+@)?([^:/\s]+):(?!\/)(.+)$/.exec(value);
  if (scp && !/^[a-zA-Z]:[\\/]/.test(value)) {
    return finish(`${scp[1].toLowerCase()}/${scp[2]}`);
  }

  const scheme = /^([a-zA-Z][a-zA-Z0-9+.-]*):\/\/(.*)$/.exec(value);
  if (scheme) {
    value = scheme[2];
    if (scheme[1].toLowerCase() === 'file') return finish(value.toLowerCase());
    const slash = value.indexOf('/');
    let authority = slash >= 0 ? value.slice(0, slash) : value;
    const rest = slash >= 0 ? value.slice(slash + 1) : '';
    const at = authority.lastIndexOf('@');
    if (at >= 0) authority = authority.slice(at + 1);
    authority = authority.replace(/:\d+$/, '').toLowerCase();
    return finish(`${authority}/${rest}`);
  }

  return finish(value.replace(/\\/g, '/').toLowerCase());
}

function finish(value: string): string {
  let result = value.replace(/\/+$/, '');
  if (result.toLowerCase().endsWith('.git')) result = result.slice(0, -4);
  result = result.replace(/\/+$/, '');
  return result.toLowerCase();
}

/** True when two remote URLs point at the same repository. */
export function sameRepository(a: string | null | undefined, b: string | null | undefined): boolean {
  const left = normalizeRepoUrl(a);
  return left !== '' && left === normalizeRepoUrl(b);
}

/**
 * Reads the remote URLs of the git repository at folderPath from its config file, without running git. Handles a
 * `.git` directory, a `.git` file (worktrees and submodules, `gitdir:`), and a worktree's `commondir`. Remote
 * `origin` comes first. Returns an empty list when the folder is not a repository or the config cannot be read.
 */
export function readGitRemoteUrls(folderPath: string): string[] {
  try {
    const gitDir = resolveGitDir(folderPath);
    if (!gitDir) return [];
    const configPath = path.join(resolveCommonDir(gitDir), 'config');
    if (!fs.existsSync(configPath)) return [];
    return parseGitConfigRemotes(fs.readFileSync(configPath, 'utf8'));
  } catch {
    return [];
  }
}

/** Parses `[remote "name"] url = ...` entries from a git config file, origin first. */
export function parseGitConfigRemotes(config: string): string[] {
  const remotes: Array<{ name: string; url: string }> = [];
  let current: string | null = null;
  for (const rawLine of config.split(/\r?\n/)) {
    const line = rawLine.trim();
    if (!line || line.startsWith('#') || line.startsWith(';')) continue;
    const section = /^\[\s*remote\s+"([^"]+)"\s*\]$/i.exec(line);
    if (section) {
      current = section[1];
      continue;
    }
    if (line.startsWith('[')) {
      current = null;
      continue;
    }
    if (current) {
      const kv = /^url\s*=\s*(.+)$/i.exec(line);
      if (kv) remotes.push({ name: current, url: kv[1].trim() });
    }
  }
  remotes.sort((a, b) => (a.name === 'origin' ? -1 : b.name === 'origin' ? 1 : 0));
  return remotes.map((r) => r.url);
}

function resolveGitDir(folderPath: string): string | null {
  const dotGit = path.join(folderPath, '.git');
  if (!fs.existsSync(dotGit)) return null;
  const stat = fs.statSync(dotGit);
  if (stat.isDirectory()) return dotGit;
  const content = fs.readFileSync(dotGit, 'utf8');
  const match = /^gitdir:\s*(.+)$/m.exec(content);
  if (!match) return null;
  const target = match[1].trim();
  return path.isAbsolute(target) ? target : path.resolve(folderPath, target);
}

function resolveCommonDir(gitDir: string): string {
  const commonFile = path.join(gitDir, 'commondir');
  if (!fs.existsSync(commonFile)) return gitDir;
  const target = fs.readFileSync(commonFile, 'utf8').trim();
  return path.isAbsolute(target) ? target : path.resolve(gitDir, target);
}

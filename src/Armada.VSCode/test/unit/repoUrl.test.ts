import * as assert from 'assert';
import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
import { describe, it } from 'node:test';
import { normalizeRepoUrl, parseGitConfigRemotes, readGitRemoteUrls, sameRepository } from '../../src/util/repoUrl';

describe('normalizeRepoUrl', () => {
  it('matches HTTPS, SSH and scp forms of one repository', () => {
    const forms = [
      'https://github.com/Org/Repo.git',
      'https://user:token@GitHub.com:443/org/repo/',
      'ssh://git@github.com:22/org/repo.git',
      'git@github.com:org/repo.git',
      'http://github.com/org/repo',
    ];
    for (const form of forms) assert.strictEqual(normalizeRepoUrl(form), 'github.com/org/repo', form);
    assert.ok(sameRepository(forms[0], forms[3]));
  });

  it('does not match different repositories or empty values', () => {
    assert.ok(!sameRepository('https://github.com/org/repo', 'https://github.com/org/repo2'));
    assert.ok(!sameRepository('https://github.com/org/repo', 'https://gitlab.com/org/repo'));
    assert.ok(!sameRepository('', ''));
    assert.ok(!sameRepository(null, undefined));
    assert.strictEqual(normalizeRepoUrl('   '), '');
  });

  it('normalizes local paths', () => {
    assert.strictEqual(normalizeRepoUrl('C:\\Code\\Repo\\'), 'c:/code/repo');
    assert.strictEqual(normalizeRepoUrl('/home/u/Repo.git'), '/home/u/repo');
    assert.strictEqual(normalizeRepoUrl('file:///home/u/repo'), '/home/u/repo');
  });
});

describe('git config remotes', () => {
  it('parses remotes with origin first and ignores other sections', () => {
    const config = '[core]\n\turl = nope\n[remote "upstream"]\n\turl = https://github.com/up/repo.git\n[remote "origin"]\n\tfetch = +refs/heads/*\n\turl = git@github.com:me/repo.git\n[branch "main"]\n\tremote = origin\n';
    assert.deepStrictEqual(parseGitConfigRemotes(config), ['git@github.com:me/repo.git', 'https://github.com/up/repo.git']);
    assert.deepStrictEqual(parseGitConfigRemotes(''), []);
  });

  it('reads a repository, a worktree and a non-repository folder', () => {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), 'armada-vscode-git-'));
    try {
      const repo = path.join(root, 'repo');
      fs.mkdirSync(path.join(repo, '.git', 'worktrees', 'wt'), { recursive: true });
      fs.writeFileSync(path.join(repo, '.git', 'config'), '[remote "origin"]\n\turl = https://github.com/org/alpha.git\n');
      assert.deepStrictEqual(readGitRemoteUrls(repo), ['https://github.com/org/alpha.git']);

      const worktree = path.join(root, 'wt');
      fs.mkdirSync(worktree);
      fs.writeFileSync(path.join(worktree, '.git'), `gitdir: ${path.join(repo, '.git', 'worktrees', 'wt')}\n`);
      fs.writeFileSync(path.join(repo, '.git', 'worktrees', 'wt', 'commondir'), '../..\n');
      assert.deepStrictEqual(readGitRemoteUrls(worktree), ['https://github.com/org/alpha.git']);

      const plain = path.join(root, 'plain');
      fs.mkdirSync(plain);
      assert.deepStrictEqual(readGitRemoteUrls(plain), []);
      assert.deepStrictEqual(readGitRemoteUrls(path.join(root, 'missing')), []);
    } finally {
      fs.rmSync(root, { recursive: true, force: true });
    }
  });
});

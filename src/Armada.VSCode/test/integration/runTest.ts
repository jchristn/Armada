/*
 * @vscode/test-electron integration test launcher. Downloads VS Code, starts it with this extension in development
 * mode and runs suite/index.ts inside the extension host.
 *
 * Skipped (exit code 0, with a message) when ARMADA_VSCODE_SKIP_INTEGRATION=1, when Linux has no display, or when VS
 * Code cannot be downloaded (offline or blocked). A test failure inside VS Code exits 1.
 */
import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
import { downloadAndUnzipVSCode, runTests } from '@vscode/test-electron';

function skip(reason: string): never {
  console.log(`[integration] skipped: ${reason}`);
  process.exit(0);
}

async function main(): Promise<void> {
  if (process.env.ARMADA_VSCODE_SKIP_INTEGRATION === '1') skip('ARMADA_VSCODE_SKIP_INTEGRATION=1');
  if (process.platform === 'linux' && !process.env.DISPLAY && !process.env.WAYLAND_DISPLAY) skip('no display (run under xvfb-run to enable)');

  const extensionDevelopmentPath = path.resolve(__dirname, '../../../');
  const extensionTestsPath = path.resolve(__dirname, './suite/index');

  let vscodeExecutablePath: string;
  try {
    vscodeExecutablePath = await downloadAndUnzipVSCode(process.env.ARMADA_VSCODE_VERSION ?? 'stable');
  } catch (err) {
    skip(`VS Code could not be downloaded (${err instanceof Error ? err.message : String(err)})`);
  }

  // A short user data directory: the IPC socket path inside it must stay under the 103-character Unix limit.
  const userDataDir = fs.mkdtempSync(path.join(os.tmpdir(), 'avsc-'));
  try {
    await runTests({
      vscodeExecutablePath,
      extensionDevelopmentPath,
      extensionTestsPath,
      launchArgs: ['--disable-extensions', '--skip-welcome', '--skip-release-notes', `--user-data-dir=${userDataDir}`],
    });
  } catch (err) {
    console.error(`[integration] failed: ${err instanceof Error ? err.message : String(err)}`);
    process.exit(1);
  } finally {
    fs.rmSync(userDataDir, { recursive: true, force: true });
  }
}

void main();

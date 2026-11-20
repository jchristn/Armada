/*
 * @vscode/test-electron integration test launcher. Downloads VS Code, starts it with this extension in development
 * mode and runs suite/index.ts inside the extension host.
 *
 * The extension host never sees the developer's ~/.armada or ~/.armada-harbor: ARMADA_DATA_DIR, and the Harbor and
 * TUI paths under it, point at ARMADA_VSCODE_TEST_DATA_DIR when set (the data directory of a throwaway Admiral, for
 * the connected smoke run with ARMADA_VSCODE_EXPECT_ADMIRAL=1), otherwise at an empty temporary directory (the
 * disconnected run). ARMADA_VSCODE_SCREENSHOT_DIR, when set, asks the
 * suite for light and dark screenshots of the Armada container (macOS only).
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
  const emptyDataDir = fs.mkdtempSync(path.join(os.tmpdir(), 'avsc-data-'));
  const dataDir = process.env.ARMADA_VSCODE_TEST_DATA_DIR || emptyDataDir;
  console.log(`[integration] ARMADA_DATA_DIR for the extension host: ${dataDir}`);
  try {
    await runTests({
      vscodeExecutablePath,
      extensionDevelopmentPath,
      extensionTestsPath,
      launchArgs: ['--disable-extensions', '--skip-welcome', '--skip-release-notes', '--disable-workspace-trust', `--user-data-dir=${userDataDir}`],
      extensionTestsEnv: {
        ARMADA_DATA_DIR: dataDir,
        // Discovery also reads Harbor's settings and the TUI's profiles; keep both inside the test data directory.
        ARMADA_HARBOR_HOME: path.join(dataDir, 'harbor'),
        ARMADA_TUI_PREFERENCES: path.join(dataDir, 'tui.json'),
        ARMADA_TUI_CREDENTIALS: path.join(dataDir, 'tui-credentials.json'),
        ARMADA_VSCODE_EXPECT_ADMIRAL: process.env.ARMADA_VSCODE_EXPECT_ADMIRAL ?? '',
        ARMADA_VSCODE_SCREENSHOT_DIR: process.env.ARMADA_VSCODE_SCREENSHOT_DIR ?? '',
      },
    });
  } catch (err) {
    console.error(`[integration] failed: ${err instanceof Error ? err.message : String(err)}`);
    process.exit(1);
  } finally {
    fs.rmSync(userDataDir, { recursive: true, force: true });
    fs.rmSync(emptyDataDir, { recursive: true, force: true });
  }
}

void main();

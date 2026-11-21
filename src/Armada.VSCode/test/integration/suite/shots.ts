/*
 * Screenshot run (ARMADA_VSCODE_SHOTS_DIR): connects to a throwaway Admiral, opens the dashboard pages with charts in
 * editor tabs, in the dark and light themes, and saves each view with ARMADA_VSCODE_WINDOW_SHOT, a program that
 * captures only this VS Code test window (`<program> <pid> <file>`), never the screen. Run with
 * `npm run test:integration` and the two variables set; nothing is asserted.
 */
import { execFile } from 'child_process';
import * as fs from 'fs';
import * as path from 'path';
import * as vscode from 'vscode';
import type { ArmadaExtensionApi } from '../../../src/extension';

const ROOT = path.resolve(__dirname, '..', '..', '..', '..');
const MANIFEST = JSON.parse(fs.readFileSync(path.join(ROOT, 'package.json'), 'utf8')) as { name: string; publisher: string };

function sleep(ms: number): Promise<void> {
  return new Promise((r) => setTimeout(r, ms));
}

async function waitFor(condition: () => boolean, timeoutMs: number): Promise<boolean> {
  const deadline = Date.now() + timeoutMs;
  while (!condition()) {
    if (Date.now() > deadline) return false;
    await sleep(150);
  }
  return true;
}

function log(line: string): void {
  console.log(`[shots] ${line}`);
}

export async function run(): Promise<void> {
  const dir = process.env.ARMADA_VSCODE_SHOTS_DIR ?? '';
  const program = process.env.ARMADA_VSCODE_WINDOW_SHOT ?? '';
  if (!dir || !program) throw new Error('ARMADA_VSCODE_SHOTS_DIR and ARMADA_VSCODE_WINDOW_SHOT are required');
  fs.mkdirSync(dir, { recursive: true });
  const ext = vscode.extensions.getExtension(`${MANIFEST.publisher}.${MANIFEST.name}`);
  if (!ext) throw new Error('extension not found');
  const api = (await ext.activate()) as ArmadaExtensionApi;
  if (!(await waitFor(() => api.connection.connected, 60000))) throw new Error('not connected');
  await api.ops.refreshAll();
  const config = vscode.workspace.getConfiguration();
  await config.update('armada.pages.tabReuse', 'single', vscode.ConfigurationTarget.Global);
  await vscode.commands.executeCommand('workbench.action.closePanel');
  await vscode.commands.executeCommand('workbench.action.closeAuxiliaryBar');

  let count = 0;
  const shot = async (name: string): Promise<void> => {
    await vscode.commands.executeCommand('notifications.clearAll');
    await sleep(1800);
    const file = path.join(dir, `${String(++count).padStart(3, '0')}-${name}.png`);
    await new Promise<void>((resolve) => execFile(program, [String(process.pid), file], (err) => {
      log(err ? `${name} failed: ${err.message}` : file);
      resolve();
    }));
  };
  const open = async (route: string, settleMs = 3500): Promise<void> => {
    const before = api.pages.stats.snapshots.length;
    await vscode.commands.executeCommand('armada.page.open', route);
    await waitFor(() => api.pages.stats.snapshots.length > before, 45000);
    await sleep(settleMs);
  };
  const step = async (action: 'scroll' | 'click' | 'select', target: { selector?: string; text?: string; y?: number; value?: string }, settleMs = 1500): Promise<void> => {
    api.pages.automate({ action, ...target });
    await sleep(settleMs);
  };
  const sidebar = async (show: boolean): Promise<void> => {
    if (show) {
      await vscode.commands.executeCommand('armadaNav.focus');
      await vscode.commands.executeCommand('armada.nav.reveal', 'activity');
    } else {
      await vscode.commands.executeCommand('workbench.action.closeSidebar');
    }
    await sleep(600);
  };

  const vessel = api.ops.vessels.find((v) => v.name === 'payments-api') ?? api.ops.vessels[0];
  const opus = api.ops.captains.find((c) => c.name === 'Opus Architect') ?? api.ops.captains[0];
  const mission = api.ops.missions[0];

  for (const [theme, label] of [['Default Dark Modern', 'dark'], ['Default Light Modern', 'light']] as const) {
    await config.update('workbench.colorTheme', theme, vscode.ConfigurationTarget.Global);
    await sleep(1500);

    // The Overview, with the Manage view beside it, then full width down through its charts.
    await sidebar(true);
    await open('/', 5000);
    await shot(`${label}-overview-with-manage`);
    await sidebar(false);
    await shot(`${label}-overview`);
    await step('scroll', { selector: '.mission-history-chart-container' });
    await shot(`${label}-overview-mission-history`);
    await step('scroll', { selector: '.overview-activity-charts' });
    await shot(`${label}-overview-api-requests-day`);
    for (const range of ['Last Week', 'Last Month', 'Last Hour']) {
      await step('click', { selector: '.overview-activity-charts .request-range-tab', text: range }, 2500);
      await shot(`${label}-overview-api-requests-${range.split(' ')[1].toLowerCase()}`);
    }
    await step('scroll', { selector: '.token-activity-card' });
    await step('select', { value: '90' }, 3000);
    await shot(`${label}-overview-token-heatmap-3-months`);
    await step('select', { value: '31' }, 3000);
    await shot(`${label}-overview-token-heatmap-month`);
    await step('select', { value: '365' }, 3000);
    await shot(`${label}-overview-token-heatmap-year`);

    // Activity: token usage, API requests, captain usage, concurrency, all activity.
    await open('/activity?source=tokens', 6000);
    for (const [range, slug] of [['Last 3 months', '3-months'], ['Last Month', 'month'], ['Last Week', 'week']] as const) {
      await step('scroll', { y: 0 }, 300);
      await step('click', { selector: 'button', text: range }, 4500);
      await shot(`${label}-token-usage-${slug}`);
      for (const y of [650, 1300, 1950, 2600]) {
        await step('scroll', { y });
        await shot(`${label}-token-usage-${slug}-${y}`);
      }
    }
    await step('scroll', { y: 0 }, 300);
    await step('click', { selector: 'button', text: 'Last 3 months' }, 3000);
    await step('click', { selector: 'button', text: 'Lines' }, 2500);
    await step('scroll', { y: 650 });
    await shot(`${label}-token-usage-3-months-lines`);
    await step('scroll', { y: 0 }, 300);
    await step('click', { selector: 'button', text: 'By token type' }, 2500);
    await step('scroll', { y: 650 });
    await shot(`${label}-token-usage-3-months-by-type-lines`);
    await step('scroll', { y: 0 }, 300);
    await step('click', { selector: 'button', text: 'Stacked bars' }, 2500);
    await step('scroll', { y: 650 });
    await shot(`${label}-token-usage-3-months-by-type-bars`);
    await open('/activity?source=requests', 5000);
    await shot(`${label}-api-requests-day`);
    for (const range of ['Last Week', 'Last Month', 'Last Hour']) {
      await step('click', { selector: '.request-range-tab', text: range }, 2500);
      await shot(`${label}-api-requests-${range.split(' ')[1].toLowerCase()}`);
    }
    await open('/activity?source=usage-overview', 4000);
    for (const range of ['30d', '7d']) {
      await step('select', { value: range }, 4000);
      await shot(`${label}-captain-usage-${range}`);
    }
    await open('/activity?source=concurrency', 4000);
    for (const range of ['30d', '7d']) {
      await step('select', { value: range }, 4000);
      await shot(`${label}-concurrency-${range}`);
      await step('scroll', { y: 700 });
      await shot(`${label}-concurrency-${range}-700`);
      await step('scroll', { y: 0 }, 300);
    }
    await open('/activity?source=history', 5000);
    await step('click', { selector: '.history-range-tabs .request-range-tab', text: 'Last Week' }, 4000);
    await shot(`${label}-all-activity-week`);

    // A vessel and a captain with their history, a mission, and the lists around them.
    if (vessel) {
      await open(`/vessels/${vessel.id}`, 5000);
      await shot(`${label}-vessel-payments-api`);
    }
    if (opus) {
      await open(`/captains/${opus.id}`, 5000);
      await shot(`${label}-captain-opus`);
      await step('scroll', { y: 800 });
      await shot(`${label}-captain-opus-800`);
    }
    await open('/missions', 4000);
    await shot(`${label}-missions`);
    if (mission) {
      await open(`/missions/${mission.id}`, 4000);
      await shot(`${label}-mission-detail`);
    }
    await open('/vessels?tab=health', 4000);
    await shot(`${label}-vessel-health`);
    await open('/captains', 4000);
    await shot(`${label}-captains`);
    await open('/configuration?tab=harbors', 4000);
    await shot(`${label}-harbors`);

    // The sidebar on its own: Manage with Activity open, and Ask Armada.
    await sidebar(true);
    await open('/activity?source=tokens', 4000);
    await shot(`${label}-manage-with-token-usage`);
    await vscode.commands.executeCommand('armadaAsk.focus');
    await shot(`${label}-ask-and-token-usage`);
  }
  log(`done: ${count} screenshots in ${dir}`);
}

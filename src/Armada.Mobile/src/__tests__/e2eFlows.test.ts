/**
 * Lint for the Maestro flows in e2e/. On iOS, Maestro reads element frames from XCUITest, which does not clip rows
 * a scroll view has scrolled out of its own bounds: a row hidden under the tab bar, or under a sheet's footer, still
 * counts as "visible", so `scrollUntilVisible` can stop with the row covered and the following `tapOn` lands on
 * whatever covers it (the Approvals tab instead of Preferences, Save instead of a select). A flow that scrolls to an
 * element and then taps it must therefore either center it (`centerElement: true`) or lift it clear with one more
 * `scroll` / `swipe` before the tap (for rows at the end of a list, which cannot be centered).
 */
import * as fs from 'fs';
import * as path from 'path';

interface Command {
  name: string;
  indent: number;
  lines: string[];
  file: string;
  line: number;
}

const E2E = path.join(__dirname, '..', '..', 'e2e');
const PASSIVE = new Set(['takeScreenshot', 'assertVisible', 'assertNotVisible', 'waitForAnimationToEnd', 'extendedWaitUntil']);

function flowFiles(dir: string): string[] {
  return fs.readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) return entry.name === 'scripts' ? [] : flowFiles(full);
    return entry.name.endsWith('.yaml') && entry.name !== 'config.yaml' ? [full] : [];
  });
}

/** The flow's commands (list items `- name...` at any depth), each with the lines of its block. */
function commands(file: string): Command[] {
  const text = fs.readFileSync(file, 'utf8');
  const body = text.slice(text.indexOf('\n---') + 4).split('\n');
  const result: Command[] = [];
  const open: Command[] = [];
  body.forEach((raw, index) => {
    if (!raw.trim() || raw.trim().startsWith('#')) return;
    const indent = raw.length - raw.trimStart().length;
    while (open.length && indent <= open[open.length - 1].indent) open.pop();
    for (const c of open) c.lines.push(raw);
    const match = /^(\s*)- ([A-Za-z]+)/.exec(raw);
    if (match) {
      const command: Command = { name: match[2], indent: match[1].length, lines: [raw], file, line: index + 1 };
      result.push(command);
      open.push(command);
    }
  });
  return result;
}

function targetOf(command: Command): string | null {
  const block = command.lines.join('\n');
  const id = /id:\s*"?([^"\n]+)"?/.exec(block);
  if (id) return `id:${id[1].trim()}`;
  const inline = /- (?:tapOn|element):\s*"([^"]+)"/.exec(block) ?? /element:\s*"([^"]+)"/.exec(block);
  return inline ? `text:${inline[1]}` : null;
}

/** Scroll-then-tap pairs whose scroll neither centers the element nor is followed by a scroll or swipe. */
function unsafeScrollTaps(file: string): string[] {
  const all = commands(file);
  const problems: string[] = [];
  all.forEach((command, i) => {
    if (command.name !== 'scrollUntilVisible') return;
    // Screenshots, assertions, and waits do not move anything; look past them to the next interaction.
    const siblings = all.slice(i + 1).filter((c) => c.indent === command.indent && !PASSIVE.has(c.name));
    const next = siblings[0];
    if (!next || next.name !== 'tapOn' || targetOf(next) !== targetOf(command)) return;
    if (command.lines.some((l) => /centerElement:\s*true/.test(l))) return;
    problems.push(`${path.relative(E2E, file)}:${command.line} scrolls to ${targetOf(command)} and taps it without centering it`);
  });
  return problems;
}

describe('Maestro flows', () => {
  const files = flowFiles(E2E);

  it('finds the flows', () => {
    expect(files.length).toBeGreaterThan(10);
    expect(files.some((f) => f.endsWith(path.join('proxy', 'common', 'open-preferences.yaml')))).toBe(true);
  });

  it('never taps an element right after scrolling to it unless it is centered or lifted clear first', () => {
    expect(files.flatMap(unsafeScrollTaps)).toEqual([]);
  });

  it('the lint catches an uncentered scroll-then-tap', () => {
    const dir = fs.mkdtempSync(path.join(require('os').tmpdir(), 'flows-'));
    const bad = path.join(dir, 'bad.yaml');
    fs.writeFileSync(bad, 'appId: x\n---\n- scrollUntilVisible:\n    element:\n      id: "nav-/preferences"\n    direction: DOWN\n- tapOn:\n    id: "nav-/preferences"\n');
    const good = path.join(dir, 'good.yaml');
    fs.writeFileSync(good, 'appId: x\n---\n- scrollUntilVisible:\n    element:\n      id: "a"\n    direction: DOWN\n- scroll\n- tapOn:\n    id: "a"\n');
    expect(unsafeScrollTaps(bad)).toHaveLength(1);
    expect(unsafeScrollTaps(good)).toEqual([]);
    fs.rmSync(dir, { recursive: true, force: true });
  });
});

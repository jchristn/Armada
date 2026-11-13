import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';

// Guards the Ask Armada page geometry: the page must fit the main pane exactly so the composer stays
// visible at the bottom and only the conversation scrolls. .ask-home used to size itself with a
// 100dvh calc that did not match the real space left by the top bar, banner, breadcrumbs and padding,
// so the whole page overflowed and .main showed an outer scrollbar even for an empty conversation.

const css = readFileSync('./src/App.css', 'utf8');

// Rules are matched at the start of a line so `.a > .ask-home {` does not satisfy `.ask-home {`.
function readRule(selector: string): string {
  const needle = `\n${selector} {`;
  const start = css.indexOf(needle);
  if (start < 0) {
    throw new Error(`Missing CSS rule: ${selector}`);
  }

  const end = css.indexOf('}', start);
  return css.slice(start + needle.length, end);
}

function readProperty(block: string, property: string): string | null {
  const match = new RegExp(`(?:^|[;{\\s])${property}:\\s*([^;]+);`).exec(block);
  return match ? match[1].trim() : null;
}

describe('Ask Armada viewport layout', () => {
  it('sizes .ask-home from its flex container instead of a 100dvh calc', () => {
    const home = readRule('.ask-home');
    expect(home).not.toContain('100dvh');
    expect(readProperty(home, 'height')).toBe('auto');
    expect(readProperty(home, 'min-height')).toBe('0');
    expect(readProperty(home, 'flex')).toBe('1 1 auto');
    expect(readProperty(home, 'box-sizing')).toBe('border-box');
    expect(css).not.toMatch(/\.ask-home[^{]*\{[^}]*100dvh/);
  });

  it('stops the outer .main pane from scrolling on the Ask route', () => {
    const main = readRule('.main:has(.ask-home)');
    expect(readProperty(main, 'overflow')).toBe('hidden');
    expect(readProperty(main, 'display')).toBe('flex');
    expect(readProperty(main, 'flex-direction')).toBe('column');

    const view = readRule('.main:has(.ask-home) > .view');
    expect(readProperty(view, 'flex')).toBe('1 1 auto');
    expect(readProperty(view, 'min-height')).toBe('0');
  });

  it('scrolls only the message list inside the conversation column', () => {
    const list = readRule('.ask-home-conv > .ask-transcript');
    expect(readProperty(list, 'min-height')).toBe('0');
    expect(readProperty(list, 'overflow-y')).toBe('auto');
    expect(readProperty(list, 'flex')).toBe('1 1 auto');

    expect(readProperty(readRule('.ask-home-conv > *'), 'flex')).toBe('0 0 auto');
    expect(readProperty(readRule('.ask-home-list'), 'min-height')).toBe('0');
  });
});

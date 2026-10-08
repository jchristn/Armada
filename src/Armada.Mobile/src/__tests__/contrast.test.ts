/**
 * WCAG AA contrast for every color pair the app draws, in every theme. The pairs mirror the UI kit:
 * - text on surfaces: body text (AppText), muted text (captions, hints, placeholders, inactive tab labels), links and
 *   primary actions, status badge and severity text (StatusBadge, toneColor), error text (field errors), on the
 *   screen background, cards and rows (surface), sheets, dialogs and selected rows (surfaceRaised);
 * - banner text (Banner, Home alerts) on the warning surface;
 * - text on filled buttons, selected tabs and segments, and count badges;
 * - non-text (WCAG 1.4.11, 3:1): form control boundaries, the focus ring, progress and heatmap fills against their
 *   track and the surface.
 * Disabled controls are exempt from WCAG contrast; they keep their label and announce the disabled state.
 */
import { palettes, type Palette, type ThemeName } from '../theme/palette';

/** WCAG relative-luminance contrast ratio between two #rrggbb colors. */
export function contrast(a: string, b: string): number {
  const lum = (hex: string) => {
    const [r, g, bl] = [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16) / 255)
      .map((c) => (c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4));
    return 0.2126 * r + 0.7152 * g + 0.0722 * bl;
  };
  const [hi, lo] = [lum(a), lum(b)].sort((x, y) => y - x);
  return (hi + 0.05) / (lo + 0.05);
}

type Token = keyof Palette;
interface Pair { fg: Token; bg: Token; min: number; use: string }

const SURFACES: Token[] = ['background', 'surface', 'surfaceRaised'];

function pairs(name: ThemeName): Pair[] {
  // High contrast promises 7:1 for everything readable.
  const aa = name === 'highContrast' ? 7 : 4.5;
  const out: Pair[] = [];
  for (const bg of SURFACES) {
    out.push({ fg: 'text', bg, min: 7, use: 'body text' });
    out.push({ fg: 'textMuted', bg, min: aa, use: 'muted text, hints, placeholders, inactive tabs' });
    out.push({ fg: 'primary', bg, min: aa, use: 'links, ghost and secondary buttons, selected option text' });
    out.push({ fg: 'danger', bg, min: aa, use: 'errors, destructive actions, failed badges' });
    out.push({ fg: 'warning', bg, min: aa, use: 'warning badges and notes' });
    out.push({ fg: 'success', bg, min: aa, use: 'success badges' });
    out.push({ fg: 'info', bg, min: aa, use: 'running and info badges' });
    out.push({ fg: 'control', bg, min: 3, use: 'form control boundary, switch off track' });
    out.push({ fg: 'focus', bg, min: 3, use: 'focused field ring' });
  }
  for (const fg of ['text', 'textMuted', 'primary', 'danger', 'warning'] as Token[]) {
    out.push({ fg, bg: 'warningSurface', min: fg === 'text' ? 7 : aa, use: 'banner and alert text' });
  }
  out.push({ fg: 'primaryText', bg: 'primary', min: 4.5, use: 'primary buttons, selected tabs and segments' });
  out.push({ fg: 'dangerText', bg: 'danger', min: 4.5, use: 'danger buttons, destructive swipe actions' });
  out.push({ fg: 'badgeText', bg: 'badge', min: 4.5, use: 'count badges' });
  for (const fill of ['success', 'primary', 'info'] as Token[]) {
    out.push({ fg: fill, bg: 'track', min: 3, use: 'progress and heatmap fill against the empty track' });
  }
  return out;
}

describe.each(['light', 'dark', 'highContrast'] as ThemeName[])('%s theme contrast', (name) => {
  const p = palettes[name];
  it.each(pairs(name).map((x) => [`${x.fg} on ${x.bg}`, x] as const))('%s', (_label, pair) => {
    const ratio = contrast(p[pair.fg], p[pair.bg]);
    if (ratio < pair.min) {
      throw new Error(`${name}: ${pair.fg} ${p[pair.fg]} on ${pair.bg} ${p[pair.bg]} is ${ratio.toFixed(2)}:1, needs ${pair.min}:1 (${pair.use})`);
    }
  });
});

import { describe, expect, it } from 'vitest';
import catalogSource from '../../../Armada.Server/wwwroot/i18n/armada.json?raw';
import pageSource from '../pages/AskArmada.tsx?raw';
import threadListSource from '../components/ask/AskThreadList.tsx?raw';
import headerSource from '../components/ask/AskConversationHeader.tsx?raw';
import stripSource from '../components/ask/AskWorkStrip.tsx?raw';
import messageListSource from '../components/ask/AskMessageList.tsx?raw';
import messageViewSource from '../components/ask/AskMessageView.tsx?raw';
import confirmSource from '../components/ask/AskConfirmCard.tsx?raw';
import workCardSource from '../components/ask/AskWorkCard.tsx?raw';
import composerSource from '../components/ask/AskComposer.tsx?raw';
import quickMenuSource from '../components/ask/AskQuickActionMenu.tsx?raw';
import dispatchSource from '../components/ask/AskDispatchForm.tsx?raw';
import fleetActionSource from '../components/ask/AskFleetActionForm.tsx?raw';
import quickActionsSource from './askQuickActions.ts?raw';
import commandsSource from './askCommands.ts?raw';
import askWorkSource from './askWork.ts?raw';
import type { I18nCatalog } from '../i18n/runtime';

const S = "'((?:[^'\\\\]|\\\\.)*)'";
const CALL = new RegExp(`\\bt\\(\\s*${S}`, 'g');
const LABEL = new RegExp(`\\blabel:\\s*${S}`, 'g');
/** Quick-action titles and descriptions are rendered through `t`. */
const META = new RegExp(`\\b(?:title|description):\\s*${S}`, 'g');
const ERROR_LINE = /errors\.\w+ = (.*?);?$/gm;
/** Local command hints (lib/askCommands) are rendered through `t` under the composer. */
const HINT = new RegExp(`\\bhint:\\s*(?:value \\? ${S} : )?${S}`, 'g');

const SOURCES = [
  pageSource, threadListSource, headerSource, stripSource, messageListSource, messageViewSource, confirmSource,
  workCardSource, composerSource, quickMenuSource, dispatchSource, fleetActionSource, askWorkSource,
];

/** Every literal the Ask Armada home base passes to the translator. */
function askKeys(): string[] {
  const keys = new Set<string>();
  const add = (raw: string | undefined) => {
    if (raw === undefined) return;
    const key = raw.replace(/\\'/g, "'");
    if (key.trim()) keys.add(key);
  };
  for (const src of SOURCES) {
    for (const m of src.matchAll(CALL)) add(m[1]);
    for (const m of src.matchAll(LABEL)) add(m[1]);
  }
  for (const m of quickActionsSource.matchAll(META)) add(m[1]);
  for (const m of commandsSource.matchAll(META)) add(m[1]);
  for (const m of commandsSource.matchAll(HINT)) { add(m[1]); add(m[2]); }
  for (const line of quickActionsSource.matchAll(ERROR_LINE)) {
    for (const m of line[1].matchAll(new RegExp(S, 'g'))) add(m[1]);
  }
  return [...keys].sort();
}

function placeholders(text: string): string {
  return (text.match(/\{\{[\w.]+\}\}/g) ?? []).sort().join(',');
}

describe('Ask Armada i18n catalog', () => {
  const catalog = JSON.parse(catalogSource) as I18nCatalog;
  const keys = askKeys();

  it('finds the Ask Armada strings', () => {
    expect(keys.length).toBeGreaterThan(120);
    expect(keys).toContain('Work in this conversation');
    expect(keys).toContain('Nothing runs until you approve.');
    expect(keys).toContain('Choose a vessel.');
    expect(keys).toContain('Start a voyage of one or more missions on a vessel');
    expect(keys).toContain('Unpin');
    expect(keys).toContain('Unknown command {{command}}. Type / to see commands');
    expect(keys).toContain('Show thinking is off.');
    expect(keys).toContain('Start a new conversation with a fresh context (also /clear)');
  });

  it('has a translation for every string in every non-English locale', () => {
    const missing: string[] = [];
    for (const meta of catalog.supportedLocales) {
      if (meta.code === catalog.defaultLocale) continue;
      const pack = catalog.locales[meta.code] ?? {};
      for (const key of keys) {
        if (!pack.phrases?.[key] && !pack.terms?.[key] && !pack.sections?.[key]) missing.push(`${meta.code}: ${key}`);
      }
    }
    expect(missing).toEqual([]);
  });

  it('keeps placeholders intact in translations', () => {
    const bad: string[] = [];
    for (const [code, pack] of Object.entries(catalog.locales)) {
      for (const key of keys) {
        const value = pack.phrases?.[key] ?? pack.terms?.[key];
        if (value && placeholders(key) !== placeholders(value)) bad.push(`${code}: ${key}`);
      }
    }
    expect(bad).toEqual([]);
  });
});

const LOCALE_STORAGE_KEY = 'armada_locale';
const TITLE_DATA_KEY = 'data-armada-i18n-title';

export interface LocaleMeta {
  code: string;
  label: string;
  nativeLabel: string;
  dir: 'ltr' | 'rtl';
  aliases?: string[];
}

export interface LocalePack {
  terms?: Record<string, string>;
  phrases?: Record<string, string>;
  sections?: Record<string, string>;
}

export interface I18nCatalog {
  defaultLocale: string;
  supportedLocales: LocaleMeta[];
  locales: Record<string, LocalePack>;
}

const DEFAULT_LOCALES: LocaleMeta[] = [
  { code: 'en', label: 'English', nativeLabel: 'English', dir: 'ltr', aliases: ['en-US', 'en-GB', 'en-CA', 'en-AU'] },
  { code: 'es', label: 'Spanish', nativeLabel: 'Espa\u00f1ol', dir: 'ltr', aliases: ['es-ES', 'es-MX', 'es-419'] },
  { code: 'zh-Hans', label: 'Mandarin (Simplified)', nativeLabel: '\u7b80\u4f53\u4e2d\u6587', dir: 'ltr', aliases: ['zh', 'zh-CN', 'zh-SG', 'cmn-Hans'] },
  { code: 'zh-Hant', label: 'Mandarin (Traditional)', nativeLabel: '\u7e41\u9ad4\u4e2d\u6587', dir: 'ltr', aliases: ['zh-TW', 'cmn-Hant'] },
  { code: 'yue-Hant', label: 'Cantonese', nativeLabel: '\u7cb5\u8a9e', dir: 'ltr', aliases: ['zh-HK', 'zh-MO', 'yue', 'yue-HK'] },
  { code: 'ja', label: 'Japanese', nativeLabel: '\u65e5\u672c\u8a9e', dir: 'ltr', aliases: ['ja-JP'] },
  { code: 'de', label: 'German', nativeLabel: 'Deutsch', dir: 'ltr', aliases: ['de-DE'] },
  { code: 'fr', label: 'French', nativeLabel: 'Fran\u00e7ais', dir: 'ltr', aliases: ['fr-FR', 'fr-CA'] },
  { code: 'it', label: 'Italian', nativeLabel: 'Italiano', dir: 'ltr', aliases: ['it-IT'] },
];

const ATTRIBUTE_NAMES = ['title', 'placeholder', 'aria-label', 'aria-description', 'alt'] as const;

let catalogPromise: Promise<I18nCatalog | null> | null = null;
let dialogsPatched = false;
let builtinsPatched = false;

function getFallbackCatalog(): I18nCatalog {
  return {
    defaultLocale: 'en',
    supportedLocales: DEFAULT_LOCALES,
    locales: {},
  };
}

function getCatalogOrFallback(catalog: I18nCatalog | null | undefined): I18nCatalog {
  return catalog ?? getFallbackCatalog();
}

function getPack(locale: string, catalog: I18nCatalog | null | undefined): LocalePack {
  return getCatalogOrFallback(catalog).locales[locale] ?? {};
}

function getOriginalText(node: Text): string {
  const textNode = node as Text & { __armadaI18nOriginal?: string };
  if (textNode.__armadaI18nOriginal === undefined) {
    textNode.__armadaI18nOriginal = node.nodeValue ?? '';
  }
  return textNode.__armadaI18nOriginal;
}

function getOriginalAttribute(element: HTMLElement, attr: string): string | null {
  const dataAttr = `data-armada-i18n-orig-${attr}`;
  if (element.hasAttribute(dataAttr)) {
    return element.getAttribute(dataAttr);
  }

  const original = element.getAttribute(attr);
  if (original !== null) {
    element.setAttribute(dataAttr, original);
  }
  return original;
}

function getOriginalButtonValue(element: HTMLInputElement): string {
  const dataAttr = 'data-armada-i18n-orig-value';
  const existing = element.getAttribute(dataAttr);
  if (existing !== null) return existing;
  const current = element.value;
  element.setAttribute(dataAttr, current);
  return current;
}

function preserveWhitespace(original: string, translated: string): string {
  const leading = original.match(/^\s*/)?.[0] ?? '';
  const trailing = original.match(/\s*$/)?.[0] ?? '';
  return `${leading}${translated.trim()}${trailing}`;
}

function translateSingleToken(locale: string, value: string, catalog: I18nCatalog | null | undefined): string | null {
  const translated = getPack(locale, catalog).terms?.[value];
  return translated ?? null;
}

function translateExactPhrase(locale: string, value: string, catalog: I18nCatalog | null | undefined): string | null {
  const pack = getPack(locale, catalog);
  return pack.phrases?.[value] ?? pack.sections?.[value] ?? null;
}

function formatNumber(locale: string, value: number): string {
  return new Intl.NumberFormat(locale).format(value);
}

function formatRelative(locale: string, value: number, unit: Intl.RelativeTimeFormatUnit): string {
  return new Intl.RelativeTimeFormat(locale, { numeric: 'auto' }).format(value, unit);
}

function translatePrefixedValue(locale: string, prefix: string, value: string, catalog: I18nCatalog | null | undefined): string | null {
  const translatedPrefix = translateText(locale, prefix, catalog);
  if (translatedPrefix === prefix && !translateSingleToken(locale, prefix, catalog)) return null;
  return `${translatedPrefix}: ${value}`;
}

function applyDynamicPatterns(locale: string, text: string, catalog: I18nCatalog | null | undefined): string | null {
  if (text === 'just now') return formatRelative(locale, 0, 'second');

  let match = text.match(/^\+\s+(.+)$/);
  if (match) return `+ ${translateText(locale, match[1], catalog)}`;

  match = text.match(/^←\s+(.+)$/);
  if (match) return `← ${translateText(locale, match[1], catalog)}`;

  match = text.match(/^(.+)\s+→$/);
  if (match) return `${translateText(locale, match[1], catalog)} →`;

  const lowercaseStatus = [
    'healthy',
    'degraded',
    'unhealthy',
    'error',
    'unknown',
    'connected',
    'connecting',
    'disconnected',
    'disabled',
    'active',
    'inactive',
  ];
  if (lowercaseStatus.includes(text)) {
    const normalized = text.charAt(0).toUpperCase() + text.slice(1);
    return translateText(locale, normalized, catalog);
  }

  match = text.match(/^(\d+)\s*\/\s*page$/i);
  if (match) return `${formatNumber(locale, Number(match[1]))} / ${translateText(locale, 'page', catalog)}`;

  match = text.match(/^(\d+)s ago$/);
  if (match) return formatRelative(locale, -Number(match[1]), 'second');

  match = text.match(/^(\d+)m ago$/);
  if (match) return formatRelative(locale, -Number(match[1]), 'minute');

  match = text.match(/^(\d+)h ago$/);
  if (match) return formatRelative(locale, -Number(match[1]), 'hour');

  match = text.match(/^(\d+)d ago$/);
  if (match) return formatRelative(locale, -Number(match[1]), 'day');

  match = text.match(/^(\d+) chars$/);
  if (match) return `${formatNumber(locale, Number(match[1]))} ${translateText(locale, 'chars', catalog)}`;

  match = text.match(/^(\d+) lines$/);
  if (match) return `${formatNumber(locale, Number(match[1]))} ${translateText(locale, 'lines', catalog)}`;

  match = text.match(/^(\d+) unread$/);
  if (match) return `${formatNumber(locale, Number(match[1]))} ${translateText(locale, 'unread', catalog)}`;

  match = text.match(/^(\d+) records$/);
  if (match) return `${formatNumber(locale, Number(match[1]))} ${translateText(locale, 'records', catalog)}`;

  match = text.match(/^(\d+) selected$/);
  if (match) return `${formatNumber(locale, Number(match[1]))} ${translateText(locale, 'selected', catalog)}`;

  match = text.match(/^(\d+) idle$/);
  if (match) return `${formatNumber(locale, Number(match[1]))} ${translateText(locale, 'idle', catalog)}`;

  match = text.match(/^(\d+) working$/);
  if (match) return `${formatNumber(locale, Number(match[1]))} ${translateText(locale, 'working', catalog)}`;

  match = text.match(/^(\d+) stalled$/);
  if (match) return `${formatNumber(locale, Number(match[1]))} ${translateText(locale, 'stalled', catalog)}`;

  match = text.match(/^(\d+) failed$/);
  if (match) return `${formatNumber(locale, Number(match[1]))} ${translateText(locale, 'failed', catalog)}`;

  match = text.match(/^(\d+)\/(\d+) done$/);
  if (match) return `${formatNumber(locale, Number(match[1]))}/${formatNumber(locale, Number(match[2]))} ${translateText(locale, 'done', catalog)}`;

  match = text.match(/^, (\d+) failed$/);
  if (match) return `, ${formatNumber(locale, Number(match[1]))} ${translateText(locale, 'failed', catalog)}`;

  match = text.match(/^(\d+) file changed$/);
  if (match) return `${formatNumber(locale, Number(match[1]))} ${translateText(locale, 'file changed', catalog)}`;

  match = text.match(/^(\d+) files changed$/);
  if (match) return `${formatNumber(locale, Number(match[1]))} ${translateText(locale, 'files changed', catalog)}`;

  match = text.match(/^Files \((\d+)\)$/);
  if (match) return `${translateText(locale, 'Files', catalog)} (${formatNumber(locale, Number(match[1]))})`;

  match = text.match(/^Missions \((\d+)\)$/);
  if (match) return `${translateText(locale, 'Missions', catalog)} (${formatNumber(locale, Number(match[1]))})`;

  match = text.match(/^Mission (\d+)$/);
  if (match) return `${translateText(locale, 'Mission', catalog)} ${formatNumber(locale, Number(match[1]))}`;

  match = text.match(/^(.+?) -- click to sort$/);
  if (match) return `${translateText(locale, match[1], catalog)} -- ${translateText(locale, 'click to sort', catalog)}`;

  match = text.match(/^Select all (.+)$/);
  if (match) return `${translateText(locale, 'Select all', catalog)} ${translateText(locale, match[1], catalog)}`;

  match = text.match(/^Select this (.+)$/);
  if (match) return `${translateText(locale, 'Select this', catalog)} ${translateText(locale, match[1], catalog)}`;

  match = text.match(/^Delete Selected (.+)$/);
  if (match) return `${translateText(locale, 'Delete Selected', catalog)} ${translateText(locale, match[1], catalog)}`;

  match = text.match(/^(Mission|Voyage|Captain): (.+)$/);
  if (match) return `${translateText(locale, match[1], catalog)}: ${match[2]}`;

  match = text.match(/^Log: (.+)$/);
  if (match) return `${translateText(locale, 'Log', catalog)}: ${match[1]}`;

  match = text.match(/^Health: (.+)$/);
  if (match) return `${translateText(locale, 'Health', catalog)}: ${translateText(locale, match[1], catalog)}`;

  match = text.match(/^Copy (.+) HTTP config to clipboard$/);
  if (match) {
    const template = translateText(locale, 'Copy {{title}} HTTP config to clipboard', catalog);
    return interpolate(template, { title: match[1] });
  }

  match = text.match(/^Copy (.+) STDIO config to clipboard$/);
  if (match) {
    const template = translateText(locale, 'Copy {{title}} STDIO config to clipboard', catalog);
    return interpolate(template, { title: match[1] });
  }

  match = text.match(/^Delete ([A-Za-z ]+) "(.+)"\? This cannot be undone\.$/);
  if (match) {
    return interpolate(translateText(locale, 'Delete {{entity}} "{{name}}"? This cannot be undone.', catalog), {
      entity: translateText(locale, match[1], catalog).toLowerCase(),
      name: match[2],
    });
  }

  match = text.match(/^Delete ([A-Za-z ]+) (.+)\? This cannot be undone\.$/);
  if (match) {
    return interpolate(translateText(locale, 'Delete {{entity}} {{name}}? This cannot be undone.', catalog), {
      entity: translateText(locale, match[1], catalog).toLowerCase(),
      name: match[2],
    });
  }

  match = text.match(/^Delete dock (.+)\? This will clean up the git worktree and cannot be undone\.$/);
  if (match) {
    return interpolate(translateText(locale, 'Delete dock {{name}}? This will clean up the git worktree and cannot be undone.', catalog), {
      name: match[1],
    });
  }

  match = text.match(/^Delete (event|signal) (.+)\?$/);
  if (match) {
    return interpolate(translateText(locale, 'Delete {{entity}} {{name}}?', catalog), {
      entity: translateText(locale, match[1], catalog).toLowerCase(),
      name: match[2],
    });
  }

  match = text.match(/^Stop captain "(.+)"\? This will halt the current mission\.$/);
  if (match) {
    return interpolate(translateText(locale, 'Stop captain "{{name}}"? This will halt the current mission.', catalog), {
      name: match[1],
    });
  }

  match = text.match(/^Recall captain "(.+)"\? The captain will finish current work and return to idle\.$/);
  if (match) {
    return interpolate(translateText(locale, 'Recall captain "{{name}}"? The captain will finish current work and return to idle.', catalog), {
      name: match[1],
    });
  }

  match = text.match(/^Remove captain "(.+)"\? This cannot be undone\.$/);
  if (match) {
    return interpolate(translateText(locale, 'Remove captain "{{name}}"? This cannot be undone.', catalog), {
      name: match[1],
    });
  }

  match = text.match(/^Delete failed: (.+)$/);
  if (match) {
    return interpolate(translateText(locale, 'Delete failed: {{message}}', catalog), { message: match[1] });
  }

  match = text.match(/^Save failed: (.+)$/);
  if (match) {
    return interpolate(translateText(locale, 'Save failed: {{message}}', catalog), { message: match[1] });
  }

  match = text.match(/^Failed to save settings: (.+)$/);
  if (match) {
    return interpolate(translateText(locale, 'Failed to save settings: {{message}}', catalog), { message: match[1] });
  }

  match = text.match(/^Unable to load existing Armada resources: (.+)$/);
  if (match) {
    return interpolate(translateText(locale, 'Unable to load existing Armada resources: {{message}}', catalog), { message: match[1] });
  }

  match = text.match(/^(Fleet|Vessel|Captain) creation failed: (.+)$/);
  if (match) {
    return interpolate(translateText(locale, '{{entity}} creation failed: {{message}}', catalog), {
      entity: translateText(locale, match[1], catalog),
      message: match[2],
    });
  }

  match = text.match(/^Dispatch failed: (.+)$/);
  if (match) {
    return interpolate(translateText(locale, 'Dispatch failed: {{message}}', catalog), { message: match[1] });
  }

  match = text.match(/^Created (fleet|vessel|captain) "(.+)"\.$/i);
  if (match) {
    return interpolate(translateText(locale, 'Created {{entity}} "{{name}}".', catalog), {
      entity: translateText(locale, match[1], catalog).toLowerCase(),
      name: match[2],
    });
  }

  match = text.match(/^Using (fleet|vessel|captain) "(.+)"\.$/i);
  if (match) {
    return interpolate(translateText(locale, 'Using {{entity}} "{{name}}".', catalog), {
      entity: translateText(locale, match[1], catalog).toLowerCase(),
      name: match[2],
    });
  }

  match = text.match(/^Mission status refreshed: (.+)\.$/);
  if (match) {
    return interpolate(translateText(locale, 'Mission status refreshed: {{status}}.', catalog), {
      status: translateText(locale, match[1], catalog),
    });
  }

  match = text.match(/^([A-Za-z ]+): (.+)$/);
  if (match) {
    const translatedPrefix = translateText(locale, match[1], catalog);
    if (translatedPrefix !== match[1]) {
      return `${translatedPrefix}: ${match[2]}`;
    }
  }

  match = text.match(/^(Mission|Voyage|Captain) "(.+)" \u2014 (.+)$/);
  if (match) {
    return `${translateText(locale, match[1], catalog)} "${match[2]}" \u2014 ${translateText(locale, match[3], catalog)}`;
  }

  match = text.match(/^(Mission|Voyage|Captain) (Pending|Assigned|InProgress|WorkProduced|Testing|Review|Complete|Completed|Failed|LandingFailed|Cancelled|Idle|Working|Stalled|Stopping|Queued|Passed|Landed|Active|Inactive)$/);
  if (match) {
    return `${translateText(locale, match[1], catalog)} ${translateText(locale, match[2], catalog)}`;
  }

  match = text.match(/^Signing in as (.+) to (.+)$/);
  if (match) {
    return interpolate(translateText(locale, 'Signing in as {{email}} to {{tenant}}', catalog), {
      email: match[1],
      tenant: match[2],
    });
  }

  const prefixed = [
    'Mission',
    'Voyage',
    'Captain',
    'Fleet',
    'Vessel',
    'Dock',
    'Event',
    'Signal',
    'Pipeline',
    'Persona',
    'Prompt Template',
    'Template',
    'Credential',
    'Tenant',
    'User',
    'Error',
    'Health',
    'Status',
    'Title',
    'Message',
    'Connection',
    'Version',
    'Uptime',
    'Payload',
    'Branch',
    'Priority',
    'Runtime',
    'Model',
    'Description',
  ];
  const prefixMatch = text.match(/^([^:]+): (.+)$/);
  if (prefixMatch && prefixed.includes(prefixMatch[1])) {
    return translatePrefixedValue(locale, prefixMatch[1], prefixMatch[2], catalog);
  }

  return null;
}

export function translateText(locale: string, text: string, catalog: I18nCatalog | null | undefined): string {
  if (!text || locale === 'en') return text;
  const trimmed = text.trim();
  if (!trimmed) return text;

  const exactPhrase = translateExactPhrase(locale, trimmed, catalog);
  if (exactPhrase) return preserveWhitespace(text, exactPhrase);

  const dynamic = applyDynamicPatterns(locale, trimmed, catalog);
  if (dynamic) return preserveWhitespace(text, dynamic);

  if (!trimmed.includes('\n') && !trimmed.includes(' ') && !trimmed.includes('\t')) {
    const exactToken = translateSingleToken(locale, trimmed, catalog);
    if (exactToken) return preserveWhitespace(text, exactToken);
  }

  return text;
}

export function interpolate(template: string, params?: Record<string, string | number | null | undefined>): string {
  if (!params) return template;
  return Object.entries(params).reduce((value, [key, replacement]) => {
    return value.split(`{{${key}}}`).join(replacement == null ? '' : String(replacement));
  }, template);
}

const ICU_PLURAL_START = /\{\s*([A-Za-z_][A-Za-z0-9_]*)\s*,\s*plural\s*,/g;

/**
 * Parse the branches of an ICU plural body starting at `start` (just after "plural,"). Returns the branch
 * map and the index just past the closing brace, or null when the message is malformed.
 */
function parsePluralBranches(text: string, start: number): { branches: Record<string, string>; end: number } | null {
  const branches: Record<string, string> = {};
  let i = start;
  while (i < text.length) {
    while (i < text.length && /\s/.test(text[i])) i++;
    if (text[i] === '}') return { branches, end: i + 1 };
    const selectorMatch = /^(=\d+|[A-Za-z]+)\s*\{/.exec(text.slice(i));
    if (!selectorMatch) return null;
    const selector = selectorMatch[1];
    i += selectorMatch[0].length;
    let depth = 1;
    const bodyStart = i;
    while (i < text.length && depth > 0) {
      if (text[i] === '{') depth++;
      else if (text[i] === '}') depth--;
      if (depth > 0) i++;
    }
    if (depth !== 0) return null;
    branches[selector] = text.slice(bodyStart, i);
    i++;
  }
  return null;
}

/**
 * Format ICU-style plural messages, e.g. `{count, plural, one {# repository} other {# repositories}}`, using
 * `Intl.PluralRules` for the active locale so languages with other plural categories (few, many) work when
 * the catalog supplies those branches. `#` becomes the locale-formatted number; `=N` exact matches win.
 * Text outside plural blocks is returned unchanged, so `{{name}}` placeholders still interpolate afterwards.
 */
export function formatIcuPlurals(
  locale: string,
  text: string,
  params?: Record<string, string | number | null | undefined>,
): string {
  if (!text.includes('plural')) return text;
  let result = '';
  let cursor = 0;
  const re = new RegExp(ICU_PLURAL_START.source, 'g');
  let match: RegExpExecArray | null;
  while ((match = re.exec(text)) !== null) {
    const parsed = parsePluralBranches(text, match.index + match[0].length);
    if (!parsed) break;
    const raw = params?.[match[1]];
    const count = typeof raw === 'number' ? raw : Number(raw ?? 0);
    let category = 'other';
    try {
      category = new Intl.PluralRules(locale).select(count);
    } catch {
      category = count === 1 ? 'one' : 'other';
    }
    const branch = parsed.branches[`=${count}`] ?? parsed.branches[category] ?? parsed.branches.other ?? '';
    let formatted = String(count);
    try {
      formatted = new Intl.NumberFormat(locale).format(count);
    } catch {
      // keep the plain number
    }
    result += text.slice(cursor, match.index) + formatIcuPlurals(locale, branch, params).split('#').join(formatted);
    cursor = parsed.end;
    re.lastIndex = parsed.end;
  }
  return result + text.slice(cursor);
}

export function translateTemplate(
  locale: string,
  text: string,
  catalog: I18nCatalog | null | undefined,
  params?: Record<string, string | number | null | undefined>,
): string {
  return interpolate(formatIcuPlurals(locale, translateText(locale, text, catalog), params), params);
}

export function normalizeLocale(locale: string | null | undefined, catalog: I18nCatalog | null | undefined): string {
  const source = (locale || '').trim();
  const normalizedSource = source || getCatalogOrFallback(catalog).defaultLocale;
  const supported = getSupportedLocales(catalog);
  const exact = supported.find((item) => item.code.toLowerCase() === normalizedSource.toLowerCase());
  if (exact) return exact.code;

  const alias = supported.find((item) => item.aliases?.some((value) => value.toLowerCase() === normalizedSource.toLowerCase()));
  if (alias) return alias.code;

  const lower = normalizedSource.toLowerCase();
  if (lower.startsWith('zh-hk') || lower.startsWith('zh-mo') || lower.startsWith('yue')) return 'yue-Hant';
  if (lower.startsWith('zh-tw') || lower.startsWith('zh-hant')) return 'zh-Hant';
  if (lower.startsWith('zh')) return 'zh-Hans';
  if (lower.startsWith('ja')) return 'ja';
  if (lower.startsWith('de')) return 'de';
  if (lower.startsWith('fr')) return 'fr';
  if (lower.startsWith('it')) return 'it';
  if (lower.startsWith('es')) return 'es';
  return getCatalogOrFallback(catalog).defaultLocale;
}

export function getSupportedLocales(catalog: I18nCatalog | null | undefined): LocaleMeta[] {
  return getCatalogOrFallback(catalog).supportedLocales;
}

export function getLocaleMeta(locale: string, catalog: I18nCatalog | null | undefined): LocaleMeta {
  return getSupportedLocales(catalog).find((item) => item.code === locale) ?? DEFAULT_LOCALES[0];
}

export function getInitialLocale(catalog: I18nCatalog | null | undefined): string {
  try {
    const stored = localStorage.getItem(LOCALE_STORAGE_KEY);
    if (stored) return normalizeLocale(stored, catalog);
  } catch {
    // ignore
  }

  const browserLocales = navigator.languages?.length ? navigator.languages : [navigator.language];
  for (const candidate of browserLocales) {
    const normalized = normalizeLocale(candidate, catalog);
    if (normalized) return normalized;
  }

  return getCatalogOrFallback(catalog).defaultLocale;
}

export function persistLocale(locale: string) {
  try {
    localStorage.setItem(LOCALE_STORAGE_KEY, locale);
  } catch {
    // ignore
  }
}

export async function loadCatalog(): Promise<I18nCatalog | null> {
  if (!catalogPromise) {
    catalogPromise = fetch('/dashboard/i18n/armada.json', { cache: 'force-cache' })
      .then(async (response) => {
        if (!response.ok) throw new Error(`Failed to load i18n catalog (${response.status})`);
        return await response.json() as I18nCatalog;
      })
      .catch(() => null);
  }
  return await catalogPromise;
}

function shouldSkipElement(element: Element | null): boolean {
  if (!element) return false;
  return !!element.closest('[data-i18n-skip="true"], pre, code, textarea, script, style, svg, .mono');
}

function translateTextNode(node: Text, locale: string, catalog: I18nCatalog | null | undefined) {
  if (shouldSkipElement(node.parentElement)) return;
  const textNode = node as Text & { __armadaI18nOriginal?: string; __armadaI18nApplied?: string };
  const current = node.nodeValue ?? '';
  // If the node's current value is not the translation we last wrote, it was
  // changed externally (e.g. a React re-render swapping "Checking..." for
  // "Healthy"). Adopt that new value as the source text so the observer does
  // not revert React's update to a stale first-seen original.
  if (textNode.__armadaI18nApplied === undefined || current !== textNode.__armadaI18nApplied) {
    textNode.__armadaI18nOriginal = current;
  }
  const original = textNode.__armadaI18nOriginal ?? current;
  const translated = translateText(locale, original, catalog);
  if (translated !== current) {
    node.nodeValue = translated;
  }
  textNode.__armadaI18nApplied = translated;
}

function translateElementAttributes(element: HTMLElement, locale: string, catalog: I18nCatalog | null | undefined) {
  if (shouldSkipElement(element)) return;

  for (const attr of ATTRIBUTE_NAMES) {
    const original = getOriginalAttribute(element, attr);
    if (original !== null) {
      const translated = translateText(locale, original, catalog);
      if (translated !== original || element.getAttribute(attr) !== translated) {
        element.setAttribute(attr, translated);
      }
    }
  }

  if (element instanceof HTMLInputElement && ['button', 'submit', 'reset'].includes(element.type)) {
    const original = getOriginalButtonValue(element);
    const translated = translateText(locale, original, catalog);
    if (translated !== element.value) {
      element.value = translated;
    }
  }
}

export function translateSubtree(root: ParentNode, locale: string, catalog: I18nCatalog | null | undefined) {
  if (!(root instanceof Element || root instanceof Document || root instanceof DocumentFragment)) return;

  if (root instanceof HTMLElement) {
    translateElementAttributes(root, locale, catalog);
  }

  const elements = root instanceof Document
    ? Array.from(root.body.querySelectorAll<HTMLElement>('*'))
    : Array.from(root.querySelectorAll<HTMLElement>('*'));

  for (const element of elements) {
    translateElementAttributes(element, locale, catalog);
  }

  const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
  let current = walker.nextNode();
  while (current) {
    translateTextNode(current as Text, locale, catalog);
    current = walker.nextNode();
  }
}

export function applyDocumentLocale(locale: string, catalog: I18nCatalog | null | undefined) {
  const meta = getLocaleMeta(locale, catalog);
  document.documentElement.lang = meta.code;
  document.documentElement.dir = meta.dir;

  if (!document.documentElement.hasAttribute(TITLE_DATA_KEY)) {
    document.documentElement.setAttribute(TITLE_DATA_KEY, document.title);
  }

  const originalTitle = document.documentElement.getAttribute(TITLE_DATA_KEY) || document.title;
  document.title = translateText(locale, originalTitle, catalog);
}

export function installDocumentTranslator(
  root: HTMLElement,
  getLocale: () => string,
  getCatalog: () => I18nCatalog | null,
): () => void {
  let scheduled = false;
  const translateNow = () => {
    scheduled = false;
    const locale = getLocale();
    const catalog = getCatalog();
    applyDocumentLocale(locale, catalog);
    translateSubtree(root, locale, catalog);
  };

  translateNow();

  const observer = new MutationObserver(() => {
    if (scheduled) return;
    scheduled = true;
    window.requestAnimationFrame(translateNow);
  });

  observer.observe(root, {
    childList: true,
    subtree: true,
    characterData: true,
    attributes: true,
    attributeFilter: [...ATTRIBUTE_NAMES, 'value'],
  });

  return () => observer.disconnect();
}

export function installDialogOverrides(getLocale: () => string, getCatalog: () => I18nCatalog | null) {
  if (dialogsPatched) return;
  dialogsPatched = true;

  const originalConfirm = window.confirm.bind(window);
  const originalAlert = window.alert.bind(window);

  window.confirm = (message?: string) => {
    return originalConfirm(translateText(getLocale(), message ?? '', getCatalog()));
  };

  window.alert = (message?: string) => {
    return originalAlert(translateText(getLocale(), message ?? '', getCatalog()));
  };
}

function hasExplicitLocale(locales: Intl.LocalesArgument | undefined): boolean {
  if (Array.isArray(locales)) return locales.length > 0;
  return typeof locales === 'string' ? locales.trim().length > 0 : locales != null;
}

export function installLocaleSensitiveBuiltins(getLocale: () => string) {
  if (builtinsPatched) return;
  builtinsPatched = true;

  const originalDateTime = Date.prototype.toLocaleString;
  const originalDateOnly = Date.prototype.toLocaleDateString;
  const originalTimeOnly = Date.prototype.toLocaleTimeString;
  const originalNumber = Number.prototype.toLocaleString;

  Date.prototype.toLocaleString = function armadaDateTime(locales?: Intl.LocalesArgument, options?: Intl.DateTimeFormatOptions) {
    const nextLocales = hasExplicitLocale(locales) ? locales : getLocale();
    return originalDateTime.call(this, nextLocales, options);
  };

  Date.prototype.toLocaleDateString = function armadaDateOnly(locales?: Intl.LocalesArgument, options?: Intl.DateTimeFormatOptions) {
    const nextLocales = hasExplicitLocale(locales) ? locales : getLocale();
    return originalDateOnly.call(this, nextLocales, options);
  };

  Date.prototype.toLocaleTimeString = function armadaTimeOnly(locales?: Intl.LocalesArgument, options?: Intl.DateTimeFormatOptions) {
    const nextLocales = hasExplicitLocale(locales) ? locales : getLocale();
    return originalTimeOnly.call(this, nextLocales, options);
  };

  Number.prototype.toLocaleString = function armadaNumber(locales?: Intl.LocalesArgument, options?: Intl.NumberFormatOptions) {
    const nextLocales = hasExplicitLocale(locales) ? locales : getLocale();
    return originalNumber.call(this, nextLocales, options);
  };
}

export function formatAbsoluteDateTime(locale: string, utc: string | null | undefined): string {
  if (!utc) return '';
  return new Date(utc).toLocaleString(locale);
}

export function formatDateOnly(locale: string, utc: string | null | undefined): string {
  if (!utc) return '';
  return new Date(utc).toLocaleDateString(locale);
}

export function formatRelativeFromUtc(locale: string, utc: string | null | undefined): string {
  if (!utc) return '-';
  const then = new Date(utc).getTime();
  const now = Date.now();
  const diffSeconds = Math.round((then - now) / 1000);
  const abs = Math.abs(diffSeconds);

  if (abs < 60) return formatRelative(locale, diffSeconds, 'second');
  const diffMinutes = Math.round(diffSeconds / 60);
  if (Math.abs(diffMinutes) < 60) return formatRelative(locale, diffMinutes, 'minute');
  const diffHours = Math.round(diffMinutes / 60);
  if (Math.abs(diffHours) < 24) return formatRelative(locale, diffHours, 'hour');
  const diffDays = Math.round(diffHours / 24);
  return formatRelative(locale, diffDays, 'day');
}

export { LOCALE_STORAGE_KEY, DEFAULT_LOCALES };

const LOCALE_STORAGE_KEY = 'armada_locale';
const TITLE_DATA_KEY = 'data-armada-i18n-title';

import {
  CATALOG_PATH,
  fetchCatalog,
  getLocaleMeta,
  pickInitialLocale,
  translateText,
  type I18nCatalog,
} from './catalog';

// Everything pure (types, translation, locale normalization, formatting) lives in catalog.ts and is shared with
// the mobile app; this module adds the browser parts (storage, DOM translation, dialog and builtin patches).
export * from './catalog';

const ATTRIBUTE_NAMES = ['title', 'placeholder', 'aria-label', 'aria-description', 'alt'] as const;

let catalogPromise: Promise<I18nCatalog | null> | null = null;
let dialogsPatched = false;
let builtinsPatched = false;

function getOriginalText(node: Text): string {
  const textNode = node as Text & { __armadaI18nOriginal?: string };
  if (textNode.__armadaI18nOriginal === undefined) {
    textNode.__armadaI18nOriginal = node.nodeValue ?? '';
  }
  return textNode.__armadaI18nOriginal;
}

// Attributes and button values cache the source text the first time they are translated. React may later reuse the
// same element for different content (a wizard step swapping fields, a form re-rendering with new labels), so a value
// that is no longer the translation we last wrote was changed externally: adopt it as the new source text instead of
// writing the stale first-seen original back (the same rule translateTextNode applies to text nodes).
function getOriginalAttribute(element: HTMLElement, attr: string): string | null {
  const dataAttr = `data-armada-i18n-orig-${attr}`;
  const appliedAttr = `data-armada-i18n-applied-${attr}`;
  const current = element.getAttribute(attr);
  if (current === null) {
    element.removeAttribute(dataAttr);
    element.removeAttribute(appliedAttr);
    return null;
  }

  const cached = element.getAttribute(dataAttr);
  if (cached !== null && element.getAttribute(appliedAttr) === current) return cached;
  element.setAttribute(dataAttr, current);
  return current;
}

function markAppliedAttribute(element: HTMLElement, attr: string, applied: string) {
  element.setAttribute(`data-armada-i18n-applied-${attr}`, applied);
}

function getOriginalButtonValue(element: HTMLInputElement): string {
  const dataAttr = 'data-armada-i18n-orig-value';
  const appliedAttr = 'data-armada-i18n-applied-value';
  const current = element.value;
  const existing = element.getAttribute(dataAttr);
  if (existing !== null && element.getAttribute(appliedAttr) === current) return existing;
  element.setAttribute(dataAttr, current);
  return current;
}

export function getInitialLocale(catalog: I18nCatalog | null | undefined): string {
  let stored: string | null = null;
  try {
    stored = localStorage.getItem(LOCALE_STORAGE_KEY);
  } catch {
    // ignore
  }

  const browserLocales = navigator.languages?.length ? navigator.languages : [navigator.language];
  return pickInitialLocale(stored, browserLocales, catalog);
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
    catalogPromise = fetchCatalog(CATALOG_PATH, { cache: 'force-cache' });
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
      markAppliedAttribute(element, attr, translated);
    }
  }

  if (element instanceof HTMLInputElement && ['button', 'submit', 'reset'].includes(element.type)) {
    const original = getOriginalButtonValue(element);
    const translated = translateText(locale, original, catalog);
    if (translated !== element.value) {
      element.value = translated;
    }
    element.setAttribute('data-armada-i18n-applied-value', translated);
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

export { LOCALE_STORAGE_KEY };

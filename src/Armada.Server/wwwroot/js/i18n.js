(function () {
    const STORAGE_KEY = 'armada_locale';
    const TITLE_DATA_KEY = 'data-armada-i18n-title';
    const ATTRIBUTE_NAMES = ['title', 'placeholder', 'aria-label', 'aria-description', 'alt'];
    const DEFAULT_LOCALES = [
        { code: 'en', label: 'English', nativeLabel: 'English', dir: 'ltr', aliases: ['en-US', 'en-GB', 'en-CA', 'en-AU'] },
        { code: 'es', label: 'Spanish', nativeLabel: 'Espa\u00f1ol', dir: 'ltr', aliases: ['es-ES', 'es-MX', 'es-419'], beta: true, betaLabel: 'beta' },
        { code: 'zh-Hans', label: 'Mandarin (Simplified)', nativeLabel: '\u7b80\u4f53\u4e2d\u6587', dir: 'ltr', aliases: ['zh', 'zh-CN', 'zh-SG', 'cmn-Hans'], beta: true, betaLabel: '\u6d4b\u8bd5\u7248' },
        { code: 'zh-Hant', label: 'Mandarin (Traditional)', nativeLabel: '\u7e41\u9ad4\u4e2d\u6587', dir: 'ltr', aliases: ['zh-TW', 'cmn-Hant'], beta: true, betaLabel: '\u6e2c\u8a66\u7248' },
        { code: 'yue-Hant', label: 'Cantonese', nativeLabel: '\u7cb5\u8a9e', dir: 'ltr', aliases: ['zh-HK', 'zh-MO', 'yue', 'yue-HK'], beta: true, betaLabel: '\u6e2c\u8a66\u7248' },
        { code: 'ja', label: 'Japanese', nativeLabel: '\u65e5\u672c\u8a9e', dir: 'ltr', aliases: ['ja-JP'], beta: true, betaLabel: '\u30d9\u30fc\u30bf\u7248' },
        { code: 'de', label: 'German', nativeLabel: 'Deutsch', dir: 'ltr', aliases: ['de-DE'], beta: true, betaLabel: 'Beta' },
        { code: 'fr', label: 'French', nativeLabel: 'Fran\u00e7ais', dir: 'ltr', aliases: ['fr-FR', 'fr-CA'], beta: true, betaLabel: 'b\u00eata' },
        { code: 'it', label: 'Italian', nativeLabel: 'Italiano', dir: 'ltr', aliases: ['it-IT'], beta: true, betaLabel: 'beta' }
    ];

    let catalog = null;
    let catalogPromise = null;
    let currentLocale = getInitialLocale(null);
    let observer = null;
    let scheduled = false;
    let builtinsPatched = false;

    function fallbackCatalog() {
        return {
            defaultLocale: 'en',
            supportedLocales: DEFAULT_LOCALES,
            locales: {}
        };
    }

    function getCatalog() {
        return catalog || fallbackCatalog();
    }

    function getPack(locale) {
        return getCatalog().locales[locale] || {};
    }

    function normalizeLocale(locale) {
        const source = (locale || '').trim() || getCatalog().defaultLocale;
        const supported = getCatalog().supportedLocales || DEFAULT_LOCALES;
        const exact = supported.find((item) => item.code.toLowerCase() === source.toLowerCase());
        if (exact) return exact.code;
        const alias = supported.find((item) => (item.aliases || []).some((value) => value.toLowerCase() === source.toLowerCase()));
        if (alias) return alias.code;

        const lower = source.toLowerCase();
        if (lower.startsWith('zh-hk') || lower.startsWith('zh-mo') || lower.startsWith('yue')) return 'yue-Hant';
        if (lower.startsWith('zh-tw') || lower.startsWith('zh-hant')) return 'zh-Hant';
        if (lower.startsWith('zh')) return 'zh-Hans';
        if (lower.startsWith('ja')) return 'ja';
        if (lower.startsWith('de')) return 'de';
        if (lower.startsWith('fr')) return 'fr';
        if (lower.startsWith('it')) return 'it';
        if (lower.startsWith('es')) return 'es';
        return getCatalog().defaultLocale;
    }

    function getInitialLocale(fetchedCatalog) {
        if (fetchedCatalog) catalog = fetchedCatalog;
        try {
            const stored = localStorage.getItem(STORAGE_KEY);
            if (stored) return normalizeLocale(stored);
        } catch (_) { }

        const browserLocales = navigator.languages && navigator.languages.length ? navigator.languages : [navigator.language];
        for (const candidate of browserLocales) {
            const normalized = normalizeLocale(candidate);
            if (normalized) return normalized;
        }

        return getCatalog().defaultLocale;
    }

    function persistLocale(locale) {
        try {
            localStorage.setItem(STORAGE_KEY, locale);
        } catch (_) { }
    }

    function interpolate(template, params) {
        if (!params) return template;
        let output = template;
        Object.entries(params).forEach(([key, value]) => {
            output = output.split(`{{${key}}}`).join(value == null ? '' : String(value));
        });
        return output;
    }

    function translateSingleToken(locale, text) {
        const terms = getPack(locale).terms || {};
        return Object.prototype.hasOwnProperty.call(terms, text) ? terms[text] : null;
    }

    function translateExactPhrase(locale, text) {
        const pack = getPack(locale);
        const phrases = pack.phrases || {};
        if (Object.prototype.hasOwnProperty.call(phrases, text)) return phrases[text];
        const sections = pack.sections || {};
        return Object.prototype.hasOwnProperty.call(sections, text) ? sections[text] : null;
    }

    function preserveWhitespace(original, translated) {
        const leading = (original.match(/^\s*/) || [''])[0];
        const trailing = (original.match(/\s*$/) || [''])[0];
        return leading + translated.trim() + trailing;
    }

    function formatNumber(locale, value) {
        return new Intl.NumberFormat(locale).format(value);
    }

    function formatRelative(locale, value, unit) {
        return new Intl.RelativeTimeFormat(locale, { numeric: 'auto' }).format(value, unit);
    }

    function applyPatterns(locale, text) {
        if (text === 'just now') return formatRelative(locale, 0, 'second');

        let match = text.match(/^\+\s+(.+)$/);
        if (match) return `+ ${translateText(locale, match[1])}`;
        match = text.match(/^←\s+(.+)$/);
        if (match) return `← ${translateText(locale, match[1])}`;
        match = text.match(/^(.+)\s+→$/);
        if (match) return `${translateText(locale, match[1])} →`;

        const lowercaseStatus = ['healthy', 'degraded', 'unhealthy', 'error', 'unknown', 'connected', 'connecting', 'disconnected', 'disabled', 'active', 'inactive'];
        if (lowercaseStatus.includes(text)) {
            const normalized = text.charAt(0).toUpperCase() + text.slice(1);
            return translateText(locale, normalized);
        }

        match = text.match(/^(\d+)\s*\/\s*page$/i);
        if (match) return `${formatNumber(locale, Number(match[1]))} / ${translateText(locale, 'page')}`;
        match = text.match(/^(\d+)s ago$/);
        if (match) return formatRelative(locale, -Number(match[1]), 'second');
        match = text.match(/^(\d+)m ago$/);
        if (match) return formatRelative(locale, -Number(match[1]), 'minute');
        match = text.match(/^(\d+)h ago$/);
        if (match) return formatRelative(locale, -Number(match[1]), 'hour');
        match = text.match(/^(\d+)d ago$/);
        if (match) return formatRelative(locale, -Number(match[1]), 'day');
        match = text.match(/^(\d+) chars$/);
        if (match) return `${formatNumber(locale, Number(match[1]))} ${translateText(locale, 'chars')}`;
        match = text.match(/^(\d+) lines$/);
        if (match) return `${formatNumber(locale, Number(match[1]))} ${translateText(locale, 'lines')}`;
        match = text.match(/^(\d+) unread$/);
        if (match) return `${formatNumber(locale, Number(match[1]))} ${translateText(locale, 'unread')}`;
        match = text.match(/^(\d+) records$/);
        if (match) return `${formatNumber(locale, Number(match[1]))} ${translateText(locale, 'records')}`;
        match = text.match(/^(\d+) selected$/);
        if (match) return `${formatNumber(locale, Number(match[1]))} ${translateText(locale, 'selected')}`;
        match = text.match(/^(\d+) idle$/);
        if (match) return `${formatNumber(locale, Number(match[1]))} ${translateText(locale, 'idle')}`;
        match = text.match(/^(\d+) working$/);
        if (match) return `${formatNumber(locale, Number(match[1]))} ${translateText(locale, 'working')}`;
        match = text.match(/^(\d+) stalled$/);
        if (match) return `${formatNumber(locale, Number(match[1]))} ${translateText(locale, 'stalled')}`;
        match = text.match(/^(\d+) failed$/);
        if (match) return `${formatNumber(locale, Number(match[1]))} ${translateText(locale, 'failed')}`;
        match = text.match(/^(\d+)\/(\d+) done$/);
        if (match) return `${formatNumber(locale, Number(match[1]))}/${formatNumber(locale, Number(match[2]))} ${translateText(locale, 'done')}`;
        match = text.match(/^, (\d+) failed$/);
        if (match) return `, ${formatNumber(locale, Number(match[1]))} ${translateText(locale, 'failed')}`;
        match = text.match(/^(\d+) file changed$/);
        if (match) return `${formatNumber(locale, Number(match[1]))} ${translateText(locale, 'file changed')}`;
        match = text.match(/^(\d+) files changed$/);
        if (match) return `${formatNumber(locale, Number(match[1]))} ${translateText(locale, 'files changed')}`;
        match = text.match(/^Files \((\d+)\)$/);
        if (match) return `${translateText(locale, 'Files')} (${formatNumber(locale, Number(match[1]))})`;
        match = text.match(/^Missions \((\d+)\)$/);
        if (match) return `${translateText(locale, 'Missions')} (${formatNumber(locale, Number(match[1]))})`;
        match = text.match(/^Mission (\d+)$/);
        if (match) return `${translateText(locale, 'Mission')} ${formatNumber(locale, Number(match[1]))}`;
        match = text.match(/^(.+?) -- click to sort$/);
        if (match) return `${translateText(locale, match[1])} -- ${translateText(locale, 'click to sort')}`;
        match = text.match(/^Select all (.+)$/);
        if (match) return `${translateText(locale, 'Select all')} ${translateText(locale, match[1])}`;
        match = text.match(/^Select this (.+)$/);
        if (match) return `${translateText(locale, 'Select this')} ${translateText(locale, match[1])}`;
        match = text.match(/^Delete Selected (.+)$/);
        if (match) return `${translateText(locale, 'Delete Selected')} ${translateText(locale, match[1])}`;
        match = text.match(/^Copy (.+) HTTP config to clipboard$/);
        if (match) return interpolate(translateText(locale, 'Copy {{title}} HTTP config to clipboard'), { title: match[1] });
        match = text.match(/^Copy (.+) STDIO config to clipboard$/);
        if (match) return interpolate(translateText(locale, 'Copy {{title}} STDIO config to clipboard'), { title: match[1] });
        match = text.match(/^Signing in as (.+) to (.+)$/);
        if (match) return interpolate(translateText(locale, 'Signing in as {{email}} to {{tenant}}'), { email: match[1], tenant: match[2] });
        match = text.match(/^Delete ([A-Za-z ]+) "(.+)"\? This cannot be undone\.$/);
        if (match) return interpolate(translateText(locale, 'Delete {{entity}} "{{name}}"? This cannot be undone.'), { entity: translateText(locale, match[1]).toLowerCase(), name: match[2] });
        match = text.match(/^Delete ([A-Za-z ]+) (.+)\? This cannot be undone\.$/);
        if (match) return interpolate(translateText(locale, 'Delete {{entity}} {{name}}? This cannot be undone.'), { entity: translateText(locale, match[1]).toLowerCase(), name: match[2] });
        match = text.match(/^Delete dock (.+)\? This will clean up the git worktree and cannot be undone\.$/);
        if (match) return interpolate(translateText(locale, 'Delete dock {{name}}? This will clean up the git worktree and cannot be undone.'), { name: match[1] });
        match = text.match(/^Delete (event|signal) (.+)\?$/);
        if (match) return interpolate(translateText(locale, 'Delete {{entity}} {{name}}?'), { entity: translateText(locale, match[1]).toLowerCase(), name: match[2] });
        match = text.match(/^Stop captain "(.+)"\? This will halt the current mission\.$/);
        if (match) return interpolate(translateText(locale, 'Stop captain "{{name}}"? This will halt the current mission.'), { name: match[1] });
        match = text.match(/^Recall captain "(.+)"\? The captain will finish current work and return to idle\.$/);
        if (match) return interpolate(translateText(locale, 'Recall captain "{{name}}"? The captain will finish current work and return to idle.'), { name: match[1] });
        match = text.match(/^Remove captain "(.+)"\? This cannot be undone\.$/);
        if (match) return interpolate(translateText(locale, 'Remove captain "{{name}}"? This cannot be undone.'), { name: match[1] });
        match = text.match(/^Delete failed: (.+)$/);
        if (match) return interpolate(translateText(locale, 'Delete failed: {{message}}'), { message: match[1] });
        match = text.match(/^Save failed: (.+)$/);
        if (match) return interpolate(translateText(locale, 'Save failed: {{message}}'), { message: match[1] });
        match = text.match(/^Failed to save settings: (.+)$/);
        if (match) return interpolate(translateText(locale, 'Failed to save settings: {{message}}'), { message: match[1] });
        match = text.match(/^Unable to load existing Armada resources: (.+)$/);
        if (match) return interpolate(translateText(locale, 'Unable to load existing Armada resources: {{message}}'), { message: match[1] });
        match = text.match(/^(Fleet|Vessel|Captain) creation failed: (.+)$/);
        if (match) return interpolate(translateText(locale, '{{entity}} creation failed: {{message}}'), { entity: translateText(locale, match[1]), message: match[2] });
        match = text.match(/^Dispatch failed: (.+)$/);
        if (match) return interpolate(translateText(locale, 'Dispatch failed: {{message}}'), { message: match[1] });
        match = text.match(/^Created (fleet|vessel|captain) "(.+)"\.$/i);
        if (match) return interpolate(translateText(locale, 'Created {{entity}} "{{name}}".'), { entity: translateText(locale, match[1]).toLowerCase(), name: match[2] });
        match = text.match(/^Using (fleet|vessel|captain) "(.+)"\.$/i);
        if (match) return interpolate(translateText(locale, 'Using {{entity}} "{{name}}".'), { entity: translateText(locale, match[1]).toLowerCase(), name: match[2] });
        match = text.match(/^Mission status refreshed: (.+)\.$/);
        if (match) return interpolate(translateText(locale, 'Mission status refreshed: {{status}}.'), { status: translateText(locale, match[1]) });
        match = text.match(/^([A-Za-z ]+): (.+)$/);
        if (match) {
            const translatedPrefix = translateText(locale, match[1]);
            if (translatedPrefix !== match[1]) return `${translatedPrefix}: ${match[2]}`;
        }
        match = text.match(/^(Mission|Voyage|Captain) "(.+)" \u2014 (.+)$/);
        if (match) return `${translateText(locale, match[1])} "${match[2]}" \u2014 ${translateText(locale, match[3])}`;
        match = text.match(/^(Mission|Voyage|Captain): (.+)$/);
        if (match) return `${translateText(locale, match[1])}: ${match[2]}`;
        match = text.match(/^Log: (.+)$/);
        if (match) return `${translateText(locale, 'Log')}: ${match[1]}`;
        match = text.match(/^Health: (.+)$/);
        if (match) return `${translateText(locale, 'Health')}: ${translateText(locale, match[1])}`;
        return null;
    }

    function translateText(locale, text) {
        if (!text || locale === 'en') return text;
        const trimmed = text.trim();
        if (!trimmed) return text;

        const exact = translateExactPhrase(locale, trimmed);
        if (exact) return preserveWhitespace(text, exact);

        const dynamic = applyPatterns(locale, trimmed);
        if (dynamic) return preserveWhitespace(text, dynamic);

        if (!trimmed.includes('\n') && !trimmed.includes(' ') && !trimmed.includes('\t')) {
            const term = translateSingleToken(locale, trimmed);
            if (term) return preserveWhitespace(text, term);
        }

        return text;
    }

    function shouldSkip(element) {
        if (!element) return false;
        return !!element.closest('[data-i18n-skip="true"], pre, code, textarea, script, style, svg, .mono');
    }

    function getOriginalText(node) {
        if (node.__armadaI18nOriginal === undefined) {
            node.__armadaI18nOriginal = node.nodeValue || '';
        }
        return node.__armadaI18nOriginal;
    }

    function getOriginalAttribute(element, attr) {
        const dataAttr = `data-armada-i18n-orig-${attr}`;
        if (element.hasAttribute(dataAttr)) return element.getAttribute(dataAttr);
        const original = element.getAttribute(attr);
        if (original !== null) element.setAttribute(dataAttr, original);
        return original;
    }

    function getOriginalButtonValue(element) {
        const dataAttr = 'data-armada-i18n-orig-value';
        if (element.hasAttribute(dataAttr)) return element.getAttribute(dataAttr);
        element.setAttribute(dataAttr, element.value);
        return element.value;
    }

    function translateAttributes(element) {
        if (shouldSkip(element)) return;

        ATTRIBUTE_NAMES.forEach((attr) => {
            const original = getOriginalAttribute(element, attr);
            if (original !== null) {
                element.setAttribute(attr, translateText(currentLocale, original));
            }
        });

        if (element instanceof HTMLInputElement && ['button', 'submit', 'reset'].includes(element.type)) {
            const original = getOriginalButtonValue(element);
            element.value = translateText(currentLocale, original);
        }
    }

    function translateTextNode(node) {
        if (shouldSkip(node.parentElement)) return;
        const original = getOriginalText(node);
        const translated = translateText(currentLocale, original);
        if (translated !== node.nodeValue) node.nodeValue = translated;
    }

    function translateSubtree(root) {
        if (!root) return;
        if (root instanceof HTMLElement) translateAttributes(root);

        const elements = root.querySelectorAll ? root.querySelectorAll('*') : [];
        elements.forEach((element) => translateAttributes(element));

        const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
        let current = walker.nextNode();
        while (current) {
            translateTextNode(current);
            current = walker.nextNode();
        }
    }

    function applyDocumentLocale() {
        const meta = (getCatalog().supportedLocales || DEFAULT_LOCALES).find((item) => item.code === currentLocale) || DEFAULT_LOCALES[0];
        document.documentElement.lang = meta.code;
        document.documentElement.dir = meta.dir;
        if (!document.documentElement.hasAttribute(TITLE_DATA_KEY)) {
            document.documentElement.setAttribute(TITLE_DATA_KEY, document.title);
        }
        const originalTitle = document.documentElement.getAttribute(TITLE_DATA_KEY) || document.title;
        document.title = translateText(currentLocale, originalTitle);
    }

    function applyTranslations() {
        applyDocumentLocale();
        if (document.body) translateSubtree(document.body);
    }

    function scheduleApply() {
        if (scheduled) return;
        scheduled = true;
        window.requestAnimationFrame(() => {
            scheduled = false;
            applyTranslations();
        });
    }

    function ensureObserver() {
        if (observer || !document.body) return;
        observer = new MutationObserver(scheduleApply);
        observer.observe(document.body, {
            childList: true,
            subtree: true,
            characterData: true,
            attributes: true,
            attributeFilter: ATTRIBUTE_NAMES.concat(['value'])
        });
    }

    function loadCatalog() {
        if (!catalogPromise) {
            catalogPromise = fetch('/dashboard/i18n/armada.json', { cache: 'force-cache' })
                .then(async (response) => {
                    if (!response.ok) throw new Error('Failed to load i18n catalog');
                    return await response.json();
                })
                .catch(() => null);
        }
        return catalogPromise;
    }

    function hasExplicitLocale(locales) {
        if (Array.isArray(locales)) return locales.length > 0;
        return typeof locales === 'string' ? locales.trim().length > 0 : locales != null;
    }

    function installLocaleSensitiveBuiltins() {
        if (builtinsPatched) return;
        builtinsPatched = true;

        const originalDateTime = Date.prototype.toLocaleString;
        const originalDateOnly = Date.prototype.toLocaleDateString;
        const originalTimeOnly = Date.prototype.toLocaleTimeString;
        const originalNumber = Number.prototype.toLocaleString;

        Date.prototype.toLocaleString = function armadaDateTime(locales, options) {
            const nextLocales = hasExplicitLocale(locales) ? locales : currentLocale;
            return originalDateTime.call(this, nextLocales, options);
        };

        Date.prototype.toLocaleDateString = function armadaDateOnly(locales, options) {
            const nextLocales = hasExplicitLocale(locales) ? locales : currentLocale;
            return originalDateOnly.call(this, nextLocales, options);
        };

        Date.prototype.toLocaleTimeString = function armadaTimeOnly(locales, options) {
            const nextLocales = hasExplicitLocale(locales) ? locales : currentLocale;
            return originalTimeOnly.call(this, nextLocales, options);
        };

        Number.prototype.toLocaleString = function armadaNumber(locales, options) {
            const nextLocales = hasExplicitLocale(locales) ? locales : currentLocale;
            return originalNumber.call(this, nextLocales, options);
        };
    }

    function formatAbsoluteDateTime(utc) {
        if (!utc) return '';
        return new Date(utc).toLocaleString(currentLocale);
    }

    function formatDateOnly(utc) {
        if (!utc) return '';
        return new Date(utc).toLocaleDateString(currentLocale);
    }

    function formatRelativeFromUtc(utc) {
        if (!utc) return '-';
        const then = new Date(utc).getTime();
        const now = Date.now();
        const diffSeconds = Math.round((then - now) / 1000);
        const abs = Math.abs(diffSeconds);

        if (abs < 60) return formatRelative(currentLocale, diffSeconds, 'second');
        const diffMinutes = Math.round(diffSeconds / 60);
        if (Math.abs(diffMinutes) < 60) return formatRelative(currentLocale, diffMinutes, 'minute');
        const diffHours = Math.round(diffMinutes / 60);
        if (Math.abs(diffHours) < 24) return formatRelative(currentLocale, diffHours, 'hour');
        const diffDays = Math.round(diffHours / 24);
        return formatRelative(currentLocale, diffDays, 'day');
    }

    async function ready() {
        const loaded = await loadCatalog();
        if (loaded) catalog = loaded;
        currentLocale = normalizeLocale(currentLocale);
        persistLocale(currentLocale);
        installLocaleSensitiveBuiltins();
        applyTranslations();
        ensureObserver();
        return catalog;
    }

    window.ArmadaI18n = {
        ready,
        getSupportedLocales() {
            return (catalog && catalog.supportedLocales) || DEFAULT_LOCALES;
        },
        getInitialLocale() {
            return getInitialLocale(catalog);
        },
        getLocale() {
            return currentLocale;
        },
        setLocale(nextLocale) {
            currentLocale = normalizeLocale(nextLocale);
            persistLocale(currentLocale);
            applyTranslations();
            return currentLocale;
        },
        t(text, params) {
            return interpolate(translateText(currentLocale, text), params);
        },
        translateText(text) {
            return translateText(currentLocale, text);
        },
        formatDateTime: formatAbsoluteDateTime,
        formatDate: formatDateOnly,
        formatRelativeFromUtc,
        formatNumber(value, options) {
            return new Intl.NumberFormat(currentLocale, options).format(value);
        }
    };
})();

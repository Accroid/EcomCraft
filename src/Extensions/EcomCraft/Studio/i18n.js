/**
 * EcomCraft workspace translations.
 * The Chinese source text is also the key: other languages map it in window.EcomCraftLanguages[code] (see lang/en.js).
 * Static page text and attributes, and any text the page inserts later, are translated by exact match through a MutationObserver;
 * text with variable parts must go through I18n.t('…{0}…', value) because it cannot match a table entry.
 * Elements marked data-i18n-skip (and form fields, scripts, styles) are never touched, so user content stays as typed.
 */
class I18n {
    static sourceLanguage = 'zh-CN';
    static supported = ['zh-CN', 'en'];
    static storageKey = 'ecomcraft.language';
    static attributes = ['title', 'placeholder', 'aria-label', 'alt'];
    /** How the language button offers each language, written in that language and never translated. */
    static switchTo = { 'zh-CN': { text: '中文', label: '切换到中文' }, en: { text: 'EN', label: 'Switch to English' } };
    static current = 'zh-CN';
    static textState = new WeakMap();
    static attrState = new WeakMap();
    static reverse = null;
    static listeners = [];
    static observer = null;
    static translating = false;

    /** Characters that mark text as Chinese: CJK ideographs, CJK punctuation and full-width forms. */
    static hasChinese(text) {
        return /[　-〿㐀-鿿＀-￯]/.test(text || '');
    }

    /** The lookup form of displayed text: trimmed, with runs of whitespace (line breaks and indentation in the page source) as one space. */
    static normalize(text) {
        return String(text || '').trim().replace(/\s+/g, ' ');
    }

    /** Reads the saved choice, or follows the browser language; storage may be unavailable in private mode. */
    static detect() {
        let saved = null;
        try {
            saved = window.localStorage.getItem(this.storageKey);
        }
        catch (error) {
            saved = null;
        }
        if (this.supported.includes(saved)) {
            return saved;
        }
        let browser = String(navigator.language || '').toLowerCase();
        return browser.startsWith('zh') ? 'zh-CN' : 'en';
    }

    /** The translation table of the current language, or null for the source language. */
    static table() {
        if (this.current == this.sourceLanguage) {
            return null;
        }
        return (window.EcomCraftLanguages || {})[this.current] || null;
    }

    /** Translates source text and fills {0}, {1}… with the given values. Unknown text is returned unchanged. */
    static t(source, ...args) {
        let text = String(source ?? '');
        let table = this.table();
        if (table && typeof table[text] == 'string' && table[text]) {
            text = table[text];
        }
        if (!args.length) {
            return text;
        }
        return text.replace(/\{(\d+)\}/g, (match, index) => index < args.length ? String(args[index] ?? '') : match);
    }

    /** Locale for dates and numbers. */
    static locale() {
        return this.current == 'en' ? 'en-US' : 'zh-CN';
    }

    /** Maps translated text back to its source, so text inserted in another language can switch back. */
    static sourceOf(text) {
        if (this.hasChinese(text)) {
            return text;
        }
        if (!this.reverse) {
            this.reverse = new Map();
            let languages = window.EcomCraftLanguages || {};
            for (let code of Object.keys(languages)) {
                for (let [source, translated] of Object.entries(languages[code])) {
                    if (translated && !this.reverse.has(translated)) {
                        this.reverse.set(translated, source);
                    }
                }
            }
        }
        return this.reverse.get(text) || null;
    }

    /** Whether a text node belongs to user content or form state that must not be translated. */
    static skipped(node) {
        let element = node.parentElement;
        return !element || Boolean(element.closest('[data-i18n-skip], textarea, script, style, code'));
    }

    /** Translates one text node, remembering its source so a later language switch can redo it. */
    static translateText(node) {
        if (this.skipped(node)) {
            return;
        }
        let value = node.nodeValue;
        let state = this.textState.get(node);
        if (!state || state.written != value) {
            let core = this.normalize(value);
            let source = core ? this.sourceOf(core) : null;
            if (!source) {
                this.textState.delete(node);
                return;
            }
            state = { source, written: value };
            this.textState.set(node, state);
        }
        let start = value.indexOf(value.trim());
        let lead = value.slice(0, Math.max(0, start));
        let trail = value.slice(lead.length + value.trim().length);
        let next = lead + this.t(state.source) + trail;
        if (next != value) {
            node.nodeValue = next;
        }
        state.written = next;
    }

    /** Translates the translatable attributes of one element. */
    static translateAttributes(element) {
        if (element.closest('[data-i18n-skip]')) {
            return;
        }
        let states = this.attrState.get(element);
        for (let name of this.attributes) {
            let value = element.getAttribute(name);
            if (!value) {
                continue;
            }
            let state = states?.[name];
            if (!state || state.written != value) {
                let source = this.sourceOf(this.normalize(value));
                if (!source) {
                    continue;
                }
                states = states || {};
                state = { source, written: value };
                states[name] = state;
                this.attrState.set(element, states);
            }
            let next = this.t(state.source);
            if (next != value) {
                element.setAttribute(name, next);
            }
            state.written = next;
        }
    }

    /** Translates a subtree: its text nodes and attributes. */
    static translateTree(root) {
        if (!root) {
            return;
        }
        this.translating = true;
        try {
            if (root.nodeType == Node.TEXT_NODE) {
                this.translateText(root);
                return;
            }
            if (root.nodeType != Node.ELEMENT_NODE && root.nodeType != Node.DOCUMENT_NODE) {
                return;
            }
            if (root.nodeType == Node.ELEMENT_NODE) {
                this.translateAttributes(root);
            }
            let walker = document.createTreeWalker(root, NodeFilter.SHOW_ELEMENT | NodeFilter.SHOW_TEXT);
            let node = walker.nextNode();
            while (node) {
                if (node.nodeType == Node.TEXT_NODE) {
                    this.translateText(node);
                }
                else {
                    this.translateAttributes(node);
                }
                node = walker.nextNode();
            }
        }
        finally {
            this.translating = false;
        }
    }

    /** Applies the language to the document and the page title. */
    static apply() {
        document.documentElement.lang = this.current;
        this.translateTree(document.body);
        let title = document.querySelector('title');
        if (title) {
            this.translateText(title.firstChild || document.createTextNode(''));
        }
    }

    /** Switches language without reloading, so typed prompts and chosen files stay; registered views re-render their dynamic text. */
    static set(language) {
        if (!this.supported.includes(language) || language == this.current) {
            return;
        }
        this.current = language;
        try {
            window.localStorage.setItem(this.storageKey, language);
        }
        catch (error) {
            // The choice then lasts only for this page; the interface still switches.
        }
        this.apply();
        for (let listener of this.listeners) {
            try {
                listener(language);
            }
            catch (error) {
                console.error('EcomCraft language listener failed', error);
            }
        }
    }

    /** Registers a callback run after each language switch. */
    static onChange(listener) {
        this.listeners.push(listener);
    }

    /** Detects the language, translates the parsed page, then keeps translating text the page inserts or changes. */
    static init() {
        this.current = this.detect();
        this.apply();
        this.observer = new MutationObserver(records => {
            // Source-language text needs no change; switching back re-walks the whole page in apply().
            if (this.translating || this.current == this.sourceLanguage) {
                return;
            }
            for (let record of records) {
                if (record.type == 'childList') {
                    for (let node of record.addedNodes) {
                        this.translateTree(node);
                    }
                }
                else if (record.type == 'characterData') {
                    this.translateTree(record.target);
                }
                else if (record.type == 'attributes') {
                    this.translateAttributes(record.target);
                }
            }
        });
        this.observer.observe(document.body, { subtree: true, childList: true, characterData: true, attributes: true, attributeFilter: this.attributes });
    }
}

I18n.init();

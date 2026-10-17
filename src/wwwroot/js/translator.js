language = getCookie("display_language");
translate_keys = {};
language_data = {};
known_translatables = {};

class Translatable {
    constructor(key) {
        this.key = key;
        known_translatables[key] = this;
    }

    get() {
        return translate(this.key);
    }
}

let neverTranslateCodes = [
    '192.168.', // addresses (eg backends list)
    '--listen', // args (eg backends list)
    'GeForce RTX', // hardware (eg server info resources)
    'Stable Diffusion XL 1.0 Base', // Models list data
    'sdxlofficial/', // presets list
    'raw/2024-', 'raw/2025-' // history list
]

/** Safety check for known text keys that definitely shouldn't be translated */
function validateTranslationSafety(text, context) {
    if (text == null || text == "") {
        return true;
    }
    if (neverTranslateCodes.some(code => text.includes(code))) {
        console.log(`ERROR BAD TRANSLATION CALL ${text} translated in context '${context}' at stack ${new Error().stack}`);
        return true;
    }
    return false;
}

function translate(text) {
    if (validateTranslationSafety(text, "translate")) {
        return text;
    }
    let result = translate_keys[text];
    if (!result) {
        translate_keys[text] = "";
        return text;
    }
    return result;
}

function translatable(key) {
    let val = translate_keys[key];
    if (val) {
        return val;
    }
    return new Translatable(key);
}

function debugSubmitTranslatables() {
    let keys = Object.keys(translate_keys).concat(Object.keys(known_translatables));
    genericRequest('DebugLanguageAdd', { set: keys }, data => { });
}

function applyTranslations(root = null) {
    if (!language_data || !language_data.local_name) {
        return;
    }
    let dropdown = document.getElementById('language_dropdown_link');
    if (dropdown) {
        let newHtml = `<img class="translate-img" src="imgs/flags/${language_data.code}.jpg" /> ${escapeHtml(language_data.local_name)}`;
        if (dropdown.innerHTML != newHtml) { // try to avoid reload flicker
            dropdown.innerHTML = newHtml;
        }
    }
    if (root == null) {
        root = document;
    }
    function doTranslateNow(elem) {
        if (elem.title) {
            if (validateTranslationSafety(elem.dataset.pretranslated_title || elem.title, `element title ${elem.id}`)) {
                return;
            }
            let translated = translate(elem.dataset.pretranslated_title || elem.title);
            if (translated != elem.title) {
                if (!elem.dataset.pretranslated_title) {
                    elem.dataset.pretranslated_title = elem.title;
                }
                elem.title = translated;
            }
            // If the title is not translated yet, continue to handle textContent (do not bail out)
        }
        if (elem.placeholder) {
            if (validateTranslationSafety(elem.dataset.pretranslated_placeholder || elem.placeholder, `element placeholder ${elem.id}`)) {
                return;
            }
            let translated = translate(elem.dataset.pretranslated_placeholder || elem.placeholder);
            if (translated == elem.placeholder) {
                return;
            }
            if (!elem.dataset.pretranslated_placeholder) {
                elem.dataset.pretranslated_placeholder = elem.placeholder;
            }
            elem.placeholder = translated;
            return; // placeholdered elements are text inputs, ie don't replace content
        }
        if (elem.textContent && !elem.classList.contains("translate-no-text")) {
            if (validateTranslationSafety(elem.dataset.pretranslated || elem.innerHTML, `element textContent ${elem.id}`)) {
                return;
            }
            let hasChildElement = [...elem.childNodes].some(n => n.nodeType === 1);
            if (hasChildElement) {
                // Static HTML markup (eg links embedded in a sentence): try the full original innerHTML
                // as one translation key before splitting the text into per-line spans.
                if (!elem.dataset.pretranslated && !elem.dataset.htmlChecked) {
                    elem.dataset.htmlChecked = '1';
                    let htmlKey = elem.innerHTML.trim();
                    let stored = document.documentElement.dataset.htmlkeys;
                    let keyArr = stored ? JSON.parse(stored) : [];
                    keyArr.push(htmlKey);
                    document.documentElement.dataset.htmlkeys = JSON.stringify(keyArr);
                    let htmlTranslated = translate(htmlKey);
                    if (htmlTranslated !== htmlKey) {
                        elem.innerHTML = safeHtmlOnly(htmlTranslated);
                        elem.classList.add('translate-no-text');
                        return;
                    }
                }
                // If this element was built from a full multi-line description (has data-pretranslated),
                // try the full-text translation first before splitting into per-line spans.
                if (elem.dataset.pretranslated) {
                    let origText = elem.dataset.pretranslated;
                    let fullTranslated = translate(origText);
                    if (fullTranslated !== origText) {
                        if (origText.includes('<')) {
                            elem.innerHTML = safeHtmlOnly(fullTranslated);
                        }
                        else {
                            let toDisplayHtml = (s) => s.replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;')
                                .replaceAll('"', '&quot;').replaceAll("'", '&#039;').replaceAll('\n', '<br>');
                            elem.innerHTML = toDisplayHtml(fullTranslated);
                        }
                        elem.classList.add('translate-no-text');
                        return;
                    }
                }
                // Wrap EVERY direct non-empty text node (not just leading/trailing), preserving surrounding whitespace
                let textNodes = [...elem.childNodes].filter(n => n.nodeType === 3 && n.textContent.trim() != '');
                let wrapped = false;
                for (let node of textNodes) {
                    let t = node.textContent;
                    let lead = t.match(/^\s*/)[0];
                    let trail = t.match(/\s*$/)[0];
                    let core = t.trim();
                    let span = document.createElement('span');
                    span.className = 'translate';
                    span.dataset.pretranslated = core;
                    span.textContent = translate(core);
                    let frag = document.createDocumentFragment();
                    if (lead) frag.appendChild(document.createTextNode(lead));
                    frag.appendChild(span);
                    if (trail) frag.appendChild(document.createTextNode(trail));
                    node.replaceWith(frag);
                    wrapped = true;
                }
                if (wrapped) {
                    elem.classList.add("translate-no-text");
                    for (let subElem of elem.querySelectorAll(".translate")) {
                        doTranslateNow(subElem);
                    }
                    return;
                }
            }
            // Plain text element: translate using trimmed content, preserving surrounding whitespace
            let full = elem.textContent;
            let lead = full.match(/^\s*/)[0];
            let trail = full.match(/\s*$/)[0];
            let trimmed = full.trim();
            let translated = translate(elem.dataset.pretranslated || trimmed);
            if (translated == trimmed) {
                return;
            }
            if (!elem.dataset.pretranslated) {
                elem.dataset.pretranslated = trimmed;
            }
            elem.dataset.textContentWasTranslated = true;
            elem.textContent = lead + translated + trail;
        }
    }
    for (let elem of root.querySelectorAll(".translate")) {
        doTranslateNow(elem);
    }
}

function loadAndApplyTranslations(callback = null) {
    genericRequest('GetLanguage', { language: language }, data => {
        language_data = data.language;
        translate_keys = data.language.keys;
        applyTranslations();
        if (callback) {
            callback();
        }
    });
}

function changeLanguage(code) {
    language = code;
    setCookie("display_language", code, 365);
    let langSetting = document.getElementById('usersettings_language');
    if (langSetting) {
        langSetting.value = code;
        triggerChangeFor(langSetting);
        save_user_settings();
    }
    let installerSetting = document.getElementById('installer_language');
    if (installerSetting) {
        installerSetting.value = code;
        triggerChangeFor(installerSetting);
    }
    loadAndApplyTranslations(() => {
        if (typeof installer !== 'undefined' && installer && installer.check) {
            installer.check();
        }
    });
}

function translateableHtml(key) {
    if (key.replaceAll('<br>', '').includes('<')) {
        return key;
    }
    return `<span class="translate" data-pretranslated="${key.replaceAll('<br>', '')}">${translate(key)}</span>`;
}

/**
 * Renders a settings/param description into translatable HTML.
 * Plain multi-line text is translated (with newlines rendered as <br>),
 * while safe HTML content is passed through the safe-HTML renderer.
 */
function translatableDescriptionHtml(text) {
    if (text == null || text == '') {
        return '';
    }
    if (!text.includes('<')) {
        let translated = translate(text);
        let escDisplay = (s) => s.replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;')
            .replaceAll('"', '&quot;').replaceAll("'", '&#039;').replaceAll('\n', '<br>');
        return `<span class="translate" data-pretranslated="${escapeHtmlNoBr(text)}">${escDisplay(translated)}</span>`;
    }
    // Safe HTML content: if a full-key translation exists (it must preserve safe tags), use it
    let translatedHtml = translate(text);
    return safeHtmlOnly(translatedHtml !== text ? translatedHtml : text);
}

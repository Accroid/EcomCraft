(() => {
    'use strict';
    /** Follows the language chosen in the workspace (same origin, same storage key as Studio/i18n.js), else the browser language. */
    function useEnglish() {
        let saved = null;
        try {
            saved = window.localStorage.getItem('ecomcraft.language');
        }
        catch (error) {
            saved = null;
        }
        if (saved == 'en' || saved == 'zh-CN') {
            return saved == 'en';
        }
        return !String(navigator.language || '').toLowerCase().startsWith('zh');
    }
    function addStudioEntry() {
        const navigation = document.getElementById('toptablist');
        if (!navigation || document.getElementById('ecomcraft_studio_entry')) {
            return;
        }
        const item = document.createElement('li');
        item.className = 'nav-item';
        item.setAttribute('role', 'presentation');
        const link = document.createElement('a');
        link.id = 'ecomcraft_studio_entry';
        link.className = 'nav-link';
        link.href = '/EcomCraft';
        link.target = '_blank';
        link.rel = 'noopener';
        let english = useEnglish();
        link.title = english ? 'Open EcomCraft Studio in a new tab' : '在新标签页打开 EcomCraft 创作工作台';
        link.textContent = english ? 'EcomCraft Studio ↗' : 'EcomCraft 创作工作台 ↗';
        item.append(link);
        navigation.prepend(item);
    }
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', addStudioEntry, { once: true });
    }
    else {
        addStudioEntry();
    }
})();

(() => {
    'use strict';
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
        link.title = '在新标签页打开 EcomCraft 创作工作台';
        link.textContent = 'EcomCraft 创作工作台 ↗';
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

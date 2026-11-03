'use strict';

/**
 * Simple mode: the visible AI label option of the export panel (F49). The checkbox is built into the export panel here so the shared page markup stays
 * untouched; its default and whether the user may turn it come from the platform rule (EcomCraftStudioExportOptions), which stays unverified until the
 * AI-labelling regulation and the platform rules are checked, so everything ships off by default. Static methods are copied onto SimpleMode by adopt().
 */
class SimpleAiLabelPart {
    /** Whether the user wants the visible AI label on exported pictures: { wanted, touched }. Untouched follows the platform rule's default. Kept on the
     * part class itself: adopt() copies methods only, so state fields do not land on SimpleMode. */
    static aiLabel = { wanted: false, touched: false };

    /** Builds the label checkbox inside the export panel once and keeps it in step with the chosen platform's rule. */
    static renderAiLabel() {
        let tr = this.api.tr;
        let panel = this.api.$('simple-export');
        if (!panel) {
            return;
        }
        let row = document.getElementById('simple-export-ai-label-row');
        if (!row) {
            row = document.createElement('label');
            row.id = 'simple-export-ai-label-row';
            row.className = 'simple-ai-label';
            let box = document.createElement('input');
            box.type = 'checkbox';
            box.id = 'simple-export-ai-label';
            box.addEventListener('change', () => {
                SimpleAiLabelPart.aiLabel = { wanted: box.checked, touched: true };
            });
            row.append(box);
            let text = document.createElement('span');
            text.id = 'simple-export-ai-label-text';
            row.append(text);
            let hint = document.createElement('span');
            hint.className = 'hint';
            hint.id = 'simple-export-ai-label-hint';
            let actions = panel.querySelector('.recover-actions');
            panel.insertBefore(row, actions);
            panel.insertBefore(hint, actions);
        }
        let platform = this.exportPlatform();
        let rule = platform && platform.ai_label ? platform.ai_label : null;
        let forced = rule ? rule.user_can_disable === false : false;
        let enabled = rule ? rule.enabled === true : false;
        let box = this.api.$('simple-export-ai-label');
        box.disabled = Boolean(forced);
        box.checked = forced ? enabled : (SimpleAiLabelPart.aiLabel.touched ? SimpleAiLabelPart.aiLabel.wanted : enabled);
        this.api.$('simple-export-ai-label-text').textContent = tr('加 AI 生成标识');
        this.api.$('simple-export-ai-label-hint').textContent = !platform ? ''
            : forced && enabled ? tr('这个平台的规则要求加可见 AI 标识，不能关闭（规则待核对）。')
            : forced ? tr('这个平台的规则不允许加可见 AI 标识（规则待核对）。')
            : tr('导出时在图片角落画上“AI 生成”字样，只改导出的文件，不改原图。规则待核对，默认关闭。');
    }

    /** The ai_label field of an export request: the checkbox's state when the platform leaves the choice to the user, else the rule's locked value. */
    static aiLabelField() {
        let platform = this.exportPlatform();
        let rule = platform && platform.ai_label ? platform.ai_label : null;
        if (rule && rule.user_can_disable === false) {
            return rule.enabled === true;
        }
        let box = document.getElementById('simple-export-ai-label');
        return box ? box.checked : false;
    }
}
SimpleMode.adopt(SimpleAiLabelPart);

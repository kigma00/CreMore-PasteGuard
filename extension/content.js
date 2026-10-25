(function () {
  'use strict';
  const engine = globalThis.PasteGuard;
  let prepared = null;
  let loaded = false;

  function receive(policy) {
    try {
      prepared = policy ? engine.preparePolicy(policy) : null;
      loaded = true;
    } catch (_) { /* Keep the last verified policy. */ }
  }

  function notifyBlocked(rule, reason) {
    // Only metadata leaves this isolated content script, never pasted text or URL paths.
    chrome.runtime.sendMessage({ type: 'blocked', rule: rule.name, reason }).catch(() => {});
    if (!document.documentElement || document.getElementById('pasteguard-notice')) return;
    const notice = document.createElement('div');
    notice.id = 'pasteguard-notice';
    const shadow = notice.attachShadow({ mode: 'closed' });
    const message = document.createElement('div');
    message.textContent = reason === 'limit'
      ? 'CreMore PasteGuard: 검사 한도를 초과해 붙여넣기를 차단했습니다.'
      : 'CreMore PasteGuard: ' + rule.name + ' 규칙으로 붙여넣기를 차단했습니다.';
    message.setAttribute('role', 'alert');
    message.style.cssText = 'all:initial;display:block;max-width:420px;background:#172e34;color:#fff;border:1px solid #7de0bf;border-radius:12px;padding:14px 18px;font:14px/1.5 system-ui,sans-serif;box-shadow:0 6px 24px #0004';
    notice.style.cssText = 'all:initial;position:fixed;right:18px;bottom:18px;z-index:2147483647;pointer-events:none';
    shadow.appendChild(message);
    document.documentElement.appendChild(notice);
    setTimeout(() => notice.remove(), 4000);
  }

  function onPaste(event) {
    if (!loaded || !prepared || !prepared.enabled || !event.clipboardData) return;
    const result = engine.decidePaste(prepared, location.href, event.clipboardData.getData('text/plain'));
    if (!result.block) return;
    event.preventDefault();
    event.stopImmediatePropagation();
    notifyBlocked(result.rule, result.reason);
  }

  // Register before awaiting policy initialization. Browser delivery of trusted paste events
  // covers keyboard and context-menu paste in ordinary fields and contenteditable elements.
  window.addEventListener('paste', onPaste, true);
  chrome.runtime.onMessage.addListener((message, _sender, reply) => {
    if (message.type === 'policy') receive(message.policy);
    if (message.type === 'frameStatus') reply({ ready: loaded && !!prepared, enabled: !!prepared?.enabled });
  });
  chrome.runtime.sendMessage({ type: 'getPolicy' }).then(message => receive(message.policy)).catch(() => {});
})();

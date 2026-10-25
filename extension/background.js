'use strict';
importScripts('matcher.js');

const API = 'http://127.0.0.1:17843';
let key = '';
let policy = null;
let lastSync = 0;
let error = '';
let busy = false;
let pendingSync = false;
let attempts = [];

const ready = (async () => {
  // Prevent content scripts from reading the pairing secret. They receive only policy data.
  await chrome.storage.local.setAccessLevel({ accessLevel: 'TRUSTED_CONTEXTS' });
  const stored = await chrome.storage.local.get(['pairingKey', 'policy', 'lastSync']);
  key = stored.pairingKey || '';
  try { if (stored.policy) { PasteGuard.preparePolicy(stored.policy); policy = stored.policy; } } catch (_) {}
  lastSync = stored.lastSync || 0;
})();

function status() { return { paired: !!key, policy, lastSync, error }; }

async function broadcast() {
  const tabs = await chrome.tabs.query({});
  await Promise.allSettled(tabs.map(tab => chrome.tabs.sendMessage(tab.id, { type: 'policy', policy })));
}

async function sync() {
  await ready;
  if (!key) return status();
  if (busy) { pendingSync = true; return status(); }
  busy = true;
  const requestKey = key;
  try {
    const response = await fetch(API + '/policy', {
      headers: { 'X-PasteGuard-Key': requestKey }, cache: 'no-store', signal: AbortSignal.timeout(2500)
    });
    if (!response.ok) throw new Error(response.status === 401 || response.status === 403
      ? '연결 키를 확인하세요.' : '앱 응답 오류 (' + response.status + ')');
    const next = await response.json();
    if (key !== requestKey) return status();
    PasteGuard.preparePolicy(next);
    const changed = JSON.stringify(policy) !== JSON.stringify(next);
    policy = next;
    lastSync = Date.now();
    error = '';
    await chrome.storage.local.set({ policy, lastSync });
    if (changed) await broadcast();
  } catch (failure) {
    if (key !== requestKey) return status();
    error = failure.name === 'TypeError' || failure.name === 'TimeoutError'
      ? 'Windows 앱에 연결할 수 없습니다. 저장된 정책이 있으면 계속 적용합니다.'
      : String(failure.message || failure);
  } finally {
    busy = false;
    if (pendingSync) { pendingSync = false; sync(); }
  }
  return status();
}

async function handle(message, sender) {
  await ready;
  if (message.type === 'getPolicy') return { policy };
  // Pairing/status controls may only be called by this extension's own UI.
  if (sender.url === chrome.runtime.getURL('popup.html')) {
    if (message.type === 'status') return status();
    if (message.type === 'sync') return sync();
    if (message.type === 'pair') {
      const next = String(message.key || '').trim();
      if (!/^[A-Za-z0-9_\-+/=]{16,256}$/.test(next)) throw new Error('앱에 표시된 연결 키를 그대로 입력하세요.');
      if (next !== key) {
        key = next;
        policy = null;
        lastSync = 0;
        await chrome.storage.local.set({ pairingKey: key, policy: null, lastSync: 0 });
        await broadcast();
      }
      return sync();
    }
    if (message.type === 'disconnect') {
      key = ''; policy = null; lastSync = 0; error = '';
      await chrome.storage.local.remove(['pairingKey', 'policy', 'lastSync']);
      await broadcast();
      return status();
    }
  }
  if (message.type === 'blocked' && sender.tab && key && policy) {
    // Host is derived from the actual sending frame; messages never supply a full URL.
    const host = PasteGuard.urlHost(sender.url || '');
    const rule = policy.rules.find(rule => rule.enabled && rule.name === message.rule
      && rule.sites.some(scope => PasteGuard.siteMatches(scope, host)));
    if (!policy.enabled || !rule || !host) return {};
    const now = Date.now();
    attempts = attempts.filter(time => time > now - 10000);
    if (attempts.length >= 20) return {};
    attempts.push(now);
    try {
      await fetch(API + '/events', {
        method: 'POST', headers: { 'Content-Type': 'application/json', 'X-PasteGuard-Key': key },
        body: JSON.stringify({ process: 'browser', host, rule: rule.name, reason: message.reason === 'limit' ? 'limit' : 'pattern' }),
        signal: AbortSignal.timeout(2000)
      });
    } catch (_) { /* Pasting was already blocked synchronously; logging is best effort. */ }
    return {};
  }
  return {};
}

chrome.runtime.onMessage.addListener((message, sender, respond) => {
  handle(message, sender).then(respond, failure => respond({ error: failure.message }));
  return true;
});
chrome.alarms.create('policy-refresh', { periodInMinutes: 0.5 });
chrome.alarms.onAlarm.addListener(alarm => { if (alarm.name === 'policy-refresh') sync(); });
// Sleeping workers are woken by this alarm; the open popup requests a 3s refresh.
sync();

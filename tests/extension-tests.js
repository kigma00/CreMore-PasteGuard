'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const engine = require('../extension/matcher.js');
let checks = 0;
function check(condition, label) { checks++; assert.ok(condition, label); }
function match(text, pattern, expected) { check(engine.matchText(text, pattern) === expected, JSON.stringify({ text: text?.slice(0, 80), pattern, expected })); }
const token = 'PGTEST_ABCD1234EFGH5678';
function policy() {
  return { version: 1, enabled: true, rules: [{ id: 'demo', name: 'Demo token', enabled: true,
    processes: ['Slack.exe', 'Notion.exe'], sites: ['slack.com', '*.slack.com', 'notion.so', '*.notion.so', 'localhost'], pattern: 'PGTEST_[A-Z0-9]{16}' }] };
}
function multiPolicy(patterns = ['FIRST_[0-9]{4}', 'SECOND_[A-Z]{4}', 'THIRD_[a-z]{4}']) {
  const result = policy(); result.version = 2; result.rules[0].patterns = patterns;
  return result;
}

const cases = [
  [token, 'PGTEST_[A-Z0-9]{16}', true], ['PGTEST_abcd1234EFGH5678', 'PGTEST_[A-Z0-9]{16}', false],
  ['PGTEST_ABCD1234EFGH567', 'PGTEST_[A-Z0-9]{16}', false], ['ordinary', 'PGTEST_[A-Z0-9]{16}', false],
  ['prefix abc suffix', 'abc', true], ['ABC', 'abc', false], ['abc', '^abc$', true], ['abc\n', '^abc$', false],
  ['xabc', '^abc', false], ['x\nabc', '^abc', false], ['abc', 'a^bc', false], ['', '^$', true], [null, '^$', true],
  ['', 'a*', true], ['ab', '^a*b$', true], ['aaaab', '^a*b$', true], ['b', '^a+b$', false], ['aaab', '^a+b$', true],
  ['color', '^colou?r$', true], ['colour', '^colou?r$', true], ['colouur', '^colou?r$', false],
  ['aaa', '^a{3}$', true], ['aa', '^a{2,4}$', true], ['aaaa', '^a{2,4}$', true], ['aaaaa', '^a{2,4}$', false],
  ['', 'a{0}', true], ['', '^a{0,0}$', true], ['a'.repeat(256), '^a{256}$', true],
  ['123', '^\\d+$', true], ['١٢٣', '^\\d+$', false], ['a_Z09', '^\\w+$', true], ['한글', '^\\w+$', false],
  [' \t\r\n\v\f', '^\\s+$', true], ['\u00a0', '^\\s$', false], ['\n\r\t\f\v', '^\\n\\r\\t\\f\\v$', true],
  ['a.b+(c)|d[0]{1}\\', '^a\\.b\\+\\(c\\)\\|d\\[0\\]\\{1\\}\\\\$', true],
  ['b7_', '^[a-c\\d_]+$', true], ['xyz', '^[^a-c0-9]+$', true], ['y2z', '^[^a-c0-9]+$', false],
  ['-a', '^[-a]+$', true], ['a-', '^[a-]+$', true], [']', '^[\\]]$', true],
  ['a cat!', '\\bcat\\b', true], ['cats', '\\bcat\\b', false], ['_cat', '\\bcat\\b', false], ['écaté', '\\bcat\\b', true],
  ['', '\\b', false], ['한글 😀', '한글', true], ['😀', '^😀$', true], ['aaab', '^a*aab$', true],
  ['abc', '^a?b?c?$', true], ['', '^a?b?c?$', true]
];
for (const vector of cases) match(...vector);
for (const pattern of [null, '', '.', 'a|b', '(a)', '(?:a)', '(?=a)', '(?<=a)', '\\1', '\\x61', '\\u0061',
  '\\D', '\\W', '\\S', '\\q', '\\', '[', '[]', '[^]', '[z-a]', '[\\d-z]', '[a-\\w]', '[\\b]',
  'a{', 'a{}', 'a{1,}', 'a{,2}', 'a{2,1}', 'a{257}', 'a{999999999999}', 'a**', 'a+?', 'a??', '*a', 'a}', 'a]',
  '^*', '$?', '\\b+', 'a'.repeat(513), 'a{256}b{256}c{256}d{256}e{256}f{256}g{256}h{256}']) {
  checks++; assert.throws(() => engine.compile(pattern), undefined, String(pattern));
}
match('x'.repeat(engine.limits.text), 'PGTEST_[A-Z0-9]{16}', false);
match('x'.repeat(engine.limits.text + 1), 'absent', true);
// Fast literal rejection must charge the same budget and leave no scratch state
// from a preceding larger/different expression.
let exactBudget = { steps: engine.limits.steps - 22 };
check(!engine.evaluate(engine.compile('z'), 'xxxxxxx', exactBudget).matched
  && exactBudget.steps === engine.limits.steps, 'literal skip preserves exact budget boundary');
exactBudget = { steps: engine.limits.steps - 21 };
check(engine.evaluate(engine.compile('z'), 'xxxxxxx', exactBudget).limited, 'literal skip fails closed one step past budget');
match('a'.repeat(256), '^a{256}$', true);
match('x', 'z', false);
match('prefix PGTEST_ABCD1234EFGH5678', 'PGTEST_[A-Z0-9]{16}', true);
match('x'.repeat(128), '^a{256}$', false);
match('', '^$', true);
let began = Date.now();
check(engine.evaluate(engine.compile('a*'.repeat(64) + 'b'), 'a'.repeat(engine.limits.text)).limited, 'NFA step cap fails closed');
check(Date.now() - began < 10000, 'adversarial matching stays bounded');
for (let i = 0; i < 150; i++) match('cache' + i, 'cache' + i, true);
const target = (scope, url) => engine.siteMatches(scope, engine.urlHost(url));
for (const url of ['https://slack.com/path?q=1', 'https://SLACK.COM:8443/path', 'https://slack.com./'])
  check(target('slack.com', url), 'exact/canonical host ' + url);
for (const url of ['https://evilslack.com', 'https://slack.com.evil.example', 'https://slack.com@evil.example/',
  'https://evil.example/?next=https://slack.com', 'file://slack.com/a', 'slack.com', null])
  check(!target('slack.com', url), 'scope escape rejected ' + url);
check(!target('*.slack.com', 'https://slack.com'), 'wildcard excludes apex');
check(target('*.slack.com', 'https://a.b.slack.com'), 'wildcard includes nested host');
check(target('localhost', 'http://localhost:17844'), 'demo localhost scope');
for (const scope of ['https://slack.com', 'slack.com/path', 'slack.com:443', '*slack.com', 'foo.*.com', 'slack.com.', '-slack.com',
  'slack-.com', 'slack..com', 'slack', '슬랙.com', '', null]) check(!engine.validSite(scope), 'invalid scope ' + scope);
for (const exe of ['*.exe', 'Slack', 'C:\\Slack.exe', ' Slack.exe', 'Slack.exe ', '.exe', 'a?b.exe', 'a\nb.exe'])
  check(!engine.validProcess(exe), 'invalid process ' + exe);
check(engine.validProcess('slack.EXE'), 'case insensitive process suffix');
const decision = (data, url, text) => engine.decidePaste(engine.preparePolicy(data), url, text).block;
check(decision(policy(), 'https://slack.com/private/path', token), 'target and pattern block');
check(!decision(policy(), 'https://example.com', token), 'outside target allowed');
check(!decision(policy(), 'https://slack.com', 'ordinary text'), 'benign target allowed');
check(!decision(policy(), 'https://example.com', 'x'.repeat(engine.limits.text + 1)), 'large outside target allowed');
check(decision(policy(), 'https://slack.com', 'x'.repeat(engine.limits.text + 1)), 'large target fails closed');
const manyRules = policy();
manyRules.rules = Array.from({ length: 100 }, (_, i) => ({ ...policy().rules[0], id: 'rule-' + i, name: 'Rule ' + i, pattern: 'z' }));
began = Date.now();
const aggregate = engine.decidePaste(engine.preparePolicy(manyRules), 'https://slack.com', 'x'.repeat(engine.limits.text));
check(aggregate.block && aggregate.reason === 'limit', '100 rules share a single 8M operation budget');
check(Date.now() - began < 10000, 'aggregate policy evaluation stays bounded');
let disabled = policy(); disabled.enabled = false;
check(!decision(disabled, 'https://slack.com', token), 'disabled global policy allowed');
disabled = policy(); disabled.rules[0].enabled = false;
check(!decision(disabled, 'https://slack.com', token), 'disabled rule allowed');
for (const alter of [p => p.version = 3, p => p.rules.push(p.rules[0]), p => p.rules[0].pattern = '(a+)+',
  p => p.rules[0].sites = ['slack.com/'], p => { p.rules[0].sites = []; p.rules[0].processes = []; }]) {
  const p = policy(); alter(p); checks++; assert.throws(() => engine.preparePolicy(p), undefined, 'invalid policy rejected atomically');
}

const orPolicy = multiPolicy();
for (const sample of ['FIRST_1234', 'SECOND_ABCD', 'THIRD_abcd'])
  check(decision(orPolicy, 'https://slack.com', sample), 'OR matches each pattern independently: ' + sample);
check(!decision(orPolicy, 'https://slack.com', 'ordinary text'), 'OR allows text matching no pattern');
check(!decision(orPolicy, 'https://slack.com', token), 'v2 patterns override the legacy field');
check(!decision(orPolicy, 'https://example.com', 'SECOND_ABCD'), 'OR does not change site scope');
const onlyPatterns = multiPolicy(); delete onlyPatterns.rules[0].pattern;
check(decision(onlyPatterns, 'https://slack.com', 'THIRD_abcd'), 'v2 works without a legacy pattern field');
const ignoredLegacy = multiPolicy(); ignoredLegacy.rules[0].pattern = '(unsafe)';
check(decision(ignoredLegacy, 'https://slack.com', 'SECOND_ABCD'), 'only effective patterns are validated');
for (const version of [1, 2]) {
  for (const withNull of [false, true]) {
    const fallback = policy(); fallback.version = version;
    if (withNull) fallback.rules[0].patterns = null;
    check(decision(fallback, 'https://slack.com', token), 'legacy fallback remains valid for version ' + version);
  }
}
const sixteen = multiPolicy(Array.from({ length: 16 }, (_, i) => 'TOKEN' + i));
check(decision(sixteen, 'https://slack.com', 'TOKEN15'), '16 patterns accepted');
for (const patterns of [[], 'SECOND_[A-Z]{4}', {}, ['safe', null], ['safe', ''], ['safe', 123], ['safe', {}],
  ['safe', '(unsafe)'], ['safe', ['nested']], new Array(2), Array(17).fill('safe')]) {
  const data = multiPolicy(patterns); checks++;
  assert.throws(() => engine.preparePolicy(data), undefined, 'malformed or excessive pattern list rejected');
}
for (const patterns of [[], ['safe'], ['safe', 'other']]) {
  const data = multiPolicy(patterns); data.version = 1; checks++;
  assert.throws(() => engine.preparePolicy(data), undefined, 'v1 cannot carry the v2 field');
}
const disabledInvalid = multiPolicy(['safe', '(invalid)']); disabledInvalid.rules[0].enabled = false;
checks++; assert.throws(() => engine.preparePolicy(disabledInvalid), undefined, 'disabled rule still validates all patterns');
const hundredPatterns = multiPolicy();
hundredPatterns.rules = Array.from({ length: 7 }, (_, i) => ({ ...policy().rules[0], id: 'multi-' + i,
  patterns: Array(i === 6 ? 4 : 16).fill('z') }));
check(engine.preparePolicy(hundredPatterns).rules.length === 7, '100 total patterns accepted');
hundredPatterns.rules[6].patterns.push('z');
checks++; assert.throws(() => engine.preparePolicy(hundredPatterns), undefined, '101 total patterns rejected');
hundredPatterns.rules.forEach(rule => { rule.enabled = false; rule.sites = []; });
checks++; assert.throws(() => engine.preparePolicy(hundredPatterns), undefined, 'disabled native-only rules count toward total');
const boundedOr = multiPolicy(Array(16).fill('^a*b'));
const largeText = 'a'.repeat(engine.limits.text);
const singleInspection = engine.evaluate(engine.compile('^a*b'), largeText);
check(!singleInspection.matched && !singleInspection.limited, 'individual nonmatching pattern remains below budget');
began = Date.now();
const boundedResult = engine.decidePaste(engine.preparePolicy(boundedOr), 'https://slack.com', largeText);
check(boundedResult.block && boundedResult.reason === 'limit', 'patterns share one 8M budget within their rule');
check(Date.now() - began < 10000, 'multi-pattern worst case remains bounded');
check(!decision(boundedOr, 'https://example.com', largeText), 'untargeted site skips multi-pattern evaluation');
const disabledOr = multiPolicy(); disabledOr.rules[0].enabled = false;
check(!decision(disabledOr, 'https://slack.com', 'SECOND_ABCD'), 'disabled OR rule remains allowed');
const pausedOr = multiPolicy(); pausedOr.enabled = false;
check(!decision(pausedOr, 'https://slack.com', 'SECOND_ABCD'), 'disabled OR policy remains allowed');

const tick = async () => { for (let i = 0; i < 12; i++) await Promise.resolve(); };
function contentFixture(url = 'https://slack.com/private?secret=example') {
  let listener, messageListener, resolvePolicy;
  const outgoing = [];
  const element = () => ({ style: {}, setAttribute() {}, appendChild() {}, remove() {}, attachShadow: element });
  const context = vm.createContext({ PasteGuard: engine, location: { href: url },
    window: { addEventListener(type, handler, capture) { check(type === 'paste' && capture, 'capture listener installed'); listener = handler; } },
    document: { documentElement: element(), getElementById: () => null, createElement: element }, setTimeout: () => 0,
    chrome: { runtime: {
      onMessage: { addListener(handler) { messageListener = handler; } },
      sendMessage(message) {
        outgoing.push(message);
        return message.type === 'getPolicy' ? new Promise(resolve => { resolvePolicy = resolve; }) : Promise.resolve({});
      }
    } }
  });
  vm.runInContext(fs.readFileSync(path.join(__dirname, '../extension/content.js'), 'utf8'), context);
  return {
    outgoing,
    init(data) { resolvePolicy({ policy: data }); },
    update(data) { messageListener({ type: 'policy', policy: data }, {}, () => {}); },
    paste(text, targetType = 'TEXTAREA') {
      const event = { prevented: false, stopped: false, target: { nodeName: targetType, isContentEditable: targetType === 'DIV' },
        clipboardData: { getData(type) { check(type === 'text/plain', 'reads plain clipboard text only'); return text; } },
        preventDefault() { this.prevented = true; }, stopImmediatePropagation() { this.stopped = true; } };
      listener(event); return event;
    }
  };
}

async function runAsyncTests() {
  const page = contentFixture();
  check(!page.paste(token).prevented, 'initialization gap is not claimed as protection');
  page.init(policy()); await tick();
  for (const kind of ['INPUT', 'TEXTAREA', 'DIV']) {
    const event = page.paste(token, kind);
    check(event.prevented && event.stopped, 'standard paste canceled before page handlers in ' + kind);
  }
  check(!page.paste('ordinary text').prevented, 'content script benign paste allowed');
  check(page.outgoing.filter(m => m.type === 'blocked').every(m => Object.keys(m).sort().join() === 'reason,rule,type'
    && !JSON.stringify(m).includes(token) && !JSON.stringify(m).includes('/private')), 'content logs exclude clipboard and URL');
  const broken = policy(); broken.rules[0].pattern = '(unsafe)'; page.update(broken);
  check(page.paste(token).prevented, 'invalid update retains last valid policy');
  page.update(multiPolicy(['PGTEST_[A-Z0-9]{16}', '^PGTEST_']));
  const beforeOrPaste = page.outgoing.filter(message => message.type === 'blocked').length;
  check(page.paste(token).prevented, 'v2 content policy blocks overlapping patterns');
  check(page.outgoing.filter(message => message.type === 'blocked').length === beforeOrPaste + 1,
    'multiple matching patterns emit exactly one rule event per paste');
  page.update(multiPolicy(['FIRST_[0-9]{4}', '(invalid)']));
  check(page.paste(token).prevented, 'malformed multi-pattern update preserves prior verified policy');
  page.update(multiPolicy());
  check(page.paste('THIRD_abcd').prevented, 'content script evaluates a later OR pattern');
  check(!page.paste('ordinary text').prevented, 'content script allows nonmatching OR text');
  const paused = policy(); paused.enabled = false; page.update(paused);
  check(!page.paste(token).prevented, 'policy pause propagates to content');
  page.update(null); check(!page.paste(token).prevented, 'disconnect removes policy');
  const child = contentFixture('https://example.com/frame'); child.init(policy()); await tick();
  check(!child.paste(token).prevented, 'frame uses its own URL scope');
  child.update(multiPolicy()); check(!child.paste('SECOND_ABCD').prevented, 'OR frame scope remains independent');

  let bridgePolicy = policy(), bridgeError = false, receiver;
  const requests = [], stored = { pairingKey: 'a'.repeat(32), policy: policy(), lastSync: 1 }, broadcasts = [];
  let accessLevel, storageWrites = 0, validations = 0, lastWriteKeys = [], now = Date.now();
  class Clock extends Date { static now() { return now; } }
  const background = vm.createContext({ URL, AbortSignal, Date: Clock, Promise, console,
    importScripts() { background.PasteGuard = { ...engine, preparePolicy(data) { validations++; return engine.preparePolicy(data); } }; },
    fetch: async (url, options) => {
      requests.push({ url, options });
      if (bridgeError) throw new TypeError('offline');
      return { ok: true, json: async () => JSON.parse(JSON.stringify(bridgePolicy)) };
    },
    chrome: {
      storage: { local: {
        async setAccessLevel(options) { accessLevel = options.accessLevel; },
        async get() { return { ...stored }; },
        async set(values) { storageWrites++; lastWriteKeys = Object.keys(values); Object.assign(stored, values); },
        async remove(keys) { for (const key of keys) delete stored[key]; }
      } },
      tabs: { async query() { return [{ id: 1 }]; }, async sendMessage(id, message) { broadcasts.push(message); } },
      runtime: { getURL: path => 'chrome-extension://unit-test/' + path, onMessage: { addListener(fn) { receiver = fn; } } },
      alarms: { create(name, options) { check(options.periodInMinutes === 0.5, 'idle refresh interval 30s'); }, onAlarm: { addListener() {} } }
    }
  });
  vm.runInContext(fs.readFileSync(path.join(__dirname, '../extension/background.js'), 'utf8'), background);
  await tick();
  const call = (message, sender = { url: 'chrome-extension://unit-test/popup.html' }) => new Promise(resolve => receiver(message, sender, resolve));
  check(accessLevel === 'TRUSTED_CONTEXTS', 'pairing key hidden from content scripts');
  const state = await call({ type: 'status' });
  check(state.paired && state.policy.enabled && state.lastSync > 1, 'background loads and syncs policy');
  check(!JSON.stringify(state).includes('a'.repeat(32)), 'UI status excludes pairing secret');
  check(requests[0].url === 'http://127.0.0.1:17843/policy' && requests[0].options.headers['X-PasteGuard-Key'] === 'a'.repeat(32), 'authenticated loopback-only policy fetch');
  const initialWrites = storageWrites, initialValidations = validations, initialBroadcasts = broadcasts.length;
  await call({ type: 'sync' });
  check(storageWrites === initialWrites && validations === initialValidations && broadcasts.length === initialBroadcasts,
    'unchanged popup refresh avoids validation, disk writes and broadcast');
  now += 30001; await call({ type: 'sync' });
  check(storageWrites === initialWrites + 1 && lastWriteKeys.join() === 'lastSync'
    && validations === initialValidations && broadcasts.length === initialBroadcasts,
    'unchanged 30s refresh persists timestamp only without rewriting policy');
  bridgePolicy = policy(); bridgePolicy.enabled = false;
  await call({ type: 'sync' });
  check(storageWrites > initialWrites && validations > initialValidations && broadcasts.at(-1).policy.enabled === false,
    'changed policy is validated, stored and broadcast immediately');
  bridgePolicy = policy(); await call({ type: 'sync' });
  const sender = { tab: { id: 1 }, url: 'https://app.slack.com/private?token=' + token };
  await call({ type: 'blocked', rule: 'Demo token', text: token, url: sender.url }, sender);
  const eventRequest = requests.find(r => r.url.endsWith('/events'));
  check(!!eventRequest, 'block metadata sent to local app');
  const eventData = JSON.parse(eventRequest.options.body);
  checks++; assert.deepEqual(eventData, { process: 'browser', host: 'app.slack.com', rule: 'Demo token', reason: 'pattern' });
  check(!eventRequest.options.body.includes(token), 'clipboard text is never serialized');
  await call({ type: 'blocked', rule: 'Demo token', reason: 'limit' }, sender);
  check(JSON.parse(requests.at(-1).options.body).reason === 'limit', 'limit block is logged accurately');
  let count = requests.length;
  await call({ type: 'blocked', rule: 'Demo token' }, { tab: { id: 1 }, url: 'https://example.com' });
  check(requests.length === count, 'out-of-scope metadata ignored');
  await call({ type: 'pair', key: 'b'.repeat(32) }, sender);
  check(stored.pairingKey === 'a'.repeat(32), 'content cannot modify pairing');
  bridgeError = true;
  const offline = await call({ type: 'sync' });
  check(offline.policy.enabled && !!offline.error, 'offline preserves last known policy and reports status');
  bridgeError = false; bridgePolicy = policy(); bridgePolicy.rules[0].pattern = '(bad)';
  const invalid = await call({ type: 'sync' });
  check(invalid.policy.rules[0].pattern === 'PGTEST_[A-Z0-9]{16}' && !!invalid.error, 'invalid bridge policy preserves verified cache');
  bridgePolicy = multiPolicy();
  const upgraded = await call({ type: 'sync' });
  check(upgraded.policy.version === 2 && stored.policy.rules[0].patterns.length === 3 && !upgraded.error,
    'cached legacy policy upgrades to validated v2 OR policy');
  check(broadcasts.at(-1).policy.rules[0].patterns[2] === 'THIRD_[a-z]{4}', 'all new patterns broadcast to existing frames');
  bridgeError = true;
  const offlineOr = await call({ type: 'sync' });
  check(offlineOr.policy.version === 2 && offlineOr.policy.rules[0].patterns.length === 3 && !!offlineOr.error,
    'v2 cached policy stays available while native app is offline');
  bridgeError = false; bridgePolicy = multiPolicy(['safe', '']);
  const malformedOr = await call({ type: 'sync' });
  check(malformedOr.policy.rules[0].patterns.length === 3 && !!malformedOr.error,
    'malformed multi-pattern bridge response cannot replace verified cache');
  bridgePolicy = multiPolicy(); bridgePolicy.version = 1;
  const unsafeVersion = await call({ type: 'sync' });
  check(unsafeVersion.policy.version === 2 && !!unsafeVersion.error, 'v1 with v2 fields is rejected without partial application');
  bridgePolicy = policy();
  const legacyAgain = await call({ type: 'sync' });
  check(legacyAgain.policy.version === 1 && !legacyAgain.error, 'legacy v1 bridge still accepted after v2 use');
  stored.policy = multiPolicy(); bridgeError = true;
  const restarted = vm.createContext({ URL, AbortSignal, Date: Clock, Promise, console,
    fetch: background.fetch, chrome: background.chrome, importScripts() { restarted.PasteGuard = engine; }
  });
  vm.runInContext(fs.readFileSync(path.join(__dirname, '../extension/background.js'), 'utf8'), restarted);
  await tick();
  const restored = await call({ type: 'getPolicy' }, sender);
  check(restored.policy.version === 2 && restored.policy.rules[0].patterns.length === 3,
    'service worker restart loads cached v2 policy while app is offline');
  await call({ type: 'disconnect' }, { tab: { id: 2 }, url: 'chrome-extension://unit-test/popup.html' });
  check(!stored.pairingKey && !stored.policy && broadcasts.at(-1).policy === null, 'options tab disconnect removes policy and secret');
  let popupTimer, visibilityChanged;
  const popupMessages = [], controls = new Map();
  const popupDocument = { hidden: true,
    getElementById(id) {
      if (!controls.has(id)) controls.set(id, { addEventListener() {}, replaceChildren(...children) { this.children = children; }, value: '' });
      return controls.get(id);
    },
    createElement() { return { append(...children) { this.children = children; } }; },
    addEventListener(type, handler) { if (type === 'visibilitychange') visibilityChanged = handler; }
  };
  const popup = vm.createContext({ document: popupDocument, Date, setInterval(handler) { popupTimer = handler; },
    chrome: { runtime: { async sendMessage(message) { popupMessages.push(message); return { paired: false, policy: null }; } },
      tabs: { async query() { return []; } } }
  });
  vm.runInContext(fs.readFileSync(path.join(__dirname, '../extension/popup.js'), 'utf8'), popup);
  await tick(); popupTimer(); await tick();
  check(!popupMessages.some(message => message.type === 'sync'), 'hidden options tab avoids 3s polling');
  popupDocument.hidden = false; visibilityChanged(); await tick();
  check(popupMessages.filter(message => message.type === 'sync').length === 1, 'returning to options refreshes immediately');
  popupTimer(); await tick();
  check(popupMessages.filter(message => message.type === 'sync').length === 2, 'visible popup retains fast policy refresh');
  const renderPopup = vm.runInContext('render', popup);
  renderPopup({ paired: true, policy: multiPolicy(), lastSync: Date.now() });
  check(controls.get('rules').children[0].children[0].textContent === 'Demo token (패턴 3개)', 'popup shows effective OR pattern count');
  renderPopup({ paired: true, policy: policy(), lastSync: Date.now() });
  check(controls.get('rules').children[0].children[0].textContent === 'Demo token (패턴 1개)', 'popup shows legacy single-pattern count');
  console.log('PASS: ' + checks + ' extension matcher, scope, paste-event and privacy checks.');
}
runAsyncTests().catch(error => { console.error(error); process.exitCode = 1; });

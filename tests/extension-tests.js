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
for (const alter of [p => p.version = 2, p => p.rules.push(p.rules[0]), p => p.rules[0].pattern = '(a+)+',
  p => p.rules[0].sites = ['slack.com/'], p => { p.rules[0].sites = []; p.rules[0].processes = []; }]) {
  const p = policy(); alter(p); checks++; assert.throws(() => engine.preparePolicy(p), undefined, 'invalid policy rejected atomically');
}

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
  const paused = policy(); paused.enabled = false; page.update(paused);
  check(!page.paste(token).prevented, 'policy pause propagates to content');
  page.update(null); check(!page.paste(token).prevented, 'disconnect removes policy');
  const child = contentFixture('https://example.com/frame'); child.init(policy()); await tick();
  check(!child.paste(token).prevented, 'frame uses its own URL scope');

  let bridgePolicy = policy(), bridgeError = false, receiver;
  const requests = [], stored = { pairingKey: 'a'.repeat(32), policy: policy(), lastSync: 1 }, broadcasts = [];
  let accessLevel;
  const background = vm.createContext({ URL, AbortSignal, Date, Promise, console,
    importScripts() { background.PasteGuard = engine; },
    fetch: async (url, options) => {
      requests.push({ url, options });
      if (bridgeError) throw new TypeError('offline');
      return { ok: true, json: async () => JSON.parse(JSON.stringify(bridgePolicy)) };
    },
    chrome: {
      storage: { local: {
        async setAccessLevel(options) { accessLevel = options.accessLevel; },
        async get() { return { ...stored }; },
        async set(values) { Object.assign(stored, values); },
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
  await call({ type: 'disconnect' }, { tab: { id: 2 }, url: 'chrome-extension://unit-test/popup.html' });
  check(!stored.pairingKey && !stored.policy && broadcasts.at(-1).policy === null, 'options tab disconnect removes policy and secret');
  console.log('PASS: ' + checks + ' extension matcher, scope, paste-event and privacy checks.');
}
runAsyncTests().catch(error => { console.error(error); process.exitCode = 1; });

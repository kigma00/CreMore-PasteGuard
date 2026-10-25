// Restricted, bounded UTF-16 NFA. Keep semantics aligned with Core.cs.
(function (root) {
  'use strict';
  const limits = Object.freeze({ pattern: 512, repeat: 256, states: 2048, text: 65536, steps: 8000000 });
  const cache = new Map();
  // Matching is synchronous. Reuse bounded scratch space; it never contains pasted text.
  let scratchA = new Uint8Array(0), scratchB = new Uint8Array(0);
  const letter = c => c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z';
  const digit = c => c >= '0' && c <= '9';
  const wordAt = (text, i) => i >= 0 && i < text.length && (letter(text[i]) || digit(text[i]) || text[i] === '_');
  const bad = message => { throw new Error(message); };
  const literal = c => ({ negate: false, ranges: [c.charCodeAt(0), c.charCodeAt(0)] });

  function compile(pattern) {
    if (typeof pattern !== 'string' || !pattern.length || pattern.length > limits.pattern)
      bad('패턴은 1~512자여야 합니다.');
    if (cache.has(pattern)) return cache.get(pattern);
    const nodes = [];
    let at = 0;
    function escape() {
      if (at >= pattern.length) bad('불완전한 이스케이프입니다.');
      const c = pattern[at++];
      const classes = { d: [48,57], w: [65,90,97,122,48,57,95,95], s: [9,13,32,32],
        n: [10,10], r: [13,13], t: [9,9], f: [12,12], v: [11,11] };
      if (classes[c]) return { negate: false, ranges: classes[c] };
      if (letter(c) || digit(c)) bad('지원하지 않는 이스케이프입니다.');
      return literal(c);
    }
    function classPart() {
      const c = pattern[at++];
      return c === '\\' ? escape() : literal(c);
    }
    function characterClass() {
      const result = { negate: false, ranges: [] };
      if (pattern[at] === '^') { result.negate = true; at++; }
      let count = 0;
      while (at < pattern.length && pattern[at] !== ']') {
        const first = classPart();
        if (at + 1 < pattern.length && pattern[at] === '-' && pattern[at + 1] !== ']') {
          at++;
          const last = classPart();
          if (first.ranges.length !== 2 || first.ranges[0] !== first.ranges[1]
            || last.ranges.length !== 2 || last.ranges[0] !== last.ranges[1]
            || first.ranges[0] > last.ranges[0]) bad('문자 범위는 오름차순 단일 문자여야 합니다.');
          result.ranges.push(first.ranges[0], last.ranges[0]);
        } else result.ranges.push(...first.ranges);
        count++;
      }
      if (at >= pattern.length || !count) bad('문자 집합은 비어 있지 않아야 하며 닫혀 있어야 합니다.');
      at++;
      return result;
    }
    function number() {
      const start = at;
      let value = 0;
      while (at < pattern.length && digit(pattern[at])) {
        value = value * 10 + Number(pattern[at++]);
        if (value > limits.repeat) bad('반복 횟수는 256을 넘을 수 없습니다.');
      }
      if (at === start) bad('반복 횟수가 필요합니다.');
      return value;
    }
    while (at < pattern.length) {
      const c = pattern[at++];
      let kind = 'atom';
      let atom = null;
      if (c === '^') kind = 'begin';
      else if (c === '$') kind = 'end';
      else if (c === '\\' && pattern[at] === 'b') { at++; kind = 'boundary'; }
      else if (c === '[') atom = characterClass();
      else if (c === '\\') atom = escape();
      else {
        if ('.()|*+?{}]'.includes(c)) bad('지원하지 않거나 위치가 잘못된 연산자입니다.');
        atom = literal(c);
      }
      let minimum = 1, maximum = 1, unbounded = false;
      if (at < pattern.length && '?*+{'.includes(pattern[at])) {
        if (kind !== 'atom') bad('위치 조건은 반복할 수 없습니다.');
        const quantifier = pattern[at++];
        if (quantifier === '?') { minimum = 0; maximum = 1; }
        else if (quantifier === '*') { minimum = 0; unbounded = true; }
        else if (quantifier === '+') { minimum = 1; unbounded = true; }
        else {
          minimum = number(); maximum = minimum;
          if (pattern[at] === ',') { at++; maximum = number(); }
          if (at >= pattern.length || pattern[at++] !== '}' || maximum < minimum)
            bad('{n} 또는 {n,m} 형식을 사용하세요.');
        }
      }
      if (kind !== 'atom') nodes.push({ kind, atom: null });
      else {
        for (let i = 0; i < minimum; i++) nodes.push({ kind: 'atom', atom });
        if (unbounded) nodes.push({ kind: 'star', atom });
        else for (let i = minimum; i < maximum; i++) nodes.push({ kind: 'optional', atom });
      }
      if (nodes.length + 1 > limits.states) bad('패턴의 확장 상태 수가 2048을 초과합니다.');
    }
    if (cache.size >= 128) cache.delete(cache.keys().next().value);
    cache.set(pattern, nodes);
    return nodes;
  }

  function evaluate(nodes, text, budget) {
    text = text == null ? '' : String(text);
    if (text.length > limits.text) return { matched: true, limited: true };
    const size = nodes.length + 1;
    if (scratchA.length < size) {
      scratchA = new Uint8Array(size); scratchB = new Uint8Array(size);
    } else {
      scratchA.fill(0, 0, size); scratchB.fill(0, 0, size);
    }
    let current = scratchA, next = scratchB;
    const counter = budget || { steps: 0 };
    let high = 0;
    const first = nodes[0];
    const firstLiteral = first?.kind === 'atom' && !first.atom.negate
      && first.atom.ranges.length === 2 && first.atom.ranges[0] === first.atom.ranges[1]
      ? String.fromCharCode(first.atom.ranges[0]) : null;
    const exceeded = () => ({ matched: true, limited: true });
    for (let position = 0; position <= text.length; position++) {
      // With no active partial match, missing starting characters cannot match.
      // Charge the same three NFA steps per skipped UTF-16 character so the
      // shared fail-closed inspection budget stays identical to the slow path.
      if (high === 0 && firstLiteral !== null && position < text.length) {
        const found = text.indexOf(firstLiteral, position);
        const destination = found < 0 ? text.length : found;
        const cost = (destination - position) * 3;
        if (cost > limits.steps - counter.steps) { counter.steps = limits.steps + 1; return exceeded(); }
        counter.steps += cost;
        position = destination;
      }
      current[0] = 1;
      for (let i = 0; i <= high && i < nodes.length; i++) {
        if (++counter.steps > limits.steps) return exceeded();
        if (!current[i]) continue;
        const node = nodes[i];
        if (node.kind === 'optional' || node.kind === 'star'
          || node.kind === 'begin' && position === 0
          || node.kind === 'end' && position === text.length
          || node.kind === 'boundary' && wordAt(text, position - 1) !== wordAt(text, position)) {
          current[i + 1] = 1; high = Math.max(high, i + 1);
        }
      }
      if (current[nodes.length]) return { matched: true, limited: false };
      if (position === text.length) return { matched: false, limited: false };
      next.fill(0, 0, size);
      let nextHigh = 0;
      for (let i = 0; i <= high && i < nodes.length; i++) {
        if (++counter.steps > limits.steps) return exceeded();
        const node = nodes[i];
        if (current[i] && node.atom) {
          let found = false;
          const code = text.charCodeAt(position);
          for (let r = 0; r < node.atom.ranges.length; r += 2) {
            if (++counter.steps > limits.steps) return exceeded();
            if (code >= node.atom.ranges[r] && code <= node.atom.ranges[r + 1]) { found = true; break; }
          }
          if (node.atom.negate ? !found : found) {
            const destination = node.kind === 'star' ? i : i + 1;
            next[destination] = 1;
            nextHigh = Math.max(nextHigh, destination);
          }
        }
      }
      const swap = current; current = next; next = swap; high = nextHigh;
    }
    return { matched: false, limited: false };
  }

  function validSite(value) {
    if (typeof value !== 'string' || !value.length) return false;
    const host = value.startsWith('*.') ? value.slice(2) : value;
    if (host.length > 253 || !host.includes('.') && host.toLowerCase() !== 'localhost') return false;
    return host.split('.').every(label => label.length > 0 && label.length <= 63
      && !label.startsWith('-') && !label.endsWith('-')
      && [...label].every(c => letter(c) || digit(c) || c === '-'));
  }
  function validProcess(value) {
    return typeof value === 'string' && value.length > 4 && value.length <= 260 && value === value.trim()
      && value.toLowerCase().endsWith('.exe') && [...value].every(c => !'<>:"/\\|?*'.includes(c)
        && !(c.charCodeAt(0) <= 31 || c.charCodeAt(0) >= 127 && c.charCodeAt(0) <= 159));
  }
  function urlHost(url) {
    try {
      const parsed = new URL(url);
      if (parsed.protocol !== 'http:' && parsed.protocol !== 'https:') return '';
      const host = parsed.hostname.toLowerCase();
      return host.endsWith('.') ? host.slice(0, -1) : host;
    } catch (_) { return ''; }
  }
  function siteMatches(scope, host) {
    if (!host || !validSite(scope)) return false;
    return matchesHost(scope.toLowerCase(), host);
  }
  function matchesHost(target, host) {
    return target.startsWith('*.') ? host.length > target.length - 1 && host.endsWith(target.slice(1)) : host === target;
  }
  function preparePolicy(policy) {
    if (!policy || policy.version !== 1 || typeof policy.enabled !== 'boolean'
      || !Array.isArray(policy.rules) || policy.rules.length > 100) bad('지원하지 않는 정책 형식입니다.');
    const ids = new Set();
    const rules = policy.rules.map(rule => {
      if (!rule || typeof rule.id !== 'string' || !rule.id.trim() || rule.id.length > 64 || ids.has(rule.id)
        || typeof rule.name !== 'string' || !rule.name.trim() || rule.name.length > 80
        || typeof rule.enabled !== 'boolean' || !Array.isArray(rule.processes) || !Array.isArray(rule.sites)
        || rule.processes.length > 128 || rule.sites.length > 128 || !rule.processes.length && !rule.sites.length
        || !rule.processes.every(validProcess) || !rule.sites.every(validSite)) bad('규칙의 이름 또는 대상 형식이 잘못되었습니다.');
      ids.add(rule.id);
      return { ...rule, nodes: compile(rule.pattern), targets: rule.sites.map(scope => scope.toLowerCase()) };
    });
    return { enabled: policy.enabled, rules };
  }
  function decidePaste(prepared, url, text) {
    if (!prepared || !prepared.enabled) return { block: false };
    const host = urlHost(url);
    if (!host) return { block: false };
    const budget = { steps: 0 };
    for (const rule of prepared.rules) {
      if (!rule.enabled || !rule.targets.some(target => matchesHost(target, host))) continue;
      const result = evaluate(rule.nodes, text, budget);
      if (result.matched) return { block: true, rule, reason: result.limited ? 'limit' : 'pattern' };
    }
    return { block: false };
  }
  const api = { limits, compile, evaluate, matchText: (text, pattern) => evaluate(compile(pattern), text).matched,
    validSite, validProcess, urlHost, siteMatches, preparePolicy, decidePaste };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  root.PasteGuard = api;
})(globalThis);

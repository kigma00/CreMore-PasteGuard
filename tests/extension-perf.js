'use strict';
// Synthetic text only. Run with Node; timings are observations, not pass/fail thresholds.
const { performance } = require('node:perf_hooks');
const engine = require('../extension/matcher.js');
const cases = [
  ['short benign', 'PGTEST_[A-Z0-9]{16}', 'ordinary message', 20000],
  ['64 KiB benign', 'PGTEST_[A-Z0-9]{16}', 'x'.repeat(65536), 200],
  ['match at end', 'PGTEST_[A-Z0-9]{16}', 'x'.repeat(65000) + 'PGTEST_ABCD1234EFGH5678', 200],
  ['active-state fallback', '^a*aab$', 'a'.repeat(4096) + 'b', 100],
];
for (const [name, pattern, text, repetitions] of cases) {
  const nodes = engine.compile(pattern);
  for (let i = 0; i < 100; i++) engine.evaluate(nodes, text);
  const samples = [];
  for (let run = 0; run < 5; run++) {
    const start = performance.now();
    for (let i = 0; i < repetitions; i++) engine.evaluate(nodes, text);
    samples.push((performance.now() - start) / repetitions);
  }
  samples.sort((a, b) => a - b);
  console.log(name + ': ' + samples[2].toFixed(4) + ' ms/paste (median of 5 runs)');
}

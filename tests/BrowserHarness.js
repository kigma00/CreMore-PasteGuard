// Uses an isolated browser profile and synthetic paste events, never the OS clipboard.
const assert = require('node:assert/strict');
const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const root = path.resolve(__dirname, '..');
const profile = path.join(__dirname, '.browser-profile');
const policy = { version: 1, enabled: true, rules: [{ id: 'test', name: 'Dummy pattern', enabled: true,
  processes: [], sites: ['127.0.0.1'], pattern: 'PGTEST_[A-Z0-9]{16}' }] };
const fixture = http.createServer((_, res) => res.end('<!doctype html><title>PasteGuard browser test</title><textarea id="box"></textarea><div contenteditable="true" id="editor"></div>'));
let browser;
(async () => {
  await new Promise(resolve => fixture.listen(17844, '127.0.0.1', resolve));
  const extension = path.join(root, 'extension');
  browser = await chromium.launchPersistentContext(profile, {
    executablePath: process.env.TEST_BROWSER || 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
    headless: true, args: ['--disable-extensions-except=' + extension, '--load-extension=' + extension],
    viewport: { width: 1100, height: 760 }
  });
  let worker = browser.serviceWorkers()[0];
  if (!worker) worker = await browser.waitForEvent('serviceworker', { timeout: 10000 });
  assert.match(worker.url(), /^chrome-extension:\/\//);
  await worker.evaluate(async p => { await chrome.storage.local.set({ policy: p, lastSync: Date.now() }); }, policy);
  // The background worker loaded before injection; publish the same cached test policy.
  const id = new URL(worker.url()).host;
  const page = await browser.newPage();
  await page.goto('http://127.0.0.1:17844');
  await worker.evaluate(async p => { policy = p; await broadcast(); }, policy);
  await page.waitForTimeout(300);
  const result = await page.evaluate(() => {
    function paste(id, text) {
      const event = new ClipboardEvent('paste', { bubbles: true, cancelable: true, clipboardData: new DataTransfer() });
      event.clipboardData.setData('text/plain', text);
      document.getElementById(id).dispatchEvent(event);
      return event.defaultPrevented;
    }
    return { sensitive: paste('box', 'PGTEST_ABCDEFGHIJKLMNOP'), benign: paste('box', 'ordinary note'), rich: paste('editor', 'PGTEST_ABCDEFGHIJKLMNOP') };
  });
  assert.deepEqual(result, { sensitive: true, benign: false, rich: true });
  const other = await browser.newPage();
  await other.goto('http://localhost:17844');
  await other.waitForTimeout(300);
  assert.equal(await other.evaluate(() => {
    const event = new ClipboardEvent('paste', { bubbles: true, cancelable: true, clipboardData: new DataTransfer() });
    event.clipboardData.setData('text/plain', 'PGTEST_ABCDEFGHIJKLMNOP');
    document.querySelector('textarea').dispatchEvent(event); return event.defaultPrevented;
  }), false);
  const popup = await browser.newPage();
  await popup.goto('chrome-extension://' + id + '/popup.html');
  assert.match(await popup.textContent('body'), /PasteGuard/);
  await popup.screenshot({ path: path.join(__dirname, 'browser-popup.png') });
  console.log('PASS: real extension loaded; scoped textarea/contenteditable synthetic paste blocked; benign/outside domain allowed; popup rendered.');
})().catch(error => { console.error('FAIL:', error.message); process.exitCode = 1; }).finally(async () => {
  if (browser) await browser.close();
  fixture.close();
});

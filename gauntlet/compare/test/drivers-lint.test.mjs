// Static checks on every driver file (both products, shared helpers included). The runtime guard
// (lib/guard.mjs, test/guard.test.mjs) refuses uncounted actions while a task is measured; these
// checks close the doors around it: a driver may import only the fixture clients and Node's file
// helpers, never Playwright, the harness's own controls, a network module or a way to run code
// the checks cannot read.
import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { DRIVERS_DIR } from '../lib/registry.mjs';

export const ALLOWED_IMPORTS = new Set([
  './_common.mjs', '../../lib/ours-api.mjs', '../../lib/odoo-rpc.mjs', '../../lib/xlsx.mjs',
  'node:fs', 'node:path', 'node:os', 'node:crypto', 'node:url',
]);

const FORBIDDEN = [
  [/\bimport\s*\(/, 'dynamic import()'],
  [/\brequire\s*\(/, 'require()'],
  [/\bcreateRequire\b/, 'createRequire'],
  [/\beval\s*\(/, 'eval()'],
  [/\bnew\s+Function\b|\bFunction\s*\(/, 'the Function constructor'],
  [/\bprocess\.(binding|dlopen|_linkedBinding)\b/, 'native bindings'],
  [/__harnessSentinel/, 'the page sentinel'],
  [/\bclaim(Clock|Violations)\b/, 'the harness controls'],
  [/\bnewCDPSession\b/, 'a raw browser debugging session'],
  // Page script and request rewriting outlive the call (round 3: a listener installed during
  // sign-in finished the task inside the measured part). Drivers read with ctx.read and wait with
  // ctx.until; the guard refuses these at run time too, in every phase.
  [/\.(evaluate|evaluateAll|evaluateHandle|\$eval|\$\$eval|waitForFunction|addInitScript|addScriptTag|addStyleTag|exposeFunction|exposeBinding|route|routeFromHAR|routeWebSocket|setExtraHTTPHeaders|dispatchEvent|setContent)\s*\(/, 'page script or request rewriting'],
  [/\.clock\b/, "the page's clock"],
  // Another browser, or a debugging session or trace on the whole browser, acts where the guard
  // and the runner's fresh start do not reach (round 4).
  [/\b(browserType|newBrowserCDPSession|startTracing|connectOverCDP)\b/, 'another browser or a browser-wide session'],
  // Continuation (no M before a step) is derived by the instrument (lib/klm.mjs).
  [/\bchain\s*:/, 'a declared chain'],
];

export function driverFiles() {
  const out = [];
  for (const product of fs.readdirSync(DRIVERS_DIR)) {
    const dir = path.join(DRIVERS_DIR, product);
    if (!fs.statSync(dir).isDirectory()) continue;
    for (const f of fs.readdirSync(dir).filter(x => x.endsWith('.mjs'))) out.push(path.join(dir, f));
  }
  return out;
}

export function lintDriver(src) {
  const problems = [];
  for (const m of src.matchAll(/^\s*(?:import|export)\s[^'"]*?from\s*['"]([^'"]+)['"]|^\s*import\s*['"]([^'"]+)['"]/gm)) {
    const spec = m[1] || m[2];
    if (!ALLOWED_IMPORTS.has(spec)) problems.push(`imports ${spec}`);
  }
  for (const [re, what] of FORBIDDEN) if (re.test(src)) problems.push(`uses ${what}`);
  return problems;
}

test('every driver imports only the fixture clients and Node file helpers, and runs no unreadable code', () => {
  const files = driverFiles();
  assert.ok(files.length >= 36, `${files.length} driver files`);
  const bad = files.map(f => [path.relative(DRIVERS_DIR, f), lintDriver(fs.readFileSync(f, 'utf8'))]).filter(([, p]) => p.length);
  assert.deepEqual(bad, []);
});

test('the driver lint catches planted escapes', () => {
  const plants = [
    "import { chromium } from 'playwright-core';",
    "import { claimClock } from '../../lib/guard.mjs';",
    "import { Operator } from '../../lib/operator.mjs';",
    "import http from 'node:http';",
    "import { exec } from 'node:child_process';",
    "const m = await import('../../lib/guard.mjs');",
    "eval('1');",
    "const f = new Function('return 1');",
    "page.context().newCDPSession(page);",
    "await ctx.page.evaluate(() => document.querySelector('#save').click());",
    "await page.locator('a').evaluateAll(as => as.length);",
    "await ctx.context.addInitScript(() => {});",
    "await ctx.page.exposeFunction('f', () => {});",
    "await ctx.page.route('**/*', r => r.continue());",
    "await page.waitForFunction(() => true);",
    "await ctx.page.clock.install();",
    "await op.press('Enter', { label: 'x', chain: true });",
    "const other = await ctx.browser.browserType().launch();",
    "await ctx.browser.newBrowserCDPSession();",
    "await ctx.browser.startTracing();",
  ];
  for (const p of plants) assert.ok(lintDriver(p).length > 0, `not caught: ${p}`);
  assert.deepEqual(lintDriver("import { adminRpc } from './_common.mjs';\nimport path from 'node:path';"), []);
  assert.deepEqual(lintDriver("const v = await ctx.read(() => document.title);\nawait ctx.until(() => true, { page });"), []);
});

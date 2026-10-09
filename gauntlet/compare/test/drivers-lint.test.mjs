// Static checks on every driver file (both products, shared helpers included). The runtime guard
// (lib/guard.mjs, test/guard.test.mjs) refuses uncounted actions while a task is measured; these
// checks close the doors around it: a driver may import only the fixture clients and Node's file
// helpers, never Playwright, the harness's own controls, a network module or a way to run code
// the checks cannot read.
import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { Parser } from 'acorn';
import { DRIVERS_DIR } from '../lib/registry.mjs';
import { PageScriptRefused, checkPageScript } from '../lib/page-script.mjs';

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
  // Round 5: drivers run in the sandboxed driver process (lib/sandbox/), which refuses these at run
  // time; the lint refuses them in the source as well, so a plant shows in review.
  [/\bgetBuiltinModule\b/, 'process.getBuiltinModule'],
  [/\bprocess\s*\.\s*(binding|_linkedBinding|dlopen|send|kill|chdir)\b|\bprocess\s*\[/, 'a process control'],
  [/(?<![\w.$])(globalThis\s*\.\s*)?fetch\b(?!\s*\()/, 'a fetch reference kept for later'],
  [/\b(WebSocket|EventSource|SharedArrayBuffer|Atomics|WebAssembly)\b/, 'a socket, shared memory or WebAssembly'],
  [/\b(Performance|performance)\s*\.\s*(prototype|now\s*=)|\bDate\s*\.\s*now\s*=/, 'a patched clock'],
  // A transport is chosen by name from lib/api-transport.mjs, never written in a driver.
  [/\btransport\s*:\s*(async\s*)?(\(|function|[\w$]+\s*=>)/, 'a transport function'],
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

/** Methods whose function argument is page script (it runs in the page's read world). */
const PAGE_FUNCTION_CALLS = new Set(['read', 'until', 'waitFor']);

/**
 * Round 7: the parsed driver. A name built from text in a computed member (x['dispatch' + 'Event'],
 * x[`...${y}`]) hides what the line calls from the checks above, so it is refused; and every page
 * function the driver hands ctx.read, ctx.until or op.waitFor must pass the read-only check the
 * harness applies at run time (lib/page-script.mjs), so a condition that acts shows in review too.
 */
function lintParsed(src) {
  const problems = [];
  let ast;
  try { ast = Parser.parse(src, { ecmaVersion: 'latest', sourceType: 'module', allowReturnOutsideFunction: true, allowAwaitOutsideFunction: true }); } catch (e) { return [`does not parse (${e.message})`]; }
  const visit = (n, parent) => {
    if (!n || typeof n.type !== 'string') return;
    if (n.type === 'MemberExpression' && n.computed) {
      const k = n.property;
      if (k.type === 'TemplateLiteral' && k.expressions.length) problems.push(`uses a member name built from a template: ${src.slice(n.start, n.end).slice(0, 60)}`);
      if (k.type === 'BinaryExpression' && k.operator === '+') problems.push(`uses a member name built from text: ${src.slice(n.start, n.end).slice(0, 60)}`);
    }
    if (n.type === 'CallExpression' && n.callee.type === 'MemberExpression' && !n.callee.computed && PAGE_FUNCTION_CALLS.has(n.callee.property.name)) {
      const fn = n.arguments[0];
      if (fn && (fn.type === 'ArrowFunctionExpression' || fn.type === 'FunctionExpression')) {
        try { checkPageScript(src.slice(fn.start, fn.end)); } catch (e) {
          if (e instanceof PageScriptRefused) problems.push(`hands ${n.callee.property.name}() ${e.message}`); else throw e;
        }
      }
    }
    for (const [k, v] of Object.entries(n)) {
      if (k === 'start' || k === 'end') continue;
      if (Array.isArray(v)) v.forEach(x => visit(x, n)); else if (v && typeof v.type === 'string') visit(v, n);
    }
  };
  visit(ast, null);
  return problems;
}

export function lintDriver(src) {
  const problems = [];
  for (const m of src.matchAll(/^\s*(?:import|export)\s[^'"]*?from\s*['"]([^'"]+)['"]|^\s*import\s*['"]([^'"]+)['"]/gm)) {
    const spec = m[1] || m[2];
    if (!ALLOWED_IMPORTS.has(spec)) problems.push(`imports ${spec}`);
  }
  for (const [re, what] of FORBIDDEN) if (re.test(src)) problems.push(`uses ${what}`);
  problems.push(...lintParsed(src));
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
    // Round 5 (the critic's plants U4 and U5, and their neighbours).
    "const cp = process.getBuiltinModule('node:child_process');",
    "const fetchAtLoad = globalThis.fetch;",
    "const f = fetch;",
    "process.binding('tcp_wrap');",
    "process['binding']('tcp_wrap');",
    "new WebSocket('ws://localhost');",
    "performance.now = () => 0;",
    "Date.now = () => 0;",
    "return { baseUrl, transport: (verb, path) => ({ url: path }) };",
    "import net from 'node:net';",
    "import { Worker } from 'node:worker_threads';",
    "import inspector from 'node:inspector';",
    // Round 7 (the critic's lint bypass, and page functions that act).
    "el['dispatch' + 'Event'](e);",
    "const k = 'Event'; el[`dispatch${k}`](e);",
    "await ctx.read(async () => { await 0; document.getElementById('go').click(); return 1; });",
    "await op.waitFor(() => { document.body.animate([], 1).onfinish = () => {}; return true; });",
    "await ctx.until(() => { location.href = 'javascript:void(0)'; return true; });",
    "await op.waitFor(() => window.ok === true);",
  ];
  for (const p of plants) assert.ok(lintDriver(p).length > 0, `not caught: ${p}`);
  assert.deepEqual(lintDriver("import { adminRpc } from './_common.mjs';\nimport path from 'node:path';"), []);
  assert.deepEqual(lintDriver("const v = await ctx.read(() => document.title);\nawait ctx.until(() => true, { page });"), []);
  assert.deepEqual(lintDriver("const r = await fetch(url, { method: 'GET' });\nreturn { baseUrl, transport: 'odoo-json2', uid };"), []);
});

// Test helper: a driver written inline in a test becomes a driver module file, run in the driver
// process like every real driver (lib/sandbox/). Drivers never run in the harness process, so a
// test's driver cannot share its variables: the names a driver uses from the test (the stand-in's
// address, a planted action ...) are passed as `scope` and written into the module as constants.
// Function values keep their source (method shorthand included); data is written as literals.
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { HARNESS_DIR } from '../../lib/config.mjs';
import { describeDriverFile } from '../../lib/registry.mjs';

/** Names a test may give a driver that it also uses itself. */
export const SCOPE = Symbol('driver-scope');

const DIR = fs.mkdtempSync(path.join(os.tmpdir(), 'compare-test-drivers-'));
process.once('exit', () => fs.rmSync(DIR, { recursive: true, force: true }));

function isExpression(src) {
  try { new Function(`return (${src});`); return true; } catch { return false; } // eslint-disable-line no-new-func
}

/** Source text of a value, as a JavaScript expression. */
export function sourceOf(v, depth = 0) {
  if (depth > 20) throw new Error('value nested too deeply for a driver module');
  if (typeof v === 'function') {
    const src = Function.prototype.toString.call(v);
    if (isExpression(src)) return `(${src})`;
    // Method shorthand ("async run(op) { ... }"): an object literal's property.
    const name = /^(?:async\s+)?(?:\*\s*)?([A-Za-z_$][\w$]*)\s*\(/.exec(src)?.[1];
    if (!name) throw new Error(`cannot write this function into a driver module: ${src.slice(0, 80)}`);
    return `({ ${src} })[${JSON.stringify(name)}]`;
  }
  if (v instanceof RegExp) return v.toString();
  if (Array.isArray(v)) return `[${v.map(x => sourceOf(x, depth + 1)).join(', ')}]`;
  if (v && typeof v === 'object') {
    return `{ ${Object.entries(v).map(([k, x]) => `${JSON.stringify(k)}: ${sourceOf(x, depth + 1)}`).join(', ')} }`;
  }
  return v === undefined ? 'undefined' : JSON.stringify(v);
}

/**
 * Write `driver` (an object of hooks) as a module and return its path. `scope` names values its
 * functions use from the test (written as constants above the default export).
 */
export function writeDriverModule(driver, scope = {}) {
  const all = { ...(driver[SCOPE] || {}), ...scope };
  const lib = pathToFileURL(path.join(HARNESS_DIR, 'lib')).href;
  const lines = [
    "import assert from 'node:assert/strict';",
    "import fs from 'node:fs';",
    "import os from 'node:os';",
    "import path from 'node:path';",
    "import http from 'node:http';",
    `import { oursAs } from '${lib}/ours-api.mjs';`,
    ...Object.entries(all).map(([k, v]) => `const ${k} = ${sourceOf(v)};`),
    `export default ${sourceOf(Object.fromEntries(Object.entries(driver)))};`,
  ];
  const file = path.join(DIR, `driver-${Math.random().toString(36).slice(2)}.mjs`);
  fs.writeFileSync(file, lines.join('\n') + '\n');
  return file;
}

/** A driver object, written as a module and described by the driver process: ready for execute(). */
export function sandboxed(driver, scope = {}) {
  return describeDriverFile(writeDriverModule(driver, scope));
}

/** A real driver file, transformed (a critic's plant), written beside it in the temporary folder. */
export function plantedFile(file, transform) {
  const lib = pathToFileURL(path.join(HARNESS_DIR, 'lib')).href;
  const dir = pathToFileURL(path.dirname(file)).href;
  const src = transform(fs.readFileSync(file, 'utf8')).replaceAll("'../../lib/", `'${lib}/`).replaceAll("'./_common.mjs'", `'${dir}/_common.mjs'`);
  const out = path.join(DIR, `${path.basename(file, '.mjs')}-${Math.random().toString(36).slice(2)}.mjs`);
  fs.writeFileSync(out, src);
  return out;
}

/** A driver module written as source text (for plants that need module top-level code). */
export function sandboxedSource(source) {
  const lib = pathToFileURL(path.join(HARNESS_DIR, 'lib')).href;
  const file = path.join(DIR, `driver-${Math.random().toString(36).slice(2)}.mjs`);
  fs.writeFileSync(file, source.replaceAll("'../../lib/", `'${lib}/`));
  return describeDriverFile(file);
}

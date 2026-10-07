// Critic r6: a variant's own set-up and sign-in hooks are never called (only its run is).
// The ours sign-in driver's `returning` variant signs in and out in its own signIn; the start then
// shows an empty e-mail field, so `returning` is measured exactly like `new-device`.
import test, { after, before } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import http from 'node:http';
import { execute, layout } from '../lib/runner.mjs';
import { sandboxed } from './helpers/driver-module.mjs';

let server, base, tmp, calls = [];
before(async () => {
  server = http.createServer((req, res) => {
    if (req.url.startsWith('/mark/')) { calls.push(req.url); res.writeHead(200); return res.end('ok'); }
    res.writeHead(200, { 'Content-Type': 'text/html' }); res.end('<!doctype html><input id="q" aria-label="Q"><div id="out"></div>');
  });
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  base = `http://127.0.0.1:${server.address().port}`;
  tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'critic-r6v-'));
});
after(() => { server?.close(); fs.rmSync(tmp, { recursive: true, force: true }); });

test('a variant\'s own setup and signIn run before its measured part', async () => {
  const TASK = { id: 'plant', title: 'Plant', startAt: 'list', moments: [], input: {} };
  const spec = await sandboxed({
    variants: {
      a: { async setup() { await fetch(base + '/mark/setup-a'); }, async signIn(ctx) { await fetch(base + '/mark/signin-a'); await ctx.page.goto(base + '/a'); }, async run(op) { await op.click('#q'); return {}; } },
    },
    async signIn(ctx) { await ctx.page.goto(base + '/base'); },
    async verify(ctx, outcome) { return { verified: outcome !== undefined }; },
  }, { base });
  const r = await execute(TASK, { ...spec, variant: 'a' }, { id: 'ours', baseUrl: base, users: {}, brandWords: [], readyKind: 'default' }, 'ours', {}, layout(path.join(tmp, 'x')), { timeout: 10_000 });
  console.log(`RESULT variant hooks: status=${r.status} start path=${r.start_state?.path} marks=${JSON.stringify(calls)}`);
  assert.deepEqual(calls, ['/mark/setup-a', '/mark/signin-a'], 'the variant\'s setup and signIn were not called');
});

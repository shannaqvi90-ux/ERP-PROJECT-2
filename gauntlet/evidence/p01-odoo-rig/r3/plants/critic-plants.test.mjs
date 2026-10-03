// Critic round 3 plants against the measuring instrument. Each plant is a driver that games
// the counts or the clock. The instrument should end each plant as "invalid" (or count it
// honestly). These tests ASSERT WHAT HAPPENED and print it, so a passing test here means the
// plant was NOT caught (it documents the miss).
import test, { after, before } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import http from 'node:http';
import { execute, layout } from '../lib/runner.mjs';

const PAGE = `<!doctype html><html><head><title>Plant page</title></head><body>
  <form id="f" onsubmit="event.preventDefault(); setTimeout(()=>{document.getElementById('out').textContent='saved '+this.a.value+'/'+this.b.value}, 50)">
  <input name="a" id="a" aria-label="A"><input name="b" id="b" aria-label="B"><button id="save">Save</button></form>
  <button id="slow" onclick="setTimeout(()=>{document.getElementById('out').textContent='found slow'},1500)">Slow</button>
  <div id="out"></div>
</body></html>`;

let server, base, tmp;
before(async () => {
  server = http.createServer((req, res) => { res.writeHead(200, { 'Content-Type': 'text/html' }); res.end(PAGE); });
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  base = `http://127.0.0.1:${server.address().port}`;
  tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'critic-plants-'));
});
after(() => { server?.close(); fs.rmSync(tmp, { recursive: true, force: true }); });

const TASK = { id: 'plant', title: 'Plant', input: {} };
const runDriver = driver => execute(TASK, driver, { id: 'ours', baseUrl: base, users: {}, brandWords: [] }, 'ours', {},
  layout(path.join(tmp, String(Math.random()).slice(2))), {});
const verifySaved = async ctx => ({ verified: ((await ctx.page.locator('#out').textContent()) || '') === 'saved x/y' });
const report = (name, r) => console.log(`PLANT ${name}: status=${r.status} steps=${r.counts?.steps} keys=${r.counts?.keystrokes} machine=${r.counts?.machine_seconds} human=${r.counts?.human_seconds} wait=${r.counts?.system_wait_seconds} err=${r.error ?? ''}`);

test('control: honest path, click A, type, click B, type, click Save', async () => {
  const r = await runDriver({
    async signIn(ctx) { await ctx.page.goto(base + '/'); },
    async run(op) {
      await op.fill('#a', 'x'); await op.fill('#b', 'y'); await op.click('#save');
      await op.waitFor(() => document.getElementById('out').textContent.startsWith('saved'));
    },
    verify: verifySaved,
  });
  report('control', r);
  assert.equal(r.status, 'verified');
});

test('PLANT U1: page script installed during signIn (unmeasured) acts during the measured part', async () => {
  const r = await runDriver({
    async signIn(ctx) {
      await ctx.page.goto(base + '/');
      // Outside the measurement evaluate is allowed: install a helper that, on the first key
      // typed into A, fills B and submits the form. These actions are never counted.
      await ctx.page.evaluate(() => {
        document.getElementById('a').addEventListener('input', () => {
          setTimeout(() => { document.getElementById('b').value = 'y'; document.getElementById('save').click(); }, 20);
        }, { once: true });
      });
    },
    async run(op) {
      await op.click('#a'); await op.type('x', { chain: true });
      await op.waitFor(() => document.getElementById('out').textContent.startsWith('saved'));
    },
    verify: verifySaved,
  });
  report('U1-setup-page-script', r);
  assert.equal(r.status, 'verified', 'if this fails, the plant was caught');
});

test('PLANT U2: init script added to the context in setup acts during the measured part', async () => {
  const r = await runDriver({
    async setup(ctx) {
      await ctx.context.addInitScript(() => {
        document.addEventListener('keydown', e => {
          if (e.key === 'F9') { document.getElementById('a').value = 'x'; document.getElementById('b').value = 'y'; document.getElementById('save').click(); }
        });
      });
    },
    async signIn(ctx) { await ctx.page.goto(base + '/'); },
    async run(op) {
      await op.press('F9');
      await op.waitFor(() => document.getElementById('out').textContent.startsWith('saved'));
    },
    verify: verifySaved,
  });
  report('U2-context-init-script', r);
  assert.equal(r.status, 'verified', 'if this fails, the plant was caught');
});

test('PLANT S1: op.type carries a newline, so Enter is pressed inside the typing step and is not a step of its own', async () => {
  const r = await runDriver({
    async signIn(ctx) { await ctx.page.goto(base + '/'); },
    async run(op) {
      await op.click('#a');
      await op.type('x', { chain: true }); await op.press('Tab');
      await op.type('y\n', { chain: true });
      await op.waitFor(() => document.getElementById('out').textContent.startsWith('saved'));
    },
    verify: verifySaved,
  });
  report('S1-newline-in-type', r);
  const honest = await runDriver({
    async signIn(ctx) { await ctx.page.goto(base + '/'); },
    async run(op) {
      await op.click('#a'); await op.type('x', { chain: true }); await op.press('Tab');
      await op.type('y', { chain: true }); await op.press('Enter', { chain: true });
      await op.waitFor(() => document.getElementById('out').textContent.startsWith('saved'));
    },
    verify: verifySaved,
  });
  report('S1-honest', honest);
  assert.ok(r.counts.steps < honest.counts.steps, 'if this fails the plant was caught');
  assert.equal(r.status, 'verified', 'if this fails, the plant was caught');
});

test('PLANT T1: screenshots taken while the product works hide its latency from machine seconds', async () => {
  const honest = await runDriver({
    async signIn(ctx) { await ctx.page.goto(base + '/'); },
    async run(op) {
      await op.click('#slow');
      await op.waitFor(() => document.getElementById('out').textContent === 'found slow');
    },
    async verify(ctx) { return { verified: (await ctx.page.locator('#out').textContent()) === 'found slow' }; },
  });
  report('T1-honest', honest);
  const gamed = await runDriver({
    async signIn(ctx) { await ctx.page.goto(base + '/'); },
    async run(op) {
      await op.click('#slow');
      let n = 0;
      while ((await op.page.locator('#out').textContent()) !== 'found slow' && n < 100) { await op.shot('progress'); n++; }
      await op.waitFor(() => document.getElementById('out').textContent === 'found slow');
      return { shots: n };
    },
    async verify(ctx, out) { return { verified: (await ctx.page.locator('#out').textContent()) === 'found slow', details: out }; },
  });
  report('T1-gamed', gamed);
  console.log(`T1 shots taken while measuring: ${gamed.verification?.details?.shots}`);
  assert.equal(gamed.status, 'verified');
  assert.ok(gamed.counts.machine_seconds < honest.counts.machine_seconds - 0.5, 'if this fails the clock was not fooled');
});

test('PLANT K1: every step marked chain:true drops every M from the human model', async () => {
  const mk = chain => ({
    async signIn(ctx) { await ctx.page.goto(base + '/'); },
    async run(op) {
      await op.click('#a', { chain }); await op.type('x', { chain: true });
      await op.click('#b', { chain }); await op.type('y', { chain: true });
      await op.click('#save', { chain });
      await op.waitFor(() => document.getElementById('out').textContent.startsWith('saved'));
    },
    verify: verifySaved,
  });
  const honest = await runDriver(mk(false));
  const gamed = await runDriver(mk(true));
  report('K1-honest', honest); report('K1-gamed', gamed);
  assert.equal(gamed.status, 'verified');
  assert.ok(gamed.counts.human_seconds < honest.counts.human_seconds, 'if this fails, the plant was caught');
});

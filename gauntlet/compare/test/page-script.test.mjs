// Round 7: page functions only read, now and later, and the screen verify() reads is the screen the
// clock left (lib/page-script.mjs, lib/runner.mjs freezePages).
//
// The round 6 critic's plants: S1 (a reader's native-await continuation filled the form and saved),
// S2 (a condition's animation callback did it), S3 (a reader's MutationObserver did it on the next
// change) and T3 (run() returned before a 2 s answer and verify() read the late answer in short
// reads). Each layer is tested on its own: the source check without a browser, the read world with
// sources that skip the source check (so the world's own refusals are what is tested), the freeze
// directly, and every plant end to end through the runner.
import test, { after, before } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import http from 'node:http';
import { execute, freezePages, layout } from '../lib/runner.mjs';
import { launch, newContext } from '../lib/browser.mjs';
import { PageScriptRefused, PageWorld, checkPageScript, screenChange } from '../lib/page-script.mjs';
import { PageFunction, isRefusal } from '../lib/guard.mjs';
import { DRIVERS_DIR } from '../lib/registry.mjs';
import { lintDriver } from './drivers-lint.test.mjs';
import { sandboxed } from './helpers/driver-module.mjs';

// ---------------------------------------------------------------------------------------------
// 1. The source check.

const READS = [
  () => document.querySelectorAll('.o_data_row').length === 1,
  () => /\/\d+$/.test(location.pathname) && location.search === '',
  m => { const form = document.querySelector('.o_form_view'); return !!form && [...form.querySelectorAll('input')].some(i => i.value === m); },
  ([a, b]) => { const t = document.body.innerText; return t.includes(a) && t.includes(b); },
  () => getComputedStyle(document.querySelector('nav')).direction === 'rtl',
  () => ({ rows: [...document.querySelectorAll('tr')].map(r => r.innerText.replace(/\s+/g, ' ').trim()), cookie: document.cookie }),
  () => { let n = 0; for (const r of document.querySelectorAll('tr')) { if (r.hidden) continue; n += 1; } return n; },
  () => document.activeElement?.closest('nav') && document.activeElement.getAttribute('href') === '/x',
  () => { const rows = document.querySelectorAll('tr'); return rows[2] === rows.item(2) && Object.keys({ a: 1 }).length === 1 && new Set([1]).size === 1; },
  function named(arg) { return JSON.stringify({ arg }) !== ''; },
];

const REFUSED = {
  'an async function (S1)': [async () => { await 0; document.getElementById('go').click(); }, /asynchronous/],
  'an async function inside a reader (S1, as the critic wrote it)': [() => { (async () => { await 0; })(); return 1; }, /asynchronous/],
  'a generator': [() => { function* g() { yield 1; } return g; }, /generator/],
  'a property write (S2: onfinish)': [() => { const a = document.body.animate([], 1); a.onfinish = () => 1; return 1; }, /writes to a property/],
  'a value write': [() => { document.getElementById('q').value = 'x'; return 1; }, /writes to a property/],
  'a text write': [() => { document.getElementById('out').textContent = 'found'; return 1; }, /writes to a property/],
  'a write to the address': [() => { location.href = 'javascript:void(0)'; return 1; }, /writes to a property|address object/],
  'an assignment to location itself': [() => { location = '/deep'; return 1; }, /assigns to "location"/], // eslint-disable-line no-global-assign
  'location.assign': [() => { location.assign('/deep'); return 1; }, /address object/],
  'location.replace with a javascript: address': [() => { location.replace('javascript:void(0)'); return 1; }, /address object/],
  'document.location': [() => document.location.pathname, /\.location/],
  'the address object kept in a name': [() => { const l = location; return l.pathname; }, /address object/],
  'window': [() => window.ok, /uses window/],
  'globalThis': [() => globalThis.ok, /uses globalThis/],
  'self, top, parent, frames': [() => self === top || parent === frames, /uses (self|top|parent|frames)/],
  'this': [function () { return this; }, /uses this/],
  'an iframe\'s window': [() => document.querySelector('iframe').contentWindow, /contentWindow/],
  'the document\'s window': [() => document.defaultView, /defaultView/],
  'a computed member name': [() => { const k = 'loc' + 'ation'; return document[k]; }, /computed/],
  'a computed member name (template)': [() => document[`${'cook'}ie`], /computed/],
  'a destructured address': [() => { const { location: l } = document; return l; }, /reads location/],
  'Object.values (reaches the address)': [() => Object.values(document).length, /Object\.values/],
  'Object.getOwnPropertyDescriptor': [() => Object.getOwnPropertyDescriptor(document, 'cookie'), /Object\.getOwnPropertyDescriptor/],
  'JSON.stringify with a replacer': [() => JSON.stringify(document, (k, v) => v), /replacer/],
  'Reflect': [() => Reflect.get(document, 'cookie'), /uses Reflect/],
  'eval': [() => eval('1'), /uses eval/], // eslint-disable-line no-eval
  'Function': [() => Function('return 1')(), /uses Function/], // eslint-disable-line no-new-func
  'a constructor reached through a function': [() => (() => 1).constructor, /\.constructor/],
  'a prototype': [() => HTMLElement.prototype, /\.prototype/],
  'a promise continuation': [() => document.fonts.ready.then(() => 1), /\.then/],
  'an object that is a promise': [() => ({ then: () => 1 }), /then/],
  'an observer (S3)': [() => { const mo = new MutationObserver(() => 1); return !!mo; }, /constructs/],
  'a promise': [() => !!new Promise(r => r(1)), /constructs/],
  'an image (it loads an address)': [() => !!new Image(), /constructs/],
  'a class': [() => class X {}, /class/],
  'a tagged template': [() => String.raw`x`, /tag function/],
  'an import': [() => import('/x.js'), /imports/],
  'with': [new PageSource('function () { with (document) { return cookie; } }'), /with/],
  'delete': [() => { const o = { a: 1 }; delete o.a; return o; }, /deletes/],
  'an update of a global': [() => { counter++; return 1; }, /did not declare/], // eslint-disable-line no-undef
  'a debugger statement': [() => { debugger; return 1; }, /debugger/], // eslint-disable-line no-debugger
};

function PageSource(text) { this.text = text; }
const sourceOf = f => (f instanceof PageSource ? f.text : Function.prototype.toString.call(f));

test('the source check accepts page functions that only read, every driver\'s among them', () => {
  for (const f of READS) assert.doesNotThrow(() => checkPageScript(sourceOf(f)), sourceOf(f));
  // Every page function in every driver passes (the lint checks the same; here as a count).
  let n = 0;
  for (const product of fs.readdirSync(DRIVERS_DIR)) {
    for (const f of fs.readdirSync(path.join(DRIVERS_DIR, product))) {
      const problems = lintDriver(fs.readFileSync(path.join(DRIVERS_DIR, product, f), 'utf8'));
      assert.deepEqual(problems, [], `${product}/${f}`);
      n++;
    }
  }
  assert.ok(n >= 44, `${n} driver files`);
});

for (const [name, [f, why]] of Object.entries(REFUSED)) {
  test(`the source check refuses a page function that acts or can act later: ${name}`, () => {
    assert.throws(() => checkPageScript(sourceOf(f)), e => e instanceof PageScriptRefused && why.test(e.message), `${name}: ${sourceOf(f)}`);
    // Handed to the harness, the same text is a refusal: the run is invalid.
    assert.throws(() => new PageFunction(sourceOf(f)), e => isRefusal(e), name);
  });
}

test('the source check refuses text that is not exactly one function expression', () => {
  for (const t of ['() => 1), document.forms[0].submit(), (() => 1', '() => 1 // trailing', '1 + 1', 'function () {} function () {}', '() => 1 <!-- x']) {
    assert.throws(() => checkPageScript(t), PageScriptRefused, t);
  }
});

// ---------------------------------------------------------------------------------------------
// Stand-in product.
let posts = 0;
let slowHits = 0;
const PAGE = `<!doctype html><html><head><title>Stand-in</title></head><body>
  <input id="q" aria-label="Query"><button id="go">Go</button><button id="look">Look</button><div id="out">none</div>
  <a id="deep" href="/deep">deep</a><iframe id="f" srcdoc="<p>child</p>"></iframe>
  <script>
    window.secret = 'main world';
    document.getElementById('go').onclick = async () => {
      const r = await fetch('/api/save', { method: 'POST', body: document.getElementById('q').value });
      document.getElementById('out').textContent = 'saved ' + (await r.text());
    };
    document.getElementById('look').onclick = async () => {
      const r = await fetch('/api/slow?q=' + encodeURIComponent(document.getElementById('q').value));
      document.getElementById('out').textContent = 'found ' + (await r.text());
    };
  </script></body></html>`;
// The answer is fast, but the page shows it 1.2 s later (a timer): the freeze must stop the timer.
const TIMER_PAGE = `<!doctype html><html><body><input id="q" aria-label="Query"><button id="look">Look</button><div id="out">none</div>
  <script>document.getElementById('look').onclick = async () => {
    const r = await fetch('/api/echo?q=' + encodeURIComponent(document.getElementById('q').value)); const t = await r.text();
    setTimeout(() => { document.getElementById('out').textContent = 'found ' + t; }, 1200); };</script></body></html>`;
// A read whose failure writes the "answer": aborting it at the clock must not let that count.
const ERROR_PAGE = `<!doctype html><html><body><input id="q" aria-label="Query"><button id="look">Look</button><div id="out">none</div>
  <script>document.getElementById('look').onclick = () => {
    fetch('/api/slow?q=x').then(r => r.text()).then(t => { document.getElementById('out').textContent = 'late ' + t; },
      () => { document.getElementById('out').textContent = 'found abcdefghij'; }); };</script></body></html>`;
// An honest product with a live clock on screen (a ticker every 100 ms): the freeze holds it still.
const TICKER_PAGE = `<!doctype html><html><body><div id="tick">0</div><input id="q" aria-label="Query"><button id="go">Go</button><div id="out">none</div>
  <script>let n = 0; setInterval(() => { document.getElementById('tick').textContent = String(++n); }, 100);
  document.getElementById('go').onclick = () => { document.getElementById('out').textContent = 'saved ' + document.getElementById('q').value; };</script></body></html>`;
const VARIANT_PAGE = '<!doctype html><input id="q" aria-label="Q"><div id="out"></div>';

let server, base, tmp, browser;
const marks = [];
before(async () => {
  server = http.createServer((req, res) => {
    const u = new URL(req.url, 'http://x');
    if (u.pathname.startsWith('/mark/')) { marks.push(u.pathname); res.writeHead(200); return res.end('ok'); }
    if (u.pathname === '/api/save') { let b = ''; req.on('data', c => { b += c; }); req.on('end', () => { posts++; setTimeout(() => { res.writeHead(200); res.end(b); }, 300); }); return; }
    if (u.pathname === '/api/slow') { slowHits++; setTimeout(() => { res.writeHead(200); res.end(u.searchParams.get('q') || ''); }, 2000); return; }
    if (u.pathname === '/api/echo') { res.writeHead(200); return res.end(u.searchParams.get('q') || ''); }
    const html = h => { res.writeHead(200, { 'Content-Type': 'text/html' }); res.end(h); };
    if (u.pathname === '/timer') return html(TIMER_PAGE);
    if (u.pathname === '/error') return html(ERROR_PAGE);
    if (u.pathname === '/ticker') return html(TICKER_PAGE);
    if (u.pathname === '/a' || u.pathname === '/base') return html(VARIANT_PAGE);
    html(PAGE);
  });
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  base = `http://127.0.0.1:${server.address().port}`;
  tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'page-script-'));
  browser = await launch();
});
after(async () => { await browser?.close(); server?.close(); fs.rmSync(tmp, { recursive: true, force: true }); });

// ---------------------------------------------------------------------------------------------
// 2 and 3. The read world on its own: sources that skip the source check, so what is tested is the
// world's own refusals (every one is also refused in source).

async function worldPage(pathname = '/plant') {
  const context = await newContext(browser);
  const page = await context.newPage();
  await page.goto(base + pathname);
  return { context, page, world: PageWorld.of(page) };
}
const outText = page => page.locator('#out').textContent();

test('the read world sees the document but not the page\'s own script world', async () => {
  const { context, world } = await worldPage();
  assert.equal((await world.run('() => typeof secret', null)).value, 'undefined', 'a page global is out of reach');
  assert.equal((await world.run('() => document.getElementById("out").textContent', null)).value, 'none');
  assert.equal((await world.run('n => n + 1', 41)).value, 42);
  await context.close();
});

const ACTS = {
  'a click (the product\'s handler never runs)': 'document.getElementById("go").click()',
  'focus': 'document.getElementById("q").focus()',
  'a value': 'document.getElementById("q").value = "abc"',
  'a text change': 'document.getElementById("out").textContent = "saved abc"',
  'an attribute (a javascript: address on a link)': 'document.getElementById("deep").setAttribute("href", "javascript:void(0)")',
  'a new element': 'document.body.appendChild(document.createElement("div"))',
  'an event': 'document.getElementById("go").dispatchEvent(new MouseEvent("click", { bubbles: true }))',
  'a form submit': 'document.createElement("form").requestSubmit()',
  'the network': 'fetch("/api/save", { method: "POST" })',
  'a timer': 'setTimeout(() => 1, 0)',
  'a promise continuation': 'Promise.resolve(1).then(() => 1)',
  'a promise of a thenable': 'Promise.resolve({ then() {} })',
  'an observer': 'new MutationObserver(() => 1)',
  'an animation (S2)': 'document.body.animate([{ opacity: 1 }], 1)',
  'a listener': 'document.addEventListener("click", () => 1)',
  'a handler property': 'document.getElementById("go").onclick = () => 1',
  'storage': 'localStorage.setItem("k", "v")',
  'history': 'history.pushState(null, "", "/x")',
  'property values by enumeration (the address among them)': 'Object.values(document)',
  'a replacer that sees every value': 'JSON.stringify(document, (k, v) => v)',
  'eval': 'eval("1")',
  'the Function constructor through a function': '(() => 1).constructor("return 1")',
  'another window through the document': 'document.defaultView',
  'an iframe\'s own script world': 'document.getElementById("f").contentWindow',
  'a selection change': 'getSelection().selectAllChildren(document.body)',
  'a document write': 'document.write("x")',
};
for (const [name, body] of Object.entries(ACTS)) {
  test(`the read world refuses and reports ${name}`, async () => {
    const { context, page, world } = await worldPage();
    const before = posts;
    await assert.rejects(world.run(`() => { ${body}; return 1; }`, null), e => e.name === 'PageScriptAction', name);
    // The refusal is reported even when the function swallows it.
    await assert.rejects(world.run(`() => { try { ${body}; } catch (e) { /* swallowed */ } return 1; }`, null), e => e.name === 'PageScriptAction', `${name} (swallowed)`);
    await new Promise(r => setTimeout(r, 400));
    assert.equal(await outText(page), 'none', `${name}: the page changed`);
    assert.equal(posts, before, `${name}: the product was called`);
    await context.close();
  });
}

test('the read world stays armed after a call returns: what a function scheduled finds every action refused (S1 at its root)', async () => {
  const { context, page, world } = await worldPage();
  // The critic's S1, unchecked: the continuation runs after the call returned.
  const r = await world.run('() => { (async () => { await 0; document.getElementById("q").focus(); document.getElementById("go").click(); })(); return 1; }', null);
  assert.equal(r.value, 1);
  await new Promise(res => setTimeout(res, 400));
  assert.equal(await outText(page), 'none', 'the continuation clicked Go');
  assert.equal(posts, posts, 'nothing posted');
  const late = await world.drain();
  assert.ok(late.some(x => /focus|click/.test(x)), `the late refusal is logged for the harness: ${JSON.stringify(late)}`);
  // The next call reports it too, whatever it does itself.
  await world.run('() => { (async () => { await 0; document.getElementById("go").click(); })(); return 1; }', null);
  await new Promise(res => setTimeout(res, 100));
  await assert.rejects(world.run('() => 1', null), e => e.name === 'PageScriptAction' && /click/.test(e.message));
  await context.close();
});

test('the read world cannot be re-armed or patched: its prototypes are frozen', async () => {
  const { context, page, world } = await worldPage();
  await assert.rejects(world.run('() => { HTMLElement.prototype.click = function () { return 1; }; return 1; }', null), /threw|read only|Cannot assign/i);
  await assert.rejects(world.run('() => { Object.defineProperty(HTMLElement.prototype, "click", { value: () => 1 }); return 1; }', null));
  await assert.rejects(world.run('() => { document.getElementById("go").click(); return 1; }', null), e => e.name === 'PageScriptAction');
  assert.equal(await outText(page), 'none');
  await context.close();
});

test('the read world reports a change it cannot refuse (a data attribute) as a change to the page', async () => {
  const { context, world } = await worldPage();
  await assert.rejects(world.run('() => { document.getElementById("out").dataset.x = "1"; return 1; }', null), e => e.name === 'PageScriptAction' && /DOM mutation/.test(e.message));
  await context.close();
});

test('a new document gets a new, armed read world', async () => {
  const { context, page, world } = await worldPage();
  await world.run('() => 1', null);
  await page.goto(base + '/plant?again');
  assert.equal((await world.run('() => location.search', null)).value, '?again');
  await assert.rejects(world.run('() => { document.getElementById("go").click(); return 1; }', null), e => e.name === 'PageScriptAction');
  await context.close();
});

test('a page function that runs too long is ended', async () => {
  const { context, world } = await worldPage();
  await assert.rejects(world.run('() => { for (;;) {} }', null, { timeoutMs: 300 }), /ran for over 300 ms/);
  assert.equal((await world.run('() => 2', null)).value, 2, 'the world still answers');
  await context.close();
});

test('the screen fingerprint sees the text, attributes, values and focus, but not what the blind shot empties', async () => {
  const { context, page, world } = await worldPage();
  const a = await world.fingerprint();
  assert.equal(screenChange(a, await world.fingerprint()), null);
  await page.evaluate(() => { document.title = 'Product'; document.getElementById('q').setAttribute('placeholder', ''); document.getElementById('q').setAttribute('aria-label', ''); });
  assert.equal(screenChange(a, await world.fingerprint()), null, 'the harness\'s own neutralising is not a change');
  await page.evaluate(() => { document.getElementById('out').setAttribute('data-state', 'done'); });
  assert.match(screenChange(a, await world.fingerprint()), /the page/);
  const b = await world.fingerprint();
  await page.fill('#q', 'x');
  assert.match(screenChange(b, await world.fingerprint()), /value|focused/);
  await context.close();
});

// ---------------------------------------------------------------------------------------------
// The freeze, on its own (round 5 froze the script only; round 7 also aborts what is still loading).

test('the freeze holds the screen as it was for longer than the product\'s answer: no late answer, no timer', async () => {
  const context = await newContext(browser);
  const page = await context.newPage();
  await page.goto(base + '/ticker');
  await page.locator('#tick').filter({ hasText: /^[3-9]|\d\d/ }).waitFor();
  const slow = await context.newPage();
  await slow.goto(base + '/plant');
  await slow.fill('#q', 'abc');
  await slow.click('#look');
  await new Promise(r => setTimeout(r, 100));
  const thaw = await freezePages(context);
  const tick = await page.locator('#tick').textContent();
  await new Promise(r => setTimeout(r, 2600)); // over the 2 s answer
  assert.equal(await page.locator('#tick').textContent(), tick, 'a timer of the product ran after the freeze');
  assert.equal(await slow.locator('#out').textContent(), 'none', 'the answer to a request under way at the freeze reached the screen');
  await thaw();
  await context.close();
});

// ---------------------------------------------------------------------------------------------
// The plants, end to end.

const TASK = { id: 'plant', title: 'Plant', startAt: 'list', moments: [], input: {} };
const standIn = () => ({ id: 'ours', baseUrl: base, users: {}, brandWords: [], readyKind: 'default' });
const runDriver = async (driver, scope = {}, task = TASK) => execute(task, await sandboxed(driver, { base, ...scope }), standIn(), 'ours', {}, layout(path.join(tmp, String(Math.random()).slice(2))), { timeout: 10_000 });
const SAVED = { async verify(ctx) { return { verified: (await ctx.page.locator('#out').textContent()) === 'saved abcdefghij' }; } };
const onPlant = { async signIn(ctx) { await ctx.page.goto(base + '/plant'); } };

test('control: the honest path verifies (3 steps, 10 keys)', async () => {
  const r = await runDriver({ ...onPlant, async run(op) { await op.fill('#q', 'abcdefghij'); await op.click('#go'); await op.waitFor(() => document.getElementById('out').textContent === 'saved abcdefghij', { label: 'saved' }); return {}; }, ...SAVED });
  assert.equal(r.status, 'verified', r.error);
  assert.equal(r.counts.steps, 3);
  assert.equal(r.counts.keystrokes, 10);
  assert.equal(r.screen_after_verify?.unchanged, true);
});

const PLANTS = {
  'S1: a reader whose native-await continuation fills and saves': async (op, ctx) => {
    await ctx.read(() => { (async () => { await 0; document.getElementById('q').value = 'abcdefghij'; document.getElementById('go').click(); })(); return 1; });
  },
  'S2: a condition that schedules the work through an animation callback': async op => {
    await op.waitFor(() => { const a = document.body.animate([{ opacity: 1 }, { opacity: 1 }], 1); a.onfinish = () => { document.getElementById('go').click(); }; return true; });
  },
  'S3: a reader that leaves an observer to act on the next change': async (op, ctx) => {
    await ctx.read(() => { const mo = new MutationObserver(() => document.getElementById('go').click()); mo.observe(document.body, { subtree: true, childList: true }); return 1; });
  },
  'a javascript: address set by a condition (it would run later in the page\'s own world)': async op => {
    await op.waitFor(() => { location.href = "javascript:document.getElementById('go').click()"; return true; });
  },
  'location.replace with a javascript: address': async op => {
    await op.waitFor(() => { location.replace("javascript:document.getElementById('go').click()"); return true; });
  },
  'the address object reached by enumerating the document': async (op, ctx) => {
    await ctx.read(() => Object.values(document).filter(v => v && v.ancestorOrigins).length);
  },
  'Object kept under another name (the source check cannot see it; the world refuses it)': async (op, ctx) => {
    await ctx.read(() => { const O = Object; return O.values(document).length; });
  },
  'a condition that clicks (the source check allows a call; the world refuses it)': async op => {
    await op.waitFor(() => { document.getElementById('go').click(); return true; });
  },
  'a condition that animates': async op => { await op.waitFor(() => !!document.body.animate([], 1)); },
  'a condition that reaches an iframe\'s own script world': async op => { await op.waitFor(() => !!document.getElementById('f').contentWindow); },
  'a reader whose result is a promise-like object': async (op, ctx) => { await ctx.read(() => ({ then: () => document.getElementById('go').click() })); },
  'a reader handed a page object as its argument': async (op, ctx) => { await ctx.read(x => !!x, op.page.locator('#go')); },
};
for (const [name, act] of Object.entries(PLANTS)) {
  test(`plant ${name} -> invalid, nothing done`, async () => {
    const before = posts;
    const r = await runDriver({
      ...onPlant,
      async run(op, ctx) {
        try { await act(op, ctx); } catch { /* a driver that swallows the refusal is still invalid */ }
        await op.waitFor(() => document.getElementById('out').textContent === 'saved abcdefghij', { label: 'saved', timeout: 1500 });
        return {};
      },
      ...SAVED,
    }, { act });
    assert.notEqual(r.status, 'verified', `${name}: verified with ${r.counts?.steps} steps and ${r.counts?.keystrokes} keys`);
    assert.equal(r.status, 'invalid', `${name}: ${r.status} ${r.error}`);
    await new Promise(res => setTimeout(res, 400));
    assert.equal(posts, before, `${name}: the product saved`);
  });
}

test('plant T3 (round 6): run() returns before a 2 s answer and verify() reads the page in short reads -> never verified', async () => {
  const hits = slowHits;
  const r = await runDriver({
    ...onPlant,
    async run(op) { await op.fill('#q', 'abcdefghij'); await op.click('#look'); return {}; },
    async verify(ctx, outcome) {
      const done = async () => (await ctx.read(() => document.getElementById('out').textContent)) === 'found abcdefghij';
      if (outcome === undefined) return { verified: await done() };
      for (let i = 0; i < 9; i++) { if (await done()) return { verified: true }; await new Promise(r2 => setTimeout(r2, 300)); }
      return { verified: false };
    },
  });
  assert.notEqual(r.status, 'verified', `verified at ${r.counts?.machine_seconds} s against the product's 2 s answer`);
  assert.equal(slowHits, hits + 1);
  assert.equal(r.requests_in_flight_at_clock?.count, 1, `the read under way at the clock is recorded: ${JSON.stringify(r.requests_in_flight_at_clock)}`);
  assert.equal(r.screen_after_verify?.unchanged, true, 'the late answer never reached the screen');
});

test('plant T4: the answer is fast but the page shows it by a timer 1.2 s later; run() returns at once -> never verified', async () => {
  const r = await runDriver({
    async signIn(ctx) { await ctx.page.goto(base + '/timer'); },
    async run(op) { await op.fill('#q', 'abcdefghij'); await op.click('#look'); return {}; },
    async verify(ctx, outcome) {
      const done = async () => (await ctx.read(() => document.getElementById('out').textContent)) === 'found abcdefghij';
      if (outcome === undefined) return { verified: await done() };
      for (let i = 0; i < 6; i++) { if (await done()) return { verified: true }; await new Promise(r2 => setTimeout(r2, 300)); }
      return { verified: false };
    },
  });
  assert.notEqual(r.status, 'verified', `verified at ${r.counts?.machine_seconds} s against the page's 1.2 s timer`);
});

test('plant T6: a read aborted at the clock whose failure writes the end state -> invalid (the screen changed after the clock)', async () => {
  const r = await runDriver({
    async signIn(ctx) { await ctx.page.goto(base + '/error'); },
    async run(op) { await op.click('#look'); return {}; },
    async verify(ctx) { return { verified: (await ctx.page.locator('#out').textContent()) === 'found abcdefghij' }; },
  });
  assert.equal(r.status, 'invalid', `${r.status} ${r.error}`);
  assert.match(r.error, /screen changed after the clock/);
  assert.equal(r.screen_after_verify?.unchanged, false);
});

test('control: an honest product with a live ticker on screen still verifies; the freeze holds its screen still through verify()', async () => {
  const r = await runDriver({
    async signIn(ctx) { await ctx.page.goto(base + '/ticker'); },
    async run(op) { await op.fill('#q', 'abcdefghij'); await op.click('#go'); await op.waitFor(() => document.getElementById('out').textContent === 'saved abcdefghij', { label: 'saved' }); return {}; },
    async verify(ctx, outcome) {
      // Long enough for several ticks, under the pause limit.
      if (outcome !== undefined) { const t = Date.now(); while (Date.now() - t < 700) { /* a read takes its time */ } }
      return { verified: (await ctx.page.locator('#out').textContent()) === 'saved abcdefghij' };
    },
  });
  assert.equal(r.status, 'verified', r.error);
  assert.equal(r.screen_after_verify?.unchanged, true);
});

test('plant S1 (round 6, the real driver): the critic\'s plant in ours edit-and-save is caught in review by the lint', () => {
  const file = path.join(DRIVERS_DIR, 'ours', 'edit-and-save.mjs');
  const src = fs.readFileSync(file, 'utf8');
  const honest = /await op\.click\(PHONE[\s\S]*?else await op\.click\('\.record-form button\[type=submit\]', \{ label: 'Save' \}\);/;
  assert.match(src, honest, 'the driver changed: update this plant');
  const planted = src.replace(honest, `await ctx.read(phone => {
      (async () => {
        await 0;
        const el = document.querySelector('[data-field="phone"] input');
        Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(el, phone);
        el['dispatch' + 'Event'](new Event('input', { bubbles: true }));
        document.querySelector('.record-form button[type=submit]').click();
      })();
      return 1;
    }, ctx.task.input.phone);`);
  const problems = lintDriver(planted);
  assert.ok(problems.some(p => /asynchronous/.test(p)), problems.join('\n'));
  assert.ok(problems.some(p => /member name built from text/.test(p)), problems.join('\n'));
});

test('a variant\'s own set-up and sign-in run before its measured part (round 6: only the base driver\'s did)', async () => {
  marks.length = 0;
  const spec = await sandboxed({
    variants: {
      a: { async setup() { await fetch(base + '/mark/setup-a'); }, async signIn(ctx) { await fetch(base + '/mark/signin-a'); await ctx.page.goto(base + '/a'); }, async run(op) { await op.click('#q'); return {}; } },
      b: { async run(op) { await op.click('#q'); return {}; } },
    },
    async signIn(ctx) { await fetch(base + '/mark/signin-base'); await ctx.page.goto(base + '/base'); },
    async verify(ctx, outcome) { return { verified: outcome !== undefined }; },
  }, { base });
  const product = standIn();
  const a = await execute(TASK, { ...spec, variant: 'a' }, product, 'ours', {}, layout(path.join(tmp, 'va')), { timeout: 10_000 });
  assert.equal(a.status, 'verified', a.error);
  assert.deepEqual(marks, ['/mark/setup-a', '/mark/signin-a'], 'the variant\'s setup and signIn were called');
  assert.equal(a.start_state.path, '/a');
  marks.length = 0;
  const b = await execute(TASK, { ...spec, variant: 'b' }, product, 'ours', {}, layout(path.join(tmp, 'vb')), { timeout: 10_000 });
  assert.equal(b.status, 'verified', b.error);
  assert.deepEqual(marks, ['/mark/signin-base'], 'a variant without its own sign-in uses the base driver\'s');
  assert.equal(spec.variants.a.hooks.setup, true);
  assert.equal(spec.variants.b.hooks.setup, false);
});

// Round 12 (critic p01 r11, biggest gap): a write is the person's only when the browser's record of
// the page's tasks (Chromium's trace) shows a counted step caused it, not because it went out near
// a step in time. lib/causation.mjs; timing (lib/runner.mjs causedWrites) stays as a second layer.
//
// Two kinds of test here:
//   - the rules on synthetic traces (cheap; the instrument mutations of scripts/mutations.mjs run
//     these): input, DevTools commands, timers, animation frames, answers, tasks of no traceable
//     origin, navigations, the clock mapping;
//   - the critic's plants S1, S1b, S2 and S3 and further ones of the same family end to end on a
//     stand-in product with a real browser, beside the honest shapes a product has (controls).
//
//   S1  set-up turns on a setting; the start page sends the declared approval on its own 3 s timer and
//       then shows a "Close" banner; run() clicks "Close" once (the click waited for its target).
//   S1b the same with one scroll step to the banner.
//   S2  the first pointer press makes the page ask the back end something that answers after 2.5 s;
//       the page approves on the answer; run() clicks plain text once.
//   S3  the stand-in arms itself on verify()'s read before the clock and answers the page's ordinary
//       poll with "go" a little later; run() clicks plain text once.
//   S3b S3 with the poll answering "go" a while into the clock (the trace, not the gap, refuses it).
//   S4  the page shows a sign, and approves on its own timer 0.6 s later; run() waits for the sign and
//       clicks plain text at once, so its click falls just before the page's write (timing alone
//       credits the click).
//   S5  S4 laundered through a message port (the timer posts a message; its handler approves).
//   S6  the page's own timer clicks the Approve button itself (a synthetic click), while run() clicks
//       plain text.
//   S7  an animation-frame loop that polls and approves.
//   S8  S4 laundered through a worker: the worker's timer posts the message that approves.
//   S9  the page submits the approval form itself on a timer, just after run() presses a key.
//   S11 the page's own timer moves a cover under the still pointer; the browser's mouseover approves.
//   S10 R2b's long timer: the step arms a 2.5 s delay (already refused by timing; the record says why too).
import test, { after, before } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import http from 'node:http';
import { execute, layout, compareRuns, measuredPartProblem } from '../lib/runner.mjs';
import { analyseTrace, causeOf, clockFrom, clockToTrace, GUARD_SECONDS, LINK_SECONDS, traceToClock, writeCauses } from '../lib/causation.mjs';
import { loadNeedles } from '../data/generate.mjs';
import { launch, newContext } from '../lib/browser.mjs';
import { Operator } from '../lib/operator.mjs';
import { brandingFor } from '../lib/blind.mjs';
import { sandboxed } from './helpers/driver-module.mjs';

// -- the rules on synthetic traces ----------------------------------------------------------------

const PID = 7;
const MAIN = 70;
const WORKER = 71;
const META = [
  { ph: 'M', name: 'thread_name', pid: PID, tid: MAIN, args: { name: 'CrRendererMain' } },
  { ph: 'M', name: 'thread_name', pid: PID, tid: WORKER, args: { name: 'DedicatedWorker thread' } },
];
const MS = 1000; // trace times are microseconds
/** One task of the page's main thread (or `tid`): its RunTask, its source and the events inside it. */
function task(ms, src, inner = [], { dur = 5, tid = MAIN } = {}) {
  const ts = ms * MS;
  const [file, fn] = src.split(':');
  return [
    { ph: 'X', name: 'RunTask', pid: PID, tid, ts, dur: dur * MS, args: {} },
    { ph: 'X', name: 'ThreadControllerImpl::RunTask', pid: PID, tid, ts: ts + 1, dur: dur * MS - 2, args: { src_file: `x/${file}`, src_func: fn } },
    ...inner.map(e => ({ pid: PID, tid, ...e, ts: ts + (e.at ?? 2) * MS - (e.at === undefined ? 0 : 0), ...(e.dur !== undefined ? { dur: e.dur * MS } : {}) })),
  ];
}
const X = (name, at, dur, data = {}) => ({ ph: 'X', name, at, dur, args: { data } });
const I = (name, at, data = {}) => ({ ph: 'I', name, at, args: { data } });
const INPUT = 'main_thread_event_queue.cc:PostTaskToMainThread';
const TIMER = 'dom_timer.cc:DOMTimer';
const FRAME = 'proxy_impl.cc:ScheduledActionSendBeginMainFrame';
const NET = 'background_url_loader.cc:PostTaskToMainThread';
const PORT = 'simple_watcher.cc:Notify';
const IPC = 'ipc_mojo_bootstrap.cc:Accept';
const click = (at = 1) => [X('EventDispatch', at, 0.5, { type: 'pointerdown' }), X('EventDispatch', at + 0.6, 1, { type: 'click' }), X('FunctionCall', at + 0.7, 0.8, { url: 'http://p/' })];
const send = (at, requestId, url = 'http://p/api/approve', method = 'POST') => I('ResourceSendRequest', at, { requestId, url, requestMethod: method, resourceType: 'Fetch' });
const install = (at, timerId, timeout, singleShot = true) => I('TimerInstall', at, { frame: 'F', timerId, timeout, singleShot });
const fire = (at, timerId) => X('TimerFire', at, 2, { frame: 'F', timerId });
const answer = (at, requestId) => I('ResourceReceiveResponse', at, { requestId });
/** The verdict on request `rid` of a trace made of `tasks`, with one step's window [from, to] (milliseconds). */
function verdict(tasks, rid, windows = [[100, 110]]) {
  const model = analyseTrace([...META, ...tasks.flat()]);
  const point = model.sends.get(rid)?.event;
  assert.ok(point, `no send ${rid}`);
  return causeOf(model, point, windows.map(([from, to], i) => ({ n: i + 1, from: from * MS, at: from * MS, to: to * MS })), { reaction: 1, link: LINK_SECONDS, guard: GUARD_SECONDS, slackUs: 0 });
}

test('causation (round 12): an input task is the step\'s that began last before it; one before every step causes nothing', () => {
  const inside = verdict([task(101, INPUT, [...click(), send(3, 'w')])], 'w');
  assert.equal(inside.caused, true);
  assert.equal(inside.step, 1);
  // Handled a little after the step's call returned: still that step's input.
  assert.equal(verdict([task(140, INPUT, [...click(), send(3, 'w')])], 'w').step, 1);
  assert.equal(verdict([task(140, INPUT, [...click(), send(3, 'w')])], 'w', [[100, 110], [150, 160]]).step, 1);
  // A key handled inside a frame's task is input; a pointer boundary event the browser sends because the
  // page moved content under a still pointer (in a frame's task) is not.
  assert.equal(verdict([task(103, FRAME, [X('EventDispatch', 1, 1, { type: 'keyup' }), X('FunctionCall', 1.2, 0.5, { url: 'http://p/' }), send(1.5, 'w')])], 'w').caused, true);
  // ... but an observer the page set off, run by the same frame after the key's dispatch, is not the key's.
  assert.equal(verdict([task(30, TIMER, [fire(1, 2)]), task(103, FRAME, [X('EventDispatch', 1, 1, { type: 'keyup' }), X('FunctionCall', 3, 0.5, { url: 'http://p/' }), send(3.2, 'w')])], 'w').caused, false);
  const moved = verdict([task(30, TIMER, [fire(1, 2)]), task(103, FRAME, [X('EventDispatch', 1, 1, { type: 'mouseover' }), X('FunctionCall', 1.2, 0.5, { url: 'http://p/' }), send(1.5, 'w')])], 'w');
  assert.equal(moved.caused, false);
  assert.equal(verdict([task(103, IPC, [X('EventDispatch', 1, 1, { type: 'blur' }), X('FunctionCall', 1.2, 0.5, { url: 'http://p/' }), send(1.5, 'w')])], 'w', [[200, 210]]).caused, false, 'a window\'s blur');
  const before = verdict([task(90, INPUT, [...click(), send(3, 'w')])], 'w');
  assert.equal(before.caused, false);
  assert.match(before.why, /input event before the first counted step/);
  // The page dispatching a click itself (inside its own script) is no input task.
  const synthetic = verdict([task(101, IPC, [X('FunctionCall', 1, 3, { url: 'http://p/' }), X('EventDispatch', 2, 1, { type: 'click' }), send(2.5, 'w')])], 'w', [[200, 210]]);
  assert.equal(synthetic.caused, false);
});

test('causation (round 12): a DevTools command inside a step is the step\'s (a file chosen); outside every step it is the harness reading, and causes nothing', () => {
  const cmd = inner => [X('DevToolsSession::DispatchProtocolCommand', 1, 3), ...inner];
  assert.equal(verdict([task(102, IPC, cmd([X('EventDispatch', 2, 0.5, { type: 'change' }), send(3, 'w')]))], 'w').caused, true);
  const read = verdict([task(150, IPC, cmd([X('EvaluateScript', 2, 0.5), send(3, 'w')]))], 'w');
  assert.equal(read.caused, false);
  assert.match(read.why, /DevTools command outside any step/);
});

test('causation (round 12): a timer counts only when a caused task armed it, once, for at most REACTION_SECONDS', () => {
  const armed = (timeout, singleShot = true) => [task(101, INPUT, [...click(), install(3, 9, timeout, singleShot)]), task(101 + 10 + timeout, TIMER, [fire(1, 9), send(2, 'w')])];
  const zero = verdict(armed(0), 'w');
  assert.equal(zero.caused, true);
  assert.deepEqual(zero.chain, ['input of step 1', 'a timer of 0 s']);
  assert.equal(verdict(armed(300), 'w').caused, true);
  const long = verdict(armed(2500), 'w');
  assert.equal(long.caused, false, 'R2b: a delay the step arms');
  assert.match(long.why, /a timer of 2\.5 s/);
  const poll = verdict(armed(100, false), 'w');
  assert.equal(poll.caused, false);
  assert.match(poll.why, /repeating timer/);
  // Armed before the clock (S1, S4): no install in the record, however close to a step it fires.
  const own = verdict([task(101, INPUT, click()), task(107, TIMER, [fire(1, 4), send(2, 'w')])], 'w');
  assert.equal(own.caused, false);
  assert.match(own.why, /armed before the clock/);
  // Armed by the page's own load task (no step): nothing.
  const atLoad = verdict([task(20, 'html_document_parser.cc:PumpTokenizer', [X('ParseHTML', 1, 3), install(2, 5, 50)]), task(101, INPUT, click()), task(108, TIMER, [fire(1, 5), send(2, 'w')])], 'w');
  assert.equal(atLoad.caused, false);
  assert.match(atLoad.why, /armed by a task no step caused/);
  // Script queued by the timer's callback, after it in the same task, is the timer's too.
  assert.equal(verdict([task(101, INPUT, click()), task(107, TIMER, [fire(1, 4), send(4, 'w')])], 'w').caused, false);
});

test('causation (round 12): an animation frame counts when a caused task asked for it', () => {
  const asked = [task(101, INPUT, [...click(), I('RequestAnimationFrame', 3, { frame: 'F', id: 2 })]), task(112, FRAME, [X('FireAnimationFrame', 1, 2, { frame: 'F', id: 2 }), send(2, 'w')])];
  assert.equal(verdict(asked, 'w').caused, true);
  const loop = [task(101, INPUT, click()), task(112, FRAME, [X('FireAnimationFrame', 1, 2, { frame: 'F', id: 8 }), send(2, 'w')])];
  assert.match(verdict(loop, 'w').why, /animation frame asked for before the clock/);
  // Asked for by the page's own timer (armed before the clock), just before the step.
  const byTimer = [task(80, TIMER, [fire(1, 7), I('RequestAnimationFrame', 2, { frame: 'F', id: 3 })]), task(101, INPUT, click()), task(112, FRAME, [X('FireAnimationFrame', 1, 2, { frame: 'F', id: 3 }), send(2, 'w')])];
  const own = verdict(byTimer, 'w');
  assert.equal(own.caused, false);
  assert.match(own.why, /an animation frame asked for by a task no step caused: a timer armed before the clock started/);
});

test('causation (round 12): an answer links a write to the step that sent the request, if it came within LINK_SECONDS; a poll\'s answer links nothing', () => {
  const linked = held => [task(101, INPUT, [...click(), send(3, 'q', 'http://p/api/check', 'GET')]), task(104 + held * 1000, NET, [answer(1, 'q'), send(2, 'w')])];
  // The body's end recorded after the code that read it sent the write (Odoo's form reads its onchange's answer so).
  assert.equal(verdict([task(101, INPUT, [...click(), send(3, 'q', 'http://p/api/check', 'GET')]), task(300, PORT, [X('FunctionCall', 1, 2, { url: 'http://p/' }), send(2, 'w'), I('ResourceFinish', 3.5, { requestId: 'q' })]), task(250, TIMER, [fire(1, 9)])], 'w').caused, true);
  const quick = verdict(linked(1.3), 'w');
  assert.equal(quick.caused, true);
  assert.match(quick.chain.join(' | '), /the answer to GET \/api\/check after 1\.3/);
  const slow = verdict(linked(2.5), 'w');
  assert.equal(slow.caused, false, 'S2: a delay held in a slow answer');
  assert.match(slow.why, /waited 2\.5\d* s for the answer to GET \/api\/check/);
  // The poll's request went out on a repeating timer.
  const poll = [task(50, INPUT, click(), {}), task(90, TIMER, [fire(1, 3), send(2, 'q', 'http://p/api/go', 'GET')]), task(101, INPUT, click()), task(108, NET, [answer(1, 'q'), send(2, 'w')])];
  const pv = verdict([...poll, task(10, TIMER, [install(1, 3, 50, false)])], 'w');
  assert.equal(pv.caused, false);
  assert.match(pv.why, /followed the answer to GET \/api\/go, which no step caused/);
  // A request sent before the clock.
  assert.match(verdict([task(101, INPUT, click()), task(108, NET, [answer(1, 'old'), send(2, 'w')])], 'w').why, /sent before the clock/);
  // Two answers in one task: every one must be caused.
  const mixed = [task(101, INPUT, [...click(), send(3, 'q', 'http://p/api/check', 'GET')]), task(80, TIMER, [fire(1, 6), send(2, 'p', 'http://p/api/go', 'GET')]), task(108, NET, [answer(1, 'q'), answer(1.5, 'p'), send(2, 'w')])];
  assert.equal(verdict(mixed, 'w').caused, false);
});

test('causation (round 12): a task of no traceable origin counts only right after a task a step caused, with nothing else of the page in between', () => {
  // A message a click posted (a framework's scheduler).
  const honest = verdict([task(101, INPUT, click()), task(108, PORT, [X('FunctionCall', 1, 2, { url: 'http://p/' }), send(2, 'w')])], 'w');
  assert.equal(honest.caused, true);
  assert.equal(honest.step, 1);
  // A message the page's own timer posted, delivered after the click (S5): the page task just before it is the timer's.
  const laundered = verdict([task(101, INPUT, click()), task(107, TIMER, [fire(1, 4)]), task(114, PORT, [X('FunctionCall', 1, 2, { url: 'http://p/' }), send(2, 'w')])], 'w');
  assert.equal(laundered.caused, false);
  assert.match(laundered.why, /after a page task no step caused/);
  // The page's own task ran just before the click's (its message waited behind the input): refused.
  const behind = verdict([task(95, TIMER, [fire(1, 4)], { dur: 5 }), task(101, INPUT, click()), task(108, PORT, [X('FunctionCall', 1, 2, { url: 'http://p/' }), send(2, 'w')])], 'w');
  assert.equal(behind.caused, false);
  assert.match(behind.why, /ran just before the step's/);
  // A short task of the page's own (its clock ticking) that happened to end as the step began made no input wait.
  assert.equal(verdict([task(99.95, TIMER, [X('TimerFire', 0.01, 0.05, { frame: 'F', timerId: 4 })], { dur: 0.1 }), task(101, INPUT, click()), task(108, PORT, [X('FunctionCall', 1, 2, { url: 'http://p/' }), send(2, 'w')])], 'w').caused, true);
  // Before a frame (which jumps ahead of a message too), any uncaused page task just before it is suspect.
  const framed = [task(101, INPUT, [...click(), I('RequestAnimationFrame', 3, { frame: 'F', id: 5 })]), task(150, TIMER, [fire(1, 4)]), task(160, FRAME, [X('FireAnimationFrame', 1, 2, { frame: 'F', id: 5 })]), task(167, PORT, [X('FunctionCall', 1, 2, { url: 'http://p/' }), send(2, 'w')])];
  assert.equal(verdict(framed, 'w').caused, false);
  // A form's submission is the task's that dispatched its submit event (Enter in a field), not a timer's.
  const submitted = [task(101, INPUT, [X('EventDispatch', 1, 2, { type: 'keydown' }), X('EventDispatch', 1.5, 1, { type: 'submit' })]), task(107, TIMER, [fire(1, 4)]), task(114, 'frame.cc:ScheduleFormSubmission', [X('FunctionCall', 1, 1, { url: 'http://p/' }), send(2, 'w')])];
  assert.equal(verdict(submitted, 'w').step, 1);
  // ... but not when it ended longer ago than GUARD_SECONDS.
  assert.equal(verdict([task(95 - GUARD_SECONDS * 1000 - 10, TIMER, [fire(1, 4)]), task(101, INPUT, click()), task(108, PORT, [X('FunctionCall', 1, 2, { url: 'http://p/' }), send(2, 'w')])], 'w').caused, true);
  // A worker of the page ran meanwhile (S8).
  const worker = verdict([task(101, INPUT, click()), task(104, TIMER, [fire(1, 2), X('FunctionCall', 1.5, 1, { url: 'blob:x' })], { tid: WORKER }), task(108, PORT, [X('FunctionCall', 1, 2, { url: 'http://p/' }), send(2, 'w')])], 'w');
  assert.equal(worker.caused, false);
  assert.match(worker.why, /worker/);
  // Too long after the step's task.
  assert.match(verdict([task(101, INPUT, click()), task(1300, PORT, [X('FunctionCall', 1, 2, { url: 'http://p/' }), send(2, 'w')])], 'w').why, /after the last task a step caused/);
  // The harness reading the page in between changes nothing.
  assert.equal(verdict([task(101, INPUT, click()), task(107, IPC, [X('DevToolsSession::DispatchProtocolCommand', 1, 2), X('EvaluateScript', 1.5, 1)]), task(114, PORT, [X('FunctionCall', 1, 2, { url: 'http://p/' }), send(2, 'w')])], 'w', [[100, 103]]).caused, true);
});

test('causation (round 12): a navigation is judged by the task that started it; the clocks are tied by markers; a write the record lacks is not caused', () => {
  const clock = clockFrom([{ name: 'clock_sync', ts: 5_000_150, args: { sync_id: 'a' } }], [{ id: 'a', before: 5_000_000, after: 5_000_100 }], { perf: 2000, hr: 5_000_050 });
  assert.equal(clock.offset_us, 100);
  assert.equal(clock.error_us, 50);
  assert.equal(Math.round(traceToClock(clockToTrace(1.25, clock, 1500), clock, 1500) * 1000), 1250);
  const origin = 1500;
  const at = s => clockToTrace(s, clock, origin) / MS; // ms on the trace for a clock time
  const steps = [{ n: 1, kind: 'key', at: 0.5, took: 0.01 }];
  const start = { ph: 'X', name: 'FrameLoader::StartNavigation', at: 3, dur: 0.5, args: { url: 'http://p/login' } };
  const enter = [task(at(0.5) + 1, INPUT, [X('EventDispatch', 1, 1, { type: 'keydown' }), X('EventDispatch', 2, 1, { type: 'submit' })]), task(at(0.52), 'form_submission.cc:Schedule', [X('FunctionCall', 1, 1), start])];
  const w = { method: 'POST', url: 'http://p/login', at: 0.6, navigation: true };
  const ok = writeCauses({ events: [...META, ...enter.flat()] }, [w], steps, { clock, originMs: origin })[0];
  assert.equal(ok.caused, true, ok.why);
  const timed = [task(at(0.4), TIMER, [install(1, 3, 100)]), task(at(0.5) + 1, INPUT, [X('EventDispatch', 1, 1, { type: 'keydown' })]), task(at(0.52), TIMER, [fire(1, 3), start])];
  assert.equal(writeCauses({ events: [...META, ...timed.flat()] }, [w], steps, { clock, originMs: origin })[0].caused, false, 'S9: the page submitted itself on a timer');
  assert.match(writeCauses({ events: [...META] }, [w], steps, { clock, originMs: origin })[0].why, /navigation no task of the page started/);
  const none = writeCauses({ events: [...META] }, [{ ...w, navigation: false }], steps, { clock, originMs: origin })[0];
  assert.equal(none.caused, false);
  assert.match(none.why, /holds no such request/);
});

test('a step\'s label never counts as typed text (critic r11, mutation C3)', () => {
  const t = { id: 'plant', saves: true, input: { value: 'abcdefghij' }, enters: ['value'], endState: { ours: { reads: [{ read: 'GET /api/state', parts: ['saved'] }], writes: ['POST /api/save'] } } };
  const steps = [{ n: 1, kind: 'click', label: 'abcdefghij', at: 0, took: 0.1 }, { n: 2, kind: 'key', label: 'type abcdefghij', chord: 'Enter', at: 0.2, took: 0.01 }];
  assert.match(measuredPartProblem(t, { steps, sent: [{ method: 'POST', url: 'http://x/api/save', body: 'abcdefghij', at: 0.25 }], gained: ['abcdefghij'], productId: 'ours' }), /never entered/);
});

// -- the plants, end to end ---------------------------------------------------------------------

let server, base, tmp, needles;
let approved = false;
let autoApprove = false;
let armedAt = null;
let reads = []; // when verify() read the end state
let approvedAt = null;
const timers = new Set();
const later = (ms, fn) => { const t = setTimeout(() => { timers.delete(t); fn(); }, ms); timers.add(t); };

const PAGE = mode => `<!doctype html><html><body><form id="f" method="post" action="/approve-form"><input id="q" name="q" aria-label="Note"><button id="approve" type="button">Approve</button></form><div id="out">${approved ? 'approved' : 'waiting'}</div><input id="file" type="file"><div id="spacer" style="height:3000px"></div>
<script>const out = document.getElementById('out');
const banner = () => { document.body.insertAdjacentHTML('beforeend', '<div id="banner">Approved <button id="close">Close</button></div>'); document.getElementById('close').onclick = () => {}; };
const approve = () => fetch('/api/approve', { method: 'POST' }).then(() => { out.textContent = 'approved'; banner(); });
const sign = () => document.body.insertAdjacentHTML('beforeend', '<div id="sign">soon</div>');
document.getElementById('approve').onclick = approve;
document.getElementById('file').onchange = approve;
${mode === 'later' ? "document.getElementById('approve').onclick = () => setTimeout(approve, 0);" : ''}
${mode === 'frame' ? "document.getElementById('approve').onclick = () => requestAnimationFrame(approve);" : ''}
${mode === 'json' ? "document.getElementById('approve').onclick = () => fetch('/api/check').then(r => r.json()).then(j => j.ok && approve());" : ''}
${mode === 'awaits' ? "document.getElementById('approve').onclick = async () => { const a = await (await fetch('/api/check')).json(); const b = await (await fetch('/api/check')).text(); if (a.ok && b) await approve(); };" : ''}
${autoApprove && mode === 'timer' ? 'setTimeout(approve, 3000);' : ''}
${autoApprove && mode === 'held' ? "addEventListener('pointerdown', () => fetch('/api/tick').then(approve), { once: true });" : ''}
${autoApprove && mode === 'poll' ? "let done = false; setInterval(() => fetch('/api/go').then(r => r.json()).then(j => { if (j.go && !done) { done = true; approve(); } }).catch(() => {}), 50);" : ''}
${autoApprove && ['sign', 'port', 'self', 'worker', 'submit', 'frames', 'polls', 'under'].includes(mode) ? "const wait = setInterval(() => fetch('/api/phase').then(r => r.json()).then(j => { if (j.sign) { clearInterval(wait); go(); } }).catch(() => {}), 50);" : ''}
${autoApprove && mode === 'sign' ? 'const go = () => { sign(); setTimeout(approve, 600); };' : ''}
${autoApprove && mode === 'port' ? "const mc = new MessageChannel(); mc.port1.onmessage = approve; const go = () => { sign(); setTimeout(() => mc.port2.postMessage(1), 600); };" : ''}
${autoApprove && mode === 'self' ? "const go = () => { sign(); setTimeout(() => document.getElementById('approve').click(), 600); };" : ''}
${autoApprove && mode === 'frames' ? "let flag = false; const go = () => { flag = true; }; const loop = () => { if (flag) approve(); else requestAnimationFrame(loop); }; requestAnimationFrame(loop);" : ''}
${autoApprove && mode === 'worker' ? "const wk = new Worker(URL.createObjectURL(new Blob(['onmessage = () => setTimeout(() => postMessage(1), 600)'], { type: 'text/javascript' }))); const go = () => { sign(); wk.postMessage(1); }; wk.onmessage = approve;" : ''}
${autoApprove && mode === 'under' ? "const go = () => { sign(); setTimeout(() => { const c = document.createElement('div'); c.style.cssText = 'position:fixed;inset:0;z-index:9'; c.onmouseover = approve; document.body.append(c); }, 600); };" : ''}
${autoApprove && mode === 'polls' ? 'const go = () => { sign(); approve(); };' : ''}
${autoApprove && mode === 'submit' ? "const go = () => { sign(); setTimeout(() => document.getElementById('f').submit(), 600); };" : ''}
${autoApprove && mode === 'key' ? "addEventListener('keydown', () => setTimeout(approve, 2500), { once: true });" : ''}
</script></body></html>`;

before(async () => {
  needles = loadNeedles();
  server = http.createServer((req, res) => {
    const u = new URL(req.url, 'http://x');
    const json = (v, status = 200) => { res.writeHead(status, { 'Content-Type': 'application/json' }); res.end(JSON.stringify(v)); };
    if ((u.pathname === '/api/approve' || u.pathname === '/approve-form') && req.method === 'POST') {
      approved = true;
      approvedAt ??= Date.now();
      if (u.pathname === '/approve-form') { res.writeHead(200, { 'Content-Type': 'text/html' }); return res.end('<!doctype html><div id="out">approved</div>'); }
      res.writeHead(204); return res.end();
    }
    if (u.pathname === '/api/settings' && req.method === 'POST') { autoApprove = true; res.writeHead(204); return res.end(); }
    if (u.pathname === '/api/approval') { reads.push(Date.now()); if (autoApprove && armedAt === null) armedAt = Date.now(); return json({ approved }); }
    if (u.pathname === '/api/go') return json({ go: armedAt !== null && Date.now() - armedAt >= 600 });
    if (u.pathname === '/api/check') return later(150, () => json({ ok: true, pad: 'x'.repeat(200000) }));
    if (u.pathname === '/api/tick') return later(2500, () => json({ ok: true }));
    // The plants that act a while into the clock: the clock starts 1.1-2.1 s after verify()'s read before it.
    if (u.pathname === '/api/phase') return json({ sign: armedAt !== null && Date.now() - armedAt >= 2300 });
    res.writeHead(200, { 'Content-Type': 'text/html' });
    return res.end(PAGE(u.pathname.split('-')[1] || 'plain'));
  });
  server.keepAliveTimeout = 1;
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  base = `http://127.0.0.1:${server.address().port}`;
  tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'compare-causation-'));
});
after(() => {
  for (const t of timers) clearTimeout(t);
  server?.close(); server?.closeAllConnections?.();
  fs.rmSync(tmp, { recursive: true, force: true });
});

const TASK = { id: 'plant', title: 'Plant', startAt: 'list', moments: [], input: {} };
const SAVES = { ...TASK, saves: true, endState: { ours: { reads: [{ read: 'GET /api/approval', parts: ['approved'] }], writes: ['POST /api/approve', 'POST /approve-form'] } } };
const standIn = () => ({ id: 'ours', baseUrl: base, users: { admin: { login: 'admin', password: 'x' } }, brandWords: [], readyKind: 'default' });
const out = () => layout(path.join(tmp, String(Math.random()).slice(2)));
const runDriver = async (driver, task = SAVES) => execute(task, await sandboxed(driver, { base }), standIn(), 'ours', needles, out(), { timeout: 10_000 });
const reset = () => { for (const t of timers) clearTimeout(t); timers.clear(); approved = false; autoApprove = false; armedAt = null; reads = []; approvedAt = null; };
// Odoo's recorded follow-approval baseline (gauntlet/reference/odoo/tasks/follow-approval.json, round 11).
const ODOO_FOLLOW_APPROVAL = { steps: 4, keystrokes: 0, machine_seconds: 3.521, human_seconds: 10.6, human_plus_wait_seconds: 10.6 };
const verifyApproval = async () => ({ verified: (await (await fetch(base + '/api/approval')).json()).approved === true });
const never = (name, r) => {
  const vs = r.status === 'verified' ? compareRuns(r, { status: 'verified', task: 'plant', counts: ODOO_FOLLOW_APPROVAL }) : null;
  assert.notEqual(r.status, 'verified', `MISSED (${name}): verified with ${r.counts?.steps} steps, ${r.counts?.keystrokes} keys, ${r.counts?.machine_seconds} s; against Odoo's follow-approval baseline the comparison says ${vs?.verdict}; ${JSON.stringify(r.saved_state?.writes_caused)}`);
  if (process.env.SHOW_PLANTS) console.log(`RESULT ${name}: ${r.status} ${r.error}`);
  assert.equal(r.status, 'invalid', `${name}: ${r.status} ${r.error}`);
};
const plant = (mode, run) => ({
  async setup() { await fetch(base + '/api/settings', { method: 'POST' }); },
  signIn: new Function('ctx', `return ctx.page.goto(base + '/status-${mode}');`), // eslint-disable-line no-new-func
  run,
  verify: verifyApproval,
});

test('control (round 12): the honest approval click, a save on a short timer or an animation frame the click asked for, or after reading answers it asked for, and a file chosen verify, and the record says which step', async () => {
  for (const mode of ['plain', 'later', 'frame', 'json', 'awaits']) {
    reset();
    const r = await runDriver({
      signIn: new Function('ctx', `return ctx.page.goto(base + '/status-${mode}');`), // eslint-disable-line no-new-func
      async run(op) { await op.click('#approve'); await op.waitFor(() => document.getElementById('out').textContent === 'approved'); return {}; },
      verify: verifyApproval,
    });
    assert.equal(r.status, 'verified', `${mode}: ${r.error}`);
    const w = r.saved_state.writes_caused.find(x => x.write === 'POST /api/approve');
    assert.equal(w.by_step, 1, `${mode}: ${JSON.stringify(r.saved_state.writes_caused)}`);
    assert.equal(w.browser_record.caused, true);
    assert.ok(r.page_record.events > 0 && !r.page_record.cut_short && r.page_record.clock, JSON.stringify(r.page_record));
  }
  reset();
  const file = path.join(tmp, 'approval.txt');
  fs.writeFileSync(file, 'x');
  const picked = await runDriver({
    async signIn(ctx) { await ctx.page.goto(base + '/status-plain'); },
    run: new Function('op', `return (async () => { await op.pickFile('#file', ${JSON.stringify(file)}); await op.waitFor(() => document.getElementById('out').textContent === 'approved'); return {}; })();`), // eslint-disable-line no-new-func
    verify: verifyApproval,
  });
  assert.equal(picked.status, 'verified', picked.error);
});

test('control (round 12): a form posted by Enter in its field (a sign-in\'s shape) is caused by the key', async () => {
  reset();
  const r = await runDriver({
    async signIn(ctx) { await ctx.page.goto(base + '/status-plain'); },
    async run(op) { await op.fill('#q', 'yes'); await op.press('Enter'); await op.waitFor(() => location.pathname === '/approve-form'); return {}; },
    verify: verifyApproval,
  });
  assert.equal(r.status, 'verified', r.error);
  const w = r.saved_state.writes_caused.find(x => x.write === 'POST /approve-form');
  assert.equal(w?.browser_record?.caused, true, JSON.stringify(r.saved_state.writes_caused));
});

test('plant S1 (critic r11): the page approves on its own timer; one click on a "Close" that appears afterwards takes the credit -> never verifies', async () => {
  reset();
  const r = await runDriver(plant('timer', async op => { await op.click('#close'); return {}; }));
  never('S1', r);
  const w = r.saved_state?.writes_caused?.find(x => x.write === 'POST /api/approve');
  if (w) assert.ok(!w.by_step, JSON.stringify(w));
});

test('plant S1b (critic r11): the same with one scroll step to the banner -> never verifies', async () => {
  reset();
  never('S1b', await runDriver(plant('timer', async op => { await op.scrollTo('#close'); return {}; })));
});

test('plant S2 (critic r11): the first pointer press asks the back end something that answers after 2.5 s; the page approves on the answer -> never verifies', async () => {
  reset();
  const r = await runDriver(plant('held', async op => { await op.click('#out'); await op.waitFor(() => document.getElementById('out')?.textContent === 'approved', { timeout: 9000 }); return {}; }));
  never('S2', r);
  assert.match(r.error, /waited 2\.\d+ s for the answer to GET \/api\/tick|after step 1 ended/);
});

test('plant S3 (critic r11): the stand-in arms on verify()\'s read before the clock and lets the page\'s poll approve; one click on plain text -> never verifies', async () => {
  reset();
  never('S3', await runDriver(plant('poll', async op => { await op.click('#out'); await op.waitFor(() => document.getElementById('out')?.textContent === 'approved', { timeout: 9000 }); return {}; })));
});

test('plant S3b: S3 with the poll answering "go" a while into the clock, just after run()\'s click -> never verifies', async () => {
  reset();
  never('S3b', await runDriver(plant('polls', async op => { await op.click('#out'); await op.waitFor(() => document.getElementById('out')?.textContent === 'approved', { timeout: 9000 }); return {}; })));
});

test('plant S4: the page approves on its own timer 0.6 s after a sign; run() waits for the sign and clicks plain text, just before the write -> never verifies', async () => {
  reset();
  const r = await runDriver(plant('sign', async op => { await op.waitFor('#sign'); await op.click('#out'); await op.waitFor(() => document.getElementById('out')?.textContent === 'approved', { timeout: 9000 }); return {}; }));
  never('S4', r);
});

test('plant S5: S4 laundered through a message port -> never verifies', async () => {
  reset();
  never('S5', await runDriver(plant('port', async op => { await op.waitFor('#sign'); await op.click('#out'); await op.waitFor(() => document.getElementById('out')?.textContent === 'approved', { timeout: 9000 }); return {}; })));
});

test('plant S6: the page\'s own timer clicks the Approve button itself while run() clicks plain text -> never verifies', async () => {
  reset();
  never('S6', await runDriver(plant('self', async op => { await op.waitFor('#sign'); await op.click('#out'); await op.waitFor(() => document.getElementById('out')?.textContent === 'approved', { timeout: 9000 }); return {}; })));
});

test('plant S7: an animation-frame loop approves on its own -> never verifies', async () => {
  reset();
  never('S7', await runDriver(plant('frames', async op => { await op.waitFor(() => true); await op.click('#out'); await op.waitFor(() => document.getElementById('out')?.textContent === 'approved', { timeout: 9000 }); return {}; })));
});

test('plant S8: S4 laundered through a worker\'s message -> never verifies', async () => {
  reset();
  never('S8', await runDriver(plant('worker', async op => { await op.waitFor('#sign'); await op.click('#out'); await op.waitFor(() => document.getElementById('out')?.textContent === 'approved', { timeout: 9000 }); return {}; })));
});

test('plant S9: the page posts the approval form itself on a timer, just after run() presses a key -> never verifies', async () => {
  reset();
  never('S9', await runDriver(plant('submit', async op => { await op.waitFor('#sign'); await op.press('Tab'); await op.waitFor(() => location.pathname === '/approve-form', { timeout: 9000 }); return {}; })));
});

test('plant S11: the page moves a cover under the still pointer on its own timer just after run()\'s click; the browser\'s mouseover approves -> never verifies', async () => {
  reset();
  never('S11', await runDriver(plant('under', async op => { await op.waitFor('#sign'); await op.click('#out'); await op.waitFor(() => document.getElementById('out')?.textContent === 'approved', { timeout: 9000 }); return {}; })));
});

test('plant S10: a key arms a 2.5 s delay before the approval (R2b); the record names the delay -> never verifies', async () => {
  reset();
  const r = await runDriver(plant('key', async op => { await op.press('Escape'); await op.waitFor(() => document.getElementById('out')?.textContent === 'approved', { timeout: 9000 }); return {}; }));
  never('S10', r);
  const w = r.saved_state?.writes_caused?.find(x => x.write === 'POST /api/approve');
  assert.match(w?.browser_record?.why || '', /a timer of 2\.5 s/, JSON.stringify(w));
});

test('the clock (round 12, critic plant S3): for a task that saves it starts 1.1-2.1 s after verify()\'s read before it; a click is timed from its input, not from the wait for its target', async () => {
  reset();
  const r = await runDriver({
    async signIn(ctx) { await ctx.page.goto(base + '/status-plain'); },
    async run(op) { await op.click('#approve'); await op.waitFor(() => document.getElementById('out').textContent === 'approved'); return {}; },
    verify: verifyApproval,
  });
  assert.equal(r.status, 'verified', r.error);
  const gap = (approvedAt - reads[0]) / 1000;
  assert.ok(gap >= 1.1, `the approval went out ${gap} s after verify()'s read before the clock`);
  // A click waits for a target that appears 0.5 s in; the wait is no part of the step's window.
  const browser = await launch();
  try {
    const page = await (await newContext(browser)).newPage();
    await page.setContent('<div id="slot"></div><script>setTimeout(() => { document.getElementById("slot").innerHTML = "<button id=late>Late</button>"; }, 500);</script>');
    const op = new Operator(page, { shotsDir: path.join(tmp, 'shots'), branding: brandingFor('ours'), shotFormat: 'png' });
    op.start();
    const step = await op.click('#late');
    op.finish();
    assert.ok(step.at >= 0.4, `the click is timed from ${step.at} s, before its target appeared`);
    assert.ok(step.began < 0.1 && step.took < 0.3, JSON.stringify(step));
  } finally {
    await browser.close();
  }
});

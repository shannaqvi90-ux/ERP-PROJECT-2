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
import crypto from 'node:crypto';
import { execute, layout, compareRuns, measuredPartProblem } from '../lib/runner.mjs';
import { analyseTrace, causeOf, clockFrom, clockToTrace, GUARD_SECONDS, LINK_SECONDS, screenCauses, traceToClock, writeCauses } from '../lib/causation.mjs';
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
  return causeOf(model, point, windows.map(([from, to, kind = 'click'], i) => ({ n: i + 1, kind, from: from * MS, at: from * MS, to: to * MS })), { reaction: 1, link: LINK_SECONDS, guard: GUARD_SECONDS, slackUs: 0 });
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
  assert.equal(verdict([task(102, IPC, cmd([X('EventDispatch', 2, 0.5, { type: 'change' }), send(3, 'w')]))], 'w', [[100, 110, 'file-pick']]).caused, true);
  // Round 13: during a click (input the browser delivers) the harness's commands only read the page.
  const during = verdict([task(102, IPC, cmd([X('EventDispatch', 2, 0.5, { type: 'change' }), send(3, 'w')]))], 'w');
  assert.equal(during.caused, false);
  assert.match(during.why, /DevTools command of the harness during step 1 \(a click/);
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
  // Round 13: a timer longer than REACTION_SECONDS is refused on its own, inside the chain's bound too.
  const longer = verdict(armed(1500), 'w');
  assert.equal(longer.caused, false, 'a 1.5 s delay the step arms');
  assert.match(longer.why, /a timer of 1\.5 s: a delay the page set/);
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

// Round 13 (critic p01 r12, biggest gap): a message whose timing lives outside the page's thread.
const WS = 'simple_watcher.cc:Notify';
const wsTag = { name: 'SimpleWatcher::OnHandleReady', ph: 'X', at: 0.5, dur: 3, args: { chrome_mojo_event_info: { watcher_notify_interface_tag: 'network.mojom.WebSocketClient' } } };
const wsSend = at => I('WebSocketSend', at, { frame: 'F', identifier: 2, dataLength: 5 });
const wsReceive = at => I('WebSocketReceive', at, { frame: 'F', identifier: 2, dataLength: 2 });
const wsMessage = (ms, extra = []) => task(ms, WS, [wsTag, wsReceive(1), X('FunctionCall', 1.2, 1, { url: 'http://p/' }), send(1.5, 'w'), ...extra]);

test('causation (round 13): a web socket\'s message is the answer to the page\'s last frame on it, sent by a caused task within LINK_SECONDS; one the server pushed on its own causes nothing (plant W1)', () => {
  // W1: the server pushes "go" on its own timer; the click just before it sent nothing on the socket.
  const pushed = verdict([task(101, INPUT, click()), wsMessage(108)], 'w');
  assert.equal(pushed.caused, false, 'W1: a push right after a click');
  assert.match(pushed.why, /server pushed over a web socket on which the page sent nothing/);
  // The page asked over the socket on the click; the server answered: the click's (like an HTTP answer).
  const asked = verdict([task(101, INPUT, [...click(), wsSend(3)]), wsMessage(140)], 'w');
  assert.equal(asked.caused, true, asked.why);
  assert.match(asked.chain.join(' | '), /the answer over a web socket after/);
  // ... but not a second message after the server already answered that frame.
  const second = verdict([task(101, INPUT, [...click(), wsSend(3)]), task(120, WS, [wsTag, wsReceive(1)]), wsMessage(140)], 'w');
  assert.equal(second.caused, false);
  assert.match(second.why, /already answered/);
  // ... nor an answer held longer than LINK_SECONDS, nor one to a frame the page's own poll sent.
  assert.match(verdict([task(101, INPUT, [...click(), wsSend(3)]), wsMessage(104 + LINK_SECONDS * 1000 + 300)], 'w').why, /waited 2\.\d+ s for a message over a web socket/);
  const polled = verdict([task(10, TIMER, [install(1, 3, 50, false)]), task(95, TIMER, [fire(1, 3), wsSend(2)]), task(101, INPUT, click()), wsMessage(108)], 'w');
  assert.equal(polled.caused, false);
  assert.match(polled.why, /answering a frame no step caused/);
  // A message of the socket's interface without the receive event (the handshake) is from outside too.
  assert.equal(verdict([task(101, INPUT, click()), task(108, WS, [wsTag, X('FunctionCall', 1.2, 1, { url: 'http://p/' }), send(1.5, 'w')])], 'w').caused, false);
});

test('causation (round 13): a window\'s postMessage is the posting task\'s; one posted from outside the page (another frame, window or worker) causes nothing (plant W2)', () => {
  const POST = 'local_dom_window.cc:SchedulePostMessage';
  const handled = (ms, id) => task(ms, POST, [X('HandlePostMessage', 0.5, 3, { traceId: id }), X('FunctionCall', 1, 2, { url: 'http://p/' }), send(2, 'w')]);
  const own = verdict([task(101, INPUT, [...click(), I('SchedulePostMessage', 3, { traceId: '77' })]), handled(108, '77')], 'w');
  assert.equal(own.caused, true, own.why);
  assert.match(own.chain.join(' | '), /posted to itself/);
  // Posted by the page's own timer armed before the clock, delivered right after the click.
  const timed = verdict([task(101, INPUT, click()), task(80, TIMER, [fire(1, 4), I('SchedulePostMessage', 2, { traceId: '78' })]), handled(108, '78')], 'w');
  assert.equal(timed.caused, false);
  // Posted from another process: no posting task in the record.
  const foreign = verdict([task(101, INPUT, click()), handled(108, '79')], 'w');
  assert.equal(foreign.caused, false);
  assert.match(foreign.why, /posted to the page from outside it/);
  // Messages on the interfaces that carry what another frame, tab, worker or server sent.
  for (const tag of ['blink.mojom.LocalFrame', 'blink.mojom.BroadcastChannelClient', 'blink.mojom.ServiceWorkerContainer', 'network.mojom.WebTransportClient']) {
    const carried = verdict([task(101, INPUT, click()), task(108, IPC, [{ name: 'Receive mojo message', ph: 'X', at: 0.5, dur: 3, args: { chrome_mojo_event_info: { mojo_interface_tag: tag } } }, X('FunctionCall', 1, 2, { url: 'http://p/' }), send(2, 'w')])], 'w');
    assert.equal(carried.caused, false, tag);
    assert.match(carried.why, /from outside the page's thread/, tag);
  }
  // Another renderer's main thread (a cross-site frame's process) ran just before an untraceable message (a port).
  const OTHER = { pid: 8, tid: 80 };
  const frameMeta = { ph: 'M', name: 'thread_name', pid: OTHER.pid, tid: OTHER.tid, args: { name: 'CrRendererMain' } };
  const model = analyseTrace([...META, frameMeta, ...task(101, INPUT, click()).flat(),
    ...task(104, TIMER, [fire(1, 2), X('FunctionCall', 1.5, 1, { url: 'http://q/frame' })]).map(e => ({ ...e, pid: OTHER.pid, tid: OTHER.tid })),
    ...task(108, PORT, [X('FunctionCall', 1, 2, { url: 'http://p/' }), send(2, 'w')])]);
  const other = causeOf(model, model.sends.get('w').event, [{ n: 1, from: 100 * MS, at: 100 * MS, to: 110 * MS }], { reaction: 1, link: LINK_SECONDS, guard: GUARD_SECONDS, slackUs: 0 });
  assert.equal(other.caused, false);
  assert.match(other.why, /another thread of the page's script ran \(CrRendererMain; a worker, another frame or window\)/);
});

test('causation (round 13): a task that delivers what the browser read for the page (a file, a blob, a body) is not refused for a worker\'s activity; a port message is', () => {
  const blobRead = [{ name: 'Receive mojo message', ph: 'X', at: 0.5, dur: 3, args: { chrome_mojo_event_info: { mojo_interface_tag: 'blink.mojom.BlobReaderClient' } } }, X('FunctionCall', 1, 2, { url: 'http://p/' }), send(2, 'w')];
  const busyWorker = task(104, TIMER, [fire(1, 2), X('FunctionCall', 1.5, 1, { url: 'blob:x' })], { tid: WORKER });
  const read = verdict([task(101, INPUT, click()), busyWorker, task(108, 'simple_watcher.cc:Notify', blobRead)], 'w');
  assert.equal(read.caused, true, `Odoo's file upload beside its shared worker: ${read.why}`);
  const port = verdict([task(101, INPUT, click()), busyWorker, task(108, 'simple_watcher.cc:Notify', [{ name: 'SimpleWatcher::OnHandleReady', ph: 'X', at: 0.5, dur: 3, args: { chrome_mojo_event_info: { watcher_notify_interface_tag: 'unknown interface' } } }, X('FunctionCall', 1, 2, { url: 'http://p/' }), send(2, 'w')])], 'w');
  assert.equal(port.caused, false, 'a port message while a worker ran (W4)');
  assert.match(port.why, /another thread of the page's script ran/);
});

test('causation (round 13): the delays of a chain add up; the write must go out within LINK_SECONDS of the step\'s input (plant W3)', () => {
  // W3: a click asks something answered after 1.9 s, then a 0.9 s timer approves: 2.8 s in all.
  const near = [task(101, INPUT, [...click(), send(3, 'q', 'http://p/api/tick19', 'GET')]), task(104 + 1900, NET, [answer(1, 'q'), install(2, 9, 900)]), task(104 + 1900 + 905, TIMER, [fire(1, 9), send(2, 'w')])];
  const w3 = verdict(near, 'w');
  assert.equal(w3.caused, false, 'W3');
  assert.match(w3.why, /after the input of step 1 that began its chain .*add up to more than 2 s/);
  // The same chain within the limit: 1.0 s answer + 0.5 s timer.
  const quick = [task(101, INPUT, [...click(), send(3, 'q', 'http://p/api/check', 'GET')]), task(104 + 1000, NET, [answer(1, 'q'), install(2, 9, 500)]), task(104 + 1000 + 505, TIMER, [fire(1, 9), send(2, 'w')])];
  assert.equal(verdict(quick, 'w').caused, true);
});

test('causation (round 13): an animation\'s, transition\'s or media element\'s event is set by its duration; an idle callback is its asker\'s; an observer while an animation runs causes nothing', () => {
  // W5: a CSS animation started before the clock ends just after the click; its animationend approves.
  const ended = verdict([task(101, INPUT, click()), task(108, FRAME, [X('EventDispatch', 1, 2, { type: 'animationend' }), X('FunctionCall', 1.2, 1.5, { url: 'http://p/' }), send(1.5, 'w')])], 'w');
  assert.equal(ended.caused, false, 'W5');
  assert.match(ended.why, /"animationend" event .* set by its duration/);
  for (const type of ['transitionend', 'timeupdate', 'ended', 'finish']) {
    assert.equal(verdict([task(101, INPUT, click()), task(108, FRAME, [X('EventDispatch', 1, 2, { type }), X('FunctionCall', 1.2, 1.5, { url: 'http://p/' }), send(1.5, 'w')])], 'w').caused, false, type);
  }
  // ... and a message that follows such an event is no step's either.
  assert.equal(verdict([task(101, INPUT, click()), task(105, FRAME, [X('EventDispatch', 1, 2, { type: 'transitionend' }), X('FunctionCall', 1.2, 1.5, { url: 'http://p/' })]), task(110, PORT, [X('FunctionCall', 1, 2, { url: 'http://p/' }), send(2, 'w')])], 'w').caused, false);
  // An idle callback the click asked for is the click's; one asked for before the clock is nobody's.
  const idle = (asker = [task(101, INPUT, [...click(), I('RequestIdleCallback', 3, { frame: 'F', id: 4 })])]) => verdict([...asker, task(130, 'scripted_idle_task_controller.cc:PostSchedulerIdleTask', [X('FireIdleCallback', 1, 3, { frame: 'F', id: 4 }), send(2, 'w')])], 'w');
  assert.equal(idle().caused, true);
  assert.match(idle().chain.join(' | '), /an idle callback/);
  assert.match(idle([task(101, INPUT, click())]).why, /idle callback asked for before the clock/);
  // W6: a resize observer's callback (run as a frame renders) while an animation of the page runs.
  const observer = [task(101, INPUT, click()), task(108, FRAME, [X('UpdateLayoutTree', 0.5, 0.3), X('FunctionCall', 1.2, 1.5, { url: 'http://p/' }), send(1.5, 'w')])];
  assert.equal(verdict(observer, 'w').caused, true, 'no animation: the frame after the click');
  const anim = (from, to) => [{ ph: 'b', name: 'Animation', pid: PID, tid: MAIN, ts: from * MS, id2: { local: '0x1' }, args: {} }, ...(to === null ? [] : [{ ph: 'e', name: 'Animation', pid: PID, tid: MAIN, ts: to * MS, id2: { local: '0x1' }, args: {} }])];
  const running = verdict([...observer, anim(50, null)], 'w');
  assert.equal(running.caused, false, 'W6: an animation running');
  assert.match(running.why, /observer .* while an animation or transition of the page was running/);
  assert.equal(verdict([...observer, anim(50, 107.95)], 'w').caused, false, 'an animation that just ended');
  assert.equal(verdict([...observer, anim(1, 5)], 'w').caused, true, 'an animation over more than GUARD_SECONDS before');
  // Running when the clock started (read from the page) and never seen to end.
  const model = analyseTrace([...META, ...observer.flat()]);
  const atStart = n => causeOf(model, model.sends.get('w').event, [{ n: 1, from: 100 * MS, at: 100 * MS, to: 110 * MS }], { reaction: 1, link: LINK_SECONDS, guard: GUARD_SECONDS, slackUs: 0, animatedAtStart: n });
  assert.equal(atStart(1).caused, false);
  assert.equal(atStart(null).caused, false, 'the page could not be read: counted as running');
  assert.equal(atStart(0).caused, true);
});

test('causation (round 13): ids start again in a new document; a callback is matched with the last request before it, and a chain never runs forward in time', () => {
  // The click arms timer 3; the page reloads later and its load task arms a timer 3 of the new document.
  const reused = [task(101, INPUT, [...click(), install(3, 3, 0)]), task(108, TIMER, [fire(1, 3), send(2, 'w')]), task(300, 'html_document_parser.cc:PumpTokenizer', [X('ParseHTML', 1, 3), install(2, 3, 50)])];
  const v = verdict(reused, 'w');
  assert.equal(v.caused, true, v.why);
  assert.ok(v.chain_seconds === undefined || v.chain_seconds >= 0);
  // ... and the other way round: a load task's timer 5, then the click's timer 5 in a new document.
  const late = [task(20, 'html_document_parser.cc:PumpTokenizer', [X('ParseHTML', 1, 3), install(2, 5, 50)]), task(75, TIMER, [fire(1, 5), send(2, 'w')]), task(101, INPUT, [...click(), install(3, 5, 0)])];
  assert.equal(verdict(late, 'w', [[60, 110]]).caused, false, 'the fire before the click is the load task\'s timer');
  // The same for animation frames.
  const frames = [task(101, INPUT, [...click(), I('RequestAnimationFrame', 3, { frame: 'F', id: 1 })]), task(112, FRAME, [X('FireAnimationFrame', 1, 2, { frame: 'F', id: 1 }), send(2, 'w')]), task(400, TIMER, [fire(1, 9), I('RequestAnimationFrame', 2, { frame: 'F', id: 1 })])];
  assert.equal(verdict(frames, 'w').caused, true);
});

test('causation (round 13): a document is its navigation\'s: one a step started is the step\'s, one started before the clock or by a refresh the page set is nobody\'s (plants W7, W8)', () => {
  const start = (at, url = 'http://p/doc') => ({ ph: 'X', name: 'FrameLoader::StartNavigation', at, dur: 0.5, args: { url } });
  const commit = ms => task(ms, IPC, [{ name: 'Receive mojo message', ph: 'X', at: 0.5, dur: 3, args: { chrome_mojo_event_info: { mojo_interface_tag: 'content.mojom.NavigationClient' } } },
    X('RenderFrameImpl::CommitNavigation', 0.6, 2), send(1, 'nav', 'http://p/doc', 'GET'), answer(1.2, 'nav'), X('EventDispatch', 1.5, 0.2, { type: 'unload' })]);
  const script = ms => task(ms, 'html_document_parser.cc:PumpTokenizer', [X('ParseHTML', 0.5, 3), X('EvaluateScript', 1, 2, { url: 'http://p/doc' }), send(2, 'w')]);
  // A click on a link: the navigation starts in the click's input task.
  const linked = verdict([task(101, INPUT, [...click(), start(2.5)]), commit(130), script(140)], 'w');
  assert.equal(linked.caused, true, linked.why);
  assert.match(linked.chain.join(' | '), /the document \/doc it loaded/);
  // W7: the page asked for the (slow) document before the clock; it arrives just after the click.
  const early = verdict([task(101, INPUT, click()), commit(130), script(140)], 'w');
  assert.equal(early.caused, false, 'W7');
  assert.match(early.why, /navigation started before the clock/);
  // W8: a meta refresh the page set navigates just after the click.
  const refreshed = verdict([task(101, INPUT, click()), task(110, 'http_refresh_scheduler.cc:MaybeStartTimer', [X('HttpRefreshScheduler::NavigateTask', 0.5, 2), start(1)]), commit(130), script(140)], 'w');
  assert.equal(refreshed.caused, false, 'W8');
  assert.match(refreshed.why, /refresh the page set/);
});

test('causation (round 13): the harness reading the page during a click is passed over: a document that arrives then is judged by its navigation', () => {
  const read = ms => task(ms, IPC, [X('DevToolsSession::DispatchProtocolCommand', 0.5, 1), X('EvaluateScript', 0.8, 0.3)]);
  const commit = ms => task(ms, IPC, [{ name: 'Receive mojo message', ph: 'X', at: 0.5, dur: 3, args: { chrome_mojo_event_info: { mojo_interface_tag: 'content.mojom.NavigationClient' } } },
    X('RenderFrameImpl::CommitNavigation', 0.6, 2), send(1, 'nav', 'http://p/doc', 'GET'), answer(1.2, 'nav'), X('EventDispatch', 1.5, 0.2, { type: 'unload' })]);
  const script = ms => task(ms, 'html_document_parser.cc:PumpTokenizer', [X('ParseHTML', 0.5, 3), X('EvaluateScript', 1, 2, { url: 'http://p/doc' }), send(2, 'w')]);
  // The document the page asked for before the clock commits during the click, then the harness reads.
  const v = verdict([task(101, INPUT, click()), commit(108), read(114), script(120)], 'w', [[100, 130]]);
  assert.equal(v.caused, false);
  assert.match(v.why, /navigation started before the clock/);
  // A message after the click's input, with the harness reading in between: still the click's.
  assert.equal(verdict([task(101, INPUT, click()), read(108), task(114, PORT, [X('FunctionCall', 1, 2, { url: 'http://p/' }), send(2, 'w')])], 'w', [[100, 120]]).caused, true);
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

let server, base, frameBase, tmp, needles;
const sockets = new Set();
// One unmasked text frame from the server (a web socket's message).
const wsFrame = text => { const b = Buffer.from(text); return Buffer.concat([Buffer.from([0x81, b.length]), b]); };
const push = text => { for (const k of sockets) { try { k.write(wsFrame(text)); } catch { /* gone */ } } };
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
${autoApprove && ['sign', 'port', 'self', 'worker', 'submit', 'frames', 'polls', 'under', 'anim', 'grow', 'long', 'refresh'].includes(mode) ? "const wait = setInterval(() => fetch('/api/phase').then(r => r.json()).then(j => { if (j.sign) { clearInterval(wait); go(); } }).catch(() => {}), 50);" : ''}
${autoApprove && mode === 'sign' ? 'const go = () => { sign(); setTimeout(approve, 600); };' : ''}
${autoApprove && mode === 'port' ? "const mc = new MessageChannel(); mc.port1.onmessage = approve; const go = () => { sign(); setTimeout(() => mc.port2.postMessage(1), 600); };" : ''}
${autoApprove && mode === 'self' ? "const go = () => { sign(); setTimeout(() => document.getElementById('approve').click(), 600); };" : ''}
${autoApprove && mode === 'frames' ? "let flag = false; const go = () => { flag = true; }; const loop = () => { if (flag) approve(); else requestAnimationFrame(loop); }; requestAnimationFrame(loop);" : ''}
${autoApprove && mode === 'worker' ? "const wk = new Worker(URL.createObjectURL(new Blob(['onmessage = () => setTimeout(() => postMessage(1), 600)'], { type: 'text/javascript' }))); const go = () => { sign(); wk.postMessage(1); }; wk.onmessage = approve;" : ''}
${autoApprove && mode === 'under' ? "const go = () => { sign(); setTimeout(() => { const c = document.createElement('div'); c.style.cssText = 'position:fixed;inset:0;z-index:9'; c.onmouseover = approve; document.body.append(c); }, 600); };" : ''}
${autoApprove && mode === 'polls' ? 'const go = () => { sign(); approve(); };' : ''}
${autoApprove && mode === 'submit' ? "const go = () => { sign(); setTimeout(() => document.getElementById('f').submit(), 600); };" : ''}
${autoApprove && mode === 'key' ? "addEventListener('keydown', () => setTimeout(approve, 2500), { once: true });" : ''}
${autoApprove && mode === 'ws' ? "const ws = new WebSocket(location.origin.replace('http', 'ws') + '/ws'); ws.onmessage = e => { if (e.data === 'show') banner(); if (e.data === 'go') approve(); };" : ''}
${mode === 'wsask' ? "const ws = new WebSocket(location.origin.replace('http', 'ws') + '/ws'); ws.onmessage = e => { if (e.data === 'ok') approve(); }; document.getElementById('approve').onclick = () => ws.send('approve?');" : ''}
${autoApprove && mode === 'iframe' ? "addEventListener('message', e => { if (e.data === 'show') banner(); if (e.data === 'go') approve(); }); document.body.insertAdjacentHTML('beforeend', '<iframe src=\"FRAMEBASE/frame\" style=\"width:10px;height:10px;border:0\"></iframe>');" : ''}
${mode === 'xsite' ? "document.body.insertAdjacentHTML('beforeend', '<iframe src=\"FRAMEBASE/quiet\" style=\"width:10px;height:10px;border:0\"></iframe>');" : ''}
${autoApprove && mode === 'near' ? "document.getElementById('out').addEventListener('pointerdown', () => fetch('/api/tick19').then(() => setTimeout(approve, 900)), { once: true });" : ''}
${autoApprove && mode === 'anim' ? "document.head.insertAdjacentHTML('beforeend', '<style>@keyframes fade { from { opacity: 1 } to { opacity: 0.6 } } .fading { animation: fade 0.6s 1; }</style>'); out.addEventListener('animationend', approve); const go = () => { sign(); out.classList.add('fading'); };" : ''}
${autoApprove && mode === 'grow' ? "document.head.insertAdjacentHTML('beforeend', '<style>@keyframes grow { from { width: 10px } to { width: 300px } } .growing { animation: grow 0.6s 1 forwards; }</style>'); document.body.insertAdjacentHTML('beforeend', '<div id=\"bar\" style=\"width:10px;height:4px\"></div>'); const bar = document.getElementById('bar'); let done = false; new ResizeObserver(() => { if (!done && bar.getBoundingClientRect().width >= 299) { done = true; approve(); } }).observe(bar); const go = () => { sign(); bar.classList.add('growing'); };" : ''}
${autoApprove && mode === 'long' ? "document.head.insertAdjacentHTML('beforeend', '<style>@keyframes long { from { width: 10px } to { width: 2010px } } #bar { animation: long 20s linear 1 forwards; }</style>'); document.body.insertAdjacentHTML('afterbegin', '<div id=\"bar\" style=\"height:4px\"></div>'); const bar = document.getElementById('bar'); let from = null; new ResizeObserver(() => { if (from !== null && bar.getBoundingClientRect().width >= from + 60) { from = null; approve(); } }).observe(bar); const go = () => { sign(); from = bar.getBoundingClientRect().width; };" : ''}
${autoApprove && mode === 'slowdoc' ? "document.body.insertAdjacentHTML('beforeend', '<iframe id=\"slot\" style=\"width:10px;height:10px;border:0\"></iframe>'); const nav = setInterval(() => fetch('/api/go').then(r => r.json()).then(j => { if (j.go) { clearInterval(nav); document.getElementById('slot').src = '/slow-doc'; } }).catch(() => {}), 50);" : ''}
${autoApprove && mode === 'refresh' ? "const go = () => { sign(); document.head.insertAdjacentHTML('beforeend', '<meta http-equiv=\"refresh\" content=\"1;url=/approve-doc\">'); };" : ''}
${mode === 'link' ? "document.body.insertAdjacentHTML('beforeend', '<a id=\"golink\" href=\"/approve-doc\">Approve on its page</a>');" : ''}
${autoApprove && mode === 'shared' ? "const sw = new SharedWorker('/shared.js'); sw.port.onmessage = e => { if (e.data === 'show') sign(); if (e.data === 'go') approve(); }; sw.port.start();" : ''}
</script></body></html>`;

// W2: a cross-site frame (its own renderer process) that waits for the stand-in's sign, then posts
// "show" and, on its own 500 ms timer, "go" to the page.
const CROSS_FRAME = `<!doctype html><html><body><script>
const poll = setInterval(() => fetch('/api/phase').then(r => r.json()).then(j => { if (j.sign) { clearInterval(poll); parent.postMessage('show', '*'); setTimeout(() => parent.postMessage('go', '*'), 500); } }).catch(() => {}), 50);
</script></body></html>`;
// W4: a shared worker that does the same through its port.
const SHARED = `onconnect = e => { const port = e.ports[0];
  const poll = setInterval(() => fetch('/api/phase').then(r => r.json()).then(j => { if (j.sign) { clearInterval(poll); port.postMessage('show'); setTimeout(() => port.postMessage('go'), 600); } }).catch(() => {}), 50); };`;

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
    if (u.pathname === '/api/approval') {
      reads.push(Date.now());
      if (autoApprove && armedAt === null) {
        armedAt = Date.now();
        // W1: the stand-in's own timer pushes "show" a while into the clock, then "go" 500 ms later.
        later(2300, () => push('show'));
        later(2800, () => push('go'));
      }
      return json({ approved });
    }
    // W7: a document the page asks for before the clock, held until a while into it, that approves as it loads.
    if (u.pathname === '/slow-doc') {
      const hold = setInterval(() => {
        if (armedAt === null || Date.now() < armedAt + 2800) return;
        clearInterval(hold);
        res.writeHead(200, { 'Content-Type': 'text/html' });
        res.end('<!doctype html><div>x</div><script>fetch("/api/approve", { method: "POST" }).then(() => { parent.document.getElementById("out").textContent = "approved"; });</script>');
      }, 20);
      timers.add(hold);
      return undefined;
    }
    if (u.pathname === '/approve-doc') { res.writeHead(200, { 'Content-Type': 'text/html' }); return res.end('<!doctype html><div id="out">x</div><script>fetch("/api/approve", { method: "POST" }).then(() => { document.getElementById("out").textContent = "approved"; });</script>'); }
    if (u.pathname === '/api/tick19') return later(1900, () => json({ ok: true }));
    if (u.pathname === '/frame' || u.pathname === '/quiet') { res.writeHead(200, { 'Content-Type': 'text/html', 'Access-Control-Allow-Origin': '*' }); return res.end(u.pathname === '/frame' ? CROSS_FRAME : '<!doctype html><p>frame</p>'); }
    if (u.pathname === '/shared.js') { res.writeHead(200, { 'Content-Type': 'text/javascript' }); return res.end(SHARED); }
    if (u.pathname === '/api/go') return json({ go: armedAt !== null && Date.now() - armedAt >= 600 });
    if (u.pathname === '/api/check') return later(150, () => json({ ok: true, pad: 'x'.repeat(200000) }));
    if (u.pathname === '/api/tick') return later(2500, () => json({ ok: true }));
    // The plants that act a while into the clock: the clock starts 1.1-2.1 s after verify()'s read before it.
    if (u.pathname === '/api/phase') { res.setHeader('Access-Control-Allow-Origin', '*'); return json({ sign: armedAt !== null && Date.now() - armedAt >= 2300 }); }
    res.writeHead(200, { 'Content-Type': 'text/html' });
    return res.end(PAGE(u.pathname.split('-')[1] || 'plain').replace(/FRAMEBASE/g, frameBase));
  });
  server.on('upgrade', (req, socket) => {
    const accept = crypto.createHash('sha1').update(req.headers['sec-websocket-key'] + '258EAFA5-E914-47DA-95CA-C5AB0DC85B11').digest('base64');
    socket.write(`HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: ${accept}\r\n\r\n`);
    sockets.add(socket);
    // The honest control asks over the socket; the server answers at once.
    socket.on('data', () => { try { socket.write(wsFrame('ok')); } catch { /* gone */ } });
    socket.on('error', () => sockets.delete(socket));
    socket.on('close', () => sockets.delete(socket));
  });
  server.keepAliveTimeout = 1;
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  base = `http://127.0.0.1:${server.address().port}`;
  // Another site (localhost, not 127.0.0.1): a frame from it gets a renderer process of its own.
  frameBase = `http://localhost:${server.address().port}`;
  tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'compare-causation-'));
});
after(() => {
  for (const t of timers) clearTimeout(t);
  for (const k of sockets) k.destroy();
  server?.close(); server?.closeAllConnections?.();
  fs.rmSync(tmp, { recursive: true, force: true });
});

const TASK = { id: 'plant', title: 'Plant', startAt: 'list', moments: [], input: {} };
const SAVES = { ...TASK, saves: true, endState: { ours: { reads: [{ read: 'GET /api/approval', parts: ['approved'] }], writes: ['POST /api/approve', 'POST /approve-form'] } } };
const standIn = () => ({ id: 'ours', baseUrl: base, users: { admin: { login: 'admin', password: 'x' } }, brandWords: [], readyKind: 'default' });
const out = () => layout(path.join(tmp, String(Math.random()).slice(2)));
const runDriver = async (driver, task = SAVES) => execute(task, await sandboxed(driver, { base }), standIn(), 'ours', needles, out(), { timeout: 10_000 });
const reset = () => { for (const t of timers) { clearTimeout(t); clearInterval(t); } timers.clear(); approved = false; autoApprove = false; armedAt = null; reads = []; approvedAt = null; };
// Odoo's recorded follow-approval baseline (gauntlet/reference/odoo/tasks/follow-approval.json, round 11).
const ODOO_FOLLOW_APPROVAL = { steps: 4, keystrokes: 0, machine_seconds: 3.521, human_seconds: 10.6, human_plus_wait_seconds: 10.6 };
const verifyApproval = async () => ({ verified: (await (await fetch(base + '/api/approval')).json()).approved === true });
const never = (name, r) => {
  const vs = r.status === 'verified' ? compareRuns(r, { status: 'verified', task: 'plant', counts: ODOO_FOLLOW_APPROVAL }) : null;
  assert.notEqual(r.status, 'verified', `MISSED (${name}): verified with ${r.counts?.steps} steps, ${r.counts?.keystrokes} keys, ${r.counts?.machine_seconds} s; against Odoo's follow-approval baseline the comparison says ${vs?.verdict}; ${JSON.stringify(r.saved_state?.writes_caused)}`);
  if (process.env.SHOW_PLANTS) console.log(`RESULT ${name}: ${r.status} ${r.error}`);
  assert.equal(r.status, 'invalid', `${name}: ${r.status} ${r.error}`);
};
/** Why the browser's record says no step caused the approval (whatever the timing layer said first). */
const recordWhy = r => r.saved_state?.writes_caused?.find(x => x.write === 'POST /api/approve')?.browser_record?.why || r.error || '';
/**
 * The record's reason, when the approval went out inside the clock: on a loaded machine the stand-in's
 * own timer may fire before the clock starts, and the run is then refused for that instead.
 */
const assertRecordWhy = (r, re) => {
  if (r.saved_state?.writes_caused?.some(x => x.write === 'POST /api/approve')) assert.match(recordWhy(r), re);
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

// -- round 13 (critic p01 r12, biggest gap): timing that lives outside the page's thread ------------

test('control (round 13): a click that asks over a web socket and approves on the server\'s answer, and a click on a page that embeds a cross-site frame, verify', async () => {
  for (const mode of ['wsask', 'xsite']) {
    reset();
    const r = await runDriver({
      signIn: new Function('ctx', `return ctx.page.goto(base + '/status-${mode}').then(() => ctx.page.waitForTimeout(300));`), // eslint-disable-line no-new-func
      async run(op) { await op.click('#approve'); await op.waitFor(() => document.getElementById('out').textContent === 'approved'); return {}; },
      verify: verifyApproval,
    });
    assert.equal(r.status, 'verified', `${mode}: ${r.error}`);
    const w = r.saved_state.writes_caused.find(x => x.write === 'POST /api/approve');
    assert.equal(w.browser_record.caused, true, `${mode}: ${JSON.stringify(w)}`);
    if (mode === 'wsask') assert.match(w.browser_record.chain.join(' | '), /the answer over a web socket/);
  }
});

test('plant W1 (critic r12): the stand-in pushes "go" over a web socket on its own timer; one click on a Close that appeared just before takes the credit -> never verifies', async () => {
  reset();
  // The first Close: on a loaded machine the page's own "go" (and the banner its approval adds) may
  // come before the click, which then finds two; the run is refused either way.
  const r = await runDriver(plant('ws', async op => { await op.click('#close >> nth=0'); await op.waitFor(() => document.getElementById('out')?.textContent === 'approved', { timeout: 9000 }); return {}; }));
  never('W1', r);
  assertRecordWhy(r, /web socket/);
});

test('plant W2 (critic r12): a cross-site frame posts "go" to the page on its own timer; one click on Close takes the credit -> never verifies (and the page can be measured)', async () => {
  reset();
  // The first Close: on a loaded machine the page's own "go" (and the banner its approval adds) may
  // come before the click, which then finds two; the run is refused either way.
  const r = await runDriver(plant('iframe', async op => { await op.click('#close >> nth=0'); await op.waitFor(() => document.getElementById('out')?.textContent === 'approved', { timeout: 9000 }); return {}; }));
  never('W2', r);
  assert.doesNotMatch(r.error, /could not arm its read world/);
  assertRecordWhy(r, /outside the page's thread|outside it|another thread of the page's script/);
});

test('plant W3 (critic r12): one click on plain text arms a 2.8 s delay (a 1.9 s answer, then a 0.9 s timer) before the approval -> never verifies', async () => {
  reset();
  const r = await runDriver(plant('near', async op => { await op.click('#out'); await op.waitFor(() => document.getElementById('out')?.textContent === 'approved', { timeout: 9000 }); return {}; }));
  never('W3', r);
  assertRecordWhy(r, /add up to more than 2 s/);
});

test('plant W4: a shared worker posts "go" through its port on its own timer, just after run()\'s click -> never verifies', async () => {
  reset();
  never('W4', await runDriver(plant('shared', async op => { await op.waitFor('#sign'); await op.click('#out'); await op.waitFor(() => document.getElementById('out')?.textContent === 'approved', { timeout: 9000 }); return {}; })));
});

test('plant W5: a CSS animation the page starts on its own ends just after run()\'s click; its animationend approves -> never verifies', async () => {
  reset();
  never('W5', await runDriver(plant('anim', async op => { await op.waitFor('#sign'); await op.waitFor(() => true); await op.click('#q'); await op.waitFor(() => document.getElementById('out')?.textContent === 'approved', { timeout: 9000 }); return {}; })));
});

test('plant W6: a resize observer approves when an animation the page started on its own reaches its end, just after run()\'s click -> never verifies', async () => {
  reset();
  const r = await runDriver(plant('grow', async op => { await op.waitFor('#sign'); await op.waitFor(() => true); await op.click('#q'); await op.waitFor(() => document.getElementById('out')?.textContent === 'approved', { timeout: 9000 }); return {}; }));
  never('W6', r);
});

test('plant W6b: W6 with an animation that started before the clock and runs on past it (the record shows neither end) -> never verifies', async () => {
  reset();
  const r = await runDriver(plant('long', async op => { await op.waitFor('#sign'); await op.waitFor(() => true); await op.click('#q'); await op.waitFor(() => document.getElementById('out')?.textContent === 'approved', { timeout: 9000 }); return {}; }));
  never('W6b', r);
  assert.ok(r.animations_at_start >= 1, `animations running at the start: ${r.animations_at_start}`);
});

test('control (round 13): a click on a link to a page that approves as it loads verifies', async () => {
  reset();
  const r = await runDriver({
    signIn: new Function('ctx', "return ctx.page.goto(base + '/status-link');"), // eslint-disable-line no-new-func
    async run(op) { await op.click('#golink'); await op.waitFor(() => document.getElementById('out')?.textContent === 'approved'); return {}; },
    verify: verifyApproval,
  });
  assert.equal(r.status, 'verified', r.error);
  const w = r.saved_state.writes_caused.find(x => x.write === 'POST /api/approve');
  assert.match(w.browser_record.chain.join(' | '), /the document \/approve-doc it loaded/, JSON.stringify(w));
});

test('plant W7: a document the page asked for before the clock (held by the stand-in) arrives just after run()\'s click and approves -> never verifies', async () => {
  reset();
  const r = await runDriver(plant('slowdoc', async op => { await op.click('#q'); await op.waitFor(() => document.getElementById('out')?.textContent === 'approved', { timeout: 9000 }); return {}; }));
  never('W7', r);
  // Round 14: the record now starts before verify()'s read, so it may hold the start of that navigation
  // too; then it names what started it before the clock (the page's own timer) instead.
  assertRecordWhy(r, /navigation started before the clock|navigation no step caused|armed before the clock/);
});

test('plant W8: a meta refresh the page set loads a page that approves, just after run()\'s click -> never verifies', async () => {
  reset();
  const r = await runDriver(plant('refresh', async op => { await op.waitFor('#sign'); await op.waitFor(() => true); await op.click('#q'); await op.waitFor(() => document.getElementById('out')?.textContent === 'approved', { timeout: 9000 }); return {}; }));
  never('W8', r);
});

// -- round 14 (critic p01 r13) ---------------------------------------------------------------------

test('causation (round 14): a click the browser delivers while running a DevTools command is still the step\'s input (critic r13, load)', () => {
  const cmd = inner => [X('DevToolsSession::DispatchProtocolCommand', 1, 3), ...inner];
  // The click's handler (inside the click's dispatch) arms a 0 ms timer that saves.
  const delivered = verdict([task(102, IPC, cmd([X('EventDispatch', 1.5, 1, { type: 'click' }), X('FunctionCall', 1.6, 0.8, { url: 'http://p/' }), install(1.8, 5, 0)])), task(109, TIMER, [fire(1, 5), send(2, 'w')])], 'w');
  assert.equal(delivered.caused, true, delivered.why);
  assert.equal(delivered.step, 1);
  // The page's own script dispatching a click inside a command task (a synthetic click) is no input.
  const synthetic = verdict([task(102, IPC, cmd([X('FunctionCall', 1.2, 2, { url: 'http://p/' }), X('EventDispatch', 1.5, 1, { type: 'click' }), install(1.8, 5, 0)])), task(109, TIMER, [fire(1, 5), send(2, 'w')])], 'w');
  assert.equal(synthetic.caused, false);
  // A boundary event (mouseover) inside a command task is not discrete input.
  const boundary = verdict([task(102, IPC, cmd([X('EventDispatch', 1.5, 1, { type: 'mouseover' }), X('FunctionCall', 1.6, 0.8, { url: 'http://p/' }), install(1.8, 5, 0)])), task(109, TIMER, [fire(1, 5), send(2, 'w')])], 'w');
  assert.equal(boundary.caused, false);
});

const CLOCK0 = { offset_us: 0, error_us: 0, perf_ms: 0, hr_us: 0 };
const change = (at, name = 'StyleRecalcInvalidationTracking', data = {}) => I(name, at, { nodeName: 'DIV', reason: 'Node was inserted into tree', ...data });
/** screenCauses on synthetic tasks; steps are [at, took, kind] in milliseconds of the clock (= trace ms). */
function screens(tasks, steps = [[100, 10, 'click']], until = 1000, harness = []) {
  return screenCauses({ events: [...META, ...tasks.flat()] }, steps.map(([at, took, kind = 'click'], i) => ({ n: i + 1, kind, at: at / 1000, took: took / 1000 })),
    { clock: CLOCK0, originMs: 0, until: until / 1000, harness: harness.map(([from, to]) => ({ from: from / 1000, to: to / 1000 })) });
}

test('causation (round 14): every change of the screen is judged by where its chain begins (screenCauses)', () => {
  // A step's input that changes the screen; a timer it arms (any delay, even repeating) that changes it later.
  const caused = screens([task(101, INPUT, [...click(), change(2), install(3, 8, 3000), install(3.5, 9, 200, false)]), task(3200, TIMER, [fire(1, 8), change(2)]), task(400, TIMER, [fire(1, 9), change(2)])], [[100, 10]], 4000);
  assert.deepEqual(caused.map(c => c.caused), [true, true, true]);
  // A timer armed before the clock (no install in the record) that changes the screen: no step's.
  const own = screens([task(101, INPUT, click()), task(150, TIMER, [fire(1, 4), change(2)])]);
  assert.equal(own.length, 1);
  assert.equal(own[0].caused, false);
  assert.match(own[0].why, /armed before the clock/);
  // Before the clock (a change of the start page after verify()'s read): nobody's.
  const early = screens([task(-500, TIMER, [fire(1, 4), change(2)])]);
  assert.equal(early[0].caused, false);
  assert.ok(early[0].at < 0);
  // An answer to a request the step sent, however late (a slow search), is the step's.
  const slow = screens([task(101, INPUT, [...click(), send(3, 'r', 'http://p/api/search', 'GET')]), task(3500, NET, [I('ResourceReceiveResponse', 1, { requestId: 'r' }), X('FunctionCall', 1.5, 1, { url: 'http://p/' }), change(2)])], [[100, 10]], 4000);
  assert.equal(slow[0].caused, true, slow[0].why);
});

test('causation (round 14): what the harness does (its reads, the blind shot) is passed over; page script it sets off is judged', () => {
  const cmd = inner => [X('DevToolsSession::DispatchProtocolCommand', 1, 3), ...inner];
  const SHOT = [[295, 310]];
  // The harness neutralising the title for a shot (inside the shot's window): no page script ran.
  const title = () => [task(300, IPC, cmd([X('EvaluateScript', 1.2, 1), change(1.5, 'LayoutInvalidationTracking', { nodeName: '#text', reason: 'Text changed' })]))];
  assert.equal(screens(title(), undefined, undefined, SHOT).length, 0);
  // The same change inside a harness command at any other time: the page's own script ran inside the
  // harness's read (a replaced getter, plant N1f), and is no step's.
  const read = screens(title());
  assert.equal(read.length, 1);
  assert.equal(read[0].caused, false);
  // A page observer that ran on the harness's shot change and opened the record (plant N1e).
  const reacted = screens([task(300, IPC, cmd([X('EvaluateScript', 1.2, 0.4), X('FunctionCall', 1.7, 1, { url: 'http://p/app.js' }), change(2)]))], undefined, undefined, SHOT);
  assert.equal(reacted.length, 1);
  assert.equal(reacted[0].caused, false);
  // The browser's own change of the window (a widget message, no script, no answer): passed over.
  assert.equal(screens([task(300, IPC, [I('Receive mojo message', 0.5, {}), change(1)])]).length, 0);
});

test('causation (round 14): a document is judged by the navigation that loaded it, in whatever task its parser ran', () => {
  const parse = (at, url) => ({ ph: 'X', name: 'ParseHTML', at, dur: 0.5, args: { beginData: { url, frame: 'F' } } });
  const nav = (at, url) => ({ ph: 'X', name: 'FrameLoader::StartNavigation', at, dur: 0.2, args: { url } });
  const byClick = screens([task(101, INPUT, [...click(), nav(2, 'http://p/contact/1')]), task(160, PORT, [parse(1, 'http://p/contact/1'), change(1.2)])]);
  assert.ok(byClick.length >= 1);
  assert.ok(byClick.every(c => c.caused), JSON.stringify(byClick));
  const byTimer = screens([task(101, INPUT, click()), task(150, TIMER, [fire(1, 4), nav(1.5, 'http://p/contact/1')]), task(160, PORT, [parse(1, 'http://p/contact/1')])]);
  assert.ok(byTimer.length >= 1);
  assert.ok(byTimer.every(c => !c.caused));
});

test('causation (round 14): a favicon or image the browser fetched before the clock, answered just before an untraceable task, does not stand between it and the step (a busy machine)', () => {
  const sendOf = (at, id, type, url) => I('ResourceSendRequest', at, { requestId: id, url, requestMethod: 'GET', resourceType: type });
  const ans = (at, id) => I('ResourceReceiveResponse', at, { requestId: id });
  const checkpoint = (at, inner = []) => [X('BlinkScheduler_PerformMicrotaskCheckpoint', at, 0.5), ...inner];
  // Before the clock the page asked for the favicon (and the step's click asked for its data); the
  // favicon's answer lands between the data's answer and the scheduler's message that renders.
  const tasks = (iconInner = []) => [
    task(50, NET, [sendOf(2, 'icon', 'Other', 'http://p/favicon.ico')]),
    task(101, INPUT, [...click(), send(3, 'd', 'http://p/api/data', 'GET')]),
    task(120, NET, [answer(1, 'd'), X('FunctionCall', 1.5, 1, { url: 'http://p/' })]),
    task(130, NET, [answer(1, 'icon'), ...checkpoint(2, iconInner)]),
    task(137, PORT, [X('FunctionCall', 1, 2, { url: 'http://p/' }), send(2, 'w')]),
  ];
  const passive = verdict(tasks(), 'w');
  assert.equal(passive.caused, true, passive.why);
  // A font whose promise ran the page's code (it posted a message) stands between them: no step's.
  const active = verdict(tasks([I('SchedulePostMessage', 2.2, { traceId: 9 })]), 'w');
  assert.equal(active.caused, false);
});

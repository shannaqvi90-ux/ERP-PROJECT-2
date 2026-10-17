// Causal evidence for the page's writes (round 12, critic p01 r11, biggest gap).
//
// Round 11 judged "a counted step caused this write" by time alone: a write that went out while a
// step was performed, or within REACTION_SECONDS after it (or after the answer to a request a step
// sent), counted as the person's. The critic planted a page that sends the task's write on its own
// timer while a click waits for its target (S1, S1b), one that holds the write behind a slow answer
// to a request the first pointer press sent (S2), and one that lets its ordinary poll approve 0.6 s
// after the product saw verify()'s read before the clock (S3). Each was recorded as verified with one
// step. Proximity in time is not evidence that a step caused a write.
//
// The evidence now comes from the browser itself. While a task that saves is measured, the harness
// records Chromium's trace of the page (the record DevTools' performance panel is made from): every
// task the page's main thread ran, what kind of task it was (an input event the browser delivered, a
// timer firing, an animation frame, the answer to a request, a DevTools command), the timers and
// animation frames each task armed (with their delays), and the requests each task sent. A write is
// the person's only when the chain of tasks that sent it leads back to a counted step:
//
//   input     a task that delivered an input event (dispatched by the browser, outside any script:
//             never the page's doing) is the step's that last began before it: only the harness's
//             steps put input in the page (from the moment the operator began a step, the pointer
//             moving to its target included);
//   command   a DevTools command task inside a step (a file chosen, the device's answer) likewise;
//             outside every step it is the harness reading the page, and causes nothing;
//   timer     a timer's task is caused when the task that armed the timer was, the timer fires once
//             and its delay is at most REACTION_SECONDS (a scheduler's setTimeout 0, a short
//             debounce); a repeating timer (a poll) or one armed before the clock causes nothing;
//   frame     an animation frame callback is caused when the task that asked for it was;
//   answer    a task that delivered the answer to a request (the page's code waiting on it runs
//             there) is caused when that request was sent by a caused task and was answered within
//             LINK_SECONDS (round 12, plant S2: a click that "approves" 2.5 s later behind a slow
//             answer it asked for is a delay, as R2b's timer was); a task delivering several answers
//             is caused only when every one of them is;
//   other     a task of no traceable origin (a message port, a worker's message, an observer, a
//             scheduled form submission) is caused only when the page task just before it was caused
//             by a step, it began within REACTION_SECONDS of that task's end, no page task that
//             caused nothing ended within GUARD_SECONDS before that task began (one that ran just
//             before the step could have posted the message), and no worker of the page ran meanwhile.
//
// A navigation (a form posted) is judged by the task that started it. The trace's clock is checked
// against the harness's with clock-sync markers. The rules are written as a pure function of the
// trace (analyseTrace, writeCauses), unit-tested on synthetic traces (test/causation.test.mjs) and
// end to end on planted pages. Timing (lib/runner.mjs causedWrites) stays as a second layer: a
// write counts only when both say a step caused it.
import crypto from 'node:crypto';

/** Trace categories: tasks, their kinds and sources, DevTools commands, navigations. */
export const TRACE_CATEGORIES = Object.freeze([
  'devtools.timeline', 'disabled-by-default-devtools.timeline', 'toplevel', 'devtools', 'navigation', 'blink.user_timing', 'loading',
]);
/** An answer the page waited on for longer than this before it sent its write links nothing (plant S2). */
export const LINK_SECONDS = 2;
/** A page task that caused nothing and ended this close before a step's task could have posted what follows it. */
export const GUARD_SECONDS = 0.1;
/** Input events a step delivers (the outermost event of an input task). */
const INPUT_EVENTS = /^(pointer(down|up|move|over|out|enter|leave|cancel|rawupdate)|mouse(down|up|move|over|out|enter|leave)|click|dblclick|auxclick|contextmenu|key(down|up|press)|wheel|touch(start|end|move|cancel)|beforeinput|input|textInput|change|composition(start|update|end)|submit|formdata|paste|copy|cut|drag\w*|drop|DOMActivate|focus|blur|focusin|focusout|DOMFocusIn|DOMFocusOut)$/;
/**
 * Input events the browser only dispatches for input it was given (a key, a press, a release, text):
 * never on its own. The browser does dispatch boundary and move events (mouseover, pointermove ...)
 * when the page changes under a still pointer, and focus and blur when windows come and go, so those
 * count as a step's input only in a task of the input queue.
 */
const DISCRETE_INPUT = /^(key(down|up|press)|pointer(down|up)|mouse(down|up)|click|dblclick|auxclick|contextmenu|beforeinput|input|textInput|composition(start|update|end)|paste|copy|cut|drop)$/;

const hrUs = () => Number(process.hrtime.bigint() / 1000n);

/**
 * The trace of one page while a task is measured. start() before the clock, stop() after it.
 * Clock-sync markers placed at both ends tie the trace's clock to the harness's (performance.now).
 */
export class PageTrace {
  #cdp = null; #events = []; #complete = null; #marks = []; #perfAtHr = null;

  static async start(page, { bufferKb = 256 * 1024 } = {}) {
    const t = new PageTrace();
    t.#cdp = await page.context().newCDPSession(page);
    t.#cdp.on('Tracing.dataCollected', e => { for (const ev of e.value) t.#events.push(ev); });
    t.#complete = new Promise(resolve => t.#cdp.once('Tracing.tracingComplete', resolve));
    await t.#cdp.send('Tracing.start', {
      traceConfig: { includedCategories: [...TRACE_CATEGORIES], recordMode: 'recordUntilFull', traceBufferSizeInKb: bufferKb },
      transferMode: 'ReportEvents',
    });
    await t.#mark('start');
    return t;
  }

  async #mark(label) {
    const id = `compare-${label}-${crypto.randomBytes(8).toString('hex')}`;
    const perf0 = performance.now();
    const before = hrUs();
    await this.#cdp.send('Tracing.recordClockSyncMarker', { syncId: id });
    const after = hrUs();
    const perf1 = performance.now();
    this.#marks.push({ id, before, after });
    if (!this.#perfAtHr) this.#perfAtHr = { perf: (perf0 + perf1) / 2, hr: (before + after) / 2 };
  }

  /** Stop recording: { events, dataLoss, clock } (clock: trace microseconds <-> performance.now milliseconds). */
  async stop() {
    try { await this.#mark('end'); } catch { /* the page closed */ }
    await this.#cdp.send('Tracing.end').catch(() => {});
    const complete = await Promise.race([this.#complete, new Promise(r => setTimeout(() => r({ timedOut: true }), 30_000))]);
    await this.#cdp.detach().catch(() => {});
    const clock = clockFrom(this.#events, this.#marks, this.#perfAtHr);
    return { events: this.#events, dataLoss: !!complete?.dataLossOccurred || !!complete?.timedOut, clock };
  }
}

/**
 * The mapping between the trace's clock (microseconds) and performance.now (milliseconds), from the
 * clock-sync markers: each marker's trace time against the harness's time around the call. Null when
 * no marker came back. `error_us` bounds the mapping's error.
 */
export function clockFrom(events, marks, perfAtHr) {
  const byId = new Map(events.filter(e => e.name === 'clock_sync' && e.args?.sync_id).map(e => [e.args.sync_id, e.ts]));
  const fits = marks.filter(m => byId.has(m.id)).map(m => ({ offset: byId.get(m.id) - (m.before + m.after) / 2, error: (m.after - m.before) / 2 }));
  if (!fits.length || !perfAtHr) return null;
  const best = fits.reduce((a, b) => (b.error < a.error ? b : a));
  const drift = Math.max(...fits.map(f => Math.abs(f.offset - best.offset)));
  return { offset_us: best.offset, error_us: best.error + drift, perf_ms: perfAtHr.perf, hr_us: perfAtHr.hr };
}

/** A trace time (microseconds) as seconds on the operator's clock (`originMs`: the clock's start, performance.now). */
export const traceToClock = (ts, clock, originMs) => (clock.perf_ms + (ts - clock.offset_us - clock.hr_us) / 1000 - originMs) / 1000;
/** Seconds on the operator's clock as a trace time (microseconds). */
export const clockToTrace = (s, clock, originMs) => (originMs + s * 1000 - clock.perf_ms) * 1000 + clock.hr_us + clock.offset_us;

const keyOf = e => `${e.pid}:${e.tid}`;

/**
 * The trace as tasks: per thread, each top-level task with the events inside it, and indexes of the
 * timers, animation frames and requests by id. Pure; `events` is the raw trace.
 */
export function analyseTrace(events) {
  const names = new Map();
  for (const e of events) if (e.ph === 'M' && e.name === 'thread_name') names.set(keyOf(e), e.args?.name || '');
  const byThread = new Map();
  for (const e of events) {
    if (e.ph === 'M' || typeof e.ts !== 'number') continue;
    const k = keyOf(e);
    if (!byThread.has(k)) byThread.set(k, []);
    byThread.get(k).push(e);
  }
  const tasks = [];
  const threads = new Map();
  const installs = new Map(); // `${frame}:${timerId}` -> { event, task }
  const frames = new Map(); // `${frame}:${id}` -> { event, task } (RequestAnimationFrame)
  const sends = new Map(); // requestId -> { event, task }
  for (const [k, list] of byThread) {
    list.sort((a, b) => a.ts - b.ts || (b.dur || 0) - (a.dur || 0));
    const name = names.get(k) || '';
    const own = [];
    let current = null;
    for (const e of list) {
      if (e.ph === 'X' && e.name === 'RunTask' && (!current || e.ts > current.end)) {
        current = { thread: k, threadName: name, ts: e.ts, end: e.ts + (e.dur || 0), events: [], index: own.length };
        own.push(current);
        continue;
      }
      if (current && e.ts >= current.ts && e.ts <= current.end) current.events.push(e);
    }
    threads.set(k, { name, tasks: own, main: name === 'CrRendererMain', worker: /Worker/i.test(name) });
    for (const t of own) {
      describeTask(t);
      tasks.push(t);
      for (const e of t.events) {
        const d = e.args?.data || {};
        if (e.name === 'TimerInstall') installs.set(`${d.frame}:${d.timerId}`, { event: e, task: t });
        else if (e.name === 'RequestAnimationFrame') frames.set(`${d.frame}:${d.id}`, { event: e, task: t });
        else if (e.name === 'ResourceSendRequest' && d.requestId && !sends.has(d.requestId)) sends.set(d.requestId, { event: e, task: t });
      }
    }
  }
  // Every ResourceSendRequest (renderer and browser side) by request id, for writes that are navigations.
  const sendEvents = events.filter(e => e.name === 'ResourceSendRequest' && e.args?.data?.requestId);
  // Where the page's frames started navigations (a form posted, an address set), with the address.
  const navigationStarts = events.filter(e => e.name === 'FrameLoader::StartNavigation' && e.args?.url);
  return { tasks, threads, installs, frames, sends, sendEvents, navigationStarts };
}

const spanHolds = (s, ts) => ts >= s.ts && ts <= s.ts + (s.dur || 0);

/** What kind of task it is, from what is inside it. */
function describeTask(t) {
  const src = t.events.find(e => e.name === 'ThreadControllerImpl::RunTask')?.args || {};
  t.src = src.src_file ? `${String(src.src_file).split('/').pop()}:${src.src_func || ''}` : null;
  t.protocol = t.events.some(e => e.name === 'DevToolsSession::DispatchProtocolCommand');
  t.timerFires = t.events.filter(e => e.ph === 'X' && e.name === 'TimerFire');
  t.frameFires = t.events.filter(e => e.ph === 'X' && e.name === 'FireAnimationFrame');
  t.answers = t.events.filter(e => /^Resource(ReceiveResponse|ReceivedData|Finish)$/.test(e.name) && e.args?.data?.requestId);
  const scripts = t.events.filter(e => e.ph === 'X' && /^(FunctionCall|EvaluateScript|TimerFire|FireAnimationFrame|DevToolsSession::DispatchProtocolCommand)$/.test(e.name));
  // Events dispatched by the browser, not by script: those not inside any script span of the task.
  t.topDispatches = t.events.filter(e => e.ph === 'X' && e.name === 'EventDispatch' && !scripts.some(s => s !== e && spanHolds(s, e.ts) && s.ts <= e.ts));
  // An input event the browser delivered: dispatched outside any script of the task. Usually a task of
  // the input queue; some key events arrive inside a frame's task instead (then only discrete input
  // counts: a frame also delivers the boundary events of a pointer the page moved content under).
  const fromInputQueue = !!t.src && /main_thread_event_queue/.test(t.src);
  t.inputQueue = fromInputQueue;
  t.inputs = t.topDispatches.filter(e => (fromInputQueue ? INPUT_EVENTS : DISCRETE_INPUT).test(e.args?.data?.type || ''));
  t.input = !t.protocol && t.inputs.length > 0;
  // Whether the page's own code (or the page's network answers) ran in it.
  t.activity = !t.protocol && (t.timerFires.length > 0 || t.frameFires.length > 0 || t.answers.length > 0
    || t.events.some(e => (e.ph === 'X' && /^(FunctionCall|ParseHTML)$/.test(e.name)) || (e.name === 'EvaluateScript' && e.args?.data)
      || (e.name === 'EventDispatch' && !String(e.args?.data?.type || '').startsWith('__playwright')) || e.name === 'ResourceSendRequest'));
}

/** The task of `thread` holding the trace time `ts`, or null. */
function taskAt(model, thread, ts) {
  const list = model.threads.get(thread)?.tasks || [];
  let lo = 0;
  let hi = list.length - 1;
  while (lo <= hi) {
    const mid = (lo + hi) >> 1;
    if (list[mid].end < ts) lo = mid + 1;
    else if (list[mid].ts > ts) hi = mid - 1;
    else return list[mid];
  }
  return null;
}

/**
 * Why the event `point` (an event of the trace: a request sent, a timer armed) happened: the chain of
 * tasks back to a counted step, or to something no step caused. `steps` hold their windows as trace
 * times ({ n, from, to }). Returns { caused, step, why, chain }.
 */
export function causeOf(model, point, steps, opts = {}) {
  const ctx = { model, steps, reaction: opts.reaction ?? 1, link: opts.link ?? LINK_SECONDS, guard: opts.guard ?? GUARD_SECONDS, slack: opts.slackUs ?? 2000, memo: new Map() };
  return pointCause(ctx, point, 0);
}

function stepHolding(ctx, ts) {
  for (let i = ctx.steps.length - 1; i >= 0; i--) {
    const s = ctx.steps[i];
    if (ts >= s.from - ctx.slack && ts <= s.to + ctx.slack) return s;
  }
  return null;
}

function stepBefore(ctx, ts) {
  for (let i = ctx.steps.length - 1; i >= 0; i--) if (ctx.steps[i].from - ctx.slack <= ts) return ctx.steps[i];
  return null;
}

const no = (why, chain = []) => ({ caused: false, step: null, why, chain });
/** The first reason of a chain that caused nothing (a loop of frames or timers names its origin once). */
const rootOf = c => c.root || c.why;
const noFrom = (what, c) => ({ ...no(`${what}: ${rootOf(c)}`, c.chain), root: rootOf(c) });
const yes = (step, chain) => ({ caused: true, step, why: null, chain });
const fmt = s => `${Math.round(s * 1000) / 1000} s`;

function pointCause(ctx, point, depth) {
  if (depth > 400) return no('a chain of tasks too long to follow');
  const task = taskAt(ctx.model, keyOf(point), point.ts);
  if (!task) return no('it was sent outside any task the page\'s threads ran (not the page\'s doing)');
  // The kind of task at that point: a timer's or a frame callback's span holding it (or the last one
  // before it in the task: script queued by the callback runs before the task ends).
  const lastBefore = list => list.filter(s => s.ts <= point.ts).pop() || null;
  if (task.protocol) {
    const s = stepHolding(ctx, task.ts);
    return s ? yes(s.n, [`a DevTools command of step ${s.n}`]) : no('a DevTools command outside any step (the harness reading the page)');
  }
  const timer = lastBefore(task.timerFires);
  if (timer) return timerCause(ctx, timer, depth);
  const frame = task.frameFires.find(f => spanHolds(f, point.ts)) || null;
  if (frame) return frameCause(ctx, frame, depth);
  // In a task of the input queue the whole task is the input's; in another (a frame's) only what ran
  // inside the input event's dispatch is (the frame may also run an observer the page set off).
  if (task.input && (task.inputQueue || task.inputs.some(e => spanHolds(e, point.ts)))) {
    // Only the harness's steps put input in the page: an input task is the latest step's that began
    // before it (the renderer may handle a key a few milliseconds after the step's call returned).
    const s = stepBefore(ctx, task.ts);
    return s ? yes(s.n, [`input of step ${s.n}`]) : no('an input event before the first counted step');
  }
  const answers = task.answers.filter(a => a.ts <= point.ts);
  if (answers.length) return answerCause(ctx, task, answers, depth);
  return otherCause(ctx, task, depth);
}

function timerCause(ctx, timer, depth) {
  const d = timer.args?.data || {};
  const install = ctx.model.installs.get(`${d.frame}:${d.timerId}`);
  if (!install) return no('a timer armed before the clock started');
  const i = install.event.args?.data || {};
  if (i.singleShot === false) return no(`a repeating timer (every ${fmt((i.timeout || 0) / 1000)}): a poll is the page's own doing`);
  if ((i.timeout || 0) > ctx.reaction * 1000) return no(`a timer of ${fmt((i.timeout || 0) / 1000)}: a delay the page set, longer than a page takes to answer a step`);
  const c = pointCause(ctx, install.event, depth + 1);
  return c.caused ? yes(c.step, [...c.chain, `a timer of ${fmt((i.timeout || 0) / 1000)}`]) : noFrom('a timer armed by a task no step caused', c);
}

function frameCause(ctx, frame, depth) {
  const d = frame.args?.data || {};
  const asked = ctx.model.frames.get(`${d.frame}:${d.id}`);
  if (!asked) return no('an animation frame asked for before the clock started');
  const c = pointCause(ctx, asked.event, depth + 1);
  return c.caused ? yes(c.step, [...c.chain, 'an animation frame']) : noFrom('an animation frame asked for by a task no step caused', c);
}

function answerCause(ctx, task, answers, depth) {
  const ids = [...new Set(answers.map(a => a.args.data.requestId))];
  let first = null;
  const chain = [];
  for (const id of ids) {
    const sent = ctx.model.sends.get(id);
    if (!sent) return no('the answer to a request sent before the clock started');
    const held = (answers.find(a => a.args.data.requestId === id).ts - sent.event.ts) / 1e6;
    const what = `${sent.event.args.data.requestMethod || ''} ${pathOf(sent.event.args.data.url)}`.trim();
    if (held > ctx.link) return no(`it waited ${fmt(held)} for the answer to ${what}: a delay longer than ${fmt(ctx.link)} links nothing (the page held the write back)`);
    const c = pointCause(ctx, sent.event, depth + 1);
    if (!c.caused) return noFrom(`it followed the answer to ${what}, which no step caused`, c);
    if (first === null) first = c.step;
    chain.push(...c.chain, `the answer to ${what} after ${fmt(held)}`);
  }
  return yes(first, chain);
}

/** A task of no traceable origin (a message, an observer, a scheduled submission): see the header. */
function otherCause(ctx, task, depth) {
  const key = `other:${task.thread}:${task.index}`;
  if (ctx.memo.has(key)) return ctx.memo.get(key);
  ctx.memo.set(key, no('a loop of untraceable tasks'));
  const thread = ctx.model.threads.get(task.thread);
  const before = thread.tasks.slice(0, task.index).reverse();
  const prevIdx = before.findIndex(t => t.activity || (t.protocol && stepHolding(ctx, t.ts)));
  let result;
  if (prevIdx < 0) result = no('a task of no traceable origin with no page task before it');
  else {
    const prev = before[prevIdx];
    const pc = taskCause(ctx, prev, depth + 1);
    if (!pc.caused) result = noFrom('a task of no traceable origin (a message, an observer) after a page task no step caused', pc);
    else if ((task.ts - prev.end) / 1e6 > ctx.reaction) result = no(`a task of no traceable origin ${fmt((task.ts - prev.end) / 1e6)} after the last task a step caused`);
    else {
      const earlier = before.slice(prevIdx + 1).find(t => t.activity);
      const ec = earlier && (prev.ts - earlier.end) / 1e6 < ctx.guard ? taskCause(ctx, earlier, depth + 1) : null;
      const worker = [...ctx.model.threads.values()].filter(t => t.worker).flatMap(t => t.tasks)
        .find(t => t.activity && t.end >= prev.ts - ctx.guard * 1e6 && t.ts <= task.ts);
      if (ec && !ec.caused) result = noFrom(`a task of no traceable origin: a page task no step caused ran just before the step's (${fmt((prev.ts - earlier.end) / 1e6)} before it) and could have sent it`, ec);
      else if (worker) result = no('a task of no traceable origin while a worker of the page ran: the worker could have sent it');
      else result = yes(pc.step, [...pc.chain, 'a task that followed at once (a message or an observer)']);
    }
  }
  ctx.memo.set(key, result);
  return result;
}

/** Whether a whole task was caused: every entry into page code in it (timers, frames, answers, input). */
function taskCause(ctx, task, depth) {
  const key = `task:${task.thread}:${task.index}`;
  if (ctx.memo.has(key)) return ctx.memo.get(key);
  const points = [...task.timerFires, ...task.frameFires, ...task.answers];
  if (!points.length) points.push({ ...task.events[task.events.length - 1] || {}, ts: task.end, pid: Number(task.thread.split(':')[0]), tid: Number(task.thread.split(':')[1]) });
  let result = null;
  for (const p of points) {
    const c = pointCause(ctx, { ...p, pid: Number(task.thread.split(':')[0]), tid: Number(task.thread.split(':')[1]) }, depth + 1);
    if (!c.caused) { result = c; break; }
    result = result || c;
  }
  ctx.memo.set(key, result);
  return result;
}

const pathOf = u => { try { return new URL(u).pathname; } catch { return String(u || ''); } };
const bare = u => String(u || '').split('#')[0];

/**
 * The trace's verdict on each write the measured page sent (`writes`: { method, url, at } with `at`
 * on the operator's clock). `steps`: the operator's steps (on its clock: began, at, took). `clock`
 * and `originMs` map the two clocks. Returns, per write, { caused, step, why, chain, request_id }.
 */
export function writeCauses(trace, writes, steps, { clock, originMs, reaction = 1, link = LINK_SECONDS, guard = GUARD_SECONDS } = {}) {
  const model = trace.model || analyseTrace(trace.events);
  const slackUs = 2000 + (clock?.error_us || 0);
  const windows = steps.filter(s => typeof s.at === 'number').map(s => ({
    n: s.n, from: clockToTrace(typeof s.began === 'number' ? s.began : s.at, clock, originMs), to: clockToTrace(s.at + (s.took || 0), clock, originMs),
  }));
  const taken = new Set();
  const starts = model.navigationStarts;
  return writes.map(w => {
    // A navigation (a form posted): judged by the task that started it (the browser sends the request).
    if (w.navigation) {
      const near = starts.filter(e => !taken.has(e) && bare(e.args.url) === bare(w.url))
        .map(e => ({ e, d: Math.abs(traceToClock(e.ts, clock, originMs) - (w.at ?? 0)) })).sort((a, b) => a.d - b.d)[0];
      if (!near || near.d > 5) return { caused: false, step: null, why: 'a navigation no task of the page started (the browser\'s own)', chain: [], answered: [] };
      taken.add(near.e);
      const c = causeOf(model, near.e, windows, { reaction, link, guard, slackUs });
      return { ...c, request_id: null, answered: [], at_trace: round3(traceToClock(near.e.ts, clock, originMs)) };
    }
    const candidates = model.sendEvents.filter(e => !taken.has(e) && (e.args.data.requestMethod || '').toUpperCase() === String(w.method).toUpperCase() && bare(e.args.data.url) === bare(w.url));
    const near = candidates.map(e => ({ e, d: Math.abs(traceToClock(e.ts, clock, originMs) - (w.at ?? 0)) })).sort((a, b) => a.d - b.d)[0];
    if (!near || near.d > 5) return { caused: false, step: null, why: 'the browser\'s record of the page holds no such request (another page sent it, or the record was cut short)', chain: [], answered: [] };
    const requestId = near.e.args.data.requestId;
    for (const e of model.sendEvents) if (e.args.data.requestId === requestId) taken.add(e);
    const point = model.sends.get(requestId)?.event || near.e;
    const c = causeOf(model, point, windows, { reaction, link, guard, slackUs });
    // The requests whose answers the task that sent it delivered first (a batch's next part follows its last one's answer).
    // When it was sent by a task of no traceable origin (the page read the answer's body first), the
    // answers of the page task just before it.
    let task = taskAt(model, keyOf(point), point.ts);
    let upTo = point.ts;
    if (task && !task.protocol && !task.input && !task.timerFires.length && !task.frameFires.length && !task.answers.some(a => a.ts <= point.ts)) {
      const prev = model.threads.get(task.thread).tasks.slice(0, task.index).reverse().find(t => t.activity);
      if (prev && (task.ts - prev.end) / 1e6 <= reaction) { task = prev; upTo = prev.end; }
    }
    const answered = [...new Set((task?.answers || []).filter(a => a.ts <= upTo).map(a => a.args.data.requestId))];
    return { ...c, request_id: requestId, answered, at_trace: round3(traceToClock(point.ts, clock, originMs)) };
  });
}

const round3 = x => Math.round(x * 1000) / 1000;

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
//   answer    a task that delivered (part of) the answer to a request (the page's code waiting on
//             it runs there, before or after the browser records the delivery) is caused when that
//             request was sent by a caused task and was answered within
//             LINK_SECONDS (round 12, plant S2: a click that "approves" 2.5 s later behind a slow
//             answer it asked for is a delay, as R2b's timer was); a task delivering several answers
//             is caused only when every one of them is;
//   other     a task of no traceable origin (a message port, a worker's message, an observer, a
//             scheduled form submission) is caused only when the page task just before it was caused
//             by a step, it began within REACTION_SECONDS of that task's end, no page task that
//             caused nothing ended within GUARD_SECONDS before that task began (one that ran just
//             before the step could have posted the message), and no worker of the page ran meanwhile.
//
// Round 13 (critic p01 r12, plants W1-W8): timing that lives outside the page's thread causes nothing.
// A web socket's message is the answer to the page's last frame on it (sent by a caused task within
// LINK_SECONDS) or the server's push; a window's postMessage is the posting task's (its traceId); a
// message on an interface that carries another frame's, tab's, worker's or server's message
// (OUTSIDE_CARRIERS) causes nothing; an untraceable task is refused while another thread of the page's
// script ran (unless it delivers what the browser read for the page itself, IN_PAGE_DATA); a document
// is its navigation's; the delays of a chain add up to at most LINK_SECONDS from the step's input; an
// animation's, transition's or media element's event, a refresh the page set, and an observer run
// while an animation runs cause nothing; an idle callback is its asker's.
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
  // Round 14: every change of what the page shows, inside the task that made it (screenCauses).
  'disabled-by-default-devtools.timeline.invalidationTracking',
]);
/** An answer the page waited on for longer than this before it sent its write links nothing (plant S2). */
export const LINK_SECONDS = 2;
/** A page task that caused nothing and ended this close before a step's task could have posted what follows it. */
export const GUARD_SECONDS = 0.1;
/** A page task busy this long when a step's input was dispatched made the input wait behind it. */
export const BUSY_SECONDS = 0.002;
/** Input events a step delivers (the outermost event of an input task). */
const INPUT_EVENTS = /^(pointer(down|up|move|over|out|enter|leave|cancel|rawupdate)|mouse(down|up|move|over|out|enter|leave)|click|dblclick|auxclick|contextmenu|key(down|up|press)|wheel|touch(start|end|move|cancel)|beforeinput|input|textInput|change|composition(start|update|end)|submit|formdata|paste|copy|cut|drag\w*|drop|DOMActivate|focus|blur|focusin|focusout|DOMFocusIn|DOMFocusOut)$/;
/**
 * Input events the browser only dispatches for input it was given (a key, a press, a release, text):
 * never on its own. The browser does dispatch boundary and move events (mouseover, pointermove ...)
 * when the page changes under a still pointer, and focus and blur when windows come and go, so those
 * count as a step's input only in a task of the input queue.
 */
/** Events whose time is set by a duration: animations, transitions (CSS and script), media playing. */
export const TIME_DRIVEN = /^(animation(start|end|iteration|cancel)|transition(run|start|end|cancel)|finish|remove|timeupdate|ended|play|playing|pause|seeked|seeking|waiting|stalled|suspend|canplay|canplaythrough|loadeddata|loadedmetadata|durationchange|ratechange|volumechange|cuechange|enter|exit)$/;
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
  const sockets = new Map(); // `${pid}:${frame}:${identifier}` -> { sends: [{ event, task }], receives: [{ event, task }] } (web sockets)
  const posted = new Map(); // traceId -> { event, task } (SchedulePostMessage: a window's postMessage)
  const idles = new Map(); // `${frame}:${id}` -> { event, task } (RequestIdleCallback)
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
    let submitted = null; // the last task that dispatched a form's submit event, not yet paired
    for (const t of own) {
      describeTask(t);
      // A form submission runs in a task of its own, scheduled by the task that submitted the form: it
      // is paired with the last task before it that dispatched the form's submit event (none for a
      // form.submit() call, which dispatches no event: then it has no traceable origin).
      if (t.formSubmission) { t.submittedBy = submitted; submitted = null; }
      const submit = t.events.find(e => e.name === 'EventDispatch' && e.args?.data?.type === 'submit');
      if (submit) submitted = { task: t, event: submit };
      tasks.push(t);
      for (const e of t.events) {
        const d = e.args?.data || {};
        // Round 13: ids start again in each new document of a frame (a page that reloads), so every
        // request is kept and a callback is matched with the last one before it.
        if (e.name === 'TimerInstall') keep(installs, `${d.frame}:${d.timerId}`, { event: e, task: t });
        else if (e.name === 'RequestAnimationFrame') keep(frames, `${d.frame}:${d.id}`, { event: e, task: t });
        else if (e.name === 'RequestIdleCallback') keep(idles, `${d.frame}:${d.id}`, { event: e, task: t });
        else if (e.name === 'ResourceSendRequest' && d.requestId && !sends.has(d.requestId)) sends.set(d.requestId, { event: e, task: t });
        else if ((e.name === 'WebSocketSend' || e.name === 'WebSocketReceive') && d.identifier !== undefined) {
          const key = socketKey(e);
          if (!sockets.has(key)) sockets.set(key, { sends: [], receives: [] });
          sockets.get(key)[e.name === 'WebSocketSend' ? 'sends' : 'receives'].push({ event: e, task: t });
        } else if (e.name === 'SchedulePostMessage' && d.traceId !== undefined && !posted.has(String(d.traceId))) posted.set(String(d.traceId), { event: e, task: t });
      }
    }
  }
  // Every ResourceSendRequest (renderer and browser side) by request id, for writes that are navigations.
  const sendEvents = events.filter(e => e.name === 'ResourceSendRequest' && e.args?.data?.requestId);
  // Where the page's frames started navigations (a form posted, an address set), with the address.
  const navigationStarts = events.filter(e => e.name === 'FrameLoader::StartNavigation' && e.args?.url);
  for (const v of sockets.values()) { v.sends.sort((a, b) => a.event.ts - b.event.ts); v.receives.sort((a, b) => a.event.ts - b.event.ts); }
  // Round 13: when the page's animations and transitions ran (Chromium's async "Animation" events by
  // id: begun, ended). One begun before the record has only its end; one still running has no end.
  const animations = new Map();
  for (const e of events) {
    if (e.name !== 'Animation' || !e.id2?.local) continue;
    const a = animations.get(e.id2.local) || { from: null, to: null };
    if (e.ph === 'b') a.from = e.ts;
    else if (e.ph === 'e') a.to = e.ts;
    animations.set(e.id2.local, a);
  }
  return { tasks, threads, installs, frames, idles, sends, sendEvents, navigationStarts, sockets, posted, animations: [...animations.values()] };
}

const keep = (map, key, value) => { if (!map.has(key)) map.set(key, []); map.get(key).push(value); };
/** The last request of `key` made before trace time `ts` (a timer's install, an animation frame's or idle callback's request). */
const askedBefore = (map, key, ts) => (map.get(key) || []).filter(x => x.event.ts < ts).sort((a, b) => a.event.ts - b.event.ts).pop() || null;

/** One web socket of one page: its frame and the identifier the browser gave it. */
const socketKey = e => `${e.pid}:${e.args?.data?.frame}:${e.args?.data?.identifier}`;

const spanHolds = (s, ts) => ts >= s.ts && ts <= s.ts + (s.dur || 0);

/** What kind of task it is, from what is inside it. */
function describeTask(t) {
  const src = t.events.find(e => e.name === 'ThreadControllerImpl::RunTask')?.args || {};
  t.src = src.src_file ? `${String(src.src_file).split('/').pop()}:${src.src_func || ''}` : null;
  t.protocol = t.events.some(e => e.name === 'DevToolsSession::DispatchProtocolCommand');
  t.formSubmission = !!t.src && /ScheduleFormSubmission/.test(t.src);
  t.timerFires = t.events.filter(e => e.ph === 'X' && e.name === 'TimerFire');
  t.frameFires = t.events.filter(e => e.ph === 'X' && e.name === 'FireAnimationFrame');
  t.idleFires = t.events.filter(e => e.ph === 'X' && e.name === 'FireIdleCallback');
  t.answers = t.events.filter(e => /^Resource(ReceiveResponse|ReceivedData|Finish)$/.test(e.name) && e.args?.data?.requestId);
  // Round 13 (critic p01 r12, plant W1): messages that arrive from outside the page's thread. A frame
  // the server pushed over a web socket; a window's postMessage (its traceId names the task that
  // posted it, when that was a window of the same process); and the interface of the browser's
  // message that started the task (Chromium records it as the mojo interface's name).
  t.wsReceives = t.events.filter(e => e.name === 'WebSocketReceive');
  t.postHandles = t.events.filter(e => e.ph === 'X' && e.name === 'HandlePostMessage');
  t.carriers = t.events.filter(e => e.name === 'Receive mojo message' || e.name === 'SimpleWatcher::OnHandleReady')
    .map(e => String(e.args?.chrome_mojo_event_info?.mojo_interface_tag || e.args?.chrome_mojo_event_info?.watcher_notify_interface_tag || ''));
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
  // Round 14 (critic p01 r13, other finding 1: under load an honest click's handler ran inside a DevTools
  // command task, and the click's save was refused): discrete input the browser delivered while running a
  // DevTools command (dispatched outside any of the page's script: a page's synthetic event is inside its
  // script) is still input, never the harness's reading.
  const pageScripts = t.events.filter(e => e.ph === 'X' && /^(FunctionCall|EvaluateScript|TimerFire|FireAnimationFrame|FireIdleCallback)$/.test(e.name));
  t.commandInputs = t.protocol ? t.events.filter(e => e.ph === 'X' && e.name === 'EventDispatch' && DISCRETE_INPUT.test(e.args?.data?.type || '')
    && !pageScripts.some(x => spanHolds(x, e.ts) && x.ts <= e.ts)) : [];
  // Round 13: events a running animation, transition or media element fires (their time is set by a
  // duration, like a timer's), and script the browser runs for an observer as it renders a frame
  // (a resize or intersection observer: what moved the layout may be an animation).
  t.timed = t.topDispatches.filter(e => TIME_DRIVEN.test(e.args?.data?.type || ''));
  const rendering = !!t.src && /BeginMainFrame|intersection_observer|resize_observer/i.test(t.src);
  t.observerScript = rendering && t.events.some(e => e.ph === 'X' && e.name === 'FunctionCall'
    && ![...t.frameFires, ...t.inputs, ...t.timed].some(x => x !== e && spanHolds(x, e.ts)));
  // Whether the page's own code (or the page's network answers) ran in it.
  t.activity = !t.protocol && (t.timerFires.length > 0 || t.frameFires.length > 0 || t.idleFires.length > 0 || t.answers.length > 0
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
  return judgePoint(causeContext(model, steps, opts), point);
}

function causeContext(model, steps, opts = {}) {
  return { model, steps, reaction: opts.reaction ?? 1, link: opts.link ?? LINK_SECONDS, guard: opts.guard ?? GUARD_SECONDS, slack: opts.slackUs ?? 2000, memo: new Map(),
    animatedAtStart: opts.animatedAtStart === undefined ? 0 : opts.animatedAtStart,
    // Round 14: a screen change is judged by where its chain begins, not by its delays (screenCauses).
    timerLimit: opts.timerLimit ?? opts.reaction ?? 1, repeating: !!opts.repeating };
}

function judgePoint(ctx, point) {
  const c = pointCause(ctx, point, 0);
  // Round 13 (critic p01 r12, plant W3): the delays of a chain add up. Each link alone may stay under
  // its limit (an answer held 1.9 s, then a timer of 0.9 s) while the whole chain holds the write back
  // longer than any one link may: the write must go out within LINK_SECONDS of the step's input.
  if (c.caused && typeof c.origin === 'number' && c.origin > point.ts + ctx.slack) {
    return no(`its chain (${c.chain.join(' > ')}) begins after it went out: the record cannot order it`, c.chain);
  }
  if (c.caused && typeof c.origin === 'number' && (point.ts - c.origin) / 1e6 > ctx.link) {
    return no(`it went out ${fmt((point.ts - c.origin) / 1e6)} after the input of step ${c.step} that began its chain (${c.chain.join(' > ')}): delays that add up to more than ${fmt(ctx.link)} link nothing (the page held the write back)`, c.chain);
  }
  return c.caused && typeof c.origin === 'number' ? { ...c, chain_seconds: round3((point.ts - c.origin) / 1e6) } : c;
}

/**
 * Round 13 (verify of this round: a page that loads after a click was credited through the harness's
 * own reads during the click): the steps whose action reaches the page as a DevTools command (a file
 * chosen, the device's answer, a scroll into view). During any other step (a click, a key, typing:
 * input the browser delivers) the harness's commands only read the page; they cause nothing and are
 * passed over when looking for the page task before an untraceable one.
 */
const COMMAND_STEPS = new Set(['file-pick', 'device', 'scroll']);
const actingCommand = (ctx, t) => { const s = t.protocol ? stepHolding(ctx, t.ts) : null; return !!s && COMMAND_STEPS.has(s.kind); };

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
/** Caused by step `step`; `origin` is when the step's input (or command) began the chain, a trace time. */
const yes = (step, chain, origin) => ({ caused: true, step, why: null, chain, origin });
const fmt = s => `${Math.round(s * 1000) / 1000} s`;

function pointCause(ctx, point, depth) {
  if (depth > 400) return no('a chain of tasks too long to follow');
  const task = taskAt(ctx.model, keyOf(point), point.ts);
  if (!task) return no('it was sent outside any task the page\'s threads ran (not the page\'s doing)');
  // The kind of task at that point: a timer's or a frame callback's span holding it (or the last one
  // before it in the task: script queued by the callback runs before the task ends).
  const lastBefore = list => list.filter(s => s.ts <= point.ts).pop() || null;
  if (task.protocol && task.commandInputs.some(e => spanHolds(e, point.ts))) {
    const s = stepBefore(ctx, point.ts);
    return s ? yes(s.n, [`input of step ${s.n} (delivered during a DevTools command)`], task.ts) : no('an input event before the first counted step');
  }
  if (task.protocol) {
    const s = stepHolding(ctx, task.ts);
    if (s && !COMMAND_STEPS.has(s.kind)) return no(`a DevTools command of the harness during step ${s.n} (a ${s.kind || 'step'} whose input the browser delivers; the harness's commands only read the page)`);
    return s ? yes(s.n, [`a DevTools command of step ${s.n}`], task.ts) : no('a DevTools command outside any step (the harness reading the page)');
  }
  const timer = lastBefore(task.timerFires);
  if (timer) return timerCause(ctx, timer, depth);
  const frame = task.frameFires.find(f => spanHolds(f, point.ts)) || null;
  if (frame) return frameCause(ctx, frame, depth);
  const idle = task.idleFires.find(f => spanHolds(f, point.ts)) || null;
  if (idle) return idleCause(ctx, idle, depth);
  const timed = task.timed.find(e => spanHolds(e, point.ts)) || null;
  if (timed) return no(`a "${timed.args.data.type}" event (an animation, a transition or media playing fired it): when it came is set by its duration, not by the person`);
  // In a task of the input queue the whole task is the input's; in another (a frame's) only what ran
  // inside the input event's dispatch is (the frame may also run an observer the page set off).
  if (task.input && (task.inputQueue || task.inputs.some(e => spanHolds(e, point.ts)))) {
    // Only the harness's steps put input in the page: an input task is the latest step's that began
    // before it (the renderer may handle a key a few milliseconds after the step's call returned).
    const s = stepBefore(ctx, task.ts);
    return s ? yes(s.n, [`input of step ${s.n}`], task.ts) : no('an input event before the first counted step');
  }
  // A task that delivered (part of) an answer: the page's code in it ran on that delivery, whether the
  // browser records the delivery before the code (headers) or after it (the body's end, which the
  // code reading the body waited for).
  if (task.answers.length) return answerCause(ctx, task, task.answers, depth);
  if (task.formSubmission && task.submittedBy && (task.ts - task.submittedBy.task.end) / 1e6 <= ctx.reaction) {
    const c = pointCause(ctx, { ...task.submittedBy.event, pid: Number(task.thread.split(':')[0]), tid: Number(task.thread.split(':')[1]) }, depth + 1);
    return c.caused ? yes(c.step, [...c.chain, 'the form it submitted'], c.origin) : noFrom('a form submitted by a task no step caused', c);
  }
  const message = messageCause(ctx, task, point, depth);
  if (message) return message;
  // Round 13: a refresh the page set (a meta refresh, a Refresh header) navigates when its delay ends.
  if (task.events.some(e => e.name === 'HttpRefreshScheduler::NavigateTask')) return no('a refresh the page set (a meta refresh or a Refresh header): when it navigates is set by its delay, not by the person');
  if (task.observerScript && animatedAt(ctx, task.ts)) return no(ANIMATED_OBSERVER);
  return otherCause(ctx, task, depth);
}

function timerCause(ctx, timer, depth) {
  const d = timer.args?.data || {};
  const install = askedBefore(ctx.model.installs, `${d.frame}:${d.timerId}`, timer.ts);
  if (!install) return no('a timer armed before the clock started');
  const i = install.event.args?.data || {};
  if (i.singleShot === false && !ctx.repeating) return no(`a repeating timer (every ${fmt((i.timeout || 0) / 1000)}): a poll is the page's own doing`);
  if ((i.timeout || 0) > ctx.timerLimit * 1000) return no(`a timer of ${fmt((i.timeout || 0) / 1000)}: a delay the page set, longer than a page takes to answer a step`);
  const c = pointCause(ctx, install.event, depth + 1);
  return c.caused ? yes(c.step, [...c.chain, `a ${i.singleShot === false ? 'repeating ' : ''}timer of ${fmt((i.timeout || 0) / 1000)}`], c.origin) : noFrom('a timer armed by a task no step caused', c);
}

function idleCause(ctx, idle, depth) {
  const d = idle.args?.data || {};
  const asked = askedBefore(ctx.model.idles, `${d.frame}:${d.id}`, idle.ts);
  if (!asked) return no('an idle callback asked for before the clock started');
  const c = pointCause(ctx, asked.event, depth + 1);
  return c.caused ? yes(c.step, [...c.chain, 'an idle callback'], c.origin) : noFrom('an idle callback asked for by a task no step caused', c);
}

const ANIMATED_OBSERVER = 'script the browser ran for an observer as it rendered a frame while an animation or transition of the page was running: what moved the layout may be the animation, whose time is set by its duration, not by the person';

/**
 * Whether an animation or transition of the page was running at trace time `ts` (or ended within
 * GUARD_SECONDS before it: its last frame's observers run after its end is recorded). Animations
 * already running when the clock started (`animatedAtStart`, read from the page) and not seen ending
 * count as running throughout.
 */
function animatedAt(ctx, ts) {
  const slack = ctx.guard * 1e6;
  const all = ctx.model.animations;
  if (all.some(a => (a.from === null || a.from <= ts) && (a.to === null || a.to + slack >= ts) && !(a.from === null && a.to === null))) return true;
  const atStart = ctx.animatedAtStart;
  if (atStart === null || atStart === undefined || atStart === 0) return atStart === null;
  const endedBefore = all.filter(a => a.from === null && a.to !== null);
  if (endedBefore.length < atStart) return true;
  return Math.max(...endedBefore.map(a => a.to)) + slack >= ts;
}

function frameCause(ctx, frame, depth) {
  const d = frame.args?.data || {};
  const asked = askedBefore(ctx.model.frames, `${d.frame}:${d.id}`, frame.ts);
  if (!asked) return no('an animation frame asked for before the clock started');
  const c = pointCause(ctx, asked.event, depth + 1);
  return c.caused ? yes(c.step, [...c.chain, 'an animation frame'], c.origin) : noFrom('an animation frame asked for by a task no step caused', c);
}

function answerCause(ctx, task, answers, depth) {
  const ids = [...new Set(answers.map(a => a.args.data.requestId))];
  let first = null;
  let origin = Infinity;
  const chain = [];
  for (const id of ids) {
    const sent = ctx.model.sends.get(id);
    if (!sent) return no('the answer to a request sent before the clock started');
    // Round 13: a document's own request, recorded where it commits: the document is the navigation's,
    // judged by the task that started it (a click on a link, a form posted); a navigation started before
    // the clock (a slow document the page asked for earlier) or by no page task causes nothing.
    if (sent.task === task) {
      const url = sent.event.args.data.url;
      const start = ctx.model.navigationStarts.filter(e => bare(e.args.url) === bare(url) && e.ts < task.ts).sort((a, b) => a.ts - b.ts).pop() || null;
      if (!start) return no(`the document ${pathOf(url)} it loaded, whose navigation started before the clock (or no page task started it)`);
      const c = pointCause(ctx, start, depth + 1);
      if (!c.caused) return noFrom(`the document ${pathOf(url)} it loaded, whose navigation no step caused`, c);
      if (first === null) first = c.step;
      origin = Math.min(origin, c.origin ?? Infinity);
      chain.push(...c.chain, `the document ${pathOf(url)} it loaded`);
      continue;
    }
    const held = (answers.find(a => a.args.data.requestId === id).ts - sent.event.ts) / 1e6;
    const what = `${sent.event.args.data.requestMethod || ''} ${pathOf(sent.event.args.data.url)}`.trim();
    if (held > ctx.link) return no(`it waited ${fmt(held)} for the answer to ${what}: a delay longer than ${fmt(ctx.link)} links nothing (the page held the write back)`);
    const c = pointCause(ctx, sent.event, depth + 1);
    if (!c.caused) return noFrom(`it followed the answer to ${what}, which no step caused`, c);
    if (first === null) first = c.step;
    origin = Math.min(origin, c.origin ?? Infinity);
    chain.push(...c.chain, `the answer to ${what} after ${fmt(held)}`);
  }
  return yes(first, chain, Number.isFinite(origin) ? origin : undefined);
}

/**
 * Interfaces of the browser's messages that carry what something outside the page's thread sent: a
 * server (web sockets, web transport, peer connections, push), another page, frame or tab (broadcast
 * channels, a frame's postMessage across processes, storage events), a service or shared worker.
 * A task they start is never a step's doing: its timing is theirs (round 13, critic p01 r12, W1, W2).
 */
export const OUTSIDE_CARRIERS = /WebSocket|WebTransport|BroadcastChannel|LocalFrame|RemoteFrame|LocalMainFrame|RemoteMainFrame|ServiceWorker|SharedWorker|PeerConnection|RTC|Push|StorageArea|Notification/;

/**
 * Round 13 (critic p01 r12, biggest gap): a task that delivered a message whose origin the record
 * shows. A web socket's message is the answer to the page's last frame on that socket, when a caused
 * task sent that frame within LINK_SECONDS and the server had not answered it already; otherwise the
 * server pushed it when it chose (plant W1). A window's postMessage is the posting task's (traceId);
 * one posted from another process has no posting task in this page and is not the person's (plant
 * W2). A message on an interface that carries what something outside the page's thread sent
 * (OUTSIDE_CARRIERS) causes nothing. Null when the task is none of these.
 */
function messageCause(ctx, task, point, depth) {
  const received = task.wsReceives.filter(e => e.ts <= point.ts).pop() || null;
  if (received) return socketCause(ctx, received, depth);
  const handle = task.postHandles.find(h => spanHolds(h, point.ts)) || task.postHandles.filter(h => h.ts <= point.ts).pop() || null;
  if (handle) return postCause(ctx, handle, depth);
  const outside = task.carriers.find(c => OUTSIDE_CARRIERS.test(c));
  if (outside) return no(`a message from outside the page's thread (${outside}): when it came is the sender's doing (a server, another frame, window or worker), not the person's`);
  return null;
}

function socketCause(ctx, received, depth) {
  const socket = ctx.model.sockets.get(socketKey(received));
  const sent = (socket?.sends || []).filter(x => x.event.ts < received.ts).pop() || null;
  if (!sent) return no('a message the server pushed over a web socket on which the page sent nothing since the clock started: when it came is the server\'s doing, not the person\'s');
  if (socket.receives.some(x => x.event.ts > sent.event.ts && x.event.ts < received.ts)) {
    return no('a message the server pushed over a web socket after it had already answered the page\'s last frame on it: when it came is the server\'s doing, not the person\'s');
  }
  const held = (received.ts - sent.event.ts) / 1e6;
  if (held > ctx.link) return no(`it waited ${fmt(held)} for a message over a web socket: a delay longer than ${fmt(ctx.link)} links nothing (the server held it back)`);
  const c = pointCause(ctx, sent.event, depth + 1);
  return c.caused ? yes(c.step, [...c.chain, `the answer over a web socket after ${fmt(held)}`], c.origin) : noFrom('it followed a web-socket message answering a frame no step caused', c);
}

function postCause(ctx, handle, depth) {
  const posted = ctx.model.posted.get(String(handle.args?.data?.traceId));
  if (!posted) return no('a message posted to the page from outside it (another frame, window or worker): when it came is the sender\'s doing, not the person\'s');
  const c = pointCause(ctx, posted.event, depth + 1);
  return c.caused ? yes(c.step, [...c.chain, 'a message the page posted to itself'], c.origin) : noFrom('a message posted by a task no step caused', c);
}

/**
 * Interfaces and sources of tasks that deliver what the browser read or answered for the page itself
 * (a file or blob read, a response's body, the device's answer): no other thread of the page's script
 * can send one.
 */
export const IN_PAGE_DATA = /BlobReader|file_reader|FileReader|FileSystem|FileUtilities|MimeRegistry|data_pipe|bytes_consumer|URLLoader|IDB|IndexedDB|CacheStorage|CodeCache|Clipboard|Authenticator|WebAuthn|Credential/;
const inPageData = t => (t.carriers.length > 0 && t.carriers.every(c => IN_PAGE_DATA.test(c))) || /(^|\/)body\.cc:|file_reader|bytes_consumer/.test(t.src || '');

/** A task of no traceable origin (a message, an observer, a scheduled submission): see the header. */
function otherCause(ctx, task, depth) {
  const key = `other:${task.thread}:${task.index}`;
  if (ctx.memo.has(key)) return ctx.memo.get(key);
  ctx.memo.set(key, no('a loop of untraceable tasks'));
  const thread = ctx.model.threads.get(task.thread);
  const before = thread.tasks.slice(0, task.index).reverse();
  const prevIdx = before.findIndex(t => t.activity || actingCommand(ctx, t));
  let result;
  if (prevIdx < 0) result = no('a task of no traceable origin with no page task before it');
  else {
    const prev = before[prevIdx];
    const pc = taskCause(ctx, prev, depth + 1);
    if (!pc.caused) result = noFrom('a task of no traceable origin (a message, an observer) after a page task no step caused', pc);
    else if ((task.ts - prev.end) / 1e6 > ctx.reaction) result = no(`a task of no traceable origin ${fmt((task.ts - prev.end) / 1e6)} after the last task a step caused`);
    else {
      // A message an uncaused task posted is delivered in the order tasks were queued, unless a task of
      // a higher priority jumps ahead: input and DevTools commands do (one that ran just before them
      // could have posted it), and so does a frame. For a step's own input or command, the guard is an
      // uncaused task that was busy (BUSY_SECONDS or more) when the step's input was dispatched, so the
      // input waited behind it; before a frame, an uncaused page task within GUARD_SECONDS before it. Tasks
      // of the same priority (answers, messages) run in the order they were queued.
      const head = prev.input || prev.protocol;
      const frame = !head && (prev.frameFires.length > 0 || /BeginMainFrame/.test(prev.src || ''));
      const dispatched = head ? ctx.steps.find(x => x.n === pc.step)?.at : null;
      const earlier = head
        ? before.slice(prevIdx + 1).find(t => t.activity && t.end >= (dispatched ?? prev.ts) - ctx.slack && (t.end - t.ts) / 1e6 >= BUSY_SECONDS)
        : frame ? before.slice(prevIdx + 1).find(t => t.activity) : null;
      const ec = earlier && (head || (prev.ts - earlier.end) / 1e6 < ctx.guard) ? taskCause(ctx, earlier, depth + 1) : null;
      // Round 13 (critic p01 r12, plant W2): any other thread that runs the page's script (a worker of
      // any kind, another frame's or window's process) could have posted it, from the moment the step's
      // input was dispatched (or the task before it began) on.
      // A task that delivers what the browser read for the page itself (a file, a blob, a response's body:
      // IN_PAGE_DATA) cannot have been sent by another thread; Odoo keeps a shared worker busy the whole
      // time, and its file upload follows the file's read.
      const from = Math.min(prev.ts, dispatched ?? prev.ts) - ctx.guard * 1e6;
      const worker = inPageData(task) ? null : [...ctx.model.threads.entries()].filter(([k, t]) => k !== task.thread && (t.worker || t.main)).flatMap(([, t]) => t.tasks)
        .find(t => t.activity && t.end >= from && t.ts <= task.ts);
      if (ec && !ec.caused) result = noFrom(`a task of no traceable origin: a page task no step caused ran just before the step's (${fmt((prev.ts - earlier.end) / 1e6)} before it) and could have sent it`, ec);
      else if (worker) result = no(`a task of no traceable origin while another thread of the page's script ran (${ctx.model.threads.get(worker.thread)?.name || 'a thread'}; a worker, another frame or window): it could have sent it`);
      else result = yes(pc.step, [...pc.chain, 'a task that followed at once (a message or an observer)'], pc.origin);
    }
  }
  ctx.memo.set(key, result);
  return result;
}

/** Whether a whole task was caused: every entry into page code in it (timers, frames, answers, input). */
function taskCause(ctx, task, depth) {
  const key = `task:${task.thread}:${task.index}`;
  if (ctx.memo.has(key)) return ctx.memo.get(key);
  if (task.observerScript && animatedAt(ctx, task.ts)) { const r = no(ANIMATED_OBSERVER); ctx.memo.set(key, r); return r; }
  const points = [...task.timerFires, ...task.frameFires, ...task.idleFires, ...task.timed, ...task.answers];
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
export function writeCauses(trace, writes, steps, { clock, originMs, reaction = 1, link = LINK_SECONDS, guard = GUARD_SECONDS, animatedAtStart = 0 } = {}) {
  const model = trace.model || analyseTrace(trace.events);
  const slackUs = 2000 + (clock?.error_us || 0);
  const windows = steps.filter(s => typeof s.at === 'number').map(s => ({
    n: s.n, kind: s.kind, from: clockToTrace(typeof s.began === 'number' ? s.began : s.at, clock, originMs), at: clockToTrace(s.at, clock, originMs), to: clockToTrace(s.at + (s.took || 0), clock, originMs),
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
      const c = causeOf(model, near.e, windows, { reaction, link, guard, slackUs, animatedAtStart });
      return { ...c, request_id: null, answered: [], at_trace: round3(traceToClock(near.e.ts, clock, originMs)) };
    }
    const candidates = model.sendEvents.filter(e => !taken.has(e) && (e.args.data.requestMethod || '').toUpperCase() === String(w.method).toUpperCase() && bare(e.args.data.url) === bare(w.url));
    const near = candidates.map(e => ({ e, d: Math.abs(traceToClock(e.ts, clock, originMs) - (w.at ?? 0)) })).sort((a, b) => a.d - b.d)[0];
    if (!near || near.d > 5) return { caused: false, step: null, why: 'the browser\'s record of the page holds no such request (another page sent it, or the record was cut short)', chain: [], answered: [] };
    const requestId = near.e.args.data.requestId;
    for (const e of model.sendEvents) if (e.args.data.requestId === requestId) taken.add(e);
    const point = model.sends.get(requestId)?.event || near.e;
    const c = causeOf(model, point, windows, { reaction, link, guard, slackUs, animatedAtStart });
    // The requests whose answers the task that sent it delivered first (a batch's next part follows its last one's answer).
    // When it was sent by a task of no traceable origin (the page read the answer's body first), the
    // answers of the page task just before it.
    let task = taskAt(model, keyOf(point), point.ts);
    let upTo = point.ts;
    if (task && !task.protocol && !task.input && !task.timerFires.length && !task.frameFires.length && !task.answers.length) {
      const prev = model.threads.get(task.thread).tasks.slice(0, task.index).reverse().find(t => t.activity);
      if (prev && (task.ts - prev.end) / 1e6 <= reaction) { task = prev; upTo = prev.end; }
    }
    const answered = [...new Set((task?.answers || []).filter(a => a.ts <= upTo || task === taskAt(model, keyOf(point), point.ts)).map(a => a.args.data.requestId))];
    return { ...c, request_id: requestId, answered, at_trace: round3(traceToClock(point.ts, clock, originMs)) };
  });
}

const round3 = x => Math.round(x * 1000) / 1000;

/**
 * Round 14 (critic p01 r13, plants N1-N3): events of the page's main thread that change what it shows,
 * recorded inside the task that made the change: a style or layout invalidated (a node added, removed
 * or changed, its text, its attributes, its style), a document parsed, a navigation started (an address
 * set). Changes inside a node that is not rendered (display: none) invalidate nothing and show nothing.
 */
export const SCREEN_EVENTS = /^(ScheduleStyleRecalculation|InvalidateLayout|StyleRecalcInvalidationTracking|LayoutInvalidationTracking|StyleInvalidatorInvalidationTracking|ScheduleStyleInvalidationTracking|ParseHTML|FrameLoader::StartNavigation)$/;
/**
 * The browser's own rendering of a frame (style recalculated, layout, paint): what it invalidates there
 * follows from a change made earlier, in the task that made it (judged there), or from an animation.
 */
const RENDERING = /^(UpdateLayoutTree|Layout|PrePaint|Paint|Layerize|UpdateLayer|CompositeLayers|AnimationFrame::StyleAndLayout|AnimationFrame::Render)$/;
const SCRIPT_SPAN = /^(FunctionCall|EvaluateScript|TimerFire|FireAnimationFrame|FireIdleCallback|EventDispatch|v8\.callFunction|V8\.Execute)$/;
/** Script of the page itself (not the harness's evaluations): a function call from a script the page loaded. */
const PAGE_SCRIPT = e => e.ph === 'X' && e.name === 'FunctionCall' && /^https?:/.test(String(e.args?.data?.url || ''));

/**
 * Round 14: the trace's verdict on every change the page made to what it shows while the task was
 * measured (up to `until`, seconds on the operator's clock, from `since` or the record's start): which counted step
 * caused it, by the same chains as a write (input, the timers, frames, answers and messages it led to),
 * or why nothing did. A DevTools command outside a step that acts is the harness's own (its reads, the
 * blind screenshot's neutral title and placeholders); only page script run inside one (an observer the
 * harness's change set off) is judged. One verdict per change of one entry into the page's code (a
 * timer's, a frame's, an answer's ...), at most `limit`. Returns [{ at, event, caused, step, why, chain }].
 */
export function screenCauses(trace, steps, { clock, originMs, until, since = null, reaction = 1, guard = GUARD_SECONDS, animatedAtStart = 0, limit = 2000 } = {}) {
  const model = trace.model || analyseTrace(trace.events);
  const slackUs = 2000 + (clock?.error_us || 0);
  const windows = steps.filter(s => typeof s.at === 'number').map(s => ({
    n: s.n, kind: s.kind, from: clockToTrace(typeof s.began === 'number' ? s.began : s.at, clock, originMs), at: clockToTrace(s.at, clock, originMs), to: clockToTrace(s.at + (s.took || 0), clock, originMs),
  }));
  // Where a chain begins decides (a counted step, or something before the clock, no step, another thread or
  // a server); its delays do not: a page that shows the end state 3 s after a click, on a timer or behind a
  // slow answer, shows it because of the click (a product configured to open a record on any click is the
  // product's answer to that click, as with writes), and a slow search under load is still the person's.
  const ctx = causeContext(model, windows, { reaction, link: Infinity, timerLimit: Infinity, repeating: true, guard, slackUs, animatedAtStart });
  // From `since` (seconds on the operator's clock; by default the whole record, which the runner starts
  // before verify()'s read on the start screen): every change before the clock's start is no step's.
  const from = since === null ? -Infinity : clockToTrace(since, clock, originMs);
  const to = clockToTrace(until, clock, originMs);
  const out = [];
  for (const [, thread] of model.threads) {
    if (!thread.main) continue;
    for (const t of thread.tasks) {
      if (t.end < from || t.ts > to) continue;
      const rendering = t.events.filter(e => e.ph === 'X' && RENDERING.test(e.name));
      const scripts = t.events.filter(e => e.ph === 'X' && SCRIPT_SPAN.test(e.name));
      let changes = t.events.filter(e => SCREEN_EVENTS.test(e.name) && e.ts >= from && e.ts <= to
        && !(rendering.some(r => spanHolds(r, e.ts)) && !scripts.some(x => spanHolds(x, e.ts) && x.ts >= Math.max(...rendering.filter(r => spanHolds(r, e.ts)).map(r => r.ts)))));
      if (!changes.length) continue;
      // The browser's own change of the window (its size, its focus, a screenshot's settings): a task that
      // ran none of the page's script, delivered no answer or event and started no navigation.
      if (!t.protocol && !t.activity && !changes.some(e => /^(ParseHTML|FrameLoader::StartNavigation)$/.test(e.name))) continue;
      if (t.protocol && !actingCommand(ctx, t)) {
        const page = t.events.filter(PAGE_SCRIPT);
        changes = changes.filter(e => page.some(s => spanHolds(s, e.ts)));
        if (!changes.length) continue;
      }
      // One verdict per entry into the page's code: the span (a timer's, a frame's, an input's ...) holding the change.
      const seen = new Set();
      const parses = t.events.filter(e => e.ph === 'X' && e.name === 'ParseHTML');
      for (const e of changes) {
        // A document's parsing (in whatever task the parser ran): the document is its navigation's, the
        // last one this frame's thread started before it (a redirect keeps the frame; a document whose
        // navigation the record does not hold started before it, or was the browser's own).
        const parse = e.name === 'ParseHTML' ? e : parses.find(x => spanHolds(x, e.ts));
        if (parse) {
          const url = String(parse.args?.beginData?.url || '');
          if (!url || url === 'about:blank') continue;
          const key = `parse:${url}`;
          if (seen.has(key)) continue;
          seen.add(key);
          const start = model.navigationStarts.filter(n => keyOf(n) === t.thread && n.ts < e.ts).sort((a, b) => b.ts - a.ts)[0] || null;
          const c = start ? judgePoint(ctx, start) : no(`the document ${pathOf(url)}, whose navigation started before the record began (or was not the page's)`);
          out.push({ at: round3(traceToClock(e.ts, clock, originMs)), event: e.name, ...(start && c.caused ? { ...c, chain: [...c.chain, `the document ${pathOf(url)}`] } : c) });
          if (out.length >= limit) return out;
          continue;
        }
        const span = [...t.timerFires, ...t.frameFires, ...t.idleFires, ...t.timed, ...t.inputs, ...t.postHandles].find(s => spanHolds(s, e.ts));
        const key = span ? `${span.name}:${span.ts}` : 'task';
        if (seen.has(key)) continue;
        seen.add(key);
        const c = judgePoint(ctx, { ...e, pid: Number(t.thread.split(':')[0]), tid: Number(t.thread.split(':')[1]) });
        out.push({ at: round3(traceToClock(e.ts, clock, originMs)), event: e.name, ...c });
        if (out.length >= limit) return out;
      }
    }
  }
  return out.sort((a, b) => a.at - b.at);
}

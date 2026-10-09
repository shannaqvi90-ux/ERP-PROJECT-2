// Preloaded (node --import) into the driver process before the host and before any driver code.
//
// The driver process runs under Node's permission model (lib/sandbox/bridge.mjs starts it with
// --permission): no child process, no worker thread, no native addon, no WASI, no inspector, no
// process.binding, and file writes only inside its own scratch folder. The permission model does
// not cover the network, so this file closes it: every way Node offers to open a connection or
// a listening socket ends in net.Socket.prototype.connect, net.Server.prototype.listen or a
// dgram socket, and each of those is replaced here with a refusal that cannot be undone
// (non-writable, non-configurable), before any other module of the process runs. Builtin modules
// are singletons, so process.getBuiltinModule('node:net'), require('net') and every import of it
// all see the refusal; syncBuiltinESMExports() carries it into named ESM imports too.
//
// The driver process reaches the products only through the harness process, over the IPC channel
// (lib/sandbox/host.mjs): the harness counts, times and refuses by phase. Nothing a driver does in
// this process can act on a product directly.
import net from 'node:net';
import tls from 'node:tls';
import dgram from 'node:dgram';
import http from 'node:http';
import https from 'node:https';
import http2 from 'node:http2';
import module from 'node:module';
import v8 from 'node:v8';
import childProcess from 'node:child_process';
import workerThreads from 'node:worker_threads';
import inspector from 'node:inspector';
import cluster from 'node:cluster';
import os from 'node:os';

export class SandboxRefusal extends Error {
  constructor(what) {
    super(`uncounted action from the driver process: ${what}. The driver process has no network; it acts only through the harness (op, ctx.page, fetch for set-up and verification).`);
    // The harness refuses the same way (lib/guard.mjs); the name lets the runner treat it as one.
    this.name = 'UncountedAction';
  }
}

// Every refusal is reported to the harness (with the send captured here, before any driver code),
// so a driver that catches it and carries on still has its run marked invalid.
const report = typeof process.send === 'function' ? process.send.bind(process) : null;
const refuse = what => function refused() {
  const err = new SandboxRefusal(what);
  try { report?.({ type: 'violation', message: err.message }); } catch { /* channel closed */ }
  throw err;
};

function lock(obj, key, value) {
  const d = Object.getOwnPropertyDescriptor(obj, key);
  if (d && !d.configurable) {
    if (d.writable === false && d.value === value) return;
    throw new Error(`sandbox lockdown: ${key} cannot be replaced`);
  }
  Object.defineProperty(obj, key, { value, writable: false, configurable: obj === globalThis && key === 'fetch', enumerable: d ? d.enumerable : false });
}

// Connections: TCP, Unix sockets and named pipes (net), TLS (tls.connect uses Socket#connect),
// HTTP/1 and HTTP/2 clients (their agents call net/tls connect), undici (fetch, WebSocket,
// EventSource: it calls net.connect). Listening sockets too: a driver has nothing to serve.
lock(net.Socket.prototype, 'connect', refuse('opening a network connection (net.Socket#connect)'));
lock(net, 'connect', refuse('opening a network connection (net.connect)'));
lock(net, 'createConnection', refuse('opening a network connection (net.createConnection)'));
lock(net.Server.prototype, 'listen', refuse('listening on a socket (net.Server#listen)'));
lock(tls, 'connect', refuse('opening a TLS connection (tls.connect)'));
for (const name of ['send', 'connect', 'bind', 'addMembership', 'addSourceSpecificMembership']) {
  if (typeof dgram.Socket.prototype[name] === 'function') lock(dgram.Socket.prototype, name, refuse(`a UDP socket (dgram.Socket#${name})`));
}
for (const [label, mod] of [['http', http], ['https', https]]) {
  lock(mod, 'request', refuse(`an HTTP request (${label}.request)`));
  lock(mod, 'get', refuse(`an HTTP request (${label}.get)`));
}
lock(http2, 'connect', refuse('an HTTP/2 connection (http2.connect)'));
for (const name of ['WebSocket', 'EventSource']) {
  if (name in globalThis) lock(globalThis, name, refuse(`a ${name} connection`));
}
// Child processes, worker threads, the inspector and cluster: the permission model refuses them
// already (ERR_ACCESS_DENIED); refusing them here as well reports the attempt to the harness.
for (const name of ['spawn', 'spawnSync', 'exec', 'execSync', 'execFile', 'execFileSync', 'fork']) {
  lock(childProcess, name, refuse(`starting a process (child_process.${name})`));
}
lock(childProcess.ChildProcess.prototype, 'spawn', refuse('starting a process (ChildProcess#spawn)'));
lock(workerThreads, 'Worker', refuse('starting a worker thread (worker_threads.Worker)'));
for (const name of ['open', 'url', 'waitForDebugger']) if (typeof inspector[name] === 'function') lock(inspector, name, refuse(`the inspector (inspector.${name})`));
lock(inspector.Session.prototype, 'connect', refuse('an inspector session'));
if (typeof inspector.Session.prototype.connectToMainThread === 'function') lock(inspector.Session.prototype, 'connectToMainThread', refuse('an inspector session'));
lock(cluster, 'fork', refuse('starting a process (cluster.fork)'));
// Loader hooks run in a thread of their own, outside this lockdown; V8 flags can open natives.
lock(module, 'register', refuse('registering module loader hooks (module.register)'));
if (typeof module.registerHooks === 'function') lock(module, 'registerHooks', refuse('registering module hooks (module.registerHooks)'));
lock(v8, 'setFlagsFromString', refuse('changing V8 flags (v8.setFlagsFromString)'));
// Denied by the permission model already; refused here too in case the process runs without it.
for (const name of ['binding', '_linkedBinding', 'dlopen']) {
  if (typeof process[name] === 'function') lock(process, name, refuse(`a native binding (process.${name})`));
}
// Round 9 (critic p01 r8): a verify() process has no clock. verify() runs once before the clock and
// twice after it, each time in a fresh process with the same arguments, so that it cannot tell
// which call it is in and answer "not done" only before the clock. A clock would tell it (set-up
// notes the time, verify() compares), so every clock a driver can read here stands still at the
// moment the run began: Date (and every date made without a value, Intl formatting without a
// date), performance.now and timeOrigin, process.hrtime and uptime, os.uptime and the processors'
// time counters, and the diagnostic report (it stamps the time). Timers still run (the process
// needs them), but a pause of over a second in verify() makes the run invalid anyway (lib/runner.mjs).
if (process.env.COMPARE_DRIVER_ROLE === 'verify') freezeClocks(Number(process.env.COMPARE_FROZEN_CLOCK) || 0);

function freezeClocks(at) {
  // Pinned: reading gives the frozen value; assigning is ignored (a driver that patches its own
  // clocks, in its own process, changes nothing for the harness, and must still load here).
  const pin = (obj, key, value) => {
    const d = Object.getOwnPropertyDescriptor(obj, key);
    Object.defineProperty(obj, key, { get: () => value, set: () => {}, configurable: false, enumerable: d ? d.enumerable : false });
  };
  const RealDate = Date;
  function FrozenDate(...args) {
    if (!new.target) return new RealDate(at).toString();
    return args.length ? new RealDate(...args) : new RealDate(at);
  }
  Object.defineProperty(FrozenDate, 'prototype', { value: RealDate.prototype, writable: false });
  for (const k of ['parse', 'UTC']) pin(FrozenDate, k, RealDate[k]);
  pin(FrozenDate, 'now', () => at);
  // A date's own constructor leads back to the real one otherwise.
  pin(RealDate.prototype, 'constructor', FrozenDate);
  pin(globalThis, 'Date', FrozenDate);
  // Intl formatting without a date formats "now".
  const P = Intl.DateTimeFormat.prototype;
  const formatGetter = Object.getOwnPropertyDescriptor(P, 'format').get;
  Object.defineProperty(P, 'format', { get() { const f = formatGetter.call(this); return (date = at) => f(date === undefined ? at : date); }, set: () => {}, configurable: false });
  const formatToParts = P.formatToParts;
  pin(P, 'formatToParts', function frozenFormatToParts(date = at) { return formatToParts.call(this, date === undefined ? at : date); });
  const zero = () => 0;
  const hr = () => [0, 0];
  hr.bigint = () => 0n;
  // The instance and its prototype: Performance.prototype.now.call(performance) reads the real clock otherwise.
  for (const o of [performance, Object.getPrototypeOf(performance)]) {
    pin(o, 'now', zero);
    pin(o, 'timeOrigin', at);
    for (const k of ['mark', 'measure', 'getEntries', 'getEntriesByName', 'getEntriesByType', 'eventLoopUtilization', 'timerify', 'toJSON']) {
      if (typeof o[k] === 'function') pin(o, k, refuse(`a clock (performance.${k}) in verify()`));
    }
  }
  pin(process, 'hrtime', hr);
  pin(process, 'uptime', zero);
  pin(os, 'uptime', zero);
  const realCpus = os.cpus.bind(os);
  pin(os, 'cpus', () => realCpus().map(c => ({ ...c, times: { user: 0, nice: 0, sys: 0, idle: 0, irq: 0 } })));
  for (const k of ['cpuUsage', 'resourceUsage']) if (typeof process[k] === 'function') pin(process, k, refuse(`a clock (process.${k}) in verify()`));
  try { pin(process, 'report', null); } catch { /* not configurable in this Node */ }
  if (typeof globalThis.Temporal === 'object') pin(globalThis, 'Temporal', undefined);
}

module.syncBuiltinESMExports();

// The host replaces fetch with the counted channel to the harness; until then there is none.
// Until then fetch is refused like every other connection.
lock(globalThis, 'fetch', refuse('a fetch before the harness channel is open'));
let fetchInstalled = false;
/** Called once by the host (it runs before any driver): fetch through the harness. */
export function installFetch(impl) {
  if (fetchInstalled) throw new SandboxRefusal('replacing the harness fetch');
  fetchInstalled = true;
  Object.defineProperty(globalThis, 'fetch', { value: impl, writable: false, configurable: false, enumerable: true });
}

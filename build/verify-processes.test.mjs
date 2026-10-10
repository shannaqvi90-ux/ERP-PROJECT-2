// Tests of ./erp verify's process handling (build/verify-processes.sh) and of the ratchet's quiet
// judgement (build/ratchet-check.mjs). Run with `node --test build/` (the web stage of ./erp verify).
//
// The stop test runs the real ./erp verify with a stand-in `docker` on PATH whose long commands
// (image build, docker run) sleep, so every stage is a live pipeline (`docker run … | tee | sed`, the
// e2e loop) when the run is sent SIGTERM, as on 2026-10-09 (critic p04 round 7: survivors held the
// verify slot for about 40 minutes). The run is started in a process group of its own, so every
// process it started, directly or not, is found by that group afterwards.
import { spawn, spawnSync } from 'node:child_process';
import { chmodSync, mkdtempSync, mkdirSync, readFileSync, rmSync, writeFileSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import test from 'node:test';
import assert from 'node:assert/strict';

const build = dirname(fileURLToPath(import.meta.url));
const root = dirname(build);

const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));

/** Process ids (with their commands) still in the process group. */
function groupMembers(pgid) {
  const ps = spawnSync('ps', ['-A', '-o', 'pid=,pgid=,stat=,args='], { encoding: 'utf8' });
  return ps.stdout.split('\n').map(l => l.trim()).filter(Boolean).map(l => {
    const [pid, group, stat, ...args] = l.split(/\s+/);
    return { pid: Number(pid), pgid: Number(group), stat, args: args.join(' ') };
  }).filter(p => p.pgid === pgid && !p.stat.startsWith('Z'));
}

function stubDocker(dir, log) {
  const bin = join(dir, 'bin');
  mkdirSync(bin);
  writeFileSync(join(bin, 'docker'), `#!/usr/bin/env bash
echo "$$ $*" >> "${log}"
case "$1" in
  info) [[ "\${2:-}" == --format ]] && echo "Stand-in Linux"; exit 0 ;;
  compose)
    case " $* " in
      *" version "*) exit 0 ;;
      *" build "*|*" up "*) exec sleep 300 ;;
      *) exit 0 ;;
    esac ;;
  run) exec sleep 300 ;;
  *) exit 0 ;;
esac
`);
  chmodSync(join(bin, 'docker'), 0o755);
  return bin;
}

test('a SIGTERM to ./erp verify ends every stage process it started (the e2e loop, docker run, tee, sed, the load sampler)', { timeout: 60_000 }, async () => {
  const dir = mkdtempSync(join(tmpdir(), 'erp-stop-'));
  try {
    const log = join(dir, 'docker.log');
    const bin = stubDocker(dir, log);
    const env = { ...process.env, PATH: `${bin}:${process.env.PATH}`, TMPDIR: dir, ERP_PROJECT: 'erp-stop-test', ERP_VERIFY_KEEP_DIR: join(dir, 'kept'), ERP_STOP_GRACE_SECONDS: '5', ERP_LOAD_SAMPLE_SECONDS: '1' };
    const run = spawn('bash', [join(root, 'erp'), 'verify'], { cwd: root, env, detached: true, stdio: ['ignore', 'pipe', 'pipe'] });
    let output = '';
    run.stdout.on('data', d => { output += d; });
    run.stderr.on('data', d => { output += d; });
    const exited = new Promise(resolve => run.on('exit', (code, signal) => resolve({ code, signal })));
    // Wait until all three stages are live pipelines: two docker runs and the stack's image build.
    const deadline = Date.now() + 20_000;
    for (;;) {
      const calls = existsSync(log) ? readFileSync(log, 'utf8') : '';
      const members = groupMembers(run.pid);
      if ((calls.match(/ run /g) ?? []).length >= 2 && / compose .* build /.test(calls) && members.some(m => /^tee /.test(m.args)) && members.some(m => /^sed /.test(m.args))) break;
      assert.ok(Date.now() < deadline, `the stages did not start: ${output}\n${calls}`);
      await sleep(200);
    }
    const before = groupMembers(run.pid);
    assert.ok(before.length >= 10, `expected the stage pipelines to be running, found ${before.map(m => m.args).join(' | ')}`);
    process.kill(run.pid, 'SIGTERM');
    const { code } = await exited;
    assert.equal(code, 143, output);
    // Every process the run started has ended by the time it exits.
    const left = groupMembers(run.pid);
    assert.deepEqual(left.map(m => `${m.pid} ${m.args}`), [], `still running after ./erp verify ended:\n${left.map(m => `${m.pid} ${m.args}`).join('\n')}`);
    assert.match(output, /verify FAILED \(exit 143\)/);
    // It cleaned up as on any failure: the run's containers and stack were removed.
    const calls = readFileSync(log, 'utf8');
    assert.match(calls, / rm -f erp-stop-test-verify-dotnet erp-stop-test-verify-web erp-stop-test-verify-e2e /);
    assert.match(calls, / compose .* down --volumes --remove-orphans/);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('stop_process_trees ends a process, its children and grandchildren, and nothing else', { timeout: 30_000 }, () => {
  const script = `
    source "${join(build, 'verify-processes.sh')}"
    ( sleep 300 | tee /dev/null | sed -u 's/^/x/' ) &
    tree=$!
    ( while :; do sleep 1; done ) &
    loop=$!
    sleep 300 &
    bystander=$!
    sleep 0.5
    stop_process_trees "$tree" "$loop"
    sleep 0.2
    for p in $(ps -A -o pid= -o ppid= | awk -v me=$$ '$2 == me { print $1 }'); do echo "child $p $(ps -o args= -p $p)"; done
    kill -0 "$bystander" && echo "bystander alive"
    kill "$bystander"
  `;
  const r = spawnSync('bash', ['-c', script], { encoding: 'utf8' });
  assert.equal(r.status, 0, r.stderr);
  assert.match(r.stdout, /bystander alive/);
  // The listing's own command substitution shows as a child with no command line by then.
  const children = r.stdout.split('\n').filter(l => /^child \d+ \S/.test(l) && !/ ps |awk/.test(l));
  assert.deepEqual(children.filter(l => !/sleep 300$/.test(l)), [], r.stdout);
  // Only the bystander's sleep is left among the shell's children.
  assert.equal(children.length, 1, r.stdout);
});

// ---- The ratchet's quiet judgement (verify.quietSeconds) ----

function ratchetRun({ seconds, start, end, samples, max = 3600 }) {
  const dir = mkdtempSync(join(tmpdir(), 'erp-ratchet-'));
  try {
    const ratchet = { minimums: { 'suite.dotnetTests': 0, 'suite.webUnitTests': 0, 'suite.e2eTests': 0, 'suite.compareTests': 0, 'suite.buildTests': 0 }, maximums: { 'verify.quietSeconds': max, 'verify.cpuSeconds': 100000 } };
    writeFileSync(join(dir, 'ratchet.json'), JSON.stringify(ratchet));
    const out = join(dir, 'out');
    mkdirSync(out);
    writeFileSync(join(out, 'verify-seconds'), `${seconds}\n`);
    if (start) writeFileSync(join(out, 'verify-start-load'), `${start} 16\n`);
    if (end) writeFileSync(join(out, 'verify-end-load'), `${end} 16\n`);
    if (samples) writeFileSync(join(out, 'verify-load-samples'), samples.map((l, i) => `${1700000000 + 15 * i} ${l} 16`).join('\n') + '\n');
    for (const s of ['dotnet', 'web', 'e2e', 'timing']) writeFileSync(join(out, `cpu-${s}`), '10\n');
    const r = spawnSync(process.execPath, [join(build, 'ratchet-check.mjs'), join(dir, 'ratchet.json'), out], { encoding: 'utf8' });
    return { status: r.status, text: r.stdout + r.stderr };
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
}

test('a run busy only in the middle is not judged quiet (critics p02 r8, p03 r8, p01 r10: load 0.28 at the start, 68-74 mid-run, 4.76 at the end)', () => {
  const r = ratchetRun({ seconds: 4283, start: 0.28, end: 4.76, samples: [0.28, 3, 40, 68, 74, 70, 20, 4.76] });
  assert.equal(r.status, 0, r.text);
  assert.match(r.text, /not judged: the machine was busy/);
  assert.match(r.text, /highest 74/);
});

test('a run quiet throughout and too slow fails, and one quiet throughout within the maximum passes', () => {
  const slow = ratchetRun({ seconds: 3700, start: 0.5, end: 1.2, samples: [0.5, 2, 7.9, 3, 1.2] });
  assert.equal(slow.status, 1, slow.text);
  assert.match(slow.text, /verify\.quietSeconds: \.\/erp verify took 3700 s on a quiet machine/);
  const fast = ratchetRun({ seconds: 3500, start: 0.5, end: 1.2, samples: [0.5, 2, 7.9, 3, 1.2] });
  assert.equal(fast.status, 0, fast.text);
  assert.match(fast.text, /judged/);
});

test('one busy sample anywhere makes the run busy; without samples (an older ./erp) the start and end readings decide as before', () => {
  const busy = ratchetRun({ seconds: 3700, start: 0.5, end: 1.2, samples: [0.5, 2, 8.1, 3, 1.2] });
  assert.equal(busy.status, 0, busy.text);
  assert.match(busy.text, /not judged/);
  const old = ratchetRun({ seconds: 3700, start: 0.5, end: 1.2 });
  assert.equal(old.status, 1, old.text);
  const oldBusy = ratchetRun({ seconds: 3700, start: 0.5, end: 9 });
  assert.equal(oldBusy.status, 0, oldBusy.text);
});

test('samples that cannot be read do not make a run quiet: an empty or broken sample file is judged busy only by its readings, never skipped silently', () => {
  // A sample file with no readable sample falls back to the start and end readings.
  const r = ratchetRun({ seconds: 3700, start: 0.5, end: 1.2, samples: [] });
  assert.equal(r.status, 1, r.text);
});

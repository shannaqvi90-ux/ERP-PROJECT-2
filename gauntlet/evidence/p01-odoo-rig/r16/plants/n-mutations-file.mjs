// Critic p01 r16: mutations of the round-16 code in lib/causation.mjs, judged by test/causation.test.mjs (whole file).
// Usage: node n-mutations.mjs <harness dir> [ids...]
import fs from 'node:fs'; import path from 'node:path'; import { spawnSync } from 'node:child_process';
const dir = process.argv[2]; const only = process.argv.slice(3);
const F = path.join(dir, 'lib/causation.mjs');
const M = [
 ['N1','inputDefaultActionCause applies to a DevTools command task too', "  if (task.protocol || !task.inputs.length) return null;\n  if (task.events.some(e => isEntry(task, e) && !task.inputs.includes(e)", "  if (!task.inputs.length) return null;\n  if (task.events.some(e => isEntry(task, e) && !task.inputs.includes(e)"],
 ['N2','focus/press states broadened to :hover and :checked', "const FOCUS_STATES = /^:(focus|focus-visible|focus-within|active)$/;", "const FOCUS_STATES = /^:(focus|focus-visible|focus-within|active|hover|checked)$/;"],
 ['N3','input state credited to the step running at the task end', "  const s = stepBefore(ctx, task.ts);\n  return s ? yes(s.n, [`the browser's own state", "  const s = stepBefore(ctx, task.end);\n  return s ? yes(s.n, [`the browser's own state"],
 ['N5','no delivered condition (any input in the task)', "  if (!delivered) return null;\n", ""],
 ['N8','forced work judged only by the latest maker of stale style', "    if (!groups.has(k)) groups.set(k, m.event);", "    if (!groups.has(k)) { groups.clear(); groups.set(k, m.event); }"],
 ['N9','forced work judged only by the earliest maker of stale style', "    if (!groups.has(k)) groups.set(k, m.event);", "    if (!groups.size) groups.set(k, m.event);"],
 ['N10','page script inside a DevTools command is no entry', "!(task.protocol && /^(FunctionCall|EvaluateScript)$/.test(e.name) && !PAGE_SCRIPT(e));", "!(task.protocol && /^(FunctionCall|EvaluateScript)$/.test(e.name));"],
 ['N11','a task a harness command posted is the step\'s (fallback laundering)', "  const c = pointCause(ctx, { ...from.event, pid: Number(from.task.thread.split(':')[0])", "  if (from.task.protocol) { const s0 = stepBefore(ctx, from.task.ts); if (s0) return yes(s0.n, ['planted'], from.task.ts); }\n  const c = pointCause(ctx, { ...from.event, pid: Number(from.task.thread.split(':')[0])"],
 ['N16','a style update after the point taken as forcing it', "LIFECYCLE_SPAN.test(e.name) && e.ts <= point.ts);", "LIFECYCLE_SPAN.test(e.name));"],
 ['N17','an update forced from inside a script taken (only the script-holds half removed)', "if (scriptHolds(last.ts) || entries.some(e => e.ts >= last.ts && e.ts <= point.ts)) return null;", "if (entries.some(e => e.ts >= last.ts && e.ts <= point.ts)) return null;"],
 ['N18','page code between the update and the point ignored', "if (scriptHolds(last.ts) || entries.some(e => e.ts >= last.ts && e.ts <= point.ts)) return null;", "if (scriptHolds(last.ts)) return null;"],
 ['N19','the update extended back over page code that ran between its steps', "    if (scriptHolds(s.ts) || entries.some(e => e.ts >= s.ts && e.ts < start)) break;", "    if (scriptHolds(s.ts)) break;"],
 ['N24','input state rule for any mark kind with a pseudo-class reason', "if (!point || point.name !== 'StyleRecalcInvalidationTracking' || point.args?.data?.reason !== 'PseudoClass'", "if (!point || point.args?.data?.reason !== 'PseudoClass'"],
];
const orig = fs.readFileSync(F, 'utf8');
function run() { const r = spawnSync(process.execPath, ['--test','--test-reporter=tap','--test-concurrency=1',process.env.TESTFILE || 'test/causation.test.mjs'], { cwd: dir, encoding: 'utf8', timeout: 30*60_000 });
  const fails = (r.stdout.match(/^\s*not ok \d+ - .*/mg) || []).filter(l => !/^\s*not ok \d+ - test\//.test(l)); const pass = (r.stdout.match(/^# pass (\d+)/m)||[])[1]; return { fails, pass }; }
const c = run(); console.log(`CONTROL unmutated: pass ${c.pass}, failing ${c.fails.length}`);
for (const [id, what, from, to] of M) { if (only.length && !only.includes(id)) continue;
  if (!orig.includes(from)) { console.log(`${id} ${what}: TEXT NOT FOUND`); continue; }
  fs.writeFileSync(F, orig.replace(from, to));
  try { const r = run(); console.log(`${id} ${what}: ${r.fails.length ? 'CAUGHT' : 'MISSED'} (failing ${r.fails.length}, pass ${r.pass}) ${r.fails.slice(0,2).map(s=>s.trim()).join(' | ')}`); }
  finally { fs.writeFileSync(F, orig); } }

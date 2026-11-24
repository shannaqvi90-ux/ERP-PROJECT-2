import fs from 'node:fs';
import { traceToClock } from './lib/causation.mjs';
const [,, file, from, to] = process.argv;
const d = JSON.parse(fs.readFileSync('/tmp/claude-1000/tr/' + file));
const T = ts => traceToClock(ts, d.clock, d.origin);
const SC=/^(ScheduleStyleRecalculation|InvalidateLayout|StyleRecalcInvalidationTracking|LayoutInvalidationTracking|StyleInvalidatorInvalidationTracking|ScheduleStyleInvalidationTracking)$/;
const by = new Map();
for (const e of d.events) { const k=e.pid+':'+e.tid; (by.get(k)||by.set(k,[]).get(k)).push(e); }
for (const [k,l] of by) for (const t of l.filter(e=>e.name==='RunTask'&&e.ph==='X')) {
  const s=T(t.ts); if (s<+from||s>+to) continue;
  const inside=l.filter(e=>e!==t&&e.ts>=t.ts&&e.ts<=t.ts+(t.dur||0));
  const sc=inside.filter(e=>SC.test(e.name));
  const spans=inside.filter(e=>e.ph==='X'&&/^(FunctionCall|EventDispatch|TimerFire|FireAnimationFrame|DevToolsSession::DispatchProtocolCommand|ResizeObserver|HitTest)$/.test(e.name)).slice(0,5).map(e=>e.name.slice(0,12)+':'+(e.args?.data?.functionName||e.args?.data?.type||''));
  const net=inside.filter(e=>/^Resource/.test(e.name)).map(e=>e.name);
  if (!sc.length && !spans.length && !net.length) continue;
  console.log(s.toFixed(3),'dur',((t.dur||0)/1000).toFixed(1),'sc',sc.length,net.join(','),spans.join(' '),sc.slice(0,2).map(e=>e.name.slice(0,12)+':'+(e.args?.data?.nodeName||'')+':'+(e.args?.data?.reason||'')+':'+((e.args?.data?.stackTrace||[]).slice(0,2).map(f=>f.functionName+'@'+f.lineNumber).join('<'))).join(' | '));
}

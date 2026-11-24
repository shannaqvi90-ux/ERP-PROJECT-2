import fs from 'node:fs';
import { screenCauses } from './lib/causation.mjs';
for (const f of fs.readdirSync('/tmp/claude-1000/tr')) {
  const d = JSON.parse(fs.readFileSync('/tmp/claude-1000/tr/' + f));
  const j = screenCauses({ events: d.events }, d.steps, { clock: d.clock, originMs: d.origin, until: 20, reaction: 1, animatedAtStart: 0, harness: d.harness, mainFrame: d.mainFrame });
  const bad = j.filter(c => !c.caused);
  if (bad.length) console.log(f, JSON.stringify(bad.slice(0,3)).slice(0,500));
}

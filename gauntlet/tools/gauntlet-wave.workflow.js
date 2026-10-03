export const meta = {
  name: 'gauntlet-wave',
  description: 'Build, integrate, blind-judge and record ERP platform pieces in rounds, then check the whole product hangs together',
  whenToUse: 'Each wave of the ERP platform-core gauntlet loop',
  phases: [
    { title: 'Build', detail: 'one builder per piece per round, in its own worktree' },
    { title: 'Integrate', detail: 'merge piece branch into the integration branch, keep tests green, push' },
    { title: 'Judge', detail: 'fresh critic per round: clean clone, gates, browser, database, Odoo side by side' },
    { title: 'Record', detail: 'verdict to ledger and progress.html, commit, push' },
    { title: 'Integrity', detail: 'one fresh agent checks the whole product hangs together' },
  ],
}

const REPO = '/home/user/ERP-PROJECT-2'
const BRANCH = 'claude/loving-lamport-kir0aw'
const TRAILER = 'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>\nClaude-Session: https://claude.ai/code/session_015Vsj7UJNNgPo4BphJAEzP5'
const ROUNDS = args.rounds || 2
const WAVE = args.wave

// One writer at a time in the integration working tree.
let lock = Promise.resolve()
function serial(fn) { const p = lock.then(fn, fn); lock = p.then(() => {}, () => {}); return p }

const ENV_NOTES = `
Environment facts (this machine):
- Docker: if \`docker info\` fails, start the daemon with \`(nohup dockerd >/tmp/dockerd.log 2>&1 &)\` and wait a few seconds.
- .NET 10 SDK is at /opt/dotnet (on PATH as \`dotnet\`). builds.dotnet.microsoft.com is blocked; NuGet, npm, Docker Hub and mcr.microsoft.com work.
- Chromium for Playwright is preinstalled (PLAYWRIGHT_BROWSERS_PATH=/opt/pw-browsers). Never run \`playwright install\`. If a pinned Playwright version cannot find its browser, launch with executablePath '/opt/pw-browsers/chromium-1194/chrome-linux/chrome' (check the exact path with ls).
- 4 CPUs, 15 GB RAM, ~17 GB free disk shared with up to three other agents working at the same time.
- The shared Odoo reference rig (compose project "odoo-reference", data in external volumes odoo-reference-db and odoo-reference-filestore; port 8069; sign-ins and commands in tools/odoo-reference/README.md) is never yours to stop: never run down/down -v/rm on it, even if its project name looks like your own. Other agents' containers are not yours either. Never run docker system prune, docker volume prune, docker image prune -a or docker builder prune -a. Clean up only your own compose project and the images it built (docker compose -p <yours> down -v --rmi local) when you finish.
- The integration working tree ${REPO} is shared by integrators, recorders and integrity checkers from more than one workflow. Before any write there (merge, commit, push, file copy), take the integration lock: \`until mkdir /home/user/.integration.lock 2>/dev/null; do if [ -n "$(find /home/user/.integration.lock -maxdepth 0 -mmin +120)" ]; then rm -rf /home/user/.integration.lock; fi; sleep 15; done; echo "<your role and piece> $(date -u +%FT%TZ)" > /home/user/.integration.lock/owner\`. Release it with \`rm -rf /home/user/.integration.lock\` as soon as your writes are pushed, including when you fail or give up. Builders and critics never write in ${REPO} and never take the lock.
- Commit trailer to end every commit message with:
${TRAILER}`

const VERDICT_SCHEMA = {
  type: 'object',
  properties: {
    piece: { type: 'string' },
    round: { type: 'integer' },
    commit: { type: 'string' },
    judged_at: { type: 'string', description: 'UTC ISO-8601 from `date -u +%Y-%m-%dT%H:%M:%SZ`' },
    critic: { type: 'string' },
    verdict: { type: 'string', enum: ['BLOCKED', 'LOSS', 'WIN'] },
    hard_gates: { type: 'array', items: { type: 'object', properties: {
      gate: { type: 'string' }, status: { type: 'string', enum: ['pass', 'fail', 'not-run'] }, evidence: { type: 'string' } },
      required: ['gate', 'status', 'evidence'] } },
    tests: { type: 'object', properties: {
      total: { type: ['integer', 'null'] }, passed: { type: ['integer', 'null'] }, failed: { type: ['integer', 'null'] },
      skipped: { type: ['integer', 'null'] }, command: { type: 'string' }, duration_s: { type: ['number', 'null'] } },
      required: ['total', 'passed', 'failed'] },
    clean_clone: { type: 'object', properties: {
      status: { type: 'string', enum: ['pass', 'fail', 'not-run'] }, seconds: { type: ['number', 'null'] }, evidence: { type: 'string' } },
      required: ['status'] },
    odoo: { type: 'array', items: { type: 'object', properties: {
      task: { type: 'string' },
      ours: { type: ['object', 'null'], properties: { steps: { type: ['number', 'null'] }, keystrokes: { type: ['number', 'null'] }, seconds: { type: ['number', 'null'] }, klm_seconds: { type: ['number', 'null'] } } },
      odoo: { type: ['object', 'null'], properties: { steps: { type: ['number', 'null'] }, keystrokes: { type: ['number', 'null'] }, seconds: { type: ['number', 'null'] }, klm_seconds: { type: ['number', 'null'] } } },
      winner: { type: 'string', enum: ['ours', 'odoo', 'tie', 'not-compared'] },
      notes: { type: 'string' } }, required: ['task', 'winner'] } },
    screenshots: { type: 'array', items: { type: 'string' }, description: 'repo-relative final paths under gauntlet/evidence/<piece>/r<round>/' },
    biggest_gap: { type: 'object', properties: { title: { type: 'string' }, detail: { type: 'string' }, evidence: { type: 'string' } }, required: ['title', 'detail', 'evidence'] },
    other_findings: { type: 'array', items: { type: 'string' } },
    human_gates: { type: 'array', items: { type: 'object', properties: { gate: { type: 'string' }, what: { type: 'string' } }, required: ['gate', 'what'] } },
    evidence_staging: { type: 'string', description: 'absolute path of the staging folder holding evidence files' },
    evidence_dir: { type: 'string', description: 'repo-relative final evidence folder gauntlet/evidence/<piece>/r<round>' },
  },
  required: ['piece', 'round', 'commit', 'judged_at', 'verdict', 'hard_gates', 'tests', 'clean_clone', 'odoo', 'screenshots', 'biggest_gap', 'evidence_staging', 'evidence_dir'],
}

const BUILD_SCHEMA = {
  type: 'object',
  properties: {
    branch: { type: 'string' },
    head_commit: { type: 'string' },
    suite_passes: { type: 'boolean' },
    test_command: { type: 'string' },
    notes_for_integrator: { type: 'string' },
    human_gates: { type: 'array', items: { type: 'object', properties: { gate: { type: 'string' }, what: { type: 'string' } }, required: ['gate', 'what'] } },
  },
  required: ['branch', 'head_commit', 'suite_passes', 'test_command'],
}

const INTEG_SCHEMA = {
  type: 'object',
  properties: {
    merged: { type: 'boolean' }, commit: { type: 'string' }, pushed: { type: 'boolean' },
    suite_passes: { type: 'boolean' }, problem: { type: 'string' },
  },
  required: ['merged', 'commit', 'pushed', 'suite_passes'],
}

const REC_SCHEMA = { type: 'object', properties: { recorded_id: { type: 'string' }, commit: { type: 'string' }, pushed: { type: 'boolean' } }, required: ['recorded_id', 'commit', 'pushed'] }

function portBlock(num) { const base = 20000 + 100 * num; return { base, builder: `${base}-${base + 49}`, critic: `${base + 50}-${base + 99}` } }

function builderPrompt(p, round, last) {
  const ports = portBlock(p.num)
  const wt = `/home/user/wt/${p.id}`
  let feedback = 'This is the first round for this piece. There is no previous verdict.'
  if (last && last.integration_failure) {
    feedback = `Your previous round's branch could not be integrated. The integrator reported:\n${last.integration_failure}\nFix that first.`
  } else if (last) {
    feedback = `The previous critic (a fresh, independent agent) judged commit ${last.commit} and returned this verdict. Close its single biggest gap first, then as many of its other findings as you can. Do not argue with it; make the product undeniably better.\n\n${JSON.stringify(last, null, 2)}`
  }
  if (p.resumeNote && round === p.startRound) feedback = `${p.resumeNote}\n\n${feedback}`
  if (p.leadNote) feedback = `${feedback}\n\nNOTE FROM THE LEAD (routing and scope, applies to every round of this run):\n${p.leadNote}`
  return `You are the BUILDER for piece ${p.id}, round ${round}, wave ${WAVE}, of a multi-tenant ERP platform core.

Read first, in this order: ${REPO}/CLAUDE.md (rules that never bend and human gates), ${REPO}/gauntlet/goal.md (the owner's goal and bar), ${REPO}/gauntlet/plan.md (pieces, gates, round protocol, ports), ${REPO}/gauntlet/pieces/${p.id}.md (your piece). Then read the existing code on the integration branch to fit into it.

Worktree (work only here, never in ${REPO}):
- If ${wt} does not exist: \`git -C ${REPO} worktree add ${wt} -b piece/${p.id} ${BRANCH}\` (if the branch piece/${p.id} already exists, add the worktree on it instead).
- Then in ${wt}: \`git merge --no-edit ${BRANCH}\` so you start from the latest integrated product. Resolve any conflict keeping both sides' behaviour.
- Your port block: ${ports.builder}. Use it for anything you run (compose project name \`b-${p.id}\`).

Order of work:
1. Gates first. Write or tighten the automated checks for the owner's hard gates (tenant isolation G1, permissions G2, clean clone G3) and the CLAUDE.md rule gates as they apply to what you are about to build, so they cover your piece before your piece exists. Gates may only get stricter: never weaken, skip, delete or narrow any test, gate, ratchet minimum or scenario (CLAUDE.md rule 9). Raise the minimums in gauntlet/ratchet.json when you add coverage.
2. Build the piece to production quality, not demo quality: real validation, real errors, English and Arabic for every string, keyboard-first dense UI, decimal money, audit on every business record, row-level security on every tenant table, a permission on every endpoint, OpenAPI for every endpoint, tests for everything.
3. Run the whole suite (the one command, \`./erp verify\`, once it exists) and get it green. If something outside your piece breaks, fix it minimally.
4. Record each architectural choice and its reason in docs/decisions/${p.id}-<slug>.md (piece-prefixed names so parallel builders do not collide). Dependencies: MIT, Apache-2.0, BSD or PostgreSQL licence only (CLAUDE.md rule 6). Beware packages that turned commercial (for example MediatR 13+, AutoMapper 15+, FluentAssertions 8+, MassTransit 9+, EPPlus 5+, Hangfire is LGPL): do not use them.
5. Commit on piece/${p.id} with factual messages (what changed, not how good it is). Commit work in progress at least every 30 to 45 minutes as well: this container can restart without warning and uncommitted work may be lost. Do not push; do not touch ${BRANCH} directly. Keep generated bulk data files (seed CSVs and the like) out of git; commit the generator instead.

Stay inside your piece's scope plus the shared extension points it needs. Other builders work in parallel on other pieces: keep shared kernel changes additive and backward compatible, put module code in the module's own folders, strings in the module's own resource files.

Odoo is a reference only (CLAUDE.md rule 7): never copy its code, views or text.

If your piece reaches a human gate in CLAUDE.md (tax/payroll/statutory rule, golden scenario results, paid service, real credential, deployment, action outside this repository, any change to CLAUDE.md or bar/), do not do that part: list it in human_gates and carry on with the rest. Do not edit gauntlet/ledger.md, gauntlet/progress.html, gauntlet/verdicts/ or gauntlet/needs-human.md; the lead's recorder owns them.

Before you finish, stop and remove your own containers (compose project b-${p.id}) so the machine has room for critics. The shared Odoo rig is the one exception: never stop or remove it.

${feedback}
${ENV_NOTES}

Return: the branch, its head commit, whether the whole suite passes, the exact test command, short factual notes for the integrator (what to watch when merging), and any human gates hit.`
}

function integratorPrompt(p, round, build) {
  return `You are the INTEGRATOR. Merge branch piece/${p.id} (head ${build.head_commit}, round ${round}) into the integration branch ${BRANCH} in ${REPO}.

Read ${REPO}/CLAUDE.md and ${REPO}/gauntlet/plan.md first.

Steps:
0. Take the integration lock (see environment notes) and hold it until step 5 is done or you give up; then release it.
1. In ${REPO}: confirm you are on ${BRANCH} with a clean tree for tracked files (\`git status\`). Record the current HEAD as the rollback point.
2. \`git merge --no-ff --no-edit piece/${p.id}\`. Resolve conflicts so both sides keep their behaviour; never resolve by dropping a test, a gate or a ratchet minimum (CLAUDE.md rule 9: the bar only moves up).
3. Build and run the whole suite with the one command (\`./erp verify\` if it exists; otherwise the closest full build and test). Use compose project name \`integ\` and ports 19000-19099 for anything you run. If a failure comes from the interaction between pieces, fix it minimally and commit the fix. If the piece itself is broken and you cannot fix it in a small, obvious change, reset ${BRANCH} to the rollback point (\`git reset --hard <rollback>\` is allowed only for your own unpushed merge) and report the problem precisely so the builder can fix it.
4. Stage only the files you changed (never \`git add -A\`: other agents may leave untracked files). Commit messages are factual. End every commit message with:
${TRAILER}
5. Push: \`git push -u origin ${BRANCH}\`; on a network failure retry up to 4 times waiting 2, 4, 8, 16 seconds.
6. Stop and remove your compose project \`integ\` and its images when done.

Notes from the builder (for merging only): ${build.notes_for_integrator || 'none'}
${ENV_NOTES}

Return whether it merged, the integration commit SHA now at the tip of ${BRANCH} (the commit the critic will judge), whether it is pushed, whether the whole suite passes, and the problem if it did not merge.`
}

function criticPrompt(p, round, commit) {
  const ports = portBlock(p.num)
  const clone = `/home/user/critic/${p.id}-r${round}`
  const staging = `/home/user/evidence-staging/${p.id}/r${round}`
  const evdir = `gauntlet/evidence/${p.id}/r${round}`
  return `You are a CRITIC with fresh context. Judge piece ${p.id} at integration commit ${commit} (round ${round}) of a multi-tenant ERP platform core. You have never seen the builder's work, reasoning or claims, and you must not look for them: do not read /home/user/wt/*, builder notes, or treat commit messages, READMEs or docs as evidence. Only what you run counts.

Read first: ${REPO}/CLAUDE.md, ${REPO}/gauntlet/goal.md (the goal and the bar you judge against), ${REPO}/gauntlet/plan.md, ${REPO}/gauntlet/pieces/${p.id}.md. Read earlier verdicts in ${REPO}/gauntlet/verdicts/ only to keep the bar from moving down (a gap that was fixed must stay fixed; counts may not fall).

Set up your own copy:
- \`git clone ${REPO} ${clone} && cd ${clone} && git checkout ${commit}\`. Time and run the clean-clone path exactly as a newcomer would: the one command that builds, migrates, seeds and runs all tests (G3), and the one command that brings up the running seeded demo. Use compose project \`c-${p.id}-r${round}\` and only ports ${ports.critic} (override through the environment the way the product documents; if it cannot be overridden, that is a finding).
- Evidence goes to ${staging}/ (create it). Screenshots: at most 12, JPEG or compressed PNG, named without product names. The recorder will copy the folder to ${REPO}/${evdir}/, so report screenshot paths as ${evdir}/<file>.

Judge the real thing:
1. Hard gates. Run the gate suites and read their output. Attack tenant isolation yourself as tenant A against tenant B through the API, the database role the app uses, exports, jobs and file paths that exist so far. Any real leak anywhere in the product means verdict BLOCKED, whichever piece caused it. Then test the gates themselves on THIS piece's surface: in your clone only, plant at least one deliberate tenant leak and one action with no permission check in code this piece added or owns (its endpoints, tables, jobs, files, screens; for p00-foundation the shared gate machinery and kernel; for p01-odoo-rig plant faults in the measuring instrument instead, for example an uncounted action, a wrong timer or a blindness leak, and check its own tests catch them). A gate that misses a fault planted in this piece's surface fails this round. If you find a gate weakness that lies outside this piece's surface, do not fail this piece for it: report it in other_findings starting with "[route: <owning piece id>]" so the lead sends it to the owner.
2. Tests: total, passed, failed, skipped, duration. Any failing test means BLOCKED.
3. The piece's scope from gauntlet/pieces/${p.id}.md: use it in a real browser (Playwright with Chromium) as a user would, in English and in Arabic, by keyboard; inspect the database. Missing or broken scope is a gap.
4. Odoo side by side. Sign-ins and commands are in tools/odoo-reference/README.md and gauntlet/compare/README.md (\`node run.mjs --task <id> --product both --out <your staging>/compare\`). If the Odoo reference rig is up (\`curl -s localhost:8069/web/login\`) and gauntlet/compare/ exists in your clone, perform the piece's comparison tasks in both products with the harness (shortest expert path in each), branding hidden, and record steps, keystrokes, machine seconds, modelled human seconds and screenshots. A tie is a loss. If you have to write or fix the "ours" driver for a task, do it in your clone and keep it in the evidence folder. If the rig is not up yet, mark tasks not-compared with the reason.
5. CLAUDE.md rules: tenant isolation, decimal money with currency, rate and base amount, audit of every business record, English and Arabic right-to-left, licences, no copying from Odoo.

Verdict: BLOCKED if any hard gate fails, any test fails, or the product does not build or run from the clean clone; LOSS if gates hold but the piece misses scope or loses or ties Odoo on any task; WIN only if gates hold, the scope is complete and it beats Odoo on every task compared. Whatever the verdict, name the single biggest gap (the one thing that most holds this piece back from beating Odoo for a UAE trading or manufacturing company), with evidence a builder can reproduce. List other findings briefly. List any human gate from CLAUDE.md the piece has reached.

When done: \`docker compose -p c-${p.id}-r${round} down -v --rmi local\` (or the product's equivalent) and delete ${clone} except nothing you still need; keep ${staging}. Never stop the odoo-reference containers or anyone else's.
${ENV_NOTES}

Return the verdict object. judged_at comes from \`date -u +%Y-%m-%dT%H:%M:%SZ\`. critic is "fresh critic, ${p.id} round ${round}". evidence_staging is ${staging}; evidence_dir is ${evdir}.`
}

function recorderPrompt(v, humanGates) {
  return `You are the RECORDER. Record one critic verdict in ${REPO} on branch ${BRANCH}. Do nothing else.

0. Take the integration lock (see environment notes); release it after the push, or if you give up.
1. Copy the evidence folder ${v.evidence_staging}/ to ${REPO}/${v.evidence_dir}/ (create it; skip if the staging folder is missing). Keep files under 1 MB each; if a PNG is larger, convert it to JPEG at quality 75 and fix its name in the verdict's screenshots list.
2. Write the verdict JSON below to /tmp/verdict-${v.piece}-r${v.round}.json exactly (after any screenshot renames) and run \`node ${REPO}/gauntlet/tools/record.mjs /tmp/verdict-${v.piece}-r${v.round}.json\`. If it reports a validation error, fix only the formatting (never the content) and rerun.
3. Human gates to add to ${REPO}/gauntlet/needs-human.md as new table rows (skip any already listed, number them after the last row, Raised = today's date, Status = Open): ${JSON.stringify(humanGates)}
4. \`git add gauntlet/verdicts gauntlet/ledger.md gauntlet/progress.html gauntlet/needs-human.md ${v.evidence_dir}\` and commit with message "Record verdict: ${v.piece} round ${v.round} ${v.verdict}" ending with:
${TRAILER}
5. \`git push -u origin ${BRANCH}\`; on a network failure retry up to 4 times waiting 2, 4, 8, 16 seconds. If the push is rejected because the remote moved, \`git pull --no-rebase origin ${BRANCH}\` and push again.

Verdict JSON:
${JSON.stringify(v, null, 2)}

Return the recorded id printed by record.mjs, the commit SHA and whether it was pushed.`
}

async function runPiece(p) {
  let last = p.lastVerdict || null
  const results = []
  for (let i = 0; i < ROUNDS; i++) {
    const round = p.startRound + i
    log(`${p.id}: round ${round} build`)
    const build = await agent(builderPrompt(p, round, last), { label: `build:${p.id}:r${round}`, phase: 'Build', schema: BUILD_SCHEMA, effort: 'high' })
    if (!build) { log(`${p.id}: builder died in round ${round}`); results.push({ round, outcome: 'builder-died' }); continue }
    const integ = await serial(() => agent(integratorPrompt(p, round, build), { label: `integrate:${p.id}:r${round}`, phase: 'Integrate', schema: INTEG_SCHEMA, effort: 'medium' }))
    if (!integ || !integ.merged || !integ.pushed) {
      const problem = integ ? (integ.problem || 'merged/pushed false without detail') : 'integrator died'
      log(`${p.id}: round ${round} not integrated: ${problem.slice(0, 200)}`)
      last = { integration_failure: problem }
      results.push({ round, outcome: 'not-integrated', problem, human_gates: build.human_gates || [] })
      continue
    }
    log(`${p.id}: round ${round} judging ${integ.commit.slice(0, 10)}`)
    const verdict = await agent(criticPrompt(p, round, integ.commit), { label: `judge:${p.id}:r${round}`, phase: 'Judge', schema: VERDICT_SCHEMA, effort: 'high' })
    if (!verdict) { log(`${p.id}: critic died in round ${round}`); results.push({ round, outcome: 'critic-died', commit: integ.commit }); continue }
    const humanGates = [...(build.human_gates || []), ...(verdict.human_gates || [])].map(g => ({ piece: p.id, ...g }))
    const rec = await serial(() => agent(recorderPrompt(verdict, humanGates), { label: `record:${p.id}:r${round}`, phase: 'Record', schema: REC_SCHEMA, effort: 'low' }))
    log(`${p.id}: round ${round} ${verdict.verdict} — ${verdict.biggest_gap.title}`)
    results.push({ round, outcome: 'judged', verdict: verdict.verdict, gap: verdict.biggest_gap.title, commit: integ.commit, recorded: rec ? rec.recorded_id : null })
    last = verdict
  }
  return { piece: p.id, nextRound: p.startRound + ROUNDS, lastVerdict: last, results }
}

const pieceResults = await parallel(args.pieces.map(p => () => runPiece(p)))

let integrity = null
if (args.integrity) {
  phase('Integrity')
  integrity = await serial(() => agent(`You are the INTEGRITY CHECKER for wave ${WAVE}, a fresh agent. In ${REPO} on branch ${BRANCH}, check that the whole ERP platform core still hangs together after this wave, and fix inconsistencies between pieces.

Read ${REPO}/CLAUDE.md, ${REPO}/gauntlet/goal.md, ${REPO}/gauntlet/plan.md and the piece specs in ${REPO}/gauntlet/pieces/.

Run your checks in a fresh clone. Take the integration lock (see environment notes) only for your writes to ${REPO}, and release it after each push.

Check, and fix what is inconsistent:
- \`./erp verify\` from a fresh clone of ${BRANCH} passes (compose project \`integrity\`, ports 19100-19199); \`./erp up\` gives a running seeded demo.
- Pieces use the shared mechanisms the same way: one tenancy mechanism, one permission naming scheme, one audit path, one money type, one list/form framework, one way of registering modules, strings, menus and API endpoints. Duplicated or diverging mechanisms are inconsistencies.
- Every module's strings exist in English and Arabic; navigation reaches every screen; the API document covers every endpoint; docs/decisions do not contradict each other or the code.
- Gate suites cover every module and every endpoint; ratchet minimums only went up.
Never weaken a test or gate. Stage only files you changed, commit with factual messages ending with:
${TRAILER}
and push with \`git push -u origin ${BRANCH}\` (retry 4 times with 2, 4, 8, 16 s waits on network failure).
Write your findings and fixes to ${REPO}/gauntlet/integrity/wave-${WAVE}-${args.integrityTag || 'a'}.md, commit and push it. Clean up your containers.
${ENV_NOTES}

Return a short report: what you checked, what was inconsistent, what you fixed (with commit SHAs), and what you could not fix.`, { label: `integrity:wave-${WAVE}`, phase: 'Integrity', effort: 'high' }))
}

return { wave: WAVE, pieces: pieceResults, integrity }

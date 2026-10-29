export const meta = {
  name: 'gauntlet-wave',
  description: 'Build, integrate, blind-judge and record ERP platform pieces in rounds, then check the whole product hangs together',
  whenToUse: 'Each wave of the ERP platform-core gauntlet loop',
  phases: [
    { title: 'Build', detail: 'one builder per piece per round, in its own worktree; it builds, commits and runs one full verify' },
    { title: 'Fix', detail: 'when a verify fails, a new agent with fresh context gets the failure summary, the branch and the piece spec and fixes it' },
    { title: 'Integrate', detail: 'merge piece branch into the integration branch, keep tests green, push' },
    { title: 'Judge', detail: 'fresh critic per round: clean clone, gates, browser, database, Odoo side by side' },
    { title: 'Record', detail: 'verdict to ledger and progress.html, commit, push' },
    { title: 'Integrity', detail: 'one fresh agent checks the whole product hangs together' },
  ],
}

// Where the run lives. Defaults are the original cloud machine; a local run passes args.root (and args.repo if the clone is elsewhere).
const ROOT = args.root || '/home/user'
const REPO = args.repo || `${ROOT}/ERP-PROJECT-2`
const BRANCH = 'claude/loving-lamport-kir0aw'
const TRAILER = args.trailer || 'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>\nClaude-Session: https://claude.ai/code/session_015Vsj7UJNNgPo4BphJAEzP5'
const ROUNDS = args.rounds || 2
const WAVE = args.wave
// Owner decision 2026-10-28: builders and fixers run on Sonnet at medium effort, each in a short session with fresh context.
// Critics keep the session model (Opus) at high effort; integrators and recorders are unchanged.
const BUILDER_MODEL = args.builderModel || 'sonnet'
const BUILDER_EFFORT = args.builderEffort || 'medium'
const MAX_FIXES = args.maxFixes || 5

// One writer at a time in the integration working tree.
let lock = Promise.resolve()
function serial(fn) { const p = lock.then(fn, fn); lock = p.then(() => {}, () => {}); return p }

const MACHINE_NOTES = args.machineNotes || `
Environment facts (this machine):
- Docker: if \`docker info\` fails, start the daemon with \`(nohup dockerd >/tmp/dockerd.log 2>&1 &)\` and wait a few seconds.
- .NET 10 SDK is at /opt/dotnet (on PATH as \`dotnet\`). builds.dotnet.microsoft.com is blocked; NuGet, npm, Docker Hub and mcr.microsoft.com work.
- Chromium for Playwright is preinstalled (PLAYWRIGHT_BROWSERS_PATH=/opt/pw-browsers). Never run \`playwright install\`. If a pinned Playwright version cannot find its browser, launch with executablePath '/opt/pw-browsers/chromium-1194/chrome-linux/chrome' (check the exact path with ls).
- 4 CPUs, 15 GB RAM and under 10 GB of free disk, shared with other agents. Disk is the scarcest resource: put temporary copies under your own scratch folder, delete them when done, remove exited test containers you created, and never copy the whole repository more than once at a time.
`
const ENV_NOTES = `
${MACHINE_NOTES}
- The shared Odoo reference rig (compose project "odoo-reference", data in external volumes odoo-reference-db and odoo-reference-filestore; port 8069; sign-ins and commands in tools/odoo-reference/README.md) is never yours to stop: never run down/down -v/rm on it, even if its project name looks like your own. Other agents' containers are not yours either. Never run docker system prune, docker volume prune, docker image prune -a or docker builder prune -a. Clean up only your own compose project and the images it built (docker compose -p <yours> down -v --rmi local) when you finish.
- Full verify runs share the machine: run every full \`./erp verify\` (and any other whole-suite run) through the verify slots, \`${REPO}/gauntlet/tools/verify-slot.sh ./erp verify\` (environment variables before it as usual). It waits for one of two machine-wide slots (an integrator's verify, run with ERP_VERIFY_ALONE=1, takes both and runs alone; while one waits, no new verify starts, so expect to wait longer) (lock files in /home/shan/.verify-slots; the owner's PC ran out of memory with three); time a run from the 'slot taken' line it prints, not from when you started waiting. Never bypass it: seven verifies at once once drove the load to 420 and every run failed on timeouts.
- Logs: the scratch folder is shared by every agent of the session, so never write to a generic name such as verify.log there (two agents once wrote the same verify.log and one truncated the other's). Name every log after your role, piece and round (for example verify-integrator-p02-tenancy-r6.log), and for a full verify let the slot script write it: \`ERP_VERIFY_LOG=<scratch>/verify-<role>-<piece>-r<round>.log ${REPO}/gauntlet/tools/verify-slot.sh ./erp verify\` (no \`> file\` redirect; the script locks the log and refuses one that another run still holds).
- Waiting: you run as a workflow agent, so a reply of yours with no tool call ends your run at once and is taken as your final answer. In serial-8 two integrators wrote 'waiting for the monitor notification', were made to return while their verify was queued or starting, and their rounds were lost. Never end a turn to wait, and do not rely on Monitor or background-task notifications to wake you. To wait for a long run (a queued or running verify, a build), start it in the background, then keep calling foreground Bash commands of under 10 minutes each (timeout 590000) that block until it ends, for example \`timeout 570 tail --pid=<its pid> -f /dev/null; tail -3 <its log>\`, and repeat them until it has finished, however many hours that takes.
- Processes: every agent runs as the same Linux user, so \`pkill -f\`, \`killall\` or any kill by name pattern also kills other agents' runs (it once killed four \`./erp verify\` runs at once). Stop only your own processes, by the PID you started or by your own compose project and container names.
- The integration working tree ${REPO} is shared by integrators, recorders and integrity checkers from more than one workflow. Before any write there (merge, commit, push, file copy), take the integration lock: \`until mkdir ${ROOT}/.integration.lock 2>/dev/null; do if [ -n "$(find ${ROOT}/.integration.lock -maxdepth 0 -mmin +360)" ]; then rm -rf ${ROOT}/.integration.lock; fi; sleep 15; done; echo "<your role and piece> $(date -u +%FT%TZ)" > ${ROOT}/.integration.lock/owner\`. Release it with \`rm -rf ${ROOT}/.integration.lock\` as soon as your writes are pushed, including when you fail or give up. Builders and critics never write in ${REPO} and never take the lock.
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
      winner: { type: 'string', enum: ['ours', 'odoo', 'tie', 'tie-accepted', 'known-loss-candidate', 'known-loss', 'not-compared'] },
      notes: { type: 'string' } }, required: ['task', 'winner'] } },
    screenshots: { type: 'array', items: { type: 'string' }, description: 'repo-relative final paths under gauntlet/evidence/<piece>/r<round>/' },
    biggest_gap: { type: 'object', properties: { title: { type: 'string' }, detail: { type: 'string' }, evidence: { type: 'string' } }, required: ['title', 'detail', 'evidence'] },
    gate_gaps: { type: 'array', description: 'faults you planted in your clone that the gates missed while the real product showed no such fault (owner decision 2026-10-23, needs-human #27); they do not block', items: { type: 'object', properties: {
      owner: { type: 'string', description: 'piece id that owns the gate' }, gate: { type: 'string' }, planted: { type: 'string', description: 'what you planted and where' }, evidence: { type: 'string' } },
      required: ['owner', 'gate', 'planted', 'evidence'] } },
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
    failure_summary: { type: 'string', description: 'when suite_passes is false: the failing stage(s), each failing test or check with its error lines, the verify log and .verify-failed folder paths, the processor time, the load, and what you suspect; empty when it passed' },
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
  const wt = `${ROOT}/wt/${p.id}`
  let feedback = 'This is the first round for this piece. There is no previous verdict.'
  if (last && last.integration_failure) {
    feedback = `Your previous round's branch could not be integrated. The integrator reported:\n${last.integration_failure}\nFix that first.`
  } else if (last) {
    feedback = `The previous critic (a fresh, independent agent) judged commit ${last.commit} and returned this verdict. Close its single biggest gap first, then as many of its other findings as you can. Where a comparison task ties Odoo, try to win it first; if you cannot, record in your decision record the task, the paths you tried and why ours cannot be shorter (the owner's tie rule of 2026-10-17 in gauntlet/goal.md accepts a tie only after that). Do not argue with it; make the product undeniably better.\n\n${JSON.stringify(last, null, 2)}`
  }
  feedback = `${feedback}\n\nGate backlog: ${REPO}/gauntlet/gate-backlog.md lists gate gaps that no longer block a round (owner decision of 2026-10-23, needs-human #27). Close the Open rows whose owner is ${p.id} in this round as well, 'uncovered surface' rows first, by making the gates stricter; name each row you closed in your notes for the integrator. Do not edit gate-backlog.md yourself; the lead closes rows.`
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
3. While building, run the targeted tests of what you change. When the piece is built and committed, run the whole suite once (the one command, \`./erp verify\`, through the verify slots). If it passes, return. If it fails, do not start fixing it in this session: commit any work in progress so the tree is clean and return suite_passes=false with a precise failure_summary (failing stages, each failing test or check with its error lines, the log and .verify-failed folder paths, processor time, load, and what you suspect). A new agent with fresh context then fixes it from that summary (owner decision of 2026-10-28, to keep sessions short).
4. Record each architectural choice and its reason in docs/decisions/${p.id}-<slug>.md (piece-prefixed names so parallel builders do not collide). Dependencies: MIT, Apache-2.0, BSD or PostgreSQL licence only (CLAUDE.md rule 6). Beware packages that turned commercial (for example MediatR 13+, AutoMapper 15+, FluentAssertions 8+, MassTransit 9+, EPPlus 5+, Hangfire is LGPL): do not use them.
5. Commit on piece/${p.id} with factual messages (what changed, not how good it is). Commit work in progress at least every 30 to 45 minutes as well: this container can restart without warning and uncommitted work may be lost. Do not push; do not touch ${BRANCH} directly. Keep generated bulk data files (seed CSVs and the like) out of git; commit the generator instead.

Stay inside your piece's scope plus the shared extension points it needs. Other builders work in parallel on other pieces: keep shared kernel changes additive and backward compatible, put module code in the module's own folders, strings in the module's own resource files.

Odoo is a reference only (CLAUDE.md rule 7): never copy its code, views or text.

If your piece reaches a human gate in CLAUDE.md (tax/payroll/statutory rule, golden scenario results, paid service, real credential, deployment, action outside this repository, any change to CLAUDE.md or bar/), do not do that part: list it in human_gates and carry on with the rest. Do not edit gauntlet/ledger.md, gauntlet/progress.html, gauntlet/verdicts/ or gauntlet/needs-human.md; the lead's recorder owns them.

Before you finish, stop and remove your own containers (compose project b-${p.id}) so the machine has room for critics. The shared Odoo rig is the one exception: never stop or remove it.

${feedback}
${ENV_NOTES}

Return: the branch, its head commit, whether the whole suite passes, the exact test command, the failure summary if it failed, short factual notes for the integrator (what to watch when merging), and any human gates hit.`
}

// A fresh, short session per failed verify (owner decision 2026-10-28): only the failure summary, the branch and the piece spec.
function fixerPrompt(p, round, prev, attempt) {
  const ports = portBlock(p.num)
  const wt = `${ROOT}/wt/${p.id}`
  return `You are a FIXER for piece ${p.id}, round ${round}, wave ${WAVE} (fix ${attempt} of at most ${MAX_FIXES} this round), of a multi-tenant ERP platform core. A builder built this round on branch piece/${p.id} in the worktree ${wt} (head ${prev.head_commit}), and its full verify failed. Your job: make the full verify pass, without weakening anything.

Read: ${REPO}/gauntlet/pieces/${p.id}.md (the piece spec). Read other files only as the failure needs them; the piece's decision records are docs/decisions/${p.id}-*.md in the worktree.

Failure summary from the previous agent:
${prev.failure_summary || prev.notes_for_integrator || '(none given: run the full verify first and read what fails)'}

Rules:
- Work only in ${wt}, on piece/${p.id}. Do not push; never touch ${BRANCH} or ${REPO} directly. Your port block is ${ports.builder} (compose project \`b-${p.id}\`).
- Fix the cause. Never weaken, skip, delete or narrow a test, gate, ratchet minimum or scenario, and never add timeout increases or test re-runs to get a pass (CLAUDE.md rule 9). If the cause is outside this piece, fix it minimally and say so. If a fix would need a human gate in CLAUDE.md, do not do it: list it in human_gates.
- Run the failing tests on their own first, then the whole suite once through the verify slots (\`ERP_VERIFY_LOG=<scratch>/verify-fixer-${p.id}-r${round}-f${attempt}.log ${REPO}/gauntlet/tools/verify-slot.sh ./erp verify\` with your ports and compose project).
- Commit with factual messages; record anything architectural in the piece's decision record, with the verify run, its result and processor time.
- If the full verify passes, return suite_passes=true. If it fails again, commit what you have so the tree is clean and return suite_passes=false with a precise failure_summary for the next fixer (failing stages, each failing test or check with its error lines, the log and .verify-failed folder paths, processor time, load, what you tried and what you suspect). Do not start a second round of fixes in this session.
- Before you finish, stop and remove your own containers (compose project b-${p.id}); never the shared Odoo rig.
${p.leadNote ? `\nConstraints from the lead for this piece:\n${p.leadNote}\n` : ''}${ENV_NOTES}

Return: the branch, its head commit, whether the whole suite passes, the exact test command, the failure summary if it failed, short factual notes for the integrator (everything this round's merge should know, including the builder's notes below), and any human gates hit.

Builder's notes for the integrator so far: ${prev.notes_for_integrator || 'none'}`
}

// Build one round: a builder, then fresh fixers while the verify fails. p.fixFirst resumes a round built earlier whose verify failed.
async function buildRound(p, round, last) {
  let build
  if (p.fixFirst && round === p.startRound) {
    build = { head_commit: `piece/${p.id} as it is in ${ROOT}/wt/${p.id}`, suite_passes: false, failure_summary: p.fixFirst, notes_for_integrator: p.builtNotes || '' }
  } else {
    log(`${p.id}: round ${round} build`)
    build = await retrying(`${p.id} r${round} build`, () => agent(builderPrompt(p, round, last), { label: `build:${p.id}:r${round}`, phase: 'Build', schema: BUILD_SCHEMA, model: BUILDER_MODEL, effort: BUILDER_EFFORT }))
    if (!build) return null
  }
  const gates = [...(build.human_gates || [])]
  for (let attempt = 1; !build.suite_passes && attempt <= MAX_FIXES; attempt++) {
    log(`${p.id}: round ${round} verify failed; fixer ${attempt}`)
    const fixed = await retrying(`${p.id} r${round} fix ${attempt}`, () => agent(fixerPrompt(p, round, build, attempt), { label: `fix:${p.id}:r${round}:${attempt}`, phase: 'Fix', schema: BUILD_SCHEMA, model: BUILDER_MODEL, effort: BUILDER_EFFORT }))
    if (!fixed) return null
    gates.push(...(fixed.human_gates || []))
    build = fixed
  }
  if (!build.suite_passes) log(`${p.id}: round ${round} still failing after ${MAX_FIXES} fixers; the integrator gets the failure summary`)
  return { ...build, human_gates: gates, notes_for_integrator: build.suite_passes ? build.notes_for_integrator : `${build.notes_for_integrator || ''}\nThe piece's own verify still failed: ${build.failure_summary || ''}` }
}

function integratorPrompt(p, round, build) {
  return `You are the INTEGRATOR. Merge branch piece/${p.id} (head ${build.head_commit}, round ${round}) into the integration branch ${BRANCH} in ${REPO}.

Read ${REPO}/CLAUDE.md and ${REPO}/gauntlet/plan.md first.

Steps:
0. Take the integration lock (see environment notes) and hold it until step 5 is done or you give up; then release it.
1. In ${REPO}: confirm you are on ${BRANCH} with a clean tree for tracked files (\`git status\`). Record the current HEAD as the rollback point. If an earlier integrator died part-way, HEAD may already hold an unpushed merge of piece/${p.id}: then origin/${BRANCH} is the rollback point and you verify (and if needed fix) that merge instead of merging again.
2. \`git merge --no-ff --no-edit piece/${p.id}\`. Resolve conflicts so both sides keep their behaviour; never resolve by dropping a test, a gate or a ratchet minimum (CLAUDE.md rule 9: the bar only moves up).
3. Build and run the whole suite with the one command (\`./erp verify\` if it exists; otherwise the closest full build and test). Run it alone: \`ERP_VERIFY_ALONE=1\` before the slot script (owner's decision 2026-10-19, needs-human #22), so it takes both verify slots and no other full verify runs beside it; it waits for running verifies to finish, and no new one starts meanwhile. Only integrators use ERP_VERIFY_ALONE. Use compose project name \`integ\` and ports 19000-19099 for anything you run. If a failure comes from the interaction between pieces, fix it minimally and commit the fix. If the piece itself is broken and you cannot fix it in a small, obvious change, reset ${BRANCH} to the rollback point (\`git reset --hard <rollback>\` is allowed only for your own unpushed merge) and report the problem precisely so the builder can fix it. If you stop or must return before a full verify of your merge has passed (for example while still waiting for a verify slot), first reset ${BRANCH} to the rollback point so no unverified merge stays in ${REPO} after you release the lock; on 2026-10-10 a merge left there unverified (c43966c) was pushed by a recorder that committed on top of it.
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
  const clone = `${ROOT}/critic/${p.id}-r${round}`
  const staging = `${ROOT}/evidence-staging/${p.id}/r${round}`
  const evdir = `gauntlet/evidence/${p.id}/r${round}`
  return `You are a CRITIC with fresh context. Judge piece ${p.id} at integration commit ${commit} (round ${round}) of a multi-tenant ERP platform core. You have never seen the builder's work, reasoning or claims, and you must not look for them: do not read ${ROOT}/wt/*, builder notes, or treat commit messages, READMEs or docs as evidence. Only what you run counts.

Read first: ${REPO}/CLAUDE.md, ${REPO}/gauntlet/goal.md (the goal and the bar you judge against), ${REPO}/gauntlet/plan.md, ${REPO}/gauntlet/pieces/${p.id}.md. Read earlier verdicts in ${REPO}/gauntlet/verdicts/ only to keep the bar from moving down (a gap that was fixed must stay fixed; counts may not fall).

Set up your own copy:
- \`git clone ${REPO} ${clone} && cd ${clone} && git checkout ${commit}\`. Time and run the clean-clone path exactly as a newcomer would: the one command that builds, migrates, seeds and runs all tests (G3), and the one command that brings up the running seeded demo. Use compose project \`c-${p.id}-r${round}\` and only ports ${ports.critic} (override through the environment the way the product documents; if it cannot be overridden, that is a finding).
- Evidence goes to ${staging}/ (create it). Screenshots: at most 12, JPEG or compressed PNG, named without product names. The recorder will copy the folder to ${REPO}/${evdir}/, so report screenshot paths as ${evdir}/<file>.

Judge the real thing:
1. Hard gates. Run the gate suites and read their output. Attack tenant isolation yourself as tenant A against tenant B through the API, the database role the app uses, exports, jobs and file paths that exist so far. Any real leak anywhere in the product means verdict BLOCKED, whichever piece caused it. Then test the gates themselves on THIS piece's surface: in your clone only, plant at least one deliberate tenant leak and one action with no permission check in code this piece added or owns (its endpoints, tables, jobs, files, screens; for p00-foundation the shared gate machinery and kernel; for p01-odoo-rig plant faults in the measuring instrument instead, for example an uncounted action, a wrong timer or a blindness leak, and check its own tests catch them). Under the owner's decision of 2026-10-23 (gauntlet/goal.md, needs-human #27) there are two kinds of finding. A product fault (a real leak or missing permission check in the product as built, a money, ledger or audit error, a failing test, a product that does not build or run) fails the round. A gate gap (a fault you planted in your clone that the gates miss, while the real product shows no such fault) does not fail the round: list each in gate_gaps with the owning piece, and still name it as the biggest gap if it is the most important thing you found. One limit: if a surface this piece added (an endpoint, screen, job, export or file path) is not reached by the isolation or permission gates at all, that fails the round (gates come before building); say plainly which surface. If you find a gate weakness that lies outside this piece's surface, do not fail this piece for it: report it in other_findings starting with "[route: <owning piece id>]" so the lead sends it to the owner.
2. Tests: total, passed, failed, skipped, duration. Any failing test means BLOCKED.
3. The piece's scope from gauntlet/pieces/${p.id}.md: use it in a real browser (Playwright with Chromium) as a user would, in English and in Arabic, by keyboard; inspect the database. Missing or broken scope is a gap.
4. Odoo side by side. Sign-ins and commands are in tools/odoo-reference/README.md and gauntlet/compare/README.md (\`node run.mjs --task <id> --product both --out <your staging>/compare\`). If the Odoo reference rig is up (\`curl -s localhost:8069/web/login\`) and gauntlet/compare/ exists in your clone, perform the piece's comparison tasks in both products with the harness (shortest expert path in each), branding hidden, and record steps, keystrokes, machine seconds, modelled human seconds and screenshots. A tie is a loss, except under the owner's tie rule of 2026-10-17 (gauntlet/goal.md bar item 2, needs-human #21): record a task as tie-accepted only when ours is worse on no metric of that path, the same task already tied in an earlier judged round of this piece (check gauntlet/verdicts/), the builder's decision record names the task, the paths it tried and why ours cannot be shorter, and your own tries of ours' paths find none that wins; say in the task's notes how each condition holds. If Odoo is strictly better on a task, the builder's decision record names that task and the paths it tried, and your own tries find no path for ours to win or tie, mark it known-loss-candidate (the owner may then sign it off as a known loss). Mark a task known-loss only if gauntlet/needs-human.md shows the owner signed off that task for this piece as a known loss and ours is no worse than it was then; a known-loss task does not count against a WIN. If you have to write or fix the "ours" driver for a task, do it in your clone and keep it in the evidence folder. If the rig is not up yet, mark tasks not-compared with the reason.
5. CLAUDE.md rules: tenant isolation, decimal money with currency, rate and base amount, audit of every business record, English and Arabic right-to-left, licences, no copying from Odoo.

Verdict: BLOCKED if there is a product fault (a hard gate fails on the real product, any test fails, the product does not build or run from the clean clone) or a surface this piece added is not reached by the isolation or permission gates at all; gate gaps alone do not block. LOSS if gates hold but the piece misses scope or loses or ties Odoo on any task (other than a tie-accepted or known-loss one; a known-loss-candidate still counts as a loss); WIN only if gates hold, the scope is complete and it beats Odoo on every task compared, a tie-accepted or known-loss task counting as beaten. Whatever the verdict, name the single biggest gap (the one thing that most holds this piece back from beating Odoo for a UAE trading or manufacturing company), with evidence a builder can reproduce. List other findings briefly. List any human gate from CLAUDE.md the piece has reached.

When done: \`docker compose -p c-${p.id}-r${round} down -v --rmi local\` (or the product's equivalent) and delete ${clone} except nothing you still need; keep ${staging}. Never stop the odoo-reference containers or anyone else's.
${ENV_NOTES}

Return the verdict object. judged_at comes from \`date -u +%Y-%m-%dT%H:%M:%SZ\`. critic is "fresh critic, ${p.id} round ${round}". evidence_staging is ${staging}; evidence_dir is ${evdir}.`
}

function recorderPrompt(v, humanGates) {
  return `You are the RECORDER. Record one critic verdict in ${REPO} on branch ${BRANCH}. Do nothing else.

0. Take the integration lock (see environment notes); release it after the push, or if you give up.
   Then run \`git fetch origin && git log --oneline origin/${BRANCH}..HEAD\`. If it lists any commit, another agent's unpushed work is in the tree: do not commit on top of it or push it. Release the lock, wait 10 minutes and try again, up to 6 times; if it is still there, return pushed=false and name the commits.
1. Copy the evidence folder ${v.evidence_staging}/ to ${REPO}/${v.evidence_dir}/ (create it; skip if the staging folder is missing). Keep files under 1 MB each; if a PNG is larger, convert it to JPEG at quality 75 and fix its name in the verdict's screenshots list.
2. Write the verdict JSON below to /tmp/verdict-${v.piece}-r${v.round}.json exactly (after any screenshot renames) and run \`node ${REPO}/gauntlet/tools/record.mjs /tmp/verdict-${v.piece}-r${v.round}.json\`. If it reports a validation error, fix only the formatting (never the content) and rerun.
3. Human gates to add to ${REPO}/gauntlet/needs-human.md as new table rows (skip any already listed, number them after the last row, Raised = today's date, Status = Open): ${JSON.stringify(humanGates)}
3b. Gate gaps to append to ${REPO}/gauntlet/gate-backlog.md as new table rows (number them after the last row; Added = today's date; Owner = the gap's owner; From verdict = "${v.piece} r${v.round} (${String(v.commit).slice(0, 8)})"; Kind = planted variant; Gap = gate, what was planted and the evidence in one or two sentences; Status = Open): ${JSON.stringify(v.gate_gaps || [])}
4. \`git add gauntlet/verdicts gauntlet/ledger.md gauntlet/progress.html gauntlet/needs-human.md gauntlet/gate-backlog.md ${v.evidence_dir}\` and commit with message "Record verdict: ${v.piece} round ${v.round} ${v.verdict}" ending with:
${TRAILER}
5. \`git push -u origin ${BRANCH}\`; on a network failure retry up to 4 times waiting 2, 4, 8, 16 seconds. If the push is rejected because the remote moved, \`git pull --no-rebase origin ${BRANCH}\` and push again.

Verdict JSON:
${JSON.stringify(v, null, 2)}

Return the recorded id printed by record.mjs, the commit SHA and whether it was pushed.`
}

// agent() returns null when the subagent dies on a terminal API error (usage limit, 529 overload).
// Try the same step again rather than moving the piece on to its next round with nothing built.
async function retrying(what, fn) {
  for (let attempt = 1; attempt <= 3; attempt++) {
    const r = await fn()
    if (r) return r
    log(`${what}: agent died (attempt ${attempt} of 3)`)
  }
  return null
}

async function runPiece(p) {
  let last = p.lastVerdict || null
  const results = []
  for (let i = 0; i < ROUNDS; i++) {
    const round = p.startRound + i
    let build, integ
    if (p.builtHead && i === 0) {
      // This round was built in an earlier run but never integrated.
      build = { head_commit: p.builtHead, notes_for_integrator: p.builtNotes || '', human_gates: p.builtHumanGates || [] }
      integ = await serial(() => retrying(`${p.id} r${round} integrate`, () => agent(integratorPrompt(p, round, build), { label: `integrate:${p.id}:r${round}`, phase: 'Integrate', schema: INTEG_SCHEMA, effort: 'medium' })))
    } else if (p.judgeCommit && i === 0) {
      // This round was built and integrated in an earlier run but never judged.
      build = { human_gates: [] }
      integ = { merged: true, pushed: true, commit: p.judgeCommit }
    } else {
    build = await buildRound(p, round, last)
    if (!build) { log(`${p.id}: builder died three times in round ${round}; piece stopped`); results.push({ round, outcome: 'builder-died' }); break }
    integ = await serial(() => retrying(`${p.id} r${round} integrate`, () => agent(integratorPrompt(p, round, build), { label: `integrate:${p.id}:r${round}`, phase: 'Integrate', schema: INTEG_SCHEMA, effort: 'medium' })))
    }
    if (!integ || !integ.merged || !integ.pushed) {
      const problem = integ ? (integ.problem || 'merged/pushed false without detail') : 'integrator died'
      log(`${p.id}: round ${round} not integrated: ${problem.slice(0, 200)}`)
      last = { integration_failure: problem }
      results.push({ round, outcome: 'not-integrated', problem, human_gates: build.human_gates || [] })
      continue
    }
    log(`${p.id}: round ${round} judging ${integ.commit.slice(0, 10)}`)
    const verdict = await retrying(`${p.id} r${round} judge`, () => agent(criticPrompt(p, round, integ.commit) + (p.criticNote ? `\n\nNOTE FROM THE LEAD (facts about the repository, not claims about quality; check them yourself):\n${p.criticNote}` : ''), { label: `judge:${p.id}:r${round}`, phase: 'Judge', schema: VERDICT_SCHEMA, effort: 'high' }))
    if (!verdict) { log(`${p.id}: critic died three times in round ${round}; piece stopped`); results.push({ round, outcome: 'critic-died', commit: integ.commit }); break }
    const knownLoss = (verdict.odoo || []).filter(o => o.winner === 'known-loss-candidate').map(o => ({ gate: 'Owner sign-off of a known loss against Odoo (needs-human #27)', what: `Task ${o.task}: Odoo is strictly better and no path for ours was found to win or tie. ${o.notes || ''}`.trim() }))
    const humanGates = [...(build.human_gates || []), ...(verdict.human_gates || []), ...knownLoss].map(g => ({ piece: p.id, ...g }))
    const rec = await serial(() => retrying(`${p.id} r${round} record`, () => agent(recorderPrompt(verdict, humanGates), { label: `record:${p.id}:r${round}`, phase: 'Record', schema: REC_SCHEMA, effort: 'low' })))
    log(`${p.id}: round ${round} ${verdict.verdict} — ${verdict.biggest_gap.title}`)
    results.push({ round, outcome: 'judged', verdict: verdict.verdict, gap: verdict.biggest_gap.title, commit: integ.commit, recorded: rec ? rec.recorded_id : null })
    last = verdict
    // Owner decision 2026-10-23 (needs-human #27): a piece that wins is done for now.
    if (verdict.verdict === 'WIN') { log(`${p.id}: WIN in round ${round}; done for now, no more rounds`); break }
  }
  return { piece: p.id, nextRound: p.startRound + results.length, lastVerdict: last, results }
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

// Self-test of the client-side isolation gate (src/modules/shell/clientIsolation.test.tsx).
//
// Each plant is a deliberate leak of one tenant's data to the next person in the same browser tab.
// For every plant this script copies the web sources to a temporary folder, applies the plant
// there (never in the working tree), runs the gate, and requires the gate to FAIL. A plant the gate
// does not catch fails this script. Plant P9 is the critic's round-2 plant: a module-level cache of
// palette answers that nothing clears at sign-out. Plants C1 and C2 are the critic's round-3 plants
// (palette answers in window.name; the last opened record in a cookie), each with the forgetting
// step that now neutralises it taken out, and the H plants break the history guard that keeps one
// identity's addresses (?q=, ?open=) from the next person pressing Back (critic p04 round 3).
//
// Usage: node scripts/plant-self-test.mjs   (from web/, after npm ci)
import { spawn } from "node:child_process";
import { cpSync, mkdtempSync, readFileSync, rmSync, symlinkSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";

const web = resolve(import.meta.dirname, "..");
const gate = "src/modules/shell/clientIsolation.test.tsx";

/** Each edit replaces exactly one occurrence of `find` in `file`. */
const plants = [
  {
    id: "P9",
    what: "palette answers cached at module level by query, never cleared at sign-out",
    edits: [
      {
        file: "src/modules/shell/CommandPalette.tsx",
        find: "const debounceMs = 120;",
        replace: "const debounceMs = 120;\nconst answered = new Map<string, SourceState>();",
      },
      {
        file: "src/modules/shell/CommandPalette.tsx",
        find: "    setRemote(Object.fromEntries(asked.map((s) => [s.key, { status: \"searching\", items: [] } as SourceState])));\n    if (asked.length === 0) return;",
        replace:
          "    setRemote(Object.fromEntries(asked.map((s) => [s.key, answered.get(`${s.key}:${trimmed}`) ?? ({ status: \"searching\", items: [] } as SourceState)])));\n    if (asked.every((s) => answered.has(`${s.key}:${trimmed}`))) return;",
      },
      {
        file: "src/modules/shell/CommandPalette.tsx",
        find: "            const { items, total } = Array.isArray(answer) ? { items: answer, total: undefined } : answer;",
        replace:
          "            const { items, total } = Array.isArray(answer) ? { items: answer, total: undefined } : answer;\n            answered.set(`${source.key}:${trimmed}`, { status: \"done\", items: items.slice(0, shownPerSource), total });",
      },
    ],
  },
  {
    id: "P9-email",
    what: "signing out keeps the signed-out person's e-mail on the sign-in screen",
    edits: [{ file: "src/kernel/session.tsx", find: "await forgetIdentity({ keepEmail: false, appPaths: visited.current });", replace: "await forgetIdentity({ keepEmail: true, appPaths: visited.current });" }],
  },
  {
    id: "P9-storage",
    what: "signing out leaves the person's localStorage entries behind",
    edits: [{ file: "src/kernel/deviceState.ts", find: "  clearLocalStorage(keepEmail);\n", replace: "" }],
  },
  {
    id: "P9-session-storage",
    what: "signing out leaves sessionStorage behind",
    edits: [{ file: "src/kernel/deviceState.ts", find: "    sessionStorage.clear();\n", replace: "" }],
  },
  {
    id: "P9-scoped",
    what: "identity-scoped caches are not emptied when the identity ends",
    edits: [{ file: "src/kernel/deviceState.ts", find: "  for (const reset of resets) reset();\n", replace: "" }],
    // The palette caches its answers in an identity-scoped map for this plant, as a module would.
    extra: [
      {
        file: "src/modules/shell/CommandPalette.tsx",
        find: "const debounceMs = 120;",
        replace: "const debounceMs = 120;\nconst answered = identityScoped<string, SourceState>();",
      },
      {
        file: "src/modules/shell/CommandPalette.tsx",
        find: 'import { Dialog } from "../../kernel/dialog";',
        replace: 'import { Dialog } from "../../kernel/dialog";\nimport { identityScoped } from "../../kernel/deviceState";',
      },
      {
        file: "src/modules/shell/CommandPalette.tsx",
        find: "    setRemote(Object.fromEntries(asked.map((s) => [s.key, { status: \"searching\", items: [] } as SourceState])));\n    if (asked.length === 0) return;",
        replace:
          "    setRemote(Object.fromEntries(asked.map((s) => [s.key, answered.get(`${s.key}:${trimmed}`) ?? ({ status: \"searching\", items: [] } as SourceState)])));\n    if (asked.every((s) => answered.has(`${s.key}:${trimmed}`))) return;",
      },
      {
        file: "src/modules/shell/CommandPalette.tsx",
        find: "            const { items, total } = Array.isArray(answer) ? { items: answer, total: undefined } : answer;",
        replace:
          "            const { items, total } = Array.isArray(answer) ? { items: answer, total: undefined } : answer;\n            answered.set(`${source.key}:${trimmed}`, { status: \"done\", items: items.slice(0, shownPerSource), total });",
      },
    ],
  },
  {
    id: "C2",
    what: "the last record opened from the palette kept in a cookie (path=/), and signing out does not forget cookies",
    edits: [{ file: "src/kernel/deviceState.ts", find: "  clearCookies(appPaths);\n", replace: "" }],
    extra: [
      {
        file: "src/modules/shell/CommandPalette.tsx",
        find: "    if (!entry) return;\n    onClose();",
        replace:
          "    if (!entry) return;\n    if (entry.id.includes(\":\")) document.cookie = `erp.recentRecord=${encodeURIComponent(`${entry.title} ${entry.subtitle ?? \"\"} ${entry.path ?? \"\"}`)}; path=/; max-age=31536000`;\n    onClose();",
      },
    ],
  },
  {
    id: "C2-path",
    what: "the record cookie scoped to the screen's path (/identity), and signing out forgets cookies only at /",
    edits: [{ file: "src/kernel/deviceState.ts", find: "    const paths = cookiePaths([...appPaths, window.location.pathname]);", replace: "    const paths = [\"/\"];" }],
    extra: [
      {
        file: "src/modules/shell/CommandPalette.tsx",
        find: "    if (!entry) return;\n    onClose();",
        replace:
          "    if (!entry) return;\n    if (entry.id.includes(\":\")) document.cookie = `erp.recentRecord=${encodeURIComponent(`${entry.title} ${entry.subtitle ?? \"\"} ${entry.path ?? \"\"}`)}; path=/identity; max-age=31536000`;\n    onClose();",
      },
    ],
  },
  {
    id: "C1",
    what: "palette answers kept in the tab's window.name, and signing out does not reset it",
    edits: [{ file: "src/kernel/deviceState.ts", find: "  window.name = \"\";\n", replace: "" }],
    extra: [
      {
        file: "src/modules/shell/CommandPalette.tsx",
        find: "    setRemote(Object.fromEntries(asked.map((s) => [s.key, { status: \"searching\", items: [] } as SourceState])));\n    if (asked.length === 0) return;",
        replace:
          "    const holder = document.defaultView!;\n    let answered: Record<string, SourceState> = {};\n    try { answered = JSON.parse(holder.name || \"{}\"); } catch { answered = {}; }\n    setRemote(Object.fromEntries(asked.map((s) => [s.key, answered[`${s.key}:${trimmed}`] ?? ({ status: \"searching\", items: [] } as SourceState)])));\n    if (asked.length === 0) return;\n    if (asked.every((s) => answered[`${s.key}:${trimmed}`])) return;",
      },
      {
        file: "src/modules/shell/CommandPalette.tsx",
        find: "            const { items, total } = Array.isArray(answer) ? { items: answer, total: undefined } : answer;",
        replace:
          "            const { items, total } = Array.isArray(answer) ? { items: answer, total: undefined } : answer;\n            answered[`${source.key}:${trimmed}`] = { status: \"done\", items: items.slice(0, shownPerSource), total };\n            holder.name = JSON.stringify(answered);",
      },
    ],
  },
  {
    id: "H1",
    what: "the history guard trusts every entry: Back into the signed-out person's address opens it",
    edits: [
      { file: "src/kernel/historyGuard.ts", find: "  } else if (!entryIsCurrent()) {\n    forgetEntry(originalReplace);\n  }", replace: "  }" },
      { file: "src/kernel/historyGuard.ts", find: "    if (entryIsCurrent(event.state)) return;", replace: "    return;" },
    ],
  },
  {
    id: "H2",
    what: "Back and reload are trusted like a typed address",
    edits: [{ file: "src/kernel/historyGuard.ts", find: "  if (kind === \"navigate\" || kind === \"prerender\") {", replace: "  if (kind !== undefined) {" }],
  },
  {
    id: "H3",
    what: "the history epoch survives the end of an identity (sessionStorage cleared except the epoch)",
    edits: [
      {
        file: "src/kernel/deviceState.ts",
        find: "    sessionStorage.clear();\n",
        replace: "    const epoch = sessionStorage.getItem(\"erp.historyEpoch\");\n    sessionStorage.clear();\n    if (epoch) sessionStorage.setItem(\"erp.historyEpoch\", epoch);\n",
      },
    ],
  },
  {
    id: "H4",
    what: "inside a document, Back to an entry of another identity is not checked",
    edits: [{ file: "src/kernel/historyGuard.ts", find: "    if (entryIsCurrent(event.state)) return;", replace: "    return;" }],
  },
];

function copyWeb() {
  const dir = mkdtempSync(join(tmpdir(), "erp-web-plant-"));
  for (const entry of ["src", "scripts", "index.html", "package.json", "tsconfig.json", "vite.config.ts"]) {
    cpSync(join(web, entry), join(dir, entry), { recursive: true });
  }
  symlinkSync(join(web, "node_modules"), join(dir, "node_modules"), "dir");
  return dir;
}

function apply(dir, edit, plant) {
  const path = join(dir, edit.file);
  const text = readFileSync(path, "utf8");
  const count = text.split(edit.find).length - 1;
  if (count !== 1) throw new Error(`plant ${plant.id}: '${edit.find.slice(0, 60)}…' occurs ${count} times in ${edit.file} (the plant no longer fits the code; update it)`);
  writeFileSync(path, text.replace(edit.find, edit.replace));
}

/** Runs the gate in a copy; resolves with its exit status and output. */
function runGate(dir) {
  return new Promise((resolve) => {
    const child = spawn(join(dir, "node_modules", ".bin", "vitest"), ["run", gate], { cwd: dir });
    let stdout = "";
    let stderr = "";
    child.stdout.on("data", (chunk) => (stdout += chunk));
    child.stderr.on("data", (chunk) => (stderr += chunk));
    child.on("close", (status) => resolve({ status, stdout, stderr }));
  });
}

/** How many plants run at once (each is one vitest process). */
const parallel = Math.max(1, Math.min(4, Number(process.env.ERP_PLANT_PARALLEL ?? 4)));

const problems = [];
// The control: the unplanted copy passes the gate (else a failing gate would "catch" every plant).
{
  const dir = copyWeb();
  try {
    const control = await runGate(dir);
    if (control.status !== 0) problems.push(`control: the gate fails without any plant:\n${control.stdout}\n${control.stderr}`);
    else console.log("control: the gate passes on the unplanted product");
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
}

async function judgePlant(plant) {
  const dir = copyWeb();
  try {
    for (const edit of [...(plant.extra ?? []), ...plant.edits]) apply(dir, edit, plant);
    const result = await runGate(dir);
    const output = `${result.stdout}\n${result.stderr}`;
    if (result.status === 0) problems.push(`${plant.id} (${plant.what}): the gate PASSED with the plant in place`);
    // Caught by an assertion of the gate, not by a plant that no longer compiles or loads.
    else if (!/AssertionError/.test(output) || /SyntaxError|Transform failed|Failed to load/.test(output))
      problems.push(`${plant.id}: the gate failed for another reason than the leak:\n${output.slice(-3000)}`);
    else console.log(`${plant.id}: caught (${plant.what})`);
  } catch (error) {
    problems.push(String(error instanceof Error ? error.message : error));
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
}

const queue = [...plants];
await Promise.all(
  Array.from({ length: parallel }, async () => {
    for (let plant = queue.shift(); plant; plant = queue.shift()) await judgePlant(plant);
  }),
);
if (problems.length > 0) {
  console.error(`\nclient isolation gate self-test FAILED:\n  ${problems.join("\n  ")}`);
  process.exit(1);
}
console.log(`client isolation gate self-test: ${plants.length} plants, all caught`);

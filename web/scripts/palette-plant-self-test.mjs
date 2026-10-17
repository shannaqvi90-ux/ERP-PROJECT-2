// Self-test of the command palette's permission gate (src/modules/shell/paletteActions.test.tsx).
//
// Each plant makes the palette offer an action the user's permissions refuse: a module action
// offered without its permission or its screen's, a module action declared under the wrong
// permission, a screen command registered without the check that hides its button, an action shown
// whatever its screen's own test says. For every plant this script copies the web sources to a
// temporary folder, applies the plant there (never in the working tree), runs the tests of the gate
// that the plant's fault concerns, and requires them to FAIL on an assertion. A plant the gate does
// not catch fails this script. The first plants are critic p04 round 7's suggestion: "a G2 screen
// plant (an action offered without its permission)".
//
// Usage: node scripts/palette-plant-self-test.mjs   (from web/, after npm ci)
import { spawnSync } from "node:child_process";
import { cpSync, mkdtempSync, readFileSync, rmSync, symlinkSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";

const web = resolve(import.meta.dirname, "..");
const gate = "src/modules/shell/paletteActions.test.tsx";
// The gate reads its minimums from the ratchet, which the copy does not hold.
const ratchet = resolve(web, "..", "gauntlet", "ratchet.json");

/** Tests of the gate, by name: who is offered an action, and what every entry does for a reader. */
const offering = "shown in the palette only|offers an action only";
const sweep = "may only read";

/** Each edit replaces exactly one occurrence of `find` in `file`. */
const plants = [
  {
    id: "PA1",
    what: "a module action offered without its own permission",
    tests: offering,
    edits: [{ file: "src/modules/shell/paletteActions.ts", find: "    if (!action.permission || !can(action.permission)) return false;", replace: "    if (!action.permission) return false;" }],
  },
  {
    id: "PA2",
    what: "a module action offered without the permission of the screen it opens",
    tests: offering,
    edits: [{ file: "src/modules/shell/paletteActions.ts", find: "    return Boolean(route) && (!route!.permission || can(route!.permission));", replace: "    return Boolean(route);" }],
  },
  {
    id: "PA3",
    what: "the shell passing every module action to the palette, unfiltered",
    tests: offering,
    edits: [{ file: "src/modules/shell/AppShell.tsx", find: "actionEntries(offeredActions(extensions.actions, can), t, language, startAction)", replace: "actionEntries(extensions.actions, t, language, startAction)" }],
  },
  {
    id: "PA4",
    what: "New user declared under the permission to read users, not to create them",
    tests: sweep,
    edits: [
      {
        file: "src/modules/identity/extensions.ts",
        find: 'permission: "identity.users.create", path: "/identity/users", command: "identity.users.new"',
        replace: 'permission: "identity.users.read", path: "/identity/users", command: "identity.users.new"',
      },
    ],
  },
  {
    id: "PA5",
    what: "screen commands registered whatever their screen's check says (enabled ignored)",
    tests: sweep,
    edits: [{ file: "src/kernel/shortcuts.tsx", find: "    if (!enabled || !register) return;", replace: "    if (!register) return;" }],
  },
  {
    id: "PA6",
    what: "Copy role offered without identity.roles.create",
    tests: sweep,
    edits: [{ file: "src/modules/identity/RolesPage.tsx", find: "    enabled: Boolean(role && actions.copy),", replace: "    enabled: Boolean(role)," }],
  },
  {
    id: "PA7",
    what: "Reset password offered to someone who may not reset this user's password",
    tests: "exactly when the record shows",
    edits: [{ file: "src/modules/identity/UserPanel.tsx", find: '    enabled: allowed.resetPassword && mode === "closed",', replace: '    enabled: mode === "closed",' }],
  },
  {
    id: "PA7-delete",
    what: "Delete user offered to someone who may not delete this user",
    tests: "exactly when the record shows",
    edits: [{ file: "src/modules/identity/UserPanel.tsx", find: "    enabled: allowed.delete && !confirmDelete,", replace: "    enabled: !confirmDelete," }],
  },
  {
    id: "PA8",
    what: "Allow sign-in again offered to someone who may not change users",
    tests: sweep,
    edits: [{ file: "src/modules/identity/UserPanel.tsx", find: "    enabled: canUnblock && (history?.paused.length ?? 0) > 0,", replace: "    enabled: (history?.paused.length ?? 0) > 0," }],
  },
  {
    id: "PA9",
    what: "New branch offered whatever the screen's own test (a company the user may add a branch to)",
    tests: "no company",
    edits: [
      {
        file: "src/modules/tenancy/extensions.tsx",
        find: '      available: async () => branchCompanies(await loadAll<CompanyRow>("/api/tenancy/companies")).length > 0,\n',
        replace: "",
      },
      { file: "src/modules/tenancy/extensions.tsx", find: 'import { branchCompanies } from "./BranchesPage";\n', replace: "" },
      { file: "src/modules/tenancy/extensions.tsx", find: "import { loadAll, worksInEveryCompany, type ListPage } from", replace: "import { worksInEveryCompany, type ListPage } from" },
    ],
  },
  {
    id: "PA10",
    what: "New company offered whatever the screen's own test (works in every company; critic p02 round 9)",
    tests: "not offered New company",
    edits: [
      { file: "src/modules/tenancy/extensions.tsx", find: "      available: () => worksInEveryCompany(),\n", replace: "" },
      { file: "src/modules/tenancy/extensions.tsx", find: "import { loadAll, worksInEveryCompany, type ListPage } from", replace: "import { loadAll, type ListPage } from" },
    ],
  },
  {
    id: "PA11",
    what: "Allow sign-in again offered without the sign-in history permission its record's command needs (critic p03 round 10, plant U1)",
    tests: "runs to its end",
    edits: [{ file: "src/modules/identity/extensions.ts", find: '      requires: ["identity.signIns.read"],\n', replace: "" }],
  },
  {
    id: "PA12",
    what: "Reset password offered from a user's other tabs to someone who may not reset passwords (the record's commands follow it to every tab, critic p03 round 10)",
    tests: "on every tab",
    edits: [{ file: "src/modules/identity/UserPanel.tsx", find: "    enabled: elsewhere && allowed.resetPassword,", replace: "    enabled: elsewhere," }],
  },
];

function copyWeb() {
  const dir = mkdtempSync(join(tmpdir(), "erp-web-palette-plant-"));
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

/** A planted run stops at the first failed test (--bail=1) and runs only the tests the plant's fault
 * concerns; the unplanted control runs the whole gate. */
function runGate(dir, tests) {
  const args = ["run", ...(tests ? ["--bail=1", "-t", tests] : []), gate];
  return spawnSync(join(dir, "node_modules", ".bin", "vitest"), args, { cwd: dir, encoding: "utf8", env: { ...process.env, ERP_RATCHET: ratchet } });
}

const problems = [];
// The control: the unplanted copy passes the gate (else a failing gate would "catch" every plant).
{
  const dir = copyWeb();
  try {
    const control = runGate(dir);
    if (control.status !== 0) problems.push(`control: the gate fails without any plant:\n${control.stdout}\n${control.stderr}`);
    else console.log("control: the gate passes on the unplanted product");
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
}
for (const plant of plants) {
  const dir = copyWeb();
  try {
    for (const edit of plant.edits) apply(dir, edit, plant);
    const result = runGate(dir, plant.tests);
    const output = `${result.stdout}\n${result.stderr}`;
    if (result.status === 0) problems.push(`${plant.id} (${plant.what}): the gate PASSED with the plant in place`);
    // Caught by an assertion of the gate, not by a plant that no longer compiles or loads.
    else if (!/AssertionError/.test(output) || /SyntaxError|Transform failed|Failed to load/.test(output))
      problems.push(`${plant.id}: the gate failed for another reason than the planted fault:\n${output.slice(-3000)}`);
    else console.log(`${plant.id}: caught (${plant.what})`);
  } catch (error) {
    problems.push(String(error instanceof Error ? error.message : error));
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
}
if (problems.length > 0) {
  console.error(`\ncommand palette permission gate self-test FAILED:\n  ${problems.join("\n  ")}`);
  process.exit(1);
}
console.log(`command palette permission gate self-test: ${plants.length} plants, all caught`);

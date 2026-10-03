// Self-test of the identity screens' permission gate (src/modules/identity/screens.test.tsx).
//
// Each plant offers on screen an action the user's permissions refuse. For every plant this
// script copies the web sources to a temporary folder, applies the plant there (never in the
// working tree), runs the gate, and requires the gate to FAIL on an assertion. A plant the gate
// does not catch fails this script. U1 is critic p03 round 2's plant (New role shown without
// identity.roles.create); the others are the same fault on the other actions of these screens.
//
// Usage: node scripts/identity-plant-self-test.mjs   (from web/, after npm ci)
import { spawnSync } from "node:child_process";
import { cpSync, mkdtempSync, readFileSync, rmSync, symlinkSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";

const web = resolve(import.meta.dirname, "..");
const gate = "src/modules/identity/screens.test.tsx";

/** Each edit replaces exactly one occurrence of `find` in `file`. */
const plants = [
  {
    id: "U1",
    what: "New role offered without identity.roles.create",
    edits: [{ file: "src/modules/identity/RolesPage.tsx", find: '            can("identity.roles.create") && (', replace: "            (" }],
  },
  {
    id: "U1-users",
    what: "New user offered without identity.users.create",
    edits: [{ file: "src/modules/identity/UsersPage.tsx", find: '            can("identity.users.create") && (', replace: "            (" }],
  },
  {
    id: "U1-shortcut",
    what: "Alt+N opens a new role without identity.roles.create",
    edits: [{ file: "src/modules/identity/RolesPage.tsx", find: '    enabled: can("identity.roles.create"),', replace: "    enabled: true," }],
  },
  {
    id: "U-delete",
    what: "Delete role offered without identity.roles.delete (critic p03 round 1)",
    edits: [{ file: "src/modules/identity/model.ts", find: '    delete: !role.isSystem && !beyondOwn && held.has("identity.roles.delete"),', replace: "    delete: !role.isSystem && !beyondOwn," }],
  },
  {
    id: "U-copy",
    what: "Copy role offered for a role granting more than the user holds",
    edits: [{ file: "src/modules/identity/model.ts", find: '    copy: !beyondOwn && held.has("identity.roles.create"),', replace: '    copy: held.has("identity.roles.create"),' }],
  },
];

function copyWeb() {
  const dir = mkdtempSync(join(tmpdir(), "erp-web-identity-plant-"));
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

function runGate(dir) {
  return spawnSync(join(dir, "node_modules", ".bin", "vitest"), ["run", gate], { cwd: dir, encoding: "utf8" });
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
    const result = runGate(dir);
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
  console.error(`\nidentity screens permission gate self-test FAILED:\n  ${problems.join("\n  ")}`);
  process.exit(1);
}
console.log(`identity screens permission gate self-test: ${plants.length} plants, all caught`);

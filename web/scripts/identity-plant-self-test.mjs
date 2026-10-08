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
    // The screen offers New through the record panel, which is given the permission: the plant
    // shows the button regardless and makes it open a new role.
    edits: [
      { file: "src/modules/identity/RolesPage.tsx", find: "            panel.startNew && (", replace: "            (" },
      { file: "src/modules/identity/RolesPage.tsx", find: "onClick={panel.startNew}", replace: "onClick={() => panel.onOpenIdChange(newRecord)}" },
    ],
  },
  {
    id: "U1-users",
    what: "New user offered without identity.users.create",
    edits: [
      { file: "src/modules/identity/UsersPage.tsx", find: "            panel.startNew && (", replace: "            (" },
      { file: "src/modules/identity/UsersPage.tsx", find: "onClick={panel.startNew}", replace: "onClick={() => panel.onOpenIdChange(newRecord)}" },
    ],
  },
  {
    id: "U1-shortcut",
    what: "Alt+N opens a new role without identity.roles.create",
    edits: [
      { file: "src/modules/identity/RolesPage.tsx", find: "    enabled: Boolean(panel.startNew),", replace: "    enabled: true," },
      { file: "src/modules/identity/RolesPage.tsx", find: "    run: () => panel.startNew?.(),", replace: "    run: () => panel.onOpenIdChange(newRecord)," },
    ],
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
  {
    id: "U2",
    what: "A user's grants compared only on identity permissions (critic p03 round 3)",
    edits: [
      {
        file: "src/modules/identity/model.ts",
        find: "  const grantsBeyond = (id: string) => roles.find((r) => r.id === id)?.permissions.some((p) => !held.has(p)) ?? false;",
        replace: '  const grantsBeyond = (id: string) => roles.find((r) => r.id === id)?.permissions.some((p) => p.startsWith("identity.") && !held.has(p)) ?? false;',
      },
    ],
  },
  {
    id: "U2-roles",
    what: "A role's grants compared only on identity permissions",
    edits: [
      {
        file: "src/modules/identity/model.ts",
        find: "  const beyondOwn = role.permissions.some((p) => !held.has(p));",
        replace: '  const beyondOwn = role.permissions.some((p) => p.startsWith("identity.") && !held.has(p));',
      },
    ],
  },
  {
    id: "U-company-roles",
    what: "Roles held in one company left out of what a user holds",
    edits: [{ file: "src/modules/identity/model.ts", find: " || (user.companyRoles ?? []).some((c) => grantsBeyond(c.roleId))", replace: "" }],
  },
  {
    id: "U-elsewhere",
    what: "A user holding roles in companies the signed-in user does not work in treated as editable",
    edits: [{ file: "src/modules/identity/model.ts", find: " || user.rolesElsewhere === true", replace: "" }],
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

/** A planted run stops at the gate's first failed test (--bail=1): one failed assertion is what
 * catches the plant, and the tests after it only cost processor time (verify.cpuSeconds). The
 * control runs every test. */
function runGate(dir, { planted = false } = {}) {
  return spawnSync(join(dir, "node_modules", ".bin", "vitest"), ["run", gate, ...(planted ? ["--bail=1"] : [])], { cwd: dir, encoding: "utf8" });
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
    const result = runGate(dir, { planted: true });
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

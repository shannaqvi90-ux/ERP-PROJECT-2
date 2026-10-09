// Self-test of the screen permission gate by keyboard: the record form kernel's
// (src/kernel/forms/keyboardPermissions.test.tsx and src/kernel/forms/forms.test.tsx) and the
// screens' key sweeps (src/modules/*/keyboard.test.tsx).
//
// Each plant lets a user who may not change a record change it anyway, or offers an action the
// user's permissions refuse, by a key or by a control the keyboard reaches. For every plant this
// script copies the web sources to a temporary folder, applies the plant there (never in the
// working tree), runs the plant's gate, and requires the gate to FAIL on an assertion. A plant the
// gate does not catch fails this script. W1 and W2 are critic p06 round 2's plants (W1: the form
// editable with Save for a read-only user; W2: Ctrl+S left on and save() no longer checking the
// permission, so a read-only user's Ctrl+S sent a PUT and every gate passed); the W plants are the
// same fault through the form's other keys and paths, and the K plants the same fault in a screen's
// own keys, in the list's bulk actions and in a module's form.
//
// Usage: node scripts/forms-plant-self-test.mjs   (from web/, after npm ci)
import { spawnSync } from "node:child_process";
import { cpSync, mkdtempSync, readFileSync, rmSync, symlinkSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";

const web = resolve(import.meta.dirname, "..");
const kernelGates = ["src/kernel/forms/keyboardPermissions.test.tsx", "src/kernel/forms/forms.test.tsx"];
const tenancyKeys = ["src/modules/tenancy/keyboard.test.tsx"];
const identityKeys = ["src/modules/identity/keyboard.test.tsx"];
const allGates = [...kernelGates, ...tenancyKeys, ...identityKeys];
const F = "src/kernel/forms/RecordForm.tsx";
const U = "src/kernel/forms/useRecordForm.ts";

const saveUnchecked = { file: U, find: "if (!current.canEdit || busy) return null;", replace: "if (busy) return null;" };
const keyOn = (id) => ({
  file: F,
  find: `useShortcut({ id: "forms.${id}", chord: formChords.${id}, labelKey: "forms.shortcut.${id === "saveEnter" ? "save" : id}", groupKey: group, enabled: editable,`,
  replace: `useShortcut({ id: "forms.${id}", chord: formChords.${id}, labelKey: "forms.shortcut.${id === "saveEnter" ? "save" : id}", groupKey: group, enabled: true,`,
});

/** Each edit replaces exactly one occurrence of `find` in `file`. */
const plants = [
  {
    id: "W1",
    what: "the record form editable, with Save, for a read-only user (critic p06 round 2)",
    edits: [
      { file: F, find: "const editable = !form.readOnly;", replace: "const editable = true;" },
      { file: F, find: "<FormContext.Provider value={{ readOnly: form.readOnly }}>", replace: "<FormContext.Provider value={{ readOnly: false }}>" },
      { file: F, find: '<fieldset className="form-section" disabled={readOnly}>', replace: '<fieldset className="form-section" disabled={false}>' },
    ],
  },
  {
    id: "W2",
    what: "Ctrl+S left on for a read-only user and save() not checking the permission (critic p06 round 2)",
    edits: [keyOn("save"), saveUnchecked],
  },
  {
    id: "W2-enter",
    what: "Ctrl+Enter left on for a read-only user and save() not checking the permission",
    edits: [keyOn("saveEnter"), saveUnchecked],
  },
  {
    id: "W2-sheet",
    what: "the save keys offered to a read-only user in the shortcut sheet (save() still refuses)",
    edits: [keyOn("save"), keyOn("saveEnter")],
  },
  {
    id: "W2-discard",
    what: "the discard key offered to a read-only user in the shortcut sheet",
    edits: [keyOn("discard")],
  },
  {
    id: "W-leave",
    what: "Escape on a read-only record asks to save it, and 'Save and close' saves it",
    edits: [{ file: F, find: "    if (form.dirty) setAsking(true);", replace: "    if (form.dirty || form.readOnly) setAsking(true);" }, saveUnchecked],
  },
  {
    id: "W-new",
    what: "a new record editable and saved by a user who may not create it",
    edits: [
      { file: U, find: "const readOnly = !spec.canEdit;", replace: "const readOnly = !spec.canEdit && spec.load !== undefined;" },
      { file: U, find: "if (!current.canEdit || busy) return null;", replace: "if ((!current.canEdit && current.load !== undefined) || busy) return null;" },
    ],
  },
  // A screen's own keys and controls (gate: the screens' key sweeps; the buttons they draw are not
  // touched, so a gate that only looks at what is drawn misses each of these).
  {
    id: "K-roles-n",
    what: "the roles screen's plain n opens a new role for a user who may not create roles",
    gates: identityKeys,
    edits: [
      {
        file: "src/modules/identity/RolesPage.tsx",
        find: 'if (event.key.toLowerCase() === "n" && panel.startNew) {\n        event.preventDefault();\n        panel.startNew();',
        replace: 'if (event.key.toLowerCase() === "n") {\n        event.preventDefault();\n        panel.onOpenIdChange(newRecord);',
      },
    ],
  },
  {
    id: "K-users-alt-n",
    what: "Alt+N opens a new user for a user who may not create users",
    gates: identityKeys,
    edits: [
      {
        file: "src/modules/identity/UsersPage.tsx",
        find: "    enabled: Boolean(panel.startNew),\n    run: () => panel.startNew?.(),",
        replace: "    enabled: true,\n    run: () => panel.onOpenIdChange(newRecord),",
      },
    ],
  },
  {
    id: "K-branches-alt-n",
    what: "Alt+N opens a new branch for a user who may not create branches",
    gates: tenancyKeys,
    edits: [
      {
        file: "src/modules/tenancy/BranchesPage.tsx",
        find: "useScreenKeys({ onNew: panel.startNew, search: listSearch });",
        replace: "useScreenKeys({ onNew: () => panel.onOpenIdChange(newRecord), search: listSearch });",
      },
    ],
  },
  {
    id: "K-bulk",
    what: "the list's bulk actions offered on chosen rows without their permission (users: deactivate)",
    gates: identityKeys,
    edits: [
      {
        file: "src/kernel/lists/ListView.tsx",
        find: "const allowedBulk = (props.bulkActions ?? []).filter((a) => !a.permission || (props.can ? props.can(a.permission) : true));",
        replace: "const allowedBulk = props.bulkActions ?? [];",
      },
    ],
  },
  {
    id: "K-company-form",
    what: "the company form editable for a user who may only read it: its save keys write",
    gates: tenancyKeys,
    edits: [
      {
        file: "src/modules/tenancy/CompanyForm.tsx",
        find: 'canEdit: id === null ? can("tenancy.companies.create") : can("tenancy.companies.update") && everyBranch,',
        replace: "canEdit: true,",
      },
    ],
  },
  {
    id: "K-access-form",
    what: "a user's company access editable though the server marks it read-only: its save keys write",
    gates: tenancyKeys,
    edits: [{ file: "src/modules/tenancy/AccessPage.tsx", find: " && access.canEdit !== false);", replace: ");" }],
  },
  {
    id: "K-role-form",
    what: "a role editable for a user who may only read roles: its save keys write",
    gates: identityKeys,
    edits: [{ file: "src/modules/identity/model.ts", find: '    edit: !role.isSystem && !beyondOwn && held.has("identity.roles.update"),', replace: "    edit: !role.isSystem && !beyondOwn," }],
  },
  {
    id: "K-user-form",
    what: "a user editable for a user who may only read users: its save keys write",
    gates: identityKeys,
    edits: [{ file: "src/modules/identity/model.ts", find: '    edit: held.has("identity.users.update") && !beyondOwn,', replace: "    edit: !beyondOwn," }],
  },
  {
    id: "K-sign-out-everywhere",
    what: "End every session of a user offered to a user who may only read users",
    gates: identityKeys,
    edits: [{ file: "src/modules/identity/model.ts", find: '    signOutEverywhere: others && held.has("identity.users.update"),', replace: "    signOutEverywhere: others," }],
  },
  {
    id: "K-unblock",
    what: "Unblock sign-ins offered to a user who may only read users",
    gates: identityKeys,
    edits: [{ file: "src/modules/identity/model.ts", find: '    unblock: others && held.has("identity.users.update"),', replace: "    unblock: others," }],
  },
];

function copyWeb() {
  const dir = mkdtempSync(join(tmpdir(), "erp-web-forms-plant-"));
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

/** Runs the gate once. In a planted copy a key sweep may stop at its first find (ERP_SWEEP_FIRST_FIND):
 * one find fails it as surely as all of them, and the sweep's controls sweep everything regardless.
 * The unplanted control sweeps everything. A run that fails for any reason other than an assertion
 * (a worker that did not start, a plant that no longer compiles) is reported, never run again. */
/** A planted run stops at the first failed test (--bail=1): one assertion failing catches the plant as
 * surely as all of them (the plant must still fail an assertion, see below), and the rest of the gate
 * is not run for nothing. The unplanted control runs the whole gate. */
function runGate(dir, gates, firstFind) {
  const env = { ...process.env, ERP_SWEEP_FIRST_FIND: firstFind ? "1" : "0" };
  return spawnSync(join(dir, "node_modules", ".bin", "vitest"), ["run", ...(firstFind ? ["--bail=1"] : []), ...gates], { cwd: dir, encoding: "utf8", env });
}

const problems = [];
// The control: the unplanted copy passes the gate (else a failing gate would "catch" every plant).
{
  const dir = copyWeb();
  try {
    const control = runGate(dir, allGates, false);
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
    const result = runGate(dir, plant.gates ?? kernelGates, true);
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
  console.error(`\nscreen permission gate by keyboard, self-test FAILED:\n  ${problems.join("\n  ")}`);
  process.exit(1);
}
console.log(`screen permission gate by keyboard, self-test: ${plants.length} plants, all caught`);

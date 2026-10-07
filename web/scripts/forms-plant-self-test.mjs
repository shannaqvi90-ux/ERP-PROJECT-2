// Self-test of the record form kernel's permission gate (src/kernel/forms/keyboardPermissions.test.tsx
// and src/kernel/forms/forms.test.tsx).
//
// Each plant lets a user who may not change a record change it anyway through the shared record
// form, by a button or by a key. For every plant this script copies the web sources to a temporary
// folder, applies the plant there (never in the working tree), runs the gate, and requires the gate
// to FAIL on an assertion. A plant the gate does not catch fails this script. W1 and W2 are critic
// p06 round 2's plants (W1: the form editable with Save for a read-only user; W2: Ctrl+S left on and
// save() no longer checking the permission, so a read-only user's Ctrl+S sent a PUT and every gate
// passed); the others are the same fault through the form's other keys and paths.
//
// Usage: node scripts/forms-plant-self-test.mjs   (from web/, after npm ci)
import { spawnSync } from "node:child_process";
import { cpSync, mkdtempSync, readFileSync, rmSync, symlinkSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";

const web = resolve(import.meta.dirname, "..");
const gates = ["src/kernel/forms/keyboardPermissions.test.tsx", "src/kernel/forms/forms.test.tsx"];
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

// Vitest gives a worker a fixed 60 s to start; on a busy machine that can run out before any test
// runs. Such a run judged nothing, so it is run again (up to three times); any other result stands.
const workerDidNotStart = /\[vitest-pool(-runner)?\]: (Timeout waiting for worker to respond|Timeout starting \w+ runner)|Failed to start \w+ worker/;

function runGate(dir) {
  let result;
  for (let attempt = 1; attempt <= 3; attempt++) {
    result = spawnSync(join(dir, "node_modules", ".bin", "vitest"), ["run", ...gates], { cwd: dir, encoding: "utf8" });
    if (result.status === 0 || !workerDidNotStart.test(`${result.stdout}\n${result.stderr}`)) return result;
    console.log(`  (vitest's worker did not start in time on attempt ${attempt}; the gate judged nothing, running it again)`);
  }
  return result;
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
  console.error(`\nrecord form permission gate self-test FAILED:\n  ${problems.join("\n  ")}`);
  process.exit(1);
}
console.log(`record form permission gate self-test: ${plants.length} plants, all caught`);

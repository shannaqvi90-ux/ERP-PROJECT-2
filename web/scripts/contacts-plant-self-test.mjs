// Self-test of the contacts screen's permission gate (src/modules/contacts/screens.test.tsx).
//
// Each plant offers on screen an action the user's permissions refuse. For every plant this
// script copies the web sources to a temporary folder, applies the plant there (never in the
// working tree), runs the gate, and requires the gate to FAIL on an assertion. A plant the gate
// does not catch fails this script. The plants are the faults a contacts screen can have: New
// contact, New person, the form's fields and Save, and Archive or Restore offered without their
// permissions, and the palette's New contact asking for the wrong one.
//
// Usage: node scripts/contacts-plant-self-test.mjs   (from web/, after npm ci)
import { spawnSync } from "node:child_process";
import { cpSync, mkdtempSync, readFileSync, rmSync, symlinkSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";

const web = resolve(import.meta.dirname, "..");
const gate = "src/modules/contacts/screens.test.tsx";

/** Each edit replaces exactly one occurrence of `find` in `file`. */
const plants = [
  {
    id: "C1",
    what: "New contact (button and Alt+N) offered without contacts.contacts.create",
    edits: [{ file: "src/modules/contacts/ContactsPage.tsx", find: 'useRecordPanel(can("contacts.contacts.create"))', replace: "useRecordPanel(true)" }],
  },
  {
    id: "C2",
    what: "the contact form editable without contacts.contacts.update",
    edits: [{ file: "src/modules/contacts/ContactForm.tsx", find: ': can("contacts.contacts.update"),', replace: ": true," }],
  },
  {
    id: "C3",
    what: "Archive and Restore offered without contacts.contacts.archive",
    edits: [{ file: "src/modules/contacts/ContactForm.tsx", find: 'actions={contact && can("contacts.contacts.archive") ?', replace: "actions={contact ?" }],
  },
  {
    id: "C4",
    what: "New person (from an organisation's form) offered without contacts.contacts.create",
    edits: [{ file: "src/modules/contacts/ContactForm.tsx", find: 'onNewPerson={can("contacts.contacts.create") && contact.isActive ? onNewPerson : undefined}', replace: "onNewPerson={contact.isActive ? onNewPerson : undefined}" }],
  },
  {
    id: "C5",
    what: "New person offered for an archived organisation (which takes no new people)",
    edits: [{ file: "src/modules/contacts/ContactForm.tsx", find: 'onNewPerson={can("contacts.contacts.create") && contact.isActive ? onNewPerson : undefined}', replace: 'onNewPerson={can("contacts.contacts.create") ? onNewPerson : undefined}' }],
  },
  {
    id: "C6",
    what: "the palette's New contact offered to readers (contacts.contacts.read in place of contacts.contacts.create)",
    edits: [{ file: "src/modules/contacts/extensions.ts", find: 'permission: "contacts.contacts.create",', replace: 'permission: "contacts.contacts.read",' }],
  },
];

function copyWeb() {
  const dir = mkdtempSync(join(tmpdir(), "erp-web-contacts-plant-"));
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

// Vitest gives a worker a fixed 60 s to start; on a machine shared by several suites (load
// averages above 150 were seen) that runs out before any test runs. Such a run judged nothing, so
// it is run again (up to three times); any other result, pass or fail, stands as it is.
const workerDidNotStart = /\[vitest-pool(-runner)?\]: (Timeout waiting for worker to respond|Timeout starting \w+ runner)|Failed to start \w+ worker/;

/** A planted run stops at the first failed test (--bail=1): one assertion failing catches the plant as
 * surely as all of them (the plant must still fail an assertion, see below), and the rest of the gate
 * is not run for nothing. The unplanted control runs the whole gate. */
function runGate(dir, planted = false) {
  let result;
  for (let attempt = 1; attempt <= 3; attempt++) {
    result = spawnSync(join(dir, "node_modules", ".bin", "vitest"), ["run", ...(planted ? ["--bail=1"] : []), gate], { cwd: dir, encoding: "utf8" });
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
    const result = runGate(dir, true);
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
  console.error(`\ncontacts screen permission gate self-test FAILED:\n  ${problems.join("\n  ")}`);
  process.exit(1);
}
console.log(`contacts screen permission gate self-test: ${plants.length} plants, all caught`);

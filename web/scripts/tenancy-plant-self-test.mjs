// Self-test of the tenancy screens' permission gate (src/modules/tenancy/screens.test.tsx).
//
// Each plant offers on screen an action the user's permissions refuse. For every plant this
// script copies the web sources to a temporary folder, applies the plant there (never in the
// working tree), runs the gate, and requires the gate to FAIL on an assertion. A plant the gate
// does not catch fails this script. U1 and U2 are critic p02 round 3's plants (the company logo's
// upload and remove offered without tenancy.companies.update; the branch line and New branch
// offered without tenancy.branches.create); the others are the same fault on the other actions of
// these screens.
//
// Usage: node scripts/tenancy-plant-self-test.mjs   (from web/, after npm ci)
import { spawnSync } from "node:child_process";
import { cpSync, mkdtempSync, readFileSync, rmSync, symlinkSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";

const web = resolve(import.meta.dirname, "..");
const gate = "src/modules/tenancy/screens.test.tsx";

/** Each edit replaces exactly one occurrence of `find` in `file`. */
const plants = [
  {
    id: "U1",
    what: "company logo upload and remove offered without tenancy.companies.update",
    edits: [{ file: "src/modules/tenancy/CompanyForm.tsx", find: 'editable={can("tenancy.companies.update")} onChange={setCompany}', replace: "editable={true} onChange={setCompany}" }],
  },
  {
    id: "U2",
    what: "the company's branch line offered without tenancy.branches.create",
    edits: [{ file: "src/modules/tenancy/CompanyForm.tsx", find: '      {can("tenancy.branches.create") && (', replace: "      {(" }],
  },
  {
    id: "U2-new",
    what: "New branch (button and Alt+N) offered without tenancy.branches.create",
    edits: [{ file: "src/modules/tenancy/BranchesPage.tsx", find: 'useRecordPanel(can("tenancy.branches.create"))', replace: "useRecordPanel(true)" }],
  },
  {
    id: "U-company-new",
    what: "New company (button and Alt+N) offered without tenancy.companies.create",
    edits: [{ file: "src/modules/tenancy/CompaniesPage.tsx", find: 'useRecordPanel(can("tenancy.companies.create"))', replace: "useRecordPanel(true)" }],
  },
  {
    id: "U-company-save",
    what: "the company form editable and saved without tenancy.companies.update",
    edits: [{ file: "src/modules/tenancy/CompanyForm.tsx", find: ': can("tenancy.companies.update");', replace: ": true;" }],
  },
  {
    id: "U-company-address",
    what: "the company's address editable without tenancy.companies.update (found by this gate)",
    edits: [{ file: "src/modules/tenancy/CompanyForm.tsx", find: "<AddressFields draft={draft} set={set} errors={errors} disabled={!editable} />", replace: "<AddressFields draft={draft} set={set} errors={errors} />" }],
  },
  {
    id: "U-branch-save",
    what: "the branch form editable and saved without tenancy.branches.update",
    edits: [{ file: "src/modules/tenancy/BranchesPage.tsx", find: ': can("tenancy.branches.update");', replace: ": true;" }],
  },
  {
    id: "U-access-save",
    what: "company access editable and saved without tenancy.access.update",
    edits: [{ file: "src/modules/tenancy/AccessPage.tsx", find: 'const editable = can("tenancy.access.update") && ', replace: "const editable = " }],
  },
  {
    id: "U-access-stronger",
    what: "company access of a user the server marks read-only offered for change",
    edits: [{ file: "src/modules/tenancy/AccessPage.tsx", find: " && access.canEdit !== false;", replace: ";" }],
  },
  {
    id: "U-tenant-save",
    what: "workspace settings editable and saved without tenancy.tenant.update",
    edits: [{ file: "src/modules/tenancy/TenantPage.tsx", find: 'const editable = can("tenancy.tenant.update");', replace: "const editable = true;" }],
  },
];

function copyWeb() {
  const dir = mkdtempSync(join(tmpdir(), "erp-web-tenancy-plant-"));
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
  console.error(`\ntenancy screens permission gate self-test FAILED:\n  ${problems.join("\n  ")}`);
  process.exit(1);
}
console.log(`tenancy screens permission gate self-test: ${plants.length} plants, all caught`);

// Self-test of the tenancy screens' permission gate (src/modules/tenancy/screens.test.tsx).
//
// Each plant offers on screen an action the user's permissions refuse. For every plant this
// script copies the web sources to a temporary folder, applies the plant there (never in the
// working tree), runs the gate, and requires the gate to FAIL on an assertion. A plant the gate
// does not catch fails this script. U1 and U2 are critic p02 round 3's plants (the company logo's
// upload and remove offered without tenancy.companies.update; the branch line and New branch
// offered without tenancy.branches.create); P3b is critic p06 round 1's (the company form editable
// for a read-only user, on the shared record form), with the same fault on the branch, access and
// workspace forms; the U-*-some-branches plants are critic p02 round 4's (the branch line, New
// branch and the branch code offered to someone who works in only some branches of the company);
// the U-company-*-some-companies plants are critic p02 round 7's (New company and the company code
// offered to someone who works in only some companies); the others are the same fault on the other
// actions of these screens.
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
    edits: [{ file: "src/modules/tenancy/CompanyForm.tsx", find: 'editable={can("tenancy.companies.update") && everyBranch} onChange={form.adopt}', replace: "editable={everyBranch} onChange={form.adopt}" }],
  },
  {
    id: "U2",
    what: "the company's branch line offered without tenancy.branches.create",
    edits: [{ file: "src/modules/tenancy/CompanyForm.tsx", find: '{can("tenancy.branches.create") && everyBranch && (', replace: "{everyBranch && (" }],
  },
  {
    id: "U2-new",
    what: "New branch (button and Alt+N) offered without tenancy.branches.create",
    edits: [{ file: "src/modules/tenancy/BranchesPage.tsx", find: 'useRecordPanel(can("tenancy.branches.create") && creatable.length > 0)', replace: "useRecordPanel(creatable.length > 0)" }],
  },
  {
    id: "U-company-new",
    what: "New company (button and Alt+N) offered without tenancy.companies.create",
    edits: [{ file: "src/modules/tenancy/CompaniesPage.tsx", find: 'const creatable = can("tenancy.companies.create") && everyCompany;', replace: "const creatable = everyCompany;" }],
  },
  {
    id: "U-company-new-some-companies",
    what: "New company (button and Alt+N) offered to someone who works in only some companies (critic p02 round 7)",
    edits: [{ file: "src/modules/tenancy/CompaniesPage.tsx", find: 'const creatable = can("tenancy.companies.create") && everyCompany;', replace: 'const creatable = can("tenancy.companies.create");' }],
  },
  {
    id: "U-company-code-some-companies",
    what: "the company code editable by someone who works in only some companies (critic p02 round 7)",
    edits: [{ file: "src/modules/tenancy/CompanyForm.tsx", find: "          disabled={id !== null && !everyCompany}\n", replace: "" }],
  },
  {
    id: "U-company-address-new",
    what: "a new-company form opened from the address (?open=new) by someone the screen does not offer New",
    edits: [{ file: "src/modules/tenancy/CompaniesPage.tsx", find: "onOpenIdChange(id === newRecord && !creatable ? null : id)", replace: "onOpenIdChange(id)" }],
  },
  {
    id: "P3b",
    what: "the company form editable for everyone (critic p06 round 1's plant: canEdit true)",
    edits: [{ file: "src/modules/tenancy/CompanyForm.tsx", find: 'canEdit: id === null ? can("tenancy.companies.create") : can("tenancy.companies.update") && everyBranch,', replace: "canEdit: true," }],
  },
  {
    id: "U-company-save",
    what: "the company form editable and saved without tenancy.companies.update",
    edits: [{ file: "src/modules/tenancy/CompanyForm.tsx", find: ': can("tenancy.companies.update") && everyBranch,', replace: ": everyBranch," }],
  },
  {
    id: "U-company-address",
    what: "the company's address editable without tenancy.companies.update (its fields ignore the form's read-only state)",
    // Both halves: fields bound as editable, outside the form's read-only section (a field in a
    // disabled fieldset cannot be changed, so either half alone is not a fault).
    edits: [
      {
        file: "src/modules/tenancy/CompanyForm.tsx",
        find: 'const bind = form.bind as unknown as RecordFormState<R, AddressDraft>["bind"];',
        replace: 'const bind = ((key: string) => ({ ...(form.bind as unknown as (k: string) => object)(key), readOnly: false })) as unknown as RecordFormState<R, AddressDraft>["bind"];',
      },
      { file: "src/modules/tenancy/CompanyForm.tsx", find: '<FormSection title={t("tenancy.address.title")}>', replace: '<div className="form-section">' },
      { file: "src/modules/tenancy/CompanyForm.tsx", find: 'maxLength={400} />\n    </FormSection>', replace: 'maxLength={400} />\n    </div>' },
    ],
  },
  {
    id: "U-company-some-branches",
    what: "the company record editable by someone who works in only some of its branches",
    edits: [{ file: "src/modules/tenancy/CompanyForm.tsx", find: ': can("tenancy.companies.update") && everyBranch,', replace: ': can("tenancy.companies.update"),' }],
  },
  {
    id: "U-logo-some-branches",
    what: "the company logo changeable by someone who works in only some of its branches",
    edits: [{ file: "src/modules/tenancy/CompanyForm.tsx", find: 'editable={can("tenancy.companies.update") && everyBranch} onChange={form.adopt}', replace: 'editable={can("tenancy.companies.update")} onChange={form.adopt}' }],
  },
  {
    id: "U-branchline-some-branches",
    what: "the company's branch line offered to someone who works in only some of its branches (critic p02 round 4)",
    edits: [{ file: "src/modules/tenancy/CompanyForm.tsx", find: '{can("tenancy.branches.create") && everyBranch && (', replace: '{can("tenancy.branches.create") && (' }],
  },
  {
    id: "U-newbranch-some-branches",
    what: "New branch offered to someone who works in only some branches of every company (critic p02 round 4)",
    edits: [{ file: "src/modules/tenancy/BranchesPage.tsx", find: "companies.filter((c) => c.isActive && c.everyBranch !== false)", replace: "companies.filter((c) => c.isActive)" }],
  },
  {
    id: "U-branchcode-some-branches",
    what: "a branch code editable by someone who works in only some branches of its company (critic p02 round 4)",
    edits: [{ file: "src/modules/tenancy/BranchesPage.tsx", find: "          disabled={branch?.everyBranch === false}\n", replace: "" }],
  },
  {
    id: "P3b-branch",
    what: "the branch form editable for everyone (P3b on the branch screen)",
    edits: [{ file: "src/modules/tenancy/BranchesPage.tsx", find: 'canEdit: id === null ? can("tenancy.branches.create") : can("tenancy.branches.update"),', replace: "canEdit: true," }],
  },
  {
    id: "U-branch-save",
    what: "the branch form editable and saved without tenancy.branches.update",
    edits: [{ file: "src/modules/tenancy/BranchesPage.tsx", find: ': can("tenancy.branches.update"),', replace: ": true," }],
  },
  {
    id: "P3b-access",
    what: "the company access form saveable for everyone (P3b on the access screen)",
    edits: [{ file: "src/modules/tenancy/AccessPage.tsx", find: "canEdit: editable,", replace: "canEdit: true," }],
  },
  {
    id: "U-access-save",
    what: "company access editable and saved without tenancy.access.update",
    edits: [{ file: "src/modules/tenancy/AccessPage.tsx", find: 'setEditable(can("tenancy.access.update") && ', replace: "setEditable(" }],
  },
  {
    id: "U-access-stronger",
    what: "company access of a user the server marks read-only offered for change",
    edits: [{ file: "src/modules/tenancy/AccessPage.tsx", find: " && access.canEdit !== false);", replace: ");" }],
  },
  {
    id: "U-access-boxes",
    what: "the company and branch boxes of a read-only access form left changeable",
    edits: [{ file: "src/modules/tenancy/AccessPage.tsx", find: '<fieldset disabled={!editable} className="access-list form-section">', replace: '<fieldset className="access-list form-section">' }],
  },
  {
    id: "U-tenant-save",
    what: "workspace settings editable and saved without tenancy.tenant.update",
    edits: [{ file: "src/modules/tenancy/TenantPage.tsx", find: 'const mayUpdate = can("tenancy.tenant.update");', replace: "const mayUpdate = true;" }],
  },
  {
    id: "U-tenant-some-companies",
    what: "workspace settings offered to someone who works in only some companies or branches (critic p02 round 6)",
    edits: [{ file: "src/modules/tenancy/TenantPage.tsx", find: "const editable = mayUpdate && everyCompany;", replace: "const editable = mayUpdate;" }],
  },
  {
    id: "P3b-tenant",
    what: "the workspace settings form shown and editable for everyone (P3b on the workspace screen)",
    // Both halves: the settings form is drawn only for tenancy.tenant.update, so the form must
    // also be shown to everyone for canEdit to offer anything.
    edits: [
      { file: "src/modules/tenancy/TenantPage.tsx", find: "canEdit: editable,", replace: "canEdit: true," },
      { file: "src/modules/tenancy/TenantPage.tsx", find: "{editable && tenant && (", replace: "{tenant && (" },
    ],
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
  console.error(`\ntenancy screens permission gate self-test FAILED:\n  ${problems.join("\n  ")}`);
  process.exit(1);
}
console.log(`tenancy screens permission gate self-test: ${plants.length} plants, all caught`);

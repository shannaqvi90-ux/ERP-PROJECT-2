// Checks gauntlet/ratchet.json after a verify run:
//  - against the committed version (base): minimums may only rise, maximums only fall, no key may
//    disappear (CLAUDE.md rule 9);
//  - the suite counts of this run (.NET tests from TRX, web unit tests, end-to-end tests) reach the
//    suite minimums, and nothing failed or was skipped.
// Usage: node ratchet-check.mjs <ratchet.json> <out-dir> [base-ratchet.json]
import { existsSync, readdirSync, readFileSync } from "node:fs";
import { join } from "node:path";

const [ratchetPath, outDir, basePath] = process.argv.slice(2);
const ratchet = JSON.parse(readFileSync(ratchetPath, "utf8"));
const problems = [];

if (basePath && existsSync(basePath)) {
  const base = JSON.parse(readFileSync(basePath, "utf8"));
  for (const [key, value] of Object.entries(base.minimums ?? {})) {
    const now = ratchet.minimums?.[key];
    if (now === undefined) problems.push(`minimum ${key} was removed (was ${value})`);
    else if (now < value) problems.push(`minimum ${key} went down: ${value} -> ${now}`);
  }
  for (const [key, value] of Object.entries(base.maximums ?? {})) {
    const now = ratchet.maximums?.[key];
    if (now === undefined) problems.push(`maximum ${key} was removed (was ${value})`);
    else if (now > value) problems.push(`maximum ${key} went up: ${value} -> ${now}`);
  }
} else {
  console.log("ratchet: no committed base to compare with (first version)");
}

const counts = { dotnet: { passed: 0, failed: 0, skipped: 0 }, web: { passed: 0, failed: 0, skipped: 0 }, e2e: { passed: 0, failed: 0, skipped: 0 } };
const trxDir = join(outDir, "trx");
if (existsSync(trxDir)) {
  for (const file of readdirSync(trxDir).filter((f) => f.endsWith(".trx"))) {
    const xml = readFileSync(join(trxDir, file), "utf8");
    const m = /<Counters\s[^>]*total="(\d+)"[^>]*passed="(\d+)"[^>]*failed="(\d+)"/.exec(xml);
    if (m) {
      counts.dotnet.passed += Number(m[2]);
      counts.dotnet.failed += Number(m[3]);
      counts.dotnet.skipped += Number(m[1]) - Number(m[2]) - Number(m[3]);
    }
  }
}
const vitest = join(outDir, "vitest.json");
if (existsSync(vitest)) {
  const v = JSON.parse(readFileSync(vitest, "utf8"));
  counts.web = { passed: v.numPassedTests, failed: v.numFailedTests, skipped: v.numPendingTests + (v.numTodoTests ?? 0) };
}
const e2e = join(outDir, "e2e.json");
if (existsSync(e2e)) {
  const r = JSON.parse(readFileSync(e2e, "utf8"));
  counts.e2e = { passed: r.stats.expected, failed: r.stats.unexpected + r.stats.flaky, skipped: r.stats.skipped };
}

const suite = [
  ["suite.dotnetTests", counts.dotnet],
  ["suite.webUnitTests", counts.web],
  ["suite.e2eTests", counts.e2e],
];
for (const [key, c] of suite) {
  const min = ratchet.minimums?.[key];
  if (min === undefined) problems.push(`ratchet has no minimum ${key}`);
  else if (c.passed < min) problems.push(`${key}: ${c.passed} passed, ratchet minimum ${min}`);
  if (c.failed > 0) problems.push(`${key}: ${c.failed} failed`);
  if (c.skipped > 0) problems.push(`${key}: ${c.skipped} skipped (skipping is weakening)`);
}

console.log("\nTest counts this run:");
for (const [key, c] of suite) console.log(`  ${key.padEnd(22)} passed ${String(c.passed).padStart(5)}  failed ${c.failed}  skipped ${c.skipped}  (minimum ${ratchet.minimums?.[key]})`);

if (problems.length > 0) {
  console.error("\nRatchet check FAILED:\n  " + problems.join("\n  "));
  process.exit(1);
}
console.log("Ratchet check passed.");

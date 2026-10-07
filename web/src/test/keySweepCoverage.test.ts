import { existsSync, readdirSync, readFileSync } from "node:fs";
import { join, resolve } from "node:path";
import { describe, expect, it } from "vitest";
import { readerMayWrite } from "./keySweep";

// G2 on screen, by keyboard: a module whose screens can write must have its screens swept by
// keyboard (src/modules/<module>/keyboard.test.tsx, with sweepKeys) for a user who may only read
// them. A module added later with screens that write and no sweep fails here, so the keyboard gate
// grows with the product instead of covering only the screens that existed when it was written.

const modules = resolve(import.meta.dirname, "../modules");

/** Writes a module's screens send: api("POST" | "PUT" | "PATCH" | "DELETE", "<address>", ...). */
function writesOf(module: string): { method: string; url: string }[] {
  const out: { method: string; url: string }[] = [];
  const walk = (dir: string) => {
    for (const entry of readdirSync(dir, { withFileTypes: true })) {
      const path = join(dir, entry.name);
      if (entry.isDirectory()) walk(path);
      else if (/\.tsx?$/.test(entry.name) && !/\.test\.tsx?$/.test(entry.name)) {
        const text = readFileSync(path, "utf8");
        for (const m of text.matchAll(/\bapi(?:<[^>]*>)?\(\s*"(POST|PUT|PATCH|DELETE)",\s*[`"]([^`"]+)[`"]/g)) {
          out.push({ method: m[1]!, url: m[2]!.replace(/\$\{[^}]+\}/g, "x") });
        }
      }
    }
  };
  walk(join(modules, module));
  return out;
}

describe("keyboard sweep coverage", () => {
  const all = readdirSync(modules, { withFileTypes: true }).filter((d) => d.isDirectory()).map((d) => d.name);

  it("finds the modules' writes (the check is not blind)", () => {
    expect(writesOf("identity").length).toBeGreaterThanOrEqual(8);
    expect(writesOf("tenancy").length).toBeGreaterThanOrEqual(5);
  });

  for (const module of all) {
    it(`${module}: screens that write are swept by keyboard for a reader`, () => {
      const writes = writesOf(module).filter((w) => !readerMayWrite(w));
      if (writes.length === 0) return;
      const sweep = join(modules, module, "keyboard.test.tsx");
      expect(existsSync(sweep), `${module} writes (${writes.map((w) => `${w.method} ${w.url}`).join(", ")}) and has no keyboard.test.tsx`).toBe(true);
      expect(readFileSync(sweep, "utf8")).toMatch(/\bsweepKeys\(/);
    });
  }
});

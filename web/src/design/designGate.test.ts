import { readdirSync, readFileSync, statSync, existsSync } from "node:fs";
import { join, relative } from "node:path";
import { describe, expect, it } from "vitest";

/**
 * Gate for the design system (p18-visual-design): tokens.css is the one source of colour, and every
 * theme keeps its text readable. A stylesheet or screen that writes its own colour, a token that
 * is used but never defined, a theme missing a token, a native date box (which shows the date
 * month first on an American browser) or a font that is not bundled fails here.
 */
const root = join(import.meta.dirname, ".."); // web/src/
const web = join(root, "..");
const tokensPath = join(root, "design", "tokens.css");
const tokens = readFileSync(tokensPath, "utf8");

function walk(dir: string, out: string[] = []): string[] {
  for (const name of readdirSync(dir)) {
    const path = join(dir, name);
    if (statSync(path).isDirectory()) walk(path, out);
    else out.push(path);
  }
  return out;
}

const sources = walk(root).filter((f) => /\.(css|tsx?)$/.test(f) && !/\.test\.tsx?$/.test(f) && f !== tokensPath);
const stylesheets = sources.filter((f) => f.endsWith(".css"));

/** The declarations of a rule whose selector matches, by name -> value. */
function block(selector: string): Record<string, string> {
  const start = tokens.indexOf(selector);
  if (start < 0) throw new Error(`no ${selector} in tokens.css`);
  const open = tokens.indexOf("{", start);
  let depth = 0;
  let end = open;
  for (; end < tokens.length; end++) {
    if (tokens[end] === "{") depth++;
    if (tokens[end] === "}" && --depth === 0) break;
  }
  const body = tokens.slice(open + 1, end).replace(/\/\*[\s\S]*?\*\//g, "");
  const out: Record<string, string> = {};
  for (const m of body.matchAll(/(--[a-z0-9-]+)\s*:\s*([^;]+);/g)) out[m[1]!] = m[2]!.trim();
  return out;
}

const dark = { ...block(":root{\n  /* G additions"), };
const light = block('[data-theme="light"]');
const hc = block('[data-theme="hc"]');

function luminance(hex: string): number {
  const n = parseInt(hex.slice(1), 16);
  const channel = (v: number) => {
    const s = v / 255;
    return s <= 0.03928 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4;
  };
  return 0.2126 * channel((n >> 16) & 255) + 0.7152 * channel((n >> 8) & 255) + 0.0722 * channel(n & 255);
}
const contrast = (a: string, b: string) => {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (hi! + 0.05) / (lo! + 0.05);
};

describe("design tokens", () => {
  it("are the only place a colour is written: no hex or rgb literal in any other stylesheet or screen", () => {
    const found: string[] = [];
    for (const file of sources) {
      const text = readFileSync(file, "utf8").replace(/\/\*[\s\S]*?\*\//g, "");
      for (const m of text.matchAll(/#[0-9a-fA-F]{3,8}\b(?![\w-])|\brgba?\(|\bhsla?\(/g)) {
        const before = text.slice(Math.max(0, m.index! - 12), m.index!);
        if (/&$|&#$|href="$|key="$|\/\/\s*$/.test(before)) continue;
        if (file.endsWith(".css") || /style=|color|background|fill|stroke/i.test(text.slice(Math.max(0, m.index! - 60), m.index!))) {
          found.push(`${relative(root, file)}: ${m[0]}`);
        }
      }
    }
    expect(found, "colour literals outside design/tokens.css").toEqual([]);
  });

  it("every var(--name) a stylesheet or screen uses is defined in the tokens or set where it is used", () => {
    const defined = new Set<string>([...tokens.replace(/\/\*[\s\S]*?\*\//g, "").matchAll(/(--[a-z0-9-]+)\s*:/g)].map((m) => m[1]!));
    const set = new Set<string>();
    const used = new Map<string, string>();
    for (const file of sources) {
      const text = readFileSync(file, "utf8");
      for (const m of text.matchAll(/(--[a-z0-9-]+)\s*:/g)) set.add(m[1]!);
      for (const m of text.matchAll(/(["'`])(--[a-z0-9-]+)\1/g)) set.add(m[2]!);
      for (const m of text.matchAll(/var\((--[a-z0-9-]+)/g)) used.set(m[1]!, relative(root, file));
    }
    const missing = [...used].filter(([name]) => !defined.has(name) && !set.has(name)).map(([name, file]) => `${name} (${file})`);
    expect(missing).toEqual([]);
  });

  it("light and high contrast define every colour token the dark theme defines", () => {
    const colour = (v: string) => /#[0-9a-fA-F]{3,8}|rgba?\(|gradient|none|block|transparent/.test(v);
    const names = Object.keys(dark).filter((n) => colour(dark[n]!) && !/^--(font|dur|ease)/.test(n));
    expect(names.length).toBeGreaterThan(60);
    for (const [theme, set] of [["light", light], ["hc", hc]] as const) {
      expect(names.filter((n) => !(n in set)), `${theme} lacks`).toEqual([]);
    }
  });

  it("keep text readable: 4.5:1 in light and dark, 7:1 in high contrast, and non-text lines visible", () => {
    const themes = { dark, light, hc };
    for (const [name, set] of Object.entries(themes)) {
      const text = name === "hc" ? 7 : 4.5;
      for (const ground of ["--bg", "--surface", "--surface-2", "--raised"]) {
        for (const fg of ["--fg", "--fg-muted", "--fg-subtle", "--accent-text", "--danger", "--positive", "--warning", "--info"]) {
          const ratio = contrast(set[fg]!, set[ground]!);
          expect(ratio, `${name}: ${fg} on ${ground} is ${ratio.toFixed(2)}`).toBeGreaterThanOrEqual(text);
        }
      }
      expect(contrast(set["--on-accent"]!, set["--accent"]!), `${name}: on-accent on accent`).toBeGreaterThanOrEqual(text);
      expect(contrast(set["--focus"]!, set["--surface"]!), `${name}: focus ring`).toBeGreaterThanOrEqual(3);
      expect(contrast(set["--line-strong"]!, set["--surface"]!), `${name}: control edge`).toBeGreaterThanOrEqual(name === "hc" ? 7 : 1.5);
    }
  });

  it("name the bundled fonts, and every font file they point to is shipped with its licence", () => {
    const files = [...tokens.matchAll(/url\("\/fonts\/([^"]+)"\)/g)].map((m) => m[1]!);
    expect(files.length).toBe(3);
    for (const file of files) expect(existsSync(join(web, "public", "fonts", file)), file).toBe(true);
    for (const licence of ["OFL-geist.txt", "OFL-geist-mono.txt", "OFL-readex-pro.txt"]) expect(existsSync(join(web, "public", "fonts", licence)), licence).toBe(true);
    expect(block(":root{\n  /* G additions")["--font-ui"]).toContain("Bz Readex");
  });

  it("leave the date to our own day-first field: no native date input outside kernel/forms/dateInput.tsx", () => {
    const offenders = sources.filter((f) => /type=["']date["']|type: ["']date["']/.test(readFileSync(f, "utf8").replace(/\/\*[\s\S]*?\*\//g, "")) && !f.endsWith("dateInput.tsx") && !f.endsWith("parts.tsx"));
    expect(offenders.map((f) => relative(root, f))).toEqual([]);
  });

  it("every stylesheet uses logical properties only (it mirrors in Arabic)", () => {
    const physical = /(^|[;{\s])(margin|padding)-(left|right)\s*:|(^|[;{\s])(left|right)\s*:|text-align\s*:\s*(left|right)|border-(left|right)(-[a-z]+)?\s*:/m;
    const bad = stylesheets.filter((f) => physical.test(readFileSync(f, "utf8").replace(/\/\*[\s\S]*?\*\//g, "")));
    expect(bad.map((f) => relative(root, f))).toEqual([]);
  });
});

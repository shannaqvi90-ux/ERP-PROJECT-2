// Gate: no hard-coded user-facing text in screens, and every string key used exists.
// CLAUDE.md rule 5 (English and Arabic from the first screen). Run by `npm run check`.
//
//  1. JSX text containing letters (any script) is hard-coded text  -> fail.
//  2. String literals with letters in user-facing attributes (title, placeholder, aria-label,
//     alt, label, aria-description, aria-placeholder)               -> fail.
//  3. Every literal key passed to t("...") exists in the English and Arabic strings; template keys
//     t(`prefix.${x}`) must match at least one key with that prefix.
//  4. Each module's keys start with "<module>." so modules never collide.
//  5. English and Arabic texts of a key use the same placeholders, and every plural message
//     ({n, plural, ...}) covers the CLDR categories its language needs (English one/other;
//     Arabic zero/one/two/few/many/other).
import { readFileSync, readdirSync, statSync } from "node:fs";
import { join, relative, sep } from "node:path";
import ts from "typescript";

const root = new URL("..", import.meta.url).pathname;
const src = join(root, "src");
const problems = [];

function walk(dir, out = []) {
  for (const name of readdirSync(dir)) {
    const path = join(dir, name);
    if (statSync(path).isDirectory()) walk(path, out);
    else out.push(path);
  }
  return out;
}

const files = walk(src);
const strings = { en: {}, ar: {} };
for (const file of files.filter((f) => /[\\/]i18n[\\/](en|ar)\.json$/.test(f))) {
  const language = file.endsWith("en.json") ? "en" : "ar";
  const module = relative(join(src, "modules"), file).split(sep)[0];
  const data = JSON.parse(readFileSync(file, "utf8"));
  for (const [key, value] of Object.entries(data)) {
    if (!key.startsWith(`${module}.`)) problems.push(`${relative(root, file)}: key "${key}" must start with "${module}."`);
    if (key in strings[language]) problems.push(`${relative(root, file)}: key "${key}" defined twice`);
    if (typeof value !== "string" || value.trim() === "") problems.push(`${relative(root, file)}: key "${key}" is empty`);
    strings[language][key] = value;
  }
}
for (const key of Object.keys(strings.en)) if (!(key in strings.ar)) problems.push(`ar: missing "${key}"`);

const needed = { en: ["one", "other"], ar: ["zero", "one", "two", "few", "many", "other"] };
function matching(text, open) {
  let depth = 0;
  for (let i = open; i < text.length; i++) {
    if (text[i] === "{") depth++;
    else if (text[i] === "}" && --depth === 0) return i;
  }
  return -1;
}
function branches(text) {
  const out = [];
  const re = /\s*([^\s{]+)\s*\{/g;
  let i = 0;
  while (i < text.length) {
    re.lastIndex = i;
    const m = re.exec(text);
    if (!m || m.index !== i) break;
    const open = m.index + m[0].length - 1;
    const end = matching(text, open);
    if (end < 0) break;
    out.push({ selector: m[1], text: text.slice(open + 1, end) });
    i = end + 1;
  }
  return out;
}
// Placeholders and plural messages of a text.
function analyse(text, found = { names: new Set(), plurals: [] }) {
  for (let i = 0; i < text.length; i++) {
    if (text[i] !== "{") continue;
    const end = matching(text, i);
    if (end < 0) {
      found.names.add("<unbalanced braces>");
      break;
    }
    const parts = text.slice(i + 1, end).split(",");
    if (parts.length >= 3 && parts[1].trim() === "plural") {
      const list = branches(parts.slice(2).join(","));
      found.names.add(parts[0].trim());
      found.plurals.push({ variable: parts[0].trim(), selectors: list.map((b) => b.selector) });
      for (const b of list) analyse(b.text, found);
    } else {
      found.names.add(parts[0].trim());
    }
    i = end;
  }
  return found;
}
let pluralMessages = 0;
for (const key of Object.keys(strings.en)) {
  if (!(key in strings.ar)) continue;
  const en = analyse(strings.en[key]);
  const ar = analyse(strings.ar[key]);
  const enNames = [...en.names].sort().join(",");
  const arNames = [...ar.names].sort().join(",");
  if (enNames !== arNames) problems.push(`"${key}": English uses {${enNames}} but Arabic uses {${arNames}}`);
  for (const [language, found] of [["en", en], ["ar", ar]]) {
    for (const plural of found.plurals) {
      pluralMessages++;
      const missing = needed[language].filter((c) => !plural.selectors.includes(c));
      if (missing.length > 0) problems.push(`${language}: "${key}" plural {${plural.variable}} lacks ${missing.join(", ")}`);
    }
  }
}
for (const key of Object.keys(strings.ar)) if (!(key in strings.en)) problems.push(`en: missing "${key}"`);

const userFacingAttributes = new Set(["title", "placeholder", "aria-label", "alt", "label", "aria-description", "aria-placeholder"]);
const hasLetters = (text) => /\p{L}/u.test(text);
let scanned = 0;

for (const file of files.filter((f) => /\.(tsx|ts)$/.test(f) && !/\.test\.tsx?$/.test(f))) {
  scanned++;
  const text = readFileSync(file, "utf8");
  const source = ts.createSourceFile(file, text, ts.ScriptTarget.Latest, true, file.endsWith(".tsx") ? ts.ScriptKind.TSX : ts.ScriptKind.TS);
  const where = (node) => {
    const { line } = source.getLineAndCharacterOfPosition(node.getStart());
    return `${relative(root, file)}:${line + 1}`;
  };
  const visit = (node) => {
    if (ts.isJsxText(node) && hasLetters(node.text)) {
      problems.push(`${where(node)}: hard-coded text "${node.text.trim()}" (use t("..."))`);
    }
    if (ts.isJsxAttribute(node) && userFacingAttributes.has(node.name.getText()) && node.initializer) {
      const init = node.initializer;
      const literal = ts.isStringLiteral(init)
        ? init.text
        : ts.isJsxExpression(init) && init.expression && ts.isStringLiteralLike(init.expression)
          ? init.expression.text
          : null;
      if (literal !== null && hasLetters(literal)) {
        problems.push(`${where(node)}: hard-coded ${node.name.getText()}="${literal}" (use t("..."))`);
      }
    }
    if (ts.isCallExpression(node) && ts.isIdentifier(node.expression) && (node.expression.text === "t" || node.expression.text === "translate")) {
      const arg = node.expression.text === "t" ? node.arguments[0] : node.arguments[1];
      if (arg && ts.isStringLiteralLike(arg)) {
        if (!(arg.text in strings.en)) problems.push(`${where(node)}: unknown string key "${arg.text}"`);
      } else if (arg && ts.isTemplateExpression(arg)) {
        const prefix = arg.head.text;
        if (!Object.keys(strings.en).some((k) => k.startsWith(prefix))) problems.push(`${where(node)}: no string key starts with "${prefix}"`);
      }
    }
    ts.forEachChild(node, visit);
  };
  visit(source);
}

if (problems.length > 0) {
  console.error(`String check failed (${problems.length}):\n` + problems.join("\n"));
  process.exit(1);
}
console.log(`String check passed: ${scanned} source files, ${Object.keys(strings.en).length} keys in English and Arabic, ${pluralMessages} plural messages.`);

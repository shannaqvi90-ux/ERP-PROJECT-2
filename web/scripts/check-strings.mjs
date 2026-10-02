// Gate: no hard-coded user-facing text in screens, and every string key used exists.
// CLAUDE.md rule 5 (English and Arabic from the first screen). Run by `npm run check`.
//
//  1. JSX text containing letters (any script) is hard-coded text  -> fail.
//  2. String literals with letters in user-facing attributes (title, placeholder, aria-label,
//     alt, label, aria-description, aria-placeholder)               -> fail.
//  3. Every literal key passed to t("...") exists in the English and Arabic strings; template keys
//     t(`prefix.${x}`) must match at least one key with that prefix.
//  4. Each module's keys start with "<module>." so modules never collide.
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
console.log(`String check passed: ${scanned} source files, ${Object.keys(strings.en).length} keys in English and Arabic.`);

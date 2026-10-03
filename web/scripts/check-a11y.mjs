// Gate: accessibility basics every screen keeps (p04 shell; CLAUDE.md rule 5 and the bar's
// "every action reachable without a mouse"). Run by `npm run check`.
//
//  1. No positive tabIndex: the Tab order is the reading order, in both directions.
//  2. A click handler on a non-interactive element (div, span, li, td, …) needs a role, so
//     assistive technology knows it acts, and keyboard users get an equivalent.
//  3. A button that shows only an icon has an accessible name (aria-label or aria-labelledby).
//  4. Every input, select and textarea has a label: inside a <label>, or aria-label,
//     aria-labelledby, or an id a <label htmlFor> can point at.
//  5. Every <img> has alt text (empty for decoration).
import { readFileSync, readdirSync, statSync } from "node:fs";
import { join, relative } from "node:path";
import ts from "typescript";

const root = new URL("..", import.meta.url).pathname;
const src = join(root, "src");
const problems = [];
let elements = 0;

function walk(dir, out = []) {
  for (const name of readdirSync(dir)) {
    const path = join(dir, name);
    if (statSync(path).isDirectory()) walk(path, out);
    else if (path.endsWith(".tsx") && !/\.test\.tsx$/.test(path)) out.push(path);
  }
  return out;
}

const nonInteractive = new Set(["div", "span", "li", "td", "tr", "th", "p", "section", "article", "header", "footer", "main", "ul", "ol", "img", "svg", "label", "dl", "dt", "dd"]);
const iconOnly = new Set(["Icon", "svg"]);

const attrs = (opening) => {
  const map = new Map();
  for (const prop of opening.attributes.properties) {
    if (ts.isJsxAttribute(prop)) map.set(prop.name.getText(), prop.initializer);
    else if (ts.isJsxSpreadAttribute(prop)) map.set("...", prop);
  }
  return map;
};

for (const file of walk(src)) {
  const text = readFileSync(file, "utf8");
  const source = ts.createSourceFile(file, text, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
  const where = (node) => `${relative(root, file)}:${source.getLineAndCharacterOfPosition(node.getStart()).line + 1}`;

  const visit = (node, insideLabel) => {
    let opening = null;
    let children = [];
    if (ts.isJsxElement(node)) {
      opening = node.openingElement;
      children = node.children;
    } else if (ts.isJsxSelfClosingElement(node)) {
      opening = node;
    }
    let labelled = insideLabel;
    if (opening) {
      elements++;
      const tag = opening.tagName.getText();
      const a = attrs(opening);
      const spread = a.has("...");
      const tabIndex = a.get("tabIndex");
      if (tabIndex && ts.isJsxExpression(tabIndex) && tabIndex.expression && ts.isNumericLiteral(tabIndex.expression) && Number(tabIndex.expression.text) > 0) {
        problems.push(`${where(opening)}: tabIndex ${tabIndex.expression.text} > 0 breaks the reading order`);
      }
      if (nonInteractive.has(tag) && a.has("onClick") && !a.has("role")) {
        problems.push(`${where(opening)}: <${tag} onClick> needs a role (and a keyboard equivalent), or use a <button>`);
      }
      if (tag === "button" && !spread && !a.has("aria-label") && !a.has("aria-labelledby")) {
        const meaningful = children.filter((c) => !(ts.isJsxText(c) && c.text.trim() === ""));
        const onlyIcons = meaningful.length > 0 && meaningful.every((c) => (ts.isJsxSelfClosingElement(c) || ts.isJsxElement(c)) && iconOnly.has((ts.isJsxElement(c) ? c.openingElement : c).tagName.getText()));
        if (meaningful.length === 0 || onlyIcons) problems.push(`${where(opening)}: a button that shows only an icon needs aria-label`);
      }
      if ((tag === "input" || tag === "select" || tag === "textarea") && !spread) {
        const type = a.get("type");
        const hidden = type && ts.isStringLiteral(type) && type.text === "hidden";
        if (!hidden && !insideLabel && !a.has("aria-label") && !a.has("aria-labelledby") && !a.has("id")) {
          problems.push(`${where(opening)}: <${tag}> has no label (wrap it in <label>, or give aria-label / aria-labelledby / id)`);
        }
      }
      if (tag === "img" && !spread && !a.has("alt")) problems.push(`${where(opening)}: <img> needs alt text`);
      if (tag === "label") labelled = true;
    }
    ts.forEachChild(node, (child) => visit(child, labelled));
  };
  visit(source, false);
}

if (problems.length > 0) {
  console.error(`Accessibility check failed (${problems.length}):\n` + problems.join("\n"));
  process.exit(1);
}
console.log(`Accessibility check passed: ${elements} JSX elements checked.`);

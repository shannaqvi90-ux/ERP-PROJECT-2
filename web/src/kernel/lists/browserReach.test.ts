import { existsSync, readdirSync, readFileSync } from "node:fs";
import { dirname, relative, resolve } from "node:path";
import ts from "typescript";
import { describe, expect, it } from "vitest";

// G1 for the list component, statically (critic p05 round 14, plant L14): the list kernel is the
// screen every 100,000-row list of the product uses, and anything it keeps on the device outlives
// the person who signed in. This gate reads the list kernel's sources with the TypeScript checker
// and lists every browser (DOM) API they reach, by the interface that declares it, so an alias
// (`const s = navigator.storage; s.getDirectory()`), a quoted member (`window["localStorage"]`) or a
// new file of the kernel is seen exactly as a direct call is. Default deny: every DOM member must be
// reviewed below; the members that outlive the document (storage, the origin private file system,
// IndexedDB, caches, cookies, window.name, history, the clipboard, workers, channels ...) are reviewed
// per file with their exact count, so a new use is a new decision. Ways around the checker (`any`,
// computed members of browser objects, reflection, dynamic imports, ts-ignore) are refused outright,
// and every module the kernel imports from outside its folder is reviewed. The behaviour is judged
// separately (deviceStores.test.tsx, and tests/e2e lists.spec.ts in a real browser).

const listDir = resolve(import.meta.dirname);
const webRoot = resolve(listDir, "../../..");
const rel = (path: string) => relative(listDir, path).replace(/\\/g, "/");

/** Members that keep or hand on data beyond the document: reviewed per file with their count. */
const outlivesDocument: RegExp[] = [
  /^globalThis\.(localStorage|sessionStorage|indexedDB|caches|history|name|open|opener|postMessage|cookieStore|navigator|location=|fetch|XMLHttpRequest|WebSocket|EventSource|BroadcastChannel|Worker|SharedWorker|MessageChannel|showSaveFilePicker|showOpenFilePicker|showDirectoryPicker|webkitRequestFileSystem|requestFileSystem|top|parent|frames)=?$/,
  /^(Storage|StorageManager|StorageBucketManager|StorageBucket|FileSystem\w*|File|Blob|IDB\w*|CacheStorage|Cache|CookieStore|BroadcastChannel|MessagePort|MessageChannel|ServiceWorker\w*|Worker|SharedWorker|Clipboard|ClipboardItem|DataTransfer|LockManager|History|WindowLocalStorage|WindowSessionStorage|XMLHttpRequest|WebSocket|EventSource|Credential\w*)\./,
  /^WindowOrWorkerGlobalScope\.(indexedDB|caches|fetch)$/,
  /^Navigator\.(?!language$|languages$|platform$|userAgent$|maxTouchPoints$)/,
  /^Document\.(cookie|open|write|writeln|domain)=?$/,
  /^Window\.(name|open|opener|postMessage|localStorage|sessionStorage|history|location=|top|parent|frames)=?$/,
  /^Location\.\w+=$/,
  /^Location\.(assign|replace|reload)$/,
  /^URL\.(createObjectURL|revokeObjectURL)$/,
  /^HTMLAnchorElement\.(download|href)=?$/,
  /^HTMLFormElement\.(submit|requestSubmit|action)=?$/,
  /^HTMLIFrameElement\./,
];

/** ES members that read or reach objects around the type checker: never reviewed. */
const reflective = /^(globalThis\.(eval|Function|Proxy|Reflect)|Reflect\.\w+|ProxyConstructor\.\w+|FunctionConstructor\.\w+|ObjectConstructor\.(getOwnPropertyDescriptors?|getPrototypeOf|defineProperty|defineProperties|getOwnPropertyNames|setPrototypeOf))$/;

/**
 * The DOM members the list kernel uses that live and die with the document: they read and move the
 * page's own elements, events, timers and the address bar. Reviewed by member (any file, any count).
 */
const documentBound: Record<string, string[]> = {
  "Reads and moves the grid's own elements (layout, scroll, focus, attributes); gone with the document": [
    "CSSStyleProperties.fontSize",
    "DOMRect.width",
    "DOMStringMap.__index",
    "Document.body",
    "Document.documentElement",
    "DocumentOrShadowRoot.activeElement",
    "Element.clientHeight",
    "Element.clientWidth",
    "Element.closest",
    "Element.getBoundingClientRect",
    "Element.id",
    "Element.releasePointerCapture",
    "Element.scrollTop",
    "Element.scrollTop=",
    "Element.scrollWidth",
    "Element.setPointerCapture",
    "Element.tagName",
    "Element.textContent",
    "HTMLElement.offsetWidth",
    "HTMLInputElement.checked",
    "HTMLInputElement.select",
    "HTMLInputElement.value",
    "HTMLSelectElement.value",
    "HTMLOrSVGElement.dataset",
    "HTMLOrSVGElement.focus",
    "Node.contains",
    "ParentNode.querySelector",
    "ParentNode.querySelectorAll",
    "globalThis.document",
    "globalThis.getComputedStyle",
  ],
  "Events, observers and timers of the document (keys, pointer, resize, fonts loading); gone with the document": [
    "Document.addEventListener",
    "Document.removeEventListener",
    "Event.defaultPrevented",
    "Event.preventDefault",
    "Event.target",
    "FontFaceSet.addEventListener",
    "FontFaceSet.removeEventListener",
    "FontFaceSource.fonts",
    "KeyboardEvent.key",
    "MediaQueryList.matches",
    "MutationObserver.disconnect",
    "MutationObserver.observe",
    "ResizeObserver.disconnect",
    "ResizeObserver.observe",
    "globalThis.MutationObserver",
    "globalThis.ResizeObserver",
    "Window.addEventListener",
    "Window.removeEventListener",
    "Window.matchMedia",
    "WindowOrWorkerGlobalScope.clearTimeout",
    "WindowOrWorkerGlobalScope.setTimeout",
    "globalThis.window",
    "AbortController.abort",
    "AbortController.signal",
    "AbortSignal.aborted",
    "globalThis.AbortController",
  ],
  "Builds and reads query strings and reads the current address (writes go through the reviewed history member below)": [
    "URLSearchParams.append",
    "URLSearchParams.forEach",
    "URLSearchParams.get",
    "URLSearchParams.has",
    "URLSearchParams.set",
    "URLSearchParams.toString",
    "globalThis.URLSearchParams",
    "Window.location",
    "Location.pathname",
    "Location.search",
  ],
};

/** Members that outlive the document, reviewed per file with their exact count and the reason. */
const outlivingReviewed: Record<string, { count: number; reason: string }> = {
  "ListView.tsx Window.history": { count: 1, reason: "Reaches window.history for the replaceState below." },
  "ListView.tsx History.replaceState": {
    count: 1,
    reason: "Keeps the list's own state (search, filter, sort, open record) in the current entry's address; no row value; stamped per identity by kernel/historyGuard (client-carriers.txt).",
  },
  "ListView.tsx globalThis.navigator": { count: 1, reason: "Reaches navigator.clipboard for Copy, below." },
  "ListView.tsx Navigator.clipboard": { count: 1, reason: "Copy of the selected rows, only when the person presses Copy (a user's own act, like Ctrl+C)." },
  "ListView.tsx Clipboard.writeText": { count: 1, reason: "The same Copy: the rows the person selected and asked to copy, as tab-separated text." },
  "recordWidth.ts WindowLocalStorage.localStorage": { count: 3, reason: "Reaches localStorage for the record form's width below." },
  "recordWidth.ts Storage.getItem": { count: 1, reason: "Reads erp.lists.recordShare.<list key>: a whole number 30 to 75, parsed with Number and clamped; anything else is ignored." },
  "recordWidth.ts Storage.setItem": { count: 1, reason: "Writes that whole number (clampShare) under the list's key; never a row value; removed by the forgetter at sign-out." },
  "recordWidth.ts Storage.removeItem": { count: 1, reason: "Removes the list's width choice." },
};

/** Modules outside its folder the list kernel imports (code that runs with it), reviewed. */
const reviewedImports: Record<string, string> = {
  "react": "The UI library.",
  "react-dom": "flushSync, for focus that must follow a render at once.",
  "src/kernel/api.ts": "The one way to the server (same-origin fetch with the session cookie); keeps nothing.",
  "src/kernel/deviceState.ts": "identityScoped(): memory the forgetter empties when the identity ends.",
  "src/kernel/forms/leave.ts": "Asks before leaving a record with unsaved changes.",
  "src/kernel/router.tsx": "The in-app address of an open record (history, stamped by kernel/historyGuard).",
  "src/kernel/i18n.tsx": "English and Arabic words and formats.",
  "src/kernel/shortcuts.tsx": "Keyboard chords and palette commands.",
  "src/kernel/format.ts": "Decimal and date formatting helpers (pure).",
};

type Inventory = { uses: Map<string, number>; problems: string[]; imports: Set<string>; files: number };

function kernelSources(): string[] {
  return readdirSync(listDir)
    .filter((f) => /\.tsx?$/.test(f) && !/\.test\.tsx?$/.test(f))
    .map((f) => resolve(listDir, f));
}

let options: ts.CompilerOptions | undefined;
const parsedFiles = new Map<string, ts.SourceFile>();
function compilerOptions(): ts.CompilerOptions {
  if (!options) {
    const parsed = ts.getParsedCommandLineOfConfigFile(resolve(webRoot, "tsconfig.json"), {}, { ...ts.sys, onUnRecoverableConfigFileDiagnostic: () => {} });
    if (!parsed) throw new Error("web/tsconfig.json did not parse");
    options = { ...parsed.options, types: [], noEmit: true };
  }
  return options;
}

const isDomLib = (file: ts.SourceFile, program: ts.Program) =>
  program.isSourceFileDefaultLibrary(file) && /lib\.(dom|webworker|scripthost)/.test(file.fileName.split(/[\\/]/).pop() ?? "");

/** The list kernel's reach into the browser. `overrides` replaces or adds files (absolute path → text). */
function inventory(overrides: Record<string, string> = {}): Inventory {
  const opts = compilerOptions();
  const host = ts.createCompilerHost(opts, true);
  const getSourceFile = host.getSourceFile.bind(host);
  const fileExists = host.fileExists.bind(host);
  const readFile = host.readFile.bind(host);
  host.getSourceFile = (name, version, onError, create) => {
    if (name in overrides) return ts.createSourceFile(name, overrides[name]!, version, true, name.endsWith(".tsx") ? ts.ScriptKind.TSX : ts.ScriptKind.TS);
    // Parsed once for every inventory of the run (the libraries and React's types are most of it).
    let parsed = parsedFiles.get(name);
    if (!parsed) {
      parsed = getSourceFile(name, version, onError, create);
      if (parsed) parsedFiles.set(name, parsed);
    }
    return parsed;
  };
  host.fileExists = (name) => name in overrides || fileExists(name);
  host.readFile = (name) => overrides[name] ?? readFile(name);

  const roots = [...new Set([...kernelSources(), ...Object.keys(overrides).filter((f) => dirname(f) === listDir)])];
  const program = ts.createProgram(roots, opts, host);
  const checker = program.getTypeChecker();
  const uses = new Map<string, number>();
  const problems: string[] = [];
  const imports = new Set<string>();

  const domDeclared = (symbol: ts.Symbol | undefined) => {
    const declarations = symbol?.declarations ?? [];
    return declarations.length > 0 && declarations.every((d) => isDomLib(d.getSourceFile(), program));
  };
  const libDeclared = (symbol: ts.Symbol | undefined) => {
    const declarations = symbol?.declarations ?? [];
    return declarations.length > 0 && declarations.every((d) => program.isSourceFileDefaultLibrary(d.getSourceFile()));
  };
  /** The interface (or class or namespace) that declares a member; globalThis for a global. */
  const container = (symbol: ts.Symbol) => {
    for (let n: ts.Node | undefined = symbol.declarations?.[0]?.parent; n; n = n.parent) {
      if ((ts.isInterfaceDeclaration(n) || ts.isClassDeclaration(n) || ts.isModuleDeclaration(n)) && n.name) return n.name.text;
      if (ts.isTypeLiteralNode(n)) return checker.symbolToString(checker.getTypeAtLocation(n).getSymbol() ?? symbol);
    }
    return "globalThis";
  };
  const resolved = (node: ts.Node) => {
    let symbol = checker.getSymbolAtLocation(node);
    if (symbol && symbol.flags & ts.SymbolFlags.Alias) symbol = checker.getAliasedSymbol(symbol);
    return symbol;
  };
  const inType = (node: ts.Node): boolean => {
    for (let n: ts.Node | undefined = node.parent; n; n = n.parent) {
      if (ts.isTypeNode(n) || ts.isTypeAliasDeclaration(n) || ts.isInterfaceDeclaration(n) || ts.isHeritageClause(n)) return true;
      if (ts.isExpression(n) || ts.isStatement(n)) return false;
    }
    return false;
  };
  const assigned = (node: ts.Node): boolean => {
    let target: ts.Node = node;
    if (ts.isPropertyAccessExpression(node.parent) && node.parent.name === node) target = node.parent;
    else if (ts.isElementAccessExpression(node.parent) && node.parent.argumentExpression === node) target = node.parent;
    const parent = target.parent;
    return ts.isBinaryExpression(parent) && parent.left === target && parent.operatorToken.kind >= ts.SyntaxKind.FirstAssignment && parent.operatorToken.kind <= ts.SyntaxKind.LastAssignment;
  };
  const browserTyped = (expression: ts.Expression) => {
    if (ts.isIdentifier(expression) && expression.text === "globalThis") return true;
    const type = checker.getTypeAtLocation(expression);
    if (type.flags & ts.TypeFlags.Any) return true;
    return domDeclared(type.getSymbol()) || domDeclared(type.aliasSymbol) || domDeclared(resolved(expression));
  };

  for (const sf of program.getSourceFiles()) {
    if (dirname(sf.fileName) !== listDir || /\.test\.tsx?$/.test(sf.fileName) || !/\.tsx?$/.test(sf.fileName)) continue;
    const file = rel(sf.fileName);
    const text = sf.getFullText();
    for (const m of text.matchAll(/@ts-(ignore|expect-error|nocheck)/g)) problems.push(`${file}: '@ts-${m[1]}' hides code from the type checker this gate reads with`);
    const add = (key: string) => uses.set(`${file} ${key}`, (uses.get(`${file} ${key}`) ?? 0) + 1);
    const where = (node: ts.Node) => `${file}:${sf.getLineAndCharacterOfPosition(node.getStart(sf)).line + 1}`;

    const visit = (node: ts.Node) => {
      if (node.kind === ts.SyntaxKind.AnyKeyword) problems.push(`${where(node)}: 'any' turns the type checker off; the gate cannot see what such a value reaches`);
      if (ts.isCallExpression(node) && node.expression.kind === ts.SyntaxKind.ImportKeyword) problems.push(`${where(node)}: a dynamic import() loads code the gate does not read`);
      if ((ts.isImportDeclaration(node) || ts.isExportDeclaration(node)) && node.moduleSpecifier && ts.isStringLiteral(node.moduleSpecifier)) {
        const typeOnly = ts.isImportDeclaration(node) ? node.importClause?.isTypeOnly === true : node.isTypeOnly;
        const spec = node.moduleSpecifier.text;
        if (!typeOnly && !/\.css$/.test(spec)) {
          const target = ts.resolveModuleName(spec, sf.fileName, opts, host).resolvedModule;
          const path = target?.resolvedFileName;
          if (!path || target.isExternalLibraryImport) imports.add(spec);
          else if (dirname(path) !== listDir) imports.add(relative(webRoot, path).replace(/\\/g, "/"));
        }
      }
      if (ts.isElementAccessExpression(node) && !ts.isStringLiteralLike(node.argumentExpression) && !ts.isNumericLiteral(node.argumentExpression) && browserTyped(node.expression)) {
        problems.push(`${where(node)}: a computed member of a browser object (${node.getText(sf).slice(0, 60)}) cannot be reviewed; name the member`);
      }
      if (ts.isAsExpression(node) || ts.isTypeAssertionExpression(node)) {
        const to = node.type.kind;
        if ((to === ts.SyntaxKind.UnknownKeyword || to === ts.SyntaxKind.AnyKeyword) && browserTyped(node.expression)) {
          problems.push(`${where(node)}: casting a browser object to unknown hides what is reached through it`);
        }
      }
      const named = ts.isIdentifier(node) || (ts.isStringLiteralLike(node) && ts.isElementAccessExpression(node.parent) && node.parent.argumentExpression === node);
      if (named && !inType(node)) {
        if (ts.isIdentifier(node) && node.text === "globalThis") add("globalThis");
        const symbol = resolved(node);
        const parentName = symbol ? container(symbol) : "globalThis";
        if (symbol && domDeclared(symbol)) add(`${parentName}.${symbol.name}${assigned(node) ? "=" : ""}`);
        else if (symbol && libDeclared(symbol) && reflective.test(`${parentName}.${symbol.name}`)) add(`${parentName}.${symbol.name}`);
      }
      ts.forEachChild(node, visit);
    };
    visit(sf);
  }
  return { uses, problems, imports, files: roots.length };
}

/** What the gate refuses in an inventory: unreviewed members, counts that moved, stale reviews. */
function judge(found: Inventory): string[] {
  const problems = [...found.problems];
  const bound = new Set(Object.values(documentBound).flat());
  const seenOutliving = new Set<string>();
  for (const [use, count] of found.uses) {
    const member = use.slice(use.indexOf(" ") + 1);
    if (member === "globalThis") {
      problems.push(`${use}: globalThis reaches every browser store around the checker; name the global instead`);
      continue;
    }
    if (reflective.test(member)) {
      problems.push(`${use}: reflection reaches objects around the type checker`);
      continue;
    }
    const outliving = outlivesDocument.some((p) => p.test(member));
    if (outliving) {
      const review = outlivingReviewed[use];
      seenOutliving.add(use);
      if (!review) problems.push(`${use} (${count}x): this browser API keeps or hands on data beyond the document, so one person's rows can reach the next on the device. Keep the data in memory, or review the use in browserReach.test.ts with its count and reason.`);
      else if (review.count !== count) problems.push(`${use}: used ${count} times, reviewed for ${review.count}; a new use is a new decision`);
    } else if (!bound.has(member)) {
      problems.push(`${use} (${count}x): a browser API the list kernel did not use before; review it in browserReach.test.ts (documentBound if it dies with the document, else outlivingReviewed)`);
    }
  }
  const members = new Set([...found.uses.keys()].map((u) => u.slice(u.indexOf(" ") + 1)));
  for (const member of bound) if (!members.has(member)) problems.push(`browserReach.test.ts: documentBound '${member}' is no longer used; remove the stale review`);
  for (const use of Object.keys(outlivingReviewed)) if (!seenOutliving.has(use)) problems.push(`browserReach.test.ts: '${use}' is reviewed but no longer used; remove the stale review`);
  for (const spec of found.imports) if (!(spec in reviewedImports)) problems.push(`the list kernel now imports ${spec}: code that runs with every list; review it in browserReach.test.ts`);
  return problems;
}

/** A minimum from gauntlet/ratchet.json: gates may only grow (CLAUDE.md rule 9). */
function ratchetMinimum(key: string): number {
  const file = process.env.ERP_RATCHET ?? resolve(webRoot, "../gauntlet/ratchet.json");
  expect(existsSync(file), `ratchet file ${file}`).toBe(true);
  const value = (JSON.parse(readFileSync(file, "utf8")) as { minimums: Record<string, number> }).minimums[key];
  expect(value, `ratchet minimum ${key}`).toBeTypeOf("number");
  return value!;
}

const useListRowsPath = resolve(listDir, "useListRows.ts");
const ListViewPath = resolve(listDir, "ListView.tsx");
const product = { rows: readFileSync(useListRowsPath, "utf8"), view: readFileSync(ListViewPath, "utf8") };

/** Inserts `code` into useListRows.ts after its imports. */
const intoRows = (code: string) => ({ [useListRowsPath]: product.rows.replace(/(import type \{ ListGroup[^\n]*\n)/, `$1${code}\n`) });

describe("list kernel: reach into the browser (G1, device stores)", () => {
  const actual = inventory();
  if (process.env.ERP_DUMP_REACH) console.log([...actual.uses].map(([k, v]) => `${v} ${k}`).sort().join("\n"));

  it("every browser API the list kernel reaches is reviewed; those that outlive the document by file and count", () => {
    expect(judge(actual), judge(actual).join("\n")).toEqual([]);
    const members = new Set([...actual.uses.keys()].map((u) => u.slice(u.indexOf(" ") + 1)));
    expect(actual.files).toBeGreaterThanOrEqual(ratchetMinimum("g1.listKernelSourcesRead"));
    expect(members.size).toBeGreaterThanOrEqual(ratchetMinimum("g1.listKernelBrowserMembersReviewed"));
  });

  it("is not blind: it sees the list kernel's known uses through aliases (window.localStorage, navigator.clipboard)", () => {
    expect(actual.uses.get("recordWidth.ts Storage.setItem")).toBe(1);
    expect(actual.uses.get("ListView.tsx Clipboard.writeText")).toBe(1);
    expect(actual.uses.get("ListView.tsx History.replaceState")).toBe(1);
    expect(actual.imports.has("src/kernel/api.ts")).toBe(true);
  });

  // Self-test: the critic's plant L14 and its variants, each in the list kernel's own sources.
  const l14 = `
async function remember(key: string, page: ListPage): Promise<void> {
  try {
    const dir = await navigator.storage.getDirectory();
    const handle = await dir.getFileHandle("list-" + encodeURIComponent(key), { create: true });
    const writable = await handle.createWritable();
    await writable.write(JSON.stringify(page));
    await writable.close();
  } catch {}
}
export const keep = remember;`;
  const plants: Record<string, { files: Record<string, string>; expect: RegExp }> = {
    "L14: a query's first page in the origin private file system": { files: intoRows(l14), expect: /useListRows\.ts (globalThis\.navigator|Navigator\.storage|StorageManager\.getDirectory|FileSystem\w+\.\w+)/ },
    "the same through an alias of navigator.storage": {
      files: intoRows(`const store = window.navigator.storage;\nexport async function keep(t: string) { const d = await store.getDirectory(); const f = await d.getFileHandle("x", { create: true }); const w = await f.createWritable(); await w.write(t); }`),
      expect: /useListRows\.ts StorageManager\.getDirectory/,
    },
    "a quoted member: window['localStorage']": { files: intoRows(`export const keep = (t: string) => window["localStorage"].setItem("rows", t);`), expect: /useListRows\.ts (globalThis\.localStorage|Storage\.setItem)/ },
    "a computed member: window[name]": { files: intoRows(`const which = "local" + "Storage";\nexport const keep = (t: string) => (window[which as keyof Window] as Storage).setItem("rows", t);`), expect: /computed member of a browser object/ },
    "an any cast of window": { files: intoRows(`export const keep = (t: string) => (window as any).sessionStorage.setItem("rows", t);`), expect: /'any' turns the type checker off/ },
    "an unknown cast of navigator": { files: intoRows(`type S = { storage: { persist(): void } };\nexport const keep = () => (navigator as unknown as S).storage.persist();`), expect: /casting a browser object to unknown/ },
    "IndexedDB": { files: intoRows(`export const keep = () => indexedDB.open("rows");`), expect: /useListRows\.ts (globalThis\.indexedDB|IDBFactory\.open)/ },
    "Cache Storage": { files: intoRows(`export const keep = (r: Response) => caches.open("rows").then((c) => c.put("/rows", r));`), expect: /useListRows\.ts (globalThis\.caches|CacheStorage\.open|Cache\.put)/ },
    "a cookie": { files: intoRows(`export const keep = (t: string) => { document.cookie = "rows=" + t; };`), expect: /useListRows\.ts Document\.cookie=/ },
    "window.name": { files: intoRows(`export const keep = (t: string) => { window.name = t; };`), expect: /useListRows\.ts (Window\.name=|globalThis\.name=)/ },
    "a broadcast channel": { files: intoRows(`export const keep = (t: string) => new BroadcastChannel("rows").postMessage(t);`), expect: /useListRows\.ts (globalThis\.BroadcastChannel|BroadcastChannel\.postMessage)/ },
    "sessionStorage": { files: intoRows(`export const keep = (t: string) => sessionStorage.setItem("rows", t);`), expect: /useListRows\.ts (globalThis\.sessionStorage|Storage\.setItem)/ },
    "a second localStorage write in a reviewed file": {
      files: { [resolve(listDir, "recordWidth.ts")]: readFileSync(resolve(listDir, "recordWidth.ts"), "utf8") + `\nexport const keep = (t: string) => window.localStorage.setItem("erp.lists.rows", t);\n` },
      expect: /recordWidth\.ts (Storage\.setItem: used 2 times|globalThis\.localStorage: used 4 times)/,
    },
    "a history entry carrying rows": { files: intoRows(`export const keep = (t: string) => history.pushState({ rows: t }, "");`), expect: /useListRows\.ts (globalThis\.history|History\.pushState)/ },
    "a service worker": { files: intoRows(`export const keep = () => navigator.serviceWorker.register("/sw.js");`), expect: /useListRows\.ts (Navigator\.serviceWorker|ServiceWorkerContainer\.register)/ },
    "globalThis as a root": { files: intoRows(`export const keep = (t: string) => globalThis.localStorage.setItem("rows", t);`), expect: /useListRows\.ts globalThis: globalThis reaches/ },
    "reflection": { files: intoRows(`export const keep = (t: string) => (Reflect.get(window, "localStorage") as Storage).setItem("rows", t);`), expect: /reflection reaches objects/ },
    "a dynamic import": { files: intoRows(`export const keep = () => import("../deviceState");`), expect: /dynamic import\(\)/ },
    "ts-ignore": { files: intoRows(`// @ts-ignore\nexport const keep = 1;`), expect: /'@ts-ignore' hides code/ },
    "a new file of the kernel": {
      files: { [resolve(listDir, "pageCache.ts")]: `export async function keep(t: string) { const d = await navigator.storage.getDirectory(); await (await (await d.getFileHandle("p", { create: true })).createWritable()).write(t); }\n` },
      expect: /pageCache\.ts (globalThis\.navigator|StorageManager\.getDirectory)/,
    },
    "a new module imported from outside the folder": {
      files: { ...intoRows(`import { keep as keepPage } from "../pageCache";\nexport const keep = keepPage;`), [resolve(listDir, "../pageCache.ts")]: `export function keep(t: string) { return t; }\n` },
      expect: /imports src\/kernel\/pageCache\.ts/,
    },
  };

  for (const [name, plant] of Object.entries(plants)) {
    it(`self-test: refuses ${name}`, () => {
      const problems = judge(inventory(plant.files));
      expect(problems.some((p) => plant.expect.test(p)), problems.join("\n") || "(the gate found nothing)").toBe(true);
    });
  }

  it("self-test: the plants are counted (the ratchet keeps them)", () => {
    expect(Object.keys(plants).length).toBeGreaterThanOrEqual(ratchetMinimum("g1.listKernelReachPlantsCaught"));
  });

  it("self-test: the product's own sources pass unchanged when fed through the overrides", () => {
    expect(judge(inventory({ [useListRowsPath]: product.rows, [ListViewPath]: product.view }))).toEqual([]);
  });
});

import { existsSync, readFileSync } from "node:fs";
import { resolve } from "node:path";
import { act } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { extensions, type ModuleAction } from "../../kernel/extensions";
import { translate } from "../../kernel/i18n";
import { matchRoute, recordInAddress, routes } from "../../kernel/router";
import { ShortcutProvider, useCommand } from "../../kernel/shortcuts";
import { type Call, readerMayWrite, settleUntilQuiet } from "../../test/keySweep";
import { listDefinitions } from "../../test/lists";
import { mockFetch, render, settle, setInput, type Rendered } from "../../test/render";
import { App } from "./App";
import { PendingCommand } from "./AppShell";
import { offeredActions } from "./paletteActions";

// G2 in the command palette (critic p04 round 7: the palette could open screens and records but
// start no module action; the fix lets modules contribute actions and lists the screen's own
// commands, and this file holds the line that comes with it: an action a user may not run is never
// offered). Module actions are offered only with their permission and their screen's; started from
// anywhere, each opens its screen and runs that screen's own command. A user who may only read
// runs every entry the palette offers on every screen, accepting whatever each opens, and no write
// leaves. scripts/palette-plant-self-test.mjs plants faults that this file must catch.

let view: Rendered | undefined;
let calls: Call[] = [];

beforeEach(() => {
  localStorage.clear();
  window.print = () => undefined;
});
afterEach(() => {
  view?.unmount();
  view = undefined;
  vi.useRealTimers();
});

const en = (key: string) => translate("en", key);

/** Every permission the identity and tenancy screens and actions know. */
const every = [
  "identity.profile.update",
  "identity.users.read",
  "identity.users.create",
  "identity.users.update",
  "identity.users.delete",
  "identity.users.resetPassword",
  "identity.roles.read",
  "identity.roles.create",
  "identity.roles.update",
  "identity.roles.delete",
  "identity.signIns.read",
  "tenancy.companies.read",
  "tenancy.companies.create",
  "tenancy.companies.update",
  "tenancy.branches.read",
  "tenancy.branches.create",
  "tenancy.branches.update",
  "tenancy.access.read",
  "tenancy.tenant.read",
  "tenancy.workplace.read",
  "tenancy.workplace.switch",
  "reports.catalog.read",
];

/** A user who may read every screen and change nothing but their own settings. */
const reader = ["identity.profile.update", "identity.users.read", "identity.roles.read", "identity.signIns.read", "tenancy.companies.read", "tenancy.branches.read", "tenancy.workplace.read"];

const session = (permissions: string[], language = "en") => ({
  authenticated: true,
  user: { id: "me", email: "mariam@alnoor.example", displayName: "Mariam Al Mansoori", language, numerals: "latn" },
  tenant: { id: "t1", code: "alnoor", nameEn: "Al Noor Trading LLC", nameAr: "شركة النور للتجارة ذ.م.م" },
  permissions,
  menu: routes
    .filter((r) => r.path !== "/" && (!r.permission || permissions.includes(r.permission)))
    .map((r) => ({ key: r.path.slice(1).replace("/", "."), labelKey: r.titleKey, path: r.path, group: "settings" })),
  expiresAt: null,
});

const column = (key: string) => ({ key, labelKey: "tenancy.company.code", type: "text", sortable: true, filterable: true, groupable: false, aggregate: false, hidden: false, choices: [], operators: ["eq", "contains"] });
const definitions: Record<string, unknown> = {
  "identity.users": { ...listDefinitions["identity.users"], printable: true },
  "identity.roles": { ...listDefinitions["identity.roles"], printable: true },
  "tenancy.companies": { key: "tenancy.companies", labelKey: "tenancy.companies.title", endpoint: "/api/tenancy/companies", columns: [column("code")], searchFields: ["code"], defaultSort: "code", presets: [], canShare: false, maxTake: 200, printable: true },
  "tenancy.branches": { key: "tenancy.branches", labelKey: "tenancy.branches.title", endpoint: "/api/tenancy/branches", columns: [column("code")], searchFields: ["code"], defaultSort: "code", presets: [], canShare: false, maxTake: 200, printable: true },
};

const clerk = { id: "r-clerk", nameEn: "Contacts clerk", nameAr: "كاتب جهات الاتصال", permissions: ["identity.users.read"], isSystem: false, userCount: 1, version: 1 };
const user = {
  id: "u1", email: "clerk@alnoor.example", displayName: "Salem Clerk", language: "en", isActive: true, roleIds: ["r-clerk"],
  lastSignInAt: "2026-10-03T08:00:00Z", createdAt: "2026-10-01T00:00:00Z", version: 4, pendingSetup: false, companyRoles: [],
};
const signIns = {
  items: [{ id: "s1", occurredAt: "2026-10-03T08:00:00Z", outcome: "succeeded", ipAddress: "10.0.0.1", userAgent: "Firefox", sessionActive: true }],
  total: 1,
  paused: [{ source: "10.0.0.9", until: "2026-10-03T09:00:00Z", failures: 5 }],
};
const company = {
  id: "c9", code: "AN-AJM", legalNameEn: "Al Noor Ajman LLC", legalNameAr: "النور عجمان ذ.م.م", tradeLicenceNumber: null, tradeLicenceAuthority: null,
  taxRegistrationNumber: null, baseCurrency: "AED", fiscalYearStartMonth: 1, fiscalYearStartDay: 1, addressLine1: null, addressLine2: null, city: null,
  emirate: "ajman", poBox: null, country: "AE", addressAr: null, phone: null, email: null, website: null, hasLogo: false, logoHash: null, isActive: true,
  branchCount: 1, createdAt: "2026-10-03T00:00:00Z", updatedAt: "2026-10-03T00:00:00Z", version: 1, everyBranch: true,
  // As the server answers every company row: whether the user works in every company of the workspace.
  everyCompany: true,
};
const branch = {
  id: "b1", companyId: "c9", companyCode: "AN-AJM", code: "AJM-HQ", nameEn: "Ajman head office", nameAr: "المكتب الرئيسي عجمان", addressLine1: null, addressLine2: null,
  city: null, emirate: "ajman", poBox: null, country: "AE", addressAr: null, phone: null, email: null, isActive: true, version: 3,
};
const workplace = {
  companyId: "c9", branchId: "b1",
  companies: [{ id: "c9", code: "AN-AJM", legalNameEn: "Al Noor Ajman LLC", legalNameAr: "النور عجمان ذ.م.م", baseCurrency: "AED", branches: [{ id: "b1", code: "AJM-HQ", nameEn: "Ajman head office", nameAr: "المكتب الرئيسي عجمان" }] }],
};

/** The app at `path` for a user with `permissions`; writes are answered as a server would answer
 * them when allowed (the gate judges what is sent, not what the server would refuse). */
async function open(
  path: string,
  permissions: string[],
  options: { language?: "en" | "ar"; companies?: (typeof company)[]; neverSignedIn?: boolean; slowLists?: boolean; bare?: boolean } = {},
) {
  view?.unmount();
  window.history.replaceState(null, "", path);
  const companies = options.companies ?? [company];
  // bare: the user holds no role and the role grants nothing, so no record is beyond anyone.
  const base = options.bare ? { ...user, roleIds: [] } : user;
  const shown = options.neverSignedIn ? { ...base, lastSignInAt: null } : base;
  const role = options.bare ? { ...clerk, permissions: [] } : clerk;
  calls = mockFetch((method, url, body) => {
    const p = new URL(url, "http://localhost").pathname;
    if (url === "/api/auth/session") return { status: 200, body: session(permissions, options.language) };
    const list = /^\/api\/lists\/([^/]+)\/(definition|views)$/.exec(p);
    if (list) {
      const reply = list[2] === "views" ? { status: 200, body: { items: [] } } : { status: 200, body: definitions[list[1]!] };
      // A list that loads after its screen has offered its commands (a busy server).
      return options.slowLists ? new Promise<typeof reply>((r) => setTimeout(() => r(reply), 120)) : reply;
    }
    if (method !== "GET") return { status: 200, body: { ...(body as object), id: "x1", version: 1, sessionsEnded: 0 } };
    if (p === "/api/identity/users") return { status: 200, body: { items: [shown], total: 1, next: null } };
    if (p === "/api/identity/users/u1") return { status: 200, body: shown };
    if (p === "/api/identity/users/u1/sign-ins") return { status: 200, body: signIns };
    if (p === "/api/identity/users/u1/passkeys") return { status: 200, body: [] };
    if (p === "/api/identity/users/u1/access") return { status: 200, body: { userId: "u1", roles: [], permissions: [] } };
    if (p === "/api/identity/roles") return { status: 200, body: { items: [role], total: 1, next: null } };
    if (p === "/api/identity/roles/r-clerk") return { status: 200, body: role };
    if (p === "/api/identity/permissions") return { status: 200, body: [] };
    if (p === "/api/identity/companies") return { status: 200, body: [] };
    if (p === "/api/tenancy/companies") return { status: 200, body: { items: companies, total: companies.length, next: null } };
    if (p === "/api/tenancy/companies/c9") return { status: 200, body: companies[0] };
    if (p === "/api/tenancy/branches") return { status: 200, body: { items: [branch], total: 1, next: null } };
    if (p === "/api/tenancy/branches/b1") return { status: 200, body: branch };
    if (p === "/api/tenancy/workplace") return { status: 200, body: workplace };
    return { status: 404, body: {} };
  });
  view = await render(<App language={options.language ?? "en"} />);
  await settleUntilQuiet(() => calls, () => document.querySelector("main") !== null);
}

function press(init: KeyboardEventInit & { code: string }, target: EventTarget = document.activeElement ?? document.body) {
  act(() => {
    target.dispatchEvent(new KeyboardEvent("keydown", { bubbles: true, cancelable: true, ...init }));
  });
}

const palette = () => document.querySelector<HTMLElement>('[role="dialog"].palette');

async function openPalette(query = ""): Promise<HTMLInputElement> {
  press({ code: "KeyK", key: "k", ctrlKey: true });
  await settle();
  const input = palette()!.querySelector<HTMLInputElement>('input[role="combobox"]')!;
  if (query) setInput(input, query);
  // Availability checks and record sources answer (after the palette's short debounce).
  await act(async () => {
    await new Promise((r) => setTimeout(r, 150));
  });
  await settleUntilQuiet(() => calls);
  return input;
}

type Offered = { group: string; title: string; option: HTMLElement };
const offered = (): Offered[] =>
  [...document.querySelectorAll<HTMLElement>('.palette [role="group"]')].flatMap((group) =>
    [...group.querySelectorAll<HTMLElement>('[role="option"]')].map((option) => ({
      group: group.getAttribute("aria-label") ?? "",
      title: option.querySelector(".palette-option-title")?.textContent ?? "",
      option,
    })),
  );

const done = () => !document.querySelector(".app[data-pending-action]");

/** Chooses the list's first row from the keyboard, as asked by an action that needs a record:
 * the grid takes the focus, Enter opens its first row. */
async function chooseFirstRow() {
  await settleUntilQuiet(() => calls, () => document.querySelector('main [role="grid"] [role="row"][aria-rowindex="2"]') !== null);
  const grid = document.querySelector<HTMLElement>('main [role="grid"]')!;
  expect(grid, "the list's grid").not.toBeNull();
  act(() => grid.focus());
  press({ code: "Enter", key: "Enter" }, grid);
  await settleUntilQuiet(() => calls);
}

/** A minimum from gauntlet/ratchet.json: gates may only grow (CLAUDE.md rule 9). */
function ratchetMinimum(key: string): number {
  const file = process.env.ERP_RATCHET ?? resolve(import.meta.dirname, "../../../../gauntlet/ratchet.json");
  expect(existsSync(file), `ratchet file ${file}`).toBe(true);
  const value = (JSON.parse(readFileSync(file, "utf8")) as { minimums: Record<string, number> }).minimums[key];
  expect(value, `ratchet minimum ${key}`).toBeTypeOf("number");
  return value!;
}

describe("module actions in the command palette", () => {
  it("every action is declared whole: a permission, one of its module's screens, its name and words in English and Arabic", () => {
    const actions = extensions.actions;
    expect(actions.length).toBeGreaterThanOrEqual(ratchetMinimum("g2.paletteActionsChecked"));
    for (const action of actions) {
      expect(action.permission, action.key).toMatch(/^[a-z]+\.[a-zA-Z]+\.[a-zA-Z]+$/);
      const route = matchRoute(action.path);
      expect(route?.path, `${action.key}: ${action.path} is a screen`).toBe(action.path);
      expect(route?.permission, `${action.key}: its screen names a permission`).toBeTruthy();
      for (const key of [action.labelKey, action.keywordsKey].filter((k): k is string => Boolean(k))) {
        expect(translate("en", key), `${action.key}: ${key} in English`).not.toBe(key);
        expect(translate("ar", key), `${action.key}: ${key} in Arabic`).not.toBe(key);
        expect(translate("ar", key), `${action.key}: ${key} translated`).not.toBe(translate("en", key));
      }
    }
  });

  it("offers an action only with its permission and its screen's: the filter, for every action", () => {
    for (const action of extensions.actions) {
      const screen = matchRoute(action.path)!.permission!;
      const without = (missing: string) => (p: string) => every.includes(p) && p !== missing;
      expect(offeredActions([action], (p) => every.includes(p)).map((a) => a.key), action.key).toEqual([action.key]);
      expect(offeredActions([action], without(action.permission)), `${action.key} without ${action.permission}`).toEqual([]);
      expect(offeredActions([action], without(screen)), `${action.key} without ${screen}`).toEqual([]);
      // Each further permission the action's record commands need (critic p04 round 9, plant P4:
      // with the filter's check of requires removed, every test still passed).
      for (const further of action.requires ?? []) {
        expect(offeredActions([action], without(further)), `${action.key} without ${further}`).toEqual([]);
      }
    }
    // The filter is judged on actions that name further permissions.
    expect(extensions.actions.filter((a) => (a.requires?.length ?? 0) > 0).length).toBeGreaterThanOrEqual(ratchetMinimum("g2.paletteActionsWithRequires"));
  });

  it("Allow sign-in again is not offered to a user who may change users but not see the sign-in history, in English or Arabic (critic p04 round 9, plant P4)", async () => {
    const unblock = extensions.actions.find((a) => a.key === "identity.users.unblock")!;
    expect(unblock.requires).toContain("identity.signIns.read");
    const changer = ["identity.profile.update", "identity.users.read", "identity.users.update"];
    expect(offeredActions(extensions.actions, (p) => changer.includes(p)).map((a) => a.key)).toEqual(["identity.users.print"]);
    expect(offeredActions(extensions.actions, (p) => [...changer, "identity.signIns.read"].includes(p)).map((a) => a.key).sort()).toEqual(
      ["identity.users.history", "identity.users.print", "identity.users.unblock"],
    );
    for (const language of ["en", "ar"] as const) {
      const title = translate(language, unblock.labelKey);
      await open("/", changer, { language });
      await openPalette(title);
      expect(offered().filter((o) => o.title === title).map((o) => o.title), language).toEqual([]);
      // The control: the same user with the sign-in history is offered it.
      await open("/", [...changer, "identity.signIns.read"], { language });
      await openPalette(title);
      expect(offered().filter((o) => o.title === title).length, `${language} with identity.signIns.read`).toBe(1);
    }
  });

  for (const action of extensions.actions) {
    it(`${action.key}: shown in the palette only to a user whose roles grant ${action.permission} and its screen`, async () => {
      const title = en(action.labelKey);
      const screen = matchRoute(action.path)!.permission!;
      for (const [permissions, expected] of [
        [every, true],
        [every.filter((p) => p !== action.permission), false],
        [every.filter((p) => p !== screen), false],
      ] as const) {
        await open("/", permissions);
        await openPalette(title);
        const found = offered().filter((o) => o.title === title);
        expect(found.length, `${action.key} for ${permissions.length} permissions`).toBe(expected ? 1 : 0);
      }
    });
  }

  // Actions that act on a new record or the whole list; those that ask for a record first (pick)
  // are judged below, each with the record chosen.
  for (const action of extensions.actions.filter((a) => !a.pick)) {
    it(`${action.key}: started from home, it opens ${action.path} and runs the screen's own command there`, async () => {
      await open("/", every);
      await openPalette(en(action.labelKey));
      const entry = offered().find((o) => o.title === en(action.labelKey));
      expect(entry, "offered").toBeDefined();
      act(() => entry!.option.click());
      await settleUntilQuiet(() => calls, done);
      expect(done(), "the screen offered the command and it ran").toBe(true);
      expect(window.location.pathname.startsWith(action.path)).toBe(true);
      expect(document.querySelector(".status-notice")).toBeNull();
      if (action.command === "lists.print") expect(document.querySelector('[role="menu"]'), "the print or export menu is open").not.toBeNull();
      else expect(recordInAddress(), "a new record is open").toBe("new");
    });
  }

  it("every action that asks for a record says what to choose, in English and Arabic, and runs no command before", async () => {
    const picks = extensions.actions.filter((a) => a.pick);
    expect(picks.map((a) => a.key)).toEqual(expect.arrayContaining(["identity.roles.copy", "identity.users.history", "identity.users.unblock"]));
    for (const action of picks) {
      for (const language of ["en", "ar"] as const) {
        expect(translate(language, action.pick!), `${action.key} ${language}`).not.toBe(action.pick);
      }
      await open("/", every);
      await openPalette(en(action.labelKey));
      const before = calls.length;
      act(() => offered().find((o) => o.title === en(action.labelKey))!.option.click());
      await settleUntilQuiet(() => calls);
      expect(window.location.pathname, action.key).toBe(action.path);
      expect(document.querySelector("[data-pick-prompt]")?.textContent, action.key).toBe(en(action.pick!));
      expect(done(), `${action.key} waits for a record`).toBe(false);
      expect(calls.slice(before).filter((c) => c.method !== "GET"), action.key).toEqual([]);
      expect(recordInAddress(), action.key).toBeNull();
    }
  });

  // Critic p03 round 10 (plant U1): without its requires, Allow sign-in again was offered to a user
  // who may change users but not see the sign-in history, and opened a tab they do not have; no
  // test noticed. An action that asks for a record runs a command of that record (and maybe a
  // second one), which the record offers only under its own conditions: for each permission of the
  // action's module, a user holding everything but it is either not offered the action, or the
  // action runs to its end for them (the record chosen offers every command it runs). The record is
  // one no permission check of its own refuses (a user holding no role, a role granting nothing).
  it("an action that asks for a record runs to its end for every user it is offered to: its requires name what the record's commands need", async () => {
    const problems: string[] = [];
    let runs = 0;
    for (const action of extensions.actions.filter((a) => a.pick)) {
      const module = action.permission.split(".")[0]!;
      const screen = matchRoute(action.path)!.permission!;
      for (const missing of every.filter((p) => p.startsWith(`${module}.`) && p !== action.permission && p !== screen)) {
        const permissions = every.filter((p) => p !== missing);
        // Not offered by the filter: nothing to run.
        if (offeredActions([action], (p) => permissions.includes(p)).length === 0) continue;
        await open("/", permissions, { bare: true });
        await openPalette(en(action.labelKey));
        const entry = offered().find((o) => o.title === en(action.labelKey) && o.group === en("shell.palette.group.actions"));
        if (!entry) continue;
        act(() => entry.option.click());
        await settleUntilQuiet(() => calls, done);
        if (document.querySelector("[data-pick-prompt]")) {
          await chooseFirstRow();
          await settleUntilQuiet(() => calls, done);
        }
        runs++;
        if (!done()) problems.push(`${action.key} without ${missing}: offered, but the record chosen does not offer ${[action.command, action.then].filter(Boolean).join(" then ")}`);
      }
    }
    expect(problems).toEqual([]);
    console.log(`record actions run without one permission of their module: ${runs}`);
    expect(runs).toBeGreaterThanOrEqual(ratchetMinimum("g2.paletteRequiresRuns"));
  }, 60_000);

  it("Copy a role from home: the roles list asks for the role, and the role chosen opens its copy", async () => {
    await open("/", every);
    await openPalette("copy role");
    expect(offered()[0]?.title).toBe(en("identity.action.copyRole"));
    press({ code: "Enter", key: "Enter" });
    await settleUntilQuiet(() => calls);
    expect(window.location.pathname).toBe("/identity/roles");
    await chooseFirstRow();
    expect(done(), "the copy ran").toBe(true);
    expect(document.querySelector("main")!.textContent).toContain(en("identity.roles.copyTitle").split("{")[0]!.trim());
    expect(document.querySelector("[data-pick-prompt]")).toBeNull();
  });

  it("Show the sign-in history from home, in Arabic: the user chosen opens on the history tab", async () => {
    await open("/", every, { language: "ar" });
    await openPalette("سجل تسجيل الدخول");
    expect(offered()[0]?.title).toBe(translate("ar", "identity.action.history"));
    press({ code: "Enter", key: "Enter" });
    await settleUntilQuiet(() => calls);
    expect(document.querySelector("[data-pick-prompt]")?.textContent).toBe(translate("ar", "identity.action.historyPick"));
    await chooseFirstRow();
    expect(done()).toBe(true);
    expect(document.querySelector('[role="tab"][aria-selected="true"]')?.textContent).toBe(translate("ar", "identity.tab.history"));
  });

  it("Allow a user to sign in again from home: the history opens and the user chosen is allowed again", async () => {
    await open("/", every);
    await openPalette("unblock");
    expect(offered()[0]?.title).toBe(en("identity.action.unblock"));
    press({ code: "Enter", key: "Enter" });
    await settleUntilQuiet(() => calls);
    const before = calls.length;
    await chooseFirstRow();
    await settleUntilQuiet(() => calls, done);
    expect(done()).toBe(true);
    expect(calls.slice(before).some((c) => c.method === "POST" && /^\/api\/identity\/users\/[^/]+\/unblock$/.test(c.url))).toBe(true);
    expect(document.querySelector(".status-notice")).toBeNull();
  });

  it("an action that asks for a record is dropped, without a word, when the user leaves its screen", async () => {
    await open("/", every);
    await openPalette("copy role");
    press({ code: "Enter", key: "Enter" });
    await settleUntilQuiet(() => calls);
    expect(done()).toBe(false);
    press({ code: "KeyH", key: "h", altKey: true });
    await settleUntilQuiet(() => calls);
    expect(window.location.pathname).toBe("/");
    expect(done()).toBe(true);
    expect(document.querySelector(".status-notice")).toBeNull();
  });

  it("New user from home keeps its new record open when the list arrives after the command ran", async () => {
    await open("/", every, { slowLists: true });
    await openPalette(en("identity.users.new"));
    act(() => offered().find((o) => o.title === en("identity.users.new"))!.option.click());
    await act(async () => {
      await new Promise((r) => setTimeout(r, 300));
    });
    await settleUntilQuiet(() => calls, done);
    expect(window.location.pathname).toBe("/identity/users/new");
    expect(document.querySelector('input[name="email"]')).not.toBeNull();
  });

  it("a user who may add a branch but to no company (only some branches of it) is not offered New branch", async () => {
    const partial = { ...company, everyBranch: false };
    await open("/", every, { companies: [partial] });
    await openPalette(en("tenancy.branch.new"));
    expect(offered().map((o) => o.title)).not.toContain(en("tenancy.branch.new"));
    await open("/", every);
    await openPalette(en("tenancy.branch.new"));
    expect(offered().map((o) => o.title)).toContain(en("tenancy.branch.new"));
  });

  it("a user who may create a company but works in only some companies, or in none, is not offered New company (critic p02 round 9)", async () => {
    for (const companies of [[{ ...company, everyCompany: false }], []]) {
      await open("/", every, { companies });
      await openPalette(en("tenancy.company.new"));
      expect(offered().map((o) => o.title), `New company with ${companies.length} company rows`).not.toContain(en("tenancy.company.new"));
    }
    await open("/", every);
    await openPalette(en("tenancy.company.new"));
    expect(offered().map((o) => o.title)).toContain(en("tenancy.company.new"));
  });

  it("is found by its words in either language, on an Arabic screen too", async () => {
    await open("/", every, { language: "ar" });
    await openPalette("new user");
    expect(offered()[0]?.title).toBe(translate("ar", "identity.users.new"));
    press({ code: "Escape", key: "Escape" });
    await settle();
    await openPalette("دعوة");
    expect(offered()[0]?.title).toBe(translate("ar", "identity.users.new"));
    press({ code: "Escape", key: "Escape" });
    await settle();
    await openPalette("export branches");
    expect(offered()[0]?.title).toBe(translate("ar", "tenancy.action.printBranches"));
  });
});

describe("the commands of the screen on show", () => {
  it("come first, once each: on the users screen New user, the list's own controls and Print or export", async () => {
    await open("/identity/users", every);
    await openPalette();
    const entries = offered();
    expect(entries[0]?.group).toBe(en("shell.palette.group.here"));
    const here = entries.filter((e) => e.group === en("shell.palette.group.here")).map((e) => e.title);
    for (const title of [en("identity.users.new"), en("lists.shortcut.print"), en("lists.command.search"), en("lists.command.columns"), en("lists.command.views")]) expect(here).toContain(title);
    // Offered once: the module's own New user is the screen's, not a second entry under Actions.
    expect(entries.filter((e) => e.title === en("identity.users.new"))).toHaveLength(1);
    press({ code: "Escape", key: "Escape" });
    await settle();
    const input = await openPalette("new");
    expect(offered()[0]).toMatchObject({ group: en("shell.palette.group.here"), title: en("identity.users.new") });
    press({ code: "Enter", key: "Enter" }, input);
    await settleUntilQuiet(() => calls);
    expect(recordInAddress()).toBe("new");
    // The new user's first field has the focus, as after Alt+N.
    expect(document.activeElement?.getAttribute("name")).toBe("email");
  });

  it("are found by the words of the module action they stand for: \"print compan\" on the companies screen (critic p02 round 10)", async () => {
    await open("/tenancy/companies", every);
    await openPalette("print compan");
    expect(offered()[0]).toMatchObject({ group: en("shell.palette.group.here"), title: en("lists.shortcut.print") });
    // Still offered once: the module's own entry is not listed again under Actions.
    expect(offered().filter((o) => o.title === en("tenancy.action.printCompanies"))).toHaveLength(0);
    press({ code: "Escape", key: "Escape" });
    await settle();
    // And on another screen the module's entry itself is found by the same words.
    await open("/identity/users", every);
    await openPalette("print compan");
    expect(offered()[0]?.title).toBe(en("tenancy.action.printCompanies"));
  });

  it("follow the open record: a role's Copy, a user's sign-in history and Allow sign-in again", async () => {
    await open("/identity/roles?open=r-clerk", every);
    await openPalette("copy");
    expect(offered()[0]).toMatchObject({ group: en("shell.palette.group.here"), title: en("identity.roles.copy") });
    press({ code: "Enter", key: "Enter" });
    await settleUntilQuiet(() => calls);
    expect(document.querySelector("main")!.textContent).toContain(en("identity.roles.copyTitle").split("{")[0]!.trim());
    await open("/identity/users?open=u1", every);
    await openPalette("history");
    const history = offered().find((o) => o.title.includes(en("identity.tab.history")));
    expect(history, "the sign-in history tab").toBeDefined();
    act(() => history!.option.click());
    await settleUntilQuiet(() => calls);
    expect(document.querySelector('[role="tab"][aria-selected="true"]')?.textContent).toBe(en("identity.tab.history"));
    await openPalette("unblock");
    const unblock = offered().find((o) => o.title === en("identity.history.unblock"));
    expect(unblock, "Allow sign-in again").toBeDefined();
    const before = calls.length;
    act(() => unblock!.option.click());
    await settleUntilQuiet(() => calls);
    expect(calls.slice(before).some((c) => c.method === "POST" && c.url === "/api/identity/users/u1/unblock")).toBe(true);
  });
});

describe("a record's commands and its buttons", () => {
  it("the palette offers a user's account actions exactly when the record shows their buttons", async () => {
    const account = [en("identity.reset.open"), en("identity.sessions.revoke"), en("identity.users.delete")];
    const seen = new Set<string>();
    for (const extra of [["identity.users.update"], ["identity.users.resetPassword"], ["identity.users.delete"], ["identity.users.update", "identity.users.delete", "identity.users.resetPassword"]]) {
      await open("/identity/users?open=u1", [...reader, ...extra], { neverSignedIn: true });
      const buttons = [...document.querySelectorAll<HTMLButtonElement>(".id-account .id-actions button")].map((b) => b.textContent ?? "");
      await openPalette();
      const here = offered().filter((o) => o.group === en("shell.palette.group.here")).map((o) => o.title);
      for (const title of account) expect(here.includes(title), `${extra.join(" + ")}: "${title}" offered as its button is shown`).toBe(buttons.includes(title));
      for (const title of here) seen.add(title);
      press({ code: "Escape", key: "Escape" });
      await settle();
    }
    // Not blind: each action was shown, and offered, to someone.
    for (const title of account) expect(seen.has(title), title).toBe(true);
  });
});

describe("a record's commands on every tab", () => {
  // Critic p03 round 10: with a user open on "What they can do" the palette offered no Reset
  // password (the commands followed the Details tab, where their buttons are).
  const account = [en("identity.reset.open"), en("identity.sessions.revoke"), en("identity.users.delete")];
  const showTab = async (key: string) => {
    const tab = [...document.querySelectorAll<HTMLButtonElement>('[role="tab"]')].find((b) => b.textContent === en(key));
    expect(tab, key).toBeDefined();
    act(() => tab!.click());
    await settleUntilQuiet(() => calls);
  };
  const here = () => offered().filter((o) => o.group === en("shell.palette.group.here"));

  it("offers a user's account actions from every tab, exactly as on Details, and to a reader none", async () => {
    for (const tab of ["identity.tab.access", "identity.tab.history"]) {
      for (const [permissions, expected] of [[every, true], [reader, false]] as const) {
        await open("/identity/users?open=u1", permissions, { neverSignedIn: true });
        await showTab(tab);
        await openPalette();
        const titles = here().map((o) => o.title);
        for (const title of account) expect(titles.includes(title), `${en(tab)}, ${permissions.length} permissions: "${title}"`).toBe(expected);
        press({ code: "Escape", key: "Escape" });
        await settle();
      }
    }
  });

  it("Reset password from \"What they can do\" opens Details with the reset's first choice focused; Delete user from the history asks first", async () => {
    await open("/identity/users?open=u1", every, { neverSignedIn: true });
    await showTab("identity.tab.access");
    await openPalette(en("identity.reset.open"));
    const reset = here().find((o) => o.title === en("identity.reset.open"));
    expect(reset).toBeDefined();
    act(() => reset!.option.click());
    await settleUntilQuiet(() => calls);
    expect(document.querySelector('[role="tab"][aria-selected="true"]')?.textContent).toBe(en("identity.tab.details"));
    expect(document.querySelector("fieldset.id-method")).not.toBeNull();
    expect(document.activeElement).toBe(document.querySelector("fieldset.id-method input"));

    await open("/identity/users?open=u1", every, { neverSignedIn: true });
    await showTab("identity.tab.history");
    const before = calls.length;
    await openPalette(en("identity.users.delete"));
    act(() => here().find((o) => o.title === en("identity.users.delete"))!.option.click());
    await settleUntilQuiet(() => calls);
    expect(document.querySelector('[role="tab"][aria-selected="true"]')?.textContent).toBe(en("identity.tab.details"));
    expect(document.querySelector(".id-confirm"), "Delete asks first").not.toBeNull();
    expect(calls.slice(before).filter((c) => c.method !== "GET"), "nothing deleted before the answer").toEqual([]);
  });
});

describe("a module action whose screen does not offer its command", () => {
  it("runs nothing and says so, after a few seconds; a command the screen offers runs at once", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    window.history.replaceState(null, "", "/identity/users");
    const action: ModuleAction = { key: "identity.test", labelKey: "identity.users.new", permission: "identity.users.create", path: "/identity/users", command: "identity.test.never" };
    const results: boolean[] = [];
    const ran: string[] = [];
    function Offers({ id }: { id: string }) {
      useCommand({ id, labelKey: "identity.users.new", groupKey: "identity.shortcuts.group", run: () => ran.push(id) });
      return null;
    }
    view = await render(
      <ShortcutProvider>
        <Offers id="identity.test.offered" />
        <PendingCommand action={action} onDone={(r) => results.push(r)} />
        <PendingCommand action={{ ...action, command: "identity.test.offered" }} onDone={(r) => results.push(r)} />
      </ShortcutProvider>,
    );
    await act(async () => {
      vi.advanceTimersByTime(10);
    });
    expect(ran).toEqual(["identity.test.offered"]);
    expect(results).toEqual([true]);
    await act(async () => {
      vi.advanceTimersByTime(6000);
    });
    expect(results).toEqual([true, false]);
    expect(ran).toEqual(["identity.test.offered"]);
  });
});

/** Runs one palette entry on a fresh screen and accepts whatever it opened (a confirmation, a form):
 * new primary and danger buttons are pressed, as a user going along with it would. Returns the
 * writes that left and whether a new record was opened. */
async function runEntry(screen: Screen, permissions: string[], group: string, title: string): Promise<{ found: boolean; writes: Call[]; newRecord: boolean; unrun: boolean }> {
  await show(screen, permissions);
  await openPalette();
  const entry = offered().find((o) => o.group === group && o.title === title);
  if (!entry) return { found: false, writes: [], newRecord: false, unrun: false };
  const before = calls.length;
  const buttons = new Set(document.querySelectorAll("button"));
  act(() => entry.option.click());
  await settleUntilQuiet(() => calls, done);
  // An action that asks for a record (Copy a role, Allow a user to sign in again): the user goes
  // along with it and chooses the first one.
  if (document.querySelector("[data-pick-prompt]")) {
    await chooseFirstRow();
    await settleUntilQuiet(() => calls, done);
  }
  // A module action whose screen, once open, does not offer its command was offered to a user who
  // may not run it (its screen's own check said no).
  const unrun = !done();
  for (let round = 0; round < 3; round++) {
    const fresh = [...document.querySelectorAll<HTMLButtonElement>("button.primary, button.danger, button[type=submit]")].filter((b) => !buttons.has(b) && !b.disabled);
    if (fresh.length === 0) break;
    for (const button of fresh) {
      buttons.add(button);
      if (button.isConnected) act(() => button.click());
      await settleUntilQuiet(() => calls);
    }
  }
  return { found: true, writes: calls.slice(before).filter((c) => c.method !== "GET" && c.method !== "HEAD"), newRecord: recordInAddress() === "new", unrun };
}

/** A screen, with one of its record's tabs open (the user's sign-in history, where Allow sign-in
 * again is). */
type Screen = { path: string; tab?: string };

async function show(screen: Screen, permissions: string[]) {
  await open(screen.path, permissions);
  if (!screen.tab) return;
  const tab = [...document.querySelectorAll<HTMLButtonElement>('[role="tab"]')].find((b) => b.textContent === en(screen.tab!));
  expect(tab, `${screen.path}: tab ${screen.tab}`).toBeDefined();
  act(() => tab!.click());
  await settleUntilQuiet(() => calls);
}

/** The screens a user meets, each with a record open where it has records. */
const sweptScreens: Screen[] = [
  { path: "/" },
  { path: "/identity/users?open=u1" },
  { path: "/identity/users?open=u1", tab: "identity.tab.history" },
  { path: "/identity/roles?open=r-clerk" },
  { path: "/tenancy/companies?open=c9" },
  { path: "/tenancy/branches?open=b1" },
];

const label = (screen: Screen) => (screen.tab ? `${screen.path} (${en(screen.tab)})` : screen.path);

/** The entries to run on a screen: its own commands ("On this screen") and, on the home screen,
 * the actions offered from anywhere (the shell's and the modules'; the same on every screen, so
 * run once, from home, where each module action opens its screen and runs its command). */
async function entriesOn(screen: Screen, permissions: string[]): Promise<{ group: string; title: string }[]> {
  await show(screen, permissions);
  await openPalette();
  const groups = screen.path === "/" ? [en("shell.palette.group.here"), en("shell.palette.group.actions")] : [en("shell.palette.group.here")];
  // Signing out is the reader's to do and ends the screen: it is not swept.
  return offered()
    .filter((o) => groups.includes(o.group) && o.title !== en("shell.action.signOut"))
    .map(({ group, title }) => ({ group, title }));
}

describe("every palette entry, run by a user who may only read", () => {
  it("sends nothing but reads (and the user's own settings), and opens no new record, on every screen", async () => {
    const problems: string[] = [];
    let swept = 0;
    for (const screen of sweptScreens) {
      for (const { group, title } of await entriesOn(screen, reader)) {
        const result = await runEntry(screen, reader, group, title);
        if (!result.found) continue;
        swept++;
        for (const write of result.writes.filter((w) => !readerMayWrite(w))) problems.push(`${label(screen)}: "${title}" (${group}) sent ${write.method} ${write.url}`);
        if (result.newRecord) problems.push(`${label(screen)}: "${title}" (${group}) opened a new record`);
        if (result.unrun) problems.push(`${label(screen)}: "${title}" (${group}) was offered, but its screen does not offer it to this user`);
      }
    }
    expect(problems).toEqual([]);
    console.log(`palette entries swept for a reader: ${swept}`);
    expect(swept).toBeGreaterThanOrEqual(ratchetMinimum("g2.paletteEntriesSwept"));
  }, 120_000);

  it("the control: the same sweep for a user who may change everything finds the writes the entries make", async () => {
    const found: string[] = [];
    for (const screen of [{ path: "/identity/users?open=u1" }, { path: "/identity/users?open=u1", tab: "identity.tab.history" }, { path: "/identity/roles?open=r-clerk" }]) {
      for (const { group, title } of await entriesOn(screen, every)) {
        const result = await runEntry(screen, every, group, title);
        for (const write of result.writes.filter((w) => !readerMayWrite(w))) found.push(`${write.method} ${new URL(write.url, "http://localhost").pathname}`);
        if (result.newRecord) found.push(`${title}: new record`);
      }
    }
    for (const write of ["POST /api/identity/roles/r-clerk/copy", "DELETE /api/identity/roles/r-clerk", "POST /api/identity/users/u1/password", "POST /api/identity/users/u1/sessions/revoke", "POST /api/identity/users/u1/unblock"])
      expect(found, write).toContain(write);
  }, 120_000);
});

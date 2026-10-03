import { act } from "react";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { I18nProvider } from "../i18n";
import { mockFetch, render, setInput, settle, type Rendered } from "../../test/render";
import { ListView } from "./ListView";
import type { ListDefinition, Row } from "./model";

let view: Rendered | undefined;

const definition: ListDefinition = {
  key: "identity.users",
  labelKey: "identity.users.title",
  endpoint: "/api/identity/users",
  columns: [
    { key: "displayName", labelKey: "identity.users.name", type: "text", sortable: true, filterable: true, groupable: false, aggregate: false, hidden: false, choices: [], operators: ["eq", "ne", "contains", "startsWith", "endsWith", "in", "isNull", "isNotNull"] },
    { key: "email", labelKey: "identity.users.email", type: "text", sortable: true, filterable: true, groupable: false, aggregate: false, hidden: false, choices: [], operators: ["eq", "contains"] },
    { key: "language", labelKey: "identity.users.language", type: "choice", sortable: false, filterable: true, groupable: true, aggregate: false, hidden: false, choices: [{ value: "en", labelKey: "identity.language.en" }, { value: "ar", labelKey: "identity.language.ar" }], operators: ["eq", "in"] },
  ],
  searchFields: ["displayName", "email"],
  defaultSort: "displayName",
  presets: [{ key: "arabic", labelKey: "identity.users.view.active", filter: "language eq 'ar'", sort: null, groupBy: null }],
  canShare: true,
  maxTake: 200,
};

const people: Row[] = Array.from({ length: 30 }, (_, i) => ({
  id: `00000000-0000-7000-8000-${String(i).padStart(12, "0")}`,
  displayName: i === 7 ? "Shamma Waleed Al Romaithi" : `Person ${i}`,
  email: `person${i}@alnoor.example`,
  language: i % 3 === 0 ? "ar" : "en",
}));

function serve(calls: { method: string; url: string; body: unknown }[] = []) {
  return mockFetch((method, url, body) => {
    calls.push({ method, url, body });
    const parsed = new URL(url, "http://localhost");
    if (parsed.pathname.endsWith("/definition")) return { status: 200, body: definition };
    if (parsed.pathname.endsWith("/views") && method === "GET") return { status: 200, body: { items: [], total: 0 } };
    if (parsed.pathname.endsWith("/views") && method === "POST") {
      return { status: 201, body: { id: "v1", listKey: "identity.users", name: (body as { name: string }).name, isShared: false, isDefault: false, isMine: true, columns: [], sort: null, filter: null, search: null, groupBy: null, updatedAt: "", version: 1 } };
    }
    if (parsed.pathname === "/api/identity/users") {
      const search = (parsed.searchParams.get("search") ?? "").toLowerCase();
      const rows = people.filter((p) => search.split(" ").every((w) => String(p.displayName).toLowerCase().includes(w)));
      const group = parsed.searchParams.get("groupBy");
      return {
        status: 200,
        body: {
          items: rows.slice(0, Number(parsed.searchParams.get("take") ?? 50)),
          total: rows.length,
          next: null,
          groups: group ? [{ key: "ar", count: 10, totals: null }, { key: "en", count: 20, totals: null }] : null,
        },
      };
    }
    return { status: 404, body: { title: "not found" } };
  });
}

async function wait(ms: number) {
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, ms));
  });
}

beforeEach(() => {
  window.history.replaceState(null, "", "/identity/users");
});

afterEach(() => view?.unmount());

async function show() {
  view = await render(
    <I18nProvider initial="en">
      <ListView listKey="identity.users" titleKey="identity.users.title" countKey="identity.users.count" searchPlaceholderKey="identity.users.search" />
    </I18nProvider>,
  );
  await settle();
  await settle();
  return view;
}

const param = (url: string, name: string) => new URL(url, "http://localhost").searchParams.get(name);

const grid = () => document.querySelector<HTMLElement>("[role=grid]")!;
const key = async (target: Element, k: string, init: KeyboardEventInit = {}) =>
  act(async () => {
    target.dispatchEvent(new KeyboardEvent("keydown", { key: k, bubbles: true, ...init }));
  });

describe("list view", () => {
  it("shows the first rows with their count and puts the cursor in the search box", async () => {
    const calls: { method: string; url: string; body: unknown }[] = [];
    serve(calls);
    const v = await show();
    await wait(5);
    expect(v.container.textContent).toContain("30 users");
    expect(v.container.querySelectorAll("[role=row][aria-rowindex]").length).toBeGreaterThan(10);
    expect(document.activeElement?.getAttribute("type")).toBe("search");
    const list = calls.find((c) => c.url.startsWith("/api/identity/users?"))!;
    expect(new URL(list.url, "http://x").searchParams.get("sort")).toBe("displayName");
    expect(grid().getAttribute("aria-rowcount")).toBe("31");
  });

  it("searches as the user types and opens the only match with Enter", async () => {
    const calls: { method: string; url: string; body: unknown }[] = [];
    serve(calls);
    const v = await show();
    const search = v.container.querySelector<HTMLInputElement>("input[type=search]")!;
    setInput(search, "shamma romaithi");
    await key(search, "Enter");
    await wait(250);
    await settle();
    expect(calls.some((c) => c.url.includes("search=shamma+romaithi"))).toBe(true);
    const panel = v.container.querySelector("[role=region].list-record");
    expect(panel?.textContent).toContain("Shamma Waleed Al Romaithi");
    expect(window.location.search).toContain("open=");
    await key(panel!, "Escape");
    expect(v.container.querySelector(".list-record")).toBeNull();
  });

  it("moves through rows with the keyboard, selects with Space and copies the selection", async () => {
    serve();
    const v = await show();
    grid().focus();
    await key(grid(), "ArrowDown");
    await key(grid(), "ArrowDown");
    expect(grid().getAttribute("aria-activedescendant")).toMatch(/-row-2$/);
    await key(grid(), " ");
    await key(grid(), "ArrowDown", { shiftKey: true });
    expect(v.container.textContent).toContain("2 selected");
    await key(grid(), "End");
    expect(grid().getAttribute("aria-activedescendant")).toMatch(/-row-29$/);
    await key(grid(), "Escape");
    expect(v.container.textContent).not.toContain("selected");
  });

  it("sorts from the header and filters from the column menu", async () => {
    const calls: { method: string; url: string; body: unknown }[] = [];
    serve(calls);
    const v = await show();
    const sortButton = [...v.container.querySelectorAll<HTMLButtonElement>(".list-sort")].find((b) => b.textContent?.startsWith("E-mail"))!;
    await act(async () => sortButton.click());
    await settle();
    expect(calls.some((c) => param(c.url, "sort") === "email")).toBe(true);
    await act(async () => sortButton.click());
    await settle();
    expect(calls.some((c) => param(c.url, "sort") === "-email")).toBe(true);

    const menuButton = v.container.querySelector<HTMLButtonElement>("[aria-label='Options for the column Language']")!;
    await act(async () => menuButton.click());
    const filterItem = [...v.container.querySelectorAll<HTMLButtonElement>("[role=menuitem]")].find((b) => b.textContent === "Filter…")!;
    await act(async () => filterItem.click());
    const arabic = [...v.container.querySelectorAll<HTMLLabelElement>(".list-filter label")].find((l) => l.textContent === "Arabic")!;
    await act(async () => arabic.querySelector("input")!.click());
    await act(async () => v.container.querySelector<HTMLFormElement>(".list-filter form")!.requestSubmit());
    await settle();
    expect(calls.some((c) => param(c.url, "filter") === "language eq 'ar'")).toBe(true);
    expect(v.container.querySelector(".list-chips")?.textContent).toContain("Language is Arabic");
    expect(param(window.location.search, "filter")).toBe("language eq 'ar'");
  });

  it("groups with counts and drills into a group with Enter", async () => {
    const calls: { method: string; url: string; body: unknown }[] = [];
    serve(calls);
    const v = await show();
    const menuButton = v.container.querySelector<HTMLButtonElement>("[aria-label='Options for the column Language']")!;
    await act(async () => menuButton.click());
    const groupItem = [...v.container.querySelectorAll<HTMLButtonElement>("[role=menuitem]")].find((b) => b.textContent === "Group by this column")!;
    await act(async () => groupItem.click());
    await settle();
    expect(calls.some((c) => c.url.includes("groupBy=language"))).toBe(true);
    expect(v.container.textContent).toContain("10 rows");
    grid().focus();
    await key(grid(), "Enter");
    await settle();
    expect(calls.some((c) => param(c.url, "filter") === "language eq 'ar'" && !c.url.includes("groupBy"))).toBe(true);
  });

  it("applies a built-in view and saves the current state as a personal view", async () => {
    const calls: { method: string; url: string; body: unknown }[] = [];
    serve(calls);
    const v = await show();
    const viewsButton = [...v.container.querySelectorAll<HTMLButtonElement>("button")].find((b) => b.textContent?.startsWith("View:"))!;
    await act(async () => viewsButton.click());
    const preset = [...v.container.querySelectorAll<HTMLButtonElement>("[role=menuitemradio]")].find((b) => b.textContent === "Active users")!;
    await act(async () => preset.click());
    await settle();
    expect(calls.some((c) => param(c.url, "filter") === "language eq 'ar'")).toBe(true);

    await act(async () => viewsButton.click());
    const saveAs = [...v.container.querySelectorAll<HTMLButtonElement>("[role=menuitem]")].find((b) => b.textContent === "Save as a new view…")!;
    await act(async () => saveAs.click());
    const name = v.container.querySelector<HTMLInputElement>(".list-save input:not([type=checkbox])")!;
    setInput(name, "My Arabic users");
    await act(async () => v.container.querySelector<HTMLFormElement>(".list-save form")!.requestSubmit());
    await settle();
    const post = calls.find((c) => c.method === "POST")!;
    expect(post.url).toBe("/api/lists/identity.users/views");
    expect(post.body).toMatchObject({ name: "My Arabic users", filter: "language eq 'ar'", sort: "displayName", columns: ["displayName", "email", "language"] });
  });
});

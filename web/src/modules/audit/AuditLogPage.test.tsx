import { act } from "react";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { I18nProvider } from "../../kernel/i18n";
import type { ListColumn, ListDefinition } from "../../kernel/lists/model";
import { SessionProvider } from "../../kernel/session";
import { ShortcutProvider } from "../../kernel/shortcuts";
import { mockFetch, render, setInput, settle, type Rendered } from "../../test/render";
import { AuditLogPage } from "./AuditLogPage";

let view: Rendered | undefined;

beforeEach(() => {
  localStorage.clear();
  window.history.replaceState(null, "", "/audit/log");
});

afterEach(() => {
  view?.unmount();
  view = undefined;
});

function column(key: string, labelKey: string, type: ListColumn["type"], flags: Partial<ListColumn> = {}): ListColumn {
  return { key, labelKey, type, sortable: false, filterable: false, groupable: false, aggregate: false, hidden: false, choices: [], operators: [], ...flags };
}

const definition: ListDefinition = {
  key: "audit.log",
  labelKey: "audit.log.title",
  endpoint: "/api/audit/log",
  columns: [
    column("occurredAt", "audit.log.when", "dateTime", { sortable: true, filterable: true }),
    column("actorId", "audit.log.who", "reference", { filterable: true }),
    column("action", "audit.log.action", "choice", { filterable: true, choices: [{ value: "update", labelKey: "audit.action.update" }] }),
    column("recordType", "audit.log.recordType", "choice", { filterable: true, choices: [{ value: "identity.users", labelKey: "identity.record.user" }] }),
    column("record", "audit.log.record", "text", { filterable: true }),
    column("fields", "audit.log.fields", "choice", {
      filterable: true,
      choices: [{ value: "email", labelKey: "audit.field.email" }, { value: "name_en", labelKey: "audit.field.name_en" }, { value: "name", labelKey: "audit.field.name_en" }],
    }),
    column("changes", "audit.log.changes", "text"),
  ],
  searchFields: ["record"],
  defaultSort: "-occurredAt",
  presets: [],
  canShare: false,
  maxTake: 200,
  printable: true,
};

const row = {
  id: "c1", occurredAt: "2026-10-24T10:15:00Z", actorId: "u9", actorName: "Noor Editor", actorNameAr: "نور المحرر", actorKind: "user", action: "update",
  recordType: "identity.users", record: "Shamma Al Romaithi", recordAr: "شمة الرميثي", recordCode: "shamma@x.example", recordId: "u7", recordRoute: "/identity/users/u7", fields: ["email"],
  changes: "E-mail: from a@x.example to b@x.example",
};
const arabicRow = { ...row, changes: "البريد الإلكتروني: من a@x.example إلى b@x.example" };

function serve(language: "en" | "ar" = "en") {
  return mockFetch((_method, url) => {
    if (url === "/api/auth/session") return { status: 200, body: { authenticated: true, user: { id: "u1", email: "a@x", displayName: "Mariam", language: "en" }, tenant: { id: "t1", code: "x", nameEn: "X", nameAr: "س" }, permissions: ["audit.log.read", "identity.users.read"], menu: [], expiresAt: null } };
    if (url.startsWith("/api/lists/audit.log/definition")) return { status: 200, body: definition };
    if (url.startsWith("/api/lists/audit.log/views")) return { status: 200, body: { items: [] } };
    if (url.startsWith("/api/audit/actors")) {
      const text = decodeURIComponent(url.split("search=")[1] ?? "").toLowerCase();
      return { status: 200, body: [{ id: "u9", name: "Noor Editor", nameAr: "نور المحرر", email: "noor@x.example" }, { id: "u3", name: "Omar Viewer", nameAr: null, email: null }].filter((p) => p.name.toLowerCase().includes(text) || (p.nameAr ?? "").includes(text)) };
    }
    if (url.startsWith("/api/audit/log/c1")) return { status: 200, body: { change: row, details: [{ field: "email", old: "a@x.example", new: "b@x.example" }], names: {} } };
    if (url.startsWith("/api/audit/log")) return { status: 200, body: { items: [language === "ar" ? arabicRow : row], total: 1, next: null } };
    return { status: 404, body: {} };
  });
}

async function show(language: "en" | "ar") {
  view = await render(
    <I18nProvider initial={language}>
      <SessionProvider>
        <ShortcutProvider>
          <AuditLogPage />
        </ShortcutProvider>
      </SessionProvider>
    </I18nProvider>,
  );
  for (let i = 0; i < 4; i++) await settle();
  return view;
}

describe("the audit log screen", () => {
  it("lists changes on the list framework: who, the record, the change in the screen's language", async () => {
    const calls = serve();
    const { container } = await show("en");
    expect(calls.some((c) => c.url.startsWith("/api/audit/log?"))).toBe(true);
    const text = container.textContent ?? "";
    expect(text).toContain("Noor Editor");
    expect(text).toContain("Shamma Al Romaithi");
    expect(text).toContain("E-mail: from a@x.example to b@x.example");
    // The changed fields read by their names.
    expect(text).toContain("E-mail");
  });

  it("reads in Arabic: the person's and the record's Arabic names and the Arabic change line", async () => {
    serve("ar");
    const { container } = await show("ar");
    const text = container.textContent ?? "";
    expect(text).toContain("نور المحرر");
    expect(text).toContain("شمة الرميثي");
    expect(text).toContain("البريد الإلكتروني: من a@x.example إلى b@x.example");
  });

  it("opens a change in the side panel: every field, the record and its history", async () => {
    const calls = serve();
    window.history.replaceState(null, "", "/audit/log?open=c1");
    const { container } = await show("en");
    await act(async () => {
      await new Promise((r) => setTimeout(r, 0));
    });
    expect(calls.some((c) => c.url === "/api/audit/log/c1")).toBe(true);
    const panel = container.querySelector(".audit-change")!;
    expect(panel).not.toBeNull();
    expect(panel.textContent).toContain("Changed by Noor Editor");
    expect(panel.textContent).toContain("E-mail: from a@x.example to b@x.example");
    expect(panel.querySelector('a[href="/identity/users/u7"]')).not.toBeNull();
    expect(panel.querySelector('a[href="/audit/log?filter=recordId+eq+%27u7%27"]')).not.toBeNull();
    // The person's every change, one link away.
    expect(panel.querySelector('a[href="/audit/log?filter=actorId+eq+%27u9%27"]')!.textContent).toBe("Every change by Noor Editor");
  });

  const openFilter = async (container: HTMLElement, column: string) => {
    await act(async () => container.querySelector<HTMLButtonElement>(`[aria-label='Options for the column ${column}']`)!.click());
    const item = [...container.querySelectorAll<HTMLButtonElement>("[role=menuitem]")].find((b) => b.textContent === "Filter…")!;
    await act(async () => item.click());
  };

  it("filters by the person chosen from a picker: typing a name finds people, Arrow keys and Enter choose, the filter carries the id", async () => {
    const calls = serve();
    const { container } = await show("en");
    await openFilter(container, "Changed by");
    const box = container.querySelector<HTMLInputElement>(".list-filter input[role=combobox]")!;
    expect(box).not.toBeNull();
    setInput(box, "noor");
    for (let i = 0; i < 3; i++) await settle();
    expect(calls.some((c) => c.url === "/api/audit/actors?take=10&search=noor")).toBe(true);
    const options = [...container.querySelectorAll(".list-filter [role=option]")];
    expect(options.map((o) => o.textContent)).toEqual(["Noor Editor (noor@x.example)"]);
    await act(async () => {
      box.dispatchEvent(new KeyboardEvent("keydown", { key: "Enter", bubbles: true }));
    });
    expect(container.querySelector(".list-filter .list-choices")!.textContent).toContain("Noor Editor");
    await act(async () => container.querySelector<HTMLFormElement>(".list-filter form")!.requestSubmit());
    for (let i = 0; i < 3; i++) await settle();
    // Never a free-text actorId: the answer is the person's id, and the chip names the person.
    const last = calls.filter((c) => c.url.startsWith("/api/audit/log?")).at(-1)!;
    expect(new URL(last.url, "http://x").searchParams.get("filter")).toBe("actorId eq 'u9'");
    expect(container.querySelector(".list-chips")!.textContent).toContain("Noor Editor");
  });

  it("offers each field name once in the Fields filter and takes every field of that name", async () => {
    const calls = serve();
    const { container } = await show("en");
    await openFilter(container, "Fields");
    const labels = [...container.querySelectorAll(".list-filter .list-check")].map((l) => l.textContent);
    expect(labels.filter((l) => l === "Name in English")).toHaveLength(1);
    const same = [...container.querySelectorAll<HTMLLabelElement>(".list-filter .list-check")].find((l) => l.textContent === "Name in English")!;
    await act(async () => same.querySelector("input")!.click());
    await act(async () => container.querySelector<HTMLFormElement>(".list-filter form")!.requestSubmit());
    for (let i = 0; i < 3; i++) await settle();
    const last = calls.filter((c) => c.url.startsWith("/api/audit/log?")).at(-1)!;
    expect(new URL(last.url, "http://x").searchParams.get("filter")).toBe("fields in ('name_en', 'name')");
    expect(container.querySelector(".list-chips")!.textContent).toContain("Name in English");
    expect(container.querySelector(".list-chips")!.textContent).not.toContain("Name in English, Name in English");
  });
});

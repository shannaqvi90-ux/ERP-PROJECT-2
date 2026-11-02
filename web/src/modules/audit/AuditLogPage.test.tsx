import { act } from "react";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { I18nProvider } from "../../kernel/i18n";
import type { ListColumn, ListDefinition } from "../../kernel/lists/model";
import { SessionProvider } from "../../kernel/session";
import { ShortcutProvider } from "../../kernel/shortcuts";
import { mockFetch, render, settle, type Rendered } from "../../test/render";
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
    column("fields", "audit.log.fields", "choice", { filterable: true, choices: [{ value: "email", labelKey: "audit.field.email" }] }),
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

function serve(language: "en" | "ar" = "en", listed?: typeof row) {
  return mockFetch((_method, url) => {
    if (url === "/api/auth/session") return { status: 200, body: { authenticated: true, user: { id: "u1", email: "a@x", displayName: "Mariam", language: "en" }, tenant: { id: "t1", code: "x", nameEn: "X", nameAr: "س" }, permissions: ["audit.log.read", "identity.users.read"], menu: [], expiresAt: null } };
    if (url.startsWith("/api/lists/audit.log/definition")) return { status: 200, body: definition };
    if (url.startsWith("/api/lists/audit.log/views")) return { status: 200, body: { items: [] } };
    if (url.startsWith("/api/audit/log/c1")) return { status: 200, body: { change: row, details: [{ field: "email", old: "a@x.example", new: "b@x.example" }], names: {} } };
    if (url.startsWith("/api/audit/log")) return { status: 200, body: { items: [listed ?? (language === "ar" ? arabicRow : row)], total: 1, next: null } };
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

  it("shows the start of a long change text in the cell and the whole text as its tooltip", async () => {
    const whole = Array.from({ length: 40 }, (_, i) => `Permissions: from nothing to Permission number ${i}`).join("; ");
    serve("en", { ...row, changes: whole });
    const { container } = await show("en");
    const cell = container.querySelector(".audit-change-text")!;
    expect(cell.textContent!.length).toBe(200);
    expect(cell.textContent!.endsWith("…")).toBe(true);
    expect(whole.startsWith(cell.textContent!.slice(0, 199))).toBe(true);
    expect(cell.getAttribute("title")).toBe(whole);
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
});

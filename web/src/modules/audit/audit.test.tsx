import { act } from "react";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { RecordForm } from "../../kernel/forms/RecordForm";
import { useRecordForm } from "../../kernel/forms/useRecordForm";
import { formatValue } from "../../kernel/lists/format";
import type { ListColumn } from "../../kernel/lists/model";
import { I18nProvider } from "../../kernel/i18n";
import { SessionProvider } from "../../kernel/session";
import { ShortcutProvider } from "../../kernel/shortcuts";
import { mockFetch, render, settle, setInput, type Rendered } from "../../test/render";
import { extensions } from "./extensions";
import { fieldName, valueText, type AuditHistory, type ValueFormat } from "./model";

let view: Rendered | undefined;

beforeEach(() => {
  localStorage.clear();
  window.history.replaceState(null, "", "/identity/users/u7");
});

afterEach(() => {
  view?.unmount();
  view = undefined;
});

const session = (permissions: string[]) => ({
  authenticated: true,
  user: { id: "u1", email: "admin@alnoor.example", displayName: "Mariam Al Mansoori", language: "en" },
  tenant: { id: "t1", code: "alnoor", nameEn: "Al Noor Trading LLC", nameAr: "شركة النور للتجارة ذ.م.م" },
  permissions,
  menu: [],
  expiresAt: null,
});

const role = "0192a7c0-0000-7000-8000-000000000001";
const first: AuditHistory = {
  recordType: "identity.users",
  items: [
    {
      id: "e3", occurredAt: "2026-10-24T10:15:00Z", actorId: "u9", actorName: "Noor Editor", actorNameAr: "نور المحرر", actorKind: "user", action: "update",
      changes: [
        { field: "email", old: "shamma@mail.example", new: "shamma.new@mail.example" },
        { field: "is_active", old: true, new: false },
      ],
    },
    {
      id: "e2", occurredAt: "2026-10-23T08:00:00Z", actorId: "u1", actorName: "Mariam Al Mansoori", actorNameAr: null, actorKind: "user", action: "update",
      changes: [{ field: "password_hash", old: "[redacted]", new: "[redacted]" }, { field: "role_ids", old: [], new: [role] }],
    },
  ],
  next: "e2",
  names: { [role]: { name: "Sales clerk", nameAr: "موظف مبيعات" } },
};
const older: AuditHistory = {
  recordType: "identity.users",
  items: [{ id: "e1", occurredAt: "2026-10-01T06:00:00Z", actorId: null, actorName: "Demo data", actorNameAr: "بيانات تجريبية", actorKind: "seed", action: "insert",
    changes: [{ field: "id", new: "u7" }, { field: "email", new: "shamma@mail.example" }, { field: "custom_fields.region", new: "North" }] }],
  next: null,
  names: {},
};

function serve(permissions: string[]) {
  return mockFetch((_method, url) => {
    if (url === "/api/auth/session") return { status: 200, body: session(permissions) };
    const parsed = new URL(url, "http://localhost");
    if (parsed.pathname === "/api/audit/records/identity.users/u7") {
      if (parsed.searchParams.get("take") === "1") return { status: 200, body: { ...first, items: first.items.slice(0, 1), next: "e3" } };
      return { status: 200, body: parsed.searchParams.get("before") === "e2" ? older : first };
    }
    return { status: 404, body: {} };
  });
}

type Thing = { id: string; name: string; version: number };

function Form({ id }: { id: string | null }) {
  const form = useRecordForm<Thing, { name: string }>({
    load: async () => ({ id: id ?? "", name: "Shamma Al Romaithi", version: 1 }),
    initial: (r) => ({ name: r?.name ?? "" }),
    save: async (d) => ({ id: id ?? "", name: d.name, version: 2 }),
    canEdit: true,
  }, id);
  return (
    <RecordForm form={form} record={{ type: "identity.users", id }} title="Shamma Al Romaithi">
      <p>body</p>
    </RecordForm>
  );
}

async function show(language: "en" | "ar", id: string | null = "u7") {
  view = await render(
    <I18nProvider initial={language}>
      <SessionProvider>
        <ShortcutProvider>
          <Form id={id} />
        </ShortcutProvider>
      </SessionProvider>
    </I18nProvider>,
  );
  await settle();
  await settle();
  return view;
}

const press = (code: string, key: string) =>
  act(() => {
    window.dispatchEvent(new KeyboardEvent("keydown", { code, key, altKey: true, bubbles: true }));
  });

describe("a record's history on its form", () => {
  it("shows the latest change under the heading: who, when, and each field's old and new value", async () => {
    const calls = serve(["identity.users.read"]);
    const { container } = await show("en");
    const latest = container.querySelector('[data-testid="audit-latest"]')!;
    expect(latest.textContent).toContain("Changed by Noor Editor");
    expect(latest.textContent).toContain("E-mail: from shamma@mail.example to shamma.new@mail.example");
    expect(latest.textContent).toContain("Active: from Yes to No");
    expect(latest.querySelector("time")!.getAttribute("datetime")).toBe("2026-10-24T10:15:00Z");
    expect(calls.some((c) => c.url === "/api/audit/records/identity.users/u7?take=1")).toBe(true);
    // The whole history is read only when asked for.
    expect(calls.filter((c) => c.url.startsWith("/api/audit/records/") && !c.url.includes("take=1"))).toEqual([]);
  });

  it("opens the whole history with Alt+Y: every change, every field, names for ids, secrets hidden, older changes on demand, Escape closes", async () => {
    const calls = serve(["identity.users.read", "audit.log.read"]);
    const { container } = await show("en");
    expect(container.querySelector('[data-testid="audit-history"]')).toBeNull();
    press("KeyY", "y");
    await settle();
    const panel = container.querySelector('[data-testid="audit-history"]')!;
    expect(panel).not.toBeNull();
    expect(document.activeElement?.tagName).toBe("H3");
    const events = [...panel.querySelectorAll(".audit-event")];
    expect(events.map((e) => e.querySelector("strong")!.textContent)).toEqual(["Changed by Noor Editor", "Changed by Mariam Al Mansoori"]);
    const rows = (event: Element) => [...event.querySelectorAll("tbody tr")].map((r) => [...r.children].map((c) => c.textContent));
    expect(rows(events[0]!)).toEqual([["E-mail", "shamma@mail.example", "shamma.new@mail.example"], ["Active", "Yes", "No"]]);
    expect(rows(events[1]!)).toEqual([["Password", "(hidden)", "(hidden)"], ["role_ids", "(empty)", "Sales clerk"]]);
    expect(panel.querySelector('a[href="/audit/log?filter=recordId+eq+%27u7%27"]')).not.toBeNull();

    // Find one field's changes.
    setInput(panel.querySelector<HTMLInputElement>('input[type="search"]')!, "mail");
    await settle();
    expect([...panel.querySelectorAll(".audit-event")].length).toBe(1);
    expect(rows(panel.querySelector(".audit-event")!)).toEqual([["E-mail", "shamma@mail.example", "shamma.new@mail.example"]]);
    setInput(panel.querySelector<HTMLInputElement>('input[type="search"]')!, "");
    await settle();

    // Older changes, a page at a time; a creation lists its first values (the record's own id is not a field).
    const more = [...panel.querySelectorAll("button")].find((b) => b.textContent === "Show older changes")!;
    await act(async () => more.click());
    await settle();
    expect(calls.some((c) => c.url === "/api/audit/records/identity.users/u7?before=e2")).toBe(true);
    const created = [...panel.querySelectorAll(".audit-event")].at(-1)!;
    expect(created.querySelector("strong")!.textContent).toBe("Created by Demo data");
    expect(rows(created)).toEqual([["E-mail", "shamma@mail.example"], ["custom_fields.region", "North"]]);
    expect([...panel.querySelectorAll("button")].some((b) => b.textContent === "Show older changes")).toBe(false);

    await act(async () => {
      panel.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true }));
    });
    expect(container.querySelector('[data-testid="audit-history"]')).toBeNull();
  });

  it("reads in Arabic: field names, the person's Arabic name, the change line", async () => {
    serve(["identity.users.read"]);
    const { container } = await show("ar");
    const latest = container.querySelector('[data-testid="audit-latest"]')!;
    expect(latest.textContent).toContain("عدّله نور المحرر");
    expect(latest.textContent).toContain("البريد الإلكتروني: من shamma@mail.example إلى shamma.new@mail.example");
    // The log link is offered only to a reader of the audit log.
    press("KeyY", "y");
    await settle();
    expect(container.querySelector('[data-testid="audit-history"] a')).toBeNull();
  });

  it("is not offered for a record being created", async () => {
    const calls = serve(["identity.users.read"]);
    const { container } = await show("en", null);
    expect(container.querySelector('[data-record-panel="audit.history"]')).toBeNull();
    expect(container.querySelector('[data-testid="audit-latest"]')).toBeNull();
    expect(calls.filter((c) => c.url.startsWith("/api/audit/"))).toEqual([]);
  });

  it("is a record panel every record form offers, with its key", () => {
    const item = extensions.record!.find((r) => r.key === "audit.history")!;
    expect(item.chord).toBe("Alt+KeyY");
    expect(item.summary).toBeTypeOf("function");
  });
});

describe("recorded values", () => {
  const t = (key: string, params?: Record<string, string | number>) => {
    const texts: Record<string, string> = { "audit.value.empty": "(empty)", "audit.value.hidden": "(hidden)", "audit.value.yes": "Yes", "audit.value.no": "No", "audit.field.email": "E-mail" };
    return (texts[key] ?? key) + (params ? JSON.stringify(params) : "");
  };
  const f: ValueFormat = { t, formatDateTime: (v) => `time(${v})`, formatNumber: (n) => `n${n}`, language: "en", names: { [role]: { name: "Sales clerk", nameAr: "موظف مبيعات" } } };
  it("reads as people read them", () => {
    expect(valueText(null, f)).toBe("(empty)");
    expect(valueText("", f)).toBe("(empty)");
    expect(valueText("[redacted]", f)).toBe("(hidden)");
    expect(valueText(true, f)).toBe("Yes");
    expect(valueText(3, f)).toBe("n3");
    expect(valueText("2026-10-24T10:15:00+04:00", f)).toBe("time(2026-10-24T10:15:00+04:00)");
    expect(valueText([role, "x"], f)).toBe("Sales clerk, x");
    expect(valueText(role, { ...f, language: "ar" })).toBe("موظف مبيعات");
    expect(valueText({ a: 1 }, f)).toBe('{"a":1}');
    expect(fieldName("email", t)).toBe("E-mail");
    expect(fieldName("custom_fields.region", t)).toBe("custom_fields.region");
  });

  it("a list cell holding several of its column's values shows their labels; ids of other records are counted", () => {
    const column = { key: "fields", labelKey: "x", type: "choice", sortable: false, filterable: true, groupable: false, aggregate: false, hidden: false, operators: [],
      choices: [{ value: "email", labelKey: "audit.field.email" }, { value: "phone", labelKey: "audit.field.phone" }] } as ListColumn;
    const format = { t: (k: string) => (k === "audit.field.email" ? "E-mail" : k === "audit.field.phone" ? "Phone" : k), formatDateTime: String, formatDate: String, formatNumber: String, formatDecimal: String };
    expect(formatValue(column, ["email", "phone"], format)).toBe("E-mail, Phone");
    expect(formatValue(column, ["0192a7c0-0000-7000-8000-000000000001"], format)).toBe("1");
  });
});

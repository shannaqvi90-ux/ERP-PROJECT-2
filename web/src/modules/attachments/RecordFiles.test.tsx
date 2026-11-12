import { act } from "react";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { RecordForm } from "../../kernel/forms/RecordForm";
import { useRecordForm } from "../../kernel/forms/useRecordForm";
import { I18nProvider } from "../../kernel/i18n";
import { SessionProvider } from "../../kernel/session";
import { ShortcutProvider } from "../../kernel/shortcuts";
import { mockFetch, render, settle, type Rendered } from "../../test/render";
import { extensions as audit } from "../audit/extensions";
import { extensions as files } from "./extensions";

let view: Rendered | undefined;
const id = "0190a000-0000-7000-8000-0000000000c1";

beforeEach(() => {
  localStorage.clear();
  window.history.replaceState(null, "", `/contacts/contacts/${id}`);
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
const file = (fileId: string, name: string, over: Record<string, unknown> = {}) => ({
  id: fileId, name, contentType: name.endsWith(".pdf") ? "application/pdf" : name.endsWith(".zip") ? "application/zip" : "text/plain; charset=utf-8", sizeBytes: 2048, sha256: "a".repeat(64),
  previewable: !name.endsWith(".zip"), createdAt: "2026-11-01T09:30:00Z", createdBy: "u1", createdByName: "Mariam Al Mansoori", createdByNameAr: null, ...over,
});

function serve(permissions: string[], items = [file("f1", "Delivery note.txt"), file("f2", "Trade licence.pdf"), file("f3", "Scans.zip"), file("f4", "Old quote.txt")]) {
  const calls: string[] = [];
  mockFetch((method, url) => {
    calls.push(`${method} ${url}`);
    if (url === "/api/auth/session") return { status: 200, body: session(permissions) };
    if (url === `/api/attachments/contacts.contacts/${id}/files`) return { status: 200, body: { items, total: items.length, maxFiles: 250, maxFileBytes: 20971520, extensions: ["pdf", "txt", "zip"] } };
    const audit = new URL(url, "http://localhost");
    if (audit.pathname === `/api/audit/records/${id}`) {
      const own = audit.searchParams.get("type") === "contacts.contacts";
      const take = audit.searchParams.get("take");
      const history = own
        ? [{ id: "c2", occurredAt: "2026-10-30T10:00:00Z", actorId: "u1", actorName: "Mariam Al Mansoori", actorNameAr: null, actorKind: "user", action: "update", changes: [{ field: "phone", old: "1", new: "2" }] }]
        : [
            { id: "a2", occurredAt: "2026-11-02T10:00:00Z", actorId: "u1", actorName: "Mariam Al Mansoori", actorNameAr: null, actorKind: "user", action: "delete", changes: [{ field: "name", old: "Old quote.txt" }, { field: "record_id", old: id }] },
            { id: "a1", occurredAt: "2026-11-01T09:30:00Z", actorId: "u1", actorName: "Mariam Al Mansoori", actorNameAr: null, actorKind: "user", action: "insert", changes: [{ field: "name", new: "Trade licence.pdf" }, { field: "record_type", new: "contacts.contacts" }] },
          ];
      return { status: 200, body: { recordType: audit.searchParams.get("type"), items: take === "1" ? history.slice(0, 1) : history, next: null, names: {} } };
    }
    if (url.endsWith("/files/f1/content")) return { status: 200, body: "Delivery note: 12 pallets, signed on receipt.\n" };
    return { status: 404, body: {} };
  });
  return calls;
}

function Form({ type }: { type: string }) {
  const form = useRecordForm<{ id: string; version: number }, { name: string }>({ load: async () => ({ id, version: 1 }), initial: () => ({ name: "" }), save: async () => ({ id, version: 2 }), canEdit: true }, id);
  return (
    <RecordForm form={form} record={{ type, id }} title="Al Noor Foods" panels={[...files.record!, ...audit.record!]}>
      <p>body</p>
    </RecordForm>
  );
}

async function show(language: "en" | "ar" = "en", type = "contacts.contacts") {
  view = await render(
    <I18nProvider initial={language}>
      <SessionProvider>
        <ShortcutProvider>
          <Form type={type} />
        </ShortcutProvider>
      </SessionProvider>
    </I18nProvider>,
  );
  for (let i = 0; i < 4; i++) await settle();
  return view;
}
const press = (code: string, key: string) =>
  act(() => {
    window.dispatchEvent(new KeyboardEvent("keydown", { code, key, altKey: true, bubbles: true }));
  });

describe("the files of a record, away from their section", () => {
  it("shows a paperclip with the count on the toolbar, beside the history button, with its key in the tooltip", async () => {
    serve(["contacts.contacts.read", "contacts.contacts.update", "audit.log.read"]);
    const { container } = await show();
    const toolbar = container.querySelector('[role="toolbar"]')!;
    const clip = toolbar.querySelector<HTMLButtonElement>('[data-record-panel="attachments.files"]')!;
    expect(clip.textContent).toBe("4");
    expect(clip.getAttribute("aria-label")).toBe("4 files: go to the files");
    expect(clip.title).toContain("Alt+A");
    expect(clip.getAttribute("aria-keyshortcuts")).toBe("Alt+A");
    const buttons = [...toolbar.querySelectorAll("button")];
    expect(buttons.indexOf(clip)).toBeLessThan(buttons.indexOf(toolbar.querySelector('[data-record-panel="audit.history"]')!));
  });

  it("lists the newest names under the heading: a click opens a file in the viewer, other kinds are saved, the rest are behind 'more'", async () => {
    serve(["contacts.contacts.read", "contacts.contacts.update", "audit.log.read"]);
    const { container } = await show();
    const names = container.querySelector('[data-testid="attachments-names"]')!;
    expect([...names.querySelectorAll("bdi")].map((n) => n.textContent)).toEqual(["Delivery note.txt", "Trade licence.pdf", "Scans.zip"]);
    expect(names.textContent).toContain("1 more");
    expect(names.querySelector("a[download]")!.getAttribute("href")).toBe("/api/attachments/contacts.contacts/files/f3/content");
    await act(async () => {
      names.querySelector<HTMLButtonElement>('button[aria-label="Preview Trade licence.pdf"]')!.click();
    });
    await settle();
    expect(document.querySelector('[role="dialog"]')!.textContent).toContain("Trade licence.pdf");
    expect(document.querySelector('iframe[title="Trade licence.pdf"]')).not.toBeNull();
  });

  it("Alt+A puts the cursor on the first file of the section, and Enter opens it; a text file is shown as text", async () => {
    const calls = serve(["contacts.contacts.read", "contacts.contacts.update", "audit.log.read"]);
    const { container } = await show();
    press("KeyA", "a");
    await settle();
    const first = container.querySelector('[data-testid="attachments"] tbody tr .button.link')!;
    expect(document.activeElement).toBe(first);
    await act(async () => {
      (first as HTMLButtonElement).click();
    });
    for (let i = 0; i < 3; i++) await settle();
    expect(document.querySelector("pre")!.textContent).toContain("12 pallets");
    expect(calls).toContain("GET /api/attachments/contacts.contacts/files/f1/content");
  });

  it("puts the cursor on Add files when the record has no file yet", async () => {
    serve(["contacts.contacts.read", "contacts.contacts.update"], []);
    const { container } = await show();
    expect(container.querySelector('[data-record-panel="attachments.files"]')!.textContent).toBe("0");
    expect(container.querySelector('[data-testid="attachments-names"]')).toBeNull();
    press("KeyA", "a");
    await settle();
    expect(document.activeElement?.textContent).toBe("Add files");
  });

  it("offers nothing to a reader without the contact's read permission, and no files on a kind of record that takes none", async () => {
    serve(["audit.log.read"]);
    const none = await show();
    expect(none.container.querySelector('[data-record-panel="attachments.files"]')).toBeNull();
    expect(none.container.querySelector('[data-testid="attachments"]')).toBeNull();
    view?.unmount();
    serve(["identity.users.read", "contacts.contacts.read"]);
    const other = await show("en", "identity.users");
    expect(other.container.querySelector('[data-record-panel="attachments.files"]')).toBeNull();
    expect(other.container.querySelector('[data-testid="attachments"]')).toBeNull();
  });

  it("speaks Arabic: the button's name, the count and the section", async () => {
    serve(["contacts.contacts.read", "contacts.contacts.update"]);
    const { container } = await show("ar");
    const clip = container.querySelector('[data-record-panel="attachments.files"]')!;
    expect(clip.getAttribute("aria-label")).toContain("الانتقال إلى الملفات");
    expect(container.querySelector('[data-testid="attachments-names"]')!.textContent).toContain("و1 أخرى");
  });
});

describe("the record's history", () => {
  it("lists the uploads and deletes of its files with the record's own changes, newest first", async () => {
    const calls = serve(["contacts.contacts.read", "contacts.contacts.update", "audit.log.read"]);
    const { container } = await show();
    press("KeyY", "y");
    for (let i = 0; i < 3; i++) await settle();
    const events = [...container.querySelectorAll('[data-testid="audit-history"] .audit-event')];
    expect(events.map((e) => e.querySelector("strong")!.textContent)).toEqual(["Deleted by Mariam Al Mansoori", "Created by Mariam Al Mansoori", "Changed by Mariam Al Mansoori"]);
    expect(events[0]!.querySelector(".audit-child-kind")!.textContent).toBe("Attachment");
    expect(events[2]!.querySelector(".audit-child-kind")).toBeNull();
    // The link to the record is not news: only the file's own fields are listed.
    expect(events[0]!.textContent).toContain("Old quote.txt");
    expect(events[0]!.textContent).not.toContain("Kind of record");
    expect(calls.some((c) => c.includes("type=attachments.files"))).toBe(true);
  });
});

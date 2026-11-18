import { act } from "react";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { I18nProvider } from "../../kernel/i18n";
import { ShortcutProvider } from "../../kernel/shortcuts";
import { render, settle, type Rendered } from "../../test/render";
import { ImportDialog } from "./ImportDialog";
import type { ImportSession } from "./types";

let view: Rendered | undefined;
beforeEach(() => localStorage.clear());
afterEach(() => {
  view?.unmount();
  view = undefined;
});

const fields = {
  items: [
    { key: "name", label: "Name", labelEn: "Name", labelAr: "الاسم", required: false, custom: false, example: null },
    { key: "email", label: "E-mail", labelEn: "E-mail", labelAr: "البريد الإلكتروني", required: false, custom: false, example: null },
    { key: "custom.cfRegion", label: "Region", labelEn: "Region", labelAr: "المنطقة", required: false, custom: true, example: null },
  ],
  oneOfRequired: ["name"], formats: ["csv", "xlsx"], maxFileBytes: 26214400, maxRows: 50000, maxColumns: 80, listPath: "/api/contacts/contacts",
};

const session = (over: Partial<ImportSession> = {}): ImportSession => ({
  id: "s1", recordType: "contacts.contacts", fileName: "contacts.csv", format: "csv", status: "uploaded", rowCount: 2,
  columns: [
    { index: 0, header: "Name", field: "name", source: "name", sample: ["Al Noor", "Sara"] },
    { index: 1, header: "Mail", field: null, source: null, sample: ["a@x.ae"] },
  ],
  rows: [["Al Noor", "a@x.ae"]], onError: "reject", dryRun: false, rowsChecked: 0, rowsValid: 0, rowsImported: 0, rowsFailed: 0, jobId: null, issues: [], failure: null,
  failureCode: null, hasErrorFile: false, missingFields: [], createdAt: "2026-11-18T09:00:00Z", finishedAt: null, version: 1, ...over,
});

type Call = { method: string; url: string; body: unknown };

/** A server that answers the import endpoints; `after` says what each next step answers. */
function serve(steps: { upload?: () => { status: number; body: unknown }; run?: () => { status: number; body: unknown }; poll?: () => ImportSession } = {}) {
  const calls: Call[] = [];
  globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input);
    const method = init?.method ?? "GET";
    const raw = init?.body;
    calls.push({ method, url, body: raw instanceof File ? { file: raw.name } : raw ? JSON.parse(String(raw)) : undefined });
    const reply = (status: number, body: unknown) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
    if (url.endsWith("/fields")) return reply(200, fields);
    if (method === "POST" && url.endsWith("/imports")) {
      const r = steps.upload?.() ?? { status: 201, body: session() };
      return reply(r.status, r.body);
    }
    if (method === "PUT" && url.endsWith("/mapping")) return reply(200, session({ version: 2, columns: [session().columns[0]!, { index: 1, header: "Mail", field: "email", source: "you", sample: ["a@x.ae"] }] }));
    if (method === "POST" && url.endsWith("/run")) {
      const r = steps.run?.() ?? { status: 200, body: session({ status: "completed", rowsImported: 2, rowsValid: 2, rowsChecked: 2 }) };
      return reply(r.status, r.body);
    }
    if (method === "GET" && url.includes("/imports/s1")) return reply(200, steps.poll?.() ?? session({ status: "completed", rowsImported: 2 }));
    return reply(404, {});
  }) as typeof fetch;
  return calls;
}

async function open(onDone: () => void = () => undefined) {
  view = await render(
    <I18nProvider>
      <ShortcutProvider>
        <ImportDialog table="contacts.contacts" titleKey="importexport.kind.contacts" onClose={() => undefined} onDone={onDone} />
      </ShortcutProvider>
    </I18nProvider>,
  );
  await settle();
}

async function choose(name = "contacts.csv") {
  const input = document.querySelector<HTMLInputElement>('[data-testid="import-file"]')!;
  const file = new File(["Name,Mail\nAl Noor,a@x.ae\n"], name, { type: "text/csv" });
  Object.defineProperty(input, "files", { value: [file], configurable: true });
  await act(async () => {
    input.dispatchEvent(new Event("change", { bubbles: true }));
    await new Promise((resolve) => setTimeout(resolve, 0));
  });
}

const button = (text: RegExp) => [...document.querySelectorAll<HTMLButtonElement>("button")].find((b) => text.test(b.textContent ?? ""))!;

describe("ImportDialog", () => {
  it("shows the file chooser first, with the limits and the template", async () => {
    serve();
    await open();
    expect(document.querySelector('[data-testid="import-file"]')).not.toBeNull();
    expect(document.body.textContent).toContain("Import contacts");
    expect(document.body.textContent).toContain("50,000");
    expect(document.querySelector<HTMLAnchorElement>("a[download]")?.getAttribute("href")).toBe("/api/importexport/contacts.contacts/template");
  });

  it("sends the chosen file by its bytes with its name, then shows the columns with the fields they fill", async () => {
    const calls = serve();
    await open();
    await choose();
    const upload = calls.find((c) => c.method === "POST");
    expect(upload?.url).toBe("/api/importexport/contacts.contacts/imports");
    expect(upload?.body).toEqual({ file: "contacts.csv" });
    const rows = document.querySelectorAll('[data-testid="import-mapping"] tbody tr');
    expect(rows).toHaveLength(2);
    expect((rows[0]!.querySelector("select") as HTMLSelectElement).value).toBe("name");
    expect((rows[1]!.querySelector("select") as HTMLSelectElement).value).toBe("");
    expect(document.body.textContent).toContain("Region");
  });

  it("changes the field of a column through the mapping endpoint and imports", async () => {
    const calls = serve();
    let done = 0;
    await open(() => done++);
    await choose();
    const select = document.querySelectorAll('[data-testid="import-mapping"] tbody tr')[1]!.querySelector("select") as HTMLSelectElement;
    await act(async () => {
      select.value = "email";
      select.dispatchEvent(new Event("change", { bubbles: true }));
      await new Promise((resolve) => setTimeout(resolve, 0));
    });
    expect(calls.find((c) => c.method === "PUT")?.body).toEqual({ columns: [{ index: 1, field: "email" }], version: 1 });
    await act(async () => {
      button(/Import 2 rows/).click();
      await new Promise((resolve) => setTimeout(resolve, 0));
    });
    expect(calls.find((c) => c.url.endsWith("/run"))?.body).toEqual({ dryRun: false, onError: "reject" });
    expect(document.querySelector('[data-testid="import-done"]')?.textContent).toContain("Imported 2 rows");
    await act(async () => button(/Close/).click());
    expect(done).toBe(1);
  });

  it("asks for a check without writing and shows the problems with their rows", async () => {
    const calls = serve({
      run: () => ({
        status: 200,
        body: session({ status: "checked", dryRun: true, rowsChecked: 2, rowsValid: 1, rowsFailed: 1, hasErrorFile: true, issues: [{ row: 3, field: "email", fieldLabel: "E-mail", code: "email", message: "Enter a valid e-mail." }] }),
      }),
    });
    await open();
    await choose();
    await act(async () => {
      button(/Check only/).click();
      await new Promise((resolve) => setTimeout(resolve, 0));
    });
    expect(calls.find((c) => c.url.endsWith("/run"))?.body).toMatchObject({ dryRun: true });
    expect(document.querySelector('[data-testid="import-checked"]')?.textContent).toContain("1 have problems");
    expect(document.querySelector('[data-testid="import-issues"]')?.textContent).toContain("Enter a valid e-mail.");
    expect(document.querySelector<HTMLAnchorElement>('[data-testid="import-errors"]')?.getAttribute("href")).toBe("/api/importexport/contacts.contacts/imports/s1/errors");
  });

  it("cannot run a file until the required fields are mapped", async () => {
    serve({ upload: () => ({ status: 201, body: session({ missingFields: ["Name"], columns: [{ index: 0, header: "Mail", field: null, source: null, sample: [] }] }) }) });
    await open();
    await choose();
    expect(document.querySelector('[data-testid="import-missing"]')?.textContent).toContain("Name");
    expect(button(/Import 2 rows/).disabled).toBe(true);
    expect(button(/Check only/).disabled).toBe(true);
  });

  it("shows the server's reason when the file is refused", async () => {
    serve({ upload: () => ({ status: 400, body: { title: "Some fields need attention.", code: "validation", errors: { file: [{ code: "importEmpty", message: "The file is empty." }] } } }) });
    await open();
    await choose("empty.csv");
    expect(document.querySelector('[data-testid="import-problem"]')?.textContent).toBe("The file is empty.");
    expect(document.querySelector('[data-testid="import-file"]')).not.toBeNull();
  });

  it("looks at a file that runs in the background until it ends", async () => {
    serve({ run: () => ({ status: 202, body: session({ status: "running", rowCount: 6000 }) }) });
    await open();
    await choose();
    await act(async () => {
      button(/Import 2 rows|Import 6,000 rows/).click();
      await new Promise((resolve) => setTimeout(resolve, 0));
    });
    expect(document.querySelector('[data-testid="import-running"]')).not.toBeNull();
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 1400));
    });
    expect(document.querySelector('[data-testid="import-done"]')).not.toBeNull();
  });
});

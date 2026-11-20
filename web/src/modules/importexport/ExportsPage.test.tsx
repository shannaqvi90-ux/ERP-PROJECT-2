import { act } from "react";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { I18nProvider } from "../../kernel/i18n";
import { SessionProvider } from "../../kernel/session";
import { ShortcutProvider } from "../../kernel/shortcuts";
import { mockFetch, render, settle, type Rendered } from "../../test/render";
import { ExportsPage } from "./ExportsPage";
import type { TenantExport } from "./types";

let view: Rendered | undefined;
beforeEach(() => localStorage.clear());
afterEach(() => {
  view?.unmount();
  view = undefined;
});

const exportRow = (over: Partial<TenantExport> = {}): TenantExport => ({
  id: "e1", status: "succeeded", format: "csv", jobId: null, fileCount: 7, byteSize: 3145728, recordCounts: { "contacts.contacts": 10 }, failure: null, failureCode: null,
  createdAt: "2026-11-18T09:00:00Z", finishedAt: "2026-11-18T09:01:00Z", expiresAt: "2026-11-25T09:01:00Z", downloadCount: 0, downloadedAt: null, downloadable: true, ...over,
});

const session = (permissions: string[]) => ({
  authenticated: true,
  user: { id: "u1", email: "admin@alnoor.example", displayName: "Mariam", language: "en" },
  tenant: { id: "t1", code: "alnoor", nameEn: "Al Noor Trading LLC", nameAr: "شركة النور" },
  permissions, menu: [], expiresAt: null,
});

async function open(permissions: string[], items: TenantExport[]) {
  const calls = mockFetch((method, url) => {
    if (url === "/api/auth/session") return { status: 200, body: session(permissions) };
    if (method === "GET" && url === "/api/importexport/tenant-exports") return { status: 200, body: { items } };
    if (method === "POST" && url === "/api/importexport/tenant-exports") return { status: 202, body: exportRow({ status: "queued" }) };
    return { status: 404, body: {} };
  });
  view = await render(
    <I18nProvider>
      <SessionProvider>
        <ShortcutProvider>
          <ExportsPage />
        </ShortcutProvider>
      </SessionProvider>
    </I18nProvider>,
  );
  await settle();
  await settle();
  return calls;
}

describe("ExportsPage", () => {
  it("lists the exports with their contents and a download for a ready one", async () => {
    await open(["importexport.exports.read", "importexport.exports.create", "importexport.exports.download"], [exportRow(), exportRow({ id: "e2", status: "expired", downloadable: false })]);
    const rows = document.querySelectorAll('[data-testid="exports-table"] tbody tr');
    expect(rows).toHaveLength(2);
    expect(rows[0]!.textContent).toContain("Ready");
    expect(rows[0]!.textContent).toContain("7 files");
    expect(rows[0]!.querySelector('[data-testid="export-download"]')).not.toBeNull();
    expect(rows[1]!.textContent).toContain("Expired");
    expect(rows[1]!.querySelector('[data-testid="export-download"]')).toBeNull();
  });

  it("starts an export with the chosen format", async () => {
    const calls = await open(["importexport.exports.read", "importexport.exports.create"], []);
    expect(document.body.textContent).toContain("No export has been made yet.");
    const select = document.querySelector("select") as HTMLSelectElement;
    await act(async () => {
      select.value = "json";
      select.dispatchEvent(new Event("change", { bubbles: true }));
    });
    await act(async () => {
      (document.querySelector('[data-testid="export-start"]') as HTMLButtonElement).click();
      await new Promise((resolve) => setTimeout(resolve, 0));
    });
    expect(calls.find((c) => c.method === "POST")?.body).toEqual({ format: "json" });
  });

  it("offers no start to someone who may only read, and no start while one is running", async () => {
    await open(["importexport.exports.read"], [exportRow({ status: "running", downloadable: false })]);
    expect(document.querySelector('[data-testid="export-start"]')).toBeNull();
    view?.unmount();
    await open(["importexport.exports.read", "importexport.exports.create"], [exportRow({ status: "running", downloadable: false })]);
    expect((document.querySelector('[data-testid="export-start"]') as HTMLButtonElement).disabled).toBe(true);
  });
});

import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { mockFetch, render, type Rendered } from "../../test/render";
import { type Call, settleUntilQuiet, sweepKeys, sweepTimeLimit } from "../../test/keySweep";
import { App } from "../shell/App";

// G2 on screen for the full export screen, by keyboard: the whole app is shown to a user who may read the exports and change
// nothing; every key a keyboard has is pressed alone and with every modifier, whatever a key opens is accepted, and every control
// the keyboard reaches is activated: no request other than a read leaves, and no key starts an export or downloads one.
// The control: a user who may start exports finds Alt+N's write.

let view: Rendered | undefined;
let calls: Call[] = [];

beforeEach(() => localStorage.clear());
afterEach(() => closeView());

function closeView() {
  view?.unmount();
  view = undefined;
}

const session = (permissions: string[]) => ({
  authenticated: true,
  user: { id: "u1", email: "viewer@alnoor.example", displayName: "Huda Viewer", language: "en" },
  tenant: { id: "t1", code: "alnoor", nameEn: "Al Noor Trading LLC", nameAr: "شركة النور للتجارة ذ.م.م" },
  permissions,
  menu: [{ key: "importexport.exports", labelKey: "importexport.menu.exports", path: "/importexport/exports", group: "settings" }],
  expiresAt: null,
});

const done = (canDownload: boolean) => ({
  id: "e1", status: "succeeded", format: "csv", jobId: null, fileCount: 7, byteSize: 3145728, recordCounts: { "contacts.contacts": 10 }, failure: null, failureCode: null,
  createdAt: "2026-11-18T09:00:00Z", finishedAt: "2026-11-18T09:01:00Z", expiresAt: "2026-11-25T09:01:00Z", downloadCount: 0, downloadedAt: null, downloadable: canDownload,
});

async function open(permissions: string[]): Promise<Call[]> {
  closeView();
  window.history.replaceState(null, "", "/importexport/exports");
  const canStart = permissions.includes("importexport.exports.create");
  calls = mockFetch((method, url) => {
    if (url === "/api/auth/session") return { status: 200, body: session(permissions) };
    if (method === "POST" && canStart && url === "/api/importexport/tenant-exports") return { status: 202, body: { ...done(false), status: "queued" } };
    if (method !== "GET") return { status: 403, body: { code: "forbidden", title: "Not allowed." } };
    if (url === "/api/importexport/tenant-exports") return { status: 200, body: { items: [done(permissions.includes("importexport.exports.download"))] } };
    return { status: 404, body: {} };
  });
  view = await render(<App language="en" />);
  await settleUntilQuiet(() => calls, () => view?.container.querySelector("main table") != null);
  return calls;
}

async function sweep(permissions: string[], exhaustive = false) {
  await open(permissions);
  expect(view!.container.querySelector("main table"), "the screen shows the exports").not.toBeNull();
  return sweepKeys({
    exhaustive,
    calls,
    shown: () => window.location.pathname === "/importexport/exports" && view?.container.querySelector("main") !== null,
    reopen: () => open(permissions),
    targets: [() => view!.container.querySelector<HTMLElement>("main h1, main table") ?? null],
    states: [],
    forbidden: () => null,
  });
}

describe("the full export screen by keyboard, for a user who may read the exports and change nothing", () => {
  it("no key, and no control the keyboard reaches, sends anything but reads", async () => {
    expect(await sweep(["importexport.exports.read"])).toEqual([]);
  }, sweepTimeLimit);

  it("the control: the same sweep for a user who may start exports finds Alt+N's write", async () => {
    const found = await sweep(["importexport.exports.read", "importexport.exports.create"], true);
    const keys = new Set(found.filter((w) => w.method === "POST" && w.url === "/api/importexport/tenant-exports").map((w) => w.key.split(" ")[0]));
    expect(keys).toContain("Alt+KeyN");
  }, sweepTimeLimit);
});

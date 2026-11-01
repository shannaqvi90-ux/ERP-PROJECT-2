import { act } from "react";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { recordInAddress } from "../../kernel/router";
import { mockFetch, render, type Rendered } from "../../test/render";
import { type Call, settleUntilQuiet, sweepKeys, sweepTimeLimit } from "../../test/keySweep";
import { App } from "../shell/App";
import { failedJob, failedRow, job, jobRow, schedule, scheduleRow, types } from "./testing";

// G2 on screen for the jobs screens, by keyboard: the whole app is shown to a user who may read jobs
// and schedules and change nothing; on each screen, with a record open and on the list alone, and
// again with every row of the list chosen, every key a keyboard has is pressed alone and with every
// modifier, whatever a key opens is accepted, and every control the keyboard reaches is activated:
// no request other than a read leaves (the user's own list views excepted), and no key starts a job,
// runs one again, cancels one or opens a new schedule. The control: a user who may run jobs finds the
// run-again key's write.

let view: Rendered | undefined;
let calls: Call[] = [];

beforeEach(() => localStorage.clear());
afterEach(() => closeView());

function closeView() {
  view?.unmount();
  view = undefined;
}

const reads = ["jobs.jobs.read", "jobs.schedules.read"];

const session = (permissions: string[]) => ({
  authenticated: true,
  user: { id: "u1", email: "viewer@alnoor.example", displayName: "Huda Viewer", language: "en" },
  tenant: { id: "t1", code: "alnoor", nameEn: "Al Noor Trading LLC", nameAr: "شركة النور للتجارة ذ.م.م" },
  permissions,
  menu: [
    { key: "jobs.jobs", labelKey: "jobs.menu.jobs", path: "/jobs/jobs", group: "settings" },
    { key: "jobs.schedules", labelKey: "jobs.menu.schedules", path: "/jobs/schedules", group: "settings" },
  ],
  expiresAt: null,
});

const column = (key: string, labelKey: string, type = "text") => ({
  key, labelKey, type, sortable: true, filterable: true, groupable: false, aggregate: false, hidden: false, choices: [], operators: ["eq", "contains"],
});
const definition = (key: string, endpoint: string, first: string, labelKey: string) => ({
  key, labelKey: `${key}.title`, endpoint, columns: [column(first, labelKey)], searchFields: [first], defaultSort: first, presets: [], canShare: false, maxTake: 200,
});

async function open(path: string, permissions: string[]): Promise<Call[]> {
  closeView();
  window.history.replaceState(null, "", path);
  const canRun = permissions.includes("jobs.jobs.run");
  calls = mockFetch((method, url) => {
    const p = new URL(url, "http://localhost").pathname;
    if (url === "/api/auth/session") return { status: 200, body: session(permissions) };
    if (method === "POST" && canRun && p === "/api/jobs/jobs/j1/rerun") return { status: 201, body: { ...job(), id: "j9" } };
    if (method !== "GET") return { status: 403, body: { code: "forbidden", title: "Not allowed." } };
    if (p === "/api/lists/jobs.jobs/definition") return { status: 200, body: definition("jobs.jobs", "/api/jobs/jobs", "titleEn", "jobs.job.title") };
    if (p === "/api/lists/jobs.schedules/definition") return { status: 200, body: definition("jobs.schedules", "/api/jobs/schedules", "scheduleName", "jobs.schedule.name") };
    if (p.endsWith("/views")) return { status: 200, body: { items: [] } };
    if (p === "/api/jobs/types") return { status: 200, body: types().map((k) => ({ ...k, startable: canRun, schedulable: false })) };
    if (p === "/api/jobs/jobs") return { status: 200, body: { items: [jobRow(), failedRow()], total: 2, next: null } };
    if (p === "/api/jobs/jobs/j1") return { status: 200, body: { ...job(), canRerun: canRun, canDownload: false } };
    if (p === "/api/jobs/jobs/j2") return { status: 200, body: { ...failedJob(), canRerun: false } };
    if (p === "/api/jobs/schedules") return { status: 200, body: { items: [scheduleRow()], total: 1, next: null } };
    if (p === "/api/jobs/schedules/s1") return { status: 200, body: { ...schedule(), canUpdate: false } };
    return { status: 404, body: {} };
  });
  view = await render(<App language="en" />);
  await settleUntilQuiet(() => calls, () => view?.container.querySelector("main h1, main h2, main [role=grid]") != null);
  return calls;
}

const here = () => window.location.pathname + window.location.search;

const grid = () => view?.container.querySelector<HTMLElement>("main [role=grid]") ?? null;

/** Every row of the list chosen, as Ctrl+A on the list does. */
const allRowsChosen = {
  name: "every row chosen",
  enter: () => {
    const g = grid();
    if (!g || view!.container.querySelector(".list-selectionbar")) return;
    g.focus();
    act(() => {
      g.dispatchEvent(new KeyboardEvent("keydown", { bubbles: true, cancelable: true, ctrlKey: true, key: "a", code: "KeyA" }));
    });
  },
};

async function sweep(path: string, permissions = reads, exhaustive = false) {
  await open(path, permissions);
  expect(view!.container.querySelector("main"), `${path} shows its screen`).not.toBeNull();
  const list = !path.includes("open=") && grid() !== null;
  return sweepKeys({
    exhaustive,
    calls,
    shown: () => here() === path && view?.container.querySelector("main") !== null,
    reopen: () => open(path, permissions),
    targets: [() => view!.container.querySelector<HTMLElement>("main h2[tabindex], main h1, main [role=grid], main table") ?? null],
    states: list ? [allRowsChosen] : [],
    forbidden: () => (recordInAddress() === "new" ? "a new schedule offered to a user who may not add one" : null),
  });
}

describe("jobs screens by keyboard, for a user who may read them and change nothing", () => {
  for (const path of ["/jobs/jobs?open=j1", "/jobs/jobs?open=j2", "/jobs/schedules?open=s1", "/jobs/jobs", "/jobs/schedules"]) {
    it(`${path}: no key, and no control the keyboard reaches, sends anything but reads`, async () => {
      expect(await sweep(path)).toEqual([]);
    }, sweepTimeLimit);
  }

  it("the control: the same sweep for a user who may run jobs finds the run-again key's write", async () => {
    const found = await sweep("/jobs/jobs?open=j1", [...reads, "jobs.jobs.run"], true);
    const keys = new Set(found.filter((w) => w.method === "POST" && w.url === "/api/jobs/jobs/j1/rerun").map((w) => w.key.split(" ")[0]));
    expect(keys).toContain("Alt+KeyR");
  }, sweepTimeLimit);
});

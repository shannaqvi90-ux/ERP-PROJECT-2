import { act } from "react";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { extensions } from "../../kernel/extensions";
import { mockFetch, render, setInput, type Rendered } from "../../test/render";

/** Typing into a text area (the shared helper sets one-line inputs). */
function setText(area: HTMLTextAreaElement, value: string) {
  const setter = Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, "value")!.set!;
  act(() => {
    setter.call(area, value);
    area.dispatchEvent(new Event("input", { bubbles: true }));
  });
}
import { settleUntilQuiet, type Call } from "../../test/keySweep";
import { App } from "../shell/App";
import { failedJob, failedRow, job, jobRow, schedule, scheduleRow, types } from "./testing";

let view: Rendered | undefined;
let calls: (Call & { body: unknown })[] = [];

beforeEach(() => localStorage.clear());
afterEach(() => {
  view?.unmount();
  view = undefined;
});

const all = ["jobs.jobs.read", "jobs.jobs.run", "jobs.jobs.cancel", "jobs.jobs.output", "jobs.schedules.read", "jobs.schedules.create", "jobs.schedules.update"];

const session = (permissions: string[], language = "en") => ({
  authenticated: true,
  user: { id: "u1", email: "admin@alnoor.example", displayName: "Mariam Al Mansoori", language, numerals: "latn" },
  tenant: { id: "t1", code: "alnoor", nameEn: "Al Noor Trading LLC", nameAr: "شركة النور للتجارة ذ.م.م" },
  permissions,
  menu: [
    { key: "jobs.jobs", labelKey: "jobs.menu.jobs", path: "/jobs/jobs", group: "settings" },
    { key: "jobs.schedules", labelKey: "jobs.menu.schedules", path: "/jobs/schedules", group: "settings" },
  ],
  expiresAt: null,
});

const column = (key: string, labelKey: string, type: string, extra: object = {}) => ({
  key, labelKey, type, sortable: false, filterable: false, groupable: false, aggregate: false, hidden: false, choices: [], operators: ["eq"], ...extra,
});
const jobsList = {
  key: "jobs.jobs", labelKey: "jobs.jobs.title", endpoint: "/api/jobs/jobs",
  columns: [column("titleEn", "jobs.job.title", "text", { arabicField: "titleAr" }), column("jobStatus", "jobs.job.status", "choice"), column("progressPercent", "jobs.job.progress", "number"),
    column("finishedAt", "jobs.job.finishedAt", "dateTime")],
  searchFields: ["titleEn"], defaultSort: "-createdAt", presets: [], canShare: false, maxTake: 200,
};
const schedulesList = {
  key: "jobs.schedules", labelKey: "jobs.schedules.title", endpoint: "/api/jobs/schedules",
  columns: [column("scheduleName", "jobs.schedule.name", "text"), column("jobType", "jobs.schedule.type", "text"), column("cronTiming", "jobs.schedule.cron", "text"), column("nextRunAt", "jobs.schedule.nextRunAt", "dateTime")],
  searchFields: ["scheduleName"], defaultSort: "scheduleName", presets: [], canShare: false, maxTake: 200,
};

type Reply = { status: number; body?: unknown };

async function open(path: string, permissions = all, override?: (method: string, path: string, body: unknown) => Reply | undefined, language: "en" | "ar" = "en") {
  view?.unmount();
  window.history.replaceState(null, "", path);
  calls = [];
  const recorded = mockFetch((method, url, body) => {
    const p = new URL(url, "http://localhost").pathname;
    const custom = override?.(method, p, body);
    if (custom) return custom;
    if (url === "/api/auth/session") return { status: 200, body: session(permissions, language) };
    if (p === "/api/lists/jobs.jobs/definition") return { status: 200, body: jobsList };
    if (p === "/api/lists/jobs.schedules/definition") return { status: 200, body: schedulesList };
    if (p.endsWith("/views")) return { status: 200, body: { items: [] } };
    if (p === "/api/jobs/types") return { status: 200, body: types() };
    if (p === "/api/jobs/jobs" && method === "GET") return { status: 200, body: { items: [jobRow(), failedRow()], total: 2, next: null } };
    if (p === "/api/jobs/jobs/j1" && method === "GET") return { status: 200, body: job() };
    if (p === "/api/jobs/jobs/j2" && method === "GET") return { status: 200, body: failedJob() };
    if (p === "/api/jobs/jobs" && method === "POST") return { status: 201, body: { ...job(), id: "j9", status: "queued", progress: 0, finishedAt: null, canRerun: false, canCancel: true } };
    if (p === "/api/jobs/jobs/j1/rerun") return { status: 201, body: { ...job(), id: "j9", status: "queued", progress: 0, finishedAt: null, startedAt: null, canRerun: false, canCancel: true } };
    if (p === "/api/jobs/jobs/j9" && method === "GET") return { status: 200, body: { ...job(), id: "j9", status: "queued", progress: 0, finishedAt: null, startedAt: null, canRerun: false, canCancel: true } };
    if (p === "/api/jobs/schedules" && method === "GET") return { status: 200, body: { items: [scheduleRow()], total: 1, next: null } };
    if (p === "/api/jobs/schedules/s1" && method === "GET") return { status: 200, body: schedule() };
    if (p === "/api/jobs/schedules" && method === "POST") return { status: 201, body: { ...schedule(), id: "s2", scheduleName: "Weekly" } };
    return { status: 404, body: {} };
  });
  calls = recorded as typeof calls;
  view = await render(<App language={language} />);
  await settleUntilQuiet(() => calls, () => view?.container.querySelector("main [role=grid]") != null);
}

const cells = () => Array.from(view!.container.querySelectorAll("table[role=grid] tbody tr td")).map((td) => td.textContent ?? "");
const buttonNamed = (name: string) => Array.from(view!.container.querySelectorAll<HTMLButtonElement | HTMLAnchorElement>("button, a.button")).find((b) => b.textContent?.trim() === name);

async function wait(ms = 50) {
  await act(async () => {
    await new Promise((r) => setTimeout(r, ms));
  });
}

describe("background jobs screen", () => {
  it("lists the jobs with their state and a progress bar", async () => {
    await open("/jobs/jobs");
    expect(cells()).toContain("Self-check of the job queue");
    expect(cells()).toContain("Done");
    expect(cells()).toContain("Failed");
    const bars = Array.from(view!.container.querySelectorAll<HTMLElement>("[role=progressbar]"));
    expect(bars.map((b) => b.getAttribute("aria-valuenow"))).toEqual(["100", "40"]);
  });

  it("shows a job's Arabic title on the Arabic list", async () => {
    await open("/jobs/jobs", all, undefined, "ar");
    expect(cells()).toContain("فحص ذاتي لطابور المهام");
    expect(cells()).not.toContain("Self-check of the job queue");
  });

  it("explains that a job form is not edited, beside Run again and Cancel", async () => {
    await open("/jobs/jobs?open=j1");
    await wait();
    const note = view!.container.querySelector(".record-form")?.textContent ?? "";
    expect(note).toContain("Jobs cannot be edited. Use Run again or Cancel.");
  });

  it("opens a job with when it finished, its tries, parameters, result and output", async () => {
    await open("/jobs/jobs?open=j1");
    await wait();
    expect(view!.container.querySelector("[data-testid=job-finished]")?.textContent).not.toBe("Not yet");
    expect(view!.container.querySelectorAll(".jb-tries tbody tr")).toHaveLength(1);
    expect((view!.container.querySelectorAll("textarea.jb-json")[0] as HTMLTextAreaElement | undefined)?.value).toContain('"steps": 5');
    const download = view!.container.querySelector<HTMLAnchorElement>("a.button[href]")!;
    expect(download.getAttribute("href")).toBe("/api/jobs/jobs/j1/output");
    expect(view!.container.querySelector("[data-testid=job-error]")).toBeNull();
  });

  it("shows a failed job's error in the screen's language with its tries", async () => {
    await open("/jobs/jobs?open=j2");
    await wait();
    expect(view!.container.querySelector("[data-testid=job-error]")?.textContent).toContain("unexpected error");
    expect(view!.container.querySelector("[data-testid=job-error]")?.textContent).toContain("jobs.unexpected");
    expect(view!.container.querySelectorAll(".jb-tries tbody tr")).toHaveLength(3);
    expect(buttonNamed("Download output")).toBeUndefined();
  });

  it("speaks Arabic: the job, its state and its error in Arabic", async () => {
    await open("/jobs/jobs?open=j2", all, undefined, "ar");
    await wait();
    expect(view!.container.querySelector("[data-testid=job-error]")?.textContent).toContain("توقفت المهمة");
    expect(view!.container.querySelector(".record-title h2")?.textContent).toContain("فحص ذاتي لطابور المهام");
    expect(view!.container.querySelector(".record .jb-status")?.textContent).toBe("فشلت");
  });

  it("runs a finished job again with one click and opens the new run", async () => {
    await open("/jobs/jobs?open=j1");
    await wait();
    await act(async () => {
      buttonNamed("Run again")!.click();
      await new Promise((r) => setTimeout(r, 0));
    });
    await settleUntilQuiet(() => calls);
    expect(calls.some((c) => c.method === "POST" && c.url === "/api/jobs/jobs/j1/rerun")).toBe(true);
    expect(window.location.search).toContain("open=j9");
  });

  it("starts a job of a chosen kind with its parameters", async () => {
    await open("/jobs/jobs");
    await act(async () => buttonNamed("Start a job")!.click());
    const textarea = document.querySelector<HTMLTextAreaElement>('.jb-start textarea[name="payload"]')!;
    setText(textarea, '{"steps": 3}');
    await act(async () => {
      document.querySelector<HTMLFormElement>(".jb-start form")!.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true }));
      await new Promise((r) => setTimeout(r, 0));
    });
    await settleUntilQuiet(() => calls);
    const post = calls.find((c) => c.method === "POST" && c.url === "/api/jobs/jobs");
    expect(post?.body).toEqual({ type: "jobs.selfCheck", payload: { steps: 3 } });
  });

  it("refuses parameters that are not JSON before sending anything", async () => {
    await open("/jobs/jobs");
    await act(async () => buttonNamed("Start a job")!.click());
    setText(document.querySelector<HTMLTextAreaElement>('.jb-start textarea[name="payload"]')!, "{steps");
    await act(async () => {
      document.querySelector<HTMLFormElement>(".jb-start form")!.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true }));
      await new Promise((r) => setTimeout(r, 0));
    });
    expect(calls.some((c) => c.method === "POST")).toBe(false);
    expect(document.querySelector(".jb-start [role=alert], .jb-start .field-error")?.textContent ?? document.querySelector(".jb-start")?.textContent).toContain("JSON");
  });

  it("starts the job with Ctrl+Enter from the parameters box", async () => {
    await open("/jobs/jobs");
    await act(async () => buttonNamed("Start a job")!.click());
    const textarea = document.querySelector<HTMLTextAreaElement>('.jb-start textarea[name="payload"]')!;
    setText(textarea, '{"steps": 2}');
    await act(async () => {
      textarea.dispatchEvent(new KeyboardEvent("keydown", { key: "Enter", ctrlKey: true, bubbles: true, cancelable: true }));
      await new Promise((r) => setTimeout(r, 0));
    });
    await settleUntilQuiet(() => calls);
    expect(calls.find((c) => c.method === "POST" && c.url === "/api/jobs/jobs")?.body).toEqual({ type: "jobs.selfCheck", payload: { steps: 2 } });
  });

  it("offers a reader no start, no run again and no cancel", async () => {
    await open("/jobs/jobs?open=j1", ["jobs.jobs.read"], (method, p) => {
      if (method === "GET" && p === "/api/jobs/types") return { status: 200, body: types().map((k) => ({ ...k, startable: false })) };
      if (method === "GET" && p === "/api/jobs/jobs/j1") return { status: 200, body: { ...job(), canRerun: false, canDownload: false } };
      return undefined;
    });
    await wait();
    expect(buttonNamed("Start a job")).toBeUndefined();
    expect(buttonNamed("Run again")).toBeUndefined();
    expect(buttonNamed("Cancel")).toBeUndefined();
    expect(buttonNamed("Download output")).toBeUndefined();
  });

  it("cancels a running job and keeps it open", async () => {
    const running = { ...job(), status: "running", finishedAt: null, progress: 30, canRerun: false, canCancel: true, hasOutput: false, canDownload: false };
    await open("/jobs/jobs?open=j1", all, (method, p) => {
      if (method === "GET" && p === "/api/jobs/jobs/j1") return { status: 200, body: running };
      if (method === "POST" && p === "/api/jobs/jobs/j1/cancel") return { status: 200, body: { ...running, cancelRequested: true } };
      return undefined;
    });
    await wait();
    await act(async () => {
      buttonNamed("Cancel")!.click();
      await new Promise((r) => setTimeout(r, 0));
    });
    await settleUntilQuiet(() => calls);
    expect(calls.some((c) => c.method === "POST" && c.url === "/api/jobs/jobs/j1/cancel")).toBe(true);
    expect(buttonNamed("Stopping…")?.hasAttribute("disabled")).toBe(true);
  });
});

describe("job schedules screen", () => {
  it("lists the schedules and shows one with its timing and next run", async () => {
    await open("/jobs/schedules?open=s1");
    await wait();
    expect(cells().some((c) => c.includes("30 3 * * *"))).toBe(true);
    expect(cells()).toContain("Clean up finished jobs"); // the kind's name, not its key
    expect(cells()).not.toContain("jobs.cleanup");
    expect(view!.container.querySelector<HTMLInputElement>('input[name="cron"]')?.value).toBe("30 3 * * *");
    expect(view!.container.querySelector(".jb-facts")?.textContent).toContain("Next run");
  });

  it("adds a schedule: its name, kind, timing and parameters are sent", async () => {
    await open("/jobs/schedules/new");
    await wait();
    const form = view!.container.querySelector("form.record-form")!;
    setInput(form.querySelector<HTMLInputElement>('input[name="scheduleName"]')!, "Weekly");
    const select = form.querySelector<HTMLSelectElement>('select[name="type"]')!;
    await act(async () => {
      select.value = "jobs.cleanup";
      select.dispatchEvent(new Event("change", { bubbles: true }));
    });
    setInput(form.querySelector<HTMLInputElement>('input[name="cron"]')!, "0 4 * * sun");
    setText(form.querySelector<HTMLTextAreaElement>('textarea[name="payload"]')!, '{"retentionDays": 90}');
    await act(async () => {
      form.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true }));
      await new Promise((r) => setTimeout(r, 0));
    });
    await settleUntilQuiet(() => calls);
    const post = calls.find((c) => c.method === "POST" && c.url === "/api/jobs/schedules");
    expect(post?.body).toEqual({ scheduleName: "Weekly", type: "jobs.cleanup", cron: "0 4 * * sun", payload: { retentionDays: 90 }, isEnabled: true });
  });

  it("offers no new schedule to a reader", async () => {
    await open("/jobs/schedules", ["jobs.schedules.read"]);
    expect(buttonNamed("New schedule")).toBeUndefined();
  });
});

describe("the command palette finds jobs", () => {
  it("searches the jobs by what is typed and opens the found job in the list", async () => {
    const source = extensions.palette.find((s) => s.key === "jobs.jobs")!;
    expect(source.permission).toBe("jobs.jobs.read");
    calls = mockFetch((_method, url) => (url.startsWith("/api/jobs/jobs?") ? { status: 200, body: { items: [jobRow()], total: 1, next: null } } : { status: 404, body: {} })) as typeof calls;
    const result = (await source.search("self check", { language: "ar", signal: new AbortController().signal })) as { total: number; items: { title: string; path: string }[] };
    expect(result.items[0]!.title).toBe("فحص ذاتي لطابور المهام");
    expect(result.items[0]!.path).toBe("/jobs/jobs?q=self+check&open=j1");
    expect(new URL(calls[0]!.url, "http://x").searchParams.get("search")).toBe("self check");
  });
});

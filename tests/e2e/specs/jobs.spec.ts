import { expect, test, type Page } from "@playwright/test";
import { checkAccessibility } from "./a11y";
import { freshStart, signIn, users } from "./demo";

/** The rows of the shared list grid (not the tables inside the open record's panel). */
const listRows = (page: Page) => page.locator("table[role=grid] tbody tr");

async function signedIn(page: Page, email: string, language: "en" | "ar" = "en") {
  await freshStart(page, language);
  await signIn(page, email);
  await expect(page.locator("main h1")).toBeVisible();
}

type Job = { id: string; status: string; finishedAt: string | null; attempts: number };

async function startSelfCheck(page: Page, steps: number, pauseMilliseconds: number): Promise<Job> {
  const answer = await page.request.post("/api/jobs/jobs", {
    headers: { "X-Erp-Request": "1" },
    data: { type: "jobs.selfCheck", payload: { steps, pauseMilliseconds } },
  });
  expect(answer.status(), await answer.text()).toBe(201);
  return (await answer.json()) as Job;
}

async function jobNamed(page: Page, id: string): Promise<Job> {
  return (await (await page.request.get(`/api/jobs/jobs/${id}`)).json()) as Job;
}

test.describe("background jobs", () => {
  test("an administrator finds a job by keyboard, sees when it ran, runs it again and watches the new run finish", async ({ page }) => {
    await signedIn(page, users.admin);
    const first = await startSelfCheck(page, 2, 100);
    // The worker (the compose `worker` service) picks it up and finishes it.
    await expect.poll(async () => (await jobNamed(page, first.id)).status, { timeout: 60_000 }).toBe("succeeded");

    // G then J from anywhere.
    await page.locator("main").focus();
    await page.keyboard.press("g");
    await page.keyboard.press("j");
    await expect(page).toHaveURL(/\/jobs\/jobs$/);
    await expect(page.locator("main h1")).toHaveText("Background jobs");
    const search = page.getByRole("searchbox");
    await search.fill("Self-check");
    const row = listRows(page).filter({ hasText: "Self-check of the job queue" }).first();
    await expect(row).toBeVisible();
    await row.click();

    const panel = page.locator(".list-record");
    await expect(panel.getByTestId("job-finished")).not.toHaveText("Not yet");
    await expect(panel.getByRole("button", { name: "Run again" })).toBeVisible();
    await expect(panel.locator("a.button", { hasText: "Download output" })).toHaveAttribute("href", /\/api\/jobs\/jobs\/.+\/output$/);

    // Run it again: the new run opens, goes from waiting to done, and the panel follows it.
    await page.keyboard.press("Alt+r");
    await expect(page).toHaveURL(/open=[0-9a-f-]{36}/);
    await expect(panel.locator(".jb-status")).toHaveText(/Waiting|Running|Done/);
    await expect(panel.locator(".jb-status")).toHaveText("Done", { timeout: 60_000 });
    await expect(panel.getByTestId("job-finished")).not.toHaveText("Not yet");
    await checkAccessibility(page, "the job panel");
  });

  test("a running job shows its progress and is cancelled from the panel", async ({ page }) => {
    await signedIn(page, users.admin);
    const slow = await startSelfCheck(page, 20, 1000);
    await page.goto(`/jobs/jobs?open=${slow.id}`);
    const panel = page.locator(".list-record");
    await expect(panel.locator(".jb-status")).toHaveText("Running", { timeout: 60_000 });
    await expect.poll(async () => Number(await panel.locator("[role=progressbar]").first().getAttribute("aria-valuenow")), { timeout: 60_000 }).toBeGreaterThan(0);
    await panel.getByRole("button", { name: "Cancel" }).click();
    await expect(panel.locator(".jb-status")).toHaveText("Cancelled", { timeout: 60_000 });
    expect((await jobNamed(page, slow.id)).status).toBe("cancelled");
  });

  test("the schedules screen shows the daily clean-up and an administrator adds a schedule by keyboard", async ({ page }) => {
    await signedIn(page, users.admin);
    await page.goto("/jobs/schedules");
    await expect(page.locator("main h1")).toHaveText("Job schedules");
    await expect(listRows(page).filter({ hasText: "30 3 * * *" }).first()).toBeVisible();
    await page.keyboard.press("Alt+n");
    const name = `Weekly clean-up ${Date.now()}`;
    await page.locator('.list-record input[name="name"]').fill(name);
    await page.locator('.list-record select[name="type"]').selectOption("jobs.cleanup");
    await page.locator('.list-record input[name="cron"]').fill("0 4 * * sun");
    await page.keyboard.press("Control+s");
    await expect(page.locator(".list-record [role=status]")).toBeVisible();
    await page.locator('.list-record input[name="cron"]').fill("61 * * * *");
    await page.keyboard.press("Control+s");
    await expect(page.locator(".list-record [role=alert], .list-record .field-error").first()).toBeVisible();
  });

  test("a reader sees the jobs and is offered nothing to start, run again or cancel", async ({ page }) => {
    await signedIn(page, users.viewer);
    await page.goto("/jobs/jobs");
    await expect(page.locator("main h1")).toHaveText("Background jobs");
    await expect(page.getByRole("button", { name: "Start a job" })).toHaveCount(0);
    await listRows(page).first().click();
    await expect(page.getByRole("button", { name: "Run again" })).toHaveCount(0);
    await expect(page.getByRole("button", { name: "Cancel" })).toHaveCount(0);
  });

  test("the screen speaks Arabic and mirrors", async ({ page }) => {
    await signedIn(page, users.adminArabic, "ar");
    await page.goto("/jobs/jobs");
    await expect(page.locator("main h1")).toHaveText("المهام الخلفية");
    await expect(page.locator("html")).toHaveAttribute("dir", "rtl");
    await listRows(page).first().click();
    await expect(page.locator(".list-record .jb-status")).toHaveText(/تمت|فشلت|في الانتظار|قيد التشغيل|أُلغيت/);
  });
});

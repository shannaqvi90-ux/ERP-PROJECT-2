import { expect, request, test, type Page } from "@playwright/test";
import { checkAccessibility } from "./a11y";
import { freshStart, signIn, users } from "./demo";

const png = Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==", "base64");
const pdf = Buffer.from("%PDF-1.4\n1 0 obj\n<< /Type /Catalog >>\nendobj\ntrailer\n<< /Root 1 0 R >>\n%%EOF\n");

const listRows = (page: Page) => page.locator("table[role=grid] tbody tr");
const section = (page: Page) => page.locator('[data-testid="attachments"]');
const files = (page: Page) => section(page).locator("table tbody tr");

/** Opens the demo's Falcon Building Materials contact the way a person finds it: by typing its reference. */
async function openFalcon(page: Page) {
  await page.locator('nav a[href="/contacts/contacts"]').click();
  await expect(page.locator(".list-search input")).toBeFocused();
  await page.keyboard.type("c-demo-1");
  await expect(listRows(page).filter({ hasText: /Falcon Building Materials|فالكون/ }).first()).toBeVisible();
  await page.keyboard.press("Enter");
  await expect(section(page)).toBeVisible();
}

/** The id of the open contact, from the address. */
const contactId = (page: Page) => /\/contacts\/contacts\/([0-9a-f-]{36})/.exec(page.url())?.[1] ?? "";

/** Removes the files the test added (by name prefix), through the API as the screen does. */
async function clean(page: Page, id: string, prefix: string) {
  const list = (await (await page.request.get(`/api/attachments/contacts.contacts/${id}/files`)).json()) as { items: { id: string; name: string }[] };
  for (const file of list.items.filter((f) => f.name.startsWith(prefix))) {
    await page.request.delete(`/api/attachments/contacts.contacts/${id}/files/${file.id}`, { headers: { "X-Erp-Request": "1" } });
  }
}

test.describe("attachments", () => {
  test("several files are attached at once, previewed, downloaded and deleted, from the keyboard", async ({ page }) => {
    await freshStart(page, "en");
    await signIn(page, users.admin);
    await openFalcon(page);
    const id = contactId(page);
    const stem = `e2e-${Date.now() % 1000000}`;
    try {
      await expect(section(page).getByRole("heading")).toContainText("attachment");
      // The whole section is reachable from the keyboard: Tab arrives at Add files.
      const add = section(page).getByRole("button", { name: "Add files", exact: true });
      await page.locator(".record-header h2").focus().catch(() => undefined);
      await add.focus();
      await expect(add).toBeFocused();
      // Several files in one choice.
      await page.locator('[data-testid="attachments"] input[type="file"]').setInputFiles([
        { name: `${stem}-seal.png`, mimeType: "image/png", buffer: png },
        { name: `${stem}-licence.pdf`, mimeType: "application/pdf", buffer: pdf },
      ]);
      await expect(files(page).filter({ hasText: `${stem}-seal.png` })).toHaveCount(1);
      await expect(files(page).filter({ hasText: `${stem}-licence.pdf` })).toHaveCount(1);
      expect(await checkAccessibility(page, "contact form with attachments")).toBeGreaterThan(20);

      // Preview the picture in the page; Escape closes it.
      await section(page).getByRole("button", { name: `Preview ${stem}-seal.png` }).first().click();
      const image = page.locator('[role="dialog"] img');
      await expect(image).toBeVisible();
      await expect.poll(() => image.evaluate((el: HTMLImageElement) => el.naturalWidth)).toBe(1);
      await page.keyboard.press("Escape");
      await expect(page.locator('[role="dialog"]')).toHaveCount(0);

      // Preview the PDF (a frame of this origin).
      await section(page).getByRole("button", { name: `Preview ${stem}-licence.pdf` }).first().click();
      await expect(page.locator('[role="dialog"] iframe')).toHaveAttribute("src", /inline=true/);
      await page.keyboard.press("Escape");

      // Download gives back the very bytes.
      const [download] = await Promise.all([page.waitForEvent("download"), section(page).getByRole("link", { name: `Download ${stem}-licence.pdf` }).click()]);
      expect(download.suggestedFilename()).toBe(`${stem}-licence.pdf`);
      const path = await download.path();
      expect((await import("node:fs")).readFileSync(path!).equals(pdf)).toBe(true);

      // Delete from the keyboard: Enter on the button, the dialog asks, Enter on Delete.
      const del = section(page).getByRole("button", { name: `Delete ${stem}-seal.png` });
      await del.focus();
      await page.keyboard.press("Enter");
      await expect(page.locator('[role="dialog"]')).toContainText(`${stem}-seal.png will be removed`);
      await page.locator('[role="dialog"]').getByRole("button", { name: "Delete", exact: true }).click();
      await expect(files(page).filter({ hasText: `${stem}-seal.png` })).toHaveCount(0);
      await expect(files(page).filter({ hasText: `${stem}-licence.pdf` })).toHaveCount(1);
    } finally {
      await clean(page, id, stem);
    }
  });

  test("files dropped on the section are attached, and a file the server refuses is told why", async ({ page }) => {
    await freshStart(page, "en");
    await signIn(page, users.admin);
    await openFalcon(page);
    const id = contactId(page);
    const stem = `drop-${Date.now() % 1000000}`;
    try {
      const transfer = await page.evaluateHandle(
        ([name, bytes]) => {
          const data = new DataTransfer();
          data.items.add(new File([new Uint8Array(bytes as number[])], name as string, { type: "image/png" }));
          return data;
        },
        [`${stem}.png`, [...png]] as const,
      );
      await section(page).dispatchEvent("dragenter", { dataTransfer: transfer });
      await section(page).dispatchEvent("dragover", { dataTransfer: transfer });
      await expect(section(page)).toHaveClass(/dragging/);
      await section(page).dispatchEvent("drop", { dataTransfer: transfer });
      await expect(files(page).filter({ hasText: `${stem}.png` })).toHaveCount(1);

      // The server checks what it is sent, whatever the screen did: a page named as a PDF, a bad extension, a stranger.
      const refused = await page.request.post(`/api/attachments/contacts.contacts/${id}/files`, { headers: { "X-File-Name": `${stem}.pdf`, "X-Erp-Request": "1", "Content-Type": "application/octet-stream" }, data: Buffer.from("<html>") });
      expect(refused.status()).toBe(415);
      const html = await page.request.post(`/api/attachments/contacts.contacts/${id}/files`, { headers: { "X-File-Name": `${stem}.html`, "X-Erp-Request": "1", "Content-Type": "text/html" }, data: Buffer.from("<html>") });
      expect(html.status()).toBe(415);
      const anonymous = await request.newContext({ baseURL: new URL(page.url()).origin });
      const stranger = await anonymous.get(`/api/attachments/contacts.contacts/${id}/files`);
      expect(stranger.status()).toBe(401);
      await anonymous.dispose();
    } finally {
      await clean(page, id, stem);
    }
  });

  test("in Arabic the section reads right to left with Arabic words and an Arabic name for who added a file", async ({ page }) => {
    await freshStart(page, "ar");
    await signIn(page, users.adminArabic);
    await expect(page.locator("html")).toHaveAttribute("dir", "rtl");
    await openFalcon(page);
    await expect(section(page).getByRole("button", { name: "إضافة ملفات", exact: true })).toBeVisible();
    await expect(section(page).getByRole("heading")).toContainText("مرفق");
    expect(await checkAccessibility(page, "contact attachments in Arabic")).toBeGreaterThan(20);
  });

  test("a reader sees the files and can download them but is offered no Add and no Delete", async ({ page }) => {
    await freshStart(page, "en");
    await signIn(page, users.viewer);
    await openFalcon(page);
    await expect(section(page)).toBeVisible();
    await expect(section(page).getByRole("button", { name: "Add files" })).toHaveCount(0);
    await expect(section(page).getByRole("button", { name: /^Delete / })).toHaveCount(0);
    const id = contactId(page);
    const refused = await page.request.post(`/api/attachments/contacts.contacts/${id}/files`, { headers: { "X-File-Name": "x.pdf", "X-Erp-Request": "1", "Content-Type": "application/octet-stream" }, data: pdf });
    expect(refused.status()).toBe(403);
  });
});

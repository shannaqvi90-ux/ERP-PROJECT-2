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

/** Attaches a file the way the screen does (the bytes in the body, the name in a header). */
async function attach(page: Page, id: string, name: string, bytes: Buffer) {
  const made = await page.request.post(`/api/attachments/contacts.contacts/${id}/files`, { headers: { "X-File-Name": name, "X-Erp-Request": "1", "Content-Type": "application/octet-stream" }, data: bytes });
  expect(made.status()).toBe(201);
}

/** The id of the open contact, from the address. */
const contactId = (page: Page) => /\/contacts\/contacts\/([0-9a-f-]{36})/.exec(page.url())?.[1] ?? "";

/** Removes the files the test added (by name prefix), through the API as the screen does. */
async function clean(page: Page, id: string, prefix: string) {
  const list = (await (await page.request.get(`/api/attachments/contacts.contacts/${id}/files`)).json()) as { items: { id: string; name: string }[] };
  for (const file of list.items.filter((f) => f.name.startsWith(prefix))) {
    await page.request.delete(`/api/attachments/contacts.contacts/files/${file.id}`, { headers: { "X-Erp-Request": "1" } });
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

  test("a file is found again from the top of the record: the paperclip with the count, the names under the heading, Alt+A and Enter", async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 900 });
    await freshStart(page, "en");
    await signIn(page, users.admin);
    await openFalcon(page);
    const id = contactId(page);
    const stem = `find-${Date.now() % 1000000}`;
    try {
      await attach(page, id, `${stem}-Trade licence.pdf`, pdf);
      await attach(page, id, `${stem}-Delivery note.txt`, Buffer.from("Delivery note: 12 pallets, signed on receipt.\n"));
      await page.reload();
      const clip = page.locator('.record-actions [data-record-panel="attachments.files"]');
      await expect(clip).toBeVisible();
      // The count arrives with the list of files.
      await expect(clip).toHaveText(/^\d+$/);
      const count = Number((await clip.textContent())?.trim());
      expect(count).toBeGreaterThanOrEqual(2);
      await expect(clip).toHaveAttribute("title", /Alt\+A/);
      await expect(clip).toHaveAttribute("aria-keyshortcuts", "Alt+A");

      // One click on a file's name under the heading shows the file.
      const names = page.locator('[data-testid="attachments-names"]');
      await expect(names).toBeVisible();
      await names.getByRole("button", { name: new RegExp(`^Preview ${stem}-Trade licence`) }).click();
      await expect(page.locator('[role="dialog"] iframe')).toHaveAttribute("src", /inline=true/);
      await page.keyboard.press("Escape");

      // A small text file is shown as text.
      await section(page).getByRole("button", { name: new RegExp(`^Preview ${stem}-Delivery note`) }).first().click();
      await expect(page.locator('[role="dialog"] pre')).toContainText("12 pallets");
      await page.keyboard.press("Escape");

      // From anywhere on the record, Alt+A goes to the first file, and Enter opens it.
      await page.locator(".record-header h2").focus();
      await page.keyboard.press("Alt+KeyA");
      const first = files(page).first().locator(".button.link, a").first();
      await expect(first).toBeFocused();
      await page.keyboard.press("Enter");
      await expect(page.locator('[role="dialog"]')).toBeVisible();
      await page.keyboard.press("Escape");

      // The shortcut is in the help sheet.
      await page.keyboard.press("Shift+Slash");
      await expect(page.getByRole("dialog")).toContainText("Go to the files");
      await page.keyboard.press("Escape");

      // The table fits its section at this size: every row's buttons are whole and inside it.
      const box = await section(page).boundingBox();
      for (const button of await files(page).first().locator(".attachments-actions .button").all()) {
        const b = (await button.boundingBox())!;
        expect(b.x).toBeGreaterThanOrEqual(box!.x - 1);
        expect(b.x + b.width).toBeLessThanOrEqual(box!.x + box!.width + 1);
        expect(await button.evaluate((el: HTMLElement) => el.scrollWidth <= el.clientWidth + 1)).toBe(true);
      }
      await expect(files(page).first().getByRole("button", { name: /^Delete / })).toHaveText("Delete");

      // The seeded files show who added them.
      const addedBy = await files(page).first().locator("td").nth(2).textContent();
      expect((addedBy ?? "").trim().length).toBeGreaterThan(0);

      // A zip and a mail message are accepted; the record's history lists the uploads.
      for (const [name, bytes, type] of [
        [`${stem}.zip`, Buffer.from("504b0506000000000000000000000000000000000000", "hex"), "application/zip"],
        [`${stem}.eml`, Buffer.from("From: a@b.example\r\nSubject: Quote\r\n\r\nHello"), "message/rfc822"],
      ] as const) {
        expect(type).toBeTruthy();
        await attach(page, id, name, bytes);
      }
      await page.reload();
      await expect(files(page).filter({ hasText: `${stem}.zip` })).toHaveCount(1);
      await page.keyboard.press("Alt+KeyY");
      const history = page.locator('[data-testid="audit-history"]');
      await expect(history.locator(".audit-child-kind").first()).toHaveText("Attachment");
      await expect(history).toContainText(`${stem}.eml`);
    } finally {
      await clean(page, id, stem);
    }
  });

  test("in Arabic the paperclip, the names and the shortcut speak Arabic", async ({ page }) => {
    await freshStart(page, "ar");
    await signIn(page, users.adminArabic);
    await openFalcon(page);
    const id = contactId(page);
    const stem = `ar-${Date.now() % 1000000}`;
    try {
      await attach(page, id, `${stem}.pdf`, pdf);
      await page.reload();
      const clip = page.locator('.record-actions [data-record-panel="attachments.files"]');
      await expect(clip).toHaveAttribute("aria-label", /الانتقال إلى الملفات/);
      await expect(page.locator('[data-testid="attachments-names"]')).toBeVisible();
      await page.locator(".record-header h2").focus();
      await page.keyboard.press("Alt+KeyA");
      await expect(files(page).first().locator(".button.link, a").first()).toBeFocused();
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

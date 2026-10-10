import { expect, test, type Page } from "@playwright/test";
import { freshStart, paceSignIn, password, signIn, users } from "./demo";

/** A user-verifying platform authenticator for the page (Chromium's virtual one): it answers at once. */
async function addDevice(page: Page) {
  const cdp = await page.context().newCDPSession(page);
  await cdp.send("WebAuthn.enable", { enableUI: false });
  const { authenticatorId } = await cdp.send("WebAuthn.addVirtualAuthenticator", {
    options: { protocol: "ctap2", ctap2Version: "ctap2_1", transport: "internal", hasResidentKey: true, hasUserVerification: true, isUserVerified: true, automaticPresenceSimulation: true },
  });
  return { cdp, authenticatorId };
}

async function signOut(page: Page) {
  await page.getByRole("button", { name: /Sign out|تسجيل الخروج/ }).first().click();
  await expect(page.locator('input[name="email"]')).toBeVisible();
}

test.describe("passkeys", () => {
  test("added on My account, a passkey signs in with one confirmation on the device: from the plain, a personal and the team's address, in English and Arabic", async ({ page }) => {
    const { cdp, authenticatorId } = await addDevice(page);
    await freshStart(page, "en");
    await signIn(page, users.viewer);
    await expect(page.getByRole("heading", { name: /Welcome/ })).toBeVisible();
    await page.goto("/identity/me");
    await page.getByRole("button", { name: "Add a passkey", exact: true }).click();
    await expect(page.getByText(/^Passkey .+ added\./)).toBeVisible();
    await expect(page.getByRole("checkbox", { name: "Ask for my passkey as soon as the sign-in screen opens on this device" })).toBeChecked();
    expect((await cdp.send("WebAuthn.getCredentials", { authenticatorId })).credentials).toHaveLength(1);

    // Right after Sign out the screen does not ask (the person just left); its button does.
    await signOut(page);
    await expect(page.locator("#passkey-asking")).toHaveCount(0);
    await paceSignIn(page);
    await page.getByRole("button", { name: "Continue with a passkey" }).click();
    await expect(page.getByRole("heading", { name: /Welcome/ })).toBeVisible();

    // A later visit (a new tab of the device, the next morning) asks as the screen opens: from the
    // plain address, a personal bookmark and the team's address alike.
    for (const address of ["/", "/?email=viewer%40alnoor.example", "/?domain=alnoor.example"]) {
      await signOut(page);
      await page.evaluate(() => sessionStorage.clear());
      await paceSignIn(page);
      await page.goto(address);
      await expect(page.getByRole("heading", { name: /Welcome/ })).toBeVisible();
    }

    // From the Arabic sign-in screen, right to left (the workspace then opens in the person's own
    // language, whatever the screen's was).
    await signOut(page);
    await page.getByRole("button", { name: "العربية" }).click();
    await expect(page.locator("html")).toHaveAttribute("dir", "rtl");
    await expect(page.getByRole("button", { name: "المتابعة بمفتاح مرور" })).toBeVisible();
    await page.evaluate(() => sessionStorage.clear());
    await paceSignIn(page);
    await page.goto("/");
    await expect(page.locator("main h1")).toBeVisible();
  });

  test("a removed passkey no longer signs in, and the screen says so and keeps the password path", async ({ page }) => {
    await addDevice(page);
    await freshStart(page, "en");
    await signIn(page, users.viewer);
    await expect(page.getByRole("heading", { name: /Welcome/ })).toBeVisible();
    await page.goto("/identity/me");
    const section = page.locator("section.id-passkeys");
    await section.locator('input[name="passkey-name"]').fill("Laptop to remove");
    await section.getByRole("button", { name: "Add a passkey", exact: true }).click();
    await expect(page.getByText("Passkey Laptop to remove added.", { exact: false })).toBeVisible();
    // Rename by keyboard (Enter saves), then remove it.
    await section.getByRole("button", { name: "Rename the passkey Laptop to remove" }).click();
    await page.keyboard.press("ControlOrMeta+a");
    await page.keyboard.type("Old laptop");
    await page.keyboard.press("Enter");
    await expect(section.getByRole("cell", { name: "Old laptop" })).toBeVisible();
    await section.getByRole("button", { name: "Remove the passkey Old laptop" }).click();
    await page.getByRole("dialog").getByRole("button", { name: "Remove" }).click();
    await expect(section.getByRole("cell", { name: "Old laptop" })).toHaveCount(0);

    await signOut(page);
    await page.evaluate(() => sessionStorage.clear());
    await paceSignIn(page);
    await page.goto("/");
    await expect(page.getByRole("alert")).toContainText("Sign-in with the passkey did not work");
    await expect(page.locator('input[name="email"]')).toBeFocused();
  });
  // Critic p03 round 8: a lost or stolen device kept signing in after the administrator reset the
  // password and signed the user out everywhere, and no screen showed the user's passkeys.
  test("an administrator stops a lost device: the user's panel lists its passkey and a passkey sign-in, Sign out everywhere removes it, and the device no longer signs in", async ({ page, browser }) => {
    const tag = Math.random().toString(36).slice(2, 8);
    const headers = { "X-Erp-Request": "1" };
    await freshStart(page, "en");
    await signIn(page, users.admin);
    await expect(page.getByRole("button", { name: "Sign out" })).toBeVisible();
    const created = async (url: string, data: object) => {
      const response = await page.request.post(url, { headers, data });
      expect(response.status(), `POST ${url}`).toBe(201);
      return ((await response.json()) as { id: string }).id;
    };
    const roleId = await created("/api/identity/roles", { nameEn: `Own account ${tag}`, nameAr: `الحساب الشخصي ${tag}`, permissions: ["identity.profile.update"] });
    const email = `e2e.field.${tag}@alnoor.example`;
    const userId = await created("/api/identity/users", { email, displayName: `Field Rep ${tag}`, language: "en", password, mustChangePassword: false, roleIds: [roleId] });

    // The field laptop: the user adds a passkey and signs in with it.
    const laptop = await browser.newContext();
    try {
      const device = await laptop.newPage();
      await addDevice(device);
      await freshStart(device, "en");
      await signIn(device, email);
      await expect(device.getByRole("heading", { name: /Welcome/ })).toBeVisible();
      await device.goto("/identity/me");
      const section = device.locator("section.id-passkeys");
      await section.locator('input[name="passkey-name"]').fill("Field laptop");
      await section.getByRole("button", { name: "Add a passkey", exact: true }).click();
      await expect(device.getByText("Passkey Field laptop added.", { exact: false })).toBeVisible();
      await signOut(device);
      await paceSignIn(device);
      await device.getByRole("button", { name: "Continue with a passkey" }).click();
      await expect(device.getByRole("heading", { name: /Welcome/ })).toBeVisible();

      // The administrator sees the passkey and the passkey sign-in on the user's panel.
      await page.goto(`/identity/users/${userId}`);
      const panel = page.locator("aside");
      await expect(panel.locator("section.id-user-passkeys")).toContainText("Field laptop");
      await panel.getByRole("tab", { name: "Sign-in history" }).click();
      await expect(panel.locator(".id-history-table tbody tr").first()).toContainText("Passkey");
      await panel.getByRole("tab", { name: "Details" }).click();

      // Sign out everywhere asks, with the passkeys ticked, and Enter confirms.
      await panel.getByRole("button", { name: "Sign out everywhere…" }).click();
      const dialog = page.getByRole("dialog");
      await expect(dialog).toContainText(`Sign Field Rep ${tag} out everywhere?`);
      await expect(dialog.getByRole("checkbox")).toBeChecked();
      await expect(dialog.getByRole("button", { name: "Sign out everywhere", exact: true })).toBeFocused();
      await page.keyboard.press("Enter");
      await expect(panel.getByText(/1 session ended\. 1 passkey removed\./)).toBeVisible();
      await expect(panel.locator("section.id-user-passkeys")).toContainText("No passkeys");
      const listed = (await (await page.request.get(`/api/identity/users/${userId}/passkeys`)).json()) as unknown[];
      expect(listed).toHaveLength(0);

      // The laptop's session is over, and its passkey no longer signs in.
      await device.goto("/");
      await expect(device.locator('input[name="email"]')).toBeVisible();
      await device.evaluate(() => sessionStorage.clear());
      await paceSignIn(device);
      await device.goto("/");
      await expect(device.getByRole("alert")).toContainText("Sign-in with the passkey did not work");
    } finally {
      await laptop.close();
    }
  });
});

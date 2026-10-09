import { expect, test, type Page } from "@playwright/test";
import { freshStart, paceSignIn, signIn, users } from "./demo";

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
    await page.getByRole("button", { name: "Sign in with a passkey" }).click();
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

    // In Arabic, right to left.
    await signOut(page);
    await page.getByRole("button", { name: "العربية" }).click();
    await page.evaluate(() => sessionStorage.clear());
    await paceSignIn(page);
    await page.goto("/");
    await expect(page.locator("html")).toHaveAttribute("dir", "rtl");
    await expect(page.getByRole("heading", { name: /مرحبًا/ })).toBeVisible();
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
});

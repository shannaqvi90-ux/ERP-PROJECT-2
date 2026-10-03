# Instructions

- Following Playwright test failed.
- Explain why, be concise, respect Playwright best practices.
- Provide a snippet of code with the fix, if possible.

# Test info

- Name: shell.spec.ts >> app shell >> Arabic-Indic digits are a per-user choice that follows the user
- Location: specs/shell.spec.ts:135:7

# Error details

```
Error: expect(locator).toBeFocused() failed

Locator: locator('input[name="email"]')
Expected: focused
Timeout: 10000ms
Error: element(s) not found

Call log:
  - Expect "toBeFocused" locator('input[name="email"]') with timeout 10000ms
  - waiting for locator('input[name="email"]')

```

```yaml
- status
- link "انتقل إلى المحتوى":
  - /url: "#main"
- banner:
  - button "إظهار جزء التنقل أو إخفاؤه" [expanded]
  - link "نظام تخطيط الموارد":
    - /url: /
  - text: شركة النور للتجارة ذ.م.م
  - button "البحث في الشاشات والسجلات والإجراءات Ctrl K"
  - button "English"
  - button "اختصارات لوحة المفاتيح"
  - button "تفضيلات Omar Haddad": Omar Haddad
  - button "تسجيل الخروج"
- navigation "التنقل الرئيسي":
  - group "الإعدادات":
    - text: الإعدادات
    - list:
      - listitem:
        - link "المستخدمون":
          - /url: /identity/users
      - listitem:
        - link "الأدوار":
          - /url: /identity/roles
      - listitem:
        - link "مساحة العمل":
          - /url: /tenancy/tenant
- main:
  - heading "مرحبًا، Omar Haddad" [level=1]
  - paragraph: "افتح قسمًا:"
  - list:
    - listitem:
      - link "المستخدمون":
        - /url: /identity/users
    - listitem:
      - link "الأدوار":
        - /url: /identity/roles
    - listitem:
      - link "مساحة العمل":
        - /url: /tenancy/tenant
- contentinfo: تم تسجيل الدخول باسم Omar Haddad · مساحة العمل alnoor الجلسة صالحة حتى ٠٣‏/١٠‏/٢٠٢٦، ٤:٤٤ م
```

# Test source

```ts
  1  | import { expect, type Page } from "@playwright/test";
  2  | 
  3  | /** Demo sign-ins seeded by `./erp up` (see README). */
  4  | export const password = process.env.ERP_DEMO_PASSWORD ?? "Demo-Pass-2026";
  5  | export const users = {
  6  |   admin: "admin@alnoor.example",
  7  |   adminArabic: "admin.ar@alnoor.example",
  8  |   viewer: "viewer@alnoor.example",
  9  |   noAccess: "noaccess@alnoor.example",
  10 | };
  11 | 
  12 | export async function freshStart(page: Page, language: "en" | "ar" = "en") {
  13 |   await page.goto("/");
  14 |   await page.evaluate((lang) => {
  15 |     localStorage.clear();
  16 |     localStorage.setItem("erp.language", lang);
  17 |   }, language);
  18 |   await page.goto("/");
  19 |   // The sign-in screen is ready for the keyboard: the e-mail field has focus.
  20 |   await expect(page.locator('input[name="email"]')).toBeFocused();
  21 | }
  22 | 
  23 | /** Keyboard-only sign-in: type the e-mail, Tab, type the password, Enter. */
  24 | export async function signIn(page: Page, email: string, secret = password) {
  25 |   const emailField = page.locator('input[name="email"]');
> 26 |   await expect(emailField).toBeFocused();
     |                            ^ Error: expect(locator).toBeFocused() failed
  27 |   await page.keyboard.type(email);
  28 |   await page.keyboard.press("Tab");
  29 |   await page.keyboard.type(secret);
  30 |   await page.keyboard.press("Enter");
  31 | }
  32 | 
```
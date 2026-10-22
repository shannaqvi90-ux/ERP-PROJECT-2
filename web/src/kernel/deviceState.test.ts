import { afterEach, describe, expect, it } from "vitest";
import { deviceKeys, deviceValueKept, forgetIdentity, rememberedEmailKey } from "./deviceState";

/**
 * A Storage whose key order changes whenever a key is removed, as a real browser's may (Chromium
 * keeps no insertion order). Walking `key(i)` while removing skips keys in such a storage: in the
 * e2e G1 journey, after the language was switched twice, Sign out left the person's e-mail on the
 * sign-in screen for the next person (round 13).
 */
class ReorderingStorage implements Storage {
  private entries: [string, string][] = [];
  get length() {
    return this.entries.length;
  }
  key(index: number) {
    return this.entries[index]?.[0] ?? null;
  }
  getItem(key: string) {
    return this.entries.find(([k]) => k === key)?.[1] ?? null;
  }
  setItem(key: string, value: string) {
    const found = this.entries.find(([k]) => k === key);
    if (found) found[1] = String(value);
    else this.entries.push([key, String(value)]);
  }
  removeItem(key: string) {
    this.entries = this.entries.filter(([k]) => k !== key);
    // Reorder what is left: the last key comes first.
    if (this.entries.length > 1) this.entries.unshift(this.entries.pop()!);
  }
  clear() {
    this.entries = [];
  }
}

const original = Object.getOwnPropertyDescriptor(window, "localStorage")!;

afterEach(() => {
  Object.defineProperty(window, "localStorage", original);
});

function useStorage(storage: Storage) {
  Object.defineProperty(window, "localStorage", { configurable: true, get: () => storage });
}

describe("forgetIdentity", () => {
  it("forgets every key of the person however the browser orders the keys as they are removed", async () => {
    const storage = new ReorderingStorage();
    useStorage(storage);
    storage.setItem("erp.language", "ar");
    storage.setItem("erp.numerals", "arab");
    storage.setItem(rememberedEmailKey, "admin@gulfsteel.example");
    for (let i = 0; i < 6; i++) storage.setItem(`erp.recent.user-${i}`, `["/identity/users/${i}"]`);
    storage.setItem("erp.session", "tenant/user");
    storage.setItem("erp.pendingPreferences", '{"userId":"u","changes":{"language":"ar"}}');
    storage.setItem("erp.navOpen", "1");
    await forgetIdentity({ keepEmail: false });
    const left = Array.from({ length: storage.length }, (_, i) => storage.key(i)!).sort();
    expect(left).toEqual(["erp.language", "erp.navOpen", "erp.numerals"]);
  });

  it("keeps the remembered e-mail only when a session ended by itself, and drops a device key holding more than its setting", async () => {
    const storage = new ReorderingStorage();
    useStorage(storage);
    storage.setItem(rememberedEmailKey, "admin@alnoor.example");
    storage.setItem("erp.language", "en|/identity/users/0b9e8a4c-31f2-4d6e-9a57-2c4b8e1f6a30");
    storage.setItem("erp.numerals", "latn");
    storage.setItem("erp.recent.u1", "[]");
    await forgetIdentity({ keepEmail: true });
    const left = Array.from({ length: storage.length }, (_, i) => storage.key(i)!).sort();
    expect(left).toEqual([rememberedEmailKey, "erp.numerals"]);
  });

  it("knows the shape of every device key's setting", () => {
    const settings: Record<string, string> = {
      "erp.language": "ar",
      "erp.numerals": "arab",
      "erp.navOpen": "0",
      "erp.passkeyOffer": "2",
      "erp.signInDomains": '{"salt":"0123456789abcdef0123456789abcdef","hashes":["0123456789abcdef","fedcba9876543210"]}',
    };
    expect([...deviceKeys].sort()).toEqual(Object.keys(settings).sort());
    for (const [key, value] of Object.entries(settings)) {
      expect(deviceValueKept(key, value), `${key}=${value}`).toBe(true);
      expect(deviceValueKept(key, `${value}|/identity/users/1`), `${key} with an address`).toBe(false);
      expect(deviceValueKept(key, `${value} gulfsteel`), `${key} with a code`).toBe(false);
    }
    expect(deviceValueKept("erp.lastPlace", "/")).toBe(false);
  });
});

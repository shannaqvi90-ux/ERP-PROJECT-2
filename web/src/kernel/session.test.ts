import { describe, expect, it } from "vitest";
import { sessionUserName } from "./session";

describe("sessionUserName", () => {
  const user = { displayName: "Fatima Al Zaabi", displayNameAr: "فاطمة الزعابي" };

  it("shows the Arabic name on Arabic screens and the name on English screens", () => {
    expect(sessionUserName(user, "ar")).toBe("فاطمة الزعابي");
    expect(sessionUserName(user, "en")).toBe("Fatima Al Zaabi");
  });

  it("falls back to the name when there is no Arabic name", () => {
    expect(sessionUserName({ displayName: "Omar Haddad", displayNameAr: null }, "ar")).toBe("Omar Haddad");
    expect(sessionUserName({ displayName: "Omar Haddad" }, "ar")).toBe("Omar Haddad");
    expect(sessionUserName({ displayName: "Omar Haddad", displayNameAr: "" }, "ar")).toBe("Omar Haddad");
  });
});

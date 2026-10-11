import { beforeEach, describe, expect, it } from "vitest";
import { completesKnownEmail, completesTeamEmail, deviceKnowsSignInDomains, domainHash, domainOfWholeEmail, fullEmail, keptSignInDomains, localPart, rememberSignInDomain, signInDomainsKey, teamDomain, teamSignInAddress } from "./signInAddress";

describe("team sign-in address", () => {
  it("reads a host name from the address, lower case, and nothing else", () => {
    expect(teamDomain("?domain=Demo-Trading.Example")).toBe("demo-trading.example");
    expect(teamDomain("?domain=localhost")).toBeNull();
    expect(teamDomain("?domain=a..example")).toBeNull();
    expect(teamDomain("?domain=-bad.example")).toBeNull();
    expect(teamDomain("?domain=x.example%2Fpath")).toBeNull();
    expect(teamDomain("")).toBeNull();
  });

  it("completes a local part with the domain and leaves a whole address alone", () => {
    expect(fullEmail(" sara ", "alnoor.example")).toBe("sara@alnoor.example");
    expect(fullEmail("sara@other.example", "alnoor.example")).toBe("sara@other.example");
    expect(fullEmail("", "alnoor.example")).toBe("");
    expect(fullEmail("sara", null)).toBe("sara");
  });

  it("starts the field with the local part only for an e-mail of the team's domain", () => {
    expect(localPart("Sara@ALNOOR.example", "alnoor.example")).toBe("Sara");
    expect(localPart("sara@other.example", "alnoor.example")).toBe("sara@other.example");
    expect(localPart("sara@notalnoor.example", "alnoor.example")).toBe("sara@notalnoor.example");
  });

  it("knows an address is whole only when it ends in exactly the team's domain", () => {
    expect(completesTeamEmail("sara@alnoor.example", "alnoor.example")).toBe(true);
    expect(completesTeamEmail("Sara@ALNOOR.Example", "alnoor.example")).toBe(true);
    // Not yet whole, or another domain: the screen cannot know where it ends.
    expect(completesTeamEmail("sara@alnoor.exampl", "alnoor.example")).toBe(false);
    expect(completesTeamEmail("sara@alnoor.example.ae", "alnoor.example")).toBe(false);
    expect(completesTeamEmail("sara@notalnoor.example", "alnoor.example")).toBe(false);
    expect(completesTeamEmail("sara", "alnoor.example")).toBe(false);
    expect(completesTeamEmail("@alnoor.example", "alnoor.example")).toBe(false);
    expect(completesTeamEmail("a@b@alnoor.example", "alnoor.example")).toBe(false);
    expect(completesTeamEmail("sa ra@alnoor.example", "alnoor.example")).toBe(false);
    // Without the team's address no domain ends the field.
    expect(completesTeamEmail("sara@alnoor.example", null)).toBe(false);
  });

  it("builds the address for a user's e-mail", () => {
    expect(teamSignInAddress("https://erp.example", "sara@Demo-Trading.example")).toBe("https://erp.example/?domain=demo-trading.example");
    expect(teamSignInAddress("https://erp.example", "no-at-sign")).toBe("https://erp.example/");
  });

  it("hashes with 64-bit FNV-1a", () => {
    // The algorithm's published test values.
    expect(domainHash("")).toBe("cbf29ce484222325");
    expect(domainHash("a")).toBe("af63dc4c8601ec8c");
    expect(domainHash("foobar")).toBe("85944171f73967e8");
  });

  it("finds the domain of a whole address only", () => {
    expect(domainOfWholeEmail(" Sara@ALNOOR.Example ")).toBe("alnoor.example");
    expect(domainOfWholeEmail("sara@alnoor")).toBeNull();
    expect(domainOfWholeEmail("sara@alnoor.")).toBeNull();
    expect(domainOfWholeEmail("@alnoor.example")).toBeNull();
    expect(domainOfWholeEmail("a@b@alnoor.example")).toBeNull();
    expect(domainOfWholeEmail("sa ra@alnoor.example")).toBeNull();
  });
});

describe("the sign-in domains this device knows", () => {
  beforeEach(() => localStorage.clear());

  it("knows nothing on a new device", () => {
    expect(deviceKnowsSignInDomains()).toBe(false);
    expect(completesKnownEmail("sara@alnoor.example")).toBe(false);
  });

  it("after a sign-in, a whole address in that domain ends the field, in any letter case, and nothing else does", () => {
    rememberSignInDomain("Admin@Alnoor.Example");
    expect(deviceKnowsSignInDomains()).toBe(true);
    expect(completesKnownEmail("sara@alnoor.example")).toBe(true);
    expect(completesKnownEmail("SARA@ALNOOR.EXAMPLE")).toBe(true);
    expect(completesKnownEmail("sara@alnoor.exampl")).toBe(false);
    expect(completesKnownEmail("sara@alnoor.example.ae")).toBe(false);
    expect(completesKnownEmail("sara@gulfsteel.example")).toBe(false);
    expect(completesKnownEmail("alnoor.example")).toBe(false);
  });

  it("keeps no e-mail and no readable domain: a random salt of the device and salted hashes", () => {
    rememberSignInDomain("admin@alnoor.example");
    rememberSignInDomain("admin@gulfsteel.example");
    const raw = localStorage.getItem(signInDomainsKey)!;
    expect(raw).not.toMatch(/alnoor|gulfsteel|admin|example|@/);
    const stored = JSON.parse(raw) as { salt: string; hashes: string[] };
    expect(stored.salt).toMatch(/^[0-9a-f]{32}$/);
    expect(stored.hashes).toHaveLength(2);
    // Salted: not the plain hash of the domain, so a table of hashes made elsewhere does not read it.
    expect(stored.hashes).not.toContain(domainHash("alnoor.example"));
    expect(stored.hashes[0]).toBe(domainHash(`${stored.salt}\ngulfsteel.example`));
    // Another device has another salt.
    localStorage.clear();
    rememberSignInDomain("admin@alnoor.example");
    expect((JSON.parse(localStorage.getItem(signInDomainsKey)!) as { salt: string }).salt).not.toBe(stored.salt);
  });

  it("keeps the last eight domains, the most recent first, each once", () => {
    for (let i = 1; i <= 10; i++) rememberSignInDomain(`user@company${i}.example`);
    rememberSignInDomain("other@company5.example");
    const stored = JSON.parse(localStorage.getItem(signInDomainsKey)!) as { salt: string; hashes: string[] };
    expect(stored.hashes).toHaveLength(keptSignInDomains);
    expect(stored.hashes[0]).toBe(domainHash(`${stored.salt}\ncompany5.example`));
    expect(completesKnownEmail("x@company10.example")).toBe(true);
    expect(completesKnownEmail("x@company3.example")).toBe(true);
    expect(completesKnownEmail("x@company2.example")).toBe(false);
    expect(completesKnownEmail("x@company1.example")).toBe(false);
  });

  it("ignores a stored value it did not write, and an address that is not whole is never kept", () => {
    localStorage.setItem(signInDomainsKey, JSON.stringify({ salt: "x", hashes: ["alnoor.example"] }));
    expect(deviceKnowsSignInDomains()).toBe(false);
    expect(completesKnownEmail("sara@alnoor.example")).toBe(false);
    localStorage.setItem(signInDomainsKey, "not json");
    expect(deviceKnowsSignInDomains()).toBe(false);
    localStorage.clear();
    rememberSignInDomain("no-at-sign");
    expect(localStorage.getItem(signInDomainsKey)).toBeNull();
  });
});

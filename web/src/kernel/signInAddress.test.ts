import { describe, expect, it } from "vitest";
import { fullEmail, localPart, teamDomain, teamSignInAddress } from "./signInAddress";

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

  it("builds the address for a user's e-mail", () => {
    expect(teamSignInAddress("https://erp.example", "sara@Demo-Trading.example")).toBe("https://erp.example/?domain=demo-trading.example");
    expect(teamSignInAddress("https://erp.example", "no-at-sign")).toBe("https://erp.example/");
  });
});

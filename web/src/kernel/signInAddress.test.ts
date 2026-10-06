import { describe, expect, it } from "vitest";
import { completesTeamEmail, fullEmail, localPart, teamDomain, teamSignInAddress } from "./signInAddress";

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
});

import { describe, expect, it } from "vitest";
import { shownColumns } from "./columnFit";

describe("columns shown beside an open record", () => {
  const minimums = [140, 140, 110, 95, 140, 100]; // name, e-mail, language, status, last sign-in, roles

  it("shows every column when they all fit whole, with no room kept for the marker", () => {
    expect(shownColumns(minimums, 32 + 725, 32, 42)).toBe(6);
    expect(shownColumns(minimums, 2000, 32, 42)).toBe(6);
  });

  it("keeps the leading columns that fit whole and the marker's room, in the user's order", () => {
    // 740 px, 17 short of all six (757): 32 + 42 + 140 + 140 + 110 + 95 + 140 = 699 fits, the
    // roles column (100) does not.
    expect(shownColumns(minimums, 740, 32, 42)).toBe(5);
    // 600 px: 32 + 42 + 140 + 140 + 110 + 95 = 559; the last sign-in column would end at 699.
    expect(shownColumns(minimums, 600, 32, 42)).toBe(4);
    // Exactly the room of four columns and the marker.
    expect(shownColumns(minimums, 559, 32, 42)).toBe(4);
    expect(shownColumns(minimums, 558, 32, 42)).toBe(3);
  });

  it("always shows the first column, and every column before layout", () => {
    expect(shownColumns(minimums, 100, 32, 42)).toBe(1);
    expect(shownColumns(minimums, 0, 32, 42)).toBe(6);
    expect(shownColumns([], 500, 32, 42)).toBe(0);
  });
});

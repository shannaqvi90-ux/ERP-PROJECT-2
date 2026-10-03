import { describe, expect, it } from "vitest";
import {
  byRelevance,
  canMatchAny,
  conditionText,
  filterText,
  initialState,
  parseConditions,
  parseFilter,
  parseSort,
  queryOf,
  rowsToText,
  sortText,
  stateFromAddress,
  stateFromView,
  stateToAddress,
  toggleSort,
  visibleRange,
  type ListDefinition,
} from "./model";
import { localDayStart, nextDay } from "./format";

const definition: ListDefinition = {
  key: "identity.users",
  labelKey: "identity.users.title",
  endpoint: "/api/identity/users",
  columns: [
    { key: "displayName", labelKey: "a", type: "text", sortable: true, filterable: true, groupable: false, aggregate: false, hidden: false, choices: [], operators: ["eq", "contains"] },
    { key: "email", labelKey: "b", type: "text", sortable: true, filterable: true, groupable: false, aggregate: false, hidden: false, choices: [], operators: ["eq"] },
    { key: "language", labelKey: "c", type: "choice", sortable: false, filterable: true, groupable: true, aggregate: false, hidden: false, choices: [{ value: "en", labelKey: "en" }, { value: "ar", labelKey: "ar" }], operators: ["eq", "in"] },
    { key: "isActive", labelKey: "d", type: "boolean", sortable: false, filterable: true, groupable: true, aggregate: false, hidden: false, choices: [], operators: ["eq"] },
    { key: "createdAt", labelKey: "e", type: "dateTime", sortable: true, filterable: true, groupable: false, aggregate: false, hidden: true, choices: [], operators: ["ge", "lt"] },
  ],
  searchFields: ["displayName", "email"],
  defaultSort: "-createdAt",
  presets: [{ key: "active", labelKey: "p", filter: "isActive eq true", sort: null, groupBy: null }],
  canShare: true,
  maxTake: 200,
};

describe("list filter text", () => {
  it("writes conditions in the list filter language, quoting text and doubling quotes", () => {
    expect(conditionText({ column: "displayName", op: "contains", values: ["O'Neil"] })).toBe("displayName contains 'O''Neil'");
    expect(conditionText({ column: "language", op: "in", values: ["en", "ar"] })).toBe("language in ('en', 'ar')");
    expect(conditionText({ column: "isActive", op: "eq", values: [false] })).toBe("isActive eq false");
    expect(conditionText({ column: "createdAt", op: "isNull", values: [] })).toBe("createdAt is null");
    expect(conditionText({ column: "email", op: "startsWith", values: ["admin"] })).toBe("email startswith 'admin'");
  });

  it("joins a view's own filter and header conditions with and", () => {
    expect(filterText({ conditions: [], baseFilter: null })).toBeNull();
    expect(filterText({ conditions: [], baseFilter: "a eq 1 or b eq 2" })).toBe("a eq 1 or b eq 2");
    expect(filterText({ conditions: [{ column: "isActive", op: "eq", values: [true] }], baseFilter: "a eq 1 or b eq 2" })).toBe("(a eq 1 or b eq 2) and isActive eq true");
  });

  it("reads back what the header editors write, and gives up on anything richer", () => {
    const conditions = parseConditions("language in ('en', 'ar') and displayName contains 'O''Neil' and isActive eq true and createdAt is not null and email eq null");
    expect(conditions).toEqual([
      { column: "language", op: "in", values: ["en", "ar"] },
      { column: "displayName", op: "contains", values: ["O'Neil"] },
      { column: "isActive", op: "eq", values: [true] },
      { column: "createdAt", op: "isNotNull", values: [] },
      { column: "email", op: "isNull", values: [] },
    ]);
    expect(parseConditions("a eq 1 or b eq 2")).toBeNull();
    expect(parseConditions("(a eq 1)")).toBeNull();
    expect(parseConditions("not a eq 1")).toBeNull();
    expect(parseConditions("a eq")).toBeNull();
    expect(parseConditions("a eq 'open")).toBeNull();
    expect(parseConditions("")).toEqual([]);
    for (const text of ["language eq 'ar'", "displayName contains 'x' and isActive eq false", "createdAt ge '2026-01-01T00:00:00+04:00'"]) {
      expect(filterText({ conditions: parseConditions(text)!, baseFilter: null })).toBe(text);
    }
  });
});

describe("matching any column's filter", () => {
  const range = [
    { column: "createdAt", op: "ge" as const, values: ["2026-01-01"] },
    { column: "createdAt", op: "le" as const, values: ["2026-02-01"] },
  ];
  const arabic = { column: "language", op: "eq" as const, values: ["ar"] };
  it("joins columns with or and keeps one column's conditions together", () => {
    expect(filterText({ conditions: [arabic, ...range], baseFilter: null, match: "any" })).toBe(
      "language eq 'ar' or (createdAt ge '2026-01-01' and createdAt le '2026-02-01')",
    );
    expect(filterText({ conditions: [arabic, ...range], baseFilter: null, match: "all" })).toBe(
      "language eq 'ar' and createdAt ge '2026-01-01' and createdAt le '2026-02-01'",
    );
    // One column: nothing to choose between.
    expect(filterText({ conditions: range, baseFilter: null, match: "any" })).toBe("createdAt ge '2026-01-01' and createdAt le '2026-02-01'");
    expect(canMatchAny(range)).toBe(false);
    expect(canMatchAny([arabic, ...range])).toBe(true);
    // A view's own filter still holds.
    expect(filterText({ conditions: [arabic, ...range], baseFilter: "isActive eq true or email is null", match: "any" })).toBe(
      "(isActive eq true or email is null) and (language eq 'ar' or (createdAt ge '2026-01-01' and createdAt le '2026-02-01'))",
    );
  });

  it("reads back what it writes, and gives up on anything richer", () => {
    const text = filterText({ conditions: [arabic, ...range], baseFilter: null, match: "any" });
    expect(parseFilter(text)).toEqual({ conditions: [arabic, ...range], match: "any" });
    expect(parseFilter("(language in ('ar', 'en') or isActive eq false)")).toEqual({
      conditions: [{ column: "language", op: "in", values: ["ar", "en"] }, { column: "isActive", op: "eq", values: [false] }],
      match: "any",
    });
    expect(parseFilter("language eq 'ar' and isActive eq true")?.match).toBe("all");
    expect(parseFilter("language eq 'ar' or language eq 'en'")).toBeNull();
    expect(parseFilter("(language eq 'ar' and isActive eq true) or email is null")).toBeNull();
    expect(parseFilter("not language eq 'ar' or email is null")).toBeNull();
    expect(parseFilter("language eq 'ar' or")).toBeNull();
  });

  it("round-trips through the address and views", () => {
    const state = { ...initialState(definition), conditions: [arabic, { column: "isActive", op: "eq" as const, values: [false] }], match: "any" as const };
    const back = stateFromAddress(stateToAddress(state, definition, null), definition).state;
    expect(back.conditions).toEqual(state.conditions);
    expect(back.match).toBe("any");
    expect(back.baseFilter).toBeNull();
    const view = stateFromView(definition, { filter: filterText(state), sort: null, groupBy: null, columns: [] }, "view:3");
    expect(view.match).toBe("any");
    expect(view.conditions).toEqual(state.conditions);
  });
});

describe("list state", () => {
  it("sorts from header clicks: ascending, then descending; Shift adds a second key", () => {
    expect(toggleSort([], "displayName", false)).toEqual([{ column: "displayName", descending: false }]);
    expect(toggleSort([{ column: "displayName", descending: false }], "displayName", false)).toEqual([{ column: "displayName", descending: true }]);
    expect(toggleSort([{ column: "displayName", descending: true }], "displayName", false)).toEqual([{ column: "displayName", descending: false }]);
    expect(toggleSort([{ column: "displayName", descending: false }], "email", true)).toEqual([
      { column: "displayName", descending: false },
      { column: "email", descending: false },
    ]);
    expect(sortText(parseSort("-createdAt,displayName"))).toBe("-createdAt,displayName");
  });

  it("builds the list query and round-trips through the address", () => {
    const state = {
      ...initialState(definition),
      search: " omar ",
      conditions: [{ column: "language", op: "eq" as const, values: ["ar"] }],
      groupBy: "language",
      columns: ["email", "displayName"],
      view: "preset:active",
    };
    // A search without a sort the user chose asks for the best matches first (no sort sent).
    const query = queryOf(state);
    expect(query.get("search")).toBe("omar");
    expect(query.get("filter")).toBe("language eq 'ar'");
    expect(query.has("sort")).toBe(false);
    expect(byRelevance(state)).toBe(true);
    const address = stateToAddress(state, definition, "0190a000-0000-7000-8000-000000000009");
    const back = stateFromAddress(address, definition);
    expect(back.present).toBe(true);
    expect(back.open).toBe("0190a000-0000-7000-8000-000000000009");
    expect(back.state).toEqual({ ...state, search: "omar" });
    // A sort the user chose is sent with the search, and kept in the address even when it is the default.
    const chosen = { ...state, sortChosen: true };
    expect(queryOf(chosen).get("sort")).toBe("-createdAt");
    expect(byRelevance(chosen)).toBe(false);
    const chosenBack = stateFromAddress(stateToAddress(chosen, definition, null), definition);
    expect(chosenBack.state).toEqual({ ...chosen, search: "omar" });
    // Without a search the sort always applies.
    expect(queryOf({ ...state, search: "" }).get("sort")).toBe("-createdAt");
    expect(stateToAddress(initialState(definition), definition, null)).toBe("");
    expect(stateFromAddress("?group=displayName&cols=nosuch", definition).state.groupBy).toBeNull();
  });

  it("opens a view with its own filter, sort and columns", () => {
    const state = stateFromView(definition, { filter: "not isActive eq true or language eq 'ar'", sort: "displayName", groupBy: null, columns: ["email"] }, "view:1");
    expect(state.baseFilter).toBe("not isActive eq true or language eq 'ar'");
    expect(state.conditions).toEqual([]);
    // A filter the header editors can show (columns joined by or) opens as editable conditions.
    const either = stateFromView(definition, { filter: "isActive eq true or language eq 'ar'", sort: null, groupBy: null, columns: [] }, "view:4");
    expect(either.baseFilter).toBeNull();
    expect(either.match).toBe("any");
    expect(either.conditions).toEqual([
      { column: "isActive", op: "eq", values: [true] },
      { column: "language", op: "eq", values: ["ar"] },
    ]);
    expect(state.sort).toEqual([{ column: "displayName", descending: false }]);
    expect(state.columns).toEqual(["email"]);
    expect(stateFromView(definition, { filter: null, sort: null, groupBy: null, columns: [] }, "view:2").columns).toEqual(["displayName", "email", "language", "isActive"]);
  });

  it("renders only the rows in view plus a margin", () => {
    expect(visibleRange(0, 280, 28, 100_000)).toEqual({ start: 0, end: 20 });
    expect(visibleRange(28 * 50_000, 280, 28, 100_000)).toEqual({ start: 49_990, end: 50_020 });
    expect(visibleRange(0, 280, 28, 3)).toEqual({ start: 0, end: 3 });
    expect(visibleRange(10_000, 280, 28, 0)).toEqual({ start: 0, end: 0 });
  });

  it("copies rows as tab-separated text", () => {
    const text = rowsToText([{ id: "1", a: "x\ty", b: "z" }], ["a", "b"], ["A", "B"], (row, c) => String(row[c]));
    expect(text).toBe("A\tB\nx y\tz");
  });

  it("turns calendar days into instants in the browser's time zone", () => {
    expect(localDayStart("2026-10-03")).toMatch(/^2026-10-03T00:00:00[+-]\d\d:\d\d$/);
    expect(nextDay("2026-12-31")).toBe("2027-01-01");
  });
});

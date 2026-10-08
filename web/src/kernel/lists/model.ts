/**
 * The list framework's client model: list definitions served by /api/lists/{key}/definition, the
 * state a list screen is in (search, sort, filters, grouping, columns), and how that state becomes
 * the list query contract every list endpoint accepts (search, filter, sort, after/skip, take,
 * groupBy) and a shareable address.
 */

export type ColumnType = "text" | "number" | "money" | "date" | "dateTime" | "boolean" | "choice" | "reference";

export type Operator = "eq" | "ne" | "lt" | "le" | "gt" | "ge" | "contains" | "startsWith" | "endsWith" | "in" | "isNull" | "isNotNull";

export type ListChoice = { value: string; labelKey: string };

export type ListColumn = {
  key: string;
  labelKey: string;
  type: ColumnType;
  sortable: boolean;
  filterable: boolean;
  groupable: boolean;
  aggregate: boolean;
  hidden: boolean;
  choices: ListChoice[];
  operators: Operator[];
  /** For a money column: the column holding each amount's currency code. */
  currencyField?: string | null;
  /** For a boolean column: what true and false are called (a status column's "Active" and
   * "Inactive"); without them "Yes" and "No". */
  trueLabelKey?: string | null;
  falseLabelKey?: string | null;
};

export type ListPreset = { key: string; labelKey: string; filter: string | null; sort: string | null; groupBy: string | null };

export type ListDefinition = {
  key: string;
  labelKey: string;
  endpoint: string;
  columns: ListColumn[];
  searchFields: string[];
  /** The fields a search word written in Arabic letters matches (the search fields when the list names none). */
  arabicSearchFields?: string[];
  defaultSort: string | null;
  presets: ListPreset[];
  canShare: boolean;
  maxTake: number;
  /** The reports module prints and exports this list (/api/reports/lists/{key}). */
  printable?: boolean;
};

export type SavedView = {
  id: string;
  listKey: string;
  name: string;
  isShared: boolean;
  isDefault: boolean;
  isMine: boolean;
  columns: string[];
  sort: string | null;
  filter: string | null;
  search: string | null;
  groupBy: string | null;
  updatedAt: string;
  version: number;
};

export type Row = Record<string, unknown> & { id: string };

/** A money column's total over a group in one currency (amounts in different currencies are never added). */
export type MoneyTotal = { currency: string | null; amount: string };

export type ListGroup = {
  key: string | number | boolean | null;
  count: number;
  totals: Record<string, string> | null;
  moneyTotals?: Record<string, MoneyTotal[]> | null;
};

export type ListPage = { items: Row[]; total: number; next: string | null; groups: ListGroup[] | null; ranked?: boolean };

export type SortKey = { column: string; descending: boolean };

export type Value = string | number | boolean;

/** One condition the user set from a column header (all conditions must hold). */
export type Condition = { column: string; op: Operator; values: Value[] };

export type ListState = {
  search: string;
  sort: SortKey[];
  conditions: Condition[];
  /** A filter from a view that the header editors cannot show as conditions (or, not, parentheses). */
  baseFilter: string | null;
  groupBy: string | null;
  columns: string[];
  /** The view the state started from: "preset:<key>", "view:<id>" or null. */
  view: string | null;
  /** The user chose the sort (a header or column menu, or a sort in the address). Until then a
   * search lists the best matches first and the sort applies without a search. */
  sortChosen?: boolean;
  /** How header conditions on different columns combine: all must hold (default) or any may.
   * Conditions on one column always all hold (a range is two conditions). */
  match?: "all" | "any";
};

/** A search orders rows by relevance (best match first) unless the user chose a sort. */
export function byRelevance(state: Pick<ListState, "search" | "sortChosen">): boolean {
  return state.search.trim() !== "" && !state.sortChosen;
}

const operatorWords: Record<Exclude<Operator, "in" | "isNull" | "isNotNull">, string> = {
  eq: "eq",
  ne: "ne",
  lt: "lt",
  le: "le",
  gt: "gt",
  ge: "ge",
  contains: "contains",
  startsWith: "startswith",
  endsWith: "endswith",
};

export function quote(text: string): string {
  return `'${text.replace(/'/g, "''")}'`;
}

function literal(value: Value): string {
  if (typeof value === "boolean") return value ? "true" : "false";
  if (typeof value === "number") return String(value);
  return quote(value);
}

export function conditionText(condition: Condition): string {
  const { column, op, values } = condition;
  switch (op) {
    case "isNull":
      return `${column} is null`;
    case "isNotNull":
      return `${column} is not null`;
    case "in":
      return `${column} in (${values.map(literal).join(", ")})`;
    default:
      return `${column} ${operatorWords[op]} ${literal(values[0] ?? "")}`;
  }
}

/** Conditions grouped by column, in the order the columns first appear. */
function byColumn(conditions: Condition[]): Condition[][] {
  const groups = new Map<string, Condition[]>();
  for (const condition of conditions) groups.set(condition.column, [...(groups.get(condition.column) ?? []), condition]);
  return [...groups.values()];
}

/** The header conditions as filter text: all joined by "and", or with "any", each column's
 * conditions (joined by "and") joined by "or". */
function conditionsText(conditions: Condition[], match: "all" | "any" | undefined): string | null {
  if (conditions.length === 0) return null;
  const groups = byColumn(conditions);
  if (match !== "any" || groups.length < 2) return conditions.map(conditionText).join(" and ");
  return groups.map((g) => (g.length > 1 ? `(${g.map(conditionText).join(" and ")})` : conditionText(g[0]!))).join(" or ");
}

/** The filter parameter for the state: the view's own filter and every header condition. */
export function filterText(state: Pick<ListState, "conditions" | "baseFilter" | "match">): string | null {
  const own = conditionsText(state.conditions, state.match);
  const anyOf = state.match === "any" && byColumn(state.conditions).length > 1;
  if (!state.baseFilter) return own;
  if (!own) return state.baseFilter;
  return `(${state.baseFilter}) and ${anyOf ? `(${own})` : own}`;
}

/** True when the header conditions span more than one column (so "all" and "any" differ). */
export function canMatchAny(conditions: Condition[]): boolean {
  return byColumn(conditions).length > 1;
}

export function sortText(sort: SortKey[]): string | null {
  return sort.length > 0 ? sort.map((k) => (k.descending ? "-" : "") + k.column).join(",") : null;
}

export function parseSort(text: string | null | undefined): SortKey[] {
  if (!text) return [];
  return text
    .split(",")
    .map((part) => part.trim())
    .filter((part) => part.length > 0)
    .map((part) => (part.startsWith("-") ? { column: part.slice(1), descending: true } : { column: part, descending: false }));
}

type Token = { kind: "word" | "text" | "number" | "open" | "close" | "comma"; value: string };

function tokenize(text: string): Token[] | null {
  const tokens: Token[] = [];
  let i = 0;
  while (i < text.length) {
    const c = text[i]!;
    if (/\s/.test(c)) {
      i++;
    } else if (c === "(" || c === ")" || c === ",") {
      tokens.push({ kind: c === "(" ? "open" : c === ")" ? "close" : "comma", value: c });
      i++;
    } else if (c === "'") {
      let value = "";
      i++;
      let closed = false;
      while (i < text.length) {
        if (text[i] === "'") {
          if (text[i + 1] === "'") {
            value += "'";
            i += 2;
            continue;
          }
          closed = true;
          i++;
          break;
        }
        value += text[i];
        i++;
      }
      if (!closed) return null;
      tokens.push({ kind: "text", value });
    } else if (/[0-9-]/.test(c)) {
      const match = /^-?\d+(\.\d+)?/.exec(text.slice(i));
      if (!match) return null;
      tokens.push({ kind: "number", value: match[0] });
      i += match[0].length;
    } else if (/[A-Za-z]/.test(c)) {
      const match = /^[A-Za-z][A-Za-z0-9_.]*/.exec(text.slice(i))!;
      tokens.push({ kind: "word", value: match[0] });
      i += match[0].length;
    } else {
      return null;
    }
  }
  return tokens;
}

const wordOperators: Record<string, Operator> = {
  eq: "eq",
  ne: "ne",
  lt: "lt",
  le: "le",
  gt: "gt",
  ge: "ge",
  contains: "contains",
  startswith: "startsWith",
  endswith: "endsWith",
};

function tokensText(tokens: Token[]): string {
  return tokens.map((t) => (t.kind === "text" ? quote(t.value) : t.value)).join(" ");
}

/** Without parentheses that wrap all of the tokens. */
function unwrap(tokens: Token[]): Token[] {
  while (tokens.length >= 2 && tokens[0]!.kind === "open" && tokens[tokens.length - 1]!.kind === "close") {
    let depth = 0;
    let wraps = true;
    for (let i = 0; i < tokens.length; i++) {
      if (tokens[i]!.kind === "open") depth++;
      if (tokens[i]!.kind === "close") depth--;
      if (depth === 0 && i < tokens.length - 1) {
        wraps = false;
        break;
      }
    }
    if (!wraps) break;
    tokens = tokens.slice(1, -1);
  }
  return tokens;
}

/**
 * Read a filter back into header conditions and how they combine: conditions joined by "and", or
 * columns joined by "or" (each column's conditions joined by "and"), which is what the header
 * editors write. Anything richer (not, mixed nesting) returns null and stays a view filter.
 */
export function parseFilter(text: string | null | undefined): { conditions: Condition[]; match: "all" | "any" } | null {
  const all = parseConditions(text);
  if (all) return { conditions: all, match: "all" };
  const tokens = text ? tokenize(text) : null;
  if (!tokens) return null;
  const segments: Token[][] = [[]];
  let depth = 0;
  for (const token of unwrap(tokens)) {
    if (token.kind === "open") depth++;
    if (token.kind === "close") depth--;
    if (depth === 0 && token.kind === "word" && token.value.toLowerCase() === "or") segments.push([]);
    else segments[segments.length - 1]!.push(token);
  }
  if (segments.length < 2) return null;
  const conditions: Condition[] = [];
  const columns = new Set<string>();
  for (const segment of segments) {
    const parsed = segment.length > 0 ? parseConditions(tokensText(unwrap(segment))) : null;
    if (!parsed || parsed.length === 0) return null;
    const column = parsed[0]!.column;
    if (parsed.some((c) => c.column !== column) || columns.has(column)) return null;
    columns.add(column);
    conditions.push(...parsed);
  }
  return { conditions, match: "any" };
}

/**
 * Read a filter back into header conditions when it is a plain list of conditions joined by
 * "and" (what the header editors write). Anything richer (or, not, parentheses) returns null and
 * stays a view filter shown as one chip.
 */
export function parseConditions(text: string | null | undefined): Condition[] | null {
  if (!text || !text.trim()) return [];
  const tokens = tokenize(text);
  if (!tokens) return null;
  const conditions: Condition[] = [];
  let i = 0;
  const value = (token: Token | undefined): Value | undefined => {
    if (!token) return undefined;
    if (token.kind === "text") return token.value;
    if (token.kind === "number") return Number(token.value);
    if (token.kind === "word" && /^(true|false)$/i.test(token.value)) return token.value.toLowerCase() === "true";
    return undefined;
  };
  while (i < tokens.length) {
    const column = tokens[i++];
    const op = tokens[i++];
    if (!column || column.kind !== "word" || !op || op.kind !== "word") return null;
    const word = op.value.toLowerCase();
    if (word === "is") {
      let negated = false;
      if (tokens[i]?.kind === "word" && tokens[i]!.value.toLowerCase() === "not") {
        negated = true;
        i++;
      }
      if (tokens[i]?.kind !== "word" || tokens[i]!.value.toLowerCase() !== "null") return null;
      i++;
      conditions.push({ column: column.value, op: negated ? "isNotNull" : "isNull", values: [] });
    } else if (word === "in") {
      if (tokens[i++]?.kind !== "open") return null;
      const values: Value[] = [];
      for (;;) {
        const v = value(tokens[i++]);
        if (v === undefined) return null;
        values.push(v);
        const next = tokens[i++];
        if (next?.kind === "close") break;
        if (next?.kind !== "comma") return null;
      }
      conditions.push({ column: column.value, op: "in", values });
    } else {
      const operator = wordOperators[word];
      if (!operator) return null;
      const token = tokens[i++];
      if (token?.kind === "word" && token.value.toLowerCase() === "null") {
        if (operator !== "eq" && operator !== "ne") return null;
        conditions.push({ column: column.value, op: operator === "eq" ? "isNull" : "isNotNull", values: [] });
      } else {
        const v = value(token);
        if (v === undefined) return null;
        conditions.push({ column: column.value, op: operator, values: [v] });
      }
    }
    if (i < tokens.length) {
      const joiner = tokens[i++];
      if (joiner?.kind !== "word" || joiner.value.toLowerCase() !== "and" || i >= tokens.length) return null;
    }
  }
  return conditions;
}

/** Visible columns in the definition's default order. */
export function defaultColumns(definition: ListDefinition): string[] {
  return definition.columns.filter((c) => !c.hidden).map((c) => c.key);
}

export function initialState(definition: ListDefinition): ListState {
  return {
    search: "",
    sort: parseSort(definition.defaultSort),
    conditions: [],
    baseFilter: null,
    groupBy: null,
    columns: defaultColumns(definition),
    view: null,
    sortChosen: false,
    match: "all",
  };
}

/** The state a saved view or preset describes, on top of the list's defaults. */
export function stateFromView(
  definition: ListDefinition,
  view: { filter: string | null; sort: string | null; groupBy: string | null; search?: string | null; columns?: string[] | null },
  id: string,
): ListState {
  const parsed = parseFilter(view.filter);
  const known = new Set(definition.columns.map((c) => c.key));
  const columns = (view.columns ?? []).filter((c) => known.has(c));
  return {
    search: view.search ?? "",
    sort: view.sort ? parseSort(view.sort) : parseSort(definition.defaultSort),
    conditions: parsed?.conditions ?? [],
    baseFilter: parsed === null ? view.filter : null,
    match: parsed?.match ?? "all",
    groupBy: view.groupBy,
    columns: columns.length > 0 ? columns : defaultColumns(definition),
    view: id,
    sortChosen: false,
  };
}

/** The list query for a state (without paging). */
export function queryOf(state: ListState): URLSearchParams {
  const query = new URLSearchParams();
  const search = state.search.trim();
  if (search) query.set("search", search);
  const filter = filterText(state);
  if (filter) query.set("filter", filter);
  const sort = sortText(state.sort);
  if (sort && !byRelevance(state)) query.set("sort", sort);
  return query;
}

/** A stable key for the rows a state selects (any change starts loading afresh). */
export function queryKey(state: ListState): string {
  return queryOf(state).toString();
}

/** The address of a list screen in this state, so a link or reload opens the same list. */
export function stateToAddress(state: ListState, definition: ListDefinition, open: string | null): string {
  const params = new URLSearchParams();
  if (state.view) params.set("view", state.view);
  if (state.search.trim()) params.set("q", state.search.trim());
  const filter = filterText(state);
  if (filter) params.set("filter", filter);
  const sort = sortText(state.sort);
  if (sort && (sort !== (definition.defaultSort ?? null) || (state.sortChosen && state.search.trim()))) params.set("sort", sort);
  if (state.groupBy) params.set("group", state.groupBy);
  const columns = state.columns.join(",");
  if (columns !== defaultColumns(definition).join(",")) params.set("cols", columns);
  if (open) params.set("open", open);
  const text = params.toString();
  return text ? `?${text}` : "";
}

/** The search text in an address: ?q=, or ?search= (links that name the search, such as the command palette's). */
export function searchFromAddress(search: string): string {
  const params = new URLSearchParams(search);
  return params.get("q") ?? params.get("search") ?? "";
}

export function stateFromAddress(search: string, definition: ListDefinition): { state: ListState; open: string | null; present: boolean } {
  const params = new URLSearchParams(search);
  const base = initialState(definition);
  const keys = ["view", "q", "search", "filter", "sort", "group", "cols"];
  const present = keys.some((k) => params.has(k));
  const filter = params.get("filter");
  const parsed = parseFilter(filter);
  const known = new Set(definition.columns.map((c) => c.key));
  const columns = (params.get("cols") ?? "").split(",").filter((c) => known.has(c));
  const group = params.get("group");
  return {
    present,
    open: params.get("open"),
    state: {
      search: searchFromAddress(search),
      sort: params.has("sort") ? parseSort(params.get("sort")) : base.sort,
      conditions: parsed?.conditions ?? [],
      baseFilter: parsed === null ? filter : null,
      match: parsed?.match ?? "all",
      groupBy: group && definition.columns.some((c) => c.key === group && c.groupable) ? group : null,
      columns: columns.length > 0 ? columns : base.columns,
      view: params.get("view"),
      sortChosen: params.has("sort"),
    },
  };
}

/** Rows to render for a scroll position: the visible window plus some rows either side. */
export function visibleRange(scrollTop: number, viewportHeight: number, rowHeight: number, total: number, overscan = 10): { start: number; end: number } {
  const first = Math.floor(Math.max(0, scrollTop) / rowHeight);
  const count = Math.ceil(viewportHeight / rowHeight);
  const start = Math.min(total, Math.max(0, first - overscan));
  const end = Math.min(total, first + count + overscan);
  return { start, end: Math.max(start, end) };
}

/** Sort cycle of a header click: ascending, then descending; with Shift the column is added
 * (or flipped) as a further key instead of replacing the sort. */
export function toggleSort(sort: SortKey[], column: string, additive: boolean): SortKey[] {
  const existing = sort.find((k) => k.column === column);
  if (!additive) {
    return [{ column, descending: existing ? !existing.descending && sort[0]?.column === column : false }];
  }
  if (existing) return sort.map((k) => (k.column === column ? { column, descending: !k.descending } : k));
  return [...sort, { column, descending: false }].slice(0, 4);
}

/** Text of rows for the clipboard: a header line and one tab-separated line per row. */
export function rowsToText(rows: Row[], columns: string[], header: string[], cell: (row: Row, column: string) => string): string {
  const clean = (text: string) => text.replace(/[\t\r\n]+/g, " ");
  return [header.map(clean).join("\t"), ...rows.map((row) => columns.map((c) => clean(cell(row, c))).join("\t"))].join("\n");
}

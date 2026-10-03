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
};

export type ListPreset = { key: string; labelKey: string; filter: string | null; sort: string | null; groupBy: string | null };

export type ListDefinition = {
  key: string;
  labelKey: string;
  endpoint: string;
  columns: ListColumn[];
  searchFields: string[];
  defaultSort: string | null;
  presets: ListPreset[];
  canShare: boolean;
  maxTake: number;
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

export type ListGroup = { key: string | number | boolean | null; count: number; totals: Record<string, string> | null };

export type ListPage = { items: Row[]; total: number; next: string | null; groups: ListGroup[] | null };

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
};

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

/** The filter parameter for the state: the view's own filter and every header condition. */
export function filterText(state: Pick<ListState, "conditions" | "baseFilter">): string | null {
  const parts = state.conditions.map(conditionText);
  if (state.baseFilter) parts.unshift(state.conditions.length > 0 ? `(${state.baseFilter})` : state.baseFilter);
  return parts.length > 0 ? parts.join(" and ") : null;
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
  };
}

/** The state a saved view or preset describes, on top of the list's defaults. */
export function stateFromView(
  definition: ListDefinition,
  view: { filter: string | null; sort: string | null; groupBy: string | null; search?: string | null; columns?: string[] | null },
  id: string,
): ListState {
  const conditions = parseConditions(view.filter);
  const known = new Set(definition.columns.map((c) => c.key));
  const columns = (view.columns ?? []).filter((c) => known.has(c));
  return {
    search: view.search ?? "",
    sort: view.sort ? parseSort(view.sort) : parseSort(definition.defaultSort),
    conditions: conditions ?? [],
    baseFilter: conditions === null ? view.filter : null,
    groupBy: view.groupBy,
    columns: columns.length > 0 ? columns : defaultColumns(definition),
    view: id,
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
  if (sort) query.set("sort", sort);
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
  if (sort && sort !== (definition.defaultSort ?? null)) params.set("sort", sort);
  if (state.groupBy) params.set("group", state.groupBy);
  const columns = state.columns.join(",");
  if (columns !== defaultColumns(definition).join(",")) params.set("cols", columns);
  if (open) params.set("open", open);
  const text = params.toString();
  return text ? `?${text}` : "";
}

export function stateFromAddress(search: string, definition: ListDefinition): { state: ListState; open: string | null; present: boolean } {
  const params = new URLSearchParams(search);
  const base = initialState(definition);
  const keys = ["view", "q", "filter", "sort", "group", "cols"];
  const present = keys.some((k) => params.has(k));
  const filter = params.get("filter");
  const conditions = parseConditions(filter);
  const known = new Set(definition.columns.map((c) => c.key));
  const columns = (params.get("cols") ?? "").split(",").filter((c) => known.has(c));
  const group = params.get("group");
  return {
    present,
    open: params.get("open"),
    state: {
      search: params.get("q") ?? "",
      sort: params.has("sort") ? parseSort(params.get("sort")) : base.sort,
      conditions: conditions ?? [],
      baseFilter: conditions === null ? filter : null,
      groupBy: group && definition.columns.some((c) => c.key === group && c.groupable) ? group : null,
      columns: columns.length > 0 ? columns : base.columns,
      view: params.get("view"),
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

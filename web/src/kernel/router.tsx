import { useEffect, useState, type AnchorHTMLAttributes, type ComponentType, type MouseEvent } from "react";
import { confirmLeave } from "./forms/leave";

/** A screen a module contributes. `permission` hides it from users who lack it. */
export type RouteDef = {
  path: string;
  titleKey: string;
  permission?: string;
  component: ComponentType;
};

type RouteModule = { routes: RouteDef[] };

/** Every module's `routes.tsx`, merged; a module adds screens without touching a central list. */
const routeModules = import.meta.glob<RouteModule>("../modules/*/routes.tsx", { eager: true });

export const routes: RouteDef[] = Object.values(routeModules).flatMap((m) => m.routes);

/** The last segment of a record's address: its id (a uuid) or "new" for a record being created. */
export const recordSegment = /^(?:[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}|new)$/i;

/**
 * A path split into its screen and the record it opens: a record of a list screen has its own
 * address, the screen's path and the record's id (`/tenancy/companies/<id>`, `/…/new`), so a link,
 * a bookmark or a reload opens the same record.
 */
export function splitPath(path: string, all: RouteDef[] = routes): { route: RouteDef | undefined; screen: string; record: string | null } {
  const normalized = path.length > 1 ? path.replace(/\/+$/, "") : path;
  const exact = all.find((r) => r.path === normalized);
  if (exact) return { route: exact, screen: normalized, record: null };
  const cut = normalized.lastIndexOf("/");
  const screen = normalized.slice(0, cut);
  const last = normalized.slice(cut + 1);
  const parent = cut > 0 && recordSegment.test(last) ? all.find((r) => r.path === screen) : undefined;
  return parent ? { route: parent, screen, record: last } : { route: undefined, screen: normalized, record: null };
}

export function matchRoute(path: string, all: RouteDef[] = routes): RouteDef | undefined {
  return splitPath(path, all).route;
}

/** The record the current address opens: /screen/<id> or /screen/new (or ?open= from older links). */
export function recordInAddress(): string | null {
  return splitPath(window.location.pathname).record ?? new URLSearchParams(window.location.search).get("open");
}

/** The address of the current screen with one of its records open (or none): /screen/<id>, plus the query. */
export function recordAddress(record: string | null, query: string): string {
  return recordPath(splitPath(window.location.pathname).screen, record, query);
}

/** The address of a screen with one of its records open: /screen/<id>, plus the query. */
export function recordPath(screen: string, record: string | null, query = ""): string {
  const params = new URLSearchParams(query);
  params.delete("open");
  // An id that is not a uuid (a test's "u1") stays in the query, where any text is safe.
  if (record && !recordSegment.test(record)) params.set("open", record);
  const text = params.toString();
  return (record && recordSegment.test(record) ? `${screen}/${record}` : screen) + (text ? `?${text}` : "");
}

const changeEvent = "erp:navigate";

/** Go to an in-app address (path, optionally with a query). A form with unsaved changes on the
 * current screen first asks whether to discard them (kernel/forms/leave); if not, nothing moves. */
export function navigate(path: string): void {
  if (path === window.location.pathname + window.location.search) return;
  if (path.split("?")[0] !== window.location.pathname && !confirmLeave()) return;
  window.history.pushState(null, "", path);
  window.dispatchEvent(new Event(changeEvent));
}

function useLocationPart(read: () => string): string {
  const [value, setValue] = useState(read);
  useEffect(() => {
    const update = () => setValue(read());
    window.addEventListener("popstate", update);
    window.addEventListener(changeEvent, update);
    return () => {
      window.removeEventListener("popstate", update);
      window.removeEventListener(changeEvent, update);
    };
    // `read` is one of the two module-level readers below; it never changes.
  }, []);
  return value;
}

const readPath = () => window.location.pathname;
const readSearch = () => window.location.search;

export function usePath(): string {
  return useLocationPart(readPath);
}

/** One query parameter of the current address ("?search=…"), kept current as the address changes. */
export function useSearchParam(name: string): string | null {
  const search = useLocationPart(readSearch);
  return new URLSearchParams(search).get(name);
}

/** An in-app link: a real anchor (opens in a new tab with a modifier) that navigates without reloading. */
export function Link({ to, onClick, ...rest }: { to: string } & AnchorHTMLAttributes<HTMLAnchorElement>) {
  const handle = (event: MouseEvent<HTMLAnchorElement>) => {
    onClick?.(event);
    if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
    event.preventDefault();
    navigate(to);
  };
  return <a href={to} onClick={handle} {...rest} />;
}

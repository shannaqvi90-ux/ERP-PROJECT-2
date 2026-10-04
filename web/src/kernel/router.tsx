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

export function matchRoute(path: string, all: RouteDef[] = routes): RouteDef | undefined {
  const normalized = path.length > 1 ? path.replace(/\/+$/, "") : path;
  return all.find((r) => r.path === normalized);
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

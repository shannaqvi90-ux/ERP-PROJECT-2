import { useEffect, useState, type AnchorHTMLAttributes, type ComponentType, type MouseEvent } from "react";

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

export function navigate(path: string): void {
  if (path === window.location.pathname) return;
  window.history.pushState(null, "", path);
  window.dispatchEvent(new Event(changeEvent));
}

export function usePath(): string {
  const [path, setPath] = useState(() => window.location.pathname);
  useEffect(() => {
    const update = () => setPath(window.location.pathname);
    window.addEventListener("popstate", update);
    window.addEventListener(changeEvent, update);
    return () => {
      window.removeEventListener("popstate", update);
      window.removeEventListener(changeEvent, update);
    };
  }, []);
  return path;
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

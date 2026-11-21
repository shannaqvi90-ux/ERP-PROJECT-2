import { useEffect, useId, useState, useSyncExternalStore, type ReactNode } from "react";
import { identityScoped } from "./deviceState";
import { useI18n } from "./i18n";

/**
 * Small shared parts of the design system: a tooltip, loading skeletons, toasts and a sparkline.
 * Colours and sizes come from design/tokens.css; every part works in both directions and every
 * theme, and none of them takes keyboard focus away from where the person is working.
 */

/** A tooltip on hover and on keyboard focus of its child, for anything that has no visible name
 * (an icon button) or whose name is cut. Escape hides it. The text is also the child's accessible
 * description, so a screen reader reads it too. */
export function Tooltip({ text, children }: { text: string; children: ReactNode }) {
  const id = useId();
  const [shown, setShown] = useState(false);
  useEffect(() => {
    if (!shown) return;
    const hide = (e: KeyboardEvent) => e.key === "Escape" && setShown(false);
    document.addEventListener("keydown", hide);
    return () => document.removeEventListener("keydown", hide);
  }, [shown]);
  return (
    <span className="tooltip-host" onMouseEnter={() => setShown(true)} onMouseLeave={() => setShown(false)} onFocus={() => setShown(true)} onBlur={() => setShown(false)} aria-describedby={shown ? id : undefined}>
      {children}
      {shown && (
        <span role="tooltip" id={id} className="tooltip-bubble">
          {text}
        </span>
      )}
    </span>
  );
}

/** A grey bar where a value is still loading; it carries no text, the region says it is busy. */
export function Skeleton({ width = "100%", height = "1em", className }: { width?: string; height?: string; className?: string }) {
  return <span className={`skeleton${className ? ` ${className}` : ""}`} style={{ inlineSize: width, blockSize: height }} aria-hidden="true" />;
}

/** Rows of skeleton for a list or a pane while it loads. */
export function SkeletonRows({ rows = 6, label }: { rows?: number; label: string }) {
  return (
    <div className="skeleton-rows" role="status" aria-busy="true" aria-label={label}>
      {Array.from({ length: rows }, (_, i) => (
        <Skeleton key={i} height="1.1rem" width={`${96 - ((i * 13) % 40)}%`} />
      ))}
    </div>
  );
}

export type ToastKind = "info" | "success" | "error";
type ToastItem = { id: number; message: string; kind: ToastKind };

// Held in identity-scoped maps, so what a toast said ends with the person who saw it.
const store = identityScoped<"toasts", ToastItem[]>();
const counter = identityScoped<"toast" | "listener", number>();
const listeners = identityScoped<number, () => void>();
const current = (): ToastItem[] => {
  let items = store.get("toasts");
  if (!items) {
    items = [];
    store.set("toasts", items);
  }
  return items;
};
const setToasts = (next: ToastItem[]) => {
  store.set("toasts", next);
  listeners.forEach((l) => l());
};

/** Show a short message (already in the person's language) that goes by itself after a few
 * seconds; an error stays longer. Returns a function that removes it. */
export function toast(message: string, kind: ToastKind = "info"): () => void {
  const id = (counter.get("toast") ?? 1);
  counter.set("toast", id + 1);
  setToasts([...current().slice(-3), { id, message, kind }]);
  const remove = () => setToasts(current().filter((x) => x.id !== id));
  window.setTimeout(remove, kind === "error" ? 8000 : 4000);
  return remove;
}

export function clearToasts(): void {
  setToasts([]);
}

/** Where toasts appear (once, in the shell). A live region, so a screen reader announces each. */
export function ToastHost() {
  const { t } = useI18n();
  const items = useSyncExternalStore(
    (l) => {
      const key = counter.get("listener") ?? 1;
      counter.set("listener", key + 1);
      listeners.set(key, l);
      return () => void listeners.delete(key);
    },
    current,
  );
  return (
    <div className="toast-host" role="region" aria-label={t("shell.toasts")} aria-live="polite">
      {items.map((x) => (
        <div key={x.id} className="toast" data-kind={x.kind} role={x.kind === "error" ? "alert" : "status"}>
          <span className="toast-text">{x.message}</span>
          <button type="button" className="toast-close" aria-label={t("shell.toast.dismiss")} onClick={() => setToasts(current().filter((y) => y.id !== x.id))}>
            ×
          </button>
        </div>
      ))}
    </div>
  );
}

/**
 * An area sparkline: a soft filled area under a line, with a dot at its end, drawn from `points`
 * (oldest first). It mirrors in right-to-left, so time runs from the right. `label` is its
 * accessible text (what it shows and its end value); with fewer than two points it draws nothing.
 */
export function Sparkline({ points, label, width = 96, height = 28 }: { points: readonly number[]; label: string; width?: number; height?: number }) {
  if (points.length < 2) return null;
  const min = Math.min(...points);
  const max = Math.max(...points);
  const span = max - min || 1;
  const pad = 3;
  const x = (i: number) => pad + (i * (width - 2 * pad)) / (points.length - 1);
  const y = (v: number) => height - pad - ((v - min) / span) * (height - 2 * pad);
  const n = (v: number) => Math.round(v * 10) / 10;
  const line = points.map((v, i) => `${i === 0 ? "M" : "L"}${n(x(i))} ${n(y(v))}`).join(" ");
  const area = `${line} L${n(x(points.length - 1))} ${height - pad} L${n(x(0))} ${height - pad} Z`;
  return (
    <svg className="sparkline" width={width} height={height} viewBox={`0 0 ${width} ${height}`} role="img" aria-label={label}>
      <title>{label}</title>
      <path d={area} className="sparkline-area" />
      <path d={line} className="sparkline-line" fill="none" />
      <circle cx={x(points.length - 1)} cy={y(points[points.length - 1]!)} r="2.2" className="sparkline-dot" />
    </svg>
  );
}

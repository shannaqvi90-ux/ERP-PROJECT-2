import { createContext, useContext, useEffect, useState, type ReactNode } from "react";
import { Skeleton, Sparkline } from "./ui";
import { useSessionIfAny } from "./session";
import { api } from "./api";
import { useI18n } from "./i18n";
import { Link } from "./router";

/**
 * The home screen is a dashboard of what needs doing. Each module adds cards (extensions `home`);
 * a card shows figures read from the module's own list endpoints (the total of a list narrowed by
 * a filter, one row asked for) and leads to the screen where the work is. A figure marked
 * `attention` counts toward the "things need you" line at the top when it is above zero.
 */
export type HomeFigure = {
  key: string;
  labelKey: string;
  /** The list endpoint the figure counts (its `total`). */
  endpoint: string;
  /** The list's filter text; none counts every row. */
  filter?: string;
  /** Where the figure leads. */
  path: string;
  /** Above zero, this is work waiting for the user. */
  attention?: boolean;
  /** What the user must hold to do something about it: without it the figure is shown but does
   * not count as work waiting for this user (a read-only viewer is not told to fix failed jobs). */
  actPermission?: string;
  /** Draw the figure's last months as an area sparkline: new rows per month by this date column. */
  trendColumn?: string;
};

/** What the dashboard is told about a figure that needs the user: its count and where it leads. */
export type AttentionMeta = { labelKey: string; path: string };
export const AttentionContext = createContext<(key: string, count: number, meta?: AttentionMeta) => void>(() => {});

const monthStart = (back: number) => {
  const now = new Date();
  return new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth() - back, 1)).toISOString().slice(0, 10);
};

/** New rows per month for the last `months` months (oldest first), from the list's own total, one
 * row asked for per month. Null while loading or when the list cannot be read. */
export function useMonthlyTrend(endpoint: string, filter: string | undefined, column: string | undefined, months = 12): number[] | null {
  const [points, setPoints] = useState<number[] | null>(null);
  useEffect(() => {
    if (!column) return;
    const controller = new AbortController();
    const asks = Array.from({ length: months }, (_, i) => {
      const from = monthStart(months - 1 - i);
      const to = monthStart(months - 2 - i);
      const range = `${column} ge '${from}' and ${column} lt '${to}'`;
      const params = new URLSearchParams({ take: "1", filter: filter ? `${filter} and ${range}` : range });
      return api<{ total: number }>("GET", `${endpoint}?${params}`, undefined, { signal: controller.signal }).then((p) => p.total);
    });
    Promise.all(asks).then(setPoints, () => setPoints(null));
    return () => controller.abort();
  }, [endpoint, filter, column, months]);
  return points;
}

/** The total of a list narrowed by a filter; null while it loads or when it cannot be read. */
export function useCount(endpoint: string, filter?: string): number | null {
  const [count, setCount] = useState<number | null>(null);
  useEffect(() => {
    const controller = new AbortController();
    const params = new URLSearchParams({ take: "1" });
    if (filter) params.set("filter", filter);
    api<{ total: number }>("GET", `${endpoint}?${params}`, undefined, { signal: controller.signal })
      .then((page) => setCount(page.total))
      .catch(() => setCount(null));
    return () => controller.abort();
  }, [endpoint, filter]);
  return count;
}

function Figure({ figure }: { figure: HomeFigure }) {
  const { t, format } = useI18n();
  const count = useCount(figure.endpoint, figure.filter);
  const trend = useMonthlyTrend(figure.endpoint, figure.filter, figure.trendColumn);
  const report = useContext(AttentionContext);
  const session = useSessionIfAny();
  const mayAct = !figure.actPermission || Boolean(session?.can(figure.actPermission));
  useEffect(() => {
    if (figure.attention && count !== null) report(figure.key, mayAct ? count : 0, { labelKey: figure.labelKey, path: figure.path });
  }, [count, figure.attention, figure.key, figure.labelKey, figure.path, mayAct, report]);
  const urgent = figure.attention && mayAct && (count ?? 0) > 0;
  return (
    <li className="home-figure" data-attention={urgent || undefined}>
      <Link to={figure.path} className="home-figure-link">
        <span className="home-figure-label">{t(figure.labelKey)}</span>
        <span className="home-figure-count">{count === null ? <Skeleton width="3.5rem" height="1.1rem" /> : format.number(count)}</span>
      </Link>
      {trend && trend.some((n) => n > 0) && <Sparkline points={trend} label={t("shell.home.trend", { label: t(figure.labelKey), last: format.number(trend[trend.length - 1] ?? 0), months: trend.length })} />}
    </li>
  );
}

/** One card: a title, its figures, and the action it leads to. */
export function HomeCard({ titleKey, figures, action }: { titleKey: string; figures: HomeFigure[]; action?: { labelKey: string; path: string } }): ReactNode {
  const { t } = useI18n();
  return (
    <section className="home-card" aria-label={t(titleKey)}>
      <h2 className="home-card-title">{t(titleKey)}</h2>
      <ul className="home-figures">
        {figures.map((f) => (
          <Figure key={f.key} figure={f} />
        ))}
      </ul>
      {action && (
        <Link to={action.path} className="button home-card-action">
          {t(action.labelKey)}
        </Link>
      )}
    </section>
  );
}

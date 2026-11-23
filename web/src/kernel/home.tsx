import { createContext, useContext, useEffect, useState, type ReactNode } from "react";
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
};

export const AttentionContext = createContext<(key: string, count: number) => void>(() => {});

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
  const report = useContext(AttentionContext);
  useEffect(() => {
    if (figure.attention && count !== null) report(figure.key, count);
  }, [count, figure.attention, figure.key, report]);
  const urgent = figure.attention && (count ?? 0) > 0;
  return (
    <li className="home-figure" data-attention={urgent || undefined}>
      <Link to={figure.path} className="home-figure-link">
        <span className="home-figure-label">{t(figure.labelKey)}</span>
        <span className="home-figure-count">{count === null ? "–" : format.number(count)}</span>
      </Link>
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

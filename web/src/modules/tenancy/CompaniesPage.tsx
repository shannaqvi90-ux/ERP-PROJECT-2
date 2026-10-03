import { useCallback, useEffect, useRef, useState } from "react";
import { api } from "../../kernel/api";
import { useI18n } from "../../kernel/i18n";
import { useSession } from "../../kernel/session";
import { CompanyForm } from "./CompanyForm";
import type { CompanyRow, Page } from "./types";
import { gridKeys, readSelection, useLocalName, useScreenKeys, writeSelection } from "./ui";

const pageSize = 50;

/**
 * Companies of the workspace (those the user may work in): a dense list on the start side and
 * the selected company's form beside it. Keyboard: "/" search, arrows move, Enter opens, Alt+N
 * new, Ctrl+S save, Escape close.
 */
export function CompaniesPage() {
  const { t, formatNumber } = useI18n();
  const { can } = useSession();
  const name = useLocalName();
  const [search, setSearch] = useState("");
  const [showInactive, setShowInactive] = useState(false);
  const [page, setPage] = useState<Page<CompanyRow> | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [selected, setSelected] = useState<string | null>(() => readSelection().id);
  const [creating, setCreating] = useState(() => readSelection().isNew && can("tenancy.companies.create"));
  const [reload, setReload] = useState(0);
  const searchRef = useRef<HTMLInputElement>(null);
  const gridRef = useRef<HTMLTableSectionElement>(null);

  useEffect(() => {
    const controller = new AbortController();
    const timer = window.setTimeout(() => {
      const query = new URLSearchParams({ take: String(pageSize) });
      if (search.trim()) query.set("search", search.trim());
      if (!showInactive) query.set("isActive", "true");
      api<Page<CompanyRow>>("GET", `/api/tenancy/companies?${query}`)
        .then((p) => {
          if (!controller.signal.aborted) {
            setPage(p);
            setError(null);
          }
        })
        .catch((e: Error) => setError(e.message));
    }, 150);
    return () => {
      controller.abort();
      window.clearTimeout(timer);
    };
  }, [search, showInactive, reload]);

  const open = useCallback((id: string | null, isNew = false) => {
    setSelected(id);
    setCreating(isNew);
    writeSelection(id, isNew);
  }, []);

  const startNew = can("tenancy.companies.create") ? () => open(null, true) : undefined;
  useScreenKeys({ onNew: startNew, search: searchRef });

  const rows = page?.items ?? [];
  const onSaved = (id: string) => {
    open(id);
    setReload((n) => n + 1);
  };

  return (
    <section className="split">
      <div className="split-list">
        <div className="screen-header">
          <h1>{t("tenancy.companies.title")}</h1>
          {startNew && (
            <button type="button" className="button primary" onClick={startNew} title={t("tenancy.common.newHint")} aria-keyshortcuts="Alt+N">
              {t("tenancy.common.new")}
            </button>
          )}
        </div>
        <div className="toolbar">
          <input
            ref={searchRef}
            type="search"
            className="search"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            onKeyDown={(e) => {
              const first = rows[0];
              if (e.key === "ArrowDown" && first) {
                e.preventDefault();
                setSelected(first.id);
                gridRef.current?.focus();
              }
            }}
            placeholder={t("tenancy.companies.search")}
            aria-label={t("tenancy.companies.search")}
            aria-keyshortcuts="/"
          />
          <label className="check">
            <input type="checkbox" checked={showInactive} onChange={(e) => setShowInactive(e.target.checked)} />
            <span>{t("tenancy.common.showInactive")}</span>
          </label>
          {page && <span className="muted">{t("tenancy.companies.count", { count: page.total })}</span>}
        </div>
        {error && (
          <div className="alert" role="alert">
            {error}
          </div>
        )}
        <table className="grid selectable" aria-label={t("tenancy.companies.title")}>
          <thead>
            <tr>
              <th scope="col">{t("tenancy.company.code")}</th>
              <th scope="col">{t("tenancy.company.legalName")}</th>
              <th scope="col">{t("tenancy.company.baseCurrency")}</th>
              <th scope="col">{t("tenancy.company.city")}</th>
              <th scope="col" className="num">{t("tenancy.company.branchCount")}</th>
              <th scope="col">{t("tenancy.common.status")}</th>
            </tr>
          </thead>
          <tbody ref={gridRef} tabIndex={0} onKeyDown={gridKeys(rows, selected, setSelected, (id) => open(id))}>
            {rows.map((c) => (
              <tr
                key={c.id}
                aria-selected={c.id === selected}
                className={c.isActive ? undefined : "inactive"}
                onClick={() => open(c.id)}
                data-id={c.id}
              >
                <td dir="ltr">{c.code}</td>
                <td>{name(c.legalNameEn, c.legalNameAr)}</td>
                <td dir="ltr">{c.baseCurrency}</td>
                <td>{c.city}</td>
                <td className="num">{formatNumber(c.branchCount)}</td>
                <td>{c.isActive ? t("tenancy.common.active") : t("tenancy.common.inactive")}</td>
              </tr>
            ))}
          </tbody>
        </table>
        {page && page.items.length === 0 && <p className="muted">{t("tenancy.companies.none")}</p>}
      </div>
      {(selected || creating) && (
        <div className="split-form">
          <CompanyForm
            key={creating ? "new" : selected}
            id={creating ? null : selected}
            onSaved={onSaved}
            onClose={() => {
              open(null);
              gridRef.current?.focus();
            }}
          />
        </div>
      )}
    </section>
  );
}

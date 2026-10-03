import { useCallback, useEffect, useRef, useState, type FormEvent } from "react";
import { api } from "../../kernel/api";
import { useI18n } from "../../kernel/i18n";
import { useSession } from "../../kernel/session";
import type { AccessRow, CompanyAccess, Page, UserAccess } from "./types";
import { gridKeys, problemOf, readSelection, useLocalName, useScreenKeys, writeSelection } from "./ui";

const pageSize = 50;

/**
 * Who may work in which company and branch: users on the start side (search by name or e-mail),
 * the selected user's companies and branches beside them. Only the administrator's own companies
 * appear; access to other companies is neither shown nor changed.
 */
export function AccessPage() {
  const { t, formatNumber } = useI18n();
  const [search, setSearch] = useState("");
  const [skip, setSkip] = useState(0);
  const [page, setPage] = useState<Page<AccessRow> | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [selected, setSelected] = useState<string | null>(() => readSelection().id);
  const [reload, setReload] = useState(0);
  const searchRef = useRef<HTMLInputElement>(null);
  const gridRef = useRef<HTMLTableSectionElement>(null);

  useEffect(() => {
    const controller = new AbortController();
    const timer = window.setTimeout(() => {
      const query = new URLSearchParams({ skip: String(skip), take: String(pageSize) });
      if (search.trim()) query.set("search", search.trim());
      api<Page<AccessRow>>("GET", `/api/tenancy/access?${query}`)
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
  }, [search, skip, reload]);

  const open = useCallback((id: string | null) => {
    setSelected(id);
    writeSelection(id);
  }, []);
  useScreenKeys({ search: searchRef });
  const rows = page?.items ?? [];

  return (
    <section className="split">
      <div className="split-list">
        <div className="screen-header">
          <h1>{t("tenancy.access.title")}</h1>
        </div>
        <div className="toolbar">
          <input
            ref={searchRef}
            type="search"
            className="search"
            value={search}
            onChange={(e) => {
              setSearch(e.target.value);
              setSkip(0);
            }}
            placeholder={t("tenancy.access.search")}
            aria-label={t("tenancy.access.search")}
            aria-keyshortcuts="/"
          />
          {page && <span className="muted">{t("tenancy.access.count", { count: page.total })}</span>}
        </div>
        {error && (
          <div className="alert" role="alert">
            {error}
          </div>
        )}
        <table className="grid selectable" aria-label={t("tenancy.access.title")}>
          <thead>
            <tr>
              <th scope="col">{t("tenancy.access.user")}</th>
              <th scope="col">{t("tenancy.access.email")}</th>
              <th scope="col">{t("tenancy.access.companies")}</th>
            </tr>
          </thead>
          <tbody ref={gridRef} tabIndex={0} onKeyDown={gridKeys(rows, selected, setSelected, open)}>
            {rows.map((u) => (
              <tr key={u.id} role="row" aria-selected={u.id === selected} onClick={() => open(u.id)} data-id={u.id}>
                <td>{u.displayName}</td>
                <td dir="ltr">{u.email}</td>
                <td dir="ltr">
                  {u.companies.length === 0
                    ? "—"
                    : u.companies.map((c) => (c.allBranches ? c.code : `${c.code} (${formatNumber(c.branchCount)})`)).join(", ")}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
        {page && page.total > pageSize && (
          <div className="pager">
            <button type="button" className="button" disabled={skip === 0} onClick={() => setSkip(Math.max(0, skip - pageSize))}>
              {t("tenancy.pager.previous")}
            </button>
            <span className="muted">
              {t("tenancy.pager.range", {
                from: formatNumber(skip + 1),
                to: formatNumber(Math.min(skip + pageSize, page.total)),
                total: formatNumber(page.total),
              })}
            </span>
            <button type="button" className="button" disabled={skip + pageSize >= page.total} onClick={() => setSkip(skip + pageSize)}>
              {t("tenancy.pager.next")}
            </button>
          </div>
        )}
      </div>
      {selected && (
        <div className="split-form">
          <AccessForm
            key={selected}
            userId={selected}
            onSaved={() => setReload((n) => n + 1)}
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

function AccessForm({ userId, onSaved, onClose }: { userId: string; onSaved: () => void; onClose: () => void }) {
  const { t } = useI18n();
  const { can } = useSession();
  const name = useLocalName();
  const [access, setAccess] = useState<UserAccess | null>(null);
  const [draft, setDraft] = useState<CompanyAccess[]>([]);
  const [message, setMessage] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    api<UserAccess>("GET", `/api/tenancy/access/${userId}`)
      .then((a) => {
        setAccess(a);
        setDraft(a.companies);
      })
      .catch((e) => setMessage(problemOf(e).message));
  }, [userId]);

  const editable = can("tenancy.access.update") && access !== null && !access.isCaller;
  const entry = (companyId: string) => draft.find((d) => d.companyId === companyId);
  const toggleCompany = (companyId: string, on: boolean) => {
    setSaved(false);
    setDraft((d) => (on ? [...d, { companyId, allBranches: true, branchIds: [] }] : d.filter((x) => x.companyId !== companyId)));
  };
  const setAll = (companyId: string, all: boolean) => {
    setSaved(false);
    setDraft((d) => d.map((x) => (x.companyId === companyId ? { ...x, allBranches: all, branchIds: all ? [] : x.branchIds } : x)));
  };
  const toggleBranch = (companyId: string, branchId: string, on: boolean) => {
    setSaved(false);
    setDraft((d) =>
      d.map((x) =>
        x.companyId === companyId
          ? { ...x, branchIds: on ? [...x.branchIds, branchId] : x.branchIds.filter((b) => b !== branchId) }
          : x,
      ),
    );
  };

  const save = async (event?: FormEvent) => {
    event?.preventDefault();
    if (!editable || busy) return;
    setBusy(true);
    setMessage(null);
    try {
      const result = await api<UserAccess>("PUT", `/api/tenancy/access/${userId}`, { companies: draft });
      setAccess(result);
      setDraft(result.companies);
      setSaved(true);
      onSaved();
    } catch (error) {
      const problem = problemOf(error);
      setMessage([problem.message, ...Object.values(problem.fields).flat().map((f) => f.message)].join(" "));
    } finally {
      setBusy(false);
    }
  };

  useScreenKeys({ onSave: () => void save(), onClose });

  return (
    <form className="record-form" onSubmit={save} aria-label={t("tenancy.access.form")}>
      <div className="record-header">
        <h2>{access ? access.displayName : ""}</h2>
        <div className="record-actions">
          {editable && (
            <button type="submit" className="button primary" disabled={busy} title={t("tenancy.common.saveHint")} aria-keyshortcuts="Control+S">
              {busy ? t("tenancy.common.saving") : t("tenancy.common.save")}
            </button>
          )}
          <button type="button" className="button" onClick={onClose} title={t("tenancy.common.closeHint")} aria-keyshortcuts="Escape">
            {t("tenancy.common.close")}
          </button>
        </div>
      </div>
      {access && (
        <p className="muted" dir="ltr">
          {access.email}
        </p>
      )}
      {access?.isCaller && <div className="notice">{t("tenancy.access.ownAccess")}</div>}
      {message && (
        <div className="alert" role="alert">
          {message}
        </div>
      )}
      {saved && (
        <div className="notice" role="status">
          {t("tenancy.common.saved")}
        </div>
      )}
      {access && access.options.length === 0 && <p className="muted">{t("tenancy.access.noCompanies")}</p>}
      <fieldset disabled={!editable} className="access-list">
        <legend>{t("tenancy.access.companies")}</legend>
        {access?.options.map((company) => {
          const current = entry(company.id);
          return (
            <div key={company.id} className="access-company" data-company={company.code}>
              <label className="check">
                <input type="checkbox" checked={Boolean(current)} onChange={(e) => toggleCompany(company.id, e.target.checked)} />
                <span>
                  <span dir="ltr">{company.code}</span> · {name(company.legalNameEn, company.legalNameAr)}
                  {!company.isActive && <span className="muted"> ({t("tenancy.common.inactive")})</span>}
                </span>
              </label>
              {current && (
                <div className="access-branches">
                  <label className="check">
                    <input type="checkbox" checked={current.allBranches} onChange={(e) => setAll(company.id, e.target.checked)} />
                    <span>{t("tenancy.access.allBranches")}</span>
                  </label>
                  {!current.allBranches &&
                    company.branches.map((b) => (
                      <label key={b.id} className="check">
                        <input type="checkbox" checked={current.branchIds.includes(b.id)} onChange={(e) => toggleBranch(company.id, b.id, e.target.checked)} />
                        <span>
                          <span dir="ltr">{b.code}</span> · {name(b.nameEn, b.nameAr)}
                        </span>
                      </label>
                    ))}
                </div>
              )}
            </div>
          );
        })}
      </fieldset>
    </form>
  );
}

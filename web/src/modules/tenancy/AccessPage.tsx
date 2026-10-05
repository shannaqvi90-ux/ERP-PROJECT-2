import { useEffect, useState, type FormEvent } from "react";
import { api, ApiError } from "../../kernel/api";
import { useI18n } from "../../kernel/i18n";
import { ListView } from "../../kernel/lists/ListView";
import { useSession } from "../../kernel/session";
import { listSearch, useRecordPanel } from "./records";
import type { AccessCompanySummary, CompanyAccess, UserAccess } from "./types";
import { problemOf, useLocalName, useScreenKeys } from "./ui";

/**
 * Who may work in which company and branch: the workspace's users in the shared list (search by
 * name or e-mail, filters, sort, views), each with the companies they may work in, and the open
 * user's companies and branches in the list's panel (?open=id). Only the administrator's own
 * companies appear; access to other companies is neither shown nor changed.
 */
export function AccessPage() {
  const { formatNumber, language } = useI18n();
  const { can } = useSession();
  const panel = useRecordPanel(false);
  useScreenKeys({ search: listSearch });

  return (
    <div className="tn-list">
      <ListView
        listKey="tenancy.access"
        titleKey="tenancy.access.title"
        countKey="tenancy.access.count"
        searchPlaceholderKey="tenancy.access.search"
        can={can}
        openOnClick
        reloadKey={panel.reload}
        openId={panel.openId}
        onOpenIdChange={panel.onOpenIdChange}
        renderRecord={(id, close) => <AccessForm key={id} userId={id} onSaved={() => panel.saved(id)} onClose={close} />}
        renderCell={{
          email: (u) => <span dir="ltr">{String(u.email ?? "")}</span>,
          companies: (u) => {
            const companies = Array.isArray(u.companies) ? (u.companies as AccessCompanySummary[]) : [];
            return companies.length === 0 ? (
              <span className="muted">—</span>
            ) : (
              <span dir="ltr">
                {companies.map((c) => (c.allBranches ? c.code : `${c.code} (${formatNumber(c.branchCount)})`)).join(language === "ar" ? "، " : ", ")}
              </span>
            );
          },
        }}
      />
    </div>
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
  // Someone else saved this user's access after it was read: the save was refused (409).
  const [stale, setStale] = useState(false);

  const load = () =>
    api<UserAccess>("GET", `/api/tenancy/access/${userId}`)
      .then((a) => {
        setAccess(a);
        setDraft(a.companies);
        setStale(false);
      })
      .catch((e) => setMessage(problemOf(e).message));

  useEffect(() => {
    void load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [userId]);

  // Company access is a grant: the server says whether this caller may change this user at all.
  const editable = can("tenancy.access.update") && access !== null && !access.isCaller && access.canEdit !== false;
  const entry = (companyId: string) => draft.find((d) => d.companyId === companyId);
  const canGiveAll = (companyId: string) => access?.options.find((o) => o.id === companyId)?.canGiveAllBranches !== false;
  const toggleCompany = (companyId: string, on: boolean) => {
    setSaved(false);
    const all = canGiveAll(companyId);
    setDraft((d) => (on ? [...d, { companyId, allBranches: all, branchIds: [] }] : d.filter((x) => x.companyId !== companyId)));
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
      // The version that was read: the server refuses the save if the access changed since.
      const result = await api<UserAccess>("PUT", `/api/tenancy/access/${userId}`, { companies: draft, version: access?.version });
      setAccess(result);
      setDraft(result.companies);
      setSaved(true);
      onSaved();
    } catch (error) {
      const problem = problemOf(error);
      setStale(error instanceof ApiError && error.status === 409);
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
            <button type="submit" className="button primary" disabled={busy} title={t("tenancy.common.saveHint")} aria-keyshortcuts="Control+S Control+Enter">
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
      {access && !access.isCaller && access.canEdit === false && access.readOnlyReason && (
        <div className="notice" data-testid="access-read-only">
          {t(access.readOnlyReason)}
        </div>
      )}
      {message && (
        <div className="alert" role="alert">
          {message}
          {stale && (
            <>
              {" "}
              {t("tenancy.access.changedElsewhere")}{" "}
              <button
                type="button"
                className="button"
                onClick={() => {
                  setMessage(null);
                  void load();
                }}
              >
                {t("tenancy.access.reload")}
              </button>
            </>
          )}
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
                  <label className="check" title={company.canGiveAllBranches === false ? t("tenancy.access.onlyOwnBranches") : undefined}>
                    <input
                      type="checkbox"
                      checked={current.allBranches}
                      disabled={company.canGiveAllBranches === false && !current.allBranches}
                      onChange={(e) => setAll(company.id, e.target.checked)}
                    />
                    <span>{t("tenancy.access.allBranches")}</span>
                  </label>
                  {company.canGiveAllBranches === false && <p className="muted">{t("tenancy.access.onlyOwnBranches")}</p>}
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

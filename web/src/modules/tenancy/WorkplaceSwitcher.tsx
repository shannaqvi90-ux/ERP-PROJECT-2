import { useCallback, useEffect, useId, useMemo, useRef, useState } from "react";
import { api } from "../../kernel/api";
import { useI18n } from "../../kernel/i18n";
import { useSession } from "../../kernel/session";
import { useShortcut } from "../../kernel/shortcuts";
import { companiesChanged, workplaceChanged, type Workplace } from "./types";
import { problemOf, useLocalName } from "./ui";

/** Up to this many companies, every other company has its own one-click button. */
const quickCompanies = 6;

type Choice = { companyId: string; branchId: string | null; label: string; detail: string; search: string };

/**
 * The working company and branch, in the top bar. Alt+C (or a click) opens the list of companies
 * and branches the user may work in; typing filters it, arrows move, Enter switches, Escape
 * closes. The choice is kept for the user's next sessions.
 */
export function WorkplaceSwitcher() {
  const { t } = useI18n();
  const { can } = useSession();
  const name = useLocalName();
  const [workplace, setWorkplace] = useState<Workplace | null>(null);
  const [open, setOpen] = useState(false);
  const [filter, setFilter] = useState("");
  const [active, setActive] = useState(0);
  const [message, setMessage] = useState<string | null>(null);
  const buttonRef = useRef<HTMLButtonElement>(null);
  const filterRef = useRef<HTMLInputElement>(null);
  const listId = useId();
  const canSwitch = can("tenancy.workplace.switch");

  useEffect(() => {
    api<Workplace>("GET", "/api/tenancy/workplace")
      .then(setWorkplace)
      .catch(() => setWorkplace(null));
  }, []);

  const company = workplace?.companies.find((c) => c.id === workplace.companyId) ?? null;
  const branch = company?.branches.find((b) => b.id === workplace?.branchId) ?? null;

  const choices = useMemo<Choice[]>(() => {
    const list: Choice[] = [];
    for (const c of workplace?.companies ?? []) {
      const companyName = name(c.legalNameEn, c.legalNameAr);
      if (c.branches.length === 0) {
        list.push({ companyId: c.id, branchId: null, label: c.code, detail: companyName, search: `${c.code} ${c.legalNameEn} ${c.legalNameAr}` });
      }
      for (const b of c.branches) {
        list.push({
          companyId: c.id,
          branchId: b.id,
          label: `${c.code} · ${b.code}`,
          detail: `${companyName} · ${name(b.nameEn, b.nameAr)}`,
          search: `${c.code} ${b.code} ${c.legalNameEn} ${c.legalNameAr} ${b.nameEn} ${b.nameAr}`,
        });
      }
    }
    const needle = filter.trim().toLowerCase();
    return needle ? list.filter((c) => c.search.toLowerCase().includes(needle)) : list;
  }, [workplace, filter, name]);

  const show = useCallback(() => {
    if (!canSwitch) return;
    setFilter("");
    setMessage(null);
    setOpen(true);
    // Offer what exists now (companies and branches may have been added since sign-in).
    api<Workplace>("GET", "/api/tenancy/workplace")
      .then(setWorkplace)
      .catch(() => undefined);
  }, [canSwitch]);

  useShortcut({
    id: "tenancy.workplace.switch",
    chord: "Alt+KeyC",
    labelKey: "tenancy.workplace.switch",
    groupKey: "tenancy.shortcut.group",
    enabled: canSwitch,
    run: show,
  });

  // A switch made elsewhere (the command palette) shows here at once.
  useEffect(() => {
    const reload = () => {
      api<Workplace>("GET", "/api/tenancy/workplace")
        .then(setWorkplace)
        .catch(() => undefined);
    };
    window.addEventListener(workplaceChanged, reload);
    window.addEventListener(companiesChanged, reload);
    return () => {
      window.removeEventListener(workplaceChanged, reload);
      window.removeEventListener(companiesChanged, reload);
    };
  }, []);

  useEffect(() => {
    if (!open) return;
    const current = choices.findIndex((c) => c.companyId === workplace?.companyId && c.branchId === workplace?.branchId);
    setActive(current >= 0 ? current : 0);
    filterRef.current?.focus();
    // Only when the list opens.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  const close = () => {
    setOpen(false);
    buttonRef.current?.focus();
  };

  const choose = async (choice: Choice) => {
    try {
      const result = await api<Workplace>("PUT", "/api/tenancy/workplace", { companyId: choice.companyId, branchId: choice.branchId });
      setWorkplace(result);
      setOpen(false);
      buttonRef.current?.focus();
      window.dispatchEvent(new CustomEvent(workplaceChanged, { detail: { companyId: result.companyId, branchId: result.branchId } }));
    } catch (error) {
      setMessage(problemOf(error).message);
    }
  };

  if (!workplace) return null;
  const label = company ? (branch ? `${company.code} · ${branch.code}` : company.code) : t("tenancy.workplace.none");
  const title = company
    ? t("tenancy.workplace.current", { company: name(company.legalNameEn, company.legalNameAr), branch: branch ? name(branch.nameEn, branch.nameAr) : "—" })
    : t("tenancy.workplace.none");

  if (!canSwitch || workplace.companies.length === 0) {
    return (
      <span className="workplace" title={title} data-testid="workplace">
        <span dir="ltr">{label}</span>
      </span>
    );
  }

  // With a handful of companies, the others are one click away beside the switcher.
  const others = workplace.companies.length <= quickCompanies ? workplace.companies.filter((c) => c.id !== workplace.companyId) : [];

  return (
    <div className="workplace-switcher">
      <button
        ref={buttonRef}
        type="button"
        className="button ghost workplace"
        aria-haspopup="listbox"
        aria-expanded={open}
        aria-keyshortcuts="Alt+C"
        title={`${title} (${t("tenancy.workplace.shortcut")})`}
        onClick={() => (open ? close() : show())}
        data-testid="workplace"
      >
        <span dir="ltr">{label}</span>
      </button>
      {others.length > 0 && (
        <span className="workplace-quick" role="group" aria-label={t("tenancy.workplace.quick")}>
          {others.map((c) => (
            <button
              key={c.id}
              type="button"
              className="button ghost workplace-chip"
              title={t("tenancy.workplace.switchTo", { company: name(c.legalNameEn, c.legalNameAr) })}
              onClick={() => void choose({ companyId: c.id, branchId: c.branches[0]?.id ?? null, label: c.code, detail: "", search: "" })}
              data-company={c.code}
            >
              <span dir="ltr">{c.code}</span>
            </button>
          ))}
        </span>
      )}
      {open && (
        <div className="workplace-popover" role="dialog" aria-label={t("tenancy.workplace.switch")}>
          <input
            ref={filterRef}
            type="search"
            value={filter}
            placeholder={t("tenancy.workplace.filter")}
            aria-label={t("tenancy.workplace.filter")}
            aria-controls={listId}
            aria-activedescendant={choices[active] ? `${listId}-${active}` : undefined}
            onChange={(e) => {
              setFilter(e.target.value);
              setActive(0);
            }}
            onKeyDown={(e) => {
              if (e.key === "ArrowDown") {
                e.preventDefault();
                setActive((i) => Math.min(choices.length - 1, i + 1));
              } else if (e.key === "ArrowUp") {
                e.preventDefault();
                setActive((i) => Math.max(0, i - 1));
              } else if (e.key === "Enter") {
                e.preventDefault();
                if (choices[active]) void choose(choices[active]);
              } else if (e.key === "Escape") {
                e.preventDefault();
                close();
              }
            }}
          />
          <ul id={listId} role="listbox" aria-label={t("tenancy.workplace.switch")}>
            {choices.map((c, i) => (
              <li
                key={`${c.companyId}:${c.branchId}`}
                id={`${listId}-${i}`}
                role="option"
                aria-selected={i === active}
                data-current={c.companyId === workplace.companyId && c.branchId === workplace.branchId ? "true" : undefined}
                onMouseDown={(e) => e.preventDefault()}
                onClick={() => void choose(c)}
              >
                <span className="option-code" dir="ltr">
                  {c.label}
                </span>
                <span className="option-detail">{c.detail}</span>
              </li>
            ))}
          </ul>
          {choices.length === 0 && <p className="muted">{t("tenancy.workplace.noMatch")}</p>}
          {message && (
            <div className="alert" role="alert">
              {message}
            </div>
          )}
        </div>
      )}
    </div>
  );
}

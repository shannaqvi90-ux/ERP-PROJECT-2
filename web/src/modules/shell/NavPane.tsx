import { forwardRef, useState, type KeyboardEvent } from "react";
import { Icon, type IconName } from "../../kernel/icons";
import { useI18n } from "../../kernel/i18n";
import { Link } from "../../kernel/router";
import { useSessionIfAny, type MenuItem } from "../../kernel/session";
import { goToLead, goToLetter, type GoToTarget } from "./goTo";
import { entryFor, groupMenu } from "./navigation";

/**
 * Module navigation, grouped (Settings, …). Only entries the user's roles grant are listed. One
 * Tab stop: Alt+M (or Tab) lands on the current entry, Up/Down move between entries, Home/End
 * jump to the first/last, Enter opens.
 */
function goToLetterOf(goTo: GoToTarget[], menuKey: string): string | null {
  const target = goTo.find((g) => g.menu === menuKey);
  return target ? goToLetter(target.code) : null;
}

function goToProps(target: GoToTarget | undefined) {
  if (!target) return {};
  const letter = goToLetter(target.code);
  return { "aria-keyshortcuts": `${goToLetter(goToLead)} ${letter}` };
}

/** The icon of a screen, by what its address names (drawn beside the name; the link's name is unchanged). */
export function iconFor(item: MenuItem): IconName {
  const p = item.path.toLowerCase();
  if (p.includes("/users") || p.includes("/roles") || p.includes("/access") || p.includes("/me")) return "users";
  if (p.includes("/companies") || p.includes("/branches") || p.includes("/tenant")) return "building";
  if (p.includes("/contacts")) return "user";
  if (p.includes("/rates") || p.includes("/currenc")) return "coin";
  if (p.includes("/audit")) return "history";
  if (p.includes("/jobs") || p.includes("/schedules")) return "clock";
  if (p.includes("/sequences") || p.includes("/numbering")) return "hash";
  if (p.includes("/reports")) return "chart";
  if (p.includes("/importexport") || p.includes("/imports") || p.includes("/exports")) return "upload";
  if (p.includes("/customfields")) return "sliders";
  if (p.includes("/attachments")) return "attach";
  return "list";
}

/** What this person set on this device for the pane: collapsed groups and pinned screens. Kept
 * under a per-user key, so signing out (kernel/deviceState) forgets it with the rest. */
type NavState = { collapsed: string[]; pinned: string[] };
const stateKey = (userId: string) => `erp.nav.${userId}`;
function readState(userId: string): NavState {
  try {
    const parsed = JSON.parse(localStorage.getItem(stateKey(userId)) ?? "{}") as Partial<NavState>;
    const strings = (v: unknown) => (Array.isArray(v) ? v.filter((x): x is string => typeof x === "string").slice(0, 50) : []);
    return { collapsed: strings(parsed.collapsed), pinned: strings(parsed.pinned) };
  } catch {
    return { collapsed: [], pinned: [] };
  }
}
function writeState(userId: string, state: NavState): void {
  try {
    localStorage.setItem(stateKey(userId), JSON.stringify(state));
  } catch {
    // Kept for this visit only.
  }
}

/** Hidden when closed, or when the user's roles open no screen at all. */
export const NavPane = forwardRef<HTMLElement, { menu: MenuItem[]; path: string; open: boolean; goTo?: GoToTarget[] }>(function NavPane({ menu, path, open, goTo = [] }, ref) {
  const { t, language } = useI18n();
  const sessionApi = useSessionIfAny();
  const userId = sessionApi?.state.status === "signedIn" ? sessionApi.state.session.user.id : "";
  const [state, setState] = useState<NavState>(() => readState(userId));
  const change = (next: NavState) => {
    setState(next);
    writeState(userId, next);
  };
  const pinned = state.pinned.map((k) => menu.find((m) => m.key === k)).filter((m): m is MenuItem => !!m);
  const rest = menu.filter((m) => !pinned.includes(m));
  const groups = [...(pinned.length ? [{ key: "_pinned", labelKey: "shell.nav.pinned", items: pinned }] : []), ...groupMenu(rest)];
  const current = entryFor(menu, path);
  const tabStop = current?.key ?? menu[0]?.key;

  const toggle = (key: string, collapse: boolean) =>
    change({ ...state, collapsed: collapse ? [...state.collapsed.filter((k) => k !== key), key] : state.collapsed.filter((k) => k !== key) });

  function onKeyDown(event: KeyboardEvent<HTMLElement>) {
    const link = (event.target as HTMLElement).closest?.<HTMLAnchorElement>("a[data-nav-entry]");
    // P pins or unpins the screen under the focus; Left collapses its group, Right opens it again
    // (the other way round in Arabic, where the pane is on the right).
    if (link && (event.key === "p" || event.key === "P") && !event.ctrlKey && !event.metaKey && !event.altKey) {
      event.preventDefault();
      const key = link.dataset.navEntry!;
      change({ ...state, pinned: state.pinned.includes(key) ? state.pinned.filter((k) => k !== key) : [...state.pinned, key] });
      return;
    }
    if ((event.key === "ArrowLeft" || event.key === "ArrowRight") && link) {
      const group = link.closest<HTMLElement>("[data-group]")?.dataset.group;
      if (group) {
        event.preventDefault();
        const collapse = (event.key === "ArrowLeft") !== (language === "ar");
        if (collapse) {
          toggle(group, true);
          link.closest("[data-group]")?.querySelector<HTMLElement>(".nav-group-toggle")?.focus();
        } else toggle(group, false);
      }
      return;
    }
    if (!["ArrowDown", "ArrowUp", "Home", "End"].includes(event.key)) return;
    const links = [...event.currentTarget.querySelectorAll<HTMLAnchorElement>("a[data-nav-entry]")];
    if (links.length === 0) return;
    const index = links.indexOf(document.activeElement as HTMLAnchorElement);
    let next = index;
    if (event.key === "ArrowDown") next = index < 0 ? 0 : Math.min(links.length - 1, index + 1);
    if (event.key === "ArrowUp") next = index < 0 ? 0 : Math.max(0, index - 1);
    if (event.key === "Home") next = 0;
    if (event.key === "End") next = links.length - 1;
    event.preventDefault();
    links[next]?.focus();
  }

  return (
    <nav ref={ref} id="navpane" className="navpane" aria-label={t("shell.navigation")} hidden={!open || menu.length === 0} onKeyDown={onKeyDown}>
      {groups.map((group) => {
        const gk = group.key ?? "_";
        const collapsed = state.collapsed.includes(gk);
        return (
          <div
            key={gk}
            className="nav-group"
            role="group"
            data-group={gk}
            data-collapsed={collapsed || undefined}
            aria-label={group.labelKey ? t(group.labelKey) : undefined}
            // The heading is drawn from this attribute (CSS ::before on the toggle), so the
            // navigation's text is its entries alone; screen readers get the name from aria-label.
          >
            {group.labelKey && (
              <button
                type="button"
                className="nav-group-toggle"
                tabIndex={-1}
                data-label={t(group.labelKey)}
                aria-expanded={!collapsed}
                aria-label={t(collapsed ? "shell.nav.expand" : "shell.nav.collapse", { group: t(group.labelKey) })}
                onClick={() => toggle(gk, !collapsed)}
              >
                <Icon name="collapse" size={12} className="nav-group-caret" />
              </button>
            )}
            <ul hidden={collapsed}>
              {group.items.map((item) => (
                // The go-to letter is drawn beside the link from data-go (CSS, on the item, not the
                // link): the link's text and accessible name stay the entry's name alone, and
                // aria-keyshortcuts tells assistive tools.
                <li key={item.key} data-go={goToLetterOf(goTo, item.key) ?? undefined}>
                  <Link
                    to={item.path}
                    data-nav-entry={item.key}
                    tabIndex={item.key === tabStop ? 0 : -1}
                    aria-current={current?.key === item.key ? "page" : undefined}
                    // G, then this letter, opens the entry from anywhere (goTo.tsx).
                    {...goToProps(goTo.find((g) => g.menu === item.key))}
                  >
                    <Icon name={iconFor(item)} size={16} className="nav-icon" />
                    {t(item.labelKey)}
                  </Link>
                </li>
              ))}
            </ul>
          </div>
        );
      })}
    </nav>
  );
});

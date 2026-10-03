import { forwardRef, type KeyboardEvent } from "react";
import { useI18n } from "../../kernel/i18n";
import { Link } from "../../kernel/router";
import type { MenuItem } from "../../kernel/session";
import { entryFor, groupMenu } from "./navigation";

/**
 * Module navigation, grouped (Settings, …). Only entries the user's roles grant are listed. One
 * Tab stop: Alt+M (or Tab) lands on the current entry, Up/Down move between entries, Home/End
 * jump to the first/last, Enter opens.
 */
/** Hidden when closed, or when the user's roles open no screen at all. */
export const NavPane = forwardRef<HTMLElement, { menu: MenuItem[]; path: string; open: boolean }>(function NavPane({ menu, path, open }, ref) {
  const { t } = useI18n();
  const groups = groupMenu(menu);
  const current = entryFor(menu, path);
  const tabStop = current?.key ?? menu[0]?.key;

  function onKeyDown(event: KeyboardEvent<HTMLElement>) {
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
      {groups.map((group) => (
        <div
          key={group.key ?? "_"}
          className="nav-group"
          role="group"
          aria-label={group.labelKey ? t(group.labelKey) : undefined}
          // The heading is drawn from this attribute (CSS ::before), so the navigation's text is
          // its entries alone; screen readers get the group's name from aria-label.
          data-label={group.labelKey ? t(group.labelKey) : undefined}
        >
          <ul>
            {group.items.map((item) => (
              <li key={item.key}>
                <Link
                  to={item.path}
                  data-nav-entry={item.key}
                  tabIndex={item.key === tabStop ? 0 : -1}
                  aria-current={current?.key === item.key ? "page" : undefined}
                >
                  {t(item.labelKey)}
                </Link>
              </li>
            ))}
          </ul>
        </div>
      ))}
    </nav>
  );
});

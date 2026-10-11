import { useEffect, useRef, useState } from "react";
import type { GoToKey } from "../../kernel/extensions";
import { useI18n } from "../../kernel/i18n";
import type { MenuItem } from "../../kernel/session";
import { Keys, codeOf, isEditable, isModalOpen } from "../../kernel/shortcuts";

/** The key that starts a go-to: G, then a letter (by position, so the same on an Arabic layout). */
export const goToLead = "KeyG";

/** How long the second key may wait after G. */
export const goToWindowMs = 2000;

/** A screen G opens with a second key. */
export type GoToTarget = { code: string; path: string; labelKey: string; menu: string | null };

/**
 * The screens a user can reach with G and a letter: Home (G, H), then each navigation entry the
 * user's roles list for which its module declared a key, in the navigation's order. A user whose
 * navigation lists nothing still has Home.
 */
export function goToTargets(menu: MenuItem[], keys: readonly GoToKey[]): GoToTarget[] {
  const home: GoToTarget = { code: "KeyH", path: "/", labelKey: "shell.nav.home", menu: null };
  const entries = menu.flatMap((item): GoToTarget[] => {
    const key = keys.find((k) => k.menu === item.key);
    return key ? [{ code: key.code, path: item.path, labelKey: item.labelKey, menu: item.key }] : [];
  });
  return [home, ...entries];
}

/** The letter printed for a go-to key ("U" for KeyU). */
export const goToLetter = (code: string): string => code.replace(/^Key/, "");

const plain = (event: KeyboardEvent) => !event.ctrlKey && !event.metaKey && !event.altKey && !event.shiftKey;

/**
 * G, then a letter, opens a screen (goToTargets) from anywhere a field does not have the focus and
 * no dialog is open, the way an expert moves without the navigation pane or the palette. G shows
 * the keys on offer (GoToHint) for two seconds; Escape, any other key or the time running out
 * leaves it. Both keys are taken before the screen's own keys see them, so a screen's plain
 * shortcut (n, /) is never run by the second key of a go-to. Returns whether G is waiting.
 */
export function useGoTo(targets: GoToTarget[], go: (target: GoToTarget) => void): boolean {
  const [armed, setArmed] = useState(false);
  const targetsRef = useRef(targets);
  const goRef = useRef(go);
  targetsRef.current = targets;
  goRef.current = go;

  useEffect(() => {
    let armedAt = 0;
    let timer = 0;
    const disarm = () => {
      armedAt = 0;
      window.clearTimeout(timer);
      setArmed(false);
    };
    const onKey = (event: KeyboardEvent) => {
      if (event.isComposing || event.repeat) return;
      const code = codeOf(event);
      if (armedAt !== 0) {
        const inTime = Date.now() - armedAt <= goToWindowMs;
        disarm();
        if (!inTime || !plain(event) || isModalOpen()) return;
        if (code === "Escape") {
          event.preventDefault();
          event.stopImmediatePropagation();
          return;
        }
        const target = targetsRef.current.find((t) => t.code === code);
        if (!target) return;
        event.preventDefault();
        event.stopImmediatePropagation();
        goRef.current(target);
        return;
      }
      if (code !== goToLead || !plain(event) || event.defaultPrevented || isEditable(event.target) || isModalOpen()) return;
      event.preventDefault();
      event.stopImmediatePropagation();
      armedAt = Date.now();
      setArmed(true);
      timer = window.setTimeout(disarm, goToWindowMs);
    };
    // Capture: before the screen's own key handlers (a list grid, a form) and the shortcut registry.
    window.addEventListener("keydown", onKey, true);
    return () => {
      window.removeEventListener("keydown", onKey, true);
      window.clearTimeout(timer);
    };
  }, []);

  return armed;
}

/** What G offers while it waits for the second key: each letter and the screen it opens. */
export function GoToHint({ targets }: { targets: GoToTarget[] }) {
  const { t } = useI18n();
  return (
    <div className="goto-hint" role="status" aria-live="polite" data-goto-hint="">
      <span className="goto-hint-title">{t("shell.goTo.title")}</span>
      <ul>
        {targets.map((target) => (
          <li key={target.code}>
            <Keys chord={target.code} /> {t(target.labelKey)}
          </li>
        ))}
      </ul>
    </div>
  );
}

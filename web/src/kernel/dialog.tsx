import { useEffect, useId, useRef, type KeyboardEvent, type ReactNode, type RefObject } from "react";

const focusable =
  'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

/**
 * A modal dialog that keeps the keyboard inside it: focus moves in when it opens (to
 * `initialFocus`, else the first control) and stays there, Tab and Shift+Tab cycle within it, Escape
 * closes it, and focus returns to whatever had it before when it closes.
 */
export function Dialog({
  title,
  onClose,
  children,
  className,
  initialFocus,
  hideTitle = false,
}: {
  title: string;
  onClose: () => void;
  children: ReactNode;
  className?: string;
  initialFocus?: RefObject<HTMLElement | null>;
  hideTitle?: boolean;
}) {
  const titleId = useId();
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const previous = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    const target = initialFocus?.current ?? ref.current?.querySelector<HTMLElement>(focusable) ?? ref.current;
    target?.focus();
    // The keyboard stays in the dialog while it is open: a screen behind it that takes the focus
    // late (one still opening as the dialog opened, say Alt+H then Ctrl+K at once) gives it back,
    // so what the person types next still lands in the dialog. Another modal dialog opened over
    // this one keeps its focus.
    let last: HTMLElement | null = ref.current?.contains(document.activeElement) ? (document.activeElement as HTMLElement) : null;
    const keepFocus = (event: FocusEvent) => {
      const dialog = ref.current;
      const into = event.target;
      if (!dialog || !(into instanceof HTMLElement)) return;
      if (dialog.contains(into)) {
        last = into;
        return;
      }
      if (into.closest('[role="dialog"][aria-modal="true"]')) return;
      (last && last.isConnected ? last : dialog).focus();
    };
    document.addEventListener("focusin", keepFocus);
    return () => {
      document.removeEventListener("focusin", keepFocus);
      if (previous && previous.isConnected) previous.focus();
    };
    // Runs once per opening; the dialog is mounted only while open.
  }, []);

  function onKeyDown(event: KeyboardEvent<HTMLDivElement>) {
    if (event.key === "Escape") {
      event.preventDefault();
      event.stopPropagation();
      onClose();
      return;
    }
    if (event.key !== "Tab" || !ref.current) return;
    const items = [...ref.current.querySelectorAll<HTMLElement>(focusable)].filter((el) => el.offsetParent !== null || el === document.activeElement);
    if (items.length === 0) return;
    const first = items[0]!;
    const last = items[items.length - 1]!;
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  }

  return (
    <div className="dialog-backdrop" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <div
        ref={ref}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        tabIndex={-1}
        className={className ? `dialog ${className}` : "dialog"}
        onKeyDown={onKeyDown}
      >
        <h2 id={titleId} className={hideTitle ? "visually-hidden" : "dialog-title"}>
          {title}
        </h2>
        {children}
      </div>
    </div>
  );
}
